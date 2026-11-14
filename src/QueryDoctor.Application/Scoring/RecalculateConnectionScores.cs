using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Scoring;

/// <summary>
/// Caso de uso que analiza los planes vigentes de una conexión y recalcula el puntaje compuesto de salud <c>H(q)</c>
/// tras cada corrida de ingesta, conservando el historial en <c>analysis.health_score</c> (S2-02, RF-SCO-01..06, Arquitectura §6.2–6.3).
/// </summary>
public sealed class RecalculateConnectionScores
{
    public static readonly TimeSpan DefaultWindowDuration = TimeSpan.FromHours(24);

    private readonly IScoreRecalculationStore _store;
    private readonly IHealthScoreCalculator _calculator;
    private readonly IRuleEngine _ruleEngine;
    private readonly IRuleCatalog _ruleCatalog;
    private readonly IFieldEncryptor _encryptor;
    private readonly TimeProvider _clock;
    private readonly HealthScoreParameters _scoreParameters;
    private readonly RuleThresholds _ruleThresholds;

    public RecalculateConnectionScores(
        IScoreRecalculationStore store,
        IHealthScoreCalculator calculator,
        IRuleEngine ruleEngine,
        IRuleCatalog ruleCatalog,
        IFieldEncryptor encryptor,
        TimeProvider clock,
        HealthScoreParameters? scoreParameters = null,
        RuleThresholds? ruleThresholds = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _ruleCatalog = ruleCatalog ?? throw new ArgumentNullException(nameof(ruleCatalog));
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _scoreParameters = scoreParameters ?? HealthScoreParameters.Default;
        _ruleThresholds = ruleThresholds ?? RuleThresholds.Default;
        _scoreParameters.Validate();
    }

    public async Task<ScoreRecalculationOutcome> ExecuteAsync(
        Guid connectionId,
        Guid? ingestionRunId = null,
        TimeSpan? windowDuration = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWindow = windowDuration is { TotalMinutes: > 0 } duration
            ? duration
            : DefaultWindowDuration;

        var thresholdHash = _ruleThresholds.ComputeConfigurationHash();
        var snapshot = await _store.LoadWindowSnapshotAsync(
            connectionId,
            ingestionRunId,
            effectiveWindow,
            _ruleCatalog.CatalogVersion,
            thresholdHash,
            cancellationToken);

        var now = _clock.GetUtcNow();
        if (snapshot is null || snapshot.Queries.Count == 0)
        {
            return new ScoreRecalculationOutcome(
                ConnectionId: connectionId,
                IngestionRunId: ingestionRunId,
                WindowStart: now - effectiveWindow,
                WindowEnd: now,
                QueriesScored: 0,
                PlansAnalyzed: 0,
                Scores: []);
        }

        // 1. Calcular participaciones de consumo en la ventana W (Ecuación 4.1) para alimentar tanto REG-03/REG-04 como I(q).
        var provisionalInputs = snapshot.Queries
            .Select(q => new QueryScoreInput(q.QueryId, q.QueryStoreQueryId, q.ResourceSamples, []))
            .ToArray();

        var consumptionShares = _calculator.CalculateConsumptionShares(provisionalInputs, _scoreParameters);

        var availableEvidence = BuildAvailableEvidenceClasses(snapshot.ActualPlanAvailable);
        var thresholdSetId = Guid.NewGuid();
        var thresholdSet = new ThresholdSet
        {
            Id = thresholdSetId,
            CatalogVersion = _ruleCatalog.CatalogVersion,
            ConfigurationHash = thresholdHash,
            ThresholdsJson = _ruleThresholds.ToCanonicalJson(),
            CreatedAt = now,
        };

        var analyzedPlans = new List<AnalyzedPlanPersistenceItem>();
        var finalScoreInputs = new List<QueryScoreInput>(snapshot.Queries.Count);

        // 2. Evaluar planes pendientes o cuyo contexto de consumo haya cambiado, y recolectar los hallazgos vigentes por consulta.
        foreach (var query in snapshot.Queries)
        {
            var share = consumptionShares[query.QueryId];
            var findingsForScore = EvaluateQueryPlanIfNeeded(
                snapshot,
                query,
                share,
                availableEvidence,
                thresholdSetId,
                now,
                analyzedPlans);

            finalScoreInputs.Add(new QueryScoreInput(
                QueryId: query.QueryId,
                QueryStoreQueryId: query.QueryStoreQueryId,
                ResourceSamples: query.ResourceSamples,
                Findings: findingsForScore));
        }

        // 3. Calcular el puntaje de salud H(q) y prioridad P(q) para todas las consultas de la ventana (Ecuaciones 4.1–4.5).
        var scoreResults = _calculator.CalculateWindow(
            finalScoreInputs,
            _scoreParameters,
            HealthScoreOrderBy.ScoreAscending);

        var healthScoreEntities = scoreResults
            .Select(r => r.ToEntity(
                connectionId: snapshot.ConnectionId,
                windowStart: snapshot.WindowStart,
                windowEnd: snapshot.WindowEnd,
                calculatedAt: now,
                ingestionRunId: snapshot.IngestionRunId))
            .ToArray();

        // 4. Persistir corridas de análisis nuevas y anexar los puntajes al historial en analysis.health_score (RF-SCO-05).
        var batch = new ConnectionScoringPersistenceBatch(
            ConnectionId: snapshot.ConnectionId,
            IngestionRunId: snapshot.IngestionRunId,
            ThresholdSet: thresholdSet,
            AnalyzedPlans: analyzedPlans,
            HealthScores: healthScoreEntities);

        await _store.SaveBatchAsync(batch, cancellationToken);

        return new ScoreRecalculationOutcome(
            ConnectionId: snapshot.ConnectionId,
            IngestionRunId: snapshot.IngestionRunId,
            WindowStart: snapshot.WindowStart,
            WindowEnd: snapshot.WindowEnd,
            QueriesScored: scoreResults.Count,
            PlansAnalyzed: analyzedPlans.Count,
            Scores: scoreResults);
    }

