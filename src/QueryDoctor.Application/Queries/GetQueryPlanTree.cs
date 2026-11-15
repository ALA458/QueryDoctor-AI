using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Masking;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.Application.Queries;

/// <summary>
/// Caso de uso para obtener el visor jerárquico seguro de un plan con nodos asociados a hallazgos resaltados
/// (<c>GET /queries/{queryId}/plans/{planId}</c>; S2-10, RF-DASH-04, CA-HU13.1, CA-HU13.2).
/// Enmascara el Showplan XML con <see cref="IMasker"/> antes de parsearlo (RNF-SEG-05, fallo cerrado).
/// </summary>
public sealed class GetQueryPlanTree
{
    private readonly IQuerySourceReader _reader;
    private readonly IMasker _masker;
    private readonly IPlanParser _planParser;
    private readonly IRuleCatalog _catalog;

    public GetQueryPlanTree(
        IQuerySourceReader reader,
        IMasker masker,
        IPlanParser planParser,
        IRuleCatalog catalog)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _masker = masker ?? throw new ArgumentNullException(nameof(masker));
        _planParser = planParser ?? throw new ArgumentNullException(nameof(planParser));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<GetQueryPlanTreeResult> ExecuteAsync(
        Guid queryId,
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        var source = await _reader.GetPlanTreeAsync(queryId, planId, cancellationToken);
        if (source is null)
        {
            return new GetQueryPlanTreeResult.NotFound();
        }

        if (source.DecryptedPlanXml is null)
        {
            return new GetQueryPlanTreeResult.Found(BuildEmptyView(
                source,
                schemaVersion: null,
                planExpired: true,
                planMaskingFailure: null,
                parseFailureReason: null));
        }

        // Fallo cerrado (D6 / RNF-SEG-05): primero se enmascaran los literales del Showplan XML.
        var masked = _masker.Mask(new MaskingInput(SqlText: null, PlanXml: source.DecryptedPlanXml));
        if (!masked.Plan.IsSendable || string.IsNullOrWhiteSpace(masked.Plan.Text))
        {
            return new GetQueryPlanTreeResult.Found(BuildEmptyView(
                source,
                schemaVersion: null,
                planExpired: false,
                planMaskingFailure: masked.Plan.FailureCause?.ToString() ?? nameof(MaskingFailureCause.InputMissing),
                parseFailureReason: null));
        }

        var parseResult = _planParser.Parse(
            masked.Plan.Text,
            source.PlanId.ToString("D"),
            source.EngineVersion);

        if (!parseResult.IsSuccess || parseResult.Plan is null)
        {
            return new GetQueryPlanTreeResult.Found(BuildEmptyView(
                source,
                schemaVersion: null,
                planExpired: false,
                planMaskingFailure: null,
                parseFailureReason: parseResult.Finding?.Reason.ToString() ?? nameof(PlanParseFailureReason.MalformedXml)));
        }

        var parsedPlan = parseResult.Plan;
        var findingsByNodeId = BuildFindingsByNodeIndex(source.Findings);
        var statements = new List<QueryPlanStatementItem>(parsedPlan.Statements.Count);
        var totalOperators = 0;
        var highlightedOperators = 0;

        foreach (var statement in parsedPlan.Statements)
        {
            var operators = FlattenOperatorsPreorder(statement, findingsByNodeId, out var highlightedInStmt);
            totalOperators += operators.Count;
            highlightedOperators += highlightedInStmt;

            statements.Add(new QueryPlanStatementItem(
                StatementId: statement.StatementId,
                StatementType: statement.StatementType,
                StatementText: statement.StatementText,
                StatementSubTreeCost: statement.StatementSubTreeCost,
                StatementEstRows: statement.StatementEstRows,
                StatementOptmLevel: statement.StatementOptmLevel,
                StatementOptmEarlyAbortReason: statement.StatementOptmEarlyAbortReason,
                CardinalityEstimationModelVersion: statement.CardinalityEstimationModelVersion,
                IsDispatcherPlan: statement.IsDispatcherPlan,
                Warnings: MapWarnings(statement.Warnings),
                MissingIndexes: MapMissingIndexes(statement.MissingIndexGroups),
                Parameters: MapParameters(statement.Parameters),
                MemoryGrant: statement.MemoryGrant,
                Operators: operators));
        }

        return new GetQueryPlanTreeResult.Found(new QueryPlanTreeView(
            QueryId: source.QueryId,
            PlanId: source.PlanId,
            QueryStorePlanId: source.QueryStorePlanId,
            QueryPlanHash: source.QueryPlanHash,
            EngineVersion: parsedPlan.EngineVersion ?? source.EngineVersion,
            SchemaVersion: parsedPlan.SchemaVersion,
            AnalysisStatus: source.AnalysisStatus,
            NotAnalyzableCause: source.NotAnalyzableCause,
            PlanExpired: false,
            PlanMaskingFailure: null,
            ParseFailureReason: null,
            TotalOperators: totalOperators,
            HighlightedOperatorsCount: highlightedOperators,
            Statements: statements));
    }

