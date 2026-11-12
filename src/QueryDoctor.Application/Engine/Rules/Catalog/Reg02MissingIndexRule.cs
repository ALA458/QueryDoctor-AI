using System.Globalization;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.Application.Engine.Rules.Catalog;

/// <summary>
/// Regla REG-02: Índice faltante (RF-MOT-07, CA-HU06.1, CA-HU06.2, CA-HU06.3, RNF-FUN-04, Cuadro 4.11).
/// Decodifica cada sugerencia de <c>MissingIndexes</c> del plan (tabla, columnas de igualdad, desigualdad e incluidas,
/// e impacto estimado) y adjunta obligatoriamente la advertencia sobre el costo de escritura y los índices existentes.
/// Nunca genera ni emite sentencias DDL <c>CREATE INDEX</c> listas para ejecutar.
/// </summary>
public sealed class Reg02MissingIndexRule : IRule
{
    public const string RuleId = "REG-02";

    /// <summary>
    /// Advertencia obligatoria exigida por RF-MOT-07, CA-HU06.3 y RNF-FUN-04 para toda sugerencia de índice faltante.
    /// </summary>
    public const string WriteCostAndExistingIndexesWarning =
        "La sugerencia del optimizador no considera todos los índices existentes ni el costo de escritura (INSERT, UPDATE, DELETE) y mantenimiento; debe evaluarse manualmente por un DBA antes de aplicarse.";

    public const double HighImpactThresholdPct = 80.0;
    public const double MediumImpactThresholdPct = 30.0;

    private static readonly RuleDescriptor CanonicalDescriptor =
        VersionedRuleCatalog.BuiltInDescriptors.First(d => d.RuleId == RuleId);

    public RuleDescriptor Descriptor => CanonicalDescriptor;

    public IReadOnlyList<RuleFindingDraft> Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var drafts = new List<RuleFindingDraft>();

        foreach (var statement in context.Plan.Statements)
        {
            if (statement.MissingIndexGroups.Count == 0)
            {
                continue;
            }

            var operators = statement.EnumerateOperators().ToArray();

            foreach (var group in statement.MissingIndexGroups)
            {
                var severity = ResolveSeverityByImpact(group.Impact);
                var impactFormatted = group.Impact.ToString(CultureInfo.InvariantCulture);

                foreach (var suggestion in group.Indexes)
                {
                    var matchingOperators = ResolveTableAccessOperators(statement, operators, suggestion);
                    var primaryOperator = matchingOperators.Length > 0 ? matchingOperators[0] : null;

                    var nodes = matchingOperators
                        .Select(op => new FindingNodeContract(op.NodeId, op.PhysicalOp))
                        .ToArray();

                    var equalityColumns = string.Join(", ", suggestion.EqualityColumns);
                    var inequalityColumns = string.Join(", ", suggestion.InequalityColumns);
                    var includeColumns = string.Join(", ", suggestion.IncludeColumns);

                    var evidence = new SortedDictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["database"] = suggestion.Database,
                        ["equality_columns"] = equalityColumns,
                        ["impact_pct"] = impactFormatted,
                        ["include_columns"] = includeColumns,
                        ["inequality_columns"] = inequalityColumns,
                        ["schema"] = suggestion.Schema,
                        ["table"] = suggestion.Table,
                        ["write_cost_warning"] = WriteCostAndExistingIndexesWarning,
                    };

                    var findingContext = new FindingContextContract(
                        Executions: context.QueryExecutionCount,
                        QueryConsumptionPct: context.QueryConsumptionPct,
                        EstimatedRows: primaryOperator?.EstimateRows ?? statement.StatementEstRows,
                        EstimatedRowsRead: primaryOperator?.EstimatedRowsRead,
                        AvgRowsReturned: context.AvgRowsReturned);

                    var discriminator = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{statement.StatementId}|{suggestion.Database}.{suggestion.Schema}.{suggestion.Table}|EQ:{equalityColumns}|INEQ:{inequalityColumns}|INC:{includeColumns}");

                    drafts.Add(new RuleFindingDraft(
                        Severity: severity,
                        Nodes: nodes,
                        Evidence: evidence,
                        Context: findingContext,
                        Kind: FindingKind.Senal,
                        Discriminator: discriminator));
                }
            }
        }

        return drafts;
    }

    private static FindingSeverity ResolveSeverityByImpact(double impactPct)
    {
        if (impactPct >= HighImpactThresholdPct)
        {
            return FindingSeverity.Alta;
        }

        if (impactPct >= MediumImpactThresholdPct)
        {
            return FindingSeverity.Media;
        }

        return FindingSeverity.Baja;
    }

    private static PlanOperatorNode[] ResolveTableAccessOperators(
        PlanStatement statement,
        IReadOnlyList<PlanOperatorNode> operators,
        MissingIndexSuggestion suggestion)
    {
        var tableOperators = operators
            .Where(op => MatchesTable(op.TargetObject, suggestion))
            .ToArray();

        if (tableOperators.Length == 0)
        {
            return statement.RootOperator is not null ? [statement.RootOperator] : [];
        }

        var primaryScansOrSeeks = tableOperators
            .Where(op => !op.IsLookup)
            .ToArray();

        return primaryScansOrSeeks.Length > 0 ? primaryScansOrSeeks : tableOperators;
    }

    private static bool MatchesTable(PlanObjectReference? targetObject, MissingIndexSuggestion suggestion)
    {
        if (targetObject is null || string.IsNullOrWhiteSpace(targetObject.Table))
        {
            return false;
        }

        if (!string.Equals(targetObject.Table, suggestion.Table, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(suggestion.Schema) &&
            !string.IsNullOrWhiteSpace(targetObject.Schema) &&
            !string.Equals(targetObject.Schema, suggestion.Schema, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(suggestion.Database) &&
            !string.IsNullOrWhiteSpace(targetObject.Database) &&
            !string.Equals(targetObject.Database, suggestion.Database, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
