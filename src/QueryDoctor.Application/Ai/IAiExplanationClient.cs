namespace QueryDoctor.Application.Ai;

/// <summary>
/// Puerto hacia el servicio de IA (S1-18, <c>POST /explain</c>). Solo acepta
/// <see cref="MaskedExplanationRequest"/>, que solo nace de un contexto enmascarado (RNF-SEG-05): el tipo impide
/// enviar texto sin pasar por el enmascarador. Interfaz mínima de S2-04; la implementación HTTP, los reintentos y
/// la degradación son de S2-05 y corren en el Worker (invariante 2).
/// </summary>
public interface IAiExplanationClient
{
    /// <summary>
    /// Envía la solicitud y devuelve el cuerpo JSON de la respuesta (<c>ExplainResponse</c>) sin interpretar:
    /// validarlo es tarea del <c>ExplanationValidator</c> (S2-03).
    /// </summary>
    Task<string> SendAsync(MaskedExplanationRequest request, CancellationToken cancellationToken);
}
