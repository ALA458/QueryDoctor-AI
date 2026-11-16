using System.Text.Json;
using QueryDoctor.Application.Ai;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Explanations;
using QueryDoctor.Application.Masking;
using QueryDoctor.Application.Validation;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.UnitTests.Connections;
using QueryDoctor.UnitTests.KnowledgeBase;
using QueryDoctor.UnitTests.Masking;

namespace QueryDoctor.UnitTests.Ai;

/// <summary>Almacén en memoria de explicaciones y material de consultas (S2-05).</summary>
internal sealed class InMemoryExplanationStore : IExplanationStore, IExplanationSourceReader
{
    private readonly Dictionary<Guid, EncryptedExplanationSource> _sources = [];

    public List<Explanation> Explanations { get; } = [];

    public List<ExplanationClaim> Claims { get; } = [];

    /// <summary>Entradas de traza escritas con cada estado final (S2-06).</summary>
    public List<QueryDoctor.Domain.Audit.TraceEntry> Traces { get; } = [];

    public void AddSource(EncryptedExplanationSource source) => _sources[source.QueryId] = source;

    public Task<ExplanationTarget?> FindTargetAsync(Guid queryId, CancellationToken cancellationToken) =>
        Task.FromResult(_sources.TryGetValue(queryId, out var source)
            ? new ExplanationTarget(queryId, Guid.Empty, source.Findings.Count)
            : null);

