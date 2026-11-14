using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Scoring;

/// <summary>
/// Implementación pura, determinista y sin E/S de la calculadora del puntaje compuesto de salud <c>H(q)</c>
/// conforme a las Ecuaciones 4.1 a 4.5 del SRS v1.1 (S2-02, RF-SCO-01..06, CA-HU08.1..08.3, Invariante 7).
/// </summary>
public sealed class HealthScoreCalculator : IHealthScoreCalculator
{
    private readonly HealthScoreParameters _defaultParameters;

    public HealthScoreCalculator(HealthScoreParameters? defaultParameters = null)
    {
        _defaultParameters = defaultParameters ?? HealthScoreParameters.Default;
        _defaultParameters.Validate();
    }

    public IReadOnlyDictionary<Guid, QueryConsumptionShare> CalculateConsumptionShares(
        IReadOnlyList<QueryScoreInput> workloadQueries,
        HealthScoreParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(workloadQueries);

        var effectiveParameters = parameters ?? _defaultParameters;
        effectiveParameters.Validate();

        if (workloadQueries.Count == 0)
        {
            return new Dictionary<Guid, QueryConsumptionShare>();
        }

        // Orden canónico para garantizar que las sumas en punto flotante sean idénticas sin importar el orden de entrada.
        var orderedQueries = workloadQueries
            .OrderBy(q => q.QueryStoreQueryId)
            .ThenBy(q => q.QueryId)
            .ToArray();

        var totalsPerQuery = new (Guid QueryId, long QueryStoreQueryId, long Executions, double Cpu, double Duration, double Reads)[orderedQueries.Length];
        var windowCpuTotal = 0.0;
        var windowDurationTotal = 0.0;
        var windowReadsTotal = 0.0;

        for (var idx = 0; idx < orderedQueries.Length; idx++)
        {
            var query = orderedQueries[idx];
            var samples = (query.ResourceSamples ?? [])
                .Where(s => s.ExecutionCount > 0)
                .OrderBy(s => s.ExecutionCount)
                .ThenBy(s => s.AvgCpuTimeMicroseconds)
                .ThenBy(s => s.AvgDurationMicroseconds)
                .ThenBy(s => s.AvgLogicalIoReads);

            long queryExecutions = 0;
            var queryCpu = 0.0;
            var queryDuration = 0.0;
            var queryReads = 0.0;

            foreach (var sample in samples)
            {
                var execs = sample.ExecutionCount;
                var avgCpu = Math.Max(0.0, sample.AvgCpuTimeMicroseconds);
                var avgDur = Math.Max(0.0, sample.AvgDurationMicroseconds);
                var avgReads = Math.Max(0.0, sample.AvgLogicalIoReads);

                queryExecutions += execs;
                queryCpu += execs * avgCpu;
                queryDuration += execs * avgDur;
                queryReads += execs * avgReads;
            }

            totalsPerQuery[idx] = (query.QueryId, query.QueryStoreQueryId, queryExecutions, queryCpu, queryDuration, queryReads);
            windowCpuTotal += queryCpu;
            windowDurationTotal += queryDuration;
            windowReadsTotal += queryReads;
        }

        var result = new Dictionary<Guid, QueryConsumptionShare>(orderedQueries.Length);

        foreach (var item in totalsPerQuery)
        {
            var cpuShare = windowCpuTotal > 0.0 ? item.Cpu / windowCpuTotal : 0.0;
            var durationShare = windowDurationTotal > 0.0 ? item.Duration / windowDurationTotal : 0.0;
            var readsShare = windowReadsTotal > 0.0 ? item.Reads / windowReadsTotal : 0.0;

            var weightedShare =
                (effectiveParameters.CpuWeight * cpuShare) +
                (effectiveParameters.DurationWeight * durationShare) +
                (effectiveParameters.LogicalReadsWeight * readsShare);

            result[item.QueryId] = new QueryConsumptionShare(
                QueryId: item.QueryId,
                QueryStoreQueryId: item.QueryStoreQueryId,
                TotalExecutions: item.Executions,
                TotalCpuMicroseconds: item.Cpu,
                TotalDurationMicroseconds: item.Duration,
                TotalLogicalReads: item.Reads,
                CpuShare: cpuShare,
                DurationShare: durationShare,
                LogicalReadsShare: readsShare,
                WeightedResourceShare: weightedShare,
                WeightedConsumptionPct: weightedShare * 100.0);
        }

        return result;
    }

