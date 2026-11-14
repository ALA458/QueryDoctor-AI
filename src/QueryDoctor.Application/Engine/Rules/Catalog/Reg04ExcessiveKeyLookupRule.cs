using System.Globalization;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.Application.Engine.Rules.Catalog;

/// <summary>
/// Regla REG-04: Búsqueda por clave excesiva — contextual (RF-MOT-06, CA-HU05.2, Cuadro 4.5, Cuadro 4.11).
/// Señala un operador <c>Key Lookup</c> (<c>&lt;IndexScan Lookup="1"&gt;</c>) o <c>RID Lookup</c> únicamente cuando:
/// 1) su número estimado de ejecuciones alcanza o supera el umbral configurable (<see cref="RuleThresholds.Reg04MinEstimatedExecutions"/>), y
/// 2) la consulta alcanza o supera el porcentaje mínimo de consumo en la ventana (<see cref="RuleThresholds.MinQueryConsumptionPct"/>).
/// </summary>
public sealed class Reg04ExcessiveKeyLookupRule : IRule
{
    public const string RuleId = "REG-04";
    public const double HighSeverityExecutionMultiplier = 10.0;

    private static readonly RuleDescriptor CanonicalDescriptor =
        VersionedRuleCatalog.BuiltInDescriptors.First(d => d.RuleId == RuleId);

    public RuleDescriptor Descriptor => CanonicalDescriptor;

    public IReadOnlyList<RuleFindingDraft> Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Condición contextual de ventana (RF-MOT-06, Opción A aprobada):
        // la consulta debe alcanzar o superar el porcentaje mínimo de consumo en la ventana.
        if (!context.QueryConsumptionPct.HasValue ||
            context.QueryConsumptionPct.Value < context.Thresholds.MinQueryConsumptionPct)
        {
            return [];
        }

        var minExecutionsThreshold = context.Thresholds.Reg04MinEstimatedExecutions;
        var minConsumptionThreshold = context.Thresholds.MinQueryConsumptionPct;
        var drafts = new List<RuleFindingDraft>();

        foreach (var statement in context.Plan.Statements)
        {
            foreach (var op in statement.EnumerateOperators())
            {
                if (!IsLookupOperator(op))
                {
                    continue;
                }

                var estimatedExecutions = ComputeEstimatedExecutions(op);
                if (estimatedExecutions < minExecutionsThreshold)
                {
                    continue;
                }

                var severity = ResolveSeverity(estimatedExecutions, minExecutionsThreshold);
                var lookupKind = IsRidLookup(op) ? "RID Lookup" : "Key Lookup";
                var outputColumns = string.Join(
                    ", ",
                    op.OutputColumns
                        .Select(c => c.Column)
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct(StringComparer.OrdinalIgnoreCase));

                var evidence = new SortedDictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["estimate_rebinds"] = op.EstimateRebinds.ToString(CultureInfo.InvariantCulture),
                    ["estimate_rewinds"] = op.EstimateRewinds.ToString(CultureInfo.InvariantCulture),
                    ["estimated_executions"] = estimatedExecutions.ToString(CultureInfo.InvariantCulture),
                    ["estimated_subtree_cost"] = op.EstimatedTotalSubtreeCost.ToString(CultureInfo.InvariantCulture),
                    ["lookup_kind"] = lookupKind,
                    ["min_estimated_executions_threshold"] = minExecutionsThreshold.ToString(CultureInfo.InvariantCulture),
                    ["min_query_consumption_pct_threshold"] = minConsumptionThreshold.ToString(CultureInfo.InvariantCulture),
                };

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

                if (!string.IsNullOrWhiteSpace(outputColumns))
                {
                    evidence["output_columns"] = outputColumns;
                }

                var findingContext = new FindingContextContract(
                    Executions: context.QueryExecutionCount,
                    QueryConsumptionPct: context.QueryConsumptionPct,
                    EstimatedRows: op.EstimateRows,
                    EstimatedRowsRead: op.EstimatedRowsRead,
                    AvgRowsReturned: context.AvgRowsReturned);

                var discriminator = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{statement.StatementId}|{op.NodeId}|{lookupKind}|{op.TargetObject?.Table ?? string.Empty}|{op.TargetObject?.Index ?? string.Empty}");

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

    /// <summary>
    /// Calcula el número estimado de ejecuciones de un operador Lookup en un plan compilado de SQL Server.
    /// En el lado interno de un Nested Loops, la primera iteración más <c>EstimateRebinds + EstimateRewinds</c>
    /// determina cuántas veces se estima que se ejecutará la búsqueda por clave.
    /// </summary>
    public static double ComputeEstimatedExecutions(PlanOperatorNode op)
    {
        ArgumentNullException.ThrowIfNull(op);

        var rebindsAndRewinds = Math.Max(0.0, op.EstimateRebinds) + Math.Max(0.0, op.EstimateRewinds);
        if (rebindsAndRewinds > 0.0)
        {
            return 1.0 + rebindsAndRewinds;
        }

        return Math.Max(1.0, op.EstimateRows);
    }

    private static bool IsLookupOperator(PlanOperatorNode op) =>
        op.IsLookup ||
        op.PhysicalOp.Contains("Key Lookup", StringComparison.OrdinalIgnoreCase) ||
        op.PhysicalOp.Contains("RID Lookup", StringComparison.OrdinalIgnoreCase) ||
        op.LogicalOp.Contains("Key Lookup", StringComparison.OrdinalIgnoreCase) ||
        op.LogicalOp.Contains("RID Lookup", StringComparison.OrdinalIgnoreCase);

    private static bool IsRidLookup(PlanOperatorNode op) =>
        op.PhysicalOp.Contains("RID", StringComparison.OrdinalIgnoreCase) ||
        op.LogicalOp.Contains("RID", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(op.TargetObject?.IndexKind, "Heap", StringComparison.OrdinalIgnoreCase);

    private static FindingSeverity ResolveSeverity(double estimatedExecutions, double threshold) =>
        threshold > 0.0 && estimatedExecutions >= threshold * HighSeverityExecutionMultiplier
            ? FindingSeverity.Alta
            : FindingSeverity.Media;
}
