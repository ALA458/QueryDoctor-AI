using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Queries;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>
/// Lector EF Core de solo lectura para listado y detalle de consultas (<c>GET /queries</c> y <c>GET /queries/{id}</c>; S2-09).
/// Descifra textos y evidencias en memoria usando <see cref="IFieldEncryptor"/> para que la capa Application los enmascare antes de responder.
/// </summary>
public sealed class EfQuerySourceReader : IQuerySourceReader
{
    private const int MaxScoreHistoryItems = 30;

    private readonly AppDbContext _db;
    private readonly IFieldEncryptor _encryptor;
    private readonly TimeProvider _clock;

    public EfQuerySourceReader(AppDbContext db, IFieldEncryptor encryptor, TimeProvider clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<QueryListSourceRow>> ListAsync(
        Guid? connectionId,
        TimeSpan windowDuration,
        CancellationToken cancellationToken = default)
    {
        var connectionsQuery = _db.Connections.AsNoTracking();
        if (connectionId.HasValue)
        {
            connectionsQuery = connectionsQuery.Where(c => c.Id == connectionId.Value);
        }

        var connections = await connectionsQuery
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        if (connections.Count == 0)
        {
            return [];
        }

        var connectionIds = connections.Keys.ToArray();

        var queries = await _db.Queries.AsNoTracking()
            .Where(q => connectionIds.Contains(q.ConnectionId))
            .ToListAsync(cancellationToken);

        if (queries.Count == 0)
        {
            return [];
        }

        var queryIds = queries.Select(q => q.Id).ToArray();
        var textIds = queries
            .Where(q => q.QueryTextId.HasValue)
            .Select(q => q.QueryTextId!.Value)
            .Distinct()
            .ToArray();

        var textsById = textIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await _db.QueryTexts.AsNoTracking()
                .Where(t => textIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.EncryptedSqlText, cancellationToken);

        var plans = await _db.Plans.AsNoTracking()
            .Where(p => queryIds.Contains(p.QueryId))
            .ToListAsync(cancellationToken);

        var plansById = plans.ToDictionary(p => p.Id);
        var planIdsByQuery = plans
            .GroupBy(p => p.QueryId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToHashSet());

        var runtimeStats = await _db.RuntimeStats.AsNoTracking()
            .Where(r => connectionIds.Contains(r.ConnectionId))
            .ToListAsync(cancellationToken);

        var scores = await _db.HealthScores.AsNoTracking()
            .Where(h => queryIds.Contains(h.QueryId))
            .ToListAsync(cancellationToken);

        var latestScoreByQuery = scores
            .GroupBy(h => h.QueryId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.CalculatedAt).ThenByDescending(x => x.Id).First());

        var succeededRuns = await _db.AnalysisRuns.AsNoTracking()
            .Where(r => queryIds.Contains(r.QueryId) && r.Status == AnalysisRunStatus.Succeeded)
            .ToListAsync(cancellationToken);

        var latestRunIdByQuery = succeededRuns
            .GroupBy(r => r.QueryId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).First().Id);

        var latestRunIds = latestRunIdByQuery.Values.ToArray();
        var findings = latestRunIds.Length == 0
            ? []
            : await _db.Findings.AsNoTracking()
                .Where(f => latestRunIds.Contains(f.AnalysisRunId))
                .ToListAsync(cancellationToken);

        var findingsByRunId = findings
            .GroupBy(f => f.AnalysisRunId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(f => f.Severity).ThenBy(f => f.FindingCode, StringComparer.Ordinal).ToList());

        // Calcular consumo dentro de la ventana por conexión como respaldo si aún no hay HealthScore calculado.
        var fallbackMetricsByQuery = ComputeFallbackConsumption(queries, plansById, runtimeStats, windowDuration);

