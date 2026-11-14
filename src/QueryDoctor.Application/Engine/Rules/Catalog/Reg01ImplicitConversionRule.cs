using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.Application.Engine.Rules.Catalog;

/// <summary>
/// Regla REG-01: Conversión implícita que afecta el plan (RF-MOT-04, CA-HU05.1, Cuadro 4.11, Apéndice B Listing B.1).
/// Detecta las advertencias <c>PlanAffectingConvert</c> del plan compilado y reporta el tipo de problema (<c>ConvertIssue</c>),
/// la expresión afectada y el nodo del plan involucrado.
/// </summary>
public sealed class Reg01ImplicitConversionRule : IRule
{
    public const string RuleId = "REG-01";

    private static readonly RuleDescriptor CanonicalDescriptor =
        VersionedRuleCatalog.BuiltInDescriptors.First(d => d.RuleId == RuleId);

    public RuleDescriptor Descriptor => CanonicalDescriptor;

    public IReadOnlyList<RuleFindingDraft> Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var drafts = new List<RuleFindingDraft>();

        foreach (var statement in context.Plan.Statements)
        {
            var operators = statement.EnumerateOperators().ToArray();

            var warnings = new List<(PlanWarning Warning, PlanOperatorNode? OwnerNode)>();
            foreach (var warning in statement.Warnings.Where(w => w.Kind == PlanWarningKind.PlanAffectingConvert))
            {
                warnings.Add((warning, null));
            }

            foreach (var op in operators)
            {
                foreach (var warning in op.Warnings.Where(w => w.Kind == PlanWarningKind.PlanAffectingConvert))
                {
                    warnings.Add((warning, op));
                }
            }

            foreach (var (warning, ownerNode) in warnings)
            {
                var matchingOperators = ResolveMatchingOperators(statement, operators, warning, ownerNode);
                var primaryOperator = matchingOperators.Length > 0 ? matchingOperators[0] : null;
                var primaryConversion = ResolveMatchingConversion(primaryOperator, warning.Expression);

                // Cuadro 4.11 del SRS: "Alta si impide una búsqueda directa; media en otro caso"
                var severity = IsSeekPlanIssue(warning.ConvertIssue)
                    ? FindingSeverity.Alta
                    : FindingSeverity.Media;

                var nodes = matchingOperators
                    .Select(op => new FindingNodeContract(op.NodeId, op.PhysicalOp))
                    .ToArray();

                var evidence = new SortedDictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["convert_issue"] = warning.ConvertIssue ?? "Unknown",
                    ["expression"] = warning.Expression ?? string.Empty,
                };

                if (!string.IsNullOrWhiteSpace(primaryConversion?.TargetDataType))
                {
                    evidence["target_data_type"] = primaryConversion.TargetDataType;
                }

                if (!string.IsNullOrWhiteSpace(primaryConversion?.Column?.Column))
                {
                    evidence["column"] = primaryConversion.Column.Column;
                }

                if (!string.IsNullOrWhiteSpace(primaryOperator?.TargetObject?.Table))
                {
                    evidence["table"] = primaryOperator.TargetObject.Table;
                }

                if (!string.IsNullOrWhiteSpace(primaryOperator?.TargetObject?.Index))
                {
                    evidence["index"] = primaryOperator.TargetObject.Index;
                }

                var findingContext = new FindingContextContract(
                    Executions: context.QueryExecutionCount,
                    QueryConsumptionPct: context.QueryConsumptionPct,
                    EstimatedRows: primaryOperator?.EstimateRows ?? statement.StatementEstRows,
                    EstimatedRowsRead: primaryOperator?.EstimatedRowsRead,
                    AvgRowsReturned: context.AvgRowsReturned);

                var discriminator = $"{statement.StatementId}|{warning.ConvertIssue ?? string.Empty}|{warning.Expression ?? string.Empty}";

                drafts.Add(new RuleFindingDraft(
                    Severity: severity,
                    Nodes: nodes,
                    Evidence: evidence,
                    Context: findingContext,
                    Kind: FindingKind.Senal,
                    Discriminator: discriminator));
            }
        }

        return drafts;
    }

    private static bool IsSeekPlanIssue(string? convertIssue) =>
        !string.IsNullOrWhiteSpace(convertIssue) &&
        convertIssue.Contains("Seek Plan", StringComparison.OrdinalIgnoreCase);

    private static PlanOperatorNode[] ResolveMatchingOperators(
        PlanStatement statement,
        IReadOnlyList<PlanOperatorNode> operators,
        PlanWarning warning,
        PlanOperatorNode? ownerNode)
    {
        if (ownerNode is not null)
        {
            return [ownerNode];
        }

        var expression = warning.Expression;
        if (!string.IsNullOrWhiteSpace(expression))
        {
            var matchedByExpression = operators
                .Where(op => MatchesExpression(op, expression))
                .ToArray();

            if (matchedByExpression.Length > 0)
            {
                return matchedByExpression;
            }
        }

        var operatorsWithImplicitConvert = operators
            .Where(op =>
                op.ImplicitConversions.Count > 0 ||
                (op.PredicateScalarString?.Contains("CONVERT_IMPLICIT", StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();

        if (operatorsWithImplicitConvert.Length > 0)
        {
            return operatorsWithImplicitConvert;
        }

        return statement.RootOperator is not null ? [statement.RootOperator] : [];
    }

    private static bool MatchesExpression(PlanOperatorNode op, string warningExpression)
    {
        if (!string.IsNullOrWhiteSpace(op.PredicateScalarString) &&
            (op.PredicateScalarString.Contains(warningExpression, StringComparison.OrdinalIgnoreCase) ||
             warningExpression.Contains(op.PredicateScalarString, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var conversion in op.ImplicitConversions)
        {
            if (!string.IsNullOrWhiteSpace(conversion.ScalarString) &&
                warningExpression.Contains(conversion.ScalarString, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(conversion.Column?.Column) &&
                warningExpression.Contains(conversion.Column.Column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ImplicitConversionInfo? ResolveMatchingConversion(
        PlanOperatorNode? op,
        string? warningExpression)
    {
        if (op is null || op.ImplicitConversions.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(warningExpression))
        {
            var matched = op.ImplicitConversions.FirstOrDefault(c =>
                (!string.IsNullOrWhiteSpace(c.Column?.Column) &&
                 warningExpression.Contains(c.Column.Column, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(c.ScalarString) &&
                 warningExpression.Contains(c.ScalarString, StringComparison.OrdinalIgnoreCase)));

            if (matched is not null)
            {
                return matched;
            }
        }

        return op.ImplicitConversions[0];
    }
}
