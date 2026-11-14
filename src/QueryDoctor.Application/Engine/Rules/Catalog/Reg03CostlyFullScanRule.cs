using System.Globalization;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.Application.Engine.Rules.Catalog;

/// <summary>
/// Regla REG-03: Lectura completa costosa — contextual (RF-MOT-05 v1.1, CA-HU05.4, Cuadro 4.5, Cuadro 4.11).
/// Señala un recorrido completo de tabla o de índice (<c>Table Scan</c>, <c>Clustered Index Scan</c> e <c>Index Scan</c>)
/// únicamente cuando se cumple alguna de estas condiciones:
/// (a) el operador tiene predicado y la razón entre filas leídas estimadas y filas devueltas estimadas supera
///     el umbral configurable (<see cref="RuleThresholds.Reg03RowsReadToReturnedRatio"/>);
/// (b) el volumen de filas estimadas del recorrido alcanza o supera <see cref="RuleThresholds.Reg03MinEstimatedRows"/> y
///     la consulta alcanza o supera <see cref="RuleThresholds.MinQueryConsumptionPct"/> en la ventana.
/// Un recorrido completo sin predicado y sobre una tabla de bajo volumen no se señala.
/// </summary>
public sealed class Reg03CostlyFullScanRule : IRule
{
    public const string RuleId = "REG-03";
    public const double HighSeverityVolumeMultiplier = 10.0;

    private static RuleDescriptor? _canonicalDescriptor;

    public RuleDescriptor Descriptor =>
        _canonicalDescriptor ??= VersionedRuleCatalog.BuiltInDescriptors.First(d => d.RuleId == RuleId);

    public IReadOnlyList<RuleFindingDraft> Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var minRowsThreshold = context.Thresholds.Reg03MinEstimatedRows;
        var ratioThreshold = context.Thresholds.Reg03RowsReadToReturnedRatio;
        var minConsumptionThreshold = context.Thresholds.MinQueryConsumptionPct;
        var hasRequiredConsumption =
            context.QueryConsumptionPct.HasValue &&
            context.QueryConsumptionPct.Value >= minConsumptionThreshold;

        var drafts = new List<RuleFindingDraft>();