        var result = new List<QueryListSourceRow>(queries.Count);
        foreach (var query in queries)
        {
            if (!connections.TryGetValue(query.ConnectionId, out var connection))
            {
                continue;
            }

            string? encryptedSql = null;
            if (query.QueryTextId.HasValue)
            {
                textsById.TryGetValue(query.QueryTextId.Value, out encryptedSql);
            }

            var decryptedSql = TryDecrypt(encryptedSql);

            latestScoreByQuery.TryGetValue(query.Id, out var latestScore);
            fallbackMetricsByQuery.TryGetValue(query.Id, out var fallback);

            // Si la ventana pedida tiene estadísticas reales, usamos su consumo en la ventana cuando no coincide o como base;
            // cuando hay HealthScore reciente, conservamos su puntaje, banda y consumo calculado de la ventana.
            var useWindowFallback = fallback.HasStats && latestScore is null;
            var consumptionPct = latestScore is not null
                ? latestScore.WeightedConsumptionPct
                : fallback.ConsumptionPct;
            var executions = latestScore is not null && !useWindowFallback
                ? latestScore.TotalExecutions
                : fallback.Executions;

            IReadOnlyList<QueryFindingSource> queryFindings = [];
            if (latestRunIdByQuery.TryGetValue(query.Id, out var runId)
                && findingsByRunId.TryGetValue(runId, out var runFindings))
            {
                queryFindings = runFindings
                    .Select(f => ToFindingSource(f, plansById))
                    .ToArray();
            }

            result.Add(new QueryListSourceRow(
                QueryId: query.Id,
                ConnectionId: query.ConnectionId,
                QueryStoreQueryId: query.QueryStoreQueryId,
                Server: connection.Server,
                DatabaseName: connection.DatabaseName,
                DecryptedSqlText: decryptedSql,
                ConsumptionPct: consumptionPct,
                Executions: executions,
                Score: latestScore?.Score,
                Band: latestScore?.Band,
                Findings: queryFindings));
        }

