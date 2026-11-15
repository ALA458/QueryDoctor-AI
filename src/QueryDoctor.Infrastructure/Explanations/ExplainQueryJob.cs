using Hangfire;
using Microsoft.Extensions.Logging;
using QueryDoctor.Application.Ai;
using QueryDoctor.Application.Explanations;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Application.Masking;
using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Infrastructure.Explanations;

/// <summary>
/// Trabajo Hangfire <c>explain</c> (S2-05, §6.4). Corre en el Worker, en la cola <see cref="JobQueues.Explain"/>, con
/// un único argumento: el id de la explicación (invariante 2, §8.1). Sin reintentos de Hangfire (E1): el servicio de
/// IA ya reintenta hacia el LLM y repetir multiplicaría las llamadas. Solo registra ids, códigos y conteos.
/// </summary>
public sealed partial class ExplainQueryJob
{
    private readonly ExplainQuery _explain;
    private readonly ILogger<ExplainQueryJob> _logger;

    public ExplainQueryJob(ExplainQuery explain, ILogger<ExplainQueryJob> logger)
    {
        _explain = explain ?? throw new ArgumentNullException(nameof(explain));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(JobQueues.Explain)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [JobDisplayName("explain {0}")]
    public async Task RunAsync(Guid explanationId, CancellationToken cancellationToken)
    {
        var outcome = await _explain.ExecuteAsync(explanationId, cancellationToken);
        if (outcome is null)
        {
            LogNotFound(_logger, explanationId);
            return;
        }

        LogOutcome(
            _logger,
            explanationId,
            outcome.State,
            outcome.Cause,
            outcome.ValidationStatus,
            outcome.FromCache,
            outcome.FindingsSent,
            outcome.Masking);
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "explain {ExplanationId}: no existe")]
    private static partial void LogNotFound(ILogger logger, Guid explanationId);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Information,
        Message = "explain {ExplanationId}: {State} causa={Cause} validacion={ValidationStatus} cache={FromCache} hallazgos={FindingsSent} enmascarado={Masking}")]
    private static partial void LogOutcome(
        ILogger logger,
        Guid explanationId,
        ExplanationState state,
        ExplanationCause? cause,
        string? validationStatus,
        bool fromCache,
        int findingsSent,
        MaskingMetrics? masking);
}

/// <summary>Encola <c>explain</c> en Hangfire con solo el id (lo usa la Api).</summary>
public sealed class HangfireExplanationJobScheduler : IExplanationJobScheduler
{
    private readonly IBackgroundJobClient _client;

    public HangfireExplanationJobScheduler(IBackgroundJobClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public void Enqueue(Guid explanationId) =>
        _client.Enqueue<ExplainQueryJob>(JobQueues.Explain, job => job.RunAsync(explanationId, CancellationToken.None));
}
