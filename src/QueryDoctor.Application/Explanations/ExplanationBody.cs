using System.Text.Json;
using System.Text.Json.Serialization;

namespace QueryDoctor.Application.Explanations;

/// <summary>
/// Resumen y fallos del validador de una explicación, cifrados en <c>ai.explanation.encrypted_body</c> (RNF-SEG-14).
/// Las afirmaciones viven en <c>ai.claim</c> y <c>ai.claim_reference</c> (§7). Lo escribe el Worker y lo lee la Api;
/// la SPA lo muestra como texto, nunca como HTML (RNF-SEG-07).
/// </summary>
public sealed record ExplanationBody(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("persistent_notice")] string? PersistentNotice,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("validation_failures")] IReadOnlyList<ExplanationBodyFailure> ValidationFailures)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ExplanationBody? FromJson(string json) => JsonSerializer.Deserialize<ExplanationBody>(json, Options);
}

/// <summary>Criterio del validador que no se cumplió (RF-IA-03). Solo código, afirmación y criterio.</summary>
public sealed record ExplanationBodyFailure(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("claim_index")] int? ClaimIndex,
    [property: JsonPropertyName("criterion")] string Criterion);
