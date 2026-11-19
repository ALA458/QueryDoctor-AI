using QueryDoctor.EngineHarness;

namespace QueryDoctor.UnitTests.Engine.Evaluation;

/// <summary>
/// Pruebas unitarias de exportación determinista de casos de extremo a extremo para contraste con especialista (S3-04, RF-EVA-03).
/// </summary>
public sealed class EngineHarnessExportTests
{
    private static readonly string FixturesDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures"));

    private static readonly string ManifestPath = Path.Combine(FixturesDirectory, "engine-ground-truth.json");

    private static readonly string[] S304SelectedCases =
    [
        "ESC01_QS_1",
        "SEEDED_REG01_SEEK_PLAN",
        "ESC03_QS_1",
        "SEEDED_REG04_POS_RID_2500",
        "TPCH_Q02",
        "TPCH_Q06",
        "TPCH_Q18",
        "TPCH_Q20",
        "TPCH_NEG02",
        "ESC04B_QS_1",
    ];

    [Fact]
    public void ExportCasesGeneraDiezCasosRealesConReglasCalibradasYHealthScoreDeterminista()
    {
        var runner = new EngineHarnessRunner();

        var first = runner.ExportCasesFromManifest(ManifestPath, S304SelectedCases);
        var second = runner.ExportCasesFromManifest(ManifestPath, S304SelectedCases);

        Assert.Equal(S304SelectedCases.Length, first.Cases.Count);
        Assert.Equal(S304SelectedCases, first.Cases.Select(c => c.CaseId).ToArray());
        Assert.Equal(ReportFormatter.ToExportJson(first), ReportFormatter.ToExportJson(second));

        var allRules = first.Cases
            .SelectMany(c => c.Findings)
            .Select(f => f.Rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("REG-01", allRules);
        Assert.Contains("REG-02", allRules);
        Assert.Contains("REG-03", allRules);
        Assert.Contains("REG-04", allRules);

        // TPCH_Q20 ya no debe emitir REG-03 sobre tabla catálogo NATION (calibración S3-01)
        var q20 = first.Cases.Single(c => c.CaseId == "TPCH_Q20");
        Assert.DoesNotContain(q20.Findings, f =>
            f.Rule.Id == "REG-03" &&
            f.Evidence.Values.Any(v => v?.Contains("NATION", StringComparison.OrdinalIgnoreCase) == true));

        // Casos sin hallazgos esperados (control negativo y out-of-catalog)
        var neg02 = first.Cases.Single(c => c.CaseId == "TPCH_NEG02");
        Assert.Empty(neg02.Findings);
        Assert.Equal("SALUDABLE", neg02.HealthScore.Band);

        var esc04b = first.Cases.Single(c => c.CaseId == "ESC04B_QS_1");
        Assert.Empty(esc04b.Findings);

        // Ranks únicos 1..10 y SQL/plan presentes en todos los casos
        Assert.Equal(Enumerable.Range(1, 10), first.Cases.Select(c => c.HealthScore.RankByScoreAsc).OrderBy(x => x));
        Assert.All(first.Cases, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.SqlText));
            Assert.False(string.IsNullOrWhiteSpace(c.MaskedSqlText));
            Assert.NotEmpty(c.PlanSummary.Operators);
        });
    }

    [Fact]
    public void ExportCasesRechazaListaVaciaOCaseIdInexistente()
    {
        var runner = new EngineHarnessRunner();

        Assert.Throws<ArgumentException>(() =>
            runner.ExportCasesFromManifest(ManifestPath, []));

        Assert.Throws<InvalidOperationException>(() =>
            runner.ExportCasesFromManifest(ManifestPath, ["CASE_INEXISTENTE_999"]));
    }
}
