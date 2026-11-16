using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Scoring;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.IntegrationTests;

// S1-16 + S2-09: GET /queries y GET /queries/{id} contra PostgreSQL real con SQL/evidencia cifrada,
// enmascarado IMasker, series por plan e intervalo, hallazgos, puntaje relativo e historial.
[Collection(PostgresGroup.Name)]
public sealed class QueryEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    private async Task<(Guid ConnectionId, Guid PrimaryQueryId, Guid SecondaryQueryId)> SeedQueriesAsync()
    {
        var encryptor = _api.Services.GetRequiredService<IFieldEncryptor>();
        var now = DateTimeOffset.UtcNow;
        var intervalStart1 = now.AddHours(-3);
        var intervalEnd1 = now.AddHours(-2);
        var intervalStart2 = now.AddHours(-2);
        var intervalEnd2 = now.AddHours(-1);

        await using var db = postgres.CreateContext();

        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = $"qd-{Guid.NewGuid():N}"[..18],
            DatabaseName = "VentasProd",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = "no-se-usa",
            Status = ConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var sqlText = new QueryText
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryStoreTextId = Random.Shared.NextInt64(1, long.MaxValue),
            EncryptedSqlText = encryptor.Encrypt("SELECT Id, Total FROM dbo.Pedidos WHERE Documento = '10203040' AND Total > 75000;"),
            IngestedAt = now,
        };

        var q1 = new Query
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryStoreQueryId = 5001,
            QueryTextId = sqlText.Id,
            QueryHash = "0xAA11BB22",
            OriginContext = "dbo.sp_BuscarPedidos",
            IngestedAt = now,
        };

        var q2 = new Query
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryStoreQueryId = 5002,
            QueryTextId = sqlText.Id,
            QueryHash = "0xCC33DD44",
            OriginContext = "dbo.sp_Resumen",
            IngestedAt = now,
        };

        var plan1Xml =
            "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\" Version=\"1.564\" Build=\"16.0.1000.6\">" +
            "<BatchSequence><Batch><Statements>" +
            "<StmtSimple StatementId=\"1\" StatementType=\"SELECT\" StatementSubTreeCost=\"3.2\" StatementEstRows=\"100\" StatementText=\"SELECT Id FROM dbo.Pedidos WHERE Documento = '10203040'\">" +
            "<QueryPlan><RelOp NodeId=\"4\" PhysicalOp=\"Clustered Index Scan\" LogicalOp=\"Clustered Index Scan\" EstimateRows=\"5000\" EstimateIO=\"2.8\" EstimateCPU=\"0.4\" AvgRowSize=\"32\" EstimatedTotalSubtreeCost=\"3.2\" Parallel=\"0\" EstimateRebinds=\"0\" EstimateRewinds=\"0\">" +
            "<Warnings><PlanAffectingConvert ConvertIssue=\"Cardinality Estimate\" Expression=\"CONVERT(varchar(20), Documento) = '10203040'\" /></Warnings>" +
            "<IndexScan><Object Database=\"[VentasProd]\" Schema=\"[dbo]\" Table=\"[Pedidos]\" Index=\"[PK_Pedidos]\" />" +
            "<Predicate><ScalarOperator ScalarString=\"Documento = '10203040'\" /></Predicate></IndexScan>" +
            "</RelOp></QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>";

        var plan1 = new Plan
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = q1.Id,
            QueryStorePlanId = 701,
            QueryPlanHash = "0xP701",
            EncryptedPlanXml = encryptor.Encrypt(plan1Xml),
            EngineVersion = "16.0.1000.6",
            AnalysisStatus = PlanAnalysisStatus.Analyzable,
            IngestedAt = now.AddMinutes(-10),
        };

        var plan2 = new Plan
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = q1.Id,
            QueryStorePlanId = 702,
            QueryPlanHash = "0xP702",
            EngineVersion = "16.0.1000.6",
            AnalysisStatus = PlanAnalysisStatus.Analyzable,
            IngestedAt = now,
        };

        var stats = new[]
        {
            new RuntimeStat
            {
                ConnectionId = connection.Id,
                PlanId = plan1.Id,
                ExecutionType = ExecutionType.Regular,
                IntervalId = 10,
                IntervalStart = intervalStart1,
                IntervalEnd = intervalEnd1,
                ExecutionCount = 120,
                AvgDurationMicroseconds = 45_000,
                AvgCpuTimeMicroseconds = 18_000,
                AvgLogicalIoReads = 1200,
                AvgPhysicalIoReads = 15,
                AvgRowCount = 25,
                IngestedAt = now,
            },
            new RuntimeStat
            {
                ConnectionId = connection.Id,
                PlanId = plan1.Id,
                ExecutionType = ExecutionType.Regular,
                IntervalId = 11,
                IntervalStart = intervalStart2,
                IntervalEnd = intervalEnd2,
                ExecutionCount = 80,
                AvgDurationMicroseconds = 30_000,
                AvgCpuTimeMicroseconds = 12_000,
                AvgLogicalIoReads = 900,
                AvgPhysicalIoReads = 8,
                AvgRowCount = 20,
                IngestedAt = now,
            },
            new RuntimeStat
            {
                ConnectionId = connection.Id,
                PlanId = plan2.Id,
                ExecutionType = ExecutionType.Regular,
                IntervalId = 11,
                IntervalStart = intervalStart2,
                IntervalEnd = intervalEnd2,
                ExecutionCount = 50,
                AvgDurationMicroseconds = 12_000,
                AvgCpuTimeMicroseconds = 5_000,
                AvgLogicalIoReads = 320,
                AvgPhysicalIoReads = 2,
                AvgRowCount = 20,
                IngestedAt = now,
            },
        };

        var thresholdSet = new ThresholdSet
        {
            Id = Guid.NewGuid(),
            CatalogVersion = "1.0.0",
            ConfigurationHash = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
        };

        var run = new AnalysisRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = q1.Id,
            PlanId = plan1.Id,
            ThresholdSetId = thresholdSet.Id,
            CatalogVersion = "1.0.0",
            EngineVersion = "16.0.1000.6",
            ThresholdsUsedJson = "{\"min_query_consumption_pct\":5}",
            Status = AnalysisRunStatus.Succeeded,
            FindingsCount = 1,
            StartedAt = now.AddMinutes(-2),
            FinishedAt = now,
        };

        var finding = new Finding
        {
            Id = Guid.NewGuid(),
            AnalysisRunId = run.Id,
            ConnectionId = connection.Id,
            QueryId = q1.Id,
            PlanId = plan1.Id,
            FindingCode = $"F-{Guid.NewGuid():N}"[..14],
            RuleId = "REG-01",
            RuleVersion = "1.0.0",
            CatalogVersion = "1.0.0",
            SchemaVersion = "1.0.0",
            Kind = FindingKind.Senal,
            Severity = FindingSeverity.Alta,
            EvidenceClass = EvidenceClass.Estimada,
            NodesJson = "[{\"node_id\":4,\"physical_op\":\"Clustered Index Scan\"}]",
            EncryptedEvidenceJson = encryptor.Encrypt("{\"expression\":\"CONVERT(varchar(20), Documento) = '10203040'\",\"convert_issue\":\"ImplicitConvert\"}"),
            ContextJson = "{\"executions\":250,\"query_consumption_pct\":62.5,\"estimated_rows\":5000,\"estimated_rows_read\":250000,\"estimated_subtree_cost\":14.2}",
            ThresholdsUsedJson = "{\"min_query_consumption_pct\":5}",
            EngineVersion = "16.0.1000.6",
            AnalyzedAt = now,
        };

        var contributionsJson = JsonSerializer.Serialize(new[]
        {
            new FindingSeverityContribution(finding.FindingCode, "REG-01", "Alta", "Structural", 1.0, 1.0, 1.0),
        });
        var paramsJson = JsonSerializer.Serialize(HealthScoreParameters.Default);
        var paramsHash = new string('c', 64);

        var scoreQ1Prev = new HealthScore
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = q1.Id,
            WindowStart = intervalStart1,
            WindowEnd = intervalEnd1,
            Score = 28.0,
            Priority = 72.0,
            Band = HealthScoreBand.Critica,
            ImpactComponent = 0.70,
            SeverityComponent = 0.75,
            AlphaUsed = 0.65,
            CpuShare = 0.60,
            DurationShare = 0.65,
            LogicalReadsShare = 0.55,
            WeightedConsumptionPct = 60.5,
            TotalExecutions = 120,
            TotalCpuMicroseconds = 2_160_000,
            TotalDurationMicroseconds = 5_400_000,
            TotalLogicalReads = 144_000,
            FindingsCount = 1,
            ContributingFindingsJson = contributionsJson,
            ParametersJson = paramsJson,
            ParametersHash = paramsHash,
            CalculatedAt = intervalEnd1,
        };

        var scoreQ1Latest = new HealthScore
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = q1.Id,
            WindowStart = intervalStart1,
            WindowEnd = intervalEnd2,
            Score = 22.5,
            Priority = 77.5,
            Band = HealthScoreBand.Critica,
            ImpactComponent = 0.76,
            SeverityComponent = 0.80,
            AlphaUsed = 0.65,
            CpuShare = 0.64,
            DurationShare = 0.68,
            LogicalReadsShare = 0.59,
            WeightedConsumptionPct = 64.2,
            TotalExecutions = 250,
            TotalCpuMicroseconds = 3_370_000,
            TotalDurationMicroseconds = 8_400_000,
            TotalLogicalReads = 232_000,
            FindingsCount = 1,
            ContributingFindingsJson = contributionsJson,
            ParametersJson = paramsJson,
            ParametersHash = paramsHash,
            CalculatedAt = intervalEnd2,
        };

        var scoreQ2Latest = new HealthScore
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = q2.Id,
            WindowStart = intervalStart1,
            WindowEnd = intervalEnd2,
            Score = 84.0,
            Priority = 16.0,
            Band = HealthScoreBand.Saludable,
            ImpactComponent = 0.20,
            SeverityComponent = 0.0,
            AlphaUsed = 0.65,
            CpuShare = 0.15,
            DurationShare = 0.18,
            LogicalReadsShare = 0.12,
            WeightedConsumptionPct = 15.3,
            TotalExecutions = 40,
            TotalCpuMicroseconds = 400_000,
            TotalDurationMicroseconds = 900_000,
            TotalLogicalReads = 12_000,
            FindingsCount = 0,
            ContributingFindingsJson = "[]",
            ParametersJson = paramsJson,
            ParametersHash = paramsHash,
            CalculatedAt = intervalEnd2,
        };

        db.Connections.Add(connection);
        db.QueryTexts.Add(sqlText);
        db.Queries.AddRange(q1, q2);
        db.Plans.AddRange(plan1, plan2);
        db.RuntimeStats.AddRange(stats);
        db.ThresholdSets.Add(thresholdSet);
        db.AnalysisRuns.Add(run);
        db.Findings.Add(finding);
        db.HealthScores.AddRange(scoreQ1Prev, scoreQ1Latest, scoreQ2Latest);
        await db.SaveChangesAsync();

        return (connection.Id, q1.Id, q2.Id);
    }

    [Fact]
    public async Task EndpointsConsultasExigenAutenticacionYFiltranPorAcceso()
    {
        using var anon = _api.CreateClient();
        using var devSinAsignaciones = await _api.ClientAsync(RoleNames.Developer);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/queries")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/queries/{Guid.NewGuid()}")).StatusCode);

        // El Desarrollador accede a /queries vía ConnectionReaderPolicy, pero sin asignaciones solo ve [] y 404.
        using var devList = await devSinAsignaciones.GetAsync("/queries");
        Assert.Equal(HttpStatusCode.OK, devList.StatusCode);
        var items = await devList.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, items.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await devSinAsignaciones.GetAsync($"/queries/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task FiltrosInvalidosYConsultaInexistenteDevuelven400Y404()
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/queries?window=90d&sort=bad")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/queries/{Guid.NewGuid()}?window=1h")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/queries/{Guid.NewGuid()}?window=24h")).StatusCode);
    }

    [Fact]
    public async Task ConDatosSembradosDevuelveListadoOrdenadoYDetalleConSeriesHallazgosYScore()
    {
        var (connectionId, q1Id, q2Id) = await SeedQueriesAsync();
        using var client = await _api.ClientAsync(RoleNames.Dba);

        var list = await client.GetFromJsonAsync<JsonElement>($"/queries?connectionId={connectionId}&window=24h&sort=score");
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal(q1Id, list[0].GetProperty("id").GetGuid());
        Assert.Equal(q2Id, list[1].GetProperty("id").GetGuid());
        Assert.Equal(22.5, list[0].GetProperty("score").GetDouble());
        Assert.Equal("Critica", list[0].GetProperty("band").GetString());
        Assert.DoesNotContain("10203040", list[0].GetProperty("sqlPreview").GetString()!, StringComparison.Ordinal);
        Assert.DoesNotContain("75000", list[0].GetProperty("sqlPreview").GetString()!, StringComparison.Ordinal);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/queries/{q1Id}?window=24h");
        var sqlText = detail.GetProperty("sqlText").GetString()!;
        Assert.DoesNotContain("10203040", sqlText, StringComparison.Ordinal);
        Assert.DoesNotContain("75000", sqlText, StringComparison.Ordinal);
        Assert.False(detail.GetProperty("sqlExpired").GetBoolean());

        Assert.Equal(2, detail.GetProperty("plans").GetArrayLength());
        Assert.Equal(3, detail.GetProperty("series").GetArrayLength());

        var findings = detail.GetProperty("findings");
        Assert.Equal(1, findings.GetArrayLength());
        Assert.True(findings[0].GetProperty("evidenceMasked").GetBoolean());
        Assert.DoesNotContain("10203040", findings[0].GetProperty("evidence").GetProperty("expression").GetString()!, StringComparison.Ordinal);

        var score = detail.GetProperty("score");
        Assert.Equal(22.5, score.GetProperty("score").GetDouble());
        Assert.Equal(1, score.GetProperty("rankInWindow").GetInt32());
        Assert.Equal(2, score.GetProperty("totalQueriesInWindow").GetInt32());
        Assert.Equal("Critica", score.GetProperty("band").GetString());
        Assert.Equal("Crítica", score.GetProperty("bandLabel").GetString());
        Assert.False(string.IsNullOrWhiteSpace(score.GetProperty("scaleDirection").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(score.GetProperty("usageDisclaimer").GetString()));
        Assert.Equal(1, score.GetProperty("contributingFindings").GetArrayLength());
        Assert.Equal(2, score.GetProperty("history").GetArrayLength());

        // S2-10: árbol jerárquico del plan con enmascarado de predicados y nodo asociado a hallazgo resaltado.
        var planId1 = detail.GetProperty("plans")[0].GetProperty("planId").GetGuid();
        var planTree = await client.GetFromJsonAsync<JsonElement>($"/queries/{q1Id}/plans/{planId1}");
        Assert.False(planTree.GetProperty("planExpired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, planTree.GetProperty("planMaskingFailure").ValueKind);
        Assert.Equal(1, planTree.GetProperty("totalOperators").GetInt32());
        Assert.Equal(1, planTree.GetProperty("highlightedOperatorsCount").GetInt32());

        var op = planTree.GetProperty("statements")[0].GetProperty("operators")[0];
        Assert.Equal(4, op.GetProperty("nodeId").GetInt32());
        Assert.Equal("Clustered Index Scan", op.GetProperty("physicalOp").GetString());
        Assert.True(op.GetProperty("hasFindings").GetBoolean());
        Assert.Equal("ALTA", op.GetProperty("maxFindingSeverity").GetString());
        Assert.DoesNotContain("10203040", op.GetProperty("predicateScalarString").GetString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(1, op.GetProperty("warnings").GetArrayLength());

        // Plan inexistente o de otra consulta -> 404.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/queries/{q2Id}/plans/{planId1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/queries/{q1Id}/plans/{Guid.NewGuid()}")).StatusCode);

        // Permisos en endpoint de plan: anónimo -> 401; Desarrollador sin asignación -> 404 (filtro ConnectionAccess).
        using var anon = _api.CreateClient();
        using var dev = await _api.ClientAsync(RoleNames.Developer);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/queries/{q1Id}/plans/{planId1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dev.GetAsync($"/queries/{q1Id}/plans/{planId1}")).StatusCode);
    }
}
