using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Infrastructure.Ingestion;

/// <summary>Fallo transitorio de la ingesta: hace que Hangfire reintente. Solo lleva el código (RNF-SEG-09).</summary>
public sealed class IngestionRetryException : Exception
{
    public IngestionRetryException()
    {
    }

    public IngestionRetryException(string message)
        : base(message)
    {
    }

    public IngestionRetryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public IngestionRetryException(IngestionCause cause)
        : base($"Ingesta fallida: {cause}. Se reintenta.")
    {
    }
}

/// <summary>
/// Trabajo Hangfire ingest_connection (S1-11, §6.2). Corre en el Worker, en la cola <see cref="JobQueues.Ingest"/>.
/// Su único argumento es el id de la conexión (§8.1); manual y programada son dos métodos para que el disparo quede
/// en ingestion_run sin agregar argumentos.
/// </summary>
/// <remarks>
/// RNF-FIA-01: los fallos transitorios (red, tiempo límite, inesperados) se relanzan y Hangfire reintenta con espera
/// exponencial; cada intento retoma desde la marca de agua confirmada. Credenciales, TLS, permisos y Query Store OFF
/// no se reintentan: quedan registrados con su causa.
/// </remarks>
public sealed partial class IngestConnectionJob
{
    /// <summary>Espera exponencial entre reintentos, en segundos.</summary>
    public static readonly int[] RetryDelaysInSeconds = [30, 60, 120, 240];

    private readonly IngestConnection _ingest;
    private readonly ILogger<IngestConnectionJob> _logger;

    public IngestConnectionJob(IngestConnection ingest, ILogger<IngestConnectionJob> logger)
    {
        _ingest = ingest ?? throw new ArgumentNullException(nameof(ingest));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(JobQueues.Ingest)]
    [AutomaticRetry(Attempts = 4, DelaysInSeconds = [30, 60, 120, 240], OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [JobDisplayName("ingest_connection {0} (manual)")]
    public Task RunManualAsync(Guid connectionId, CancellationToken cancellationToken) =>
        RunAsync(connectionId, IngestionTrigger.Manual, cancellationToken);

    [Queue(JobQueues.Ingest)]
    [AutomaticRetry(Attempts = 4, DelaysInSeconds = [30, 60, 120, 240], OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [JobDisplayName("ingest_connection {0} (programada)")]
    public Task RunScheduledAsync(Guid connectionId, CancellationToken cancellationToken) =>
        RunAsync(connectionId, IngestionTrigger.Scheduled, cancellationToken);

    private async Task RunAsync(Guid connectionId, IngestionTrigger trigger, CancellationToken cancellationToken)
    {
        IngestionOutcome outcome;
        try
        {
            outcome = await _ingest.ExecuteAsync(connectionId, trigger, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Lo que escapa del caso de uso no pasó por su clasificación: bloqueo, lectura de la conexión o apertura y
            // cierre de la ejecución (casi siempre PostgreSQL). Se registra solo el tipo y Hangfire recibe una excepción
            // sin el mensaje original, que podría traer host o usuario (RNF-SEG-09). Auditoría B6: no se etiqueta como
            // "persistencia" lo que no se sabe que lo sea. Una ejecución que quedó en Running la cierra la siguiente.
            LogOutsideIngestionFailed(_logger, connectionId, ex.GetType().Name);
            throw new IngestionRetryException(IngestionCause.Unknown);
        }

        // Solo id, estado, código, conteos y marca de agua: nunca servidor, usuario, SQL ni texto del motor.
        switch (outcome.Status)
        {
            case null:
                LogNotFound(_logger, connectionId);
                break;
            case IngestionStatus.Succeeded:
                LogSucceeded(_logger, connectionId, outcome.RowsUpserted, outcome.WatermarkBefore, outcome.WatermarkAfter);
                break;
            default:
                LogStopped(_logger, connectionId, outcome.Status.Value, outcome.Cause);
                break;
        }

        foreach (var feature in outcome.OmittedFeatures)
        {
            LogOmitted(_logger, connectionId, feature);
        }

        if (outcome.DiscardedRows > 0)
        {
            LogDiscarded(_logger, connectionId, outcome.DiscardedRows);
        }

        if (outcome.ShouldRetry)
        {
            throw new IngestionRetryException(outcome.Cause ?? IngestionCause.Unknown);
        }
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "ingest_connection {ConnectionId}: Succeeded, {RowsUpserted} filas, marca {WatermarkBefore} -> {WatermarkAfter}")]
    private static partial void LogSucceeded(ILogger logger, Guid connectionId, int rowsUpserted, long? watermarkBefore, long? watermarkAfter);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "ingest_connection {ConnectionId}: {Status} ({Cause})")]
    private static partial void LogStopped(ILogger logger, Guid connectionId, IngestionStatus status, IngestionCause? cause);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Information, Message = "ingest_connection {ConnectionId}: la conexión no existe")]
    private static partial void LogNotFound(ILogger logger, Guid connectionId);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Warning,
        Message = "ingest_connection {ConnectionId}: función omitida por el perfil de capacidades: {Feature} (RNF-COM-04)")]
    private static partial void LogOmitted(ILogger logger, Guid connectionId, OmittedFeature feature);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Warning,
        Message = "ingest_connection {ConnectionId}: {Count} filas con execution_type desconocido descartadas")]
    private static partial void LogDiscarded(ILogger logger, Guid connectionId, int count);

    [LoggerMessage(EventId = 4006, Level = LogLevel.Error,
        Message = "ingest_connection {ConnectionId}: fallo fuera de la ingesta (bloqueo o registro de la ejecución): {ExceptionType}")]
    private static partial void LogOutsideIngestionFailed(ILogger logger, Guid connectionId, string exceptionType);
}

