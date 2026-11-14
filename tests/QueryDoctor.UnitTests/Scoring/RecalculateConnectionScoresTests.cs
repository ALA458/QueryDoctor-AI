using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Scoring;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure;
using QueryDoctor.UnitTests.Connections;
using QueryDoctor.UnitTests.Ingestion;
using static QueryDoctor.UnitTests.Ingestion.IngestionSamples;

namespace QueryDoctor.UnitTests.Scoring;

/// <summary>
/// Pruebas unitarias de <see cref="RecalculateConnectionScores"/>, recálculo tras cada ingesta,
/// preservación del historial en <c>analysis.health_score</c> y registro en DI (S2-02, RF-SCO-02, RF-SCO-05).
/// </summary>
public sealed class RecalculateConnectionScoresTests
{
    private const string ProblematicShowplanXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple StatementText="SELECT OrderId FROM dbo.Orders WHERE CustomerCode = @code" StatementId="1" StatementType="SELECT" StatementSubTreeCost="1.5" StatementEstRows="100">
                  <QueryPlan>
                    <Warnings>
                      <PlanAffectingConvert ConvertIssue="Seek Plan" Expression="CONVERT_IMPLICIT(nvarchar(20),[QD_Lab].[dbo].[Orders].[CustomerCode],0)=[@code]" />
                    </Warnings>
                    <RelOp NodeId="1" PhysicalOp="Index Scan" LogicalOp="Index Scan" EstimateRows="100" EstimatedRowsRead="50000" EstimateIO="1.0" EstimateCPU="0.5" AvgRowSize="16" EstimatedTotalSubtreeCost="1.5" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
                      <OutputList />
                      <IndexScan Ordered="0">
                        <Object Database="[QD_Lab]" Schema="[dbo]" Table="[Orders]" Index="[IX_Orders_CustomerCode]" IndexKind="NonClustered" Storage="RowStore" />
                      </IndexScan>
                    </RelOp>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

    private const string HealthyShowplanXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4315.2">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple StatementText="SELECT OrderId FROM dbo.Orders WHERE CustomerId = @id" StatementId="1" StatementType="SELECT" StatementSubTreeCost="0.01" StatementEstRows="1">
                  <QueryPlan>
                    <RelOp NodeId="1" PhysicalOp="Index Seek" LogicalOp="Index Seek" EstimateRows="1" EstimatedRowsRead="1" EstimateIO="0.003" EstimateCPU="0.001" AvgRowSize="16" EstimatedTotalSubtreeCost="0.01" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
                      <OutputList />
                      <IndexScan Ordered="1">
                        <Object Database="[QD_Lab]" Schema="[dbo]" Table="[Orders]" Index="[IX_Orders_CustomerId]" IndexKind="NonClustered" Storage="RowStore" />
                      </IndexScan>
                    </RelOp>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

