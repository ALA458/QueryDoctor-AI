using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QueryDoctor.Application.Masking;
using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Application.Ai;

/// <summary>
/// Material del contexto que no proviene de la consulta del cliente: catálogo de reglas, fragmentos curados de la
/// base de conocimiento y métricas numéricas calculadas por la plataforma (RF-IA-01, RF-IA-04). Nada de esto lleva
/// literales de la consulta, así que no pasa por el enmascarador.
/// </summary>
/// <param name="CatalogVersion">Versión del catálogo de reglas (RF-TRZ-01). Entra en el hash del contexto.</param>
/// <param name="RuleDescriptions">Descripción del catálogo por regla (<c>description</c> de cada hallazgo).</param>
/// <param name="KbFragments">Fragmentos curados de las reglas de los hallazgos (RF-IA-04).</param>
/// <param name="QueryMetrics">Métricas ingeridas de la consulta (RF-IA-01), las mismas que usa el validador.</param>
public sealed record ExplanationContextAdditions(
    string CatalogVersion,
    IReadOnlyDictionary<string, string> RuleDescriptions,
    IReadOnlyList<KbFragment> KbFragments,
    IReadOnlyDictionary<string, double> QueryMetrics)
{
    /// <summary>Sin catálogo, fragmentos ni métricas.</summary>
    public static ExplanationContextAdditions Empty { get; } = new(
        string.Empty,
        new Dictionary<string, string>(StringComparer.Ordinal),
        [],
        new Dictionary<string, double>(StringComparer.Ordinal));
}

/// <summary>
/// Solicitud de explicación hacia el servicio de IA, construida solo a partir de un <see cref="MaskedContext"/>
/// (RNF-SEG-05, RES-06). El constructor es privado: no hay forma de meter texto sin enmascarar, y los únicos
/// valores libres son identificadores (<see cref="Guid"/>) y material curado de la plataforma
/// (<see cref="ExplanationContextAdditions"/>). Refleja <c>ExplainRequest</c> de S1-18.
/// </summary>
public sealed class MaskedExplanationRequest
{
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Sin escapar no ASCII: lo que viaja es literalmente lo que la suite de centinelas inspecciona.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ExplanationContextAdditions _additions;

    private MaskedExplanationRequest(
        Guid queryId,
        MaskedTextResult sql,
        IReadOnlyList<MaskedFinding> findings,
        ExplanationContextAdditions additions,
        Guid? correlationId)
    {
        QueryId = queryId;
        SqlText = sql.IsSendable ? sql.Text! : string.Empty;
        SqlOmittedCause = sql.FailureCause;
        Findings = findings;
        _additions = additions;
        CorrelationId = correlationId;
    }

    /// <summary>Consulta explicada.</summary>
    public Guid QueryId { get; }

    /// <summary>
    /// Texto SQL enmascarado. Vacío si no fue enviable (RA-03, invariante 5: se envían solo los hallazgos).
    /// </summary>
    public string SqlText { get; }

    /// <summary>Causa por la que el SQL no viaja; <see langword="null"/> si viaja.</summary>
    public MaskingFailureCause? SqlOmittedCause { get; }

    /// <summary>Hallazgos enmascarados (al menos uno, RF-IA-01).</summary>
    public IReadOnlyList<MaskedFinding> Findings { get; }

    /// <summary>Versión del catálogo de reglas con la que se produjeron los hallazgos.</summary>
    public string CatalogVersion => _additions.CatalogVersion;

    /// <summary>Métricas de la consulta incluidas en el contexto.</summary>
    public IReadOnlyDictionary<string, double> QueryMetrics => _additions.QueryMetrics;

    /// <summary>Identificador de correlación del trabajo (RNF-MAN-05). No entra en el hash.</summary>
    public Guid? CorrelationId { get; }

    /// <summary>
    /// Arma la solicitud aplicando RA-03: si el SQL no es enviable, viaja vacío y viajan los hallazgos.
    /// Devuelve <see langword="null"/> si no queda ningún hallazgo enviable: sin hallazgos no se pide
    /// explicación (RF-IA-01) y no se envía nada.
    /// </summary>
    public static MaskedExplanationRequest? TryCreate(Guid queryId, MaskedContext context, Guid? correlationId = null) =>
        TryCreate(queryId, context, ExplanationContextAdditions.Empty, correlationId);

