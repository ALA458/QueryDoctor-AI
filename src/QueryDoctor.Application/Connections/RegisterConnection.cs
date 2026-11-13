using QueryDoctor.Application.Common.Security;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Connections;

/// <summary>
/// Datos de alta tal como llegan del cliente. Los enumerados vienen como texto para validarlos aquí y
/// devolver errores por campo (contrato de la pantalla de conexiones, S1-15).
/// </summary>
public sealed record RegisterConnectionCommand(
    string? Platform,
    string? Server,
    string? DatabaseName,
    string? AuthenticationMethod,
    string? Encryption,
    string? Username,
    string? Password)
{
    public override string ToString() =>
        $"RegisterConnectionCommand {{ Platform = {Platform}, Server = {Server}, DatabaseName = {DatabaseName}, Username = {Username}, Password = ******** }}";
}

/// <summary>Resultado del alta. La API lo traduce a 202, 400 o 409.</summary>
public abstract record RegisterConnectionResult
{
    private RegisterConnectionResult()
    {
    }

    /// <summary>Registrada (o reemplazada, D3) y con el sondeo encolado.</summary>
    public sealed record Accepted(Guid ConnectionId, bool ReplacedPrevious) : RegisterConnectionResult;

    /// <summary>Errores por campo, con las claves del cuerpo JSON.</summary>
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : RegisterConnectionResult;

    /// <summary>La base ya está registrada en un estado que no se reemplaza (Active, Pending, Disabled).</summary>
    public sealed record Conflict(Guid ExistingConnectionId, ConnectionStatus ExistingStatus) : RegisterConnectionResult;
}

/// <summary>
/// Caso de uso de alta de conexión (RF-CFG-01, RF-CFG-02, CA-HU01.1–01.3). Valida y normaliza, cifra las
/// credenciales, guarda la conexión en Pending y encola probe_connection con solo el identificador.
/// La API nunca abre la conexión: el sondeo corre en el Worker (invariante 2).
/// </summary>
public sealed class RegisterConnection
{
    /// <summary>Longitud máxima de un login de SQL Server (sysname).</summary>
    public const int MaxUserNameLength = 128;

    /// <summary>Longitud máxima de la contraseña de un login de SQL Server.</summary>
    public const int MaxPasswordLength = 128;

    private readonly IConnectionRepository _repository;
    private readonly IProbeJobScheduler _scheduler;
    private readonly IFieldEncryptor _encryptor;
    private readonly TimeProvider _clock;

    public RegisterConnection(
        IConnectionRepository repository,
        IProbeJobScheduler scheduler,
        IFieldEncryptor encryptor,
        TimeProvider clock)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<RegisterConnectionResult> ExecuteAsync(
        RegisterConnectionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var input = Validate(command, errors);
        if (input is null)
        {
            return new RegisterConnectionResult.Invalid(errors);
        }

        var existing = await _repository.FindByAddressAsync(input.Server, input.DatabaseName, cancellationToken);
        if (existing is not null && existing.Status is not (ConnectionStatus.Failed or ConnectionStatus.Rejected))
        {
            return new RegisterConnectionResult.Conflict(existing.Id, existing.Status);
        }

        var now = _clock.GetUtcNow();
        var encryptedCredentials = ConnectionCredentialsCodec.Encrypt(input.Credentials, _encryptor);
        Connection connection;
        bool replaced;

        if (existing is null)
        {
            connection = new Connection
            {
                Id = Guid.NewGuid(),
                Server = input.Server,
                CreatedAt = now,
            };
            Apply(connection, input, encryptedCredentials, now);
            _repository.Add(connection);
            replaced = false;
        }
        else
        {
            // D3: un registro fallido o rechazado se reemplaza con las credenciales nuevas y se vuelve a sondear.
            connection = existing;
            Apply(connection, input, encryptedCredentials, now);
            replaced = true;
        }

        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateConnectionException)
        {
            // Carrera: otra solicitud registró la misma base entre la búsqueda y el guardado; la UK decide.
            var winner = await _repository.FindByAddressAsync(input.Server, input.DatabaseName, cancellationToken);
            return new RegisterConnectionResult.Conflict(winner?.Id ?? Guid.Empty, winner?.Status ?? ConnectionStatus.Pending);
        }