    private static QueryPlanTreeView BuildEmptyView(
        QueryPlanTreeSource source,
        string? schemaVersion,
        bool planExpired,
        string? planMaskingFailure,
        string? parseFailureReason) =>
        new(
            QueryId: source.QueryId,
            PlanId: source.PlanId,
            QueryStorePlanId: source.QueryStorePlanId,
            QueryPlanHash: source.QueryPlanHash,
            EngineVersion: source.EngineVersion,
            SchemaVersion: schemaVersion,
            AnalysisStatus: source.AnalysisStatus,
            NotAnalyzableCause: source.NotAnalyzableCause,
            PlanExpired: planExpired,
            PlanMaskingFailure: planMaskingFailure,
            ParseFailureReason: parseFailureReason,
            TotalOperators: 0,
            HighlightedOperatorsCount: 0,
            Statements: []);

    private Dictionary<int, List<(FindingSeverity Severity, QueryPlanNodeFindingRef Ref)>> BuildFindingsByNodeIndex(
        IReadOnlyList<QueryFindingSource> findings)
    {
        var map = new Dictionary<int, List<(FindingSeverity, QueryPlanNodeFindingRef)>>();

        foreach (var finding in findings)
        {
            var nodes = QueryMaskingProjection.ParseNodes(finding.NodesJson);
            if (nodes.Count == 0)
            {
                continue;
            }

            var ruleName = _catalog.TryGetDescriptor(finding.RuleId)?.Name ?? finding.RuleId;
            var refItem = new QueryPlanNodeFindingRef(
                FindingId: finding.Id,
                FindingCode: finding.FindingCode,
                RuleId: finding.RuleId,
                RuleName: ruleName,
                Severity: finding.Severity.ToContractCode(),
                Kind: finding.Kind.ToContractCode());

            foreach (var nodeId in nodes.Select(n => n.NodeId).Distinct())
            {
                if (!map.TryGetValue(nodeId, out var list))
                {
                    list = [];
                    map[nodeId] = list;
                }

                list.Add((finding.Severity, refItem));
            }
        }

        return map;
    }