    public IReadOnlyList<QueryHealthScoreResult> CalculateWindow(
        IReadOnlyList<QueryScoreInput> workloadQueries,
        HealthScoreParameters? parameters = null,
        HealthScoreOrderBy orderBy = HealthScoreOrderBy.ScoreAscending)
    {
        ArgumentNullException.ThrowIfNull(workloadQueries);

        var effectiveParameters = parameters ?? _defaultParameters;
        effectiveParameters.Validate();

        if (workloadQueries.Count == 0)
        {
            return [];
        }

        var sharesByQuery = CalculateConsumptionShares(workloadQueries, effectiveParameters);
        var parametersJson = effectiveParameters.ToCanonicalJson();
        var parametersHash = effectiveParameters.ComputeConfigurationHash();

        var orderedQueries = workloadQueries
            .OrderBy(q => q.QueryStoreQueryId)
            .ThenBy(q => q.QueryId)
            .ToArray();

        var results = new List<QueryHealthScoreResult>(orderedQueries.Length);

        foreach (var query in orderedQueries)
        {
            var share = sharesByQuery[query.QueryId];

            // Paso 2 (Ecuación 4.2): I(q) = min(1.0, (w_CPU * s_CPU + w_DUR * s_DUR + w_LECT * s_LECT) / tau)
            var impactComponent = Math.Clamp(
                share.WeightedResourceShare / effectiveParameters.SaturationCapTau,
                0.0,
                1.0);

            // Paso 3 (Ecuación 4.3): S(q) = 1.0 - prod_{i in hallazgos(q)} (1.0 - sev_i * conf_i)
            var canonicalFindings = NormalizeFindings(query.Findings);
            var contributions = new List<FindingSeverityContribution>(canonicalFindings.Length);
            var survivalProduct = 1.0;

            foreach (var finding in canonicalFindings)
            {
                var sevWeight = effectiveParameters.GetSeverityWeight(finding.Severity);
                var confWeight = effectiveParameters.GetConfidenceWeight(finding.EvidenceClass);
                var combinedWeight = Math.Clamp(sevWeight * confWeight, 0.0, 1.0);

                survivalProduct *= 1.0 - combinedWeight;

                contributions.Add(new FindingSeverityContribution(
                    RuleId: finding.RuleId,
                    FindingCode: finding.FindingCode,
                    Severity: finding.Severity.ToContractCode(),
                    EvidenceClass: finding.EvidenceClass.ToContractCode(),
                    SeverityWeight: sevWeight,
                    ConfidenceWeight: confWeight,
                    CombinedWeight: combinedWeight));
            }

            var severityComponent = Math.Clamp(1.0 - survivalProduct, 0.0, 1.0);

            // Paso 4 (Ecuaciones 4.4 y 4.5):
            // P(q) = 100 * I(q) * (alpha + (1 - alpha) * S(q))
            // H(q) = 100 - P(q)
            var alpha = effectiveParameters.BasePriorityAlpha;
            var priority = Math.Clamp(
                100.0 * impactComponent * (alpha + ((1.0 - alpha) * severityComponent)),
                0.0,
                100.0);

            var score = Math.Clamp(100.0 - priority, 0.0, 100.0);
            var band = effectiveParameters.ResolveBand(score);
            var contributingFindingsJson = HealthScoreJson.SerializeContributingFindings(contributions);

            results.Add(new QueryHealthScoreResult(
                QueryId: query.QueryId,
                QueryStoreQueryId: query.QueryStoreQueryId,
                Score: score,
                Priority: priority,
                Band: band,
                BandCode: band.ToContractCode(),
                ImpactComponent: impactComponent,
                SeverityComponent: severityComponent,
                AlphaUsed: alpha,
                CpuShare: share.CpuShare,
                DurationShare: share.DurationShare,
                LogicalReadsShare: share.LogicalReadsShare,
                WeightedConsumptionPct: share.WeightedConsumptionPct,
                TotalExecutions: share.TotalExecutions,
                TotalCpuMicroseconds: share.TotalCpuMicroseconds,
                TotalDurationMicroseconds: share.TotalDurationMicroseconds,
                TotalLogicalReads: share.TotalLogicalReads,
                FindingsCount: contributions.Count,
                ContributingFindings: contributions,
                ContributingFindingsJson: contributingFindingsJson,
                ParametersUsed: effectiveParameters,
                ParametersJson: parametersJson,
                ParametersHash: parametersHash,
                ScaleDirection: HealthScoreParameters.ScaleDirection,
                UsageDisclaimer: HealthScoreParameters.UsageDisclaimer));
        }

        return orderBy switch
        {
            HealthScoreOrderBy.ConsumptionDescending => results
                .OrderByDescending(r => r.WeightedConsumptionPct)
                .ThenBy(r => r.Score)
                .ThenBy(r => r.QueryStoreQueryId)
                .ThenBy(r => r.QueryId)
                .ToArray(),
            _ => results
                .OrderBy(r => r.Score)
                .ThenByDescending(r => r.WeightedConsumptionPct)
                .ThenBy(r => r.QueryStoreQueryId)
                .ThenBy(r => r.QueryId)
                .ToArray(),
        };
    }

    private static QueryFindingSample[] NormalizeFindings(IReadOnlyList<QueryFindingSample>? findings)
    {
        if (findings is null || findings.Count == 0)
        {
            return [];
        }

        // Si un mismo finding_code llega repetido en la entrada, se deduplica de forma determinista y se ordena por (RuleId, FindingCode).
        return findings
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.RuleId))
            .Select((f, idx) => (
                Index: idx,
                Finding: f with
                {
                    RuleId = f.RuleId.Trim().ToUpperInvariant(),
                    FindingCode = (f.FindingCode ?? string.Empty).Trim(),
                }))
            .GroupBy(
                item => string.IsNullOrEmpty(item.Finding.FindingCode)
                    ? $"{item.Finding.RuleId}|{item.Finding.Severity}|{item.Finding.EvidenceClass}|#{item.Index}"
                    : $"{item.Finding.RuleId}|{item.Finding.FindingCode}",
                StringComparer.Ordinal)
            .Select(g => g
                .Select(item => item.Finding)
                .OrderByDescending(f => f.Severity)
                .ThenByDescending(f => f.EvidenceClass)
                .First())
            .OrderBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.FindingCode, StringComparer.Ordinal)
            .ToArray();
    }
}
