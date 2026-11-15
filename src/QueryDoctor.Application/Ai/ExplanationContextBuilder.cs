using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Masking;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Ai;

/// <summary>
/// Material ya descifrado de una consulta para armar su contexto de explicación. Vive solo en memoria del Worker:
/// nunca se registra ni se serializa (RNF-SEG-09).
/// </summary>
/// <param name="QueryId">Consulta de la plataforma.</param>
/// <param name="SqlText">Texto SQL descifrado; <see langword="null"/> si no hay.</param>
/// <param name="PlanXml">Showplan XML descifrado del plan analizado; <see langword="null"/> si no hay.</param>
/// <param name="Findings">Hallazgos del último análisis de la consulta.</param>
/// <param name="LatestScore">Último puntaje de salud (S2-02), fuente de las métricas ingeridas (E6).</param>
/// <param name="EngineVersion">Versión del motor SQL Server del plan.</param>
public sealed record ExplanationSource(
    Guid QueryId,
    string? SqlText,
    string? PlanXml,
    IReadOnlyList<FindingContract> Findings,
    HealthScore? LatestScore,
    string? EngineVersion);

/// <summary>
/// Contexto armado: el resultado del enmascarado (con sus métricas registrables) y la solicitud enviable, o
/// <see langword="null"/> si no quedó ningún hallazgo enviable.
/// </summary>
public sealed record ExplanationContext(
    MaskedContext Masked,
    MaskedExplanationRequest? Request,
    IReadOnlyDictionary<string, double> QueryMetrics);

/// <summary>
/// Arma el contexto de la explicación exclusivamente con hallazgos, métricas ingeridas y fragmentos curados
/// (RF-IA-01, RF-IA-04), todo después de pasar por el enmascarador (RNF-SEG-05). Puro y determinista: mismo
/// material, misma solicitud y mismo hash (RF-IA-07).
/// </summary>
public sealed class ExplanationContextBuilder
{
    private readonly IMasker _masker;
    private readonly IRuleCatalog _catalog;

    public ExplanationContextBuilder(IMasker masker, IRuleCatalog catalog)
    {
        _masker = masker ?? throw new ArgumentNullException(nameof(masker));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>
    /// Enmascara el material y arma la solicitud. <paramref name="kbFragments"/> son los fragmentos curados de las
    /// reglas de los hallazgos; solo viajan los de reglas cuyos hallazgos resultaron enviables.
    /// </summary>
    public ExplanationContext Build(ExplanationSource source, IReadOnlyList<KbFragment> kbFragments, Guid? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(kbFragments);

        var masked = _masker.Mask(new MaskingInput(source.SqlText, source.PlanXml, source.Findings));
        var metrics = QueryMetricsFrom(source.LatestScore);

        var sendableRules = masked.Findings
            .Select(f => f.RuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ruleId in sendableRules)
        {
            if (_catalog.TryGetDescriptor(ruleId) is { } descriptor)
            {
                descriptions[ruleId] = descriptor.Description;
            }
        }

        var additions = new ExplanationContextAdditions(
            _catalog.CatalogVersion,
            descriptions,
            kbFragments
                .Where(k => sendableRules.Contains(k.RuleId))
                .GroupBy(k => k.Id)
                .Select(g => g.First())
                .ToArray(),
            metrics);

        var request = MaskedExplanationRequest.TryCreate(source.QueryId, masked, additions, correlationId);
        return new ExplanationContext(masked, request, metrics);
    }

    /// <summary>
    /// Métricas ingeridas de la consulta (RF-IA-01, E6): promedios por ejecución y consumo de la ventana del último
    /// puntaje. Son números calculados por la plataforma, no literales de la consulta. Se redondean a 4 decimales para
    /// que la IA y el validador citen el mismo número (RF-IA-03 b).
    /// </summary>
    public static IReadOnlyDictionary<string, double> QueryMetricsFrom(HealthScore? score)
    {
        var metrics = new SortedDictionary<string, double>(StringComparer.Ordinal);
        if (score is null)
        {
            return metrics;
        }

        metrics["executions"] = score.TotalExecutions;
        metrics["weighted_consumption_pct"] = Math.Round(score.WeightedConsumptionPct, 4);
        metrics["health_score"] = Math.Round(score.Score, 4);
        if (score.TotalExecutions > 0)
        {
            metrics["avg_duration_ms"] = Math.Round(score.TotalDurationMicroseconds / score.TotalExecutions / 1000.0, 4);
            metrics["avg_cpu_ms"] = Math.Round(score.TotalCpuMicroseconds / score.TotalExecutions / 1000.0, 4);
            metrics["avg_logical_reads"] = Math.Round(score.TotalLogicalReads / score.TotalExecutions, 4);
        }

        return metrics;
    }
}
