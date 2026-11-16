using Hangfire;
using Microsoft.Extensions.Logging;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Application.Retention;

namespace QueryDoctor.Infrastructure.Retention;

/// <summary>
/// Trabajo recurrente <c>retention</c> (S2-13, RNF-SEG-10) en la cola <see cref="JobQueues.Retention"/>. No recibe
/// argumentos (invariante 2). Sin reintento automático: si falla, la corrida del día siguiente purga lo pendiente.
/// </summary>
public sealed partial class RetentionJob
{
    /// <summary>Identificador del trabajo recurrente en Hangfire.</summary>
    public const string RecurringJobId = "retention";

    /// <summary>Diario a las 03:00 UTC (D2): fuera del horario habitual, sin configurar.</summary>
    public const string DailyCron = "0 3 * * *";

    private readonly PurgeExpiredEvidence _purge;
    private readonly ILogger<RetentionJob> _logger;

    public RetentionJob(PurgeExpiredEvidence purge, ILogger<RetentionJob> logger)
    {
        _purge = purge ?? throw new ArgumentNullException(nameof(purge));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(JobQueues.Retention)]
    [AutomaticRetry(Attempts = 0)]
    [JobDisplayName("retention")]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var outcome = await _purge.ExecuteAsync(cancellationToken);

        // RNF-SEG-09: solo ids y conteos, nunca texto SQL ni planes.
        foreach (var (connectionId, counts) in outcome.Purged)
        {
            LogPurged(_logger, connectionId, counts.SqlTexts, counts.Plans, counts.Findings);
        }

        LogRunFinished(_logger, outcome.IngestedBefore, outcome.Purged.Count);
    }

    [LoggerMessage(
        EventId = 8001,
        Level = LogLevel.Information,
        Message = "retention {ConnectionId}: {SqlTexts} textos, {Plans} planes y {Findings} evidencias de hallazgos purgados")]
    private static partial void LogPurged(ILogger logger, Guid connectionId, int sqlTexts, int plans, int findings);

    [LoggerMessage(
        EventId = 8002,
        Level = LogLevel.Information,
        Message = "retention: corrida terminada (ingerido antes de {IngestedBefore:o}); {Connections} conexiones con purga")]
    private static partial void LogRunFinished(ILogger logger, DateTimeOffset ingestedBefore, int connections);
}
