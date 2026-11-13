using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Ingestion;

/// <summary>
/// Resultado de una ingesta, para que el trabajo lo registre (solo ids, códigos y conteos; RNF-SEG-09).
/// </summary>
/// <param name="ConnectionId">Conexión.</param>
/// <param name="Status">Estado final; null si la conexión no existe.</param>
/// <param name="Cause">Causa si se omitió o falló.</param>
/// <param name="RowsUpserted">Filas insertadas o actualizadas.</param>
/// <param name="WatermarkBefore">Marca de agua al empezar.</param>
/// <param name="WatermarkAfter">Marca de agua al terminar (igual a la anterior si no se confirmó nada).</param>
/// <param name="OmittedFeatures">Funciones omitidas por el perfil de capacidades (RNF-COM-04).</param>
/// <param name="DiscardedRows">Filas con tipo de ejecución desconocido.</param>
/// <param name="ShouldRetry">Fallo transitorio: el trabajo debe dejar que Hangfire reintente (RNF-FIA-01).</param>
public sealed record IngestionOutcome(
    Guid ConnectionId,
    IngestionStatus? Status,
    IngestionCause? Cause,
    int RowsUpserted,
    long? WatermarkBefore,
    long? WatermarkAfter,
    IReadOnlyList<OmittedFeature> OmittedFeatures,
    int DiscardedRows,
    bool ShouldRetry)
{
    internal static IngestionOutcome Stopped(Guid connectionId, IngestionStatus? status, IngestionCause? cause, long? watermark, bool retry = false) =>
        new(connectionId, status, cause, 0, watermark, watermark, [], 0, retry);
}

/// <summary>
/// Caso de uso del trabajo ingest_connection (S1-11; RF-ING-01, 02, 04 a 09; RNF-FIA-01; CA-HU03.1–03.4, CA-HU02.2).
/// Corre en el Worker con el bloqueo de la conexión tomado. Lee de Query Store solo con SELECT de la lista blanca,
/// agrega, cifra texto y planes (RNF-SEG-14) y confirma upsert + marca de agua + ejecución en una transacción.
/// </summary>
/// <remarks>
/// El texto SQL y el plan se guardan completos y cifrados; el enmascarado de literales ocurre al construir el contexto
/// que sale hacia la IA (S2-04/S2-05, invariante 5, D2 de S1-11). Esta clase nunca registra SQL ni texto del motor.
/// </remarks>
public sealed class IngestConnection
{
    private readonly IIngestionStore _store;
    private readonly IQueryStoreReader _reader;
    private readonly IIngestionLock _lock;
    private readonly IFieldEncryptor _encryptor;
    private readonly TimeProvider _clock;
    private readonly IngestionOptions _options;
    private readonly ProbeOptions _probeOptions;