    private static List<QueryPlanOperatorItem> FlattenOperatorsPreorder(
        PlanStatement statement,
        Dictionary<int, List<(FindingSeverity Severity, QueryPlanNodeFindingRef Ref)>> findingsByNodeId,
        out int highlightedCount)
    {
        highlightedCount = 0;
        if (statement.RootOperator is null)
        {
            return [];
        }

        var baseCost = ResolveStatementCostBase(statement);
        var result = new List<QueryPlanOperatorItem>();
        var stack = new Stack<(PlanOperatorNode Node, int? ParentNodeId, int Depth, bool IsLastSibling)>();
        stack.Push((statement.RootOperator, null, 0, true));

        while (stack.Count > 0)
        {
            var (node, parentNodeId, depth, isLastSibling) = stack.Pop();

            IReadOnlyList<QueryPlanNodeFindingRef> nodeFindings = [];
            string? maxSeverity = null;
            var hasFindings = false;

            if (findingsByNodeId.TryGetValue(node.NodeId, out var entries) && entries.Count > 0)
            {
                hasFindings = true;
                highlightedCount++;
                var ordered = entries
                    .OrderByDescending(e => e.Severity)
                    .ThenBy(e => e.Ref.FindingCode, StringComparer.Ordinal)
                    .ToArray();

                maxSeverity = ordered[0].Severity.ToContractCode();
                nodeFindings = ordered.Select(e => e.Ref).ToArray();
            }

            double? costSharePct = baseCost > 0.0
                ? Math.Round(
                    Math.Clamp((node.EstimatedTotalSubtreeCost / baseCost) * 100.0, 0.0, 100.0),
                    1,
                    MidpointRounding.AwayFromZero)
                : null;

            result.Add(new QueryPlanOperatorItem(
                NodeId: node.NodeId,
                ParentNodeId: parentNodeId,
                Depth: depth,
                HasChildren: node.Children.Count > 0,
                IsLastSibling: isLastSibling,
                PhysicalOp: node.PhysicalOp,
                LogicalOp: node.LogicalOp,
                IsParallel: node.IsParallel,
                IsLookup: node.IsLookup,
                EstimatedExecutionMode: node.EstimatedExecutionMode,
                EstimatedTotalSubtreeCost: node.EstimatedTotalSubtreeCost,
                CostSharePct: costSharePct,
                EstimateIo: node.EstimateIO,
                EstimateCpu: node.EstimateCPU,
                EstimateRows: node.EstimateRows,
                EstimatedRowsRead: node.EstimatedRowsRead,
                TableCardinality: node.TableCardinality,
                AvgRowSize: node.AvgRowSize,
                TargetObject: FormatObject(node.TargetObject),
                OutputColumns: node.OutputColumns.Select(FormatColumn).ToArray(),
                SeekColumns: node.SeekColumns.Select(FormatColumn).ToArray(),
                PredicateScalarString: node.PredicateScalarString,
                ImplicitConversions: node.ImplicitConversions
                    .Select(c => new QueryPlanImplicitConversionItem(
                        c.TargetDataType,
                        c.Length,
                        c.ScalarString,
                        c.Column is null ? null : FormatColumn(c.Column)))
                    .ToArray(),
                ConstantLiterals: node.ConstantLiterals,
                Warnings: MapWarnings(node.Warnings),
                Findings: nodeFindings,
                HasFindings: hasFindings,
                MaxFindingSeverity: maxSeverity));

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push((
                    node.Children[i],
                    node.NodeId,
                    depth + 1,
                    i == node.Children.Count - 1));
            }
        }

        return result;
    }

    private static double ResolveStatementCostBase(PlanStatement statement)
    {
        if (statement.StatementSubTreeCost is > 0.0)
        {
            return statement.StatementSubTreeCost.Value;
        }

        if (statement.RootOperator is { EstimatedTotalSubtreeCost: > 0.0 })
        {
            return statement.RootOperator.EstimatedTotalSubtreeCost;
        }

        return 0.0;
    }

    private static List<QueryPlanWarningItem> MapWarnings(IReadOnlyList<PlanWarning> warnings) =>
        warnings.Select(w => new QueryPlanWarningItem(
                Kind: w.Kind.ToString(),
                ConvertIssue: w.ConvertIssue,
                Expression: w.Expression,
                SpillLevel: w.SpillLevel,
                SpilledThreadCount: w.SpilledThreadCount,
                Details: w.Details))
            .ToList();

    private static List<QueryPlanMissingIndexItem> MapMissingIndexes(IReadOnlyList<MissingIndexGroup> groups) =>
        groups.SelectMany(g => g.Indexes.Select(idx => new QueryPlanMissingIndexItem(
                Impact: g.Impact,
                Table: FormatTableName(idx.Database, idx.Schema, idx.Table),
                EqualityColumns: idx.EqualityColumns,
                InequalityColumns: idx.InequalityColumns,
                IncludeColumns: idx.IncludeColumns)))
            .ToList();

    private static List<QueryPlanParameterItem> MapParameters(IReadOnlyList<PlanParameter> parameters) =>
        parameters.Select(p => new QueryPlanParameterItem(p.Name, p.DataType, p.CompiledValue)).ToList();

    private static string? FormatObject(PlanObjectReference? obj)
    {
        if (obj is null)
        {
            return null;
        }

        var parts = new[] { obj.Schema, obj.Table, obj.Index }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        if (parts.Length > 0)
        {
            return string.Join('.', parts);
        }

        return !string.IsNullOrWhiteSpace(obj.Database) ? obj.Database : obj.Alias;
    }

    private static string FormatTableName(string? database, string? schema, string table)
    {
        var parts = new[] { database, schema, table }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        return string.Join('.', parts);
    }

    private static string FormatColumn(PlanColumnReference col)
    {
        var prefixParts = new[] { col.Table ?? col.Alias, col.Column }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        return prefixParts.Length > 0 ? string.Join('.', prefixParts) : col.Column;
    }
}