/// <summary>
/// Encola ingest_connection con el cliente Hangfire de la Api (S1-10). Auditoría B1: no encola otra si ya hay una de la
/// misma conexión esperando en la cola ingest (se mira con la API de monitoreo de Hangfire, que solo lee PostgreSQL).
/// </summary>
public sealed class HangfireIngestionJobScheduler : IIngestionJobScheduler
{
    /// <summary>Cuántos trabajos en espera de la cola ingest se revisan.</summary>
    public const int PendingJobsChecked = 1000;

    private readonly IBackgroundJobClient _client;
    private readonly JobStorage _storage;

    public HangfireIngestionJobScheduler(IBackgroundJobClient client, JobStorage storage)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public bool TryEnqueueManual(Guid connectionId)
    {
        if (IsPending(connectionId))
        {
            return false;
        }

        _client.Enqueue<IngestConnectionJob>(JobQueues.Ingest, job => job.RunManualAsync(connectionId, CancellationToken.None));
        return true;
    }

    private bool IsPending(Guid connectionId) =>
        _storage.GetMonitoringApi()
            .EnqueuedJobs(JobQueues.Ingest, 0, PendingJobsChecked)
            .Select(entry => entry.Value?.Job)
            .Any(job => job is not null
                && job.Type == typeof(IngestConnectionJob)
                && job.Args.Count > 0
                && job.Args[0] is Guid id
                && id == connectionId);
}

/// <summary>Lecturas de ejecuciones de ingesta con EF Core, sin rastreo.</summary>
public sealed class EfIngestionQueries : IIngestionQueries
{
    private readonly AppDbContext _db;

    public EfIngestionQueries(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<IngestionRunView>?> ListRunsAsync(Guid connectionId, int take, CancellationToken cancellationToken)
    {
        if (!await _db.Connections.AsNoTracking().AnyAsync(c => c.Id == connectionId, cancellationToken))
        {
            return null;
        }

        var runs = await _db.IngestionRuns.AsNoTracking()
            .Where(r => r.ConnectionId == connectionId)
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);

        return runs.Select(IngestionRunView.From).ToList();
    }

    public Task<bool> HasRunningAsync(Guid connectionId, CancellationToken cancellationToken) =>
        _db.IngestionRuns.AsNoTracking()
            .AnyAsync(r => r.ConnectionId == connectionId && r.Status == IngestionStatus.Running, cancellationToken);
}