        return result;
    }

    public async Task<QueryDetailSource?> GetDetailAsync(
        Guid queryId,
        TimeSpan windowDuration,
        CancellationToken cancellationToken = default)
    {
        var query = await _db.Queries.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == queryId, cancellationToken);

        if (query is null)
        {
            return null;
        }

        var connection = await _db.Connections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.ConnectionId, cancellationToken);

        if (connection is null)
        {
            return null;
        }

        string? encryptedSql = null;
        if (query.QueryTextId.HasValue)
        {
            encryptedSql = await _db.QueryTexts.AsNoTracking()
                .Where(t => t.Id == query.QueryTextId.Value)
                .Select(t => t.EncryptedSqlText)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var decryptedSql = TryDecrypt(encryptedSql);

        var plans = await _db.Plans.AsNoTracking()
            .Where(p => p.QueryId == queryId)
            .OrderBy(p => p.IngestedAt)
            .ThenBy(p => p.QueryStorePlanId)
            .ToListAsync(cancellationToken);

        var planIds = plans.Select(p => p.Id).ToArray();
        var plansById = plans.ToDictionary(p => p.Id);

        var allQueryStats = planIds.Length == 0
            ? []
            : await _db.RuntimeStats.AsNoTracking()
                .Where(r => planIds.Contains(r.PlanId))
                .ToListAsync(cancellationToken);

        var windowEnd = await ResolveConnectionWindowEndAsync(query.ConnectionId, query.IngestedAt, allQueryStats, cancellationToken);
        var windowStart = windowEnd - windowDuration;

        var windowStats = allQueryStats
            .Where(r => r.IntervalEnd >= windowStart && r.IntervalStart <= windowEnd)
            .OrderBy(r => r.IntervalStart)
            .ThenBy(r => r.IntervalEnd)
            .ToList();

        // Tomar el último AnalysisRun exitoso por cada plan de la consulta para cubrir consultas multi-plan.
        var succeededRuns = await _db.AnalysisRuns.AsNoTracking()
            .Where(r => r.QueryId == queryId && r.Status == AnalysisRunStatus.Succeeded)
            .ToListAsync(cancellationToken);

        var latestRunIdsPerPlan = succeededRuns
            .GroupBy(r => r.PlanId)
            .Select(g => g.OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).First().Id)
            .ToArray();

        var findings = latestRunIdsPerPlan.Length == 0
            ? []
            : (await _db.Findings.AsNoTracking()
                .Where(f => latestRunIdsPerPlan.Contains(f.AnalysisRunId))
                .ToListAsync(cancellationToken))
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.FindingCode, StringComparer.Ordinal)
                .ToList();

        var findingSources = findings
            .Select(f => ToFindingSource(f, plansById))
            .ToArray();

        var connectionScores = await _db.HealthScores.AsNoTracking()
            .Where(h => h.ConnectionId == query.ConnectionId)
            .ToListAsync(cancellationToken);

        var latestScoresByQuery = connectionScores
            .GroupBy(h => h.QueryId)
            .Select(g => g.OrderByDescending(x => x.CalculatedAt).ThenByDescending(x => x.Id).First())
            .OrderBy(h => h.Score)
            .ThenBy(h => h.QueryId)
            .ToList();

        var scoreHistory = connectionScores
            .Where(h => h.QueryId == queryId)
            .OrderByDescending(h => h.CalculatedAt)
            .ThenByDescending(h => h.Id)
            .Take(MaxScoreHistoryItems)
            .Reverse()
            .ToList();

        var latestScore = scoreHistory.LastOrDefault();
        var rankIndex = latestScore is null
            ? -1
            : latestScoresByQuery.FindIndex(h => h.QueryId == queryId);

        var rankInWindow = rankIndex >= 0 ? rankIndex + 1 : 0;
        var totalInWindow = latestScoresByQuery.Count;

        return new QueryDetailSource(
            QueryId: query.Id,
            ConnectionId: query.ConnectionId,
            QueryStoreQueryId: query.QueryStoreQueryId,
            Server: connection.Server,
            DatabaseName: connection.DatabaseName,
            Platform: connection.Platform,
            QueryHash: query.QueryHash,
            OriginContext: query.OriginContext,
            WindowStart: windowStart,
            WindowEnd: windowEnd,
            DecryptedSqlText: decryptedSql,
            Plans: plans,
            RuntimeStats: windowStats,
            Findings: findingSources,
            LatestScore: latestScore,
            RankInWindow: rankInWindow,
            TotalQueriesInWindow: totalInWindow,
            ScoreHistory: scoreHistory);
    }

    public async Task<QueryPlanTreeSource?> GetPlanTreeAsync(
        Guid queryId,
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        var plan = await _db.Plans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == planId && p.QueryId == queryId, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var latestSucceededRunId = await _db.AnalysisRuns.AsNoTracking()
            .Where(r => r.QueryId == queryId && r.PlanId == planId && r.Status == AnalysisRunStatus.Succeeded)
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var findings = latestSucceededRunId is null
            ? []
            : (await _db.Findings.AsNoTracking()
                .Where(f => f.AnalysisRunId == latestSucceededRunId.Value)
                .ToListAsync(cancellationToken))
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.FindingCode, StringComparer.Ordinal)
                .ToList();

        var plansById = new Dictionary<Guid, Plan>(1) { [plan.Id] = plan };
        var findingSources = findings
            .Select(f => ToFindingSource(f, plansById))
            .ToArray();

        return new QueryPlanTreeSource(
            QueryId: queryId,
            PlanId: plan.Id,
            QueryStorePlanId: plan.QueryStorePlanId,
            QueryPlanHash: plan.QueryPlanHash,
            EngineVersion: plan.EngineVersion,
            AnalysisStatus: plan.AnalysisStatus,
            NotAnalyzableCause: plan.NotAnalyzableCause,
            IngestedAt: plan.IngestedAt,
            DecryptedPlanXml: TryDecrypt(plan.EncryptedPlanXml),
            Findings: findingSources);
    }

    private async Task<DateTimeOffset> ResolveConnectionWindowEndAsync(
        Guid connectionId,
        DateTimeOffset fallbackTimestamp,
        List<RuntimeStat> queryStats,
        CancellationToken cancellationToken)
    {
        if (queryStats.Count > 0)
        {
            var latestConnectionStat = await _db.RuntimeStats.AsNoTracking()
                .Where(r => r.ConnectionId == connectionId)
                .MaxAsync(r => (DateTimeOffset?)r.IntervalEnd, cancellationToken);

            return latestConnectionStat ?? queryStats.Max(r => r.IntervalEnd);
        }

        var anyConnectionStat = await _db.RuntimeStats.AsNoTracking()
            .Where(r => r.ConnectionId == connectionId)
            .MaxAsync(r => (DateTimeOffset?)r.IntervalEnd, cancellationToken);

        return anyConnectionStat ?? (fallbackTimestamp != default ? fallbackTimestamp : _clock.GetUtcNow());
    }

    private QueryFindingSource ToFindingSource(Finding finding, Dictionary<Guid, Plan> plansById)
    {
        plansById.TryGetValue(finding.PlanId, out var plan);

        return new QueryFindingSource(
            Id: finding.Id,
            PlanId: finding.PlanId,
            QueryStorePlanId: plan?.QueryStorePlanId ?? 0,
            FindingCode: finding.FindingCode,
            RuleId: finding.RuleId,
            RuleVersion: finding.RuleVersion,
            CatalogVersion: finding.CatalogVersion,
            SchemaVersion: finding.SchemaVersion,
            Kind: finding.Kind,
            Severity: finding.Severity,
            EvidenceClass: finding.EvidenceClass,
            NodesJson: finding.NodesJson,
            DecryptedEvidenceJson: TryDecrypt(finding.EncryptedEvidenceJson),
            ContextJson: finding.ContextJson,
            ThresholdsUsedJson: finding.ThresholdsUsedJson,
            EngineVersion: finding.EngineVersion,
            AnalyzedAt: finding.AnalyzedAt);
    }

    private string? TryDecrypt(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return null;
        }

        return _encryptor.Decrypt(cipherText);
    }

    private static Dictionary<Guid, (bool HasStats, double ConsumptionPct, long Executions)> ComputeFallbackConsumption(
        List<Query> queries,
        Dictionary<Guid, Plan> plansById,
        List<RuntimeStat> runtimeStats,
        TimeSpan windowDuration)
    {
        var result = new Dictionary<Guid, (bool, double, long)>(queries.Count);

        foreach (var connectionGroup in queries.GroupBy(q => q.ConnectionId))
        {
            var connStats = runtimeStats.Where(r => r.ConnectionId == connectionGroup.Key).ToList();
            if (connStats.Count == 0)
            {
                continue;
            }

            var windowEnd = connStats.Max(r => r.IntervalEnd);
            var windowStart = windowEnd - windowDuration;
            var windowStats = connStats
                .Where(r => r.IntervalEnd >= windowStart && r.IntervalStart <= windowEnd)
                .ToList();

            if (windowStats.Count == 0)
            {
                continue;
            }

            var perQueryTotals = new Dictionary<Guid, (long Execs, double Cpu, double Dur, double Reads)>();
            double totalCpu = 0;
            double totalDur = 0;
            double totalReads = 0;

            foreach (var stat in windowStats)
            {
                if (!plansById.TryGetValue(stat.PlanId, out var plan))
                {
                    continue;
                }

                var execs = Math.Max(0L, stat.ExecutionCount);
                var cpu = execs * Math.Max(0.0, stat.AvgCpuTimeMicroseconds);
                var dur = execs * Math.Max(0.0, stat.AvgDurationMicroseconds);
                var reads = execs * Math.Max(0.0, stat.AvgLogicalIoReads);

                totalCpu += cpu;
                totalDur += dur;
                totalReads += reads;

                perQueryTotals.TryGetValue(plan.QueryId, out var current);
                perQueryTotals[plan.QueryId] = (
                    current.Execs + execs,
                    current.Cpu + cpu,
                    current.Dur + dur,
                    current.Reads + reads);
            }

            foreach (var (queryId, totals) in perQueryTotals)
            {
                var cpuShare = totalCpu > 0 ? totals.Cpu / totalCpu : 0.0;
                var durShare = totalDur > 0 ? totals.Dur / totalDur : 0.0;
                var readsShare = totalReads > 0 ? totals.Reads / totalReads : 0.0;

                var weightedPct = (HealthScoreParameters.DefaultCpuWeight * cpuShare
                    + HealthScoreParameters.DefaultDurationWeight * durShare
                    + HealthScoreParameters.DefaultLogicalReadsWeight * readsShare) * 100.0;

                result[queryId] = (true, weightedPct, totals.Execs);
            }
        }

        return result;
    }
}
