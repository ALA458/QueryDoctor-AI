using System.Text.Json;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.UnitTests.Engine.Rules;

/// <summary>
/// S1-13: verifica el catálogo de reglas versionado (RF-MOT-15), los umbrales por defecto del Cuadro 4.5 (RF-MOT-14)
/// y el contrato JSON versionado del hallazgo (RF-MOT-03, RF-MOT-17, Apéndice B Listing B.1).
/// </summary>
public sealed class RuleCatalogAndContractTests
{
    [Fact]
    public void CatalogVersionedContieneLasOnceReglasDelSrsEnOrdenCanónico()
    {
        var catalog = new VersionedRuleCatalog();
        var descriptors = catalog.GetAllDescriptors();

        Assert.Equal("1.0.0", catalog.CatalogVersion);
        Assert.Equal(11, descriptors.Count);

        var ids = descriptors.Select(d => d.RuleId).ToArray();
        var expectedIds = new[]
        {
            "REG-01", "REG-02", "REG-03", "REG-04", "REG-05",
            "REG-06", "REG-07", "REG-08", "REG-09", "REG-10", "REG-11",
        };
        Assert.Equal(expectedIds, ids);

        foreach (var descriptor in descriptors)
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Name));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.DocumentationSource));
            Assert.Equal("1.0.0", descriptor.Version);
        }
    }

    [Fact]
    public void UmbralesPorDefectoCoincidenConCuadro45DelSrs()
    {
        var defaults = RuleThresholds.Default;

        Assert.Equal(10_000.0, defaults.Reg03MinEstimatedRows);
        Assert.Equal(10.0, defaults.Reg03RowsReadToReturnedRatio);
        Assert.Equal(1.0, defaults.MinQueryConsumptionPct);
        Assert.Equal(1_000.0, defaults.Reg04MinEstimatedExecutions);
        Assert.Equal(10.0, defaults.Reg07CardinalityRatio);
        Assert.Equal(50.0, defaults.Reg08RegressionIncreasePct);
        Assert.Equal(100L, defaults.Reg08MinExecutionsPerPlan);
        Assert.Equal(5.0, defaults.Reg10DurationMaxMinRatio);

        var reg03Thresholds = defaults.GetThresholdsForRule("REG-03");
        Assert.Equal(10_000.0, reg03Thresholds["min_estimated_rows"]);
        Assert.Equal(10.0, reg03Thresholds["rows_read_to_returned_ratio"]);
        Assert.Equal(1.0, reg03Thresholds["min_query_consumption_pct"]);

        var reg04Thresholds = defaults.GetThresholdsForRule("REG-04");
        Assert.Equal(1_000.0, reg04Thresholds["min_estimated_executions"]);
        Assert.Equal(1.0, reg04Thresholds["min_query_consumption_pct"]);

        Assert.Empty(defaults.GetThresholdsForRule("REG-01"));
        Assert.Empty(defaults.GetThresholdsForRule("REG-02"));
    }

    [Fact]
    public void ContratoJsonDelHallazgoCumpleConListingB1DelSrsYEsVersionado()
    {
        var contract = new FindingContract(
            SchemaVersion: FindingContract.CurrentSchemaVersion,
            FindingId: "F-000123ABCDEF",
            CatalogVersion: "1.0.0",
            Rule: new FindingRuleContract("REG-01", "Conversión implícita que afecta el plan", "1.0.0"),
            Kind: FindingKind.Senal.ToContractCode(),
            Severity: FindingSeverity.Alta.ToContractCode(),
            EvidenceClass: EvidenceClass.Estimada.ToContractCode(),
            QueryId: 4521,
            PlanId: 9917,
            Nodes: [new FindingNodeContract(5, "Clustered Index Scan")],
            Evidence: new Dictionary<string, string?>
            {
                ["expression"] = "CONVERT_IMPLICIT(nvarchar(20),[Documento],0)=[@d]",
                ["convert_issue"] = "Seek Plan",
            },
            Context: new FindingContextContract(
                Executions: 1840,
                QueryConsumptionPct: 3.2,
                EstimatedRows: 120.0,
                EstimatedRowsRead: 100_000.0),
            ThresholdsUsed: new Dictionary<string, double>(),
            EngineVersion: "16.0.4315.2",
            AnalyzedAt: new DateTimeOffset(2026, 10, 5, 14, 22, 31, TimeSpan.Zero));

        var json = contract.ToJson();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("1.0.0", root.GetProperty("schema_version").GetString());
        Assert.Equal("F-000123ABCDEF", root.GetProperty("finding_id").GetString());
        Assert.Equal("1.0.0", root.GetProperty("catalog_version").GetString());
        Assert.Equal("REG-01", root.GetProperty("rule").GetProperty("id").GetString());
        Assert.Equal("1.0.0", root.GetProperty("rule").GetProperty("version").GetString());
        Assert.Equal("SENAL", root.GetProperty("kind").GetString());
        Assert.Equal("ALTA", root.GetProperty("severity").GetString());
        Assert.Equal("ESTIMADA", root.GetProperty("evidence_class").GetString());
        Assert.Equal(4521, root.GetProperty("query_id").GetInt64());
        Assert.Equal(9917, root.GetProperty("plan_id").GetInt64());
        Assert.Equal(5, root.GetProperty("nodes")[0].GetProperty("node_id").GetInt32());
        Assert.Equal("Clustered Index Scan", root.GetProperty("nodes")[0].GetProperty("physical_op").GetString());
        Assert.Equal("Seek Plan", root.GetProperty("evidence").GetProperty("convert_issue").GetString());
        Assert.Equal("CONVERT_IMPLICIT(nvarchar(20),[Documento],0)=[@d]", root.GetProperty("evidence").GetProperty("expression").GetString());
        Assert.Equal(1840, root.GetProperty("context").GetProperty("executions").GetInt64());
        Assert.Equal(3.2, root.GetProperty("context").GetProperty("query_consumption_pct").GetDouble());
        Assert.Equal("16.0.4315.2", root.GetProperty("engine_version").GetString());

        var roundTrip = FindingContractJson.Deserialize(json);
        Assert.NotNull(roundTrip);
        Assert.Equal(contract.FindingId, roundTrip.FindingId);
        Assert.Equal(contract.Rule.Id, roundTrip.Rule.Id);
    }
}
