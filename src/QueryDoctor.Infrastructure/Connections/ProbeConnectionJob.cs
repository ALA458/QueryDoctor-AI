using Hangfire;
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
/// Sin reintentos automáticos: un sondeo fallido ya terminó en Failed con su causa, y repetirlo con las mismas
/// credenciales solo bloquearía la cuenta. Si el proceso muere a mitad, Hangfire vuelve a entregar el trabajo y
/// <see cref="ProbeConnection"/> lo retoma porque la conexión sigue en Pending.
/// </remarks>
public sealed partial class ProbeConnectionJob
{
    private readonly ProbeConnection _probe;
    private readonly ILogger<ProbeConnectionJob> _logger;

    public ProbeConnectionJob(ProbeConnection probe, ILogger<ProbeConnectionJob> logger)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(JobQueues.Probe)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [JobDisplayName("probe_connection {0}")]
    public async Task RunAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var outcome = await _probe.ExecuteAsync(connectionId, cancellationToken);

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
