using Microsoft.Data.SqlClient;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Connections;
using QueryDoctor.Infrastructure.Telemetry;
using QueryDoctor.UnitTests.Connections;

namespace QueryDoctor.LabTests;

/// <summary>
/// El sondeo real del Worker (ProbeConnection → CapabilityProbe → QueryStoreGateway → SqlClient) contra el laboratorio.
/// Solo el repositorio y el cifrado son dobles: la persistencia ya se prueba en IntegrationTests con PostgreSQL.
/// </summary>
internal sealed class LabProbeHarness
{
    public InMemoryStatementLogWriter StatementLog { get; } = new();

    public InMemoryConnectionRepository Repository { get; } = new();

    private readonly FakeEncryptor _encryptor = new();

    public Connection AddPending(string database, string userName, string password, string? server = null)
    {
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = server ?? LabEnvironment.RequiredServer,
            DatabaseName = database,
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = ConnectionCredentialsCodec.Encrypt(new ConnectionCredentials(userName, password), _encryptor),
            Status = ConnectionStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };

        Repository.Stored.Add(connection);
        return connection;
    }

    /// <param name="connectionId">Conexión en Pending.</param>
    /// <param name="trustLabCertificate">
    /// true = excepción de TLS del laboratorio para este servidor (invariante 3, solo aquí); false = validación normal.
    /// </param>
    /// <param name="decorate">Para sustituir una sola respuesta del sondeo real (versión no soportada).</param>
    public Task<ProbeOutcome> ProbeAsync(
        Guid connectionId,
        bool trustLabCertificate = true,
        Func<ICapabilityProbe, ICapabilityProbe>? decorate = null)
    {
        ICapabilityProbe probe = new CapabilityProbe(new QueryStoreGateway(StatementLog));
        if (decorate is not null)
        {
            probe = decorate(probe);
        }

        var options = new ProbeOptions
        {
            TimeoutSeconds = 15,
            LabTrustedCertificateServers = trustLabCertificate ? [LabEnvironment.RequiredServer] : [],
        };

        return new ProbeConnection(Repository, probe, _encryptor, TimeProvider.System, options).ExecuteAsync(connectionId);
    }

    /// <summary>
    /// Foto de la configuración de Query Store, leída por la prueba (no por la plataforma) para comprobar que el
    /// sondeo no la cambió (CA-HU02.3). Es un SELECT, como lector, con la excepción de TLS del laboratorio.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> QueryStoreConfigurationAsync(string database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = LabEnvironment.RequiredServer,
            InitialCatalog = database,
            UserID = LabEnvironment.Reader,
            Password = LabEnvironment.ReaderPassword(),
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = true,
            ApplicationName = "QueryDoctor-AI LabTests",
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT desired_state_desc, actual_state_desc, readonly_reason, interval_length_minutes, " +
            "flush_interval_seconds, max_storage_size_mb, query_capture_mode_desc, size_based_cleanup_mode_desc, " +
            "stale_query_threshold_days, max_plans_per_query, wait_stats_capture_mode_desc " +
            "FROM sys.database_query_store_options;";

        await using var reader = await command.ExecuteReaderAsync();
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        if (await reader.ReadAsync())
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                snapshot[reader.GetName(i)] = reader.IsDBNull(i)
                    ? "NULL"
                    : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "NULL";
            }
        }

        return snapshot;
    }
}

/// <summary>Sondeo real con una única respuesta sustituida: la versión del motor (CA-HU01.3 sin un SQL Server 2014).</summary>
internal sealed class EngineVersionOverride(ICapabilityProbe inner, string productVersion) : ICapabilityProbe
{
    public async Task<EngineInfo> GetEngineAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
        (await inner.GetEngineAsync(target, cancellationToken)) with { ProductVersion = productVersion };

    public Task<IReadOnlyList<string>> GetDatabasePermissionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
        inner.GetDatabasePermissionsAsync(target, cancellationToken);

    public Task<QueryStoreOptions?> GetQueryStoreOptionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
        inner.GetQueryStoreOptionsAsync(target, cancellationToken);

    public Task<IReadOnlyList<string>> GetRuntimeStatsColumnsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
        inner.GetRuntimeStatsColumnsAsync(target, cancellationToken);
}
