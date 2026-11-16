using Hangfire;
using Microsoft.Extensions.Logging;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Application.Retention;

namespace QueryDoctor.Infrastructure.Retention;

/// <summary>
/// Trabajo Hangfire periódico de purga por retención (S2-13, RNF-SEG-10). Corre en la cola <see cref="JobQueues.Retention"/>.
/// </summary>
public sealed partial class RetentionPurgeJob
{
    private readonly PurgeExpiredTelemetry _purge;
    private readonly ILogger<RetentionPurgeJob> _logger;

    public RetentionPurgeJob(PurgeExpiredTelemetry purge, ILogger<RetentionPurgeJob> logger)
    {
        _purge = purge ?? throw new ArgumentNullException(nameof(purge));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(JobQueues.Retention)]
    [AutomaticRetry(Attempts = 2)]
    [JobDisplayName("retention_purge")]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var outcome = await _purge.ExecuteAsync(cancellationToken);
        LogCompleted(_logger, outcome.TextsCleared, outcome.PlansCleared);
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information,
        Message = "retention_purge: {TextsCleared} textos SQL y {PlansCleared} planes purgados por expiración.")]
    private static partial void LogCompleted(ILogger logger, int textsCleared, int plansCleared);
}
