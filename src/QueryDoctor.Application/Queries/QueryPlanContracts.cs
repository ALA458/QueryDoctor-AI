using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.Application.Queries;

/// <summary>
/// Referencia ligera a un hallazgo determinístico que afecta a un operador del plan (S2-10, CA-HU13.2).
/// </summary>
public sealed record QueryPlanNodeFindingRef(
    Guid FindingId,
    string FindingCode,
    string RuleId,
    string RuleName,
    string Severity,
    string Kind);

/// <summary>
/// Advertencia asociada al plan, a una sentencia o a un operador individual (CA-HU13.1).
/// </summary>
public sealed record QueryPlanWarningItem(
    string Kind,
    string? ConvertIssue,
    string? Expression,
    int? SpillLevel,
    int? SpilledThreadCount,
    string? Details);

/// <summary>
/// Conversión implícita detectada sobre un operador (con expresión ya enmascarada).
/// </summary>
public sealed record QueryPlanImplicitConversionItem(
    string TargetDataType,
    int? Length,
    string? ScalarString,
    string? Column);

/// <summary>
/// Operador físico/lógico del Showplan XML proyectado en preorden plano (inmune a <c>MaxDepth</c> en planes profundos como F8).
/// </summary>
public sealed record QueryPlanOperatorItem(
    int NodeId,
    int? ParentNodeId,
    int Depth,
    bool HasChildren,
    bool IsLastSibling,
    string PhysicalOp,
    string LogicalOp,
    bool IsParallel,
    bool IsLookup,
    string? EstimatedExecutionMode,
    double EstimatedTotalSubtreeCost,
    double? CostSharePct,
    double EstimateIo,
    double EstimateCpu,
    double EstimateRows,
    double? EstimatedRowsRead,
    double? TableCardinality,
    double AvgRowSize,
    string? TargetObject,
    IReadOnlyList<string> OutputColumns,
    IReadOnlyList<string> SeekColumns,
    string? PredicateScalarString,
    IReadOnlyList<QueryPlanImplicitConversionItem> ImplicitConversions,
    IReadOnlyList<string> ConstantLiterals,
    IReadOnlyList<QueryPlanWarningItem> Warnings,
    IReadOnlyList<QueryPlanNodeFindingRef> Findings,
    bool HasFindings,
    string? MaxFindingSeverity);

/// <summary>
/// Sugerencia de índice faltante detectada por el optimizador en la sentencia.
/// </summary>
public sealed record QueryPlanMissingIndexItem(
    double Impact,
    string Table,
    IReadOnlyList<string> EqualityColumns,
    IReadOnlyList<string> InequalityColumns,
    IReadOnlyList<string> IncludeColumns);

/// <summary>
/// Parámetro compilado del plan (con <see cref="CompiledValue"/> enmascarado).
/// </summary>
public sealed record QueryPlanParameterItem(
    string Name,
    string? DataType,
    string? CompiledValue);

/// <summary>
/// Sentencia individual dentro del Showplan XML junto a su árbol plano de operadores.
/// </summary>
public sealed record QueryPlanStatementItem(
    int StatementId,
    string? StatementType,
    string? StatementText,
    double? StatementSubTreeCost,
    double? StatementEstRows,
    string? StatementOptmLevel,
    string? StatementOptmEarlyAbortReason,
    int? CardinalityEstimationModelVersion,
    bool IsDispatcherPlan,
    IReadOnlyList<QueryPlanWarningItem> Warnings,
    IReadOnlyList<QueryPlanMissingIndexItem> MissingIndexes,
    IReadOnlyList<QueryPlanParameterItem> Parameters,
    MemoryGrantSummary? MemoryGrant,
    IReadOnlyList<QueryPlanOperatorItem> Operators);

/// <summary>
/// Vista jerárquica segura de un plan de ejecución (<c>GET /queries/{queryId}/plans/{planId}</c>; S2-10, RF-DASH-04, HU-13).
/// </summary>
public sealed record QueryPlanTreeView(
    Guid QueryId,
    Guid PlanId,
    long QueryStorePlanId,
    string? QueryPlanHash,
    string? EngineVersion,
    string? SchemaVersion,
    PlanAnalysisStatus AnalysisStatus,
    string? NotAnalyzableCause,
    bool PlanExpired,
    DateTimeOffset? PlanExpiredAt,
    string? PlanMaskingFailure,
    string? ParseFailureReason,
    int TotalOperators,
    int HighlightedOperatorsCount,
    IReadOnlyList<QueryPlanStatementItem> Statements);

/// <summary>Resultado de consultar el árbol jerárquico de un plan.</summary>
public abstract record GetQueryPlanTreeResult
{
    private GetQueryPlanTreeResult()
    {
    }

    public sealed record Found(QueryPlanTreeView View) : GetQueryPlanTreeResult;

    public sealed record NotFound : GetQueryPlanTreeResult;
}
