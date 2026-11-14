using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QueryDoctor.Application.Masking;

namespace QueryDoctor.Application.Ai;

/// <summary>
/// Solicitud de explicación hacia el servicio de IA, construida solo a partir de un <see cref="MaskedContext"/>
/// (RNF-SEG-05, RES-06). El constructor es interno: no hay forma de meter texto sin enmascarar, y los únicos
/// valores libres son identificadores (<see cref="Guid"/>). Refleja <c>ExplainRequest</c> de S1-18; S2-05 le
/// agrega los fragmentos de la base de conocimiento y las métricas de la consulta.
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

    private MaskedExplanationRequest(Guid queryId, MaskedTextResult sql, IReadOnlyList<MaskedFinding> findings, Guid? correlationId)
    {
        QueryId = queryId;
        SqlText = sql.IsSendable ? sql.Text! : string.Empty;
        SqlOmittedCause = sql.FailureCause;
        Findings = findings;
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

    /// <summary>Identificador de correlación del trabajo (RNF-MAN-05).</summary>
    public Guid? CorrelationId { get; }

    /// <summary>
    /// Arma la solicitud aplicando RA-03: si el SQL no es enviable, viaja vacío y viajan los hallazgos.
    /// Devuelve <see langword="null"/> si no queda ningún hallazgo enviable: sin hallazgos no se pide
    /// explicación (RF-IA-01) y no se envía nada.
    /// </summary>
    public static MaskedExplanationRequest? TryCreate(Guid queryId, MaskedContext context, Guid? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Findings.Count == 0
            ? null
            : new MaskedExplanationRequest(queryId, context.Sql, context.Findings, correlationId);
    }

    /// <summary>
    /// Cuerpo JSON exacto que se envía a <c>POST /explain</c> (esquema <c>ExplainRequest</c> de S1-18).
    /// </summary>
    public string ToWireJson()
    {
        var wire = new WireRequest(
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
                f.RuleName)).ToArray(),
            [],
            CorrelationId?.ToString("D", CultureInfo.InvariantCulture));

        return JsonSerializer.Serialize(wire, WireOptions);
    }

    private sealed record WireRequest(
        [property: JsonPropertyName("query_id")] string QueryId,
        [property: JsonPropertyName("sql_text")] string SqlText,
        [property: JsonPropertyName("findings")] IReadOnlyList<WireFinding> Findings,
        [property: JsonPropertyName("kb_fragments")] IReadOnlyList<object> KbFragments,
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
}
