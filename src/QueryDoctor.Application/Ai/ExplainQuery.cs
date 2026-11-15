using System.Text.Json;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Explanations;
using QueryDoctor.Application.KnowledgeBase;
using QueryDoctor.Application.Masking;
using QueryDoctor.Application.Validation;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Ai;

/// <summary>
/// Resultado de procesar una explicación, para el registro del trabajo. Solo códigos y conteos (RNF-SEG-09).
/// </summary>
public sealed record ExplainQueryOutcome(
    Guid ExplanationId,
    ExplanationState State,
    ExplanationCause? Cause,
    string? ValidationStatus,
    bool FromCache,
    int FindingsSent,
    MaskingMetrics? Masking);

/// <summary>
/// Trabajo <c>explain</c> del Worker (S2-05, CU-05): arma el contexto enmascarado, reutiliza la caché por hash
/// (RF-IA-07), llama al servicio de IA y valida la salida en .NET (RF-IA-03, invariante 6). Si la IA falla, la
/// explicación queda <see cref="ExplanationState.Unavailable"/> con su causa y los hallazgos siguen visibles
/// (RF-IA-06). Sin hallazgos enviables no se llama a la IA (RF-IA-01).
/// </summary>
public sealed class ExplainQuery
{
    private static readonly JsonSerializerOptions ContractJson = new();

    private readonly IExplanationStore _store;
    private readonly IExplanationSourceReader _sources;
    private readonly IFieldEncryptor _encryptor;
    private readonly ExplanationContextBuilder _contextBuilder;
    private readonly IKnowledgeBaseRepository _knowledgeBase;
    private readonly IAiExplanationClient _client;
    private readonly IExplanationValidator _validator;
    private readonly IExplanationValidationContextBuilder _validationContextBuilder;
    private readonly IRuleCatalog _catalog;
    private readonly TimeProvider _clock;

