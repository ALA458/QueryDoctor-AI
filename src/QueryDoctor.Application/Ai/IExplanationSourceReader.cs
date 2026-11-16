using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Ai;

/// <summary>
/// Material cifrado de una consulta tal como está en la base: texto SQL, plan del último análisis, sus hallazgos
/// (evidencia cifrada) y el último puntaje de salud (S2-02).
/// </summary>
public sealed record EncryptedExplanationSource(
    Guid QueryId,
    long QueryStoreQueryId,
    long QueryStorePlanId,
    string? EncryptedSqlText,
    string? EncryptedPlanXml,
    string? EngineVersion,
    IReadOnlyList<Finding> Findings,
    HealthScore? LatestScore,
    bool EvidenceExpired = false);

/// <summary>Lee el material de una consulta para su explicación. Solo lo usa el Worker (invariante 2).</summary>
public interface IExplanationSourceReader
{
    /// <summary><see langword="null"/> si la consulta no existe o no tiene ningún análisis terminado.</summary>
    Task<EncryptedExplanationSource?> LoadAsync(Guid queryId, CancellationToken cancellationToken);
}
