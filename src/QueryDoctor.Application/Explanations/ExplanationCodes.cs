using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Application.Explanations;

/// <summary>
/// Códigos de contrato y textos fijos de la explicación (S2-05). Los textos no dicen "verificada" ni "validada"
/// (RF-IA-10, invariante 6).
/// </summary>
public static class ExplanationCodes
{
    /// <summary>Aviso de indisponibilidad de RF-IA-06 y CA-HU12.1.</summary>
    public const string UnavailableNotice =
        "El servicio de IA no está disponible. Se muestran los hallazgos determinísticos sin explicación.";

    /// <summary>Aviso de CA-HU09.2: sin hallazgos no se genera explicación (RF-IA-01).</summary>
    public const string NoFindingsNotice =
        "La consulta no tiene hallazgos del motor determinístico: no se genera una explicación.";

    /// <summary>Aviso cuando ningún hallazgo se pudo enmascarar (fallo cerrado de RNF-SEG-05).</summary>
    public const string NoSendableFindingsNotice =
        "Ningún hallazgo se pudo enviar al servicio de IA de forma segura: no se genera una explicación.";

    public const string StatePending = "PENDIENTE";
    public const string StateRunning = "EN_CURSO";
    public const string StateCompleted = "COMPLETADA";
    public const string StateUnavailable = "IA_NO_DISPONIBLE";
    public const string StateNotGenerated = "NO_GENERADA";
    public const string StateNoFindings = "SIN_HALLAZGOS";

    public static string ToContractCode(this ExplanationState state) => state switch
    {
        ExplanationState.Pending => StatePending,
        ExplanationState.Running => StateRunning,
        ExplanationState.Completed => StateCompleted,
        ExplanationState.Unavailable => StateUnavailable,
        ExplanationState.NotGenerated => StateNotGenerated,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>Aviso que acompaña a un estado y causa, o <see langword="null"/> si no corresponde.</summary>
    public static string? NoticeFor(ExplanationState state, ExplanationCause? cause) => state switch
    {
        ExplanationState.Unavailable => UnavailableNotice,
        ExplanationState.NotGenerated when cause == ExplanationCause.NoSendableFindings => NoSendableFindingsNotice,
        ExplanationState.NotGenerated => NoFindingsNotice,
        _ => null,
    };
}
