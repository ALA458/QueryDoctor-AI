using System.Text.Json.Serialization;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Explanations;

/// <summary>Hallazgo que acompaña a la explicación o al aviso de indisponibilidad (E7, CA-HU12.1).</summary>
public sealed record ExplanationFindingView(
    [property: JsonPropertyName("finding_code")] string FindingCode,
    [property: JsonPropertyName("rule_id")] string RuleId,
    [property: JsonPropertyName("rule_name")] string RuleName,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("evidence_class")] string EvidenceClass);

/// <summary>
/// Estado de la última explicación de una consulta (S2-05). Siempre lleva los hallazgos del último análisis: si la
/// IA no está disponible, el usuario los sigue viendo junto con el aviso (RF-IA-06, CA-HU12.1).
/// </summary>
public sealed record ExplanationView(
    [property: JsonPropertyName("explanation_id")] Guid ExplanationId,
    [property: JsonPropertyName("query_id")] Guid QueryId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("cause")] string? Cause,
    [property: JsonPropertyName("validation_status")] string? ValidationStatus,
    [property: JsonPropertyName("notice")] string? Notice,
    [property: JsonPropertyName("from_cache")] bool FromCache,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("prompt_version")] string? PromptVersion,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("claims")] IReadOnlyList<ExplanationBodyClaim> Claims,
    [property: JsonPropertyName("validation_failures")] IReadOnlyList<ExplanationBodyFailure> ValidationFailures,
    [property: JsonPropertyName("findings")] IReadOnlyList<ExplanationFindingView> Findings);

/// <summary>Resultado de consultar la última explicación.</summary>
public abstract record GetLatestExplanationResult
{
    private GetLatestExplanationResult()
    {
    }

    public sealed record Found(ExplanationView View) : GetLatestExplanationResult;

    public sealed record QueryNotFound : GetLatestExplanationResult;

    public sealed record NotRequested : GetLatestExplanationResult;
}

/// <summary>
/// Lee la última explicación de una consulta para la Api (S2-05). Descifra el cuerpo para mostrarlo; la SPA lo pinta
/// como texto (RNF-SEG-07). No escribe nada: una solicitud abandonada se muestra como no disponible sin tocarla.
/// </summary>
public sealed class GetLatestExplanation
{
    private readonly IExplanationStore _store;
    private readonly IFieldEncryptor _encryptor;
    private readonly IRuleCatalog _catalog;
    private readonly TimeProvider _clock;

    public GetLatestExplanation(IExplanationStore store, IFieldEncryptor encryptor, IRuleCatalog catalog, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<GetLatestExplanationResult> ExecuteAsync(Guid queryId, CancellationToken cancellationToken = default)
    {
        if (await _store.FindTargetAsync(queryId, cancellationToken) is null)
        {
            return new GetLatestExplanationResult.QueryNotFound();
        }

        var explanation = await _store.GetLatestAsync(queryId, cancellationToken);
        if (explanation is null)
        {
            return new GetLatestExplanationResult.NotRequested();
        }

        var findings = (await _store.GetLatestFindingsAsync(queryId, cancellationToken))
            .Select(f => new ExplanationFindingView(
                f.FindingCode,
                f.RuleId,
                _catalog.TryGetDescriptor(f.RuleId)?.Name ?? f.RuleId,
                f.Severity.ToContractCode(),
                f.EvidenceClass.ToContractCode()))
            .ToArray();

        var state = explanation.State;
        var cause = explanation.Cause;
        if (RequestExplanation.IsActive(state) && _clock.GetUtcNow() - explanation.RequestedAt >= RequestExplanation.ActiveRequestWindow)
        {
            state = ExplanationState.Unavailable;
            cause = ExplanationCause.Abandoned;
        }

        var body = state == ExplanationState.Completed && explanation.EncryptedBody is not null
            ? ExplanationBody.FromJson(_encryptor.Decrypt(explanation.EncryptedBody))
            : null;

        return new GetLatestExplanationResult.Found(new ExplanationView(
            explanation.Id,
            explanation.QueryId,
            state.ToContractCode(),
            cause?.ToString(),
            explanation.ValidationStatus,
            body?.PersistentNotice ?? ExplanationCodes.NoticeFor(state, cause),
            explanation.ReusedFromId is not null,
            explanation.RequestedAt,
            explanation.CompletedAt,
            explanation.Model,
            explanation.PromptVersion,
            body?.Summary,
            body?.Claims ?? [],
            body?.ValidationFailures ?? [],
            findings));
    }
}
