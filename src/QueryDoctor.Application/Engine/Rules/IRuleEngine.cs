using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Engine.Rules;

/// <summary>
/// Solicitud de evaluación determinista para un plan de ejecución (RF-MOT-01, RF-MOT-02, RF-MOT-14).
/// </summary>
public sealed record PlanAnalysisRequest(
    string? ShowplanXml,
    long QueryStoreQueryId = 0,
    long QueryStorePlanId = 0,
    string? PlanIdentifier = null,
    string? FallbackEngineVersion = null,
    RuleThresholds? Thresholds = null,
    IReadOnlySet<EvidenceClass>? AvailableEvidenceClasses = null,
    long? QueryExecutionCount = null,
    double? QueryConsumptionPct = null,
    double? AvgRowsReturned = null,
    DateTimeOffset? AnalyzedAt = null);

/// <summary>
/// Resultado determinista del análisis de un plan por el motor de reglas (RF-MOT-02, RF-MOT-03, RF-MOT-14, RF-MOT-17).
/// </summary>
public sealed record PlanAnalysisResult(
    string PlanKey,
    PlanAnalysisStatus AnalysisStatus,
    string CatalogVersion,
    string? EngineVersion,
    RuleThresholds ThresholdsUsed,
    string ThresholdsUsedJson,
    string ThresholdConfigurationHash,
    IReadOnlyList<FindingContract> Findings,
    IReadOnlyList<string> OmittedRuleIds,
    string? NotAnalyzableCause = null);

/// <summary>
/// Motor determinístico de evaluación de planes de ejecución (RF-MOT-02..17, Arquitectura §5.1).
/// Opera en memoria sin E/S (Invariante 7).
/// </summary>
public interface IRuleEngine
{
    /// <summary>
    /// Analiza un plan en formato Showplan XML aplicando el catálogo de reglas versionado y los umbrales configurados.
    /// Si el XML es inválido, produce el hallazgo <c>REG-11</c> (<c>PLAN_NO_ANALIZABLE</c>) sin lanzar excepción.
    /// </summary>
    PlanAnalysisResult Analyze(PlanAnalysisRequest request);

    /// <summary>
    /// Analiza un resultado previamente obtenido de <see cref="IPlanParser"/>.
    /// </summary>
    PlanAnalysisResult AnalyzeParsed(
        PlanParseResult parseResult,
        PlanAnalysisRequest request);

    /// <summary>
    /// Analiza un lote de planes de forma determinista garantizando que un plan con XML inválido produzca <c>REG-11</c>
    /// sin interrumpir el análisis de los demás planes del lote (RF-MOT-16, OWASP A10).
    /// </summary>
    IReadOnlyList<PlanAnalysisResult> AnalyzeBatch(IEnumerable<PlanAnalysisRequest> requests);
}
