using System.Text.Json;
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

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

    private readonly IRuleCatalog _catalog;
    private readonly IRuleEngine _engine;

    public EngineHarnessRunner()
    {
        _catalog = new VersionedRuleCatalog();
        _engine = new DeterministicRuleEngine(new ShowplanXmlParser(), _catalog);
    }

    public EngineHarnessRunner(IRuleCatalog catalog, IRuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(engine);
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
