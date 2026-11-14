using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Engine.Rules.Catalog;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Infrastructure;

namespace QueryDoctor.UnitTests.Engine.Rules;

/// <summary>
/// S1-14: suite de pruebas positivas y negativas (RNF-MAN-02) para las reglas Debe:
/// - REG-01 (RF-MOT-04, CA-HU05.1): Conversión implícita que afecta el plan
/// - REG-02 (RF-MOT-07, CA-HU06.1, CA-HU06.2, CA-HU06.3, RNF-FUN-04): Índice faltante + advertencia de costo de escritura
/// - REG-04 (RF-MOT-06, CA-HU05.2): Búsqueda por clave excesiva (contextual)
/// </summary>
public sealed class MustRulesEvaluationTests
{
    private static readonly string FixturesDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "plans"));

    private readonly ShowplanXmlParser _parser = new();

    [Fact]
    public void Reg01PlanPositivoConPlanAffectingConvertReportaConvertIssueExpresionYNodoDelPlanCaHu051()
    {
        var xml = LoadFixture("ESC01.qs.1.sqlplan");
        var engine = CreateSingleRuleEngine(new Reg01ImplicitConversionRule());

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 101,
            QueryStorePlanId: 201,
            PlanIdentifier: "ESC01.qs.1",
            QueryExecutionCount: 300,
            QueryConsumptionPct: 4.5));

        var finding = Assert.Single(result.Findings);

        Assert.Equal("REG-01", finding.Rule.Id);
        Assert.Equal("Conversión implícita que afecta el plan", finding.Rule.Name);
        Assert.Equal("1.0.0", finding.Rule.Version);
        Assert.Equal("SENAL", finding.Kind);
        Assert.Equal("ALTA", finding.Severity);
        Assert.Equal("ESTIMADA", finding.EvidenceClass);

        var node = Assert.Single(finding.Nodes);
        Assert.Equal(3, node.NodeId);
        Assert.Equal("Index Scan", node.PhysicalOp);

        Assert.Equal("Seek Plan", finding.Evidence["convert_issue"]);
        Assert.Equal(
            "CONVERT_IMPLICIT(nvarchar(20),[QD_Lab].[dbo].[Orders].[CustomerCode],0)=[@code]",
            finding.Evidence["expression"]);
        Assert.Equal("nvarchar", finding.Evidence["target_data_type"]);
        Assert.Equal("CustomerCode", finding.Evidence["column"]);
        Assert.Equal("Orders", finding.Evidence["table"]);
        Assert.Equal("IX_Orders_CustomerCode", finding.Evidence["index"]);
        Assert.Empty(finding.ThresholdsUsed);
    }

    [Fact]
    public void Reg01PlanPositivoConCardinalityEstimateAsignaSeveridadMediaSegunCuadro411()
    {
        const string cardinalityWarningXml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementText="SELECT OrderId FROM dbo.Orders WHERE TRY_CONVERT(int, CustomerCode) = 10" StatementId="1" StatementType="SELECT" StatementSubTreeCost="1.2" StatementEstRows="10">
                      <QueryPlan>
                        <Warnings>
                          <PlanAffectingConvert ConvertIssue="Cardinality Estimate" Expression="CONVERT_IMPLICIT(nvarchar(20),[QD_Lab].[dbo].[Orders].[CustomerCode],0)" />
                        </Warnings>
                        <RelOp NodeId="1" PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="10" EstimatedRowsRead="1000" EstimateIO="1.0" EstimateCPU="0.2" AvgRowSize="16" EstimatedTotalSubtreeCost="1.2" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
                          <OutputList />
                          <IndexScan Ordered="0">
                            <Object Database="[QD_Lab]" Schema="[dbo]" Table="[Orders]" Index="[PK_Orders]" IndexKind="Clustered" Storage="RowStore" />
                          </IndexScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var engine = CreateSingleRuleEngine(new Reg01ImplicitConversionRule());
        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: cardinalityWarningXml,
            QueryStoreQueryId: 102,
            QueryStorePlanId: 202));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("REG-01", finding.Rule.Id);
        Assert.Equal("MEDIA", finding.Severity);
        Assert.Equal("Cardinality Estimate", finding.Evidence["convert_issue"]);
        Assert.Equal(1, Assert.Single(finding.Nodes).NodeId);
    }

    [Theory]
    [InlineData("ESC02.qs.1.sqlplan")]
    [InlineData("ESC03.qs.1.sqlplan")]
    [InlineData("ESC04.qs.1.sqlplan")]
    [InlineData("ESC05.qs.1.sqlplan")]
    public void Reg01PlanesNegativosSinPlanAffectingConvertNoProducenHallazgosRnfMan02(string fixtureName)
    {
        var xml = LoadFixture(fixtureName);
        var engine = CreateSingleRuleEngine(new Reg01ImplicitConversionRule());

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 1,
            QueryStorePlanId: 1,
            QueryConsumptionPct: 10.0));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Reg02PlanPositivoConMissingIndexesDecodificaTablaColumnasImpactoYAdvertenciaSinEjecutarCreateIndexCaHu06()
    {
        var xml = LoadFixture("ESC03.qs.1.sqlplan");
        var engine = CreateSingleRuleEngine(new Reg02MissingIndexRule());

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 301,
            QueryStorePlanId: 401,
            PlanIdentifier: "ESC03.qs.1",
            QueryExecutionCount: 150,
            QueryConsumptionPct: 8.4));

        var finding = Assert.Single(result.Findings);

        Assert.Equal("REG-02", finding.Rule.Id);
        Assert.Equal("Índice faltante", finding.Rule.Name);
        Assert.Equal("SENAL", finding.Kind);
        Assert.Equal("ALTA", finding.Severity);
        Assert.Equal("ESTIMADA", finding.EvidenceClass);

        // CA-HU06.1: tabla, columnas de igualdad, desigualdad e incluidas e impacto estimado
        Assert.Equal("QD_Lab", finding.Evidence["database"]);
        Assert.Equal("dbo", finding.Evidence["schema"]);
        Assert.Equal("Orders", finding.Evidence["table"]);
        Assert.Equal("Region", finding.Evidence["equality_columns"]);
        Assert.Equal("Amount", finding.Evidence["inequality_columns"]);
        Assert.Equal(string.Empty, finding.Evidence["include_columns"]);
        Assert.Equal("99.277", finding.Evidence["impact_pct"]);

        var node = Assert.Single(finding.Nodes);
        Assert.Equal(1, node.NodeId);
        Assert.Equal("Clustered Index Scan", node.PhysicalOp);

        // CA-HU06.3 y RF-MOT-07: advertencia explícita de costo de escritura e índices existentes
        Assert.Equal(
            Reg02MissingIndexRule.WriteCostAndExistingIndexesWarning,
            finding.Evidence["write_cost_warning"]);
        Assert.Contains("costo de escritura", finding.Evidence["write_cost_warning"], StringComparison.OrdinalIgnoreCase);

        // CA-HU06.2 y RNF-FUN-04: no incluye ninguna sentencia CREATE INDEX lista para ejecutar
        var json = finding.ToJson();
        Assert.DoesNotContain("CREATE INDEX", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE NONCLUSTERED INDEX", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reg02PlanConIncludeColumnsEImpactoMedioDecodificaIncludeYAsignaSeveridadMedia()
    {
        const string missingIndexWithIncludeXml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementText="SELECT OrderId, Amount, CreatedAt FROM dbo.Orders WHERE CustomerId = @id" StatementId="1" StatementType="SELECT" StatementSubTreeCost="5.0" StatementEstRows="200">
                      <QueryPlan>
                        <MissingIndexes>
                          <MissingIndexGroup Impact="54.5">
                            <MissingIndex Database="[QD_Lab]" Schema="[dbo]" Table="[Orders]">
                              <ColumnGroup Usage="EQUALITY">
                                <Column Name="[CustomerId]" ColumnId="2" />
                              </ColumnGroup>
                              <ColumnGroup Usage="INCLUDE">
                                <Column Name="[Amount]" ColumnId="6" />
                                <Column Name="[CreatedAt]" ColumnId="7" />
                              </ColumnGroup>
                            </MissingIndex>
                          </MissingIndexGroup>
                        </MissingIndexes>
                        <RelOp NodeId="2" PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="200" EstimatedRowsRead="10000" EstimateIO="4.5" EstimateCPU="0.5" AvgRowSize="24" EstimatedTotalSubtreeCost="5.0" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
                          <OutputList />
                          <IndexScan Ordered="0">
                            <Object Database="[QD_Lab]" Schema="[dbo]" Table="[Orders]" Index="[PK_Orders]" IndexKind="Clustered" Storage="RowStore" />
                          </IndexScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var engine = CreateSingleRuleEngine(new Reg02MissingIndexRule());
        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: missingIndexWithIncludeXml,
            QueryStoreQueryId: 302,
            QueryStorePlanId: 402));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("REG-02", finding.Rule.Id);
        Assert.Equal("MEDIA", finding.Severity);
        Assert.Equal("CustomerId", finding.Evidence["equality_columns"]);
        Assert.Equal(string.Empty, finding.Evidence["inequality_columns"]);
        Assert.Equal("Amount, CreatedAt", finding.Evidence["include_columns"]);
        Assert.Equal("54.5", finding.Evidence["impact_pct"]);
    }

    [Theory]
    [InlineData("ESC01.qs.1.sqlplan")]
    [InlineData("ESC02.qs.1.sqlplan")]
    [InlineData("ESC04.qs.1.sqlplan")]
    [InlineData("ESC05.qs.1.sqlplan")]
    public void Reg02PlanesNegativosSinMissingIndexesNoProducenHallazgosRnfMan02(string fixtureName)
    {
        var xml = LoadFixture(fixtureName);
        var engine = CreateSingleRuleEngine(new Reg02MissingIndexRule());

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 2,
            QueryStorePlanId: 2,
            QueryConsumptionPct: 10.0));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Reg04PlanPositivoConKeyLookupSobreUmbralPorDefectoReportaEjecucionesEstimadasYUmbralAplicadoCaHu052()
    {
        // Basado en ESC02.qs.1.sqlplan pero con EstimateRebinds="1499" (1 500 ejecuciones estimadas >= 1 000 por defecto)
        var xmlWith1500Lookups = LoadFixture("ESC02.qs.1.sqlplan")
            .Replace("EstimateRebinds=\"19\"", "EstimateRebinds=\"1499\"", StringComparison.Ordinal);

        var engine = CreateSingleRuleEngine(new Reg04ExcessiveKeyLookupRule());

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xmlWith1500Lookups,
            QueryStoreQueryId: 501,
            QueryStorePlanId: 601,
            PlanIdentifier: "ESC02-1500",
            Thresholds: RuleThresholds.Default,
            QueryExecutionCount: 80,
            QueryConsumptionPct: 3.8));

        var finding = Assert.Single(result.Findings);

        Assert.Equal("REG-04", finding.Rule.Id);
        Assert.Equal("Búsqueda por clave excesiva (contextual)", finding.Rule.Name);
        Assert.Equal("SENAL", finding.Kind);
        Assert.Equal("MEDIA", finding.Severity);
        Assert.Equal("ESTIMADA", finding.EvidenceClass);

        var node = Assert.Single(finding.Nodes);
        Assert.Equal(3, node.NodeId);
        Assert.Equal("Clustered Index Seek", node.PhysicalOp);

        // CA-HU05.2: número estimado de ejecuciones y umbral aplicado
        Assert.Equal("1500", finding.Evidence["estimated_executions"]);
        Assert.Equal("1000", finding.Evidence["min_estimated_executions_threshold"]);
        Assert.Equal("1", finding.Evidence["min_query_consumption_pct_threshold"]);
        Assert.Equal("Key Lookup", finding.Evidence["lookup_kind"]);
        Assert.Equal("Orders", finding.Evidence["table"]);
        Assert.Equal("PK_Orders", finding.Evidence["index"]);
        Assert.Equal("Amount, CreatedAt", finding.Evidence["output_columns"]);

        Assert.Equal(1_000.0, finding.ThresholdsUsed["min_estimated_executions"]);
        Assert.Equal(1.0, finding.ThresholdsUsed["min_query_consumption_pct"]);
    }

    [Fact]
    public void Reg04FixtureRealEsc02ConUmbralCalibradoDetectaKeyLookupEnNodo3()
    {
        var xml = LoadFixture("ESC02.qs.1.sqlplan");
        var engine = CreateSingleRuleEngine(new Reg04ExcessiveKeyLookupRule());
        var calibratedThresholds = RuleThresholds.Default with
        {
            Reg04MinEstimatedExecutions = 15.0,
        };

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 502,
            QueryStorePlanId: 602,
            Thresholds: calibratedThresholds,
            QueryConsumptionPct: 2.5));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("REG-04", finding.Rule.Id);
        Assert.Equal("20", finding.Evidence["estimated_executions"]);
        Assert.Equal("15", finding.Evidence["min_estimated_executions_threshold"]);
        Assert.Equal(15.0, finding.ThresholdsUsed["min_estimated_executions"]);
        Assert.Equal(3, Assert.Single(finding.Nodes).NodeId);
    }

    [Fact]
    public void Reg04PlanNegativoKeyLookupPorDebajoDelUmbralDeEjecucionesNoSeSenalaRnfMan02()
    {
        // ESC02.qs.1.sqlplan tiene 20 ejecuciones estimadas (19 rebinds + 1), menor que el umbral por defecto 1 000.
        var xml = LoadFixture("ESC02.qs.1.sqlplan");
        var engine = CreateSingleRuleEngine(new Reg04ExcessiveKeyLookupRule());

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 503,
            QueryStorePlanId: 603,
            Thresholds: RuleThresholds.Default,
            QueryConsumptionPct: 5.0));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Reg04PlanNegativoKeyLookupAltoPeroConsultaDebajoDelPorcentajeMinimoDeConsumoOSinConsumoNoSeSenalaRfMot06()
    {
        var xmlWith1500Lookups = LoadFixture("ESC02.qs.1.sqlplan")
            .Replace("EstimateRebinds=\"19\"", "EstimateRebinds=\"1499\"", StringComparison.Ordinal);

        var engine = CreateSingleRuleEngine(new Reg04ExcessiveKeyLookupRule());

        // Caso 1: consumo 0.4 % < 1.0 % mínimo configurado
        var lowConsumptionResult = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xmlWith1500Lookups,
            QueryStoreQueryId: 504,
            QueryStorePlanId: 604,
            Thresholds: RuleThresholds.Default,
            QueryConsumptionPct: 0.4));

        Assert.Empty(lowConsumptionResult.Findings);

        // Caso 2: QueryConsumptionPct nulo (Opción A aprobada)
        var nullConsumptionResult = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xmlWith1500Lookups,
            QueryStoreQueryId: 504,
            QueryStorePlanId: 604,
            Thresholds: RuleThresholds.Default,
            QueryConsumptionPct: null));

        Assert.Empty(nullConsumptionResult.Findings);
    }

    [Theory]
    [InlineData("ESC03.qs.1.sqlplan")]
    [InlineData("ESC04.qs.1.sqlplan")]
    [InlineData("ESC05.qs.1.sqlplan")]
    public void Reg04PlanesNegativosSinKeyLookupNoProducenHallazgosRnfMan02(string fixtureName)
    {
        var xml = LoadFixture(fixtureName);
        var engine = CreateSingleRuleEngine(new Reg04ExcessiveKeyLookupRule());

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 3,
            QueryStorePlanId: 3,
            Thresholds: RuleThresholds.Default with { Reg04MinEstimatedExecutions = 1.0 },
            QueryConsumptionPct: 10.0));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void CatalogoYContenedorDiDescubrenYEjecutanAutomaticamenteReg01Reg02YReg04RnfMan01()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RuleThresholds:Reg04MinEstimatedExecutions"] = "15",
                ["RuleThresholds:MinQueryConsumptionPct"] = "1.0",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<IRuleCatalog>();
        var activeRuleIds = catalog.GetActiveRules().Select(r => r.Descriptor.RuleId).ToArray();

        Assert.Equal(["REG-01", "REG-02", "REG-04"], activeRuleIds);

        var engine = provider.GetRequiredService<IRuleEngine>();

        // En ESC01.qs.1.sqlplan están presentes tanto PlanAffectingConvert (REG-01) como el Key Lookup con 20 ejecuciones (REG-04 con umbral 15)
        var esc01Result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: LoadFixture("ESC01.qs.1.sqlplan"),
            QueryStoreQueryId: 10,
            QueryStorePlanId: 100,
            QueryConsumptionPct: 5.0));

        Assert.Equal(
            ["REG-01", "REG-04"],
            esc01Result.Findings.Select(f => f.Rule.Id).ToArray());

        // En ESC03.qs.1.sqlplan está presente MissingIndexes (REG-02)
        var esc03Result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: LoadFixture("ESC03.qs.1.sqlplan"),
            QueryStoreQueryId: 30,
            QueryStorePlanId: 300,
            QueryConsumptionPct: 5.0));

        Assert.Equal(
            ["REG-02"],
            esc03Result.Findings.Select(f => f.Rule.Id).ToArray());
    }

    private DeterministicRuleEngine CreateSingleRuleEngine(IRule rule) =>
        new(_parser, new VersionedRuleCatalog([rule]), RuleThresholds.Default);

    private static string LoadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(FixturesDirectory, fileName));
}