    public Task<IReadOnlyList<ExplanationFindingSummary>> GetLatestFindingsAsync(Guid queryId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExplanationFindingSummary>>(_sources.TryGetValue(queryId, out var source)
            ? source.Findings.Select(f => new ExplanationFindingSummary(f.FindingCode, f.RuleId, f.Severity, f.EvidenceClass)).ToArray()
            : []);

    public Task<Explanation?> GetLatestAsync(Guid queryId, CancellationToken cancellationToken) =>
        Task.FromResult(Explanations.Where(e => e.QueryId == queryId).OrderByDescending(e => e.RequestedAt).FirstOrDefault());

    public Task<Explanation?> GetAsync(Guid explanationId, CancellationToken cancellationToken) =>
        Task.FromResult(Explanations.FirstOrDefault(e => e.Id == explanationId));

    public Task<Explanation?> FindTracedByContextHashAsync(string contextHash, CancellationToken cancellationToken) =>
        Task.FromResult(Explanations
            .Where(e => e.ContextHash == contextHash
                && e.State == ExplanationState.Completed
                && e.ValidationStatus == ExplanationValidationConstants.StatusTrazadaCode
                && e.EncryptedBody is not null)
            .OrderByDescending(e => e.CompletedAt)
            .FirstOrDefault());

    public Task AddAsync(Explanation explanation, CancellationToken cancellationToken)
    {
        Explanations.Add(explanation);
        return Task.CompletedTask;
    }

    public Task SaveAsync(Explanation explanation, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CompleteAsync(Explanation explanation, IReadOnlyList<ExplanationClaim> claims, CancellationToken cancellationToken)
    {
        Claims.AddRange(claims);
        Traces.Add(Application.Traceability.TraceEntries.FromExplanation(explanation));
        return Task.CompletedTask;
    }

    public Task FinishAsync(Explanation explanation, CancellationToken cancellationToken)
    {
        Traces.Add(Application.Traceability.TraceEntries.FromExplanation(explanation));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExplanationClaim>> GetClaimsAsync(Guid explanationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExplanationClaim>>(Claims.Where(c => c.ExplanationId == explanationId).OrderBy(c => c.ClaimIndex).ToArray());

    public Task<EncryptedExplanationSource?> LoadAsync(Guid queryId, CancellationToken cancellationToken) =>
        Task.FromResult(_sources.TryGetValue(queryId, out var source) ? source : null);
}

/// <summary>Doble del cliente de IA: registra el cuerpo y responde lo programado.</summary>
internal sealed class ScriptedAiExplanationClient : IAiExplanationClient
{
    private readonly Func<MaskedExplanationRequest, string> _respond;

    public ScriptedAiExplanationClient(Func<MaskedExplanationRequest, string> respond)
    {
        _respond = respond;
    }

    public List<string> RecordedBodies { get; } = [];

    /// <summary>Perfil que informa <c>/info</c>; coincide con la metadata de <see cref="ExplainQueryHarness.TracedResponse"/>.</summary>
    public AiServiceProfile? Profile { get; set; } = ExplainQueryHarness.Profile;

    public Task<AiServiceProfile?> GetProfileAsync(CancellationToken cancellationToken) => Task.FromResult(Profile);

    public Task<string> SendAsync(MaskedExplanationRequest request, CancellationToken cancellationToken)
    {
        RecordedBodies.Add(request.ToWireJson());
        return Task.FromResult(_respond(request));
    }

    public static ScriptedAiExplanationClient Throwing(Exception exception) => new(_ => throw exception);
}

/// <summary>Arma un <see cref="ExplainQuery"/> con los componentes reales del núcleo y dobles en los bordes.</summary>
internal sealed class ExplainQueryHarness
{
    public static readonly VersionedRuleCatalog Catalog = new(VersionedRuleCatalog.DiscoverBuiltInRules());

    public static readonly AiServiceProfile Profile = new("v1", "mock");

    public InMemoryExplanationStore Store { get; } = new();

    public FakeEncryptor Encryptor { get; } = new();

    public InMemoryKnowledgeBaseRepository KnowledgeBase { get; } = new();

    public ExplainQuery Create(IAiExplanationClient client) => new(
        Store,
        Store,
        Encryptor,
        new ExplanationContextBuilder(new LiteralMasker(), Catalog),
        KnowledgeBase,
        client,
        new ExplanationValidator(),
        new ExplanationValidationContextBuilder(KnowledgeBase),
        Catalog,
        TimeProvider.System);

    /// <summary>Siembra una consulta con el SQL sembrado, el plan del laboratorio sembrado y sus hallazgos reales.</summary>
    public Guid SeedQuery(
        string fixture = "ESC01.actual.sqlplan",
        Func<FindingContract, FindingContract>? alter = null,
        bool evidenceExpired = false)
    {
        var queryId = Guid.NewGuid();
        var contracts = LiteralMaskerEvidenceTests.AnalyzeSeeded(fixture, out var plan);
        var findings = contracts.Select(c => alter is null ? c : alter(c)).Select(c => ToEntity(c, queryId)).ToArray();

        Store.AddSource(new EncryptedExplanationSource(
            queryId,
            QueryStoreQueryId: 1,
            QueryStorePlanId: 1,
            Encryptor.Encrypt(MaskingFixtures.SeededStatementText),
            Encryptor.Encrypt(plan),
            "16.0.4315.2",
            findings,
            ExplanationContextBuilderTests.Score(),
            evidenceExpired));
        return queryId;
    }

    public Guid SeedQueryWithoutFindings()
    {
        var queryId = Guid.NewGuid();
        Store.AddSource(new EncryptedExplanationSource(queryId, 1, 1, null, null, null, [], null));
        return queryId;
    }

    public Explanation Request(Guid queryId)
    {
        var explanation = new Explanation
        {
            Id = Guid.NewGuid(),
            QueryId = queryId,
            State = ExplanationState.Pending,
            CorrelationId = Guid.NewGuid(),
            RequestedAt = DateTimeOffset.UtcNow,
        };
        Store.Explanations.Add(explanation);
        return explanation;
    }

    public ExplanationBody Body(Explanation explanation) =>
        ExplanationBody.FromJson(Encryptor.Decrypt(explanation.EncryptedBody!))!;

    private Finding ToEntity(FindingContract f, Guid queryId) => new()
    {
        Id = Guid.NewGuid(),
        QueryId = queryId,
        FindingCode = f.FindingId,
        RuleId = f.Rule.Id,
        RuleVersion = f.Rule.Version,
        CatalogVersion = f.CatalogVersion,
        SchemaVersion = f.SchemaVersion,
        Kind = AnalysisEnumCodes.ParseFindingKind(f.Kind),
        Severity = AnalysisEnumCodes.ParseFindingSeverity(f.Severity),
        EvidenceClass = AnalysisEnumCodes.ParseEvidenceClass(f.EvidenceClass),
        NodesJson = FindingContractJson.SerializeNodes(f.Nodes),
        EncryptedEvidenceJson = Encryptor.Encrypt(FindingContractJson.SerializeEvidence(f.Evidence)),
        ContextJson = FindingContractJson.SerializeContext(f.Context),
        ThresholdsUsedJson = RuleThresholds.SerializeDictionaryToCanonicalJson(f.ThresholdsUsed),
        EngineVersion = f.EngineVersion,
        AnalyzedAt = f.AnalyzedAt,
    };

    /// <summary>
    /// Respuesta del servicio de IA que el validador acepta como TRAZADA: solo observaciones sin números, cada una
    /// citando un hallazgo real del contexto.
    /// </summary>
    public static string TracedResponse(MaskedExplanationRequest request) => JsonSerializer.Serialize(new
    {
        query_id = request.QueryId.ToString("D"),
        summary = "La consulta presenta señales del motor que conviene revisar.",
        claims = request.Findings.Select(f => new
        {
            claim_type = "OBSERVACION",
            statement = "El motor señaló este operador del plan.",
            finding_ids = new[] { f.FindingId },
        }).ToArray(),
        metadata = new
        {
            llm_profile = "mock",
            model = "mock-model",
            prompt_version = "v1",
            served_by = "mock",
            data_policy = "lab_only",
            execution_time_seconds = 0.1,
        },
    });

    private static readonly string[] UnknownFindingIds = ["F-NO-EXISTE"];

    /// <summary>Respuesta que cita un hallazgo que no está en el contexto: el validador la deja NO_VERIFICADA.</summary>
    public static string UntracedResponse(MaskedExplanationRequest request) => JsonSerializer.Serialize(new
    {
        query_id = request.QueryId.ToString("D"),
        summary = "Resumen.",
        claims = new[] { new { claim_type = "OBSERVACION", statement = "Afirmación sin sustento.", finding_ids = UnknownFindingIds } },
        metadata = new { llm_profile = "mock", model = "mock-model", prompt_version = "v1", served_by = "mock", data_policy = "lab_only", execution_time_seconds = 0.1 },
    });
}
