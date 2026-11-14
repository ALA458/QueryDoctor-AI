using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.UnitTests.Engine.Parsing;

/// <summary>
/// Pruebas de seguridad XML (OWASP A05), compatibilidad hacia adelante (RNF-COM-02)
/// y manejo de planes inválidos produciendo <c>REG-11</c> sin detener el lote (RF-MOT-16, OWASP A10).
/// </summary>
public sealed class ShowplanXmlParserSecurityAndResilienceTests
{
    private readonly ShowplanXmlParser _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n  ")]
    public void ParseConXmlNuloOVacioProduceReg11EmptyOrNullXml(string? invalidXml)
    {
        var result = _sut.Parse(invalidXml, "plan-vacio");

        Assert.False(result.IsSuccess);
        Assert.Equal(PlanAnalysisStatus.NotAnalyzable, result.Status);
        Assert.Null(result.Plan);
        Assert.NotNull(result.Finding);
        Assert.Equal("REG-11", result.Finding.RuleId);
        Assert.Equal(PlanParseFailureReason.EmptyOrNullXml, result.Finding.Reason);
        Assert.Equal("plan-vacio", result.Finding.PlanIdentifier);
    }

    [Fact]
    public void ParseConXmlTruncadoOMalFormadoProduceReg11MalformedXmlSinLanzarExcepcion()
    {
        const string truncatedXml =
            """<ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2"><BatchSequence><Batch><Statements><StmtSimple StatementId="1" """;

        var result = _sut.Parse(truncatedXml, "plan-truncado");

        Assert.False(result.IsSuccess);
        Assert.Equal(PlanAnalysisStatus.NotAnalyzable, result.Status);
        Assert.NotNull(result.Finding);
        Assert.Equal("REG-11", result.Finding.RuleId);
        Assert.Equal(PlanParseFailureReason.MalformedXml, result.Finding.Reason);
    }

