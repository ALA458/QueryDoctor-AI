using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Engine.Rules;

/// <summary>
/// Implementación pura y determinista del motor de reglas sobre Showplan XML (S1-13, RF-MOT-02, RF-MOT-03, RF-MOT-14, RF-MOT-15, RF-MOT-16, RF-MOT-17).
/// Mismo plan + misma versión del catálogo + mismos umbrales = mismos hallazgos y mismos <c>finding_id</c>.
/// </summary>
public sealed class DeterministicRuleEngine : IRuleEngine
{
    private static readonly DateTimeOffset DefaultDeterministicTimestamp =
        new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlySet<EvidenceClass> DefaultAvailableEvidenceClasses =
        new HashSet<EvidenceClass>
        {
            EvidenceClass.Estimada,
            EvidenceClass.EjecucionAgregada,
            EvidenceClass.NoAplica,
        };

    private readonly IPlanParser _planParser;
    private readonly IRuleCatalog _ruleCatalog;
    private readonly RuleThresholds _defaultThresholds;

    public DeterministicRuleEngine(
        IPlanParser planParser,
        IRuleCatalog ruleCatalog,
        RuleThresholds? defaultThresholds = null)
    {
        ArgumentNullException.ThrowIfNull(planParser);
        ArgumentNullException.ThrowIfNull(ruleCatalog);

        _planParser = planParser;
        _ruleCatalog = ruleCatalog;
        _defaultThresholds = defaultThresholds ?? RuleThresholds.Default;
    }

    public PlanAnalysisResult Analyze(PlanAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var planKey = ResolvePlanKey(request);
        var parseResult = _planParser.Parse(
            request.ShowplanXml,
            planKey,
            request.FallbackEngineVersion);

        return AnalyzeParsed(parseResult, request);
    }

