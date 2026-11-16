using System.Globalization;
using System.Text.Json;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Masking;
using QueryDoctor.Application.Scoring;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Queries;

internal static class QueryFilterParser
{
    public static bool TryParseWindow(string? raw, out string normalizedCode, out TimeSpan duration)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? "24h" : raw.Trim().ToLowerInvariant();
        switch (value)
        {
            case "24h":
                normalizedCode = "24h";
                duration = TimeSpan.FromHours(24);
                return true;
            case "7d":
                normalizedCode = "7d";
                duration = TimeSpan.FromDays(7);
                return true;
            case "30d":
                normalizedCode = "30d";
                duration = TimeSpan.FromDays(30);
                return true;
            default:
                normalizedCode = value;
                duration = default;
                return false;
        }
    }

    public static bool TryParseSort(string? raw, out QueryListSort sort)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? "consumption" : raw.Trim().ToLowerInvariant();
        switch (value)
        {
            case "consumption":
                sort = QueryListSort.Consumption;
                return true;
            case "score":
                sort = QueryListSort.Score;
                return true;
            default:
                sort = default;
                return false;
        }
    }

    public static bool TryParseSeverity(string? raw, out FindingSeverity? severity)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            severity = null;
            return true;
        }

        switch (raw.Trim().ToUpperInvariant())
        {
            case "HIGH" or "ALTA":
                severity = FindingSeverity.Alta;
                return true;
            case "MEDIUM" or "MEDIA":
                severity = FindingSeverity.Media;
                return true;
            case "LOW" or "BAJA":
                severity = FindingSeverity.Baja;
                return true;
            case "INFO" or "INFORMATIVA":
                severity = FindingSeverity.Informativa;
                return true;
            default:
                severity = null;
                return false;
        }
    }
}

internal static class QueryMaskingProjection
{
    private const int SqlPreviewMaxLength = 180;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static (string? SqlText, string? SqlMaskingFailure, IReadOnlyDictionary<string, MaskedFinding> MaskedByCode, IReadOnlyDictionary<string, MaskingExclusion> ExclusionsByCode) MaskQueryContent(
        IMasker masker,
        IRuleCatalog catalog,
        long queryStoreQueryId,
        string? decryptedSqlText,
        IReadOnlyList<QueryFindingSource> findings)
    {
        var contracts = findings
            .Select(f => ToContract(f, catalog, queryStoreQueryId))
            .ToArray();

        var masked = masker.Mask(new MaskingInput(decryptedSqlText, PlanXml: null, Findings: contracts));

        string? sqlText = masked.Sql.IsSendable ? masked.Sql.Text : null;
        string? sqlFailure = decryptedSqlText is not null && !masked.Sql.IsSendable && masked.Sql.FailureCause is not null
            ? masked.Sql.FailureCause.Value.ToString()
            : null;

        var maskedByCode = masked.Findings.ToDictionary(f => f.FindingId, StringComparer.OrdinalIgnoreCase);
        var exclusionsByCode = masked.ExcludedFindings.ToDictionary(e => e.FindingId, StringComparer.OrdinalIgnoreCase);

        return (sqlText, sqlFailure, maskedByCode, exclusionsByCode);
    }

    public static FindingContract ToContract(QueryFindingSource source, IRuleCatalog catalog, long queryStoreQueryId)
    {
        var evidence = ParseStringNullableDictionary(source.DecryptedEvidenceJson);
        var nodes = ParseNodes(source.NodesJson);
        var context = ParseContext(source.ContextJson);
        var thresholds = ParseDoubleDictionary(source.ThresholdsUsedJson);

        return new FindingContract(
            SchemaVersion: source.SchemaVersion,
            FindingId: source.FindingCode,
            CatalogVersion: source.CatalogVersion,
            Rule: new FindingRuleContract(
                source.RuleId,
                catalog.TryGetDescriptor(source.RuleId)?.Name ?? source.RuleId,
                source.RuleVersion),
            Kind: source.Kind.ToContractCode(),
            Severity: source.Severity.ToContractCode(),
            EvidenceClass: source.EvidenceClass.ToContractCode(),
            QueryId: queryStoreQueryId,
            PlanId: source.QueryStorePlanId,
            Nodes: nodes,
            Evidence: evidence,
            Context: context,
            ThresholdsUsed: thresholds,
            EngineVersion: source.EngineVersion,
            AnalyzedAt: source.AnalyzedAt);
    }

