using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Scoring;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Queries;

/// <summary>
/// Ventanas de tiempo soportadas para listado y series temporales de consultas (RF-DASH-01, RF-DASH-03).
/// </summary>
public enum QueryTimeWindow
{
    Last24Hours,
    Last7Days,
    Last30Days,
}

/// <summary>
/// Criterios de ordenamiento soportados en el listado de consultas (RF-SCO-04, CONTRATO_FRONT_CONSULTAS).
/// </summary>
public enum QueryListSort
{
    Consumption,
    Score,
}

/// <summary>
/// Filtros validados para consultar el listado de consultas.
/// </summary>
public sealed record QueryListFilter(
    Guid? ConnectionId,
    string? Window,
    string? Severity,
    string? Sort);

/// <summary>
/// Vista resumida de un hallazgo dentro de la fila del listado <c>GET /queries</c>.
/// </summary>
public sealed record QueryFindingPreview(
    string RuleId,
    string Severity,
    string Summary);

/// <summary>
/// Fila del listado <c>GET /queries</c> conforme a <c>docs/CONTRATO_FRONT_CONSULTAS.md</c> y <c>RF-SCO-04</c>.
/// </summary>
public sealed record QuerySummaryItem(
    Guid Id,
    Guid ConnectionId,
    long QueryStoreQueryId,
    string Server,
    string DatabaseName,
    string SqlPreview,
    bool SqlExpired,
    double ConsumptionPct,
    long Executions,
    double? Score,
    string? Band,
    IReadOnlyList<QueryFindingPreview> Findings);

/// <summary>
/// Plan de ejecución asociado a la consulta (RF-DASH-02, CA-HU07.2).
/// </summary>
public sealed record QueryPlanSummary(
    Guid PlanId,
    long QueryStorePlanId,
    string? QueryPlanHash,
    string? EngineVersion,
    PlanAnalysisStatus AnalysisStatus,
    string? NotAnalyzableCause,
    DateTimeOffset IngestedAt);

/// <summary>
/// Punto de serie temporal agregado por intervalo y plan para duración, CPU y lecturas lógicas (RF-DASH-03, CA-HU07.1, CA-HU07.2).
/// </summary>
public sealed record QueryRuntimeSeriesPoint(
    long IntervalId,
    DateTimeOffset IntervalStart,
    DateTimeOffset IntervalEnd,
    Guid PlanId,
    long QueryStorePlanId,
    string? QueryPlanHash,
    long ExecutionCount,
    double AvgDurationMs,
    double AvgCpuTimeMs,
    double AvgLogicalIoReads,
    double AvgPhysicalIoReads,
    double AvgRowCount);

/// <summary>
/// Hallazgo determinístico detallado con evidencia enmascarada, nodos y contexto (RF-DASH-02, RF-MOT-03).
/// </summary>
public sealed record QueryFindingDetail(
    Guid Id,
    Guid PlanId,
    long QueryStorePlanId,
    string FindingCode,
    string RuleId,
    string RuleName,
    string RuleVersion,
    string CatalogVersion,
    string Kind,
    string Severity,
    string EvidenceClass,
    IReadOnlyList<FindingNodeContract> Nodes,
    IReadOnlyDictionary<string, string> Evidence,
    bool EvidenceMasked,
    string? EvidenceMaskingFailure,
    FindingContextContract Context,
    IReadOnlyDictionary<string, double> ThresholdsUsed,
    string? EngineVersion,
    DateTimeOffset AnalyzedAt);

/// <summary>
/// Registro histórico de puntaje para mostrar su evolución entre ingestas (RF-SCO-05).
/// </summary>
public sealed record QueryScoreHistoryItem(
    DateTimeOffset CalculatedAt,
    double Score,
    double Priority,
    string Band,
    double ImpactComponent,
    double SeverityComponent,
    double WeightedConsumptionPct);

/// <summary>
/// Desglose completo e interpretación relativa del Query Health Score (RF-SCO-01..06, CA-HU08.1, CA-HU08.3).
/// </summary>
public sealed record QueryHealthScoreDetail(
    double Score,
    double Priority,
    string Band,
    string BandLabel,
    int RankInWindow,
    int TotalQueriesInWindow,
    string ScaleDirection,
    string UsageDisclaimer,
    double ImpactComponent,
    double SeverityComponent,
    double AlphaUsed,
    double CpuShare,
    double DurationShare,
    double LogicalReadsShare,
    double WeightedConsumptionPct,
    long TotalExecutions,
    double TotalCpuMicroseconds,
    double TotalDurationMicroseconds,
    double TotalLogicalReads,
    int FindingsCount,
    IReadOnlyList<FindingSeverityContribution> ContributingFindings,
    HealthScoreParameters ParametersUsed,
    DateTimeOffset CalculatedAt,
    IReadOnlyList<QueryScoreHistoryItem> History);

/// <summary>
/// Detalle completo de una consulta (<c>GET /queries/{id}</c>; S2-09, RF-DASH-02, RF-DASH-03, RF-SCO-06).
/// </summary>
public sealed record QueryDetailView(
    Guid Id,
    Guid ConnectionId,
    long QueryStoreQueryId,
    string Server,
    string DatabaseName,
    ConnectionPlatform Platform,
    string? QueryHash,
    string? OriginContext,
    string Window,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string? SqlText,
    bool SqlExpired,
    string? SqlMaskingFailure,
    QueryHealthScoreDetail? Score,
    IReadOnlyList<QueryPlanSummary> Plans,
    IReadOnlyList<QueryRuntimeSeriesPoint> Series,
    IReadOnlyList<QueryFindingDetail> Findings);

/// <summary>Resultado de consultar el listado de consultas.</summary>
public abstract record GetQueriesResult
{
    private GetQueriesResult()
    {
    }

    public sealed record Ok(IReadOnlyList<QuerySummaryItem> Items) : GetQueriesResult;

    public sealed record Invalid(IDictionary<string, string[]> Errors) : GetQueriesResult;
}

/// <summary>Resultado de consultar el detalle de una consulta.</summary>
public abstract record GetQueryDetailResult
{
    private GetQueryDetailResult()
    {
    }

    public sealed record Found(QueryDetailView View) : GetQueryDetailResult;

    public sealed record NotFound : GetQueryDetailResult;

    public sealed record Invalid(IDictionary<string, string[]> Errors) : GetQueryDetailResult;
}
