using System.Globalization;
using System.Text.Json;
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Masking;
using QueryDoctor.Application.Scoring;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.EngineHarness;

/// <summary>
/// Núcleo puro y determinista del arnés de evaluación del motor (S2-14, RF-EVA-01, RNF-FUN-01).
/// </summary>
public sealed class EngineHarnessRunner
{
    public const double TargetPrecision = 0.90;
    public const double TargetRecall = 0.80;

    public static readonly IReadOnlyList<string> EvaluatedRuleIds = ["REG-01", "REG-02", "REG-03", "REG-04"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IPlanParser _parser;
    private readonly IRuleCatalog _catalog;
    private readonly IRuleEngine _engine;

    public EngineHarnessRunner()
    {
        _parser = new ShowplanXmlParser();
        _catalog = new VersionedRuleCatalog();
        _engine = new DeterministicRuleEngine(_parser, _catalog);
    }

    public EngineHarnessRunner(IRuleCatalog catalog, IRuleEngine engine, IPlanParser? parser = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(engine);
        _parser = parser ?? new ShowplanXmlParser();
        _catalog = catalog;
        _engine = engine;
    }

    public static GroundTruthManifest LoadManifest(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var fullPath = Path.GetFullPath(manifestPath);
        var json = File.ReadAllText(fullPath);
        var manifest = JsonSerializer.Deserialize<GroundTruthManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException($"El manifiesto '{fullPath}' está vacío o es inválido.");
        return manifest;
    }

    public static RuleThresholds LoadThresholds(string thresholdsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thresholdsPath);
        var json = File.ReadAllText(Path.GetFullPath(thresholdsPath));
        return JsonSerializer.Deserialize<RuleThresholds>(json, JsonOptions)
            ?? throw new InvalidOperationException($"El archivo de umbrales '{thresholdsPath}' es inválido.");
    }

    public static string ResolveSafePlanPath(string baseDirectory, string relativePlanPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePlanPath);

        var normalizedBase = Path.GetFullPath(baseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(normalizedBase, relativePlanPath));

