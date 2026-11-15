using System.Text.Json;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Masking;
using QueryDoctor.Application.Queries;
using QueryDoctor.Application.Scoring;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Queries;

public sealed class QueryUseCasesTests
{
    private sealed class StubQuerySourceReader : IQuerySourceReader
    {
        public IReadOnlyList<QueryListSourceRow> Rows { get; init; } = [];
        public QueryDetailSource? Detail { get; init; }
        public TimeSpan? LastWindowRequested { get; private set; }

        public Task<IReadOnlyList<QueryListSourceRow>> ListAsync(
            Guid? connectionId,
            TimeSpan windowDuration,
            CancellationToken cancellationToken = default)
        {
            LastWindowRequested = windowDuration;
            var filtered = connectionId.HasValue
                ? Rows.Where(r => r.ConnectionId == connectionId.Value).ToList()
                : Rows;
            return Task.FromResult<IReadOnlyList<QueryListSourceRow>>(filtered);
        }

        public Task<QueryDetailSource?> GetDetailAsync(
            Guid queryId,
            TimeSpan windowDuration,
            CancellationToken cancellationToken = default)
        {
            LastWindowRequested = windowDuration;
            if (Detail is not null && Detail.QueryId == queryId)
            {
                return Task.FromResult<QueryDetailSource?>(Detail);
            }

            return Task.FromResult<QueryDetailSource?>(null);
        }

        public Task<QueryPlanTreeSource?> GetPlanTreeAsync(
            Guid queryId,
            Guid planId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<QueryPlanTreeSource?>(null);
    }