    public static string BuildSqlPreview(string? maskedSql, string? sqlMaskingFailure, bool sqlExpired = false)
    {
        if (sqlExpired)
        {
            // S2-13 (RNF-SEG-10): purgado por retención o a pedido del DBA.
            return "[Evidencia expirada]";
        }

        if (string.IsNullOrWhiteSpace(maskedSql))
        {
            return sqlMaskingFailure is not null
                ? "[Texto SQL no disponible por enmascarado seguro]"
                : "[Sin texto SQL]";
        }

        var singleLine = string.Join(
            ' ',
            maskedSql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (singleLine.Length <= SqlPreviewMaxLength)
        {
            return singleLine;
        }

        return string.Concat(singleLine.AsSpan(0, SqlPreviewMaxLength - 1), "…");
    }

    public static string BuildFindingSummary(
        QueryFindingSource source,
        IRuleCatalog catalog,
        IReadOnlyDictionary<string, string> maskedEvidence,
        IReadOnlyList<FindingNodeContract> nodes)
    {
        var ruleName = catalog.TryGetDescriptor(source.RuleId)?.Name ?? source.RuleId;
        var opDesc = nodes.Count > 0 ? $" en {nodes[0].PhysicalOp}" : string.Empty;

        if (maskedEvidence.TryGetValue("expression", out var expr) && !string.IsNullOrWhiteSpace(expr))
        {
            return $"{ruleName}{opDesc}: {expr}";
        }

        if (maskedEvidence.TryGetValue("table", out var table) && !string.IsNullOrWhiteSpace(table))
        {
            return $"{ruleName}{opDesc} ({table})";
        }

        if (maskedEvidence.TryGetValue("cause", out var cause) && !string.IsNullOrWhiteSpace(cause))
        {
            return $"{ruleName}: {cause}";
        }

        return $"{ruleName}{opDesc}";
    }

    public static string ToBandCode(HealthScoreBand band) =>
        band switch
        {
            HealthScoreBand.Critica => "Critica",
            HealthScoreBand.Atencion => "Atencion",
            HealthScoreBand.Saludable => "Saludable",
            _ => band.ToString(),
        };

    public static string ToBandLabel(HealthScoreBand band) =>
        band switch
        {
            HealthScoreBand.Critica => "Crítica",
            HealthScoreBand.Atencion => "Atención",
            HealthScoreBand.Saludable => "Saludable",
            _ => band.ToString(),
        };

    public static IReadOnlyList<FindingNodeContract> ParseNodes(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<FindingNodeContract>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static FindingContextContract ParseContext(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new FindingContextContract();
        }

        try
        {
            return JsonSerializer.Deserialize<FindingContextContract>(json, JsonOptions) ?? new FindingContextContract();
        }
        catch (JsonException)
        {
            return new FindingContextContract();
        }
    }

    public static IReadOnlyDictionary<string, double> ParseDoubleDictionary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, double>>(json, JsonOptions)
                ?? new Dictionary<string, double>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, string?> ParseStringNullableDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string?>(StringComparer.Ordinal);
            }

            var result = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => prop.Value.GetRawText(),
                    _ => prop.Value.GetRawText(),
                };
            }

            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }
}

/// <summary>
/// Caso de uso para el listado de consultas (<c>GET /queries</c>; S1-16 / S2-09, RF-DASH-01, RF-SCO-04).
/// </summary>
public sealed class GetQueries
{
    private readonly IQuerySourceReader _reader;
    private readonly IMasker _masker;
    private readonly IRuleCatalog _catalog;

