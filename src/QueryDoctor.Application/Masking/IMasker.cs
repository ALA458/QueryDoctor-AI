using QueryDoctor.Application.Engine.Rules;

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
    /// en orden fijo: SQL, plan y hallazgos por <c>finding_id</c>. Cada componente falla cerrado por su cuenta,
    /// con causa tipada.
    /// </summary>
    MaskedContext Mask(MaskingInput input);
}

/// <summary>
/// Contenido sin enmascarar que el núcleo quiere enviar a la IA. Nunca se registra ni se serializa.
/// </summary>
/// <param name="SqlText">Texto SQL de la consulta (Query Store), ya descifrado.</param>
/// <param name="PlanXml">Showplan XML del plan, ya descifrado.</param>
/// <param name="Findings">Hallazgos del motor cuya evidencia se enmascara.</param>
public sealed record MaskingInput(
    string? SqlText,
    string? PlanXml = null,
    IReadOnlyList<FindingContract>? Findings = null);

/// <summary>
/// Resultado del enmascarado. Solo el enmascarador puede construirlo.
/// </summary>
public sealed class MaskedContext
{
    internal MaskedContext(
        MaskedTextResult sql,
        MaskedTextResult plan,
        IReadOnlyList<MaskedFinding> findings,
        IReadOnlyList<MaskingExclusion> excludedFindings,
        MaskingMetrics metrics)
    {
        Sql = sql;
        Plan = plan;
        Findings = findings;
        ExcludedFindings = excludedFindings;
        Metrics = metrics;
    }

    /// <summary>Texto SQL enmascarado, o no enviable con su causa.</summary>
    public MaskedTextResult Sql { get; }

    /// <summary>Showplan XML enmascarado, o no enviable con su causa.</summary>
    public MaskedTextResult Plan { get; }

    /// <summary>Hallazgos enviables, ordenados por <c>finding_id</c>.</summary>
    public IReadOnlyList<MaskedFinding> Findings { get; }

    /// <summary>Hallazgos que quedaron fuera por fallo cerrado, con su causa.</summary>
    public IReadOnlyList<MaskingExclusion> ExcludedFindings { get; }

    /// <summary>Métricas registrables (sin texto, RNF-SEG-09).</summary>
    public MaskingMetrics Metrics { get; }
}
