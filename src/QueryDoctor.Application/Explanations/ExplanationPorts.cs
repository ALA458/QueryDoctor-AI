using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Explanations;

/// <summary>
/// Consulta a explicar, con lo mínimo para decidir si se encola (RF-IA-01, CA-HU09.2).
/// </summary>
public sealed record ExplanationTarget(Guid QueryId, Guid ConnectionId, int FindingsInLatestAnalysis);

/// <summary>Resumen de un hallazgo del último análisis, para mostrar junto al aviso de indisponibilidad (E7).</summary>
public sealed record ExplanationFindingSummary(string FindingCode, string RuleId, FindingSeverity Severity, EvidenceClass EvidenceClass);

/// <summary>
/// Persistencia de <c>ai.explanation</c> (S2-05). La usan la Api (solicitar y consultar) y el Worker (procesar).
/// </summary>
public interface IExplanationStore
{
    /// <summary>La consulta y cuántos hallazgos tiene su último análisis; <see langword="null"/> si no existe.</summary>
    Task<ExplanationTarget?> FindTargetAsync(Guid queryId, CancellationToken cancellationToken);

    /// <summary>Hallazgos del último análisis de la consulta, sin evidencia (E7).</summary>
    Task<IReadOnlyList<ExplanationFindingSummary>> GetLatestFindingsAsync(Guid queryId, CancellationToken cancellationToken);

    /// <summary>Última explicación solicitada para la consulta.</summary>
    Task<Explanation?> GetLatestAsync(Guid queryId, CancellationToken cancellationToken);

    Task<Explanation?> GetAsync(Guid explanationId, CancellationToken cancellationToken);

    /// <summary>
    /// Explicación TRAZADA y completada con el mismo hash de contexto, la más reciente (RF-IA-07: solo se reutilizan
    /// explicaciones trazadas).
    /// </summary>
    Task<Explanation?> FindTracedByContextHashAsync(string contextHash, CancellationToken cancellationToken);

    Task AddAsync(Explanation explanation, CancellationToken cancellationToken);

    /// <summary>
    /// Guarda la explicación completada, sus afirmaciones y referencias (<c>ai.claim</c>, <c>ai.claim_reference</c>,
    /// §7) y su entrada de traza (RF-TRZ-01, S2-06) en una sola transacción.
    /// </summary>
    Task CompleteAsync(Explanation explanation, IReadOnlyList<ExplanationClaim> claims, CancellationToken cancellationToken);

    /// <summary>
    /// Guarda un estado final sin afirmaciones (caché, IA no disponible, no generada, abandonada) junto con su entrada
    /// de traza, en una sola transacción (S2-06, T2 y T3).
    /// </summary>
    Task FinishAsync(Explanation explanation, CancellationToken cancellationToken);

    /// <summary>Afirmaciones de una explicación con sus referencias, en orden.</summary>
    Task<IReadOnlyList<ExplanationClaim>> GetClaimsAsync(Guid explanationId, CancellationToken cancellationToken);

    /// <summary>Guarda un estado intermedio (en curso). No escribe traza.</summary>
    Task SaveAsync(Explanation explanation, CancellationToken cancellationToken);
}

/// <summary>Encola el trabajo <c>explain</c> con solo el id de la explicación (invariante 2, §8.1).</summary>
public interface IExplanationJobScheduler
{
    void Enqueue(Guid explanationId);
}