    [Fact]
    public void ParseConElementoRaizInesperadoProduceReg11UnexpectedRootElement()
    {
        const string wrongRootXml = """<OtroDocumento Version="1.0"><Nodo/></OtroDocumento>""";

        var result = _sut.Parse(wrongRootXml, "plan-raiz-invalida");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Finding);
        Assert.Equal("REG-11", result.Finding.RuleId);
        Assert.Equal(PlanParseFailureReason.UnexpectedRootElement, result.Finding.Reason);
    }

    [Fact]
    public void ParseConDeclaracionDtdOXxeRechazaYProduceReg11DtdProhibited()
    {
        const string xxeXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE ShowPlanXML [
              <!ENTITY xxe SYSTEM "file:///etc/passwd">
            ]>
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0">
              <BatchSequence>&xxe;</BatchSequence>
            </ShowPlanXML>
            """;

        var result = _sut.Parse(xxeXml, "plan-xxe");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Finding);
        Assert.Equal("REG-11", result.Finding.RuleId);
        Assert.Equal(PlanParseFailureReason.DtdProhibited, result.Finding.Reason);
    }

    [Fact]
    public void ParseCuandoSuperaMaxCharactersInDocumentProduceReg11SizeLimitExceeded()
    {
        var parser = new ShowplanXmlParser(new PlanParserOptions { MaxCharactersInDocument = 256 });
        var oversizedXml =
            $"""<ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0"><BatchSequence><Batch><Statements><StmtSimple StatementId="1" StatementText="{new string('A', 400)}"/></Statements></Batch></BatchSequence></ShowPlanXML>""";

        var result = parser.Parse(oversizedXml, "plan-gigante");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Finding);
        Assert.Equal("REG-11", result.Finding.RuleId);
        Assert.Equal(PlanParseFailureReason.SizeLimitExceeded, result.Finding.Reason);
    }

    [Fact]
    public void ParseCuandoSuperaMaxElementDepthProduceReg11MaxDepthExceeded()
    {
        var parser = new ShowplanXmlParser(new PlanParserOptions { MaxElementDepth = 12 });
        var nested = string.Concat(Enumerable.Repeat("<RelOp NodeId=\"1\" PhysicalOp=\"Filter\" LogicalOp=\"Filter\" EstimateRows=\"1\" EstimateIO=\"0\" EstimateCPU=\"0\" AvgRowSize=\"8\" EstimatedTotalSubtreeCost=\"0\" Parallel=\"0\" EstimateRebinds=\"0\" EstimateRewinds=\"0\"><Filter>", 10))
                   + string.Concat(Enumerable.Repeat("</Filter></RelOp>", 10));
        var deepXml =
            $"""<ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0"><BatchSequence><Batch><Statements><StmtSimple StatementId="1"><QueryPlan>{nested}</QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>""";

        var result = parser.Parse(deepXml, "plan-profundo");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Finding);
        Assert.Equal("REG-11", result.Finding.RuleId);
        Assert.Equal(PlanParseFailureReason.MaxDepthExceeded, result.Finding.Reason);
    }

    [Fact]
    public void ParseConElementosYAtributosDesconocidosDeVersionesFuturasLosIgnoraYParseaElArbol()
    {
        const string futurePlanXml =
            """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="2.999" Build="18.0.1000.0" FutureRootAttr="true">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementType="SELECT" StatementText="SELECT 1" StatementSubTreeCost="0.01" StatementEstRows="1" FutureStmtAttr="42">
                      <QueryPlan CachedPlanSize="16">
                        <FutureQuantumOptimizerInfo Hint="AI_BOOST">
                          <NestedUnknownElement Value="123" />
                        </FutureQuantumOptimizerInfo>
                        <MissingIndexes>
                          <MissingIndexGroup Impact="85.5">
                            <MissingIndex Database="[DB1]" Schema="[dbo]" Table="[T1]">
                              <ColumnGroup Usage="EQUALITY">
                                <Column Name="[C1]" ColumnId="1" />
                              </ColumnGroup>
                              <ColumnGroup Usage="INCLUDE">
                                <Column Name="[C2]" ColumnId="2" />
                              </ColumnGroup>
                            </MissingIndex>
                          </MissingIndexGroup>
                        </MissingIndexes>
                        <RelOp NodeId="0" PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="100" EstimatedRowsRead="100" TableCardinality="100" EstimateIO="0.01" EstimateCPU="0.001" AvgRowSize="16" EstimatedTotalSubtreeCost="0.011" Parallel="0" EstimateRebinds="0" EstimateRewinds="0" FutureOpAttr="x">
                          <OutputList>
                            <ColumnReference Database="[DB1]" Schema="[dbo]" Table="[T1]" Column="[C1]" />
                          </OutputList>
                          <Warnings NoJoinPredicate="1">
                            <ColumnsWithNoStatistics />
                            <MemoryGrantWarning GrantWarningKind="Excessive Grant" />
                            <FutureCustomWarning Severity="Low" />
                          </Warnings>
                          <TableScan Ordered="0">
                            <Object Database="[DB1]" Schema="[dbo]" Table="[T1]" IndexKind="Heap" Storage="RowStore" />
                            <FutureOperatorChild>
                              <DeepUnknown />
                            </FutureOperatorChild>
                          </TableScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var result = _sut.Parse(futurePlanXml, "plan-futuro");

        Assert.True(result.IsSuccess);
        Assert.Equal("18.0.1000.0", result.Plan!.EngineVersion);
        Assert.Equal("2.999", result.Plan.SchemaVersion);

        var stmt = Assert.Single(result.Plan.Statements);
        var mi = Assert.Single(Assert.Single(stmt.MissingIndexGroups).Indexes);
        Assert.Equal(["C1"], mi.EqualityColumns);
        Assert.Equal(["C2"], mi.IncludeColumns);

        var op = stmt.RootOperator!;
        Assert.Equal("Table Scan", op.PhysicalOp);
        Assert.Equal("T1", op.TargetObject?.Table);
        Assert.Equal("C1", Assert.Single(op.OutputColumns).Column);
        Assert.Contains(op.Warnings, w => w.Kind == PlanWarningKind.NoJoinPredicate);
        Assert.Contains(op.Warnings, w => w.Kind == PlanWarningKind.ColumnsWithNoStatistics);
        Assert.Contains(op.Warnings, w => w.Kind == PlanWarningKind.MemoryGrantWarning && w.Details == "Excessive Grant");
        Assert.Contains(op.Warnings, w => w.Kind == PlanWarningKind.Other && w.Details == "FutureCustomWarning");
    }

    [Fact]
    public void ParseBatchConPlanesValidosEInvalidosMezcladosProduceReg11EnLosInvalidosSinDetenerElLote()
    {
        const string validXml1 =
            """<ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564"><BatchSequence><Batch><Statements><StmtSimple StatementId="1" StatementType="SELECT" StatementText="SELECT 1"><QueryPlan><RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateIO="0" EstimateCPU="0.000001" AvgRowSize="9" EstimatedTotalSubtreeCost="0.000001" Parallel="0" EstimateRebinds="0" EstimateRewinds="0"><OutputList/><ConstantScan/></RelOp></QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>""";

        const string validXml2 =
            """<ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2"><BatchSequence><Batch><Statements><StmtSimple StatementId="1" StatementType="SELECT" StatementText="SELECT 2"><QueryPlan><RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateIO="0" EstimateCPU="0.000001" AvgRowSize="9" EstimatedTotalSubtreeCost="0.000001" Parallel="0" EstimateRebinds="0" EstimateRewinds="0"><OutputList/><ConstantScan/></RelOp></QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>""";

        var batch = new[]
        {
            new PlanInput("plan-1-ok", validXml1, FallbackEngineVersion: "15.0.2000.5"),
            new PlanInput("plan-2-corrupto", "<ShowPlanXML><BatchSequence><sin-cerrar"),
            new PlanInput("plan-3-vacio", "   "),
            new PlanInput("plan-4-dtd", "<!DOCTYPE ShowPlanXML><ShowPlanXML/>"),
            new PlanInput("plan-5-ok", validXml2),
        };

        var results = _sut.ParseBatch(batch);

        Assert.Equal(5, results.Count);

        Assert.True(results[0].IsSuccess);
        Assert.Equal("plan-1-ok", results[0].PlanIdentifier);
        Assert.Equal("15.0.2000.5", results[0].Plan!.EngineVersion);

        Assert.False(results[1].IsSuccess);
        Assert.Equal("REG-11", results[1].Finding!.RuleId);
        Assert.Equal(PlanParseFailureReason.MalformedXml, results[1].Finding!.Reason);

        Assert.False(results[2].IsSuccess);
        Assert.Equal("REG-11", results[2].Finding!.RuleId);
        Assert.Equal(PlanParseFailureReason.EmptyOrNullXml, results[2].Finding!.Reason);

        Assert.False(results[3].IsSuccess);
        Assert.Equal("REG-11", results[3].Finding!.RuleId);
        Assert.Equal(PlanParseFailureReason.DtdProhibited, results[3].Finding!.Reason);

        Assert.True(results[4].IsSuccess);
        Assert.Equal("plan-5-ok", results[4].PlanIdentifier);
        Assert.Equal("16.0.4315.2", results[4].Plan!.EngineVersion);
    }
}
