namespace QueryDoctor.Application.Masking;

/// <summary>
/// Enmascarador de literales: única puerta por la que el contexto sale hacia el servicio de IA
/// (RNF-SEG-05, RES-06, CA-HU21.1). No tiene opciones ni modo para desactivarlo (CA-HU21.4).
/// Es puro y determinista: misma entrada, misma salida. No registra nada: solo devuelve métricas.
/// </summary>
public interface IMasker
{
    /// <summary>
    /// Enmascara todos los componentes de <paramref name="input"/> dentro de una misma numeración de marcadores,
    /// en orden fijo: SQL y después plan. Cada componente falla cerrado por su cuenta, con causa tipada.
    /// </summary>
    MaskedContext Mask(MaskingInput input);
}

/// <summary>
/// Contenido sin enmascarar que el núcleo quiere enviar a la IA. Nunca se registra ni se serializa.
/// </summary>
/// <param name="SqlText">Texto SQL de la consulta (Query Store), ya descifrado.</param>
/// <param name="PlanXml">Showplan XML del plan, ya descifrado.</param>
public sealed record MaskingInput(string? SqlText, string? PlanXml = null);

/// <summary>
/// Resultado del enmascarado. Solo el enmascarador puede construirlo.
/// </summary>
public sealed class MaskedContext
{
    internal MaskedContext(MaskedTextResult sql, MaskedTextResult plan, MaskingMetrics metrics)
    {
        Sql = sql;
        Plan = plan;
        Metrics = metrics;
    }

    /// <summary>Texto SQL enmascarado, o no enviable con su causa.</summary>
    public MaskedTextResult Sql { get; }

    /// <summary>Showplan XML enmascarado, o no enviable con su causa.</summary>
    public MaskedTextResult Plan { get; }

    /// <summary>Métricas registrables (sin texto, RNF-SEG-09).</summary>
    public MaskingMetrics Metrics { get; }
}
