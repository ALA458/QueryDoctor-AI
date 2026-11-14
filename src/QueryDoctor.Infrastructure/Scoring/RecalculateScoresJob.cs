using Hangfire;
using Microsoft.Extensions.Logging;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Application.Scoring;

namespace QueryDoctor.Infrastructure.Scoring;

/// <summary>
/// Trabajo Hangfire de análisis de planes y recálculo del puntaje de salud <c>H(q)</c> en la cola <see cref="JobQueues.Analyze"/> (S2-02, §6.2–6.3).
/// Su único argumento es el identificador de la conexión (§8.1).
/// </summary>
public sealed partial class RecalculateScoresJob
{
    private readonly RecalculateConnectionScores _recalculate;
    private readonly ILogger<RecalculateScoresJob> _logger;

    public RecalculateScoresJob(
        RecalculateConnectionScores recalculate,
        ILogger<RecalculateScoresJob> logger)
    {
        _recalculate = recalculate ?? throw new ArgumentNullException(nameof(recalculate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(JobQueues.Analyze)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [15, 30, 60], OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [JobDisplayName("recalculate_scores {0}")]
    public async Task RunAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var outcome = await _recalculate.ExecuteAsync(
            connectionId: connectionId,
            ingestionRunId: null,
            windowDuration: null,
            cancellationToken: cancellationToken);

        LogScoresRecalculated(
            _logger,
            connectionId,
            outcome.QueriesScored,
            outcome.PlansAnalyzed);
    }

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "recalculate_scores {ConnectionId}: {QueriesScored} consultas puntuadas, {PlansAnalyzed} planes analizados")]
    private static partial void LogScoresRecalculated(
        ILogger logger,
        Guid connectionId,
        int queriesScored,
        int plansAnalyzed);
}