    [Fact]
    public async Task RecalculosSucesivosAnalizanPlanesYConservanHistorialCompletoDePuntajes()
    {
        var encryptor = new FakeEncryptor();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        var store = new InMemoryScoreRecalculationStore();
        var ruleCatalog = new VersionedRuleCatalog();
        var ruleEngine = new DeterministicRuleEngine(new ShowplanXmlParser(), ruleCatalog, RuleThresholds.Default);
        var calculator = new HealthScoreCalculator();

        var useCase = new RecalculateConnectionScores(
            store,
            calculator,
            ruleEngine,
            ruleCatalog,
            encryptor,
            clock);

        var connectionId = Guid.NewGuid();
        var targetQueryId = Guid.NewGuid();
        var baselineQueryId = Guid.NewGuid();
        var run1Id = Guid.NewGuid();
        var run2Id = Guid.NewGuid();

        // Ingesta 1: consulta objetivo con plan problemático (REG-01 + REG-03) y consumo > tau (I = 1.0)
        store.Snapshot = new ConnectionScoringWindowSnapshot(
            ConnectionId: connectionId,
            IngestionRunId: run1Id,
            WindowStart: clock.Now.AddHours(-24),
            WindowEnd: clock.Now,
            EngineVersion: "16.0.4315.2",
            ActualPlanAvailable: false,
            Queries:
            [
                new WindowQuerySnapshot(
                    QueryId: targetQueryId,
                    QueryStoreQueryId: 10,
                    ResourceSamples: [new QueryResourceSample(100, 600.0, 600.0, 50.0)],
                    LatestPlan: new WindowPlanSnapshot(
                        PlanId: Guid.NewGuid(),
                        QueryStorePlanId: 100,
                        EncryptedPlanXml: encryptor.Encrypt(ProblematicShowplanXml),
                        EngineVersion: "16.0.4315.2",
                        AnalysisStatus: PlanAnalysisStatus.Pending,
                        NotAnalyzableCause: null,
                        PlanExecutionCount: 100,
                        AvgRowsReturned: 10.0,
                        HasCurrentAnalysisRun: false,
                        ExistingFindings: [])),
                new WindowQuerySnapshot(
                    QueryId: baselineQueryId,
                    QueryStoreQueryId: 20,
                    ResourceSamples: [new QueryResourceSample(100, 400.0, 400.0, 50.0)],
                    LatestPlan: null),
            ]);

        var firstOutcome = await useCase.ExecuteAsync(connectionId, run1Id);

        Assert.Equal(2, firstOutcome.QueriesScored);
        Assert.Equal(1, firstOutcome.PlansAnalyzed);
        var firstTargetScore = firstOutcome.Scores.Single(s => s.QueryId == targetQueryId);
        Assert.Equal(HealthScoreBand.Critica, firstTargetScore.Band);
        Assert.True(firstTargetScore.FindingsCount >= 1);
        Assert.NotEmpty(firstTargetScore.ParametersJson);
        Assert.Equal(64, firstTargetScore.ParametersHash.Length);

        // Avanzar reloj 1 hora -> Ingesta 2: el índice optimizado resuelve REG-01 y REG-03, y baja el consumo a 1%.
        clock.Now = clock.Now.AddHours(1);
        store.Snapshot = new ConnectionScoringWindowSnapshot(
            ConnectionId: connectionId,
            IngestionRunId: run2Id,
            WindowStart: clock.Now.AddHours(-24),
            WindowEnd: clock.Now,
            EngineVersion: "16.0.4315.2",
            ActualPlanAvailable: false,
            Queries:
            [
                new WindowQuerySnapshot(
                    QueryId: targetQueryId,
                    QueryStoreQueryId: 10,
                    ResourceSamples: [new QueryResourceSample(10, 10.0, 10.0, 1.0)],
                    LatestPlan: new WindowPlanSnapshot(
                        PlanId: Guid.NewGuid(),
                        QueryStorePlanId: 101,
                        EncryptedPlanXml: encryptor.Encrypt(HealthyShowplanXml),
                        EngineVersion: "16.0.4315.2",
                        AnalysisStatus: PlanAnalysisStatus.Pending,
                        NotAnalyzableCause: null,
                        PlanExecutionCount: 10,
                        AvgRowsReturned: 1.0,
                        HasCurrentAnalysisRun: false,
                        ExistingFindings: [])),
                new WindowQuerySnapshot(
                    QueryId: baselineQueryId,
                    QueryStoreQueryId: 20,
                    ResourceSamples: [new QueryResourceSample(100, 99.0, 99.0, 9.9)],
                    LatestPlan: null),
            ]);

        var secondOutcome = await useCase.ExecuteAsync(connectionId, run2Id);

        var secondTargetScore = secondOutcome.Scores.Single(s => s.QueryId == targetQueryId);
        Assert.Equal(HealthScoreBand.Saludable, secondTargetScore.Band);
        Assert.Equal(0, secondTargetScore.FindingsCount);
        Assert.True(secondTargetScore.Score > firstTargetScore.Score);

        // El historial en analysis.health_score conserva ambas mediciones cronológicas (RF-SCO-05).
        var history = await store.GetQueryScoreHistoryAsync(connectionId, targetQueryId);
        Assert.Equal(2, history.Count);
        Assert.Equal(run1Id, history[0].IngestionRunId);
        Assert.Equal(HealthScoreBand.Critica, history[0].Band);
        Assert.Equal(run2Id, history[1].IngestionRunId);
        Assert.Equal(HealthScoreBand.Saludable, history[1].Band);

        var latest = await store.GetLatestConnectionScoresAsync(connectionId, HealthScoreOrderBy.ScoreAscending);
        Assert.Equal(2, latest.Count);
        Assert.Equal(run2Id, latest.Single(h => h.QueryId == targetQueryId).IngestionRunId);
    }