    public PlanAnalysisResult AnalyzeParsed(
        PlanParseResult parseResult,
        PlanAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(request);

        var planKey = ResolvePlanKey(request, parseResult.PlanIdentifier);
        var thresholds = request.Thresholds ?? _defaultThresholds;
        var thresholdsJson = thresholds.ToCanonicalJson();
        var thresholdHash = thresholds.ComputeConfigurationHash();
        var analyzedAt = request.AnalyzedAt ?? DefaultDeterministicTimestamp;

        if (!parseResult.IsSuccess || parseResult.Plan is null)
        {
            var reg11Finding = BuildUnanalyzablePlanFinding(
                parseResult.Finding,
                planKey,
                request,
                analyzedAt);

            return new PlanAnalysisResult(
                PlanKey: planKey,
                AnalysisStatus: PlanAnalysisStatus.NotAnalyzable,
                CatalogVersion: _ruleCatalog.CatalogVersion,
                EngineVersion: request.FallbackEngineVersion,
                ThresholdsUsed: thresholds,
                ThresholdsUsedJson: thresholdsJson,
                ThresholdConfigurationHash: thresholdHash,
                Findings: [reg11Finding],
                OmittedRuleIds: [],
                NotAnalyzableCause: parseResult.Finding?.Message ?? "Plan Showplan XML no analizable.");
        }

        var parsedPlan = parseResult.Plan;
        var engineVersion = parsedPlan.EngineVersion ?? request.FallbackEngineVersion;
        var availableEvidence = request.AvailableEvidenceClasses ?? DefaultAvailableEvidenceClasses;

        var evalContext = new RuleEvaluationContext(
            Plan: parsedPlan,
            Thresholds: thresholds,
            QueryStoreQueryId: request.QueryStoreQueryId,
            QueryStorePlanId: request.QueryStorePlanId,
            PlanIdentifier: planKey,
            QueryExecutionCount: request.QueryExecutionCount,
            QueryConsumptionPct: request.QueryConsumptionPct,
            AvgRowsReturned: request.AvgRowsReturned);

        var findings = new List<FindingContract>();
        var omittedRules = new List<string>();

        foreach (var rule in _ruleCatalog.GetActiveRules())
        {
            var descriptor = rule.Descriptor;
            if (descriptor.RequiredEvidenceClass != EvidenceClass.NoAplica &&
                !availableEvidence.Contains(descriptor.RequiredEvidenceClass))
            {
                omittedRules.Add(descriptor.RuleId);
                continue;
            }

            var drafts = rule.Evaluate(evalContext);
            if (drafts is null || drafts.Count == 0)
            {
                continue;
            }

            var ruleThresholds = thresholds.GetThresholdsForRule(descriptor.RuleId);

            foreach (var draft in drafts)
            {
                var sortedNodes = (draft.Nodes ?? [])
                    .OrderBy(n => n.NodeId)
                    .ThenBy(n => n.PhysicalOp, StringComparer.Ordinal)
                    .ToArray();

                var sortedEvidence = new SortedDictionary<string, string?>(StringComparer.Ordinal);
                if (draft.Evidence is not null)
                {
                    foreach (var pair in draft.Evidence)
                    {
                        sortedEvidence[pair.Key] = pair.Value;
                    }
                }

                var discriminator = ResolveDiscriminator(draft, sortedEvidence);
                var findingId = ComputeDeterministicFindingId(
                    planKey,
                    descriptor.RuleId,
                    descriptor.Version,
                    sortedNodes,
                    discriminator);

                var contextContract = draft.Context ?? new FindingContextContract(
                    Executions: request.QueryExecutionCount,
                    QueryConsumptionPct: request.QueryConsumptionPct);

                findings.Add(new FindingContract(
                    SchemaVersion: FindingContract.CurrentSchemaVersion,
                    FindingId: findingId,
                    CatalogVersion: _ruleCatalog.CatalogVersion,
                    Rule: new FindingRuleContract(descriptor.RuleId, descriptor.Name, descriptor.Version),
                    Kind: draft.Kind.ToContractCode(),
                    Severity: draft.Severity.ToContractCode(),
                    EvidenceClass: descriptor.RequiredEvidenceClass.ToContractCode(),
                    QueryId: request.QueryStoreQueryId,
                    PlanId: request.QueryStorePlanId,
                    Nodes: sortedNodes,
                    Evidence: sortedEvidence,
                    Context: contextContract,
                    ThresholdsUsed: ruleThresholds,
                    EngineVersion: engineVersion,
                    AnalyzedAt: analyzedAt));
            }
        }

        // Orden canónico: por Rule.Id, primer NodeId involucrado (-1 si es de sentencia) y FindingId.
        var orderedFindings = findings
            .OrderBy(f => f.Rule.Id, StringComparer.Ordinal)
            .ThenBy(f => f.Nodes.Count > 0 ? f.Nodes[0].NodeId : -1)
            .ThenBy(f => f.FindingId, StringComparer.Ordinal)
            .ToArray();

        var orderedOmitted = omittedRules
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new PlanAnalysisResult(
            PlanKey: planKey,
            AnalysisStatus: PlanAnalysisStatus.Analyzable,
            CatalogVersion: _ruleCatalog.CatalogVersion,
            EngineVersion: engineVersion,
            ThresholdsUsed: thresholds,
            ThresholdsUsedJson: thresholdsJson,
            ThresholdConfigurationHash: thresholdHash,
            Findings: orderedFindings,
            OmittedRuleIds: orderedOmitted);
    }

    public IReadOnlyList<PlanAnalysisResult> AnalyzeBatch(IEnumerable<PlanAnalysisRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var results = new List<PlanAnalysisResult>();
        foreach (var request in requests)
        {
            results.Add(Analyze(request));
        }

        return results;
    }