    /// <inheritdoc cref="TryCreate(Guid, MaskedContext, Guid?)"/>
    public static MaskedExplanationRequest? TryCreate(
        Guid queryId,
        MaskedContext context,
        ExplanationContextAdditions additions,
        Guid? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(additions);
        return context.Findings.Count == 0
            ? null
            : new MaskedExplanationRequest(queryId, context.Sql, context.Findings, additions, correlationId);
    }

    /// <summary>
    /// Cuerpo JSON exacto que se envía a <c>POST /explain</c> (esquema <c>ExplainRequest</c> de S1-18).
    /// </summary>
    public string ToWireJson() => JsonSerializer.Serialize(BuildWire(includeCorrelation: true), WireOptions);

    /// <summary>
    /// Resumen SHA-256 (hexadecimal en minúsculas) del contexto canónico, para la caché de RF-IA-07 (E3).
    /// Se calcula sobre el contenido YA enmascarado más la versión del catálogo y de cada regla, sin el
    /// identificador de correlación: mismo contexto, mismo hash. Por eso se puede guardar en claro.
    /// </summary>
    public string ComputeContextHash()
    {
        var canonical = new CanonicalContext(
            _additions.CatalogVersion,
            new SortedDictionary<string, string>(
                Findings
                    .GroupBy(f => f.RuleId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().RuleVersion, StringComparer.Ordinal),
                StringComparer.Ordinal),
            BuildWire(includeCorrelation: false));

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, WireOptions));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private WireRequest BuildWire(bool includeCorrelation) => new(
        QueryId.ToString("D", CultureInfo.InvariantCulture),
        SqlText,
        Findings.Select(f => new WireFinding(
            f.FindingId,
            f.RuleId,
            f.RuleName,
            f.EvidenceClass,
            f.Severity,
            f.Nodes.Select(n => n.NodeId.ToString(CultureInfo.InvariantCulture)).ToArray(),
            f.Evidence,
            _additions.RuleDescriptions.TryGetValue(f.RuleId, out var description) && !string.IsNullOrWhiteSpace(description)
                ? description
                : f.RuleName)).ToArray(),
        _additions.KbFragments
            .OrderBy(k => k.Id)
            .Select(k => new WireKbFragment(
                k.Id.ToString("D", CultureInfo.InvariantCulture),
                k.RuleId,
                k.Title,
                k.Content,
                k.SourceCitation))
            .ToArray(),
        _additions.QueryMetrics.Count == 0
            ? null
            : new SortedDictionary<string, double>(_additions.QueryMetrics.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        includeCorrelation ? CorrelationId?.ToString("D", CultureInfo.InvariantCulture) : null);

    private sealed record CanonicalContext(
        [property: JsonPropertyName("catalog_version")] string CatalogVersion,
        [property: JsonPropertyName("rule_versions")] IReadOnlyDictionary<string, string> RuleVersions,
        [property: JsonPropertyName("request")] WireRequest Request);

    private sealed record WireRequest(
        [property: JsonPropertyName("query_id")] string QueryId,
        [property: JsonPropertyName("sql_text")] string SqlText,
        [property: JsonPropertyName("findings")] IReadOnlyList<WireFinding> Findings,
        [property: JsonPropertyName("kb_fragments")] IReadOnlyList<WireKbFragment> KbFragments,
        [property: JsonPropertyName("metrics")] IReadOnlyDictionary<string, double>? Metrics,
        [property: JsonPropertyName("correlation_id")] string? CorrelationId);

    private sealed record WireFinding(
        [property: JsonPropertyName("finding_id")] string FindingId,
        [property: JsonPropertyName("rule_id")] string RuleId,
        [property: JsonPropertyName("rule_name")] string RuleName,
        [property: JsonPropertyName("evidence_class")] string EvidenceClass,
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("target_nodes")] IReadOnlyList<string> TargetNodes,
        [property: JsonPropertyName("metrics")] IReadOnlyDictionary<string, string> Metrics,
        [property: JsonPropertyName("description")] string Description);

    private sealed record WireKbFragment(
        [property: JsonPropertyName("fragment_id")] string FragmentId,
        [property: JsonPropertyName("rule_id")] string RuleId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("source_citation")] string SourceCitation);
}