    public ExplainQuery(
        IExplanationStore store,
        IExplanationSourceReader sources,
        IFieldEncryptor encryptor,
        ExplanationContextBuilder contextBuilder,
        IKnowledgeBaseRepository knowledgeBase,
        IAiExplanationClient client,
        IExplanationValidator validator,
        IExplanationValidationContextBuilder validationContextBuilder,
        IRuleCatalog catalog,
        TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _knowledgeBase = knowledgeBase ?? throw new ArgumentNullException(nameof(knowledgeBase));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _validationContextBuilder = validationContextBuilder ?? throw new ArgumentNullException(nameof(validationContextBuilder));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ExplainQueryOutcome?> ExecuteAsync(Guid explanationId, CancellationToken cancellationToken)
    {
        var explanation = await _store.GetAsync(explanationId, cancellationToken);
        if (explanation is null)
        {
            return null;
        }

        if (explanation.State != ExplanationState.Pending)
        {
            // Idempotente: un trabajo repetido no vuelve a llamar a la IA.
            return Outcome(explanation, fromCache: false, findingsSent: 0, masking: null);
        }

        explanation.State = ExplanationState.Running;
        explanation.StartedAt = _clock.GetUtcNow();
        await _store.SaveAsync(explanation, cancellationToken);

        try
        {
            return await ProcessAsync(explanation, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Se deja constancia con causa tipada y se relanza para que el trabajo quede fallido en Hangfire.
            await FinishAsync(explanation, ExplanationState.Unavailable, ExplanationCause.Unexpected, CancellationToken.None);
            throw;
        }
    }

    private async Task<ExplainQueryOutcome> ProcessAsync(Explanation explanation, CancellationToken cancellationToken)
    {
        var encrypted = await _sources.LoadAsync(explanation.QueryId, cancellationToken);
        if (encrypted is null || encrypted.Findings.Count == 0)
        {
            await FinishAsync(explanation, ExplanationState.NotGenerated, ExplanationCause.NoFindings, cancellationToken);
            return Outcome(explanation, fromCache: false, findingsSent: 0, masking: null);
        }

        var source = Decrypt(encrypted);
        var kb = await LoadFragmentsAsync(source.Findings, cancellationToken);
        var context = _contextBuilder.Build(source, kb, explanation.CorrelationId);
        var request = context.Request;
        if (request is null)
        {
            await FinishAsync(explanation, ExplanationState.NotGenerated, ExplanationCause.NoSendableFindings, cancellationToken);
            return Outcome(explanation, fromCache: false, findingsSent: 0, context.Masked.Metrics);
        }

        explanation.ContextHash = request.ComputeContextHash();
        explanation.CatalogVersion = request.CatalogVersion;
        explanation.FindingCodesJson = JsonSerializer.Serialize(request.Findings.Select(f => f.FindingId).ToArray(), ContractJson);

        var cached = await _store.FindTracedByContextHashAsync(explanation.ContextHash, cancellationToken);
        if (cached is not null && cached.Id != explanation.Id)
        {
            explanation.EncryptedBody = cached.EncryptedBody;
            explanation.ValidationStatus = cached.ValidationStatus;
            explanation.LlmProfile = cached.LlmProfile;
            explanation.Model = cached.Model;
            explanation.PromptVersion = cached.PromptVersion;
            explanation.ServedBy = cached.ServedBy;
            explanation.DataPolicy = cached.DataPolicy;
            explanation.ReusedFromId = cached.Id;
            await FinishAsync(explanation, ExplanationState.Completed, cause: null, cancellationToken);
            return Outcome(explanation, fromCache: true, request.Findings.Count, context.Masked.Metrics);
        }

        string response;
        try
        {
            response = await _client.SendAsync(request, cancellationToken);
        }
        catch (AiServiceUnavailableException ex)
        {
            await FinishAsync(explanation, ExplanationState.Unavailable, ex.Cause, cancellationToken);
            return Outcome(explanation, fromCache: false, request.Findings.Count, context.Masked.Metrics);
        }

        var validationContext = await _validationContextBuilder.BuildAsync(
            request.QueryId.ToString("D"),
            request.Findings,
            context.QueryMetrics,
            source.EngineVersion,
            cancellationToken);

        var result = _validator.ValidateJson(response, validationContext);
        if (result.Failures.Any(f => f.Code == ValidationFailureCode.InvalidJson))
        {
            // E5: una respuesta que no se puede leer no es una explicación NO_VERIFICADA, es una IA no disponible.
            await FinishAsync(explanation, ExplanationState.Unavailable, ExplanationCause.InvalidOutput, cancellationToken);
            return Outcome(explanation, fromCache: false, request.Findings.Count, context.Masked.Metrics);
        }

        explanation.ValidationStatus = result.StatusCode;
        explanation.LlmProfile = result.Metadata?.LlmProfile;
        explanation.Model = result.Metadata?.Model;
        explanation.PromptVersion = result.Metadata?.PromptVersion;
        explanation.ServedBy = result.Metadata?.ServedBy;
        explanation.DataPolicy = result.Metadata?.DataPolicy;
        explanation.EncryptedBody = _encryptor.Encrypt(ToBody(result).ToJson());
        await FinishAsync(explanation, ExplanationState.Completed, cause: null, cancellationToken);
        return Outcome(explanation, fromCache: false, request.Findings.Count, context.Masked.Metrics);
    }

    private ExplanationSource Decrypt(EncryptedExplanationSource encrypted) => new(
        encrypted.QueryId,
        encrypted.EncryptedSqlText is null ? null : _encryptor.Decrypt(encrypted.EncryptedSqlText),
        encrypted.EncryptedPlanXml is null ? null : _encryptor.Decrypt(encrypted.EncryptedPlanXml),
        encrypted.Findings.Select(f => ToContract(f, encrypted)).ToArray(),
        encrypted.LatestScore,
        encrypted.EngineVersion);

    private FindingContract ToContract(Finding finding, EncryptedExplanationSource source)
    {
        var evidence = string.IsNullOrEmpty(finding.EncryptedEvidenceJson)
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : JsonSerializer.Deserialize<Dictionary<string, string?>>(_encryptor.Decrypt(finding.EncryptedEvidenceJson), ContractJson)
                ?? new Dictionary<string, string?>(StringComparer.Ordinal);

        return new FindingContract(
            SchemaVersion: finding.SchemaVersion,
            FindingId: finding.FindingCode,
            CatalogVersion: finding.CatalogVersion,
            Rule: new FindingRuleContract(
                finding.RuleId,
                _catalog.TryGetDescriptor(finding.RuleId)?.Name ?? finding.RuleId,
                finding.RuleVersion),
            Kind: finding.Kind.ToContractCode(),
            Severity: finding.Severity.ToContractCode(),
            EvidenceClass: finding.EvidenceClass.ToContractCode(),
            QueryId: source.QueryStoreQueryId,
            PlanId: source.QueryStorePlanId,
            Nodes: JsonSerializer.Deserialize<List<FindingNodeContract>>(finding.NodesJson, ContractJson) ?? [],
            Evidence: evidence,
            Context: JsonSerializer.Deserialize<FindingContextContract>(finding.ContextJson, ContractJson) ?? new FindingContextContract(),
            ThresholdsUsed: JsonSerializer.Deserialize<Dictionary<string, double>>(finding.ThresholdsUsedJson, ContractJson)
                ?? new Dictionary<string, double>(StringComparer.Ordinal),
            EngineVersion: finding.EngineVersion,
            AnalyzedAt: finding.AnalyzedAt);
    }

    private async Task<IReadOnlyList<KbFragment>> LoadFragmentsAsync(IReadOnlyList<FindingContract> findings, CancellationToken cancellationToken)
    {
        var fragments = new List<KbFragment>();
        foreach (var ruleId in findings.Select(f => f.Rule.Id).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            fragments.AddRange(await _knowledgeBase.GetFragmentsByRuleAsync(ruleId, cancellationToken));
        }

        return fragments;
    }

    private static ExplanationBody ToBody(ValidatedExplanationResult result) => new(
        result.StatusCode,
        result.PersistentNotice,
        result.Summary,
        result.Claims
            .Select(c => new ExplanationBodyClaim(c.ClaimTypeCode, c.Statement, c.FindingIds, c.KbFragmentId, c.IndicationLabel, c.Passed))
            .ToArray(),
        result.Failures
            .Select(f => new ExplanationBodyFailure(f.Code.ToString(), f.ClaimIndex, f.Criterion))
            .ToArray());

    private async Task FinishAsync(
        Explanation explanation,
        ExplanationState state,
        ExplanationCause? cause,
        CancellationToken cancellationToken)
    {
        explanation.State = state;
        explanation.Cause = cause;
        explanation.CompletedAt = _clock.GetUtcNow();
        await _store.SaveAsync(explanation, cancellationToken);
    }

    private static ExplainQueryOutcome Outcome(Explanation explanation, bool fromCache, int findingsSent, MaskingMetrics? masking) => new(
        explanation.Id,
        explanation.State,
        explanation.Cause,
        explanation.ValidationStatus,
        fromCache,
        findingsSent,
        masking);
}
