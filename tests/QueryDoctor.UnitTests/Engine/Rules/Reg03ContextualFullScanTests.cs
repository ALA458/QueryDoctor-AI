using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Engine.Rules.Catalog;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.UnitTests.Engine.Rules;

/// <summary>
/// Pruebas unitarias positivas, negativas, de frontera y de umbrales personalizados para la regla
/// contextual REG-03 (RF-MOT-05 v1.1, RF-MOT-14, RF-MOT-15, RNF-MAN-02, Cuadro 4.5, Cuadro 4.11, CA-HU05.4).
/// </summary>
public sealed class Reg03ContextualFullScanTests
{
    private static readonly string FixturesDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "plans"));

    private readonly ShowplanXmlParser _parser = new();

    [Fact]
    public void Reg03PlanPositivoEsc03ClusteredIndexScanConPredicadoYRazonMayorA10SeSenalaPorCondicionACaHu054()
    {
        var xml = LoadFixture("ESC03.qs.1.sqlplan");
        var engine = CreateSingleRuleEngine();

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 301,
            QueryStorePlanId: 401,
            PlanIdentifier: "ESC03",
            Thresholds: RuleThresholds.Default,
            QueryExecutionCount: 45,
            QueryConsumptionPct: null));

        var finding = Assert.Single(result.Findings);

        Assert.Equal("REG-03", finding.Rule.Id);
        Assert.Equal("1.0.0", finding.Rule.Version);
        Assert.Equal("Lectura completa costosa (contextual)", finding.Rule.Name);
        Assert.Equal(RuleClass.ClassA, new Reg03CostlyFullScanRule().Descriptor.RuleClass);
        Assert.Equal("SENAL", finding.Kind);
        Assert.Equal("ALTA", finding.Severity);
        Assert.Equal("ESTIMADA", finding.EvidenceClass);

        var node = Assert.Single(finding.Nodes);
        Assert.Equal(1, node.NodeId);
        Assert.Equal("Clustered Index Scan", node.PhysicalOp);

        Assert.Equal("Clustered Index Scan", finding.Evidence["physical_op"]);
        Assert.Equal("true", finding.Evidence["has_predicate"]);
        Assert.Equal("1000000", finding.Evidence["estimated_rows_read"]);
        Assert.Equal("16172.1", finding.Evidence["estimated_rows"]);
        Assert.Equal("61.8349", finding.Evidence["rows_read_to_returned_ratio"]);
        Assert.Equal("predicate_ratio", finding.Evidence["triggered_by"]);
        Assert.Equal("Orders", finding.Evidence["table"]);
        Assert.Equal("PK_Orders", finding.Evidence["index"]);
        Assert.Contains("[Region]=[@region]", finding.Evidence["predicate"], StringComparison.Ordinal);

        Assert.Equal(10_000.0, finding.ThresholdsUsed["min_estimated_rows"]);
        Assert.Equal(10.0, finding.ThresholdsUsed["rows_read_to_returned_ratio"]);
        Assert.Equal(1.0, finding.ThresholdsUsed["min_query_consumption_pct"]);
    }

    [Fact]
    public void Reg03PlanPositivoEsc01IndexScanConPredicadoSeSenalaYExcluyeOperadorKeyLookup()
    {
        var xml = LoadFixture("ESC01.qs.1.sqlplan");
        var engine = CreateSingleRuleEngine();

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 101,
            QueryStorePlanId: 201,
            PlanIdentifier: "ESC01",
            Thresholds: RuleThresholds.Default,
            QueryConsumptionPct: null));

        // En ESC01.qs.1.sqlplan el nodo 3 es un Index Scan con predicado (1 000 000 / 20 = 50 000 > 10)
        // y el nodo 5 es un Clustered Index Seek con Lookup="1" que pertenece a REG-04, no a REG-03.
        var finding = Assert.Single(result.Findings);
        var node = Assert.Single(finding.Nodes);

        Assert.Equal(3, node.NodeId);
        Assert.Equal("Index Scan", node.PhysicalOp);
        Assert.Equal("ALTA", finding.Severity);
        Assert.Equal("50000", finding.Evidence["rows_read_to_returned_ratio"]);
        Assert.Equal("IX_Orders_CustomerCode", finding.Evidence["index"]);
    }

    [Fact]
    public void Reg03PlanPositivoEsc05ScanSinPredicadoSobreTablaGrandeConConsumoRelevanteSeSenalaPorCondicionB()
    {
        var xml = LoadFixture("ESC05.qs.1.sqlplan");
        var engine = CreateSingleRuleEngine();

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 501,
            QueryStorePlanId: 601,
            PlanIdentifier: "ESC05",
            Thresholds: RuleThresholds.Default,
            QueryExecutionCount: 120,
            QueryConsumptionPct: 2.5));

        var finding = Assert.Single(result.Findings);
        var node = Assert.Single(finding.Nodes);

        Assert.Equal(2, node.NodeId);
        Assert.Equal("Clustered Index Scan", node.PhysicalOp);
        Assert.Equal("ALTA", finding.Severity);
        Assert.Equal("false", finding.Evidence["has_predicate"]);
        Assert.Equal("volume_and_consumption", finding.Evidence["triggered_by"]);
        Assert.Equal("1000000", finding.Evidence["volume_rows_evaluated"]);
        Assert.False(finding.Evidence.ContainsKey("predicate"));
    }

    [Fact]
    public void Reg03PlanPositivoTableScanEnHeapConPredicadoYRazonMayorA10EnTablaMedianaEmiteSeveridadMedia()
    {
        const string tableScanWithPredicateXml = """
            <?xml version="1.0" encoding="utf-16"?>
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT Id FROM dbo.AuditHeap WHERE Status = 3" StatementType="SELECT" StatementSubTreeCost="1.2" StatementEstRows="200">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="200" EstimatedRowsRead="5000" TableCardinality="5000" EstimateIO="1.0" EstimateCPU="0.2" AvgRowSize="32" EstimatedTotalSubtreeCost="1.2" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
                          <OutputList />
                          <TableScan Ordered="0">
                            <Object Database="[QD_Lab]" Schema="[dbo]" Table="[AuditHeap]" IndexKind="Heap" Storage="RowStore" />
                            <Predicate>
                              <ScalarOperator ScalarString="[QD_Lab].[dbo].[AuditHeap].[Status]=(3)" />
                            </Predicate>
                          </TableScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var engine = CreateSingleRuleEngine();
        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: tableScanWithPredicateXml,
            QueryStoreQueryId: 701,
            QueryStorePlanId: 801,
            QueryConsumptionPct: null));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("REG-03", finding.Rule.Id);
        Assert.Equal("MEDIA", finding.Severity);
        Assert.Equal("Table Scan", finding.Evidence["physical_op"]);
        Assert.Equal("25", finding.Evidence["rows_read_to_returned_ratio"]);
        Assert.Equal("5000", finding.Evidence["volume_rows_evaluated"]);
        Assert.Equal("predicate_ratio", finding.Evidence["triggered_by"]);
    }

    [Fact]
    public void Reg03PlanPositivoScanConPredicadoMuySelectivoQueLee250MilFilasYDevuelve5EvaluaSeveridadAltaSobreFilasLeidas()
    {
        // Verifica el detalle técnico clave: en un scan con predicado que lee 250 000 filas (TableCardinality)
        // para devolver solo 5 (EstimateRows), el volumen real evaluado para la severidad es 250 000 (>= 100 000 -> ALTA).
        const string selectiveScanOverLargeTableXml = """
            <?xml version="1.0" encoding="utf-16"?>
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT OrderId FROM dbo.Orders WHERE TrackingCode = @code" StatementType="SELECT" StatementSubTreeCost="8.4" StatementEstRows="5">
                      <QueryPlan>
                        <RelOp NodeId="4" PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="5" TableCardinality="250000" EstimateIO="8.0" EstimateCPU="0.4" AvgRowSize="24" EstimatedTotalSubtreeCost="8.4" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
                          <OutputList />
                          <IndexScan Ordered="0">
                            <Object Database="[QD_Lab]" Schema="[dbo]" Table="[Orders]" Index="[PK_Orders]" IndexKind="Clustered" Storage="RowStore" />
                            <Predicate>
                              <ScalarOperator ScalarString="[QD_Lab].[dbo].[Orders].[TrackingCode]=[@code]" />
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

        var engine = CreateSingleRuleEngine();
        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: selectiveScanOverLargeTableXml,
            QueryStoreQueryId: 702,
            QueryStorePlanId: 802,
            QueryConsumptionPct: 0.2));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("ALTA", finding.Severity);
        Assert.Equal("5", finding.Evidence["estimated_rows"]);
        Assert.Equal("250000", finding.Evidence["estimated_rows_read"]);
        Assert.Equal("250000", finding.Evidence["volume_rows_evaluated"]);
        Assert.Equal("50000", finding.Evidence["rows_read_to_returned_ratio"]);
        Assert.Equal(250_000.0, finding.Context.EstimatedRowsRead);
    }

    [Fact]
    public void Reg03PlanPositivoCondicionBConVolumenEntre1xY10xDelUmbralEmiteSeveridadMedia()
    {
        const string mediumVolumeScanXml = """
            <?xml version="1.0" encoding="utf-16"?>
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT OrderId, Amount FROM dbo.Orders" StatementType="SELECT" StatementSubTreeCost="1.8" StatementEstRows="25000">
                      <QueryPlan>
                        <RelOp NodeId="1" PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="25000" EstimatedRowsRead="25000" TableCardinality="25000" EstimateIO="1.6" EstimateCPU="0.2" AvgRowSize="24" EstimatedTotalSubtreeCost="1.8" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
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

        var engine = CreateSingleRuleEngine();
        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: mediumVolumeScanXml,
            QueryStoreQueryId: 703,
            QueryStorePlanId: 803,
            QueryConsumptionPct: 1.5));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("MEDIA", finding.Severity);
        Assert.Equal("volume_and_consumption", finding.Evidence["triggered_by"]);
        Assert.Equal("25000", finding.Evidence["volume_rows_evaluated"]);
    }

    [Fact]
    public void Reg03PlanPositivoCuandoCumpleCondicionAYCondicionBSimultaneamenteReportaTriggeredByCombinado()
    {
        var xml = LoadFixture("ESC03.qs.1.sqlplan");
        var engine = CreateSingleRuleEngine();

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 704,
            QueryStorePlanId: 804,
            QueryConsumptionPct: 4.2));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("predicate_ratio_and_volume", finding.Evidence["triggered_by"]);
    }

    [Fact]
    public void Reg03PlanNegativoEsc06ScanSinPredicadoSobreTablaPequenaNoSeSenalaCaHu054RnfMan02()
    {
        // ESC06.qs.1.sqlplan contiene en NodeId="139" un Clustered Index Scan sin predicado
        // sobre dbo.Tiny (TableCardinality="3", EstimateRows="3", EstimatedRowsRead="3").
        var xml = LoadFixture("ESC06.qs.1.sqlplan");
        var engine = CreateSingleRuleEngine();

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 601,
            QueryStorePlanId: 701,
            Thresholds: RuleThresholds.Default,
            QueryConsumptionPct: 50.0));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Reg03PlanNegativoScanConPredicadoPeroRazonMenorOIgualAlUmbralYVolumenBajoNoSeSenalaRnfMan02()
    {
        // Razón exacta 5000 / 500 = 10.0 (no supera estrictamente el umbral 10.0) y volumen 5000 < 10 000.
        const string boundaryRatioScanXml = """
            <?xml version="1.0" encoding="utf-16"?>
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT Id FROM dbo.SmallOrders WHERE Status = 1" StatementType="SELECT" StatementSubTreeCost="0.5" StatementEstRows="500">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="500" EstimatedRowsRead="5000" TableCardinality="5000" EstimateIO="0.4" EstimateCPU="0.1" AvgRowSize="24" EstimatedTotalSubtreeCost="0.5" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
                          <OutputList />
                          <IndexScan Ordered="0">
                            <Object Database="[QD_Lab]" Schema="[dbo]" Table="[SmallOrders]" Index="[PK_SmallOrders]" IndexKind="Clustered" Storage="RowStore" />
                            <Predicate>
                              <ScalarOperator ScalarString="[QD_Lab].[dbo].[SmallOrders].[Status]=(1)" />
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

        var engine = CreateSingleRuleEngine();
        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: boundaryRatioScanXml,
            QueryStoreQueryId: 705,
            QueryStorePlanId: 805,
            QueryConsumptionPct: 10.0));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Reg03PlanNegativoEsc05ScanSinPredicadoSobreTablaGrandePeroSinConsumoMinimoNoSeSenalaRfMot05()
    {
        var xml = LoadFixture("ESC05.qs.1.sqlplan");
        var engine = CreateSingleRuleEngine();

        var lowConsumptionResult = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 706,
            QueryStorePlanId: 806,
            QueryConsumptionPct: 0.5));

        Assert.Empty(lowConsumptionResult.Findings);

        var nullConsumptionResult = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 706,
            QueryStorePlanId: 806,
            QueryConsumptionPct: null));

        Assert.Empty(nullConsumptionResult.Findings);
    }

    [Theory]
    [InlineData("ESC02.qs.1.sqlplan")]
    [InlineData("ESC04.qs.1.sqlplan")]
    [InlineData("ESC04B.qs.1.sqlplan")]
    public void Reg03PlanesNegativosConIndexSeekYKeyLookupNoProducenHallazgosRnfMan02(string fixtureName)
    {
        var xml = LoadFixture(fixtureName);
        var engine = CreateSingleRuleEngine();

        var result = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: xml,
            QueryStoreQueryId: 707,
            QueryStorePlanId: 807,
            QueryConsumptionPct: 25.0));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Reg03UmbralesPersonalizadosOltpYOlapAjustanDisparoYSeveridadProporcionalmenteRfMot15()
    {
        var engine = CreateSingleRuleEngine();

        // Perfil OLAP (umbral de razón elevado a 100): ESC03 con razón 61.83 y sin consumo ya no se señala
        var olapThresholds = RuleThresholds.Default with
        {
            Reg03MinEstimatedRows = 50_000.0,
            Reg03RowsReadToReturnedRatio = 100.0,
        };

        var esc03OlapResult = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: LoadFixture("ESC03.qs.1.sqlplan"),
            QueryStoreQueryId: 708,
            QueryStorePlanId: 808,
            Thresholds: olapThresholds,
            QueryConsumptionPct: null));

        Assert.Empty(esc03OlapResult.Findings);

        // Perfil OLTP (Reg03MinEstimatedRows = 2 000): un scan de 25 000 filas supera 10x (20 000) y pasa a severidad ALTA
        const string scan25kRowsXml = """
            <?xml version="1.0" encoding="utf-16"?>
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT OrderId FROM dbo.Orders" StatementType="SELECT" StatementSubTreeCost="1.8" StatementEstRows="25000">
                      <QueryPlan>
                        <RelOp NodeId="1" PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="25000" EstimatedRowsRead="25000" TableCardinality="25000" EstimateIO="1.6" EstimateCPU="0.2" AvgRowSize="24" EstimatedTotalSubtreeCost="1.8" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
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

        var oltpThresholds = RuleThresholds.Default with
        {
            Reg03MinEstimatedRows = 2_000.0,
        };

        var oltpResult = engine.Analyze(new PlanAnalysisRequest(
            ShowplanXml: scan25kRowsXml,
            QueryStoreQueryId: 709,
            QueryStorePlanId: 809,
            Thresholds: oltpThresholds,
            QueryConsumptionPct: 1.5));

        var oltpFinding = Assert.Single(oltpResult.Findings);
        Assert.Equal("ALTA", oltpFinding.Severity);
        Assert.Equal(2_000.0, oltpFinding.ThresholdsUsed["min_estimated_rows"]);
    }

    private DeterministicRuleEngine CreateSingleRuleEngine() =>
        new(_parser, new VersionedRuleCatalog([new Reg03CostlyFullScanRule()]), RuleThresholds.Default);

    private static string LoadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(FixturesDirectory, fileName));
}