    private IReadOnlyList<QueryFindingSample> EvaluateQueryPlanIfNeeded(
        ConnectionScoringWindowSnapshot snapshot,
        WindowQuerySnapshot query,
        QueryConsumptionShare share,
        IReadOnlySet<EvidenceClass> availableEvidence,
        Guid thresholdSetId,
        DateTimeOffset now,
        List<AnalyzedPlanPersistenceItem> analyzedPlans)
    {
        var plan = query.LatestPlan;
        if (plan is null)
        {
            return [];
        }

        // Si el plan no tiene XML cifrado pero ya cuenta con hallazgos persistidos, se reutilizan.
        if (string.IsNullOrWhiteSpace(plan.EncryptedPlanXml) && plan.ExistingFindings.Count > 0)
        {
            return plan.ExistingFindings;
        }

        string? decryptedXml = null;
        if (!string.IsNullOrWhiteSpace(plan.EncryptedPlanXml))
        {
            decryptedXml = _encryptor.Decrypt(plan.EncryptedPlanXml);
        }

        var analysisRequest = new PlanAnalysisRequest(
            ShowplanXml: decryptedXml,
            QueryStoreQueryId: query.QueryStoreQueryId,
            QueryStorePlanId: plan.QueryStorePlanId,
            FallbackEngineVersion: plan.EngineVersion ?? snapshot.EngineVersion,
            Thresholds: _ruleThresholds,
            AvailableEvidenceClasses: availableEvidence,
            QueryExecutionCount: share.TotalExecutions > 0 ? share.TotalExecutions : plan.PlanExecutionCount,
            QueryConsumptionPct: share.WeightedConsumptionPct,
            AvgRowsReturned: plan.AvgRowsReturned,
            AnalyzedAt: now);

        var analysisResult = _ruleEngine.Analyze(analysisRequest);

        var evaluatedSamples = analysisResult.Findings
            .Select(f => new QueryFindingSample(
                RuleId: f.Rule.Id,
                FindingCode: f.FindingId,
                Severity: AnalysisEnumCodes.ParseFindingSeverity(f.Severity),
                EvidenceClass: AnalysisEnumCodes.ParseEvidenceClass(f.EvidenceClass),
                Kind: AnalysisEnumCodes.ParseFindingKind(f.Kind)))
            .ToArray();

        if (!plan.HasCurrentAnalysisRun || !AreFindingsEquivalent(plan.ExistingFindings, evaluatedSamples))
        {
            var analysisRunId = Guid.NewGuid();
            var analysisRun = new AnalysisRun
            {
                Id = analysisRunId,
                ConnectionId = snapshot.ConnectionId,
                QueryId = query.QueryId,
                PlanId = plan.PlanId,
                ThresholdSetId = thresholdSetId,
                CatalogVersion = analysisResult.CatalogVersion,
                EngineVersion = analysisResult.EngineVersion,
                ThresholdsUsedJson = analysisResult.ThresholdsUsedJson,
                Status = AnalysisRunStatus.Succeeded,
                FindingsCount = analysisResult.Findings.Count,
                StartedAt = now,
                FinishedAt = now,
                ErrorCause = analysisResult.NotAnalyzableCause,
            };

            var findingEntities = analysisResult.Findings
                .Select(f => new Finding
                {
                    Id = Guid.NewGuid(),
                    AnalysisRunId = analysisRunId,
                    ConnectionId = snapshot.ConnectionId,
                    QueryId = query.QueryId,
                    PlanId = plan.PlanId,
                    FindingCode = f.FindingId,
                    RuleId = f.Rule.Id,
                    RuleVersion = f.Rule.Version,
                    CatalogVersion = f.CatalogVersion,
                    SchemaVersion = f.SchemaVersion,
                    Kind = AnalysisEnumCodes.ParseFindingKind(f.Kind),
                    Severity = AnalysisEnumCodes.ParseFindingSeverity(f.Severity),
                    EvidenceClass = AnalysisEnumCodes.ParseEvidenceClass(f.EvidenceClass),
                    NodesJson = FindingContractJson.SerializeNodes(f.Nodes),
                    EncryptedEvidenceJson = _encryptor.Encrypt(FindingContractJson.SerializeEvidence(f.Evidence)),
                    ContextJson = FindingContractJson.SerializeContext(f.Context),
                    ThresholdsUsedJson = RuleThresholds.SerializeDictionaryToCanonicalJson(f.ThresholdsUsed),
                    EngineVersion = f.EngineVersion,
                    AnalyzedAt = now,
                })
                .ToArray();

            analyzedPlans.Add(new AnalyzedPlanPersistenceItem(
                PlanId: plan.PlanId,
                UpdatedPlanAnalysisStatus: analysisResult.AnalysisStatus,
                AnalysisRun: analysisRun,
                Findings: findingEntities));
        }

        return evaluatedSamples;
    }

