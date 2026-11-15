using System.Text.Json;
using System.Text.Json.Serialization;

namespace QueryDoctor.Application.Explanations;

/// <summary>
/// Contenido de la explicación validada que se guarda cifrado en <c>ai.explanation.encrypted_body</c> (E4,
/// RNF-SEG-14). Lo escribe el Worker y lo lee la Api. La SPA lo muestra como texto, nunca como HTML (RNF-SEG-07).
/// </summary>
public sealed record ExplanationBody(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("persistent_notice")] string? PersistentNotice,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("claims")] IReadOnlyList<ExplanationBodyClaim> Claims,
    [property: JsonPropertyName("validation_failures")] IReadOnlyList<ExplanationBodyFailure> ValidationFailures)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ExplanationBody? FromJson(string json) => JsonSerializer.Deserialize<ExplanationBody>(json, Options);
}

/// <summary>Afirmación tipada con sus referencias (RF-IA-02) y la etiqueta de indicio (RF-IA-12).</summary>
public sealed record ExplanationBodyClaim(
    [property: JsonPropertyName("claim_type")] string ClaimType,
    [property: JsonPropertyName("statement")] string Statement,
    [property: JsonPropertyName("finding_ids")] IReadOnlyList<string> FindingIds,
    [property: JsonPropertyName("kb_fragment_id")] Guid? KbFragmentId,
    [property: JsonPropertyName("indication_label")] string? IndicationLabel,
    [property: JsonPropertyName("passed")] bool Passed);

/// <summary>Criterio del validador que no se cumplió (RF-IA-03). Solo código, afirmación y criterio.</summary>
public sealed record ExplanationBodyFailure(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("claim_index")] int? ClaimIndex,
    [property: JsonPropertyName("criterion")] string Criterion);
