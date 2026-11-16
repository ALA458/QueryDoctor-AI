using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Queries;

/// <summary>
/// Fuente descifrada temporal en memoria de un hallazgo para proyectarlo y enmascararlo antes de responder al cliente.
/// </summary>
public sealed record QueryFindingSource(
    Guid Id,
    Guid PlanId,
    long QueryStorePlanId,
    string FindingCode,
    string RuleId,
    string RuleVersion,
    string CatalogVersion,
    string SchemaVersion,
    FindingKind Kind,
    FindingSeverity Severity,
    EvidenceClass EvidenceClass,
    string NodesJson,
    string? DecryptedEvidenceJson,
    string ContextJson,
    string ThresholdsUsedJson,
    string? EngineVersion,
    DateTimeOffset AnalyzedAt,
    bool EvidenceExpired = false);

/// <summary>
/// Fila cruda de consulta leída para el listado <c>GET /queries</c> antes del enmascarado de salida.
/// </summary>
public sealed record QueryListSourceRow(
    Guid QueryId,
    Guid ConnectionId,
    long QueryStoreQueryId,
    string Server,
    string DatabaseName,
    string? DecryptedSqlText,
    double ConsumptionPct,
    long Executions,
    double? Score,
    HealthScoreBand? Band,
    IReadOnlyList<QueryFindingSource> Findings,
    DateTimeOffset? SqlPurgedAt = null);

/// <summary>
/// Fuente completa de una consulta leída desde persistencia para proyectar su detalle seguro.
/// </summary>
public sealed record QueryDetailSource(
    Guid QueryId,
    Guid ConnectionId,
    long QueryStoreQueryId,
    string Server,
    string DatabaseName,
    ConnectionPlatform Platform,
    string? QueryHash,
    string? OriginContext,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string? DecryptedSqlText,
    IReadOnlyList<Plan> Plans,
    IReadOnlyList<RuntimeStat> RuntimeStats,
    IReadOnlyList<QueryFindingSource> Findings,
    HealthScore? LatestScore,
    int RankInWindow,
    int TotalQueriesInWindow,
    IReadOnlyList<HealthScore> ScoreHistory,
    DateTimeOffset? SqlPurgedAt = null);

/// <summary>
/// Fuente descifrada temporal en memoria del Showplan XML de un plan y sus hallazgos asociados (S2-10).
/// </summary>
public sealed record QueryPlanTreeSource(
    Guid QueryId,
    Guid PlanId,
    long QueryStorePlanId,
    string? QueryPlanHash,
    string? EngineVersion,
    PlanAnalysisStatus AnalysisStatus,
    string? NotAnalyzableCause,
    DateTimeOffset IngestedAt,
    string? DecryptedPlanXml,
    IReadOnlyList<QueryFindingSource> Findings,
    DateTimeOffset? PurgedAt = null);

/// <summary>
/// Puerto de solo lectura para obtener datos crudos descifrados de consultas, series y puntajes desde persistencia.
/// </summary>
public interface IQuerySourceReader
{
    Task<IReadOnlyList<QueryListSourceRow>> ListAsync(
        Guid? connectionId,
        TimeSpan windowDuration,
        CancellationToken cancellationToken = default);

    Task<QueryDetailSource?> GetDetailAsync(
        Guid queryId,
        TimeSpan windowDuration,
        CancellationToken cancellationToken = default);

    Task<QueryPlanTreeSource?> GetPlanTreeAsync(
        Guid queryId,
        Guid planId,
        CancellationToken cancellationToken = default);
}
