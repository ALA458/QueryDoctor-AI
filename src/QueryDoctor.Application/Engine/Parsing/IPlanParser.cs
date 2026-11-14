using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.Application.Engine.Parsing;

/// <summary>
/// Opciones de seguridad y límites estructurales para el análisis de Showplan XML (RF-MOT-01, RF-MOT-16, OWASP A05).
/// </summary>
public sealed record PlanParserOptions
{
    /// <summary>
    /// Identificador canónico de la regla determinista para planes no analizables (RF-MOT-16, RF-ING-09).
    /// </summary>
    public const string UnanalyzablePlanRuleId = "REG-11";

    /// <summary>
    /// Límite predeterminado de caracteres por documento Showplan XML (~2 millones de caracteres ≈ 2–4 MB).
    /// Permite planes de hasta 1 MB (RNF-RND-04) y planes profundos como ESC06 (~305 KB) bloqueando bombas XML.
    /// </summary>
    public const int DefaultMaxCharactersInDocument = 2_097_152;

    /// <summary>
    /// Profundidad máxima predeterminada de elementos XML anidados.
    /// Soporta holgadamente el escenario F8 (140 niveles de RelOp, ~300 niveles XML) y evita desbordamientos.
    /// </summary>
    public const int DefaultMaxElementDepth = 512;

    /// <summary>
    /// Número máximo de caracteres permitidos en el documento Showplan XML.
    /// </summary>
    public int MaxCharactersInDocument { get; init; } = DefaultMaxCharactersInDocument;

    /// <summary>
    /// Profundidad máxima de elementos XML permitida durante el lectura en streaming.
    /// </summary>
    public int MaxElementDepth { get; init; } = DefaultMaxElementDepth;
}

/// <summary>
/// Causa categorizada cuando un Showplan XML no puede analizarse y produce <c>REG-11</c> (RF-MOT-16, RF-ING-09).
/// </summary>
public enum PlanParseFailureReason
{
    EmptyOrNullXml,
    SizeLimitExceeded,
    DtdProhibited,
    MaxDepthExceeded,
    MalformedXml,
    UnexpectedRootElement,
}

/// <summary>
/// Señal determinista <c>REG-11</c> emitida cuando un plan es nulo, truncado o inválido (RF-MOT-16, RF-ING-09).
/// </summary>
public sealed record UnanalyzablePlanFinding(
    string RuleId,
    PlanParseFailureReason Reason,
    string Message,
    string? PlanIdentifier = null);

/// <summary>
/// Resultado determinista del parseo de un plan Showplan XML.
/// </summary>
public sealed record PlanParseResult(
    bool IsSuccess,
    ParsedPlan? Plan,
    UnanalyzablePlanFinding? Finding,
    string? PlanIdentifier = null)
{
    /// <summary>
    /// Estado correspondiente para <see cref="Plan.AnalysisStatus"/> (RF-ING-09).
    /// </summary>
    public PlanAnalysisStatus Status =>
        IsSuccess ? PlanAnalysisStatus.Analyzable : PlanAnalysisStatus.NotAnalyzable;

    public static PlanParseResult Success(ParsedPlan plan, string? planIdentifier = null) =>
        new(true, plan, null, planIdentifier);

    public static PlanParseResult Failure(
        PlanParseFailureReason reason,
        string message,
        string? planIdentifier = null) =>
        new(
            false,
            null,
            new UnanalyzablePlanFinding(PlanParserOptions.UnanalyzablePlanRuleId, reason, message, planIdentifier),
            planIdentifier);
}

/// <summary>
/// Entrada de un lote de planes a parsear.
/// </summary>
public sealed record PlanInput(
    string PlanIdentifier,
    string? ShowplanXml,
    string? FallbackEngineVersion = null);

/// <summary>
/// Analizador de documentos Showplan XML en un árbol tipado de operadores (RF-MOT-01, RNF-COM-02, RF-MOT-16).
/// Opera en memoria sin E/S (Invariante 7).
/// </summary>
public interface IPlanParser
{
    /// <summary>
    /// Parsea un Showplan XML individual con <c>XmlReader</c> (sin DTD y con límite de tamaño).
    /// Nunca lanza <see cref="System.Xml.XmlException"/>: si el XML es inválido, devuelve <c>REG-11</c>.
    /// </summary>
    PlanParseResult Parse(
        string? showplanXml,
        string? planIdentifier = null,
        string? fallbackEngineVersion = null);

    /// <summary>
    /// Parsea una secuencia de planes garantizando que cualquier XML nulo, truncado o inválido
    /// produzca <c>REG-11</c> sin detener el procesamiento del resto del lote (RF-MOT-16, OWASP A10).
    /// </summary>
    IReadOnlyList<PlanParseResult> ParseBatch(IEnumerable<PlanInput> plans);
}