    public GetQueries(IQuerySourceReader reader, IMasker masker, IRuleCatalog catalog)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _masker = masker ?? throw new ArgumentNullException(nameof(masker));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<GetQueriesResult> ExecuteAsync(QueryListFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (!QueryFilterParser.TryParseWindow(filter.Window, out _, out var duration))
        {
            errors["window"] = ["Los valores admitidos son 24h, 7d o 30d."];
        }

        if (!QueryFilterParser.TryParseSort(filter.Sort, out var sort))
        {
            errors["sort"] = ["Los valores admitidos son consumption o score."];
        }

        if (!QueryFilterParser.TryParseSeverity(filter.Severity, out var severityFilter))
        {
            errors["severity"] = ["Los valores admitidos son High, Medium, Low o Info."];
        }

        if (errors.Count > 0)
        {
            return new GetQueriesResult.Invalid(errors);
        }

        var rows = await _reader.ListAsync(filter.ConnectionId, duration, cancellationToken);

        var filtered = severityFilter is null
            ? rows
            : rows.Where(r => r.Findings.Any(f => f.Severity == severityFilter.Value)).ToList();

        var ordered = sort == QueryListSort.Score
            ? filtered
                .OrderBy(r => r.Score is null ? 1 : 0)
                .ThenBy(r => r.Score ?? double.MaxValue)
                .ThenByDescending(r => r.ConsumptionPct)
                .ThenBy(r => r.QueryId)
            : filtered
                .OrderByDescending(r => r.ConsumptionPct)
                .ThenByDescending(r => r.Executions)
                .ThenBy(r => r.QueryId);

        var items = new List<QuerySummaryItem>(filtered.Count);
        foreach (var row in ordered)
        {
            var (maskedSql, sqlFailure, maskedByCode, _) = QueryMaskingProjection.MaskQueryContent(
                _masker,
                _catalog,
                row.QueryStoreQueryId,
                row.DecryptedSqlText,
                row.Findings);

            var findingPreviews = row.Findings
                .Select(f =>
                {
                    var nodes = QueryMaskingProjection.ParseNodes(f.NodesJson);
                    var evidence = maskedByCode.TryGetValue(f.FindingCode, out var mf)
                        ? mf.Evidence
                        : new Dictionary<string, string>(StringComparer.Ordinal);

                    var summary = QueryMaskingProjection.BuildFindingSummary(f, _catalog, evidence, nodes);
                    return new QueryFindingPreview(f.RuleId, f.Severity.ToString(), summary);
                })
                .ToArray();

            items.Add(new QuerySummaryItem(
                Id: row.QueryId,
                ConnectionId: row.ConnectionId,
                QueryStoreQueryId: row.QueryStoreQueryId,
                Server: row.Server,
                DatabaseName: row.DatabaseName,
                SqlPreview: QueryMaskingProjection.BuildSqlPreview(maskedSql, sqlFailure, row.SqlPurgedAt is not null),
                // S2-13: expirado solo si se purgó; un texto que nunca llegó no es "evidencia expirada".
                SqlExpired: row.SqlPurgedAt is not null,
                ConsumptionPct: Math.Round(row.ConsumptionPct, 2, MidpointRounding.AwayFromZero),
                Executions: row.Executions,
                Score: row.Score.HasValue ? Math.Round(row.Score.Value, 2, MidpointRounding.AwayFromZero) : null,
                Band: row.Band.HasValue ? QueryMaskingProjection.ToBandCode(row.Band.Value) : null,
                Findings: findingPreviews));
        }

        return new GetQueriesResult.Ok(items);
    }
}