        if (!candidate.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"La ruta '{relativePlanPath}' intenta salir del directorio base '{baseDirectory}'.");
        }

        return candidate;
    }

    public EngineEvaluationReport EvaluateFromManifest(
        string manifestPath,
        string corpusFilter = "all",
        string splitFilter = "all",
        RuleThresholds? thresholds = null)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var baseDir = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidOperationException("No se pudo determinar la carpeta del manifiesto.");
        var manifest = LoadManifest(fullManifestPath);

        return Evaluate(
            manifest,
            planPath => File.ReadAllText(ResolveSafePlanPath(baseDir, planPath)),
            corpusFilter,
            splitFilter,
            thresholds);
    }

    public EngineEvaluationReport Evaluate(
        GroundTruthManifest manifest,
        Func<string, string> readPlanXml,
        string corpusFilter = "all",
        string splitFilter = "all",
        RuleThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(readPlanXml);

        var normCorpus = NormalizeFilter(corpusFilter, ["all", "seeded", "tpch"], "corpus");
        var normSplit = NormalizeFilter(splitFilter, ["all", "calibration", "evaluation"], "split");
        var effectiveThresholds = thresholds ?? RuleThresholds.Default;

        var filteredCases = manifest.Cases
            .Where(c => normCorpus == "all" || string.Equals(c.Corpus, normCorpus, StringComparison.OrdinalIgnoreCase))
            .Where(c => normSplit == "all" || string.Equals(c.Split, normSplit, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var totalCounters = CreateRuleCounters();
        var seededCounters = CreateRuleCounters();
        var tpchCounters = CreateRuleCounters();
        var diffs = new List<FindingDiffItem>();

        var expectedNotAnalyzable = 0;
        var correctlyClassifiedReg11 = 0;
        var unexpectedNotAnalyzable = 0;
        var seededCount = 0;
        var tpchCount = 0;

        foreach (var testCase in filteredCases)
        {
            var isSeeded = string.Equals(testCase.Corpus, "seeded", StringComparison.OrdinalIgnoreCase);
            if (isSeeded)
            {
                seededCount++;
            }
            else if (string.Equals(testCase.Corpus, "tpch", StringComparison.OrdinalIgnoreCase))
            {
                tpchCount++;
            }

            var xml = readPlanXml(testCase.PlanPath);
            var ctx = testCase.Context ?? new CaseExecutionContext();

            var result = _engine.Analyze(new PlanAnalysisRequest(
                ShowplanXml: xml,
                QueryStoreQueryId: ctx.QueryStoreQueryId,
                QueryStorePlanId: ctx.QueryStorePlanId,
                PlanIdentifier: testCase.CaseId,
                Thresholds: effectiveThresholds,
                QueryExecutionCount: ctx.QueryExecutionCount,
                QueryConsumptionPct: ctx.QueryConsumptionPct,
                AvgRowsReturned: ctx.AvgRowsReturned));

            var expectsNotAnalyzable = string.Equals(
                testCase.ExpectedAnalysisStatus,
                nameof(PlanAnalysisStatus.NotAnalyzable),
                StringComparison.OrdinalIgnoreCase);

            if (expectsNotAnalyzable)
            {
                expectedNotAnalyzable++;
                if (result.AnalysisStatus == PlanAnalysisStatus.NotAnalyzable &&
                    result.Findings.Any(f => string.Equals(f.Rule.Id, "REG-11", StringComparison.OrdinalIgnoreCase)))
                {
                    correctlyClassifiedReg11++;
                }

                continue;
            }

            if (result.AnalysisStatus == PlanAnalysisStatus.NotAnalyzable)
            {
                unexpectedNotAnalyzable++;
                diffs.Add(new FindingDiffItem(
                    testCase.CaseId,
                    testCase.Corpus,
                    testCase.Split,
                    "UNEXPECTED_NOT_ANALYZABLE",
                    "REG-11",
                    [],
                    result.NotAnalyzableCause ?? "El plan válido no pudo analizarse."));
                continue;
            }

            EvaluateAnalyzedCase(
                testCase,
                result.Findings,
                totalCounters,
                isSeeded ? seededCounters : tpchCounters,
                diffs);
        }

        var ruleMetrics = BuildMetricsRows(totalCounters);
        var seededMetrics = BuildMetricsRows(seededCounters);
        var tpchMetrics = BuildMetricsRows(tpchCounters);
        var allTargetsMet = ruleMetrics.All(r =>
            (r.MeetsPrecisionTarget ?? true) && (r.MeetsRecallTarget ?? true));

        return new EngineEvaluationReport(
            CatalogVersion: _catalog.CatalogVersion,
            ThresholdsHash: effectiveThresholds.ComputeConfigurationHash(),
            Thresholds: effectiveThresholds.ToDictionary(),
            CorpusFilter: normCorpus,
            SplitFilter: normSplit,
            TotalCases: filteredCases.Length,
            SeededCases: seededCount,
            TpchCases: tpchCount,
            Rules: ruleMetrics,
            SeededRules: seededMetrics,
            TpchRules: tpchMetrics,
            NotAnalyzable: new NotAnalyzableMetrics(
                expectedNotAnalyzable,
                correctlyClassifiedReg11,
                unexpectedNotAnalyzable),
            Diffs: diffs,
            AllMustTargetsMet: allTargetsMet);
    }

    public SpecialistCaseExportBundle ExportCasesFromManifest(
        string manifestPath,
        IReadOnlyList<string> caseIds,
        RuleThresholds? thresholds = null)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var baseDir = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidOperationException("No se pudo determinar la carpeta del manifiesto.");
        var manifest = LoadManifest(fullManifestPath);

        return ExportCases(
            manifest,
            planPath => File.ReadAllText(ResolveSafePlanPath(baseDir, planPath)),
            caseIds,
            thresholds);
    }

    public SpecialistCaseExportBundle ExportCases(
        GroundTruthManifest manifest,
        Func<string, string> readPlanXml,
        IReadOnlyList<string> caseIds,
        RuleThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(readPlanXml);
        ArgumentNullException.ThrowIfNull(caseIds);

        var requestedIds = caseIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requestedIds.Length == 0)
        {
            throw new ArgumentException("Debe indicar al menos un case_id en --cases.", nameof(caseIds));
        }

        var caseMap = manifest.Cases.ToDictionary(c => c.CaseId, StringComparer.OrdinalIgnoreCase);
        var missing = requestedIds.Where(id => !caseMap.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Los siguientes case_id no existen en el manifiesto: {string.Join(", ", missing)}.");
        }

        var effectiveThresholds = thresholds ?? RuleThresholds.Default;
        var scoreParameters = HealthScoreParameters.Default;
        var masker = new LiteralMasker();
        var scoreCalculator = new HealthScoreCalculator(scoreParameters);

        var analyzedItems = new List<(GroundTruthCase TestCase, Guid QueryGuid, long QsQueryId, string SqlText, string MaskedSql, CaseExecutionContext Context, SpecialistPlanSummary PlanSummary, PlanAnalysisResult Analysis)>(requestedIds.Length);
        var scoreInputs = new List<QueryScoreInput>(requestedIds.Length);

        for (var idx = 0; idx < requestedIds.Length; idx++)
        {
            var testCase = caseMap[requestedIds[idx]];
            var xml = readPlanXml(testCase.PlanPath);
            var ctx = testCase.Context ?? new CaseExecutionContext();
            var parseResult = _parser.Parse(xml, testCase.CaseId);
            var analysis = _engine.AnalyzeParsed(parseResult, new PlanAnalysisRequest(
                ShowplanXml: xml,
                QueryStoreQueryId: ctx.QueryStoreQueryId,
                QueryStorePlanId: ctx.QueryStorePlanId,
                PlanIdentifier: testCase.CaseId,
                Thresholds: effectiveThresholds,
                QueryExecutionCount: ctx.QueryExecutionCount,
                QueryConsumptionPct: ctx.QueryConsumptionPct,
                AvgRowsReturned: ctx.AvgRowsReturned));

            var rawSql = BuildStatementText(parseResult.Plan, testCase.Description);
            var maskedContext = masker.Mask(new MaskingInput(rawSql, xml, analysis.Findings));
            var maskedSql = maskedContext.Sql.Text ?? rawSql;

            var queryGuid = new Guid(idx + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
            var qsQueryId = ctx.QueryStoreQueryId > 0 ? ctx.QueryStoreQueryId : 10_000L + idx;
            var execs = ctx.QueryExecutionCount is > 0 ? ctx.QueryExecutionCount.Value : 1L;
            var perExecProxy = Math.Max(0.0, ctx.QueryConsumptionPct ?? 0.0) * 1000.0 / execs;

            var scoreFindings = analysis.Findings
                .Select(f => new QueryFindingSample(
                    f.Rule.Id,
                    f.FindingId,
                    AnalysisEnumCodes.ParseFindingSeverity(f.Severity),
                    AnalysisEnumCodes.ParseEvidenceClass(f.EvidenceClass),
                    AnalysisEnumCodes.ParseFindingKind(f.Kind)))
                .ToArray();

            scoreInputs.Add(new QueryScoreInput(
                queryGuid,
                qsQueryId,
                [new QueryResourceSample(execs, perExecProxy, perExecProxy, perExecProxy)],
                scoreFindings));

            analyzedItems.Add((testCase, queryGuid, qsQueryId, rawSql, maskedSql, ctx, BuildPlanSummary(parseResult.Plan), analysis));
        }

        var scoreByScoreAsc = scoreCalculator.CalculateWindow(scoreInputs, scoreParameters, HealthScoreOrderBy.ScoreAscending);
        var scoreByConsumption = scoreCalculator.CalculateWindow(scoreInputs, scoreParameters, HealthScoreOrderBy.ConsumptionDescending);
        var rankAscMap = scoreByScoreAsc
            .Select((item, pos) => (item.QueryId, Rank: pos + 1))
            .ToDictionary(x => x.QueryId, x => x.Rank);
        var rankConsMap = scoreByConsumption
            .Select((item, pos) => (item.QueryId, Rank: pos + 1))
            .ToDictionary(x => x.QueryId, x => x.Rank);
        var scoreResultMap = scoreByScoreAsc.ToDictionary(x => x.QueryId);

        var exportedCases = new List<SpecialistExportedCase>(analyzedItems.Count);
        foreach (var item in analyzedItems)
        {
            var score = scoreResultMap[item.QueryGuid];
            exportedCases.Add(new SpecialistExportedCase(
                CaseId: item.TestCase.CaseId,
                Corpus: item.TestCase.Corpus,
                Split: item.TestCase.Split,
                PlanPath: item.TestCase.PlanPath,
                Description: item.TestCase.Description,
                SqlText: item.SqlText,
                MaskedSqlText: item.MaskedSql,
                Context: item.Context,
                HealthScore: new SpecialistHealthScoreSummary(
                    Score: Math.Round(score.Score, 2),
                    Priority: Math.Round(score.Priority, 2),
                    Band: score.BandCode,
                    ImpactComponent: Math.Round(score.ImpactComponent, 4),
                    SeverityComponent: Math.Round(score.SeverityComponent, 4),
                    WeightedConsumptionPct: Math.Round(score.WeightedConsumptionPct, 2),
                    RankByScoreAsc: rankAscMap[item.QueryGuid],
                    RankByConsumptionDesc: rankConsMap[item.QueryGuid]),
                PlanSummary: item.PlanSummary,
                Findings: item.Analysis.Findings));
        }

        return new SpecialistCaseExportBundle(
            CatalogVersion: _catalog.CatalogVersion,
            ThresholdsHash: effectiveThresholds.ComputeConfigurationHash(),
            ScoreParametersHash: scoreParameters.ComputeConfigurationHash(),
            Thresholds: effectiveThresholds.ToDictionary(),
            Cases: exportedCases);
    }

    private static string BuildStatementText(ParsedPlan? plan, string fallback)
    {
        if (plan is null || plan.Statements.Count == 0)
        {
            return fallback;
        }

        var statements = plan.Statements
            .Select(s => s.StatementText?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        return statements.Length > 0 ? string.Join("\n\n", statements) : fallback;
    }

    private static SpecialistPlanSummary BuildPlanSummary(ParsedPlan? plan)
    {
        if (plan is null)
        {
            return new SpecialistPlanSummary(null, null, null, null, null, [], [], []);
        }

        var firstStmt = plan.Statements.Count > 0 ? plan.Statements[0] : null;
        var warnings = plan.Statements
            .SelectMany(s => s.Warnings.Select(FormatWarning))
            .Concat(plan.EnumerateAllOperators().SelectMany(o => o.Warnings.Select(w => $"Nodo #{o.NodeId}: {FormatWarning(w)}")))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var missingIndexes = plan.Statements
            .SelectMany(s => s.MissingIndexGroups)
            .SelectMany(g => g.Indexes.Select(idx => FormatMissingIndex(g.Impact, idx)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var operators = plan.EnumerateAllOperators()
            .Select(o => new SpecialistPlanOperatorSummary(
                NodeId: o.NodeId,
                PhysicalOp: o.PhysicalOp,
                LogicalOp: o.LogicalOp,
                EstimateRows: o.EstimateRows,
                EstimatedRowsRead: o.EstimatedRowsRead,
                EstimatedTotalSubtreeCost: o.EstimatedTotalSubtreeCost,
                TargetObject: FormatTargetObject(o.TargetObject),
                Warnings: o.Warnings.Select(FormatWarning).ToArray()))
            .ToArray();

        return new SpecialistPlanSummary(
            EngineVersion: plan.EngineVersion,
            StatementOptmLevel: firstStmt?.StatementOptmLevel,
            StatementOptmEarlyAbortReason: firstStmt?.StatementOptmEarlyAbortReason,
            StatementSubTreeCost: firstStmt?.StatementSubTreeCost,
            StatementEstRows: firstStmt?.StatementEstRows,
            Warnings: warnings,
            MissingIndexes: missingIndexes,
            Operators: operators);
    }

    private static string FormatWarning(PlanWarning warning) =>
        string.IsNullOrWhiteSpace(warning.Details)
            ? warning.Kind.ToString()
            : $"{warning.Kind}: {warning.Details}";

    private static string? FormatTargetObject(PlanObjectReference? target)
    {
        if (target is null || string.IsNullOrWhiteSpace(target.Table))
        {
            return null;
        }

        var tablePart = string.IsNullOrWhiteSpace(target.Schema)
            ? target.Table
            : $"{target.Schema}.{target.Table}";
        return string.IsNullOrWhiteSpace(target.Index)
            ? tablePart
            : $"{tablePart} [{target.Index}]";
    }

    private static string FormatMissingIndex(double impact, MissingIndexSuggestion idx)
    {
        var tablePart = string.IsNullOrWhiteSpace(idx.Schema)
            ? idx.Table
            : $"{idx.Schema}.{idx.Table}";
        var eq = idx.EqualityColumns.Count > 0 ? $"EQ({string.Join(", ", idx.EqualityColumns)})" : string.Empty;
        var ineq = idx.InequalityColumns.Count > 0 ? $"INEQ({string.Join(", ", idx.InequalityColumns)})" : string.Empty;
        var inc = idx.IncludeColumns.Count > 0 ? $"INCLUDE({string.Join(", ", idx.IncludeColumns)})" : string.Empty;
        var cols = string.Join(" ", new[] { eq, ineq, inc }.Where(p => p.Length > 0));
        return string.Create(CultureInfo.InvariantCulture, $"Impacto {impact:0.0}% en {tablePart}: {cols}").Trim();
    }

    public static bool FindingMatchesSpec(FindingContract emitted, ExpectedFindingSpec expected)
    {
        ArgumentNullException.ThrowIfNull(emitted);
        ArgumentNullException.ThrowIfNull(expected);

        if (!string.Equals(emitted.Rule.Id, expected.RuleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expected.ExpectedNodes is null || expected.ExpectedNodes.Count == 0)
        {
            return true;
        }

        var emittedNodeIds = emitted.Nodes.Select(n => n.NodeId).ToHashSet();
        return expected.ExpectedNodes.Any(emittedNodeIds.Contains);
    }

    private static void EvaluateAnalyzedCase(
        GroundTruthCase testCase,
        IReadOnlyList<FindingContract> emittedFindings,
        Dictionary<string, RuleCounter> totalCounters,
        Dictionary<string, RuleCounter> corpusCounters,
        List<FindingDiffItem> diffs)
    {
        var expectedList = testCase.ExpectedFindings ?? [];

        foreach (var ruleId in EvaluatedRuleIds)
        {
            var expectedForRule = expectedList
                .Where(e => string.Equals(e.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var emittedForRule = emittedFindings
                .Where(f => string.Equals(f.Rule.Id, ruleId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var matchedEmittedIndices = new HashSet<int>();
            var tp = 0;
            var fn = 0;

            foreach (var expected in expectedForRule)
            {
                var matchIndex = -1;
                for (var i = 0; i < emittedForRule.Count; i++)
                {
                    if (!matchedEmittedIndices.Contains(i) && FindingMatchesSpec(emittedForRule[i], expected))
                    {
                        matchIndex = i;
                        break;
                    }
                }

                if (matchIndex >= 0)
                {
                    matchedEmittedIndices.Add(matchIndex);
                    tp++;
                }
                else
                {
                    fn++;
                    diffs.Add(new FindingDiffItem(
                        testCase.CaseId,
                        testCase.Corpus,
                        testCase.Split,
                        "FN",
                        ruleId,
                        expected.ExpectedNodes ?? [],
                        expected.Rationale ?? "Hallazgo esperado no detectado por el motor."));
                }
            }

            var fp = 0;
            for (var i = 0; i < emittedForRule.Count; i++)
            {
                if (matchedEmittedIndices.Contains(i))
                {
                    continue;
                }

                fp++;
                var emitted = emittedForRule[i];
                diffs.Add(new FindingDiffItem(
                    testCase.CaseId,
                    testCase.Corpus,
                    testCase.Split,
                    "FP",
                    ruleId,
                    emitted.Nodes.Select(n => n.NodeId).ToArray(),
                    $"Hallazgo emitido sin anotación esperada (severidad {emitted.Severity})."));
            }

            totalCounters[ruleId].Add(tp, fp, fn);
            corpusCounters[ruleId].Add(tp, fp, fn);
        }
    }

    private List<RuleMetricsSummary> BuildMetricsRows(Dictionary<string, RuleCounter> counters)
    {
        var rows = new List<RuleMetricsSummary>(EvaluatedRuleIds.Count);
        foreach (var ruleId in EvaluatedRuleIds)
        {
            var counter = counters[ruleId];
            var descriptor = _catalog.TryGetDescriptor(ruleId);
            var ruleName = descriptor?.Name ?? ruleId;

            double? precision = (counter.Tp + counter.Fp) > 0
                ? (double)counter.Tp / (counter.Tp + counter.Fp)
                : null;
            double? recall = (counter.Tp + counter.Fn) > 0
                ? (double)counter.Tp / (counter.Tp + counter.Fn)
                : null;
            double? f1 = precision.HasValue && recall.HasValue && (precision.Value + recall.Value) > 0
                ? 2.0 * precision.Value * recall.Value / (precision.Value + recall.Value)
                : null;

            rows.Add(new RuleMetricsSummary(
                RuleId: ruleId,
                RuleName: ruleName,
                ExpectedPositives: counter.Tp + counter.Fn,
                EmittedTotal: counter.Tp + counter.Fp,
                TruePositives: counter.Tp,
                FalsePositives: counter.Fp,
                FalseNegatives: counter.Fn,
                Precision: precision,
                Recall: recall,
                F1: f1,
                MeetsPrecisionTarget: precision.HasValue ? precision.Value >= TargetPrecision : null,
                MeetsRecallTarget: recall.HasValue ? recall.Value >= TargetRecall : null));
        }

        return rows;
    }

    private static Dictionary<string, RuleCounter> CreateRuleCounters() =>
        EvaluatedRuleIds.ToDictionary(id => id, _ => new RuleCounter(), StringComparer.OrdinalIgnoreCase);

    private static string NormalizeFilter(string value, string[] allowed, string optionName)
    {
        var normalized = (value ?? "all").Trim().ToLowerInvariant();
        if (!allowed.Contains(normalized, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Valor inválido '{value}' para --{optionName}. Valores permitidos: {string.Join(", ", allowed)}.",
                optionName);
        }

        return normalized;
    }

    private sealed class RuleCounter
    {
        public int Tp { get; private set; }
        public int Fp { get; private set; }
        public int Fn { get; private set; }

        public void Add(int tp, int fp, int fn)
        {
            Tp += tp;
            Fp += fp;
            Fn += fn;
        }
    }
}