        foreach (var statement in context.Plan.Statements)
        {
            foreach (var op in statement.EnumerateOperators())
            {
                if (!IsFullScanOperator(op))
                {
                    continue;
                }

                var hasPredicate = !string.IsNullOrWhiteSpace(op.PredicateScalarString);
                var effectiveRowsRead = op.EstimatedRowsRead ?? op.TableCardinality;
                var effectiveRowsReturned = Math.Max(1.0, op.EstimateRows);
                double? rowsReadToReturnedRatio = effectiveRowsRead.HasValue
                    ? effectiveRowsRead.Value / effectiveRowsReturned
                    : null;

                // Volumen real del recorrido: prioriza las filas leídas (EstimatedRowsRead o TableCardinality)
                // frente a las filas devueltas tras el filtro (EstimateRows).
                var volumeRows = Math.Max(effectiveRowsRead ?? 0.0, op.EstimateRows);

                var matchedByPredicateRatio =
                    hasPredicate &&
                    rowsReadToReturnedRatio.HasValue &&
                    rowsReadToReturnedRatio.Value > ratioThreshold;

                var matchedByVolumeAndConsumption =
                    volumeRows >= minRowsThreshold &&
                    hasRequiredConsumption;

                if (!matchedByPredicateRatio && !matchedByVolumeAndConsumption)
                {
                    continue;
                }

                var severity = ResolveSeverity(volumeRows, minRowsThreshold);
                var triggeredBy = matchedByPredicateRatio && matchedByVolumeAndConsumption
                    ? "predicate_ratio_and_volume"
                    : matchedByPredicateRatio
                        ? "predicate_ratio"
                        : "volume_and_consumption";

                var evidence = new SortedDictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["estimated_rows"] = op.EstimateRows.ToString("G", CultureInfo.InvariantCulture),
                    ["estimated_subtree_cost"] = op.EstimatedTotalSubtreeCost.ToString("G", CultureInfo.InvariantCulture),
                    ["has_predicate"] = hasPredicate ? "true" : "false",
                    ["logical_op"] = op.LogicalOp,
                    ["min_estimated_rows_threshold"] = minRowsThreshold.ToString("G", CultureInfo.InvariantCulture),
                    ["min_query_consumption_pct_threshold"] = minConsumptionThreshold.ToString("G", CultureInfo.InvariantCulture),
                    ["physical_op"] = op.PhysicalOp,
                    ["rows_read_to_returned_ratio_threshold"] = ratioThreshold.ToString("G", CultureInfo.InvariantCulture),
                    ["triggered_by"] = triggeredBy,
                    ["volume_rows_evaluated"] = volumeRows.ToString("G", CultureInfo.InvariantCulture),
                };

                if (effectiveRowsRead.HasValue)
                {
                    evidence["estimated_rows_read"] = effectiveRowsRead.Value.ToString("G", CultureInfo.InvariantCulture);
                }

                if (op.TableCardinality.HasValue)
                {
                    evidence["table_cardinality"] = op.TableCardinality.Value.ToString("G", CultureInfo.InvariantCulture);
                }

                if (rowsReadToReturnedRatio.HasValue)
                {
                    evidence["rows_read_to_returned_ratio"] = Math.Round(rowsReadToReturnedRatio.Value, 4)
                        .ToString("G", CultureInfo.InvariantCulture);
                }

                if (hasPredicate)
                {
                    evidence["predicate"] = op.PredicateScalarString;
                }

                if (!string.IsNullOrWhiteSpace(op.TargetObject?.Database))
                {
                    evidence["database"] = op.TargetObject.Database;
                }

                if (!string.IsNullOrWhiteSpace(op.TargetObject?.Schema))
                {
                    evidence["schema"] = op.TargetObject.Schema;
                }

                if (!string.IsNullOrWhiteSpace(op.TargetObject?.Table))
                {
                    evidence["table"] = op.TargetObject.Table;
                }

                if (!string.IsNullOrWhiteSpace(op.TargetObject?.Index))
                {
                    evidence["index"] = op.TargetObject.Index;
                }

                var findingContext = new FindingContextContract(
                    Executions: context.QueryExecutionCount,
                    QueryConsumptionPct: context.QueryConsumptionPct,
                    EstimatedRows: op.EstimateRows,
                    EstimatedRowsRead: effectiveRowsRead,
                    AvgRowsReturned: context.AvgRowsReturned);

                var discriminator = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{statement.StatementId}|{op.NodeId}|{op.PhysicalOp}|{op.TargetObject?.Table ?? string.Empty}|{op.TargetObject?.Index ?? string.Empty}");

                drafts.Add(new RuleFindingDraft(
                    Severity: severity,
                    Nodes: [new FindingNodeContract(op.NodeId, op.PhysicalOp)],
                    Evidence: evidence,
                    Context: findingContext,
                    Kind: FindingKind.Senal,
                    Discriminator: discriminator));
            }
        }

        return drafts;
    }

    public static bool IsFullScanOperator(PlanOperatorNode op)
    {
        ArgumentNullException.ThrowIfNull(op);

        if (op.IsLookup)
        {
            return false;
        }

        return IsScanOperatorName(op.PhysicalOp) || IsScanOperatorName(op.LogicalOp);
    }

    private static bool IsScanOperatorName(string? operatorName) =>
        string.Equals(operatorName, "Table Scan", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(operatorName, "Clustered Index Scan", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(operatorName, "Index Scan", StringComparison.OrdinalIgnoreCase);

    private static FindingSeverity ResolveSeverity(double volumeRows, double minRowsThreshold) =>
        minRowsThreshold > 0.0 && volumeRows >= minRowsThreshold * HighSeverityVolumeMultiplier
            ? FindingSeverity.Alta
            : FindingSeverity.Media;
}
