using System.Text.Json;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Connections;

/// <summary>Resultado del sondeo, para que el trabajo lo registre (solo id y código, RNF-SEG-09).</summary>
/// <param name="ConnectionId">Conexión sondeada.</param>
/// <param name="Ran">false si no había nada que sondear (no existe o ya no está en Pending).</param>
/// <param name="Status">Estado final de la conexión.</param>
/// <param name="FailureCause">Código del fallo, o null si quedó Active.</param>
public sealed record ProbeOutcome(Guid ConnectionId, bool Ran, ConnectionStatus? Status, ProbeFailureCause? FailureCause);

/// <summary>
/// Caso de uso del trabajo probe_connection (RF-CFG-02 a 04, RF-CFG-07, RNF-COM-04; CA-HU01.1–01.3, CA-HU02.1,
/// CA-HU02.3). Corre en el Worker. Lee la conexión, descifra las credenciales, sondea con un tiempo límite y deja
/// la conexión en Active, Failed o Rejected; nunca en Pending (RF-CFG-02). Solo lee: no modifica Query Store.
/// </summary>
public sealed class ProbeConnection
{
    private readonly IConnectionRepository _repository;
    private readonly ICapabilityProbe _probe;
    private readonly IFieldEncryptor _encryptor;
    private readonly TimeProvider _clock;
    private readonly ProbeOptions _options;

