using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Scoring;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Infrastructure.Scoring;

/// <summary>
/// Adaptador EF Core de <see cref="IScoreRecalculationStore"/> para cargar la ventana de análisis de una conexión,
/// persistir corridas de análisis y hallazgos, y almacenar y consultar el historial de puntajes en <c>analysis.health_score</c> (S2-02, RF-SCO-04, RF-SCO-05).
/// </summary>
public sealed class EfScoreRecalculationStore : IScoreRecalculationStore
{
    private readonly AppDbContext _db;

    public EfScoreRecalculationStore(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<ConnectionScoringWindowSnapshot?> LoadWindowSnapshotAsync(
        Guid connectionId,
        Guid? ingestionRunId,
        TimeSpan windowDuration,
        string catalogVersion,
        string thresholdConfigurationHash,
        CancellationToken cancellationToken = default)
    {
        var connectionExists = await _db.Connections
            .AsNoTracking()
            .AnyAsync(c => c.Id == connectionId, cancellationToken);

        if (!connectionExists)
        {
            return null;
        }

        var profile = await _db.CapabilityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ConnectionId == connectionId, cancellationToken);

        var queries = await _db.Queries
            .AsNoTracking()
            .Where(q => q.ConnectionId == connectionId)
            .ToListAsync(cancellationToken);

        var actualPlanAvailable = false;

        if (queries.Count == 0)
        {
            var emptyNow = DateTimeOffset.UtcNow;
            return new ConnectionScoringWindowSnapshot(
                ConnectionId: connectionId,
                IngestionRunId: ingestionRunId,
                WindowStart: emptyNow - windowDuration,
                WindowEnd: emptyNow,
                EngineVersion: profile?.EngineVersion,
                ActualPlanAvailable: actualPlanAvailable,
                Queries: []);
        }

        var plans = await _db.Plans
            .AsNoTracking()
            .Where(p => p.ConnectionId == connectionId)
            .ToListAsync(cancellationToken);

        var runtimeStats = await _db.RuntimeStats
            .AsNoTracking()
            .Where(r => r.ConnectionId == connectionId)
            .ToListAsync(cancellationToken);

        var windowEnd = runtimeStats.Count > 0
            ? runtimeStats.Max(r => r.IntervalEnd)
            : queries.Max(q => q.IngestedAt);

        var windowStart = windowEnd - windowDuration;

        var windowStats = runtimeStats
            .Where(r => r.IntervalEnd >= windowStart && r.IntervalStart <= windowEnd)
            .ToList();

        var matchingThresholdSetIds = (await _db.ThresholdSets
            .AsNoTracking()
            .Where(t => t.CatalogVersion == catalogVersion && t.ConfigurationHash == thresholdConfigurationHash)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var succeededRuns = await _db.AnalysisRuns
            .AsNoTracking()
            .Where(a => a.ConnectionId == connectionId && a.Status == AnalysisRunStatus.Succeeded)
            .ToListAsync(cancellationToken);

        var allFindings = await _db.Findings
            .AsNoTracking()
            .Where(f => f.ConnectionId == connectionId)
            .ToListAsync(cancellationToken);

        var findingsByRunId = allFindings
            .GroupBy(f => f.AnalysisRunId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var plansByQueryId = plans
            .GroupBy(p => p.QueryId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(p => p.IngestedAt)
                      .ThenByDescending(p => p.QueryStorePlanId)
                      .ToList());

        var statsByPlanId = windowStats
            .GroupBy(r => r.PlanId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var latestRunByPlanId = succeededRuns
            .GroupBy(a => a.PlanId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.FinishedAt ?? a.StartedAt).First());

        var querySnapshots = new List<WindowQuerySnapshot>(queries.Count);

        foreach (var query in queries.OrderBy(q => q.QueryStoreQueryId).ThenBy(q => q.Id))
        {
            var queryPlans = plansByQueryId.GetValueOrDefault(query.Id) ?? [];
            var queryStats = new List<RuntimeStat>();

            foreach (var plan in queryPlans)
            {
                if (statsByPlanId.TryGetValue(plan.Id, out var pStats))
                {
                    queryStats.AddRange(pStats);
                }
            }

            var resourceSamples = queryStats
                .Select(s => new QueryResourceSample(
                    ExecutionCount: s.ExecutionCount,
                    AvgCpuTimeMicroseconds: s.AvgCpuTimeMicroseconds,
                    AvgDurationMicroseconds: s.AvgDurationMicroseconds,
                    AvgLogicalIoReads: s.AvgLogicalIoReads))
                .ToArray();

            WindowPlanSnapshot? latestPlanSnapshot = null;
            if (queryPlans.Count > 0)
            {
                var latestPlan = queryPlans[0];
                var latestPlanStats = statsByPlanId.GetValueOrDefault(latestPlan.Id) ?? [];
                var planExecutions = latestPlanStats.Sum(s => s.ExecutionCount);
                double? avgRowsReturned = planExecutions > 0
                    ? latestPlanStats.Sum(s => s.ExecutionCount * s.AvgRowCount) / planExecutions
                    : null;

                var hasCurrentRun = false;
                IReadOnlyList<QueryFindingSample> existingFindings = [];

                if (latestRunByPlanId.TryGetValue(latestPlan.Id, out var latestRun))
                {
                    hasCurrentRun =
                        string.Equals(latestRun.CatalogVersion, catalogVersion, StringComparison.Ordinal) &&
                        matchingThresholdSetIds.Contains(latestRun.ThresholdSetId);

                    if (findingsByRunId.TryGetValue(latestRun.Id, out var runFindings))
                    {
                        existingFindings = runFindings
                            .OrderBy(f => f.RuleId, StringComparer.Ordinal)
                            .ThenBy(f => f.FindingCode, StringComparer.Ordinal)
                            .Select(f => new QueryFindingSample(
                                RuleId: f.RuleId,
                                FindingCode: f.FindingCode,
                                Severity: f.Severity,
                                EvidenceClass: f.EvidenceClass,
                                Kind: f.Kind))
                            .ToArray();
                    }
                }
                else
                {
                    // Respaldo por si existen hallazgos vinculados directamente al plan/consulta en pruebas.
                    var directPlanFindings = allFindings
                        .Where(f => f.PlanId == latestPlan.Id)
                        .OrderBy(f => f.RuleId, StringComparer.Ordinal)
                        .ThenBy(f => f.FindingCode, StringComparer.Ordinal)
                        .Select(f => new QueryFindingSample(
                            RuleId: f.RuleId,
                            FindingCode: f.FindingCode,
                            Severity: f.Severity,
                            EvidenceClass: f.EvidenceClass,
                            Kind: f.Kind))
                        .ToArray();

                    if (directPlanFindings.Length > 0)
                    {
                        existingFindings = directPlanFindings;
                    }
                }

                latestPlanSnapshot = new WindowPlanSnapshot(
                    PlanId: latestPlan.Id,
                    QueryStorePlanId: latestPlan.QueryStorePlanId,
                    EncryptedPlanXml: latestPlan.EncryptedPlanXml,
                    EngineVersion: latestPlan.EngineVersion,
                    AnalysisStatus: latestPlan.AnalysisStatus,
                    NotAnalyzableCause: latestPlan.NotAnalyzableCause,
                    PlanExecutionCount: planExecutions,
                    AvgRowsReturned: avgRowsReturned,
                    HasCurrentAnalysisRun: hasCurrentRun,
                    ExistingFindings: existingFindings);
            }

            querySnapshots.Add(new WindowQuerySnapshot(
                QueryId: query.Id,
                QueryStoreQueryId: query.QueryStoreQueryId,
                ResourceSamples: resourceSamples,
                LatestPlan: latestPlanSnapshot));
        }

        return new ConnectionScoringWindowSnapshot(
            ConnectionId: connectionId,
            IngestionRunId: ingestionRunId,
            WindowStart: windowStart,
            WindowEnd: windowEnd,
            EngineVersion: profile?.EngineVersion,
            ActualPlanAvailable: actualPlanAvailable,
            Queries: querySnapshots);
    }

    public async Task SaveBatchAsync(
        ConnectionScoringPersistenceBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (batch.AnalyzedPlans.Count > 0)
        {
            var existingThresholdSet = await _db.ThresholdSets
                .FirstOrDefaultAsync(
                    t => t.CatalogVersion == batch.ThresholdSet.CatalogVersion &&
                         t.ConfigurationHash == batch.ThresholdSet.ConfigurationHash,
                    cancellationToken);

            Guid effectiveThresholdSetId;
            if (existingThresholdSet is not null)
            {
                effectiveThresholdSetId = existingThresholdSet.Id;
            }
            else
            {
                _db.ThresholdSets.Add(batch.ThresholdSet);
                effectiveThresholdSetId = batch.ThresholdSet.Id;
            }

            var planIdsToUpdate = batch.AnalyzedPlans.Select(p => p.PlanId).ToHashSet();
            var trackedPlans = await _db.Plans
                .Where(p => planIdsToUpdate.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            foreach (var analyzed in batch.AnalyzedPlans)
            {
                if (trackedPlans.TryGetValue(analyzed.PlanId, out var planEntity))
                {
                    planEntity.AnalysisStatus = analyzed.UpdatedPlanAnalysisStatus;
                }

                analyzed.AnalysisRun.ThresholdSetId = effectiveThresholdSetId;
                _db.AnalysisRuns.Add(analyzed.AnalysisRun);
                _db.Findings.AddRange(analyzed.Findings);
            }
        }

        if (batch.HealthScores.Count > 0)
        {
            // Se anexan nuevas filas a analysis.health_score sin sobrescribir las anteriores para conservar el historial (RF-SCO-05).
            _db.HealthScores.AddRange(batch.HealthScores);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HealthScore>> GetLatestConnectionScoresAsync(
        Guid connectionId,
        HealthScoreOrderBy orderBy = HealthScoreOrderBy.ScoreAscending,
        CancellationToken cancellationToken = default)
    {
        var allScores = await _db.HealthScores
            .AsNoTracking()
            .Where(h => h.ConnectionId == connectionId)
            .ToListAsync(cancellationToken);

        if (allScores.Count == 0)
        {
            return [];
        }

        var latestPerQuery = allScores
            .GroupBy(h => h.QueryId)
            .Select(g => g
                .OrderByDescending(h => h.CalculatedAt)
                .ThenByDescending(h => h.WindowEnd)
                .First())
            .ToList();

        return orderBy switch
        {
            HealthScoreOrderBy.ConsumptionDescending => latestPerQuery
                .OrderByDescending(h => h.WeightedConsumptionPct)
                .ThenBy(h => h.Score)
                .ThenBy(h => h.QueryId)
                .ToArray(),
            _ => latestPerQuery
                .OrderBy(h => h.Score)
                .ThenByDescending(h => h.WeightedConsumptionPct)
                .ThenBy(h => h.QueryId)
                .ToArray(),
        };
    }

    public async Task<IReadOnlyList<HealthScore>> GetQueryScoreHistoryAsync(
        Guid connectionId,
        Guid queryId,
        CancellationToken cancellationToken = default)
    {
        var history = await _db.HealthScores
            .AsNoTracking()
            .Where(h => h.ConnectionId == connectionId && h.QueryId == queryId)
            .ToListAsync(cancellationToken);

        return history
            .OrderBy(h => h.CalculatedAt)
            .ThenBy(h => h.WindowEnd)
            .ToArray();
    }
}