    public IngestConnection(
        IIngestionStore store,
        IQueryStoreReader reader,
        IIngestionLock ingestionLock,
        IFieldEncryptor encryptor,
        TimeProvider clock,
        IngestionOptions options,
        ProbeOptions probeOptions)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _lock = ingestionLock ?? throw new ArgumentNullException(nameof(ingestionLock));
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _probeOptions = probeOptions ?? throw new ArgumentNullException(nameof(probeOptions));
        _options.Validate();
    }

    public async Task<IngestionOutcome> ExecuteAsync(Guid connectionId, IngestionTrigger trigger, CancellationToken cancellationToken = default)
    {
        await using var lease = await _lock.TryAcquireAsync(connectionId, cancellationToken);

        var connection = await _store.GetConnectionAsync(connectionId, cancellationToken);
        if (connection is null)
        {
            return IngestionOutcome.Stopped(connectionId, null, null, null);
        }

        var watermark = connection.LastConsolidatedIntervalId;

        if (lease is null)
        {
            await _store.RecordSkippedAsync(connectionId, trigger, IngestionCause.AlreadyRunning, _clock.GetUtcNow(), cancellationToken);
            return IngestionOutcome.Stopped(connectionId, IngestionStatus.Skipped, IngestionCause.AlreadyRunning, watermark);
        }

        if (connection.Status != ConnectionStatus.Active)
        {
            await _store.RecordSkippedAsync(connectionId, trigger, IngestionCause.ConnectionNotActive, _clock.GetUtcNow(), cancellationToken);
            return IngestionOutcome.Stopped(connectionId, IngestionStatus.Skipped, IngestionCause.ConnectionNotActive, watermark);
        }

        // Con el bloqueo tomado, cualquier Running de esta conexión es de un proceso que murió.
        await _store.CloseInterruptedRunsAsync(connectionId, _clock.GetUtcNow(), cancellationToken);
        var runId = await _store.StartRunAsync(connectionId, trigger, _clock.GetUtcNow(), cancellationToken);

        IngestionOutcome outcome;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds), _clock))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        {
            try
            {
                outcome = await RunAsync(connection, runId, linked.Token);
            }
            catch (Exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                outcome = Failed(connectionId, IngestionCause.Timeout, watermark);
            }
            catch (TelemetrySourceException ex)
            {
                outcome = Failed(connectionId, MapKind(ex.Kind), watermark);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Persistencia, descifrado o un dato inesperado. Nada de catch silencioso: Failed/Unknown y se reintenta.
                outcome = Failed(connectionId, IngestionCause.Unknown, watermark);
            }
        }

        if (outcome.Status is IngestionStatus.Skipped or IngestionStatus.Failed)
        {
            // Se guarda aunque el trabajo se esté cancelando: la ejecución no queda en Running.
            await _store.FinishRunAsync(runId, outcome.Status.Value, outcome.Cause!.Value, _clock.GetUtcNow(), CancellationToken.None);
        }

        return outcome;
    }

    private async Task<IngestionOutcome> RunAsync(Connection connection, Guid runId, CancellationToken cancellationToken)
    {
        var watermark = connection.LastConsolidatedIntervalId;

        if (string.IsNullOrWhiteSpace(connection.EncryptedCredentials))
        {
            return Failed(connection.Id, IngestionCause.InvalidCredentials, watermark, retry: false);
        }

        var credentials = ConnectionCredentialsCodec.Decrypt(connection.EncryptedCredentials, _encryptor);
        var target = TargetConnectionFactory.Build(
            connection, credentials, _probeOptions.LabTrustedCertificateServers, _options.StatementTimeoutSeconds);

        // RF-ING-08 / CA-HU02.2: el estado se lee EN VIVO (el perfil puede tener horas) y se decide con CanIngest.
        // Se omite sin tocar nada de lo ya almacenado.
        var queryStore = await _reader.GetOptionsAsync(target, cancellationToken);
        if (queryStore is null || !QueryStoreState.CanIngest(queryStore.ActualState))
        {
            var cause = string.Equals(queryStore?.ActualState, "OFF", StringComparison.OrdinalIgnoreCase)
                ? IngestionCause.QueryStoreDisabled
                : IngestionCause.QueryStoreNotReadable;
            return IngestionOutcome.Stopped(connection.Id, IngestionStatus.Skipped, cause, watermark);
        }

        // RNF-COM-04: sin las columnas de TempDB y bytes de log se usa la sentencia que no las nombra.
        var profile = await _store.GetProfileAsync(connection.Id, cancellationToken);
        var omitted = OmittedFeatures(profile);
        var includeTempDbAndLog = !omitted.Contains(OmittedFeature.TempDbAndLogBytes);

        var now = _clock.GetUtcNow();
        var request = new QueryStoreReadRequest(
            WatermarkCalculator.AfterIntervalId(watermark),
            watermark is null ? now.AddHours(-_options.InitialLookbackHours) : DateTimeOffset.MinValue,
            _options.TopN,
            includeTempDbAndLog);

        var read = await _reader.ReadStatsAsync(target, request, cancellationToken);

        var aggregated = RuntimeStatAggregator.Aggregate(read.Rows.Select(r => r.Stat));
        var newWatermark = WatermarkCalculator.Next(watermark, aggregated);

        var queries = read.Rows
            .GroupBy(r => r.QueryStoreQueryId)
            .Select(g => g.First())
            .OrderBy(r => r.QueryStoreQueryId)
            .Select(r => new IngestedQuery(r.QueryStoreQueryId, r.QueryStoreTextId, r.QueryHash, r.OriginContext))
            .ToList();

        var planRows = read.Rows
            .GroupBy(r => r.Stat.QueryStorePlanId)
            .Select(g => g.First())
            .OrderBy(r => r.Stat.QueryStorePlanId)
            .ToList();

        var textIds = queries.Select(q => q.QueryStoreTextId).Distinct().ToList();
        var known = await _store.GetKnownIdsAsync(connection.Id, textIds, planRows.Select(p => p.Stat.QueryStorePlanId).ToList(), cancellationToken);

        // Textos y planes solo se leen y cifran si son nuevos: en Query Store no cambian, y volver a cifrarlos (nonce
        // aleatorio) cambiaría el valor guardado en cada ingesta (CA-HU03.2: mismos valores).
        var texts = new List<IngestedText>();
        foreach (var textId in textIds.Where(id => !known.TextIds.Contains(id)))
        {
            var text = await _reader.GetQueryTextAsync(target, textId, cancellationToken);
            texts.Add(new IngestedText(textId, text is { Found: true, Content: { } sql } ? _encryptor.Encrypt(sql) : null));
        }

        var plans = new List<IngestedPlan>();
        foreach (var row in planRows.Where(p => !known.PlanIds.Contains(p.Stat.QueryStorePlanId)))
        {
            var xml = await _reader.GetPlanXmlAsync(target, row.Stat.QueryStorePlanId, cancellationToken);

            // RF-ING-09 / CA-HU03.3: un plan irrecuperable queda NotAnalyzable con su causa y la ingesta sigue.
            var classification = PlanXmlClassifier.Classify(xml.Found, xml.Content);
            var encrypted = xml is { Found: true, Content: { Length: > 0 } content } ? _encryptor.Encrypt(content) : null;

            plans.Add(new IngestedPlan(
                row.Stat.QueryStorePlanId,
                row.QueryStoreQueryId,
                row.QueryPlanHash,
                row.EngineVersion,
                encrypted,
                classification));
        }

        var rows = await _store.ApplyAsync(
            new IngestionBatch(connection.Id, runId, watermark, newWatermark, texts, queries, plans, aggregated, _clock.GetUtcNow()),
            cancellationToken);

        return new IngestionOutcome(
            connection.Id, IngestionStatus.Succeeded, null, rows, watermark, newWatermark, omitted, read.DiscardedRows, false);
    }

    private static List<OmittedFeature> OmittedFeatures(CapabilityProfile? profile)
    {
        // Sin perfil (no debería pasar en una conexión Active) se asume lo mínimo: la sentencia de 2016 nunca falla
        // por columnas inexistentes.
        if (profile?.OmittedFeaturesJson is not { Length: > 0 } json)
        {
            return profile is null ? [OmittedFeature.TempDbAndLogBytes] : [];
        }

        var codes = System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
        return codes
            .Select(c => Enum.TryParse<OmittedFeature>(c, out var f) ? f : (OmittedFeature?)null)
            .OfType<OmittedFeature>()
            .ToList();
    }

    private static IngestionOutcome Failed(Guid connectionId, IngestionCause cause, long? watermark, bool? retry = null) =>
        IngestionOutcome.Stopped(connectionId, IngestionStatus.Failed, cause, watermark, retry ?? IsTransient(cause));

    /// <summary>
    /// RNF-FIA-01: red, tiempo límite y fallos inesperados (incluida la persistencia) se reintentan; credenciales,
    /// TLS y permisos no, porque repetirlos solo acercaría la cuenta al bloqueo.
    /// </summary>
    public static bool IsTransient(IngestionCause cause) =>
        cause is IngestionCause.ServerUnreachable or IngestionCause.Timeout or IngestionCause.Unknown;

    private static IngestionCause MapKind(TelemetryFailureKind kind) => kind switch
    {
        TelemetryFailureKind.InvalidCredentials => IngestionCause.InvalidCredentials,
        TelemetryFailureKind.ServerUnreachable => IngestionCause.ServerUnreachable,
        TelemetryFailureKind.TlsCertificateInvalid => IngestionCause.TlsCertificateInvalid,
        TelemetryFailureKind.PermissionDenied => IngestionCause.MissingPermission,
        TelemetryFailureKind.Timeout => IngestionCause.Timeout,
        _ => IngestionCause.Unknown,
    };
}