        try
        {
            _scheduler.EnqueueProbe(connection.Id);
        }
        catch (Exception)
        {
            // RF-CFG-02: una conexión nunca queda en Pending para siempre. Si no se pudo encolar, queda Failed.
            connection.Status = ConnectionStatus.Failed;
            connection.ProbeFailureCause = ProbeFailureCause.Unknown;
            connection.LastProbedAt = _clock.GetUtcNow();
            connection.UpdatedAt = connection.LastProbedAt.Value;
            await _repository.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        return new RegisterConnectionResult.Accepted(connection.Id, replaced);
    }

    private static void Apply(Connection connection, ValidInput input, string encryptedCredentials, DateTimeOffset now)
    {
        connection.Platform = input.Platform;
        connection.DatabaseName = input.DatabaseName;
        connection.AuthenticationMethod = input.AuthenticationMethod;
        connection.Encryption = input.Encryption;
        connection.EncryptedCredentials = encryptedCredentials;
        connection.Status = ConnectionStatus.Pending;
        connection.ProbeFailureCause = null;
        connection.LastProbedAt = null;
        connection.UpdatedAt = now;
    }

    private static ValidInput? Validate(RegisterConnectionCommand command, Dictionary<string, string[]> errors)
    {
        var platform = ParseEnum<ConnectionPlatform>(command.Platform, "platform", required: true, errors);
        var authentication = ParseEnum<AuthenticationMethod>(
            command.AuthenticationMethod, "authenticationMethod", required: true, errors);

        // Sin valor se usa Mandatory; nunca existe un modo sin cifrado (RNF-SEG-03).
        var encryption = string.IsNullOrWhiteSpace(command.Encryption)
            ? ConnectionEncryption.Mandatory
            : ParseEnum<ConnectionEncryption>(command.Encryption, "encryption", required: true, errors);

        if (!ConnectionAddress.TryNormalizeServer(command.Server, out var server, out var serverError))
        {
            errors["server"] = [ServerMessage(serverError)];
        }

        if (!ConnectionAddress.TryNormalizeDatabaseName(command.DatabaseName, out var databaseName, out var databaseError))
        {
            errors["databaseName"] = [databaseError == ConnectionAddressError.TooLong
                ? $"El nombre de la base admite como máximo {ConnectionAddress.DatabaseNameMaxLength} caracteres."
                : "Indica el nombre de la base de datos."];
        }

        var userName = command.Username?.Trim() ?? string.Empty;
        if (userName.Length == 0)
        {
            errors["username"] = ["Indica el usuario de la cuenta de solo lectura."];
        }
        else if (userName.Length > MaxUserNameLength)
        {
            errors["username"] = [$"El usuario admite como máximo {MaxUserNameLength} caracteres."];
        }

        // La contraseña no se recorta: los espacios pueden ser parte de ella.
        var password = command.Password ?? string.Empty;
        if (password.Length == 0)
        {
            errors["password"] = ["Indica la contraseña."];
        }
        else if (password.Length > MaxPasswordLength)
        {
            errors["password"] = [$"La contraseña admite como máximo {MaxPasswordLength} caracteres."];
        }

        if (errors.Count > 0)
        {
            return null;
        }

        return new ValidInput(
            platform!.Value,
            server,
            databaseName,
            authentication!.Value,
            encryption!.Value,
            new ConnectionCredentials(userName, password));
    }

    private static TEnum? ParseEnum<TEnum>(string? value, string field, bool required, Dictionary<string, string[]> errors)
        where TEnum : struct, Enum
    {
        // Solo nombres: "1" o "99" no se aceptan aunque Enum.TryParse los convierta.
        if (!string.IsNullOrWhiteSpace(value)
            && !value.Trim().All(char.IsDigit)
            && Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        if (required || !string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"Valor no válido. Usa uno de: {string.Join(", ", Enum.GetNames<TEnum>())}."];
        }

        return null;
    }

    private static string ServerMessage(ConnectionAddressError error) => error switch
    {
        ConnectionAddressError.ForbiddenProtocol =>
            "Los prefijos np:, lpc: y admin: no se permiten: la plataforma solo se conecta por TCP y sin la conexión de administrador.",
        ConnectionAddressError.InvalidPort => "El puerto debe ser un número entre 1 y 65535.",
        ConnectionAddressError.TooLong => $"El servidor admite como máximo {ConnectionAddress.ServerMaxLength} caracteres.",
        _ => "Indica el servidor.",
    };

    private sealed record ValidInput(
        ConnectionPlatform Platform,
        string Server,
        string DatabaseName,
        AuthenticationMethod AuthenticationMethod,
        ConnectionEncryption Encryption,
        ConnectionCredentials Credentials);
}