    public ProbeConnection(
        IConnectionRepository repository,
        ICapabilityProbe probe,
        IFieldEncryptor encryptor,
        TimeProvider clock,
        ProbeOptions options)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (_options.TimeoutSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Probe:TimeoutSeconds debe ser al menos 1.");
        }
    }

    public async Task<ProbeOutcome> ExecuteAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var connection = await _repository.GetAsync(connectionId, cancellationToken);

        // Idempotente: Hangfire puede volver a ejecutar el trabajo; solo se sondea lo que sigue pendiente.
        if (connection is null || connection.Status != ConnectionStatus.Pending)
        {
            return new ProbeOutcome(connectionId, false, connection?.Status, connection?.ProbeFailureCause);
        }

        ProbeResult result;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds), _clock))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        {
            try
            {
                result = await RunAsync(connection, linked.Token);
            }
            catch (Exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // RF-CFG-02: superó Probe:TimeoutSeconds, sea cual sea la excepción con la que cortó SqlClient.
                result = ProbeResult.Fail(ProbeFailureCause.Timeout);
            }
            catch (TelemetrySourceException ex)
            {
                result = ProbeResult.Fail(MapKind(ex.Kind));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Nada de catch silencioso: se registra como Unknown y la conexión no se activa.
                result = ProbeResult.Fail(ProbeFailureCause.Unknown);
            }
        }

        var now = _clock.GetUtcNow();
        connection.LastProbedAt = now;
        connection.UpdatedAt = now;

        if (result.FailureCause is { } cause)
        {
            connection.ProbeFailureCause = cause;

            if (cause == ProbeFailureCause.UnsupportedVersion)
            {
                // CA-HU01.3: registro rechazado; sus credenciales no se conservan (RF-CFG-06, mínimo de datos).
                connection.Status = ConnectionStatus.Rejected;
                connection.EncryptedCredentials = null;
            }
            else
            {
                connection.Status = ConnectionStatus.Failed;
            }
        }
        else
        {
            connection.Status = ConnectionStatus.Active;
            connection.ProbeFailureCause = null;
            await SaveProfileAsync(connection.Id, result.Profile!, now, cancellationToken);
        }

        // El estado final se guarda aunque el trabajo se esté cancelando: nunca queda Pending.
        await _repository.SaveChangesAsync(CancellationToken.None);
        return new ProbeOutcome(connection.Id, true, connection.Status, connection.ProbeFailureCause);
    }

    private async Task<ProbeResult> RunAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.EncryptedCredentials))
        {
            return ProbeResult.Fail(ProbeFailureCause.InvalidCredentials);
        }

        var credentials = ConnectionCredentialsCodec.Decrypt(connection.EncryptedCredentials, _encryptor);
        var target = BuildTarget(connection, credentials);

        // 1. Versión y plataforma: si no está soportada no se sigue (en < 2016 Query Store no existe).
        var engine = await _probe.GetEngineAsync(target, cancellationToken);
        var assessment = SupportMatrix.Assess(connection.Platform, engine.ProductVersion, engine.EngineEdition);
        if (assessment.FailureCause is { } versionCause)
        {
            return ProbeResult.Fail(versionCause);
        }

        // 2. Permiso mínimo de la matriz.
        var permissions = await _probe.GetDatabasePermissionsAsync(target, cancellationToken);
        if (!SupportMatrix.HasMinimumPermission(assessment, permissions))
        {
            return ProbeResult.Fail(ProbeFailureCause.MissingPermission);
        }

        // 3. Estado de Query Store (solo lectura de sys.database_query_store_options, CA-HU02.3).
        QueryStoreOptions? options;
        try
        {
            options = await _probe.GetQueryStoreOptionsAsync(target, cancellationToken);
        }
        catch (TelemetrySourceException ex) when (ex.Kind is TelemetryFailureKind.PermissionDenied)
        {
            // Auditoría B4: solo la falta de permiso sobre la vista es "Query Store no disponible". Un Unknown sigue
            // como Unknown (lo mapea el catch exterior) para no esconder un error inesperado tras un diagnóstico válido.
            return ProbeResult.Fail(ProbeFailureCause.QueryStoreUnavailable);
        }

        if (options is null)
        {
            return ProbeResult.Fail(ProbeFailureCause.QueryStoreUnavailable);
        }

        // 4. Columnas disponibles → funciones omitidas (RNF-COM-04).
        var columns = await _probe.GetRuntimeStatsColumnsAsync(target, cancellationToken);
        var omitted = SupportMatrix.OmittedFeatures(columns);

        return ProbeResult.Ok(new ProfileData(engine, assessment.SupportLevel, options, omitted));
    }

    // Excepción de TLS SOLO para el laboratorio local (invariante 3): lista explícita, vacía por defecto.
    private TargetDatabaseConnection BuildTarget(Connection connection, ConnectionCredentials credentials) =>
        TargetConnectionFactory.Build(
            connection,
            credentials,
            _options.LabTrustedCertificateServers,
            ConnectTimeoutSeconds(_options.TimeoutSeconds));

    /// <summary>
    /// 2/3 del límite total para abrir la conexión (mínimo 1 s). Ante un error de red SqlClient reintenta hasta agotar
    /// su Connect Timeout; con margen, el fallo llega con su causa real (ServerUnreachable) y Timeout queda para un
    /// sondeo que de verdad superó Probe:TimeoutSeconds (hallazgo de laboratorio de P8).
    /// </summary>
    private static int ConnectTimeoutSeconds(int probeTimeoutSeconds) => Math.Max(1, probeTimeoutSeconds * 2 / 3);

    private async Task SaveProfileAsync(Guid connectionId, ProfileData data, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var profile = await _repository.GetProfileAsync(connectionId, cancellationToken);
        if (profile is null)
        {
            profile = new CapabilityProfile { Id = Guid.NewGuid(), ConnectionId = connectionId };
            _repository.AddProfile(profile);
        }

        profile.EngineVersion = data.Engine.ProductVersion ?? string.Empty;
        profile.Edition = data.Engine.Edition;
        profile.SupportLevel = data.SupportLevel;

        // Si pasó el paso 2 tiene VIEW DATABASE STATE o su equivalente de la matriz.
        profile.HasViewDatabaseState = true;

        // RF-CFG-05 se evalúa en el Sprint 2: false aquí significa "no evaluado"; la API lo expone como null.
        profile.HasExcessivePermissions = false;

        profile.QueryStoreActualState = data.QueryStore.ActualState;
        profile.QueryStoreDesiredState = data.QueryStore.DesiredState;
        profile.QueryStoreReadOnlyReason = data.QueryStore.ReadOnlyReason?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        profile.IntervalLengthMinutes = data.QueryStore.IntervalLengthMinutes;
        profile.CaptureMode = data.QueryStore.CaptureMode;
        profile.RetentionDays = data.QueryStore.RetentionDays;
        profile.OmittedFeaturesJson = JsonSerializer.Serialize(data.Omitted.Select(f => f.ToString()).ToArray());
        profile.ProbedAt = now;
    }

    private static ProbeFailureCause MapKind(TelemetryFailureKind kind) => kind switch
    {
        TelemetryFailureKind.InvalidCredentials => ProbeFailureCause.InvalidCredentials,
        TelemetryFailureKind.ServerUnreachable => ProbeFailureCause.ServerUnreachable,
        TelemetryFailureKind.TlsCertificateInvalid => ProbeFailureCause.TlsCertificateInvalid,
        TelemetryFailureKind.PermissionDenied => ProbeFailureCause.MissingPermission,
        TelemetryFailureKind.Timeout => ProbeFailureCause.Timeout,
        _ => ProbeFailureCause.Unknown,
    };

    private sealed record ProfileData(
        EngineInfo Engine,
        SupportLevel SupportLevel,
        QueryStoreOptions QueryStore,
        IReadOnlyList<OmittedFeature> Omitted);

    private sealed record ProbeResult(ProbeFailureCause? FailureCause, ProfileData? Profile)
    {
        public static ProbeResult Fail(ProbeFailureCause cause) => new(cause, null);

        public static ProbeResult Ok(ProfileData profile) => new(null, profile);
    }
}
