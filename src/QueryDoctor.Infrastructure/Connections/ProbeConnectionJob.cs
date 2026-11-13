using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Connections;

/// <summary>
/// Trabajo Hangfire probe_connection (S1-09, §5.1). Corre en el Worker, en la cola <see cref="JobQueues.Probe"/>.
/// Su único argumento es el id de la conexión (§8.1): Hangfire guarda los argumentos en claro.
/// </summary>
/// <remarks>
/// Los fallos del SONDEO (credenciales, red, TLS, permisos, tiempo límite) no escapan de
/// <see cref="ProbeConnection"/>: ya terminan en Failed con su causa y no se reintentan, porque repetirlos con las
/// mismas credenciales solo acercaría la cuenta al bloqueo.
/// Lo que sí escapa son fallos de PERSISTENCIA (PostgreSQL al leer la conexión o al guardar el resultado). Auditoría
/// M1: antes eso dejaba la conexión en Pending para siempre. Ahora el trabajo la marca Failed/Unknown en un scope
/// nuevo (DbContext limpio) y, si ni eso se puede, Hangfire reintenta; el reintento solo vuelve a sondear si la
/// conexión sigue en Pending.
/// </remarks>
public sealed partial class ProbeConnectionJob
{
    /// <summary>Reintentos de Hangfire: solo llegan aquí fallos de persistencia (ver remarks).</summary>
    public const int PersistenceRetryAttempts = 3;

    private readonly ProbeConnection _probe;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProbeConnectionJob> _logger;

    public ProbeConnectionJob(
        ProbeConnection probe,
        IServiceScopeFactory scopes,
        TimeProvider clock,
        ILogger<ProbeConnectionJob> logger)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(JobQueues.Probe)]
    [AutomaticRetry(Attempts = PersistenceRetryAttempts, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [JobDisplayName("probe_connection {0}")]
    public async Task RunAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ProbeOutcome outcome;
        try
        {
            outcome = await _probe.ExecuteAsync(connectionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Solo el tipo: el mensaje de un error de base de datos puede traer host o usuario (RNF-SEG-09).
            LogPersistenceFailed(_logger, connectionId, ex.GetType().Name);

            // Si esto también falla (PostgreSQL caído), la excepción sube y Hangfire reintenta.
            await using var scope = _scopes.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
            var marked = await repository.MarkProbeFailedIfPendingAsync(
                connectionId, ProbeFailureCause.Unknown, _clock.GetUtcNow(), CancellationToken.None);

            if (marked)
            {
                LogFailed(_logger, connectionId, ConnectionStatus.Failed, ProbeFailureCause.Unknown);
            }
            else
            {
                LogSkipped(_logger, connectionId, null);
            }

            return;
        }

        // Solo id, estado y código (RNF-SEG-09): nunca servidor, usuario ni texto del motor.
        if (!outcome.Ran)
        {
            LogSkipped(_logger, connectionId, outcome.Status);
        }
        else if (outcome.FailureCause is { } cause)
        {
            LogFailed(_logger, connectionId, outcome.Status, cause);
        }
        else
        {
            LogSucceeded(_logger, connectionId);
        }
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "probe_connection {ConnectionId}: Active")]
    private static partial void LogSucceeded(ILogger logger, Guid connectionId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "probe_connection {ConnectionId}: {Status} ({FailureCause})")]
    private static partial void LogFailed(ILogger logger, Guid connectionId, ConnectionStatus? status, ProbeFailureCause failureCause);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "probe_connection {ConnectionId}: omitido, estado {Status}")]
    private static partial void LogSkipped(ILogger logger, Guid connectionId, ConnectionStatus? status);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error, Message = "probe_connection {ConnectionId}: fallo de persistencia ({ExceptionType})")]
    private static partial void LogPersistenceFailed(ILogger logger, Guid connectionId, string exceptionType);
}

/// <summary>Encola probe_connection con el cliente Hangfire de la API (S1-10).</summary>
public sealed class HangfireProbeJobScheduler : IProbeJobScheduler
{
    private readonly IBackgroundJobClient _client;

    public HangfireProbeJobScheduler(IBackgroundJobClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public void EnqueueProbe(Guid connectionId) =>
        _client.Enqueue<ProbeConnectionJob>(JobQueues.Probe, job => job.RunAsync(connectionId, CancellationToken.None));
}
