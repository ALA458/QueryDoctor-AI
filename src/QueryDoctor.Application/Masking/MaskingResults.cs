namespace QueryDoctor.Application.Masking;

/// <summary>
/// Tipo de literal sustituido por un marcador (RNF-SEG-05). Se conserva en el marcador
/// para que la IA entienda la estructura de la consulta sin ver el valor.
/// </summary>
public enum LiteralKind
{
    /// <summary>Cadena ASCII o Unicode (<c>'…'</c>, <c>N'…'</c>) y texto entre comillas dobles.</summary>
    Text,

    /// <summary>Entero, decimal, real o money.</summary>
    Number,

    /// <summary>Binario hexadecimal (<c>0x…</c>).</summary>
    Binary,

    /// <summary>Cadena cuyo contenido es una fecha u hora en formato ISO.</summary>
    Temporal,

    /// <summary>Cadena cuyo contenido es un GUID.</summary>
    UniqueId,
}

/// <summary>
/// Causa tipada por la que un componente no se puede enviar al proveedor de IA (fallo cerrado, RA-03).
/// Nunca se acompaña del mensaje del analizador: ScriptDom y XmlReader repiten el texto de entrada en sus mensajes.
/// </summary>
public enum MaskingFailureCause
{
    /// <summary>No se recibió contenido.</summary>
    InputMissing,

    /// <summary>El contenido supera el límite de tamaño admitido.</summary>
    InputTooLarge,

    /// <summary>ScriptDom no pudo tokenizar el texto SQL (cadena o comentario sin cerrar, texto truncado…).</summary>
    SqlLexicalError,

    /// <summary>El plan no es XML bien formado o está truncado.</summary>
    PlanMalformed,

    /// <summary>El plan declara un DTD (prohibido, como en el parser de S1-12).</summary>
    PlanDtdProhibited,

    /// <summary>El elemento raíz no es <c>ShowPlanXML</c>.</summary>
    PlanUnexpectedRoot,

    /// <summary>El plan supera la profundidad máxima de anidamiento.</summary>
    PlanTooDeep,

    /// <summary>Un atributo que lleva T-SQL o un valor literal no se pudo tokenizar.</summary>
    PlanAttributeLexicalError,

    /// <summary>
    /// Apareció un posible portador de literales no catalogado (atributo con apóstrofo o texto en un elemento).
    /// </summary>
    PlanUnexpectedLiteralCarrier,
}

/// <summary>
/// Resultado de enmascarar un componente de texto (SQL o plan). Si no es enviable, <see cref="Text"/> es
/// <see langword="null"/>: nunca se entrega el original ni una versión a medio enmascarar.
/// Solo el enmascarador puede construirlo.
/// </summary>
public sealed class MaskedTextResult
{
    internal MaskedTextResult(string? text, MaskingFailureCause? failureCause)
    {
        Text = text;
        FailureCause = failureCause;
    }

    /// <summary>Indica si el componente se puede enviar.</summary>
    public bool IsSendable => FailureCause is null;

    /// <summary>Texto enmascarado; <see langword="null"/> si no es enviable.</summary>
    public string? Text { get; }

    /// <summary>Causa del fallo cerrado; <see langword="null"/> si es enviable.</summary>
    public MaskingFailureCause? FailureCause { get; }

    internal static MaskedTextResult Sendable(string text) => new(text, null);

    internal static MaskedTextResult NotSendable(MaskingFailureCause cause) => new(null, cause);
}

/// <summary>
/// Métricas del enmascarado: lo único que se puede registrar (RNF-SEG-09). No contienen texto.
/// </summary>
public sealed record MaskingMetrics(
    int StringLiterals,
    int NumberLiterals,
    int BinaryLiterals,
    int DateLiterals,
    int GuidLiterals,
    int CommentsRemoved,
    int DistinctMarkers)
{
    /// <summary>Total de literales sustituidos.</summary>
    public int TotalLiterals => StringLiterals + NumberLiterals + BinaryLiterals + DateLiterals + GuidLiterals;
}
