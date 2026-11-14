using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Masking;
using QueryDoctor.Domain.Analysis;
using static QueryDoctor.UnitTests.Masking.MaskingSentinels;

namespace QueryDoctor.UnitTests.Masking;

/// <summary>
/// Enmascarado de la evidencia de los hallazgos (S2-04, RNF-SEG-05: "campos de evidencia de los hallazgos"):
/// hallazgos reales del motor sobre los planes sembrados, prueba guardiana del catálogo y fallo cerrado.
/// </summary>
public sealed class LiteralMaskerEvidenceTests
{
    private readonly LiteralMasker _sut = new();

    internal static IReadOnlyList<FindingContract> AnalyzeSeeded(string fixture, out string seededPlan)
    {
        seededPlan = MaskingFixtures.Seed(MaskingFixtures.Read(fixture));
        return Analyze(seededPlan, fixture);
    }

    internal static IReadOnlyList<FindingContract> Analyze(string planXml, string planIdentifier)
    {
        var engine = new DeterministicRuleEngine(
            new ShowplanXmlParser(),
            new VersionedRuleCatalog(VersionedRuleCatalog.DiscoverBuiltInRules()));

        // Umbral de REG-04 bajado para que los lookups del laboratorio también emitan evidencia.
        return engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: planXml,
            QueryStoreQueryId: 1,
            QueryStorePlanId: 1,
            PlanIdentifier: planIdentifier,
            Thresholds: RuleThresholds.Default with { Reg04MinEstimatedExecutions = 1.0 },
            QueryExecutionCount: 1_000,
            QueryConsumptionPct: 50,
            AnalyzedAt: new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero))).Findings;
    }

    internal static string Flatten(MaskedFinding finding) =>
        string.Join('\n', finding.Evidence.Select(pair => $"{pair.Key}={pair.Value}"));

    [Fact]
    public void LaEvidenciaDeLosHallazgosRealesDelMotorQuedaSinCentinelasYNingunaClaveSinClasificar()
    {
        var firedRules = new SortedSet<string>(StringComparer.Ordinal);
        var rawEvidenceWithSentinels = 0;

        var seededTruncated = MaskingFixtures.Seed(MaskingFixtures.Read("ESC01.actual.sqlplan"));
        var corpus = MaskingFixtures.FixturePaths()
            .Select(path => AnalyzeSeeded(Path.GetFileName(path), out _))
            .Append(Analyze(seededTruncated[..(seededTruncated.Length / 2)], "ESC01-truncado"));

        foreach (var findings in corpus)
        {
            rawEvidenceWithSentinels += findings.Count(f => f.Evidence.Values.Any(v =>
                v is not null && (v.Contains(PredicateSecret, StringComparison.Ordinal) || v.Contains(Secret, StringComparison.Ordinal))));

            var result = _sut.Mask(new MaskingInput(null, null, findings));

            // Guardiana del catálogo (D5): toda clave que emiten las reglas integradas está clasificada.
            Assert.DoesNotContain(result.ExcludedFindings, e => e.Cause == MaskingFailureCause.EvidenceKeyUnclassified);
            Assert.Empty(result.ExcludedFindings);
            Assert.Equal(findings.Count, result.Findings.Count);

            foreach (var masked in result.Findings)
            {
                firedRules.Add(masked.RuleId);
                AssertNoneIn(Flatten(masked));
            }
        }

        Assert.True(rawEvidenceWithSentinels > 0, "La siembra no llegó a la evidencia: la prueba no demostraría nada.");
        Assert.Equal(["REG-01", "REG-02", "REG-03", "REG-04", "REG-11"], firedRules);
    }

    [Fact]
    public void ElPredicadoYLaExpresionSeEnmascaranYLosIdentificadoresYNumerosDelMotorSeConservan()
    {
        var finding = Contract("REG-03", new Dictionary<string, string?>
        {
            ["predicate"] = $"[QD_Lab].[dbo].[Orders].[Note] like N'%{PredicateSecret}%' AND [QD_Lab].[dbo].[Orders].[Id]>({PredicateNumber})",
            ["table"] = "Orders",
            ["estimated_rows"] = "1500",
            ["physical_op"] = "Clustered Index Scan",
        });

        var result = _sut.Mask(new MaskingInput(null, null, [finding]));

        var masked = Assert.Single(result.Findings);
        Assert.Equal(
            "[QD_Lab].[dbo].[Orders].[Note] like @qd_str_1 AND [QD_Lab].[dbo].[Orders].[Id]>(@qd_num_2)",
            masked.Evidence["predicate"]);
        Assert.Equal("Orders", masked.Evidence["table"]);
        Assert.Equal("1500", masked.Evidence["estimated_rows"]);
        Assert.Equal("Clustered Index Scan", masked.Evidence["physical_op"]);
    }

    [Fact]
    public void LaCausaLibreDeReg11NoSaleNuncaSoloElCodigo()
    {
        var finding = Contract("REG-11", new Dictionary<string, string?>
        {
            ["cause"] = $"Error de XML en la línea 1 cerca de '{Secret}'",
            ["failure_reason"] = "MalformedXml",
        });

        var masked = Assert.Single(_sut.Mask(new MaskingInput(null, null, [finding])).Findings);

        Assert.False(masked.Evidence.ContainsKey("cause"));
        Assert.Equal("MalformedXml", masked.Evidence["failure_reason"]);
    }

    public static TheoryData<string, string, MaskingFailureCause> EvidenciaQueFallaCerrado() => new()
    {
        { "clave_nueva_sin_clasificar", "QD_SECRET_7f3a", MaskingFailureCause.EvidenceKeyUnclassified },
        { "table", "N'QD_SECRET_7f3a'", MaskingFailureCause.EvidenceUnexpectedLiteral },
        { "predicate", "[c] = 'QD_SECRET_7f3a", MaskingFailureCause.EvidenceFragmentLexicalError },
    };

    [Theory]
    [MemberData(nameof(EvidenciaQueFallaCerrado))]
    public void LaEvidenciaQueNoSePuedeEnmascararDejaFueraSoloEseHallazgo(string key, string value, MaskingFailureCause expected)
    {
        var bad = Contract("REG-99", new Dictionary<string, string?> { [key] = value, ["schema"] = "dbo" }, "b-malo");
        var good = Contract("REG-03", new Dictionary<string, string?> { ["predicate"] = "[c]=(5)" }, "a-bueno");

        var result = _sut.Mask(new MaskingInput(null, null, [bad, good]));

        var exclusion = Assert.Single(result.ExcludedFindings);
        Assert.Equal(new MaskingExclusion("b-malo", "REG-99", expected), exclusion);
        var kept = Assert.Single(result.Findings);
        Assert.Equal("a-bueno", kept.FindingId);
        Assert.Equal("[c]=(@qd_num_1)", kept.Evidence["predicate"]);
        Assert.Equal(1, result.Metrics.TotalLiterals);
    }

    [Fact]
    public void LosHallazgosSeProcesanEnOrdenDeFindingIdYElResultadoEsDeterminista()
    {
        var findings = AnalyzeSeeded("ESC01.actual.sqlplan", out var plan);
        var reversed = findings.Reverse().ToArray();

        var first = _sut.Mask(new MaskingInput("SELECT 1", plan, findings));
        var second = new LiteralMasker().Mask(new MaskingInput("SELECT 1", plan, reversed));

        Assert.Equal(first.Findings.Select(f => f.FindingId), second.Findings.Select(f => f.FindingId));
        Assert.Equal(first.Findings.Select(Flatten), second.Findings.Select(Flatten));
        Assert.Equal(first.Plan.Text, second.Plan.Text);
        Assert.Equal(first.Metrics, second.Metrics);
    }

    internal static FindingContract Contract(string ruleId, IDictionary<string, string?> evidence, string findingId = "f-1") => new(
        SchemaVersion: FindingContract.CurrentSchemaVersion,
        FindingId: findingId,
        CatalogVersion: "test",
        Rule: new FindingRuleContract(ruleId, "Regla de prueba", "1.0.0"),
        Kind: FindingKind.Senal.ToContractCode(),
        Severity: FindingSeverity.Media.ToContractCode(),
        EvidenceClass: EvidenceClass.Estimada.ToContractCode(),
        QueryId: 1,
        PlanId: 1,
        Nodes: [new FindingNodeContract(3, "Clustered Index Scan")],
        Evidence: new Dictionary<string, string?>(evidence),
        Context: new FindingContextContract(),
        ThresholdsUsed: new Dictionary<string, double>(),
        EngineVersion: "16.0.4315.2",
        AnalyzedAt: new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));
}
