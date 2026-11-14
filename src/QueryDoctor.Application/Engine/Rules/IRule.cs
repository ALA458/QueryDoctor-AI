using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.Application.Engine.Rules;

/// <summary>
/// Descriptor inmutable de una regla del catálogo determinístico (RF-MOT-15, Tabla 4.11 del SRS).
/// Declara identificador, nombre, versión, descripción, fuente documental, clase de evidencia requerida y umbrales por defecto.
/// </summary>
public sealed record RuleDescriptor(
    string RuleId,
    string Name,
    string Version,
    string Description,
    string DocumentationSource,
    RuleClass RuleClass,
    EvidenceClass RequiredEvidenceClass,
    FindingSeverity BaseSeverity,
    RulePriority Priority,
    IReadOnlyDictionary<string, double> DefaultThresholds);

/// <summary>
/// Contexto puro de entrada suministrado a cada regla <see cref="IRule"/> durante la evaluación de un plan (RF-MOT-02, RF-MOT-03, RF-MOT-14).
/// </summary>
public sealed record RuleEvaluationContext(
    ParsedPlan Plan,
    RuleThresholds Thresholds,
    long QueryStoreQueryId = 0,
    long QueryStorePlanId = 0,
    string? PlanIdentifier = null,
    long? QueryExecutionCount = null,
    double? QueryConsumptionPct = null,
    double? AvgRowsReturned = null);

/// <summary>
/// Borrador de hallazgo producido por una implementación de <see cref="IRule"/> antes de que el motor le asigne su <c>finding_id</c> determinista y metadatos del catálogo.
/// </summary>
public sealed record RuleFindingDraft(
    FindingSeverity Severity,
    IReadOnlyList<FindingNodeContract> Nodes,
    IReadOnlyDictionary<string, string?> Evidence,
    FindingContextContract? Context = null,
    FindingKind Kind = FindingKind.Senal,
    string? Discriminator = null);

/// <summary>
/// Unidad independiente de evaluación del motor determinístico (RF-MOT-15, RNF-MAN-01, Arquitectura §5.1).
/// Toda implementación debe ser pura, determinista y libre de E/S (Invariante 7).
/// </summary>
public interface IRule
{
    /// <summary>Metadatos declarativos de la regla en el catálogo.</summary>
    RuleDescriptor Descriptor { get; }

    /// <summary>
    /// Evalúa un plan tipado y devuelve cero o más borradores de hallazgo.
    /// </summary>
    IReadOnlyList<RuleFindingDraft> Evaluate(RuleEvaluationContext context);
}
