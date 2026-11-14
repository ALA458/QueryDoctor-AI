namespace QueryDoctor.Application.Validation;

/// <summary>
/// Puerto puro y determinista de validación de explicaciones generadas por IA (S2-03, RF-IA-03, RF-IA-10, RF-IA-12, RNF-FUN-02).
/// </summary>
public interface IExplanationValidator
{
    /// <summary>
    /// Deserializa de forma estricta el JSON devuelto por el servicio de IA y aplica los criterios (a), (b) y (c).
    /// </summary>
    ValidatedExplanationResult ValidateJson(string rawResponseJson, ExplanationValidationContext context);

    /// <summary>
    /// Valida un payload ya deserializado frente a los hallazgos, métricas y relaciones curadas del contexto.
    /// </summary>
    ValidatedExplanationResult Validate(AiExplanationPayload payload, ExplanationValidationContext context);
}
