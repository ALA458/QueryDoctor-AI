using System.Globalization;
using System.Text;
using System.Text.Json;
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Masking;
using QueryDoctor.Application.Queries;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Queries;

public sealed class GetQueryPlanTreeTests
{
    private const string ShowplanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class StubPlanSourceReader(QueryPlanTreeSource? source) : IQuerySourceReader
    {
        public Task<IReadOnlyList<QueryListSourceRow>> ListAsync(
            Guid? connectionId,
            TimeSpan windowDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QueryListSourceRow>>([]);

        public Task<QueryDetailSource?> GetDetailAsync(
            Guid queryId,
            TimeSpan windowDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<QueryDetailSource?>(null);

        public Task<QueryPlanTreeSource?> GetPlanTreeAsync(
            Guid queryId,
            Guid planId,
            CancellationToken cancellationToken = default)
        {
            if (source is not null && source.QueryId == queryId && source.PlanId == planId)
            {
                return Task.FromResult<QueryPlanTreeSource?>(source);
            }

            return Task.FromResult<QueryPlanTreeSource?>(null);
        }
    }

    private static string BuildSampleShowplanXml() =>
        $"""
        <ShowPlanXML xmlns="{ShowplanNs}" Version="1.564" Build="16.0.1000.6">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple StatementId="1" StatementType="SELECT" StatementSubTreeCost="2.5" StatementEstRows="45" StatementOptmLevel="FULL" StatementText="SELECT p.Id FROM dbo.Pedidos p WHERE p.Nit = '900123456-7' AND p.Monto &gt; 85000">
                  <QueryPlan DegreeOfParallelism="2">
                    <ParameterList>
                      <ColumnReference Column="@pNit" ParameterDataType="varchar(20)" ParameterCompiledValue="'900123456-7'" />
                    </ParameterList>
                    <RelOp NodeId="0" PhysicalOp="Nested Loops" LogicalOp="Inner Join" EstimateRows="45" EstimateIO="0" EstimateCPU="0.1" AvgRowSize="32" EstimatedTotalSubtreeCost="2.5" Parallel="1" EstimateRebinds="0" EstimateRewinds="0">
                      <NestedLoops>
                        <RelOp NodeId="1" PhysicalOp="Index Seek" LogicalOp="Index Seek" EstimateRows="45" EstimateIO="0.2" EstimateCPU="0.05" AvgRowSize="16" EstimatedTotalSubtreeCost="0.5" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
                          <Warnings>
                            <PlanAffectingConvert ConvertIssue="Cardinality Estimate" Expression="CONVERT(varchar(20), p.Nit, 0) = '900123456-7'" />
                          </Warnings>
                          <IndexScan Ordered="1" ScanDirection="FORWARD">
                            <Object Database="[Ventas]" Schema="[dbo]" Table="[Pedidos]" Index="[IX_Pedidos_Nit]" />
                            <Predicate>
                              <ScalarOperator ScalarString="p.Nit = '900123456-7'" />
                            </Predicate>
                          </IndexScan>
                        </RelOp>
                        <RelOp NodeId="2" PhysicalOp="Key Lookup" LogicalOp="Key Lookup" EstimateRows="45" EstimateIO="1.6" EstimateCPU="0.05" AvgRowSize="24" EstimatedTotalSubtreeCost="1.8" Parallel="0" EstimateRebinds="44" EstimateRewinds="0" Lookup="1">
                          <Warnings>
                            <SpillToTempDb SpillLevel="1" SpilledThreadCount="2" />
                          </Warnings>
                          <IndexScan Lookup="1">
                            <Object Database="[Ventas]" Schema="[dbo]" Table="[Pedidos]" Index="[PK_Pedidos]" />
                          </IndexScan>
                        </RelOp>
                      </NestedLoops>
                    </RelOp>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

    private static string BuildDeepShowplanXml(int depth)
    {
        var sb = new StringBuilder();
        sb.Append($"<ShowPlanXML xmlns=\"{ShowplanNs}\" Version=\"1.564\" Build=\"16.0.1000.6\"><BatchSequence><Batch><Statements>");
        sb.Append("<StmtSimple StatementId=\"1\" StatementType=\"SELECT\" StatementSubTreeCost=\"10\" StatementEstRows=\"1\"><QueryPlan>");

        for (var i = 0; i < depth; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<RelOp NodeId=\"{i}\" PhysicalOp=\"Compute Scalar\" LogicalOp=\"Compute Scalar\" EstimateRows=\"1\" EstimateIO=\"0\" EstimateCPU=\"0.01\" AvgRowSize=\"16\" EstimatedTotalSubtreeCost=\"{depth - i}\" Parallel=\"0\" EstimateRebinds=\"0\" EstimateRewinds=\"0\"><ComputeScalar>");
        }

        sb.Append("<RelOp NodeId=\"").Append(depth).Append("\" PhysicalOp=\"Constant Scan\" LogicalOp=\"Constant Scan\" EstimateRows=\"1\" EstimateIO=\"0\" EstimateCPU=\"0.01\" AvgRowSize=\"16\" EstimatedTotalSubtreeCost=\"0.01\" Parallel=\"0\" EstimateRebinds=\"0\" EstimateRewinds=\"0\"><ConstantScan /></RelOp>");

        for (var i = 0; i < depth; i++)
        {
            sb.Append("</ComputeScalar></RelOp>");
        }

        sb.Append("</QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>");
        return sb.ToString();
    }

    private static QueryFindingSource CreateFinding(
        Guid planId,
        string findingCode,
        string ruleId,
        FindingSeverity severity,
        params int[] nodeIds)
    {
        var nodesJson = JsonSerializer.Serialize(nodeIds.Select(id => new { node_id = id, physical_op = "Op" }));
        return new QueryFindingSource(
            Id: Guid.NewGuid(),
            PlanId: planId,
            QueryStorePlanId: 701,
            FindingCode: findingCode,
            RuleId: ruleId,
            RuleVersion: "1.0.0",
            CatalogVersion: "1.0.0",
            SchemaVersion: "1.0.0",
            Kind: FindingKind.Senal,
            Severity: severity,
            EvidenceClass: EvidenceClass.Estimada,
            NodesJson: nodesJson,
            DecryptedEvidenceJson: "{}",
            ContextJson: "{}",
            ThresholdsUsedJson: "{}",
            EngineVersion: "16.0.1000.6",
            AnalyzedAt: new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task PlanInexistenteDevuelveNotFound()
    {
        var sut = new GetQueryPlanTree(
            new StubPlanSourceReader(null),
            new LiteralMasker(),
            new ShowplanXmlParser(),
            new VersionedRuleCatalog());

        var result = await sut.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<GetQueryPlanTreeResult.NotFound>(result);
    }

    [Fact]
    public async Task PlanSinXmlExpiradoConservaEstadoDeAnalisisYMarcaExpirado()
    {
        var queryId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var source = new QueryPlanTreeSource(
            QueryId: queryId,
            PlanId: planId,
            QueryStorePlanId: 701,
            QueryPlanHash: "0xABC",
            EngineVersion: "16.0.1000.6",
            AnalysisStatus: PlanAnalysisStatus.NotAnalyzable,
            NotAnalyzableCause: "MalformedXml",
            IngestedAt: DateTimeOffset.UtcNow,
            DecryptedPlanXml: null,
            Findings: []);

        var sut = new GetQueryPlanTree(
            new StubPlanSourceReader(source),
            new LiteralMasker(),
            new ShowplanXmlParser(),
            new VersionedRuleCatalog());

        var found = Assert.IsType<GetQueryPlanTreeResult.Found>(await sut.ExecuteAsync(queryId, planId));
        Assert.True(found.View.PlanExpired);
        Assert.Equal(PlanAnalysisStatus.NotAnalyzable, found.View.AnalysisStatus);
        Assert.Equal("MalformedXml", found.View.NotAnalyzableCause);
        Assert.Empty(found.View.Statements);
    }

    [Fact]
    public async Task PlanValidoSeEnmascaraAntesDeParsearResaltaNodosYConservaAdvertencias()
    {
        var queryId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var source = new QueryPlanTreeSource(
            QueryId: queryId,
            PlanId: planId,
            QueryStorePlanId: 701,
            QueryPlanHash: "0x701",
            EngineVersion: "16.0.1000.6",
            AnalysisStatus: PlanAnalysisStatus.Analyzable,
            NotAnalyzableCause: null,
            IngestedAt: DateTimeOffset.UtcNow,
            DecryptedPlanXml: BuildSampleShowplanXml(),
            Findings:
            [
                CreateFinding(planId, "F-REG01", "REG-01", FindingSeverity.Media, 1),
                CreateFinding(planId, "F-REG04", "REG-04", FindingSeverity.Alta, 2),
            ]);

        var sut = new GetQueryPlanTree(
            new StubPlanSourceReader(source),
            new LiteralMasker(),
            new ShowplanXmlParser(),
            new VersionedRuleCatalog());

        var found = Assert.IsType<GetQueryPlanTreeResult.Found>(await sut.ExecuteAsync(queryId, planId));
        var view = found.View;

        Assert.False(view.PlanExpired);
        Assert.Null(view.PlanMaskingFailure);
        Assert.Null(view.ParseFailureReason);
        Assert.Equal(3, view.TotalOperators);
        Assert.Equal(2, view.HighlightedOperatorsCount);

        var stmt = Assert.Single(view.Statements);
        Assert.DoesNotContain("900123456-7", stmt.StatementText ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("85000", stmt.StatementText ?? string.Empty, StringComparison.Ordinal);
        Assert.Single(stmt.Parameters);
        Assert.DoesNotContain("900123456-7", stmt.Parameters[0].CompiledValue ?? string.Empty, StringComparison.Ordinal);

        Assert.Equal([0, 1, 2], stmt.Operators.Select(o => o.NodeId).ToArray());
        Assert.Equal([0, 1, 1], stmt.Operators.Select(o => o.Depth).ToArray());
        Assert.Equal([null, 0, 0], stmt.Operators.Select(o => o.ParentNodeId).ToArray());
        Assert.False(stmt.Operators[1].IsLastSibling);
        Assert.True(stmt.Operators[2].IsLastSibling);

        var node1 = stmt.Operators[1];
        Assert.True(node1.HasFindings);
        Assert.Equal("MEDIA", node1.MaxFindingSeverity);
        Assert.Equal("F-REG01", Assert.Single(node1.Findings).FindingCode);
        Assert.DoesNotContain("900123456-7", node1.PredicateScalarString ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(node1.Warnings, w => w.Kind == "PlanAffectingConvert");

        var node2 = stmt.Operators[2];
        Assert.True(node2.HasFindings);
        Assert.Equal("ALTA", node2.MaxFindingSeverity);
        Assert.Equal("F-REG04", Assert.Single(node2.Findings).FindingCode);
        Assert.Equal(72.0, node2.CostSharePct); // 1.8 / 2.5 * 100 = 72.0%
        Assert.Contains(node2.Warnings, w => w.Kind == "SpillToTempDb" && w.SpillLevel == 1);
    }

    [Fact]
    public async Task PlanConAtributoDesconocidoFallaCerradoSinExponerOperadores()
    {
        var queryId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var suspiciousXml =
            $"<ShowPlanXML xmlns=\"{ShowplanNs}\" Version=\"1.564\" Build=\"16.0\">" +
            "<BatchSequence><Batch><Statements><StmtSimple StatementId=\"1\"><QueryPlan>" +
            "<RelOp NodeId=\"0\" PhysicalOp=\"Table Scan\" LogicalOp=\"Table Scan\" UncatalogedAttr=\"Secreto123\" EstimateRows=\"1\" EstimateIO=\"0\" EstimateCPU=\"0.01\" AvgRowSize=\"8\" EstimatedTotalSubtreeCost=\"0.01\" />" +
            "</QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>";

        var source = new QueryPlanTreeSource(
            queryId, planId, 701, "0x1", "16.0", PlanAnalysisStatus.Analyzable, null, DateTimeOffset.UtcNow, suspiciousXml, []);

        var sut = new GetQueryPlanTree(
            new StubPlanSourceReader(source),
            new LiteralMasker(),
            new ShowplanXmlParser(),
            new VersionedRuleCatalog());

        var found = Assert.IsType<GetQueryPlanTreeResult.Found>(await sut.ExecuteAsync(queryId, planId));
        Assert.False(string.IsNullOrWhiteSpace(found.View.PlanMaskingFailure));
        Assert.Empty(found.View.Statements);
    }

    [Fact]
    public async Task PlanProfundoSeProyectaEnListaPlanaSerializablePorSystemTextJson()
    {
        var queryId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        const int depth = 110; // Supera el MaxDepth = 64 por defecto de System.Text.Json.

        var source = new QueryPlanTreeSource(
            queryId, planId, 799, "0xF8", "16.0.1000.6", PlanAnalysisStatus.Analyzable, null,
            DateTimeOffset.UtcNow, BuildDeepShowplanXml(depth),
            [CreateFinding(planId, "F-DEEP", "REG-01", FindingSeverity.Alta, depth)]);

        var sut = new GetQueryPlanTree(
            new StubPlanSourceReader(source),
            new LiteralMasker(),
            new ShowplanXmlParser(),
            new VersionedRuleCatalog());

        var found = Assert.IsType<GetQueryPlanTreeResult.Found>(await sut.ExecuteAsync(queryId, planId));
        Assert.Equal(depth + 1, found.View.TotalOperators);
        Assert.Equal(depth, found.View.Statements[0].Operators[^1].Depth);
        Assert.True(found.View.Statements[0].Operators[^1].HasFindings);

        // Debe serializar sin superar MaxDepth = 64 de System.Text.Json.
        var json = JsonSerializer.Serialize(found.View, WebJsonOptions);
        Assert.Contains("\"nodeId\":110", json, StringComparison.Ordinal);
    }
}