    private static QueryFindingSource CreateFinding(
        Guid planId,
        long queryStorePlanId,
        string code,
        string ruleId,
        FindingSeverity severity,
        string evidenceJson,
        string expressionOp = "Clustered Index Scan")
    {
        return new QueryFindingSource(
            Id: Guid.NewGuid(),
            PlanId: planId,
            QueryStorePlanId: queryStorePlanId,
            FindingCode: code,
            RuleId: ruleId,
            RuleVersion: "1.0.0",
            CatalogVersion: "1.0.0",
            SchemaVersion: "1.0.0",
            Kind: FindingKind.Senal,
            Severity: severity,
            EvidenceClass: EvidenceClass.Estimada,
            NodesJson: $"[{{\"node_id\":3,\"physical_op\":\"{expressionOp}\"}}]",
            DecryptedEvidenceJson: evidenceJson,
            ContextJson: "{\"executions\":1500,\"query_consumption_pct\":42.5,\"estimated_rows\":12000,\"estimated_rows_read\":250000,\"estimated_subtree_cost\":8.4}",
            ThresholdsUsedJson: "{\"min_query_consumption_pct\":5}",
            EngineVersion: "16.0.1000.6",
            AnalyzedAt: new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetQueriesRechazaFiltrosInvalidos()
    {
        var sut = new GetQueries(new StubQuerySourceReader(), new LiteralMasker(), new VersionedRuleCatalog());

        var result = await sut.ExecuteAsync(new QueryListFilter(null, "90d", "Fatal", "priority"));

        var invalid = Assert.IsType<GetQueriesResult.Invalid>(result);
        Assert.True(invalid.Errors.ContainsKey("window"));
        Assert.True(invalid.Errors.ContainsKey("sort"));
        Assert.True(invalid.Errors.ContainsKey("severity"));
    }

    [Fact]
    public async Task GetQueriesEnmascaraLiteralesYOrdenaPorConsumoOScore()
    {
        var connId = Guid.NewGuid();
        var planIdA = Guid.NewGuid();
        var planIdB = Guid.NewGuid();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        var reader = new StubQuerySourceReader
        {
            Rows =
            [
                new QueryListSourceRow(
                    QueryId: idA,
                    ConnectionId: connId,
                    QueryStoreQueryId: 101,
                    Server: "sql-prod-01",
                    DatabaseName: "Ventas",
                    DecryptedSqlText: "SELECT c.Id FROM dbo.Clientes c WHERE c.Nombre = 'Juan Perez' AND c.Saldo > 9500;",
                    ConsumptionPct: 68.4,
                    Executions: 4000,
                    Score: 55.25,
                    Band: HealthScoreBand.Atencion,
                    Findings:
                    [
                        CreateFinding(planIdA, 11, "F-AAA111", "REG-01", FindingSeverity.Media,
                            "{\"expression\":\"CONVERT(varchar(50), c.Nombre) = 'Juan Perez'\",\"convert_issue\":\"ImplicitConvert\"}"),
                    ]),
                new QueryListSourceRow(
                    QueryId: idB,
                    ConnectionId: connId,
                    QueryStoreQueryId: 102,
                    Server: "sql-prod-01",
                    DatabaseName: "Ventas",
                    DecryptedSqlText: null,
                    ConsumptionPct: 21.1,
                    Executions: 900,
                    Score: 18.4,
                    Band: HealthScoreBand.Critica,
                    Findings:
                    [
                        CreateFinding(planIdB, 22, "F-BBB222", "REG-02", FindingSeverity.Alta,
                            "{\"table\":\"dbo.Pedidos\",\"estimated_rows\":\"9000\"}"),
                    ]),
            ],
        };

        var sut = new GetQueries(reader, new LiteralMasker(), new VersionedRuleCatalog());

        var byConsumption = Assert.IsType<GetQueriesResult.Ok>(
            await sut.ExecuteAsync(new QueryListFilter(connId, "7d", null, "consumption")));
        Assert.Equal(TimeSpan.FromDays(7), reader.LastWindowRequested);
        Assert.Equal([idA, idB], byConsumption.Items.Select(x => x.Id).ToArray());

        var firstItem = byConsumption.Items[0];
        Assert.DoesNotContain("Juan Perez", firstItem.SqlPreview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9500", firstItem.SqlPreview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Juan Perez", firstItem.Findings[0].Summary, StringComparison.OrdinalIgnoreCase);
        Assert.False(firstItem.SqlExpired);
        Assert.Equal("Atencion", firstItem.Band);

        var byScore = Assert.IsType<GetQueriesResult.Ok>(
            await sut.ExecuteAsync(new QueryListFilter(connId, "24h", "High", "score")));
        var onlyHigh = Assert.Single(byScore.Items);
        Assert.Equal(idB, onlyHigh.Id);
        Assert.True(onlyHigh.SqlExpired);
        Assert.Equal("Critica", onlyHigh.Band);
    }

    [Fact]
    public async Task GetQueryDetailDevuelveNotFoundEInvalidSegunEntrada()
    {
        var reader = new StubQuerySourceReader();
        var sut = new GetQueryDetail(reader, new LiteralMasker(), new VersionedRuleCatalog());

        var invalid = await sut.ExecuteAsync(Guid.NewGuid(), "1h");
        var invalidRes = Assert.IsType<GetQueryDetailResult.Invalid>(invalid);
        Assert.True(invalidRes.Errors.ContainsKey("window"));

        var missing = await sut.ExecuteAsync(Guid.NewGuid(), "24h");
        Assert.IsType<GetQueryDetailResult.NotFound>(missing);
    }

    [Fact]
    public async Task GetQueryDetailAgregaSeriesPorPlanYIntervaloEnMilisegundosYEnmascaraEvidencia()
    {
        var queryId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        var plan1 = new Plan
        {
            Id = Guid.NewGuid(),
            ConnectionId = connId,
            QueryId = queryId,
            QueryStorePlanId = 501,
            QueryPlanHash = "0xABCD01",
            EngineVersion = "16.0.1000.6",
            AnalysisStatus = PlanAnalysisStatus.Analyzable,
            IngestedAt = new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.Zero),
        };
        var plan2 = new Plan
        {
            Id = Guid.NewGuid(),
            ConnectionId = connId,
            QueryId = queryId,
            QueryStorePlanId = 502,
            QueryPlanHash = "0xABCD02",
            EngineVersion = "16.0.1000.6",
            AnalysisStatus = PlanAnalysisStatus.NotAnalyzable,
            NotAnalyzableCause = "XmlCorrupted",
            IngestedAt = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero),
        };

        var intervalStart = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        var intervalEnd = intervalStart.AddHours(1);

        var contributions = new[]
        {
            new FindingSeverityContribution("F-VALID", "REG-01", "Media", "Structural", 0.5, 1.0, 0.5),
        };

        var latestScore = new HealthScore
        {
            Id = Guid.NewGuid(),
            ConnectionId = connId,
            QueryId = queryId,
            WindowStart = intervalStart.AddHours(-24),
            WindowEnd = intervalEnd,
            Score = 32.5,
            Priority = 67.5,
            Band = HealthScoreBand.Critica,
            ImpactComponent = 0.72,
            SeverityComponent = 0.55,
            AlphaUsed = 0.65,
            CpuShare = 0.45,
            DurationShare = 0.60,
            LogicalReadsShare = 0.30,
            WeightedConsumptionPct = 46.5,
            TotalExecutions = 100,
            TotalCpuMicroseconds = 1_200_000,
            TotalDurationMicroseconds = 2_400_000,
            TotalLogicalReads = 95_000,
            FindingsCount = 1,
            ContributingFindingsJson = JsonSerializer.Serialize(contributions),
            ParametersJson = JsonSerializer.Serialize(HealthScoreParameters.Default),
            ParametersHash = new string('b', 64),
            CalculatedAt = intervalEnd,
        };

        var reader = new StubQuerySourceReader
        {
            Detail = new QueryDetailSource(
                QueryId: queryId,
                ConnectionId: connId,
                QueryStoreQueryId: 88,
                Server: "srv-sql",
                DatabaseName: "Facturacion",
                Platform: ConnectionPlatform.SqlServer,
                QueryHash: "0x112233",
                OriginContext: "sp_Facturar",
                WindowStart: intervalStart.AddHours(-24),
                WindowEnd: intervalEnd,
                DecryptedSqlText: "SELECT * FROM dbo.Facturas WHERE Nit = '900123456-7' AND Monto > 50000;",
                Plans: [plan1, plan2],
                RuntimeStats:
                [
                    // Dos ExecutionType para el mismo (plan1, intervalId=1): deben promediarse ponderando por ExecutionCount.
                    new RuntimeStat
                    {
                        Id = 1,
                        ConnectionId = connId,
                        PlanId = plan1.Id,
                        ExecutionType = ExecutionType.Regular,
                        IntervalId = 1,
                        IntervalStart = intervalStart,
                        IntervalEnd = intervalEnd,
                        ExecutionCount = 30,
                        AvgDurationMicroseconds = 10_000,
                        AvgCpuTimeMicroseconds = 4_000,
                        AvgLogicalIoReads = 100,
                        AvgPhysicalIoReads = 10,
                        AvgRowCount = 50,
                    },
                    new RuntimeStat
                    {
                        Id = 2,
                        ConnectionId = connId,
                        PlanId = plan1.Id,
                        ExecutionType = ExecutionType.Aborted,
                        IntervalId = 1,
                        IntervalStart = intervalStart,
                        IntervalEnd = intervalEnd,
                        ExecutionCount = 10,
                        AvgDurationMicroseconds = 50_000,
                        AvgCpuTimeMicroseconds = 20_000,
                        AvgLogicalIoReads = 300,
                        AvgPhysicalIoReads = 50,
                        AvgRowCount = 10,
                    },
                    new RuntimeStat
                    {
                        Id = 3,
                        ConnectionId = connId,
                        PlanId = plan2.Id,
                        ExecutionType = ExecutionType.Regular,
                        IntervalId = 1,
                        IntervalStart = intervalStart,
                        IntervalEnd = intervalEnd,
                        ExecutionCount = 20,
                        AvgDurationMicroseconds = 8_000,
                        AvgCpuTimeMicroseconds = 3_000,
                        AvgLogicalIoReads = 80,
                        AvgPhysicalIoReads = 5,
                        AvgRowCount = 40,
                    },
                ],
                Findings:
                [
                    CreateFinding(plan1.Id, plan1.QueryStorePlanId, "F-VALID", "REG-01", FindingSeverity.Media,
                        "{\"expression\":\"CONVERT(varchar(20), Nit) = '900123456-7'\",\"convert_issue\":\"ImplicitConvert\"}"),
                    // Clave no clasificada -> IMasker la excluye (fallo cerrado).
                    CreateFinding(plan1.Id, plan1.QueryStorePlanId, "F-FAILCLOSED", "REG-02", FindingSeverity.Alta,
                        "{\"unclassified_secret_field\":\"Secreto123\"}"),
                ],
                LatestScore: latestScore,
                RankInWindow: 1,
                TotalQueriesInWindow: 12,
                ScoreHistory: [latestScore]),
        };

        var sut = new GetQueryDetail(reader, new LiteralMasker(), new VersionedRuleCatalog());
        var result = await sut.ExecuteAsync(queryId, "24h");

        var found = Assert.IsType<GetQueryDetailResult.Found>(result);
        var view = found.View;

        Assert.NotNull(view.SqlText);
        Assert.DoesNotContain("900123456-7", view.SqlText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("50000", view.SqlText, StringComparison.OrdinalIgnoreCase);
        Assert.False(view.SqlExpired);
        Assert.Null(view.SqlMaskingFailure);

        // Series: 2 puntos (plan1 agregado + plan2) con tiempos en ms.
        Assert.Equal(2, view.Series.Count);
        var p1Point = view.Series.Single(s => s.PlanId == plan1.Id);
        Assert.Equal(40, p1Point.ExecutionCount);
        Assert.Equal(20.0, p1Point.AvgDurationMs); // (30*10000 + 10*50000) / 40 / 1000 = 20.0 ms
        Assert.Equal(8.0, p1Point.AvgCpuTimeMs);   // (30*4000 + 10*20000) / 40 / 1000 = 8.0 ms
        Assert.Equal(150.0, p1Point.AvgLogicalIoReads);

        // Hallazgos: F-VALID enmascarado; F-FAILCLOSED falla cerrado sin filtrar el valor crudo.
        var validFinding = view.Findings.Single(f => f.FindingCode == "F-VALID");
        Assert.True(validFinding.EvidenceMasked);
        Assert.DoesNotContain("900123456-7", validFinding.Evidence["expression"], StringComparison.OrdinalIgnoreCase);

        var failedFinding = view.Findings.Single(f => f.FindingCode == "F-FAILCLOSED");
        Assert.False(failedFinding.EvidenceMasked);
        Assert.Empty(failedFinding.Evidence);
        Assert.False(string.IsNullOrWhiteSpace(failedFinding.EvidenceMaskingFailure));

        // Score y posición relativa (CA-HU08.1, RF-SCO-06).
        Assert.NotNull(view.Score);
        Assert.Equal(32.5, view.Score!.Score);
        Assert.Equal("Critica", view.Score.Band);
        Assert.Equal("Crítica", view.Score.BandLabel);
        Assert.Equal(1, view.Score.RankInWindow);
        Assert.Equal(12, view.Score.TotalQueriesInWindow);
        Assert.Equal(HealthScoreParameters.ScaleDirection, view.Score.ScaleDirection);
        Assert.Equal(HealthScoreParameters.UsageDisclaimer, view.Score.UsageDisclaimer);
        Assert.Single(view.Score.ContributingFindings);
        Assert.Single(view.Score.History);
    }
}
