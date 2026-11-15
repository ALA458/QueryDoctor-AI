using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Application.Ai;

/// <summary>
/// El servicio de IA no devolvió una respuesta usable (RF-IA-06). Solo lleva la causa tipada: nunca el cuerpo de
/// la respuesta ni el mensaje del error, que pueden repetir datos (RNF-SEG-09).
/// </summary>
public sealed class AiServiceUnavailableException : Exception
{
    public AiServiceUnavailableException()
        : this(ExplanationCause.Unexpected)
    {
    }

    public AiServiceUnavailableException(string message)
        : base(message)
    {
        Cause = ExplanationCause.Unexpected;
    }

    public AiServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
        Cause = ExplanationCause.Unexpected;
    }

    public AiServiceUnavailableException(ExplanationCause cause)
        : base($"Servicio de IA no disponible: {cause}.")
    {
        Cause = cause;
    }

    public AiServiceUnavailableException(ExplanationCause cause, Exception innerException)
        : base($"Servicio de IA no disponible: {cause}.", innerException)
    {
        Cause = cause;
    }

    public ExplanationCause Cause { get; }
}
