using System.Text.Json.Serialization;
using QueryDoctor.Application.Masking;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Validation;

/// <summary>
/// Estado normativo asignado por el validador determinista a una explicación generada por IA (RF-IA-10, RNF-FUN-02).
/// </summary>
public enum ExplanationValidationStatus
{
    /// <summary>Todos los criterios (a), (b) y (c) se cumplieron sin excepciones.</summary>
    Trazada = 1,

    /// <summary>Al menos un criterio falló o el JSON no cumplió la estructura obligatoria.</summary>
    NoVerificada = 2,
}

/// <summary>
/// Tipo de afirmación dentro de la respuesta estructurada de IA (RF-IA-02).
/// </summary>
public enum ExplanationClaimType
{
    Observacion = 1,
    Interpretacion = 2,
    Recomendacion = 3,
}

/// <summary>
/// Código tipado de fallo de validación para auditoría y trazabilidad (RF-IA-03, RF-TRZ-01, RNF-FUN-03).
/// </summary>
public enum ValidationFailureCode
{
    InvalidJson = 1,
    QueryIdMismatch = 2,
    EmptyClaims = 3,
    InvalidClaimType = 4,
    EmptyStatement = 5,
    MissingFindingReferences = 6,
    UnknownFindingId = 7,
    MissingKbFragmentForCausalClaim = 8,
    InvalidKbFragmentIdFormat = 9,
    UnknownKbFragmentId = 10,
    NumericMismatch = 11,
    UnverifiedCausalRelation = 12,
}

/// <summary>
/// Constantes y etiquetas normativas del contrato de validación (RF-IA-10, RF-IA-12).
/// Nunca utilizan las palabras prohibidas «verificada» ni «validada» en etiquetas positivas.
/// </summary>
public static class ExplanationValidationConstants
{
    /// <summary>Aviso persistente obligatorio para toda explicación con estado <c>TRAZADA</c> (RF-IA-10).</summary>
    public const string PersistentNoticeText =
        "Trazada a evidencia del motor; no equivale a un diagnóstico confirmado";

    /// <summary>Etiqueta determinística para interpretaciones basadas en evidencia estimada o agregada (RF-IA-12).</summary>
    public const string IndicationLabel = "indicio";

    public const string StatusTrazadaCode = "TRAZADA";
    public const string StatusNoVerificadaCode = "NO_VERIFICADA";

    public const string ClaimObservacionCode = "OBSERVACION";
    public const string ClaimInterpretacionCode = "INTERPRETACION";
    public const string ClaimRecomendacionCode = "RECOMENDACION";

    public static string ToContractCode(this ExplanationValidationStatus status) => status switch
    {
        ExplanationValidationStatus.Trazada => StatusTrazadaCode,
        ExplanationValidationStatus.NoVerificada => StatusNoVerificadaCode,
        _ => StatusNoVerificadaCode,
    };

    public static string ToContractCode(this ExplanationClaimType claimType) => claimType switch
    {
        ExplanationClaimType.Observacion => ClaimObservacionCode,
        ExplanationClaimType.Interpretacion => ClaimInterpretacionCode,
        ExplanationClaimType.Recomendacion => ClaimRecomendacionCode,
        _ => throw new ArgumentOutOfRangeException(nameof(claimType)),
    };

    public static bool TryParseClaimType(string? rawCode, out ExplanationClaimType claimType)
    {
        switch (rawCode?.Trim().ToUpperInvariant())
        {
            case ClaimObservacionCode:
                claimType = ExplanationClaimType.Observacion;
                return true;
            case ClaimInterpretacionCode:
                claimType = ExplanationClaimType.Interpretacion;
                return true;
            case ClaimRecomendacionCode:
                claimType = ExplanationClaimType.Recomendacion;
                return true;
            default:
                claimType = default;
                return false;
        }
    }
}

/// <summary>
/// Parámetros de tolerancia y política del validador determinista de explicaciones (RA-02, RF-IA-03).
/// </summary>
public sealed class ExplanationValidationOptions
{
    public const string SectionName = "ExplanationValidation";

    public const double DefaultRelativeTolerance = 0.005; // 0.5 %
    public const double DefaultAbsoluteEpsilon = 1e-9;
    public const int DefaultMaxJsonDepth = 32;

    public static ExplanationValidationOptions Default { get; } = new();

    /// <summary>Tolerancia relativa máxima respecto al valor esperado de evidencia (por defecto 0.005 = 0.5 %).</summary>
    public double RelativeTolerance { get; init; } = DefaultRelativeTolerance;

    /// <summary>Margen numérico mínimo absoluto para comparaciones en punto flotante cercanas a cero.</summary>
    public double AbsoluteEpsilon { get; init; } = DefaultAbsoluteEpsilon;

    /// <summary>Si es <see langword="true"/>, acepta redondeos compatibles con medio paso del último decimal escrito por el modelo.</summary>
    public bool AllowHalfLastDecimalStepRounding { get; init; } = true;

    /// <summary>Si es <see langword="true"/>, acepta la equivalencia porcentual <c>0.85 ↔ 85%</c> cuando la evidencia está en <c>[0, 1]</c>.</summary>
    public bool AllowPercentageScaleEquivalence { get; init; } = true;

    /// <summary>Si es <see langword="true"/>, también valida los números presentes en <c>summary</c> contra la evidencia global del contexto.</summary>
    public bool ValidateSummaryNumbers { get; init; } = true;

    /// <summary>
    /// Si es <see langword="true"/> y una afirmación causal cita hallazgos de distintas reglas, exige relación curada
    /// para todas ellas; si es <see langword="false"/>, basta con que exista para al menos una regla citada.
    /// </summary>
    public bool RequireAllCitedRulesForCausalRelation { get; init; } = true;

