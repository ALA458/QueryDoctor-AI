using QueryDoctor.Application.KnowledgeBase;
using QueryDoctor.Application.Masking;
using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Application.Validation;

/// <summary>
/// Construye el <see cref="ExplanationValidationContext"/> a partir de hallazgos enmascarados y la base curada de S1-17,
/// dejando preparado el puente para la orquestación asíncrona de explicaciones en S2-05.
/// </summary>
public interface IExplanationValidationContextBuilder
{
    Task<ExplanationValidationContext> BuildAsync(
        string queryId,
        IReadOnlyList<MaskedFinding> findings,
        IReadOnlyDictionary<string, double>? queryMetrics = null,
        string? engineVersion = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación estándar en capa Application que consulta <see cref="IKnowledgeBaseRepository"/> sin acoplarse a E/S ni EF Core.
/// </summary>
public sealed class ExplanationValidationContextBuilder : IExplanationValidationContextBuilder
{
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository;

    public ExplanationValidationContextBuilder(IKnowledgeBaseRepository knowledgeBaseRepository)
    {
        ArgumentNullException.ThrowIfNull(knowledgeBaseRepository);
        _knowledgeBaseRepository = knowledgeBaseRepository;
    }

    public async Task<ExplanationValidationContext> BuildAsync(
        string queryId,
        IReadOnlyList<MaskedFinding> findings,
        IReadOnlyDictionary<string, double>? queryMetrics = null,
        string? engineVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentNullException.ThrowIfNull(findings);

        var distinctRules = findings
            .Select(static f => f.RuleId?.Trim() ?? string.Empty)
            .Where(static r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static r => r, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var curatedFragments = new List<KbFragment>();
        var curatedRelations = new List<KbCausalRelation>();

        foreach (var ruleId in distinctRules)
        {
            var fragments = await _knowledgeBaseRepository.GetFragmentsByRuleAsync(ruleId, cancellationToken);
            curatedFragments.AddRange(fragments);

            var relations = await _knowledgeBaseRepository.GetCausalRelationsAsync(ruleId, cancellationToken);
            curatedRelations.AddRange(relations);
        }

        return FromCuratedCatalog(
            queryId,
            findings,
            curatedFragments,
            curatedRelations,
            queryMetrics,
            engineVersion);
    }

    /// <summary>
    /// Crea un contexto puro en memoria cuando el llamador ya dispone de los fragmentos y relaciones curadas.
    /// </summary>
    public static ExplanationValidationContext FromCuratedCatalog(
        string queryId,
        IReadOnlyList<MaskedFinding> findings,
        IEnumerable<KbFragment> curatedFragments,
        IEnumerable<KbCausalRelation> curatedRelations,
        IReadOnlyDictionary<string, double>? queryMetrics = null,
        string? engineVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(curatedFragments);
        ArgumentNullException.ThrowIfNull(curatedRelations);

        var findingInputs = findings
            .Select(static f => ValidatedFindingInput.FromMaskedFinding(f))
            .ToArray();

        var fragmentIds = curatedFragments
            .Select(static f => f.Id)
            .Where(static id => id != Guid.Empty)
            .ToHashSet();

        var relationSet = curatedRelations
            .Where(static r => !string.IsNullOrWhiteSpace(r.RuleId) && r.FragmentId != Guid.Empty)
            .Select(static r => new CuratedCausalRelation(r.RuleId, r.FragmentId, r.RelationType))
            .ToHashSet();

        var metrics = queryMetrics ?? new Dictionary<string, double>(StringComparer.Ordinal);

        return new ExplanationValidationContext(
            queryId,
            findingInputs,
            metrics,
            fragmentIds,
            relationSet,
            engineVersion);
    }
}