    /// <summary>
    /// Calcula el identificador determinista de un hallazgo (<c>finding_id</c>) a partir del hash SHA-256 de (plan, regla, versión, nodos, discriminador) (Arquitectura §5.1, RF-MOT-02).
    /// </summary>
    public static string ComputeDeterministicFindingId(
        string planKey,
        string ruleId,
        string ruleVersion,
        IReadOnlyList<FindingNodeContract> sortedNodes,
        string? discriminator = null)
    {
        ArgumentNullException.ThrowIfNull(planKey);
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentNullException.ThrowIfNull(ruleVersion);
        ArgumentNullException.ThrowIfNull(sortedNodes);

        var nodePart = sortedNodes.Count == 0
            ? "stmt"
            : string.Join(",", sortedNodes.Select(n => n.NodeId.ToString(CultureInfo.InvariantCulture)));

        var canonicalInput = string.Create(
            CultureInfo.InvariantCulture,
            $"{planKey.Trim()}|{ruleId.Trim().ToUpperInvariant()}|{ruleVersion.Trim()}|{nodePart}|{discriminator ?? string.Empty}");

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalInput));
        var hex = Convert.ToHexString(hashBytes);
        return $"F-{hex[..12]}";
    }

    private static string ResolvePlanKey(PlanAnalysisRequest request, string? fallbackIdentifier = null)
    {
        if (!string.IsNullOrWhiteSpace(request.PlanIdentifier))
        {
            return request.PlanIdentifier.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackIdentifier))
        {
            return fallbackIdentifier.Trim();
        }

        return request.QueryStorePlanId.ToString(CultureInfo.InvariantCulture);
    }

    private static string ResolveDiscriminator(
        RuleFindingDraft draft,
        SortedDictionary<string, string?> sortedEvidence)
    {
        if (!string.IsNullOrWhiteSpace(draft.Discriminator))
        {
            return draft.Discriminator.Trim();
        }

        if (sortedEvidence.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            ";",
            sortedEvidence.Select(kvp => $"{kvp.Key}={kvp.Value ?? string.Empty}"));
    }

    private FindingContract BuildUnanalyzablePlanFinding(
        UnanalyzablePlanFinding? parserFinding,
        string planKey,
        PlanAnalysisRequest request,
        DateTimeOffset analyzedAt)
    {
        var descriptor = _ruleCatalog.TryGetDescriptor(PlanParserOptions.UnanalyzablePlanRuleId)
            ?? VersionedRuleCatalog.BuiltInDescriptors.First(d => d.RuleId == PlanParserOptions.UnanalyzablePlanRuleId);

        var reason = parserFinding?.Reason.ToString() ?? PlanParseFailureReason.EmptyOrNullXml.ToString();
        var message = parserFinding?.Message ?? "El documento Showplan XML es nulo o inválido.";

        var evidence = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["cause"] = message,
            ["failure_reason"] = reason,
        };

        var findingId = ComputeDeterministicFindingId(
            planKey,
            descriptor.RuleId,
            descriptor.Version,
            [],
            reason);

        return new FindingContract(
            SchemaVersion: FindingContract.CurrentSchemaVersion,
            FindingId: findingId,
            CatalogVersion: _ruleCatalog.CatalogVersion,
            Rule: new FindingRuleContract(descriptor.RuleId, descriptor.Name, descriptor.Version),
            Kind: FindingKind.PlanNoAnalizable.ToContractCode(),
            Severity: FindingSeverity.Informativa.ToContractCode(),
            EvidenceClass: EvidenceClass.NoAplica.ToContractCode(),
            QueryId: request.QueryStoreQueryId,
            PlanId: request.QueryStorePlanId,
            Nodes: [],
            Evidence: evidence,
            Context: new FindingContextContract(
                Executions: request.QueryExecutionCount,
                QueryConsumptionPct: request.QueryConsumptionPct),
            ThresholdsUsed: new SortedDictionary<string, double>(StringComparer.Ordinal),
            EngineVersion: request.FallbackEngineVersion,
            AnalyzedAt: analyzedAt);
    }
}