    /// <summary>Profundidad máxima aceptada al deserializar el JSON proveniente del servicio de IA.</summary>
    public int MaxJsonDepth { get; init; } = DefaultMaxJsonDepth;

    public void Validate()
    {
        if (double.IsNaN(RelativeTolerance) || double.IsInfinity(RelativeTolerance) || RelativeTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RelativeTolerance), "La tolerancia relativa no puede ser negativa ni infinita.");
        }

        if (double.IsNaN(AbsoluteEpsilon) || double.IsInfinity(AbsoluteEpsilon) || AbsoluteEpsilon < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AbsoluteEpsilon), "El margen absoluto no puede ser negativo ni infinito.");
        }

        if (MaxJsonDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxJsonDepth), "La profundidad máxima de JSON debe ser positiva.");
        }
    }
}

/// <summary>
/// Relación causa-efecto curada entre una regla y un fragmento de la base de conocimiento (S1-17, RF-IA-03).
/// </summary>
public readonly record struct CuratedCausalRelation
{
    public CuratedCausalRelation(string ruleId, Guid fragmentId, CausalRelationType relationType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        if (fragmentId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del fragmento curado no puede estar vacío.", nameof(fragmentId));
        }

        RuleId = ruleId.Trim().ToUpperInvariant();
        FragmentId = fragmentId;
        RelationType = relationType;
    }

    public string RuleId { get; }
    public Guid FragmentId { get; }
    public CausalRelationType RelationType { get; }
}

/// <summary>
/// Evidencia de un hallazgo disponible para validar afirmaciones (construible desde <see cref="MaskedFinding"/> o contratos del motor).
/// </summary>
public sealed record ValidatedFindingInput(
    string FindingId,
    string RuleId,
    EvidenceClass EvidenceClass,
    IReadOnlyList<int> NodeIds,
    IReadOnlyDictionary<string, string> Evidence,
    IReadOnlyDictionary<string, double>? NumericEvidence = null)
{
    /// <summary>Construye la entrada del validador directamente a partir de un <see cref="MaskedFinding"/> emitido por S2-04.</summary>
    public static ValidatedFindingInput FromMaskedFinding(
        MaskedFinding finding,
        IReadOnlyDictionary<string, double>? numericEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return new ValidatedFindingInput(
            finding.FindingId,
            finding.RuleId,
            AnalysisEnumCodes.ParseEvidenceClass(finding.EvidenceClass),
            finding.Nodes.Select(static node => node.NodeId).ToArray(),
            finding.Evidence,
            numericEvidence);
    }
}

/// <summary>
/// Contexto determinista completo contra el que <see cref="IExplanationValidator"/> contrasta una respuesta de IA.
/// </summary>
public sealed record ExplanationValidationContext(
    string QueryId,
    IReadOnlyList<ValidatedFindingInput> Findings,
    IReadOnlyDictionary<string, double> QueryMetrics,
    IReadOnlySet<Guid> CuratedFragmentIds,
    IReadOnlySet<CuratedCausalRelation> CuratedCausalRelations,
    string? EngineVersion = null);

/// <summary>
/// Representación JSON de la respuesta emitida por <c>ai-service</c> (alineada con <c>ExplainResponse</c> en Python).
/// </summary>
public sealed record AiExplanationPayload(
    [property: JsonPropertyName("query_id")] string? QueryId,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("claims")] IReadOnlyList<AiClaimPayload>? Claims,
    [property: JsonPropertyName("metadata")] AiExplanationMetadata? Metadata = null);

/// <summary>Afirmación individual devuelta por el modelo de IA.</summary>
public sealed record AiClaimPayload(
    [property: JsonPropertyName("claim_type")] string? ClaimType,
    [property: JsonPropertyName("statement")] string? Statement,
    [property: JsonPropertyName("finding_ids")] IReadOnlyList<string>? FindingIds,
    [property: JsonPropertyName("kb_fragment_id")] string? KbFragmentId = null);

/// <summary>Metadatos de ejecución y política de datos devueltos por <c>ai-service</c> para trazabilidad (RF-TRZ-01).</summary>
public sealed record AiExplanationMetadata(
    [property: JsonPropertyName("llm_profile")] string? LlmProfile,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("prompt_version")] string? PromptVersion,
    [property: JsonPropertyName("served_by")] string? ServedBy,
    [property: JsonPropertyName("data_policy")] string? DataPolicy,
    [property: JsonPropertyName("execution_time_seconds")] double? ExecutionTimeSeconds);

/// <summary>Detalle estructurado de un incumplimiento detectado durante la validación.</summary>
public sealed record ValidationFailure(
    ValidationFailureCode Code,
    int? ClaimIndex,
    string Criterion,
    string Detail);

/// <summary>Resultado individual de validar una afirmación, incluida la marca determinística de indicio (RF-IA-12).</summary>
public sealed record ValidatedClaimResult(
    int ClaimIndex,
    ExplanationClaimType? ClaimType,
    string ClaimTypeCode,
    string Statement,
    IReadOnlyList<string> FindingIds,
    Guid? KbFragmentId,
    bool IsIndicio,
    string? IndicationLabel,
    bool Passed);

/// <summary>Resultado final del validador determinista de explicaciones (RF-IA-03, RF-IA-10, RF-IA-12, RNF-FUN-02).</summary>
public sealed record ValidatedExplanationResult(
    ExplanationValidationStatus Status,
    string StatusCode,
    string? PersistentNotice,
    string QueryId,
    string Summary,
    AiExplanationMetadata? Metadata,
    IReadOnlyList<ValidatedClaimResult> Claims,
    IReadOnlyList<ValidationFailure> Failures)
{
    public bool IsTraced => Status == ExplanationValidationStatus.Trazada;
}
