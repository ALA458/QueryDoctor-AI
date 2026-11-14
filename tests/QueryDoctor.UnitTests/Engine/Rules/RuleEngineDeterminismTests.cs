using System.Globalization;
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Engine.Rules;

/// <summary>
/// S1-13: prueba el criterio de aceptación ("Mismo plan + misma versión + mismos umbrales = mismos hallazgos"),
/// la sensibilidad a cambios de versión/umbral, el manejo de REG-11 en lotes y la omisión por clase de evidencia.
/// </summary>
public sealed class RuleEngineDeterminismTests
{
    private static readonly string FixturesDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "plans"));

    [Fact]
    public void MismoPlanMismaVersionYMismosUmbralesProducenMismosHallazgosBitABit()
    {
        var xml = LoadFixture("ESC01.qs.1.sqlplan");
        var parser = new ShowplanXmlParser();

        var ruleA = new ProbeConvertWarningRule("1.0.0");
        var ruleB = new ProbeHighCostOperatorRule("1.0.0");

        // Dos motores con las mismas reglas pero registradas en orden inverso en el catálogo.
        var engineForward = new DeterministicRuleEngine(
            parser,
            new VersionedRuleCatalog([ruleA, ruleB]),
            RuleThresholds.Default);

        var engineReverse = new DeterministicRuleEngine(
            parser,
            new VersionedRuleCatalog([ruleB, ruleA]),
            RuleThresholds.Default);

        var request = new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 101,
            QueryStorePlanId: 202,
            PlanIdentifier: "ESC01.qs.1",
            Thresholds: RuleThresholds.Default,
            QueryExecutionCount: 500,
            QueryConsumptionPct: 4.5,
            AnalyzedAt: new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));

        var firstRun = engineForward.Analyze(request);
        var secondRun = engineForward.Analyze(request);
        var reverseOrderRun = engineReverse.Analyze(request);

        Assert.Equal(PlanAnalysisStatus.Analyzable, firstRun.AnalysisStatus);
        Assert.NotEmpty(firstRun.Findings);

        var jsonFirst = FindingContractJson.SerializeList(firstRun.Findings);
        var jsonSecond = FindingContractJson.SerializeList(secondRun.Findings);
        var jsonReverse = FindingContractJson.SerializeList(reverseOrderRun.Findings);

        Assert.Equal(jsonFirst, jsonSecond);
        Assert.Equal(jsonFirst, jsonReverse);

        Assert.Equal(
            firstRun.Findings.Select(f => f.FindingId),
            reverseOrderRun.Findings.Select(f => f.FindingId));
    }

    [Fact]
    public void CambiarUmbralCambiaElConjuntoDeHallazgosYQuedaRegistradoEnThresholdsUsed()
    {
        var xml = LoadFixture("ESC03.qs.1.sqlplan");
        var parser = new ShowplanXmlParser();
        var engine = new DeterministicRuleEngine(
            parser,
            new VersionedRuleCatalog([new ProbeHighCostOperatorRule("1.0.0")]));

        // ESC03 tiene un Clustered Index Scan con EstimateRows = 16172.1 (> 10 000 por defecto).
        var defaultThresholds = RuleThresholds.Default; // Reg03MinEstimatedRows = 10 000
        var strictThresholds = RuleThresholds.Default with
        {
            Reg03MinEstimatedRows = 50_000.0,
        };

        var resultWithDefault = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 10,
            QueryStorePlanId: 20,
            Thresholds: defaultThresholds,
            QueryConsumptionPct: 5.0));

        var resultWithStrict = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 10,
            QueryStorePlanId: 20,
            Thresholds: strictThresholds,
            QueryConsumptionPct: 5.0));

        var finding = Assert.Single(resultWithDefault.Findings);
        Assert.Equal("REG-03", finding.Rule.Id);
        Assert.Equal(10_000.0, finding.ThresholdsUsed["min_estimated_rows"]);

        Assert.Empty(resultWithStrict.Findings);
        Assert.NotEqual(
            resultWithDefault.ThresholdConfigurationHash,
            resultWithStrict.ThresholdConfigurationHash);
    }

    [Fact]
    public void CambiarVersionDeLaReglaModificaElFindingIdDeterminista()
    {
        var xml = LoadFixture("ESC01.qs.1.sqlplan");
        var parser = new ShowplanXmlParser();

        var engineV1 = new DeterministicRuleEngine(
            parser,
            new VersionedRuleCatalog([new ProbeConvertWarningRule("1.0.0")]));

        var engineV2 = new DeterministicRuleEngine(
            parser,
            new VersionedRuleCatalog([new ProbeConvertWarningRule("1.1.0")]));

        var request = new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 1,
            QueryStorePlanId: 1,
            PlanIdentifier: "plan-1");

        var findingV1 = Assert.Single(engineV1.Analyze(request).Findings);
        var findingV2 = Assert.Single(engineV2.Analyze(request).Findings);

        Assert.NotEqual(findingV1.FindingId, findingV2.FindingId);
        Assert.Equal("1.0.0", findingV1.Rule.Version);
        Assert.Equal("1.1.0", findingV2.Rule.Version);
    }

    [Fact]
    public void LoteConXmlInvalidoProduceReg11SinDetenerLosDemasPlanes()
    {
        var validXml1 = LoadFixture("ESC01.qs.1.sqlplan");
        var validXml2 = LoadFixture("ESC03.qs.1.sqlplan");
        var parser = new ShowplanXmlParser();
        var engine = new DeterministicRuleEngine(
            parser,
            new VersionedRuleCatalog([new ProbeConvertWarningRule("1.0.0"), new ProbeHighCostOperatorRule("1.0.0")]));

        var batch = new[]
        {
            new PlanAnalysisRequest(validXml1, QueryStoreQueryId: 1, QueryStorePlanId: 10, PlanIdentifier: "P-10", QueryConsumptionPct: 5.0),
            new PlanAnalysisRequest("<ShowPlanXML><BatchSequence><Batch>", QueryStoreQueryId: 2, QueryStorePlanId: 20, PlanIdentifier: "P-20"),
            new PlanAnalysisRequest(null, QueryStoreQueryId: 3, QueryStorePlanId: 30, PlanIdentifier: "P-30"),
            new PlanAnalysisRequest(validXml2, QueryStoreQueryId: 4, QueryStorePlanId: 40, PlanIdentifier: "P-40", QueryConsumptionPct: 5.0),
        };

        var results = engine.AnalyzeBatch(batch);

        Assert.Equal(4, results.Count);

        Assert.Equal(PlanAnalysisStatus.Analyzable, results[0].AnalysisStatus);
        Assert.Contains(results[0].Findings, f => f.Rule.Id == "REG-01");

        Assert.Equal(PlanAnalysisStatus.NotAnalyzable, results[1].AnalysisStatus);
        var reg11Malformed = Assert.Single(results[1].Findings);
        Assert.Equal("REG-11", reg11Malformed.Rule.Id);
        Assert.Equal("PLAN_NO_ANALIZABLE", reg11Malformed.Kind);
        Assert.Equal("INFORMATIVA", reg11Malformed.Severity);
        Assert.Equal("NO_APLICA", reg11Malformed.EvidenceClass);

        Assert.Equal(PlanAnalysisStatus.NotAnalyzable, results[2].AnalysisStatus);
        var reg11Null = Assert.Single(results[2].Findings);
        Assert.Equal("REG-11", reg11Null.Rule.Id);

        Assert.Equal(PlanAnalysisStatus.Analyzable, results[3].AnalysisStatus);
        Assert.Equal("REG-03", Assert.Single(results[3].Findings).Rule.Id);
    }

    [Fact]
    public void ReglaQueRequierePlanRealSeOmiteCuandoLaClaseDeEvidenciaNoEstaDisponible()
    {
        var xml = LoadFixture("ESC05.actual.sqlplan");
        var parser = new ShowplanXmlParser();
        var engine = new DeterministicRuleEngine(
            parser,
            new VersionedRuleCatalog([new ProbeRealPlanSpillRule("1.0.0")]));

        // Por defecto AvailableEvidenceClasses solo incluye Estimada y EjecucionAgregada (Query Store estándar).
        var resultWithoutRealPlan = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 5,
            QueryStorePlanId: 50));

        Assert.Empty(resultWithoutRealPlan.Findings);
        Assert.Contains("REG-09", resultWithoutRealPlan.OmittedRuleIds);

        // Si la conexión habilitó PlanReal, la regla se evalúa.
        var resultWithRealPlan = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 5,
            QueryStorePlanId: 50,
            AvailableEvidenceClasses: new HashSet<EvidenceClass>
            {
                EvidenceClass.Estimada,
                EvidenceClass.EjecucionAgregada,
                EvidenceClass.PlanReal,
            }));

        Assert.Empty(resultWithRealPlan.OmittedRuleIds);
        Assert.Single(resultWithRealPlan.Findings);
    }

    private static string LoadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(FixturesDirectory, fileName));

    private sealed class ProbeConvertWarningRule(string version) : IRule
    {
        public RuleDescriptor Descriptor { get; } = VersionedRuleCatalog.BuiltInDescriptors
            .First(d => d.RuleId == "REG-01") with
        {
            Version = version,
        };

        public IReadOnlyList<RuleFindingDraft> Evaluate(RuleEvaluationContext context)
        {
            var drafts = new List<RuleFindingDraft>();
            foreach (var statement in context.Plan.Statements)
            {
                foreach (var warning in statement.Warnings.Where(w => w.Kind == QueryDoctor.Domain.Plans.PlanWarningKind.PlanAffectingConvert))
                {
                    var matchingNodes = statement.EnumerateOperators()
                        .Where(op => op.ImplicitConversions.Count > 0)
                        .Select(op => new FindingNodeContract(op.NodeId, op.PhysicalOp))
                        .ToArray();

                    drafts.Add(new RuleFindingDraft(
                        Severity: FindingSeverity.Alta,
                        Nodes: matchingNodes,
                        Evidence: new Dictionary<string, string?>
                        {
                            ["convert_issue"] = warning.ConvertIssue,
                            ["expression"] = warning.Expression,
                        },
                        Discriminator: warning.Expression));
                }
            }

            return drafts;
        }
    }

    private sealed class ProbeHighCostOperatorRule(string version) : IRule
    {
        public RuleDescriptor Descriptor { get; } = VersionedRuleCatalog.BuiltInDescriptors
            .First(d => d.RuleId == "REG-03") with
        {
            Version = version,
        };

        public IReadOnlyList<RuleFindingDraft> Evaluate(RuleEvaluationContext context)
        {
            if ((context.QueryConsumptionPct ?? 0.0) < context.Thresholds.MinQueryConsumptionPct)
            {
                return [];
            }

            var drafts = new List<RuleFindingDraft>();
            foreach (var op in context.Plan.EnumerateAllOperators())
            {
                if (op.PhysicalOp.Contains("Scan", StringComparison.OrdinalIgnoreCase) &&
                    op.EstimateRows >= context.Thresholds.Reg03MinEstimatedRows)
                {
                    drafts.Add(new RuleFindingDraft(
                        Severity: FindingSeverity.Media,
                        Nodes: [new FindingNodeContract(op.NodeId, op.PhysicalOp)],
                        Evidence: new Dictionary<string, string?>
                        {
                            ["estimate_rows"] = op.EstimateRows.ToString(CultureInfo.InvariantCulture),
                            ["physical_op"] = op.PhysicalOp,
                        },
                        Context: new FindingContextContract(
                            Executions: context.QueryExecutionCount,
                            QueryConsumptionPct: context.QueryConsumptionPct,
                            EstimatedRows: op.EstimateRows,
                            EstimatedRowsRead: op.EstimatedRowsRead)));
                }
            }

            return drafts;
        }
    }

    private sealed class ProbeRealPlanSpillRule(string version) : IRule
    {
        public RuleDescriptor Descriptor { get; } = VersionedRuleCatalog.BuiltInDescriptors
            .First(d => d.RuleId == "REG-09") with
        {
            Version = version,
        };

        public IReadOnlyList<RuleFindingDraft> Evaluate(RuleEvaluationContext context)
        {
            var drafts = new List<RuleFindingDraft>();
            foreach (var op in context.Plan.EnumerateAllOperators())
            {
                if (op.Warnings.Any(w => w.Kind == QueryDoctor.Domain.Plans.PlanWarningKind.SpillToTempDb))
                {
                    drafts.Add(new RuleFindingDraft(
                        Severity: FindingSeverity.Media,
                        Nodes: [new FindingNodeContract(op.NodeId, op.PhysicalOp)],
                        Evidence: new Dictionary<string, string?>
                        {
                            ["spill_operator"] = op.PhysicalOp,
                        }));
                }
            }

            return drafts;
        }
    }
}