/// <summary>
/// Caso de uso para el detalle de una consulta (<c>GET /queries/{id}</c>; S2-09, RF-DASH-02, RF-DASH-03, RF-SCO-06).
/// </summary>
public sealed class GetQueryDetail
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IQuerySourceReader _reader;
    private readonly IMasker _masker;
    private readonly IRuleCatalog _catalog;

    public GetQueryDetail(IQuerySourceReader reader, IMasker masker, IRuleCatalog catalog)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _masker = masker ?? throw new ArgumentNullException(nameof(masker));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<GetQueryDetailResult> ExecuteAsync(
        Guid queryId,
        string? window,
        CancellationToken cancellationToken = default)
    {
        if (!QueryFilterParser.TryParseWindow(window, out var windowCode, out var duration))
        {
            return new GetQueryDetailResult.Invalid(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["window"] = ["Los valores admitidos son 24h, 7d o 30d."],
            });
        }

        var source = await _reader.GetDetailAsync(queryId, duration, cancellationToken);
        if (source is null)
        {
            return new GetQueryDetailResult.NotFound();
        }

        var (maskedSql, sqlFailure, maskedByCode, exclusionsByCode) = QueryMaskingProjection.MaskQueryContent(
            _masker,
            _catalog,
            source.QueryStoreQueryId,
            source.DecryptedSqlText,
            source.Findings);

        var plansById = source.Plans.ToDictionary(p => p.Id);

        var plans = source.Plans
            .OrderBy(p => p.IngestedAt)
            .ThenBy(p => p.QueryStorePlanId)
            .Select(p => new QueryPlanSummary(
                PlanId: p.Id,
                QueryStorePlanId: p.QueryStorePlanId,
                QueryPlanHash: p.QueryPlanHash,
                EngineVersion: p.EngineVersion,
                AnalysisStatus: p.AnalysisStatus,
                NotAnalyzableCause: p.NotAnalyzableCause,
                IngestedAt: p.IngestedAt,
                PlanExpired: p.PurgedAt is not null,
                ExpiredAt: p.PurgedAt))
            .ToArray();

        // Agrupar estadísticas por intervalo y plan para no duplicar puntos si existen varios ExecutionType en el mismo intervalo.
        var series = source.RuntimeStats
            .GroupBy(s => new { s.PlanId, s.IntervalId, s.IntervalStart, s.IntervalEnd })
            .OrderBy(g => g.Key.IntervalStart)
            .ThenBy(g => g.Key.IntervalEnd)
            .ThenBy(g => plansById.TryGetValue(g.Key.PlanId, out var plan) ? plan.QueryStorePlanId : 0L)
            .Select(g =>
            {
                var plan = plansById.GetValueOrDefault(g.Key.PlanId);
                var totalExecutions = g.Sum(x => Math.Max(0L, x.ExecutionCount));

                double WeightedAvg(Func<Domain.Core.RuntimeStat, double> selector)
                {
                    if (totalExecutions <= 0)
                    {
                        return g.Average(selector);
                    }

                    var weightedSum = g.Sum(x => Math.Max(0L, x.ExecutionCount) * selector(x));
                    return weightedSum / totalExecutions;
                }

                return new QueryRuntimeSeriesPoint(
                    IntervalId: g.Key.IntervalId,
                    IntervalStart: g.Key.IntervalStart,
                    IntervalEnd: g.Key.IntervalEnd,
                    PlanId: g.Key.PlanId,
                    QueryStorePlanId: plan?.QueryStorePlanId ?? 0,
                    QueryPlanHash: plan?.QueryPlanHash,
                    ExecutionCount: totalExecutions,
                    AvgDurationMs: Math.Round(WeightedAvg(x => x.AvgDurationMicroseconds) / 1000.0, 3, MidpointRounding.AwayFromZero),
                    AvgCpuTimeMs: Math.Round(WeightedAvg(x => x.AvgCpuTimeMicroseconds) / 1000.0, 3, MidpointRounding.AwayFromZero),
                    AvgLogicalIoReads: Math.Round(WeightedAvg(x => x.AvgLogicalIoReads), 2, MidpointRounding.AwayFromZero),
                    AvgPhysicalIoReads: Math.Round(WeightedAvg(x => x.AvgPhysicalIoReads), 2, MidpointRounding.AwayFromZero),
                    AvgRowCount: Math.Round(WeightedAvg(x => x.AvgRowCount), 2, MidpointRounding.AwayFromZero));
            })
            .ToArray();

        var findings = source.Findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.FindingCode, StringComparer.Ordinal)
            .Select(f =>
            {
                var hasMasked = maskedByCode.TryGetValue(f.FindingCode, out var maskedFinding);
                var hasExclusion = exclusionsByCode.TryGetValue(f.FindingCode, out var exclusion);

                IReadOnlyDictionary<string, string> evidence = hasMasked && maskedFinding is not null
                    ? maskedFinding.Evidence
                    : new Dictionary<string, string>(StringComparer.Ordinal);

                return new QueryFindingDetail(
                    Id: f.Id,
                    PlanId: f.PlanId,
                    QueryStorePlanId: f.QueryStorePlanId,
                    FindingCode: f.FindingCode,
                    RuleId: f.RuleId,
                    RuleName: _catalog.TryGetDescriptor(f.RuleId)?.Name ?? f.RuleId,
                    RuleVersion: f.RuleVersion,
                    CatalogVersion: f.CatalogVersion,
                    Kind: f.Kind.ToContractCode(),
                    Severity: f.Severity.ToContractCode(),
                    EvidenceClass: f.EvidenceClass.ToContractCode(),
                    Nodes: QueryMaskingProjection.ParseNodes(f.NodesJson),
                    Evidence: evidence,
                    EvidenceMasked: hasMasked && !hasExclusion,
                    EvidenceMaskingFailure: hasExclusion ? exclusion!.Cause.ToString() : null,
                    EvidenceExpired: f.EvidenceExpired,
                    Context: QueryMaskingProjection.ParseContext(f.ContextJson),
                    ThresholdsUsed: QueryMaskingProjection.ParseDoubleDictionary(f.ThresholdsUsedJson),
                    EngineVersion: f.EngineVersion,
                    AnalyzedAt: f.AnalyzedAt);
            })
            .ToArray();

        var score = BuildScoreDetail(source.LatestScore, source.RankInWindow, source.TotalQueriesInWindow, source.ScoreHistory);

        return new GetQueryDetailResult.Found(new QueryDetailView(
            Id: source.QueryId,
            ConnectionId: source.ConnectionId,
            QueryStoreQueryId: source.QueryStoreQueryId,
            Server: source.Server,
            DatabaseName: source.DatabaseName,
            Platform: source.Platform,
            QueryHash: source.QueryHash,
            OriginContext: source.OriginContext,
            Window: windowCode,
            WindowStart: source.WindowStart,
            WindowEnd: source.WindowEnd,
            SqlText: maskedSql,
            SqlExpired: source.SqlPurgedAt is not null,
            SqlExpiredAt: source.SqlPurgedAt,
            SqlMaskingFailure: sqlFailure,
            Score: score,
            Plans: plans,
            Series: series,
            Findings: findings));
    }

    private static QueryHealthScoreDetail? BuildScoreDetail(
        HealthScore? latest,
        int rankInWindow,
        int totalQueriesInWindow,
        IReadOnlyList<HealthScore> scoreHistory)
    {
        if (latest is null)
        {
            return null;
        }

        IReadOnlyList<FindingSeverityContribution> contributions;
        try
        {
            contributions = HealthScoreJson.DeserializeContributingFindings(latest.ContributingFindingsJson);
        }
        catch (JsonException)
        {
            contributions = [];
        }

        HealthScoreParameters parameters;
        try
        {
            parameters = string.IsNullOrWhiteSpace(latest.ParametersJson)
                ? HealthScoreParameters.Default
                : JsonSerializer.Deserialize<HealthScoreParameters>(latest.ParametersJson, JsonOptions)
                    ?? HealthScoreParameters.Default;
        }
        catch (JsonException)
        {
            parameters = HealthScoreParameters.Default;
        }

        var history = scoreHistory
            .OrderBy(h => h.CalculatedAt)
            .ThenBy(h => h.Id)
            .Select(h => new QueryScoreHistoryItem(
                CalculatedAt: h.CalculatedAt,
                Score: Math.Round(h.Score, 2, MidpointRounding.AwayFromZero),
                Priority: Math.Round(h.Priority, 2, MidpointRounding.AwayFromZero),
                Band: QueryMaskingProjection.ToBandCode(h.Band),
                ImpactComponent: Math.Round(h.ImpactComponent, 4, MidpointRounding.AwayFromZero),
                SeverityComponent: Math.Round(h.SeverityComponent, 4, MidpointRounding.AwayFromZero),
                WeightedConsumptionPct: Math.Round(h.WeightedConsumptionPct, 2, MidpointRounding.AwayFromZero)))
            .ToArray();

        return new QueryHealthScoreDetail(
            Score: Math.Round(latest.Score, 2, MidpointRounding.AwayFromZero),
            Priority: Math.Round(latest.Priority, 2, MidpointRounding.AwayFromZero),
            Band: QueryMaskingProjection.ToBandCode(latest.Band),
            BandLabel: QueryMaskingProjection.ToBandLabel(latest.Band),
            RankInWindow: rankInWindow > 0 ? rankInWindow : 1,
            TotalQueriesInWindow: totalQueriesInWindow > 0 ? totalQueriesInWindow : 1,
            ScaleDirection: HealthScoreParameters.ScaleDirection,
            UsageDisclaimer: HealthScoreParameters.UsageDisclaimer,
            ImpactComponent: Math.Round(latest.ImpactComponent, 4, MidpointRounding.AwayFromZero),
            SeverityComponent: Math.Round(latest.SeverityComponent, 4, MidpointRounding.AwayFromZero),
            AlphaUsed: latest.AlphaUsed,
            CpuShare: Math.Round(latest.CpuShare, 4, MidpointRounding.AwayFromZero),
            DurationShare: Math.Round(latest.DurationShare, 4, MidpointRounding.AwayFromZero),
            LogicalReadsShare: Math.Round(latest.LogicalReadsShare, 4, MidpointRounding.AwayFromZero),
            WeightedConsumptionPct: Math.Round(latest.WeightedConsumptionPct, 2, MidpointRounding.AwayFromZero),
            TotalExecutions: latest.TotalExecutions,
            TotalCpuMicroseconds: latest.TotalCpuMicroseconds,
            TotalDurationMicroseconds: latest.TotalDurationMicroseconds,
            TotalLogicalReads: latest.TotalLogicalReads,
            FindingsCount: latest.FindingsCount,
            ContributingFindings: contributions,
            ParametersUsed: parameters,
            CalculatedAt: latest.CalculatedAt,
            History: history);
    }
}
