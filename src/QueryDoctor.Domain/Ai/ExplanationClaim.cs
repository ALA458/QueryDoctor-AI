namespace QueryDoctor.Domain.Ai;

/// <summary>
/// Afirmación tipada de una explicación (tabla <c>ai.claim</c>, §7, RF-IA-02). El texto va cifrado (RNF-SEG-14).
/// </summary>
public sealed class ExplanationClaim
{
    public Guid Id { get; set; }

    public Guid ExplanationId { get; set; }

    /// <summary>Posición de la afirmación en la respuesta, desde 0.</summary>
    public int ClaimIndex { get; set; }

    /// <summary>Código del tipo: <c>OBSERVACION</c>, <c>INTERPRETACION</c> o <c>RECOMENDACION</c>.</summary>
    public string ClaimType { get; set; } = string.Empty;

    /// <summary>Texto de la afirmación cifrado con <c>IFieldEncryptor</c>.</summary>
    public string EncryptedText { get; set; } = string.Empty;

    /// <summary>Fragmento curado citado (RF-IA-02, RF-IA-04).</summary>
    public Guid? KbFragmentId { get; set; }

    /// <summary>Etiqueta determinística "indicio" (RF-IA-12), o <see langword="null"/>.</summary>
    public string? IndicationLabel { get; set; }

    /// <summary>Si la afirmación superó los criterios del validador (RF-IA-03).</summary>
    public bool Passed { get; set; }

    public ICollection<ExplanationClaimReference> References { get; set; } = new List<ExplanationClaimReference>();
}

/// <summary>
/// Referencia de una afirmación a un hallazgo y a los nodos del plan que la sustentan (tabla <c>ai.claim_reference</c>,
/// §7, RF-DASH-05). Solo identificadores.
/// </summary>
public sealed class ExplanationClaimReference
{
    public Guid Id { get; set; }

    public Guid ClaimId { get; set; }

    /// <summary>Código del hallazgo citado tal como vino del modelo; puede no existir si la afirmación no pasó (a).</summary>
    public string FindingCode { get; set; } = string.Empty;

    /// <summary>Ids de los nodos del plan del hallazgo, en JSON (RF-DASH-05).</summary>
    public string NodeIdsJson { get; set; } = "[]";
}
