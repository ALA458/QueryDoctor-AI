namespace QueryDoctor.Domain.Ai;

/// <summary>
/// Estado de una solicitud de explicación (S2-05, CU-05).
/// </summary>
public enum ExplanationState
{
    /// <summary>Encolada en <c>explain</c>, todavía sin tomar.</summary>
    Pending = 1,

    /// <summary>El Worker la está procesando.</summary>
    Running = 2,

    /// <summary>Hay explicación validada (TRAZADA o NO_VERIFICADA), generada o tomada de la caché.</summary>
    Completed = 3,

    /// <summary>El servicio o el proveedor de IA fallaron: se muestran los hallazgos sin explicación (RF-IA-06).</summary>
    Unavailable = 4,

    /// <summary>No se generó: no había hallazgos o ninguno era enviable (RF-IA-01, RNF-SEG-05).</summary>
    NotGenerated = 5,
}

/// <summary>
/// Causa tipada de <see cref="ExplanationState.Unavailable"/> o <see cref="ExplanationState.NotGenerated"/>.
/// Nunca se guarda el texto de un error: puede repetir datos de la consulta o de la respuesta (RNF-SEG-09).
/// </summary>
public enum ExplanationCause
{
    /// <summary>El último análisis de la consulta no tiene hallazgos (RF-IA-01, CA-HU09.2).</summary>
    NoFindings = 1,

    /// <summary>Ningún hallazgo sobrevivió al enmascarado (fallo cerrado de S2-04).</summary>
    NoSendableFindings = 2,

    /// <summary>El servicio de IA no respondió dentro del tiempo límite (RF-IA-06, RNF-FIA-02).</summary>
    Timeout = 3,

    /// <summary>No se pudo conectar con el servicio de IA.</summary>
    ServiceUnreachable = 4,

    /// <summary>El servicio de IA informó un fallo del proveedor (5xx).</summary>
    ProviderError = 5,

    /// <summary>La salida del modelo no cumplió el esquema (422) o no se pudo leer (E5).</summary>
    InvalidOutput = 6,

    /// <summary>Fallo inesperado en el Worker.</summary>
    Unexpected = 7,

    /// <summary>La solicitud quedó en curso más allá del tiempo límite (el Worker se detuvo).</summary>
    Abandoned = 8,
}

/// <summary>
/// Explicación de una consulta (tabla <c>ai.explanation</c>, S2-05, E4). Las afirmaciones y el resumen viven en
/// <see cref="EncryptedBody"/>, cifrado (RNF-SEG-14). Las demás columnas son identificadores, códigos y el hash del
/// contexto enmascarado, que alimentan la caché (RF-IA-07) y la trazabilidad (RF-TRZ-01).
/// </summary>
public sealed class Explanation
{
    public Guid Id { get; set; }

    public Guid ConnectionId { get; set; }

    public Guid QueryId { get; set; }

    public ExplanationState State { get; set; } = ExplanationState.Pending;

    public ExplanationCause? Cause { get; set; }

    /// <summary>Código del validador: <c>TRAZADA</c> o <c>NO_VERIFICADA</c> (RF-IA-10). Solo si está completada.</summary>
    public string? ValidationStatus { get; set; }

    /// <summary>SHA-256 hexadecimal del contexto canónico ya enmascarado (RF-IA-07, E3).</summary>
    public string? ContextHash { get; set; }

    /// <summary>Códigos de los hallazgos enviados, en JSON (RF-TRZ-01).</summary>
    public string FindingCodesJson { get; set; } = "[]";

    public string? CatalogVersion { get; set; }

    public string? LlmProfile { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public string? ServedBy { get; set; }

    public string? DataPolicy { get; set; }

    /// <summary>Resultado validado (resumen, afirmaciones y fallos) cifrado con <c>IFieldEncryptor</c>.</summary>
    public string? EncryptedBody { get; set; }

    /// <summary>Explicación de la que se tomó el resultado cuando vino de la caché.</summary>
    public Guid? ReusedFromId { get; set; }

    /// <summary>Identificador de correlación propagado al servicio de IA (RNF-MAN-05).</summary>
    public Guid CorrelationId { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
