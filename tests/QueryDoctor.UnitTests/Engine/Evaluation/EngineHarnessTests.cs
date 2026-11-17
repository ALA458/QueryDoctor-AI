using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.EngineHarness;

namespace QueryDoctor.UnitTests.Engine.Evaluation;

/// <summary>
/// Pruebas de integridad, cálculo de métricas y determinismo del arnés del motor (S2-14, RF-EVA-01, CA-HU20.1).
/// </summary>
public sealed class EngineHarnessTests
{
    private static readonly string[] AllowedCorpora = ["seeded", "tpch"];
    private static readonly string[] AllowedSplits = ["calibration", "evaluation"];

    private static readonly string FixturesDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures"));

    private static readonly string ManifestPath = Path.Combine(FixturesDirectory, "engine-ground-truth.json");

    [Fact]
    public void ManifiestoReferenciaPlanesExistentesSplitsValidosYReglasDelCatalogo()
    {
        var manifest = EngineHarnessRunner.LoadManifest(ManifestPath);
        var catalog = new VersionedRuleCatalog();
        var allowedRuleIds = catalog.GetAllDescriptors().Select(d => d.RuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(manifest.Cases);
        Assert.Contains(manifest.Cases, c => string.Equals(c.Corpus, "seeded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(manifest.Cases, c => string.Equals(c.Corpus, "tpch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(manifest.Cases, c => string.Equals(c.Split, "calibration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(manifest.Cases, c => string.Equals(c.Split, "evaluation", StringComparison.OrdinalIgnoreCase));

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Cases)
        {
            Assert.True(seenIds.Add(item.CaseId), $"CaseId duplicado en manifiesto: {item.CaseId}");
            Assert.Contains(item.Corpus, AllowedCorpora);
            Assert.Contains(item.Split, AllowedSplits);

            var fullPlanPath = EngineHarnessRunner.ResolveSafePlanPath(FixturesDirectory, item.PlanPath);
            Assert.True(File.Exists(fullPlanPath), $"No existe el plan '{item.PlanPath}' para {item.CaseId}");

            foreach (var expected in item.ExpectedFindings ?? [])
            {
                Assert.True(
                    allowedRuleIds.Contains(expected.RuleId),
                    $"Regla desconocida '{expected.RuleId}' en caso {item.CaseId}");
            }
        }
    }

    [Fact]
    public void ResolveSafePlanPathRechazaTraversalFueraDelDirectorioBase()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EngineHarnessRunner.ResolveSafePlanPath(FixturesDirectory, Path.Combine("..", "secrets", "mssql_sa_password.txt")));
    }

    [Fact]
    public void ArnesEsDeterministaEntreCorridasYReportaPrecisionYExhaustividadPorRegla()
    {
        var runner = new EngineHarnessRunner();

        var firstRun = runner.EvaluateFromManifest(ManifestPath, corpusFilter: "all", splitFilter: "all");
        var secondRun = runner.EvaluateFromManifest(ManifestPath, corpusFilter: "all", splitFilter: "all");

        Assert.Equal(firstRun.ThresholdsHash, secondRun.ThresholdsHash);
        Assert.Equal(firstRun.TotalCases, secondRun.TotalCases);
        Assert.True(firstRun.SeededCases > 0);
        Assert.True(firstRun.TpchCases >= 26);

        Assert.Equal(4, firstRun.Rules.Count);
        for (var i = 0; i < firstRun.Rules.Count; i++)
        {
            Assert.Equal(firstRun.Rules[i], secondRun.Rules[i]);
            Assert.True(firstRun.Rules[i].ExpectedPositives > 0, $"Debe haber casos positivos para {firstRun.Rules[i].RuleId}");
            Assert.NotNull(firstRun.Rules[i].Precision);
            Assert.NotNull(firstRun.Rules[i].Recall);
        }

        Assert.Equal(1, firstRun.NotAnalyzable.ExpectedNotAnalyzable);
        Assert.Equal(1, firstRun.NotAnalyzable.CorrectlyClassifiedReg11);
        Assert.Equal(0, firstRun.NotAnalyzable.UnexpectedNotAnalyzable);

        var calibrationRun = runner.EvaluateFromManifest(ManifestPath, splitFilter: "calibration");
        var evaluationRun = runner.EvaluateFromManifest(ManifestPath, splitFilter: "evaluation");
        Assert.Equal(firstRun.TotalCases, calibrationRun.TotalCases + evaluationRun.TotalCases);
        Assert.True(calibrationRun.AllMustTargetsMet);
        Assert.True(evaluationRun.AllMustTargetsMet);
        Assert.True(firstRun.AllMustTargetsMet);
        Assert.Empty(firstRun.Diffs);
    }

    [Fact]
    public void CalculoDeMatrizDeConfusionDistingueTpFpFnYEmparejamientoPorNodo()
    {
        const string planWithConvertAndScan = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementText="SELECT OrderId FROM dbo.Orders WHERE CustomerCode = @code" StatementId="1" StatementType="SELECT" StatementSubTreeCost="1.0" StatementEstRows="10">
                      <QueryPlan>
                        <Warnings>
                          <PlanAffectingConvert ConvertIssue="Seek Plan" Expression="CONVERT_IMPLICIT(nvarchar(20),[Orders].[CustomerCode],0)=[@code]" />
                        </Warnings>
                        <RelOp NodeId="3" PhysicalOp="Index Scan" LogicalOp="Index Scan" EstimateRows="10" EstimatedRowsRead="50000" EstimateIO="0.5" EstimateCPU="0.1" AvgRowSize="16" EstimatedTotalSubtreeCost="0.6" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
                          <OutputList />
                          <IndexScan Ordered="0" Storage="RowStore">
                            <Object Database="[QD_Lab]" Schema="[dbo]" Table="[Orders]" Index="[IX_Orders_CustomerCode]" IndexKind="NonClustered" Storage="RowStore" />
                            <Predicate>
                              <ScalarOperator ScalarString="CONVERT_IMPLICIT(nvarchar(20),[Orders].[CustomerCode],0)=[@code]" />
                            </Predicate>
                          </IndexScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var manifest = new GroundTruthManifest(
            SchemaVersion: "1.0.0",
            Description: "Prueba unitaria de matriz TP/FP/FN",
            Cases:
            [
                new GroundTruthCase(
                    CaseId: "CASE_SYNTHETIC_1",
                    Corpus: "seeded",
                    Split: "calibration",
                    PlanPath: "synthetic.sqlplan",
                    Description: "Registra REG-01 esperado (TP), omite REG-03 (FP) y espera REG-02 ausente (FN).",
                    Context: new CaseExecutionContext(1, 1, 10, 5.0, 10.0),
                    ExpectedFindings:
                    [
                        new ExpectedFindingSpec("REG-01", [3]),
                        new ExpectedFindingSpec("REG-02"),
                    ]),
            ]);

        var runner = new EngineHarnessRunner();
        var report = runner.Evaluate(manifest, _ => planWithConvertAndScan, thresholds: RuleThresholds.Default);

        var reg01 = report.Rules.Single(r => r.RuleId == "REG-01");
        var reg02 = report.Rules.Single(r => r.RuleId == "REG-02");
        var reg03 = report.Rules.Single(r => r.RuleId == "REG-03");
        var reg04 = report.Rules.Single(r => r.RuleId == "REG-04");

        Assert.Equal(1, reg01.TruePositives);
        Assert.Equal(0, reg01.FalsePositives);
        Assert.Equal(0, reg01.FalseNegatives);
        Assert.Equal(1.0, reg01.Precision);
        Assert.Equal(1.0, reg01.Recall);

        Assert.Equal(0, reg02.TruePositives);
        Assert.Equal(0, reg02.FalsePositives);
        Assert.Equal(1, reg02.FalseNegatives);
        Assert.Null(reg02.Precision);
        Assert.Equal(0.0, reg02.Recall);

        Assert.Equal(0, reg03.TruePositives);
        Assert.Equal(1, reg03.FalsePositives);
        Assert.Equal(0, reg03.FalseNegatives);
        Assert.Equal(0.0, reg03.Precision);
        Assert.Null(reg03.Recall);

        Assert.Null(reg04.Precision);
        Assert.Null(reg04.Recall);

        var markdown = ReportFormatter.ToMarkdown(report);
        Assert.Contains("REG-01", markdown, StringComparison.Ordinal);
        Assert.Contains("CASE_SYNTHETIC_1", markdown, StringComparison.Ordinal);
    }
}
