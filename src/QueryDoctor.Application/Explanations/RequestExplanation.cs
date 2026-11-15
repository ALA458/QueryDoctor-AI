using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Application.Explanations;

/// <summary>Resultado de pedir una explicación.</summary>
public abstract record RequestExplanationResult
{
    private RequestExplanationResult()
    {
    }

    public sealed record Accepted(Guid ExplanationId) : RequestExplanationResult;

    public sealed record QueryNotFound : RequestExplanationResult;

    /// <summary>CA-HU09.2: sin hallazgos no se genera explicación y se indica (RF-IA-01).</summary>
    public sealed record NoFindings : RequestExplanationResult;

    /// <summary>E10: ya hay una explicación pendiente o en curso para la consulta.</summary>
    public sealed record AlreadyInProgress(Guid ExplanationId) : RequestExplanationResult;
}

/// <summary>
/// Pide la explicación de una consulta (S2-05, CU-05 paso 1). Lo usa la Api: guarda la solicitud y encola
/// <c>explain</c> con solo el id. Nunca llama al servicio de IA (invariante 2).
/// </summary>
public sealed class RequestExplanation
{
    /// <summary>
    /// Una solicitud pendiente o en curso más vieja que esto se da por abandonada (el Worker se detuvo): supera el
    /// tiempo total por defecto del cliente de IA (200 s) con holgura para la espera en cola.
    /// </summary>
    public static readonly TimeSpan ActiveRequestWindow = TimeSpan.FromMinutes(10);

    private readonly IExplanationStore _store;
    private readonly IExplanationJobScheduler _scheduler;
    private readonly TimeProvider _clock;

    public RequestExplanation(IExplanationStore store, IExplanationJobScheduler scheduler, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<RequestExplanationResult> ExecuteAsync(Guid queryId, CancellationToken cancellationToken = default)
    {
        var target = await _store.FindTargetAsync(queryId, cancellationToken);
        if (target is null)
        {
            return new RequestExplanationResult.QueryNotFound();
        }

        if (target.FindingsInLatestAnalysis == 0)
        {
            return new RequestExplanationResult.NoFindings();
        }

        var now = _clock.GetUtcNow();
        var latest = await _store.GetLatestAsync(queryId, cancellationToken);
        if (latest is not null && IsActive(latest.State))
        {
            if (now - latest.RequestedAt < ActiveRequestWindow)
            {
                return new RequestExplanationResult.AlreadyInProgress(latest.Id);
            }

            latest.State = ExplanationState.Unavailable;
            latest.Cause = ExplanationCause.Abandoned;
            latest.CompletedAt = now;
            await _store.FinishAsync(latest, cancellationToken);
        }

        var explanation = new Explanation
        {
            Id = Guid.NewGuid(),
            ConnectionId = target.ConnectionId,
            QueryId = queryId,
            State = ExplanationState.Pending,
            CorrelationId = Guid.NewGuid(),
            RequestedAt = now,
        };
        await _store.AddAsync(explanation, cancellationToken);
        _scheduler.Enqueue(explanation.Id);

        return new RequestExplanationResult.Accepted(explanation.Id);
    }

    internal static bool IsActive(ExplanationState state) =>
        state is ExplanationState.Pending or ExplanationState.Running;
}