    [Fact]
    public async Task IngestConnectionEjecutaRecalculoAlFinalizarConExito()
    {
        var encryptor = new FakeEncryptor();
        var reader = new FakeQueryStoreReader();
        var ingestionLock = new FakeIngestionLock();
        var ingestionStore = new InMemoryIngestionStore();
        var scoreStore = new InMemoryScoreRecalculationStore();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));

        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Server = "lab",
            DatabaseName = "QD_Lab",
            Status = ConnectionStatus.Active,
            EncryptedCredentials = ConnectionCredentialsCodec.Encrypt(new ConnectionCredentials("qd_reader", "S3creta!"), encryptor),
        };
        ingestionStore.Connection = connection;
        ingestionStore.Profile = new CapabilityProfile { ConnectionId = connection.Id, OmittedFeaturesJson = "[]" };

        var queryId = Guid.NewGuid();
        var ruleCatalog = new VersionedRuleCatalog();
        var ruleEngine = new DeterministicRuleEngine(new ShowplanXmlParser(), ruleCatalog, RuleThresholds.Default);
        var scoreRecalculator = new RecalculateConnectionScores(
            scoreStore,
            new HealthScoreCalculator(),
            ruleEngine,
            ruleCatalog,
            encryptor,
            clock);

        reader.Rows.Add(new QueryStoreStatRow(
            1,
            10,
            "AB",
            "CD",
            "16.0",
            "dbo.usp",
            Row(100, 5, Metrics(50, 100d))));
        reader.Texts[10] = "SELECT 1";
        reader.Plans[100] = ProblematicShowplanXml;

        // Simulamos que tras ApplyAsync la ventana ya contiene la consulta recién ingerida.
        scoreStore.Snapshot = new ConnectionScoringWindowSnapshot(
            ConnectionId: connection.Id,
            IngestionRunId: null,
            WindowStart: clock.Now.AddHours(-24),
            WindowEnd: clock.Now,
            EngineVersion: "16.0",
            ActualPlanAvailable: false,
            Queries:
            [
                new WindowQuerySnapshot(
                    QueryId: queryId,
                    QueryStoreQueryId: 1,
                    ResourceSamples: [new QueryResourceSample(50, 100.0, 100.0, 100.0)],
                    LatestPlan: new WindowPlanSnapshot(
                        PlanId: Guid.NewGuid(),
                        QueryStorePlanId: 100,
                        EncryptedPlanXml: encryptor.Encrypt(ProblematicShowplanXml),
                        EngineVersion: "16.0",
                        AnalysisStatus: PlanAnalysisStatus.Pending,
                        NotAnalyzableCause: null,
                        PlanExecutionCount: 50,
                        AvgRowsReturned: 1.0,
                        HasCurrentAnalysisRun: false,
                        ExistingFindings: [])),
            ]);

        var ingest = new IngestConnection(
            ingestionStore,
            reader,
            ingestionLock,
            encryptor,
            clock,
            new IngestionOptions(),
            new ProbeOptions(),
            scoreRecalculator);

        var outcome = await ingest.ExecuteAsync(connection.Id, IngestionTrigger.Manual);

        Assert.Equal(IngestionStatus.Succeeded, outcome.Status);
        var savedBatch = Assert.Single(scoreStore.SavedBatches);
        var savedRun = Assert.Single(ingestionStore.Runs);
        Assert.Equal(savedRun.Id, savedBatch.IngestionRunId);
        Assert.Single(savedBatch.HealthScores);
        Assert.Single(scoreStore.StoredScores);
    }

    [Fact]
    public void DependencyInjectionEnlazaParametrosYRegistraCalculadoraDeScore()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HealthScore:CpuWeight"] = "0.5",
                ["HealthScore:DurationWeight"] = "0.25",
                ["HealthScore:LogicalReadsWeight"] = "0.25",
                ["HealthScore:SaturationCapTau"] = "0.10",
                ["HealthScore:BasePriorityAlpha"] = "0.4",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(config);

        using var provider = services.BuildServiceProvider();
        var parameters = provider.GetRequiredService<HealthScoreParameters>();
        var calculator = provider.GetRequiredService<IHealthScoreCalculator>();

        Assert.Equal(0.5, parameters.CpuWeight, 6);
        Assert.Equal(0.25, parameters.DurationWeight, 6);
        Assert.Equal(0.25, parameters.LogicalReadsWeight, 6);
        Assert.Equal(0.10, parameters.SaturationCapTau, 6);
        Assert.Equal(0.4, parameters.BasePriorityAlpha, 6);

        var result = Assert.Single(calculator.CalculateWindow(
            [new QueryScoreInput(Guid.NewGuid(), 1, [new QueryResourceSample(10, 100.0, 100.0, 10.0)], [])],
            parameters));
        Assert.Equal(60.0, result.Score, 6);
    }

    private sealed class InMemoryScoreRecalculationStore : IScoreRecalculationStore
    {
        public ConnectionScoringWindowSnapshot? Snapshot { get; set; }

        public List<ConnectionScoringPersistenceBatch> SavedBatches { get; } = [];

        public List<HealthScore> StoredScores { get; } = [];

        public Task<ConnectionScoringWindowSnapshot?> LoadWindowSnapshotAsync(
            Guid connectionId,
            Guid? ingestionRunId,
            TimeSpan windowDuration,
            string catalogVersion,
            string thresholdConfigurationHash,
            CancellationToken cancellationToken = default)
        {
            if (Snapshot is null || Snapshot.ConnectionId != connectionId)
            {
                return Task.FromResult<ConnectionScoringWindowSnapshot?>(null);
            }

            return Task.FromResult<ConnectionScoringWindowSnapshot?>(
                Snapshot with { IngestionRunId = ingestionRunId ?? Snapshot.IngestionRunId });
        }

        public Task SaveBatchAsync(
            ConnectionScoringPersistenceBatch batch,
            CancellationToken cancellationToken = default)
        {
            SavedBatches.Add(batch);
            StoredScores.AddRange(batch.HealthScores);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HealthScore>> GetLatestConnectionScoresAsync(
            Guid connectionId,
            HealthScoreOrderBy orderBy = HealthScoreOrderBy.ScoreAscending,
            CancellationToken cancellationToken = default)
        {
            var latest = StoredScores
                .Where(s => s.ConnectionId == connectionId)
                .GroupBy(s => s.QueryId)
                .Select(g => g.OrderByDescending(s => s.CalculatedAt).ThenByDescending(s => s.WindowEnd).First());

            IReadOnlyList<HealthScore> ordered = orderBy switch
            {
                HealthScoreOrderBy.ConsumptionDescending => latest
                    .OrderByDescending(s => s.WeightedConsumptionPct)
                    .ThenBy(s => s.Score)
                    .ToArray(),
                _ => latest
                    .OrderBy(s => s.Score)
                    .ThenByDescending(s => s.WeightedConsumptionPct)
                    .ToArray(),
            };

            return Task.FromResult(ordered);
        }

        public Task<IReadOnlyList<HealthScore>> GetQueryScoreHistoryAsync(
            Guid connectionId,
            Guid queryId,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<HealthScore> history = StoredScores
                .Where(s => s.ConnectionId == connectionId && s.QueryId == queryId)
                .OrderBy(s => s.CalculatedAt)
                .ThenBy(s => s.WindowEnd)
                .ToArray();

            return Task.FromResult(history);
        }
    }
}