    private static HashSet<EvidenceClass> BuildAvailableEvidenceClasses(bool actualPlanAvailable)
    {
        var set = new HashSet<EvidenceClass>
        {
            EvidenceClass.Estimada,
            EvidenceClass.EjecucionAgregada,
            EvidenceClass.NoAplica,
        };

        if (actualPlanAvailable)
        {
            set.Add(EvidenceClass.PlanReal);
        }

        return set;
    }

    private static bool AreFindingsEquivalent(
        IReadOnlyList<QueryFindingSample> existing,
        QueryFindingSample[] current)
    {
        if (existing.Count != current.Length)
        {
            return false;
        }

        var orderedExisting = existing
            .OrderBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.FindingCode, StringComparer.Ordinal)
            .ToArray();

        var orderedCurrent = current
            .OrderBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.FindingCode, StringComparer.Ordinal)
            .ToArray();

        for (var i = 0; i < orderedExisting.Length; i++)
        {
            if (!string.Equals(orderedExisting[i].RuleId, orderedCurrent[i].RuleId, StringComparison.Ordinal) ||
                !string.Equals(orderedExisting[i].FindingCode, orderedCurrent[i].FindingCode, StringComparison.Ordinal) ||
                orderedExisting[i].Severity != orderedCurrent[i].Severity ||
                orderedExisting[i].EvidenceClass != orderedCurrent[i].EvidenceClass)
            {
                return false;
            }
        }

        return true;
    }
}
