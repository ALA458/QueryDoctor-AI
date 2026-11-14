namespace QueryDoctor.Domain.Ai;

/// <summary>
/// Relación causal curada entre una regla del motor y un fragmento de la base de conocimiento (RF-IA-03 v1.1).
/// Utilizada por el criterio (c) de ExplanationValidator para verificar que cualquier INTERPRETACIÓN
/// o RECOMENDACIÓN emitida por la IA cite un fragmento previamente curado.
/// </summary>
public sealed class KbCausalRelation
{
    public Guid Id { get; set; }

    /// <summary>
    /// Identificador de la regla evaluada por el motor (ej. "REG-01", "REG-02", "REG-03", "REG-04").
    /// </summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    /// Identificador del fragmento curado asociado.
    /// </summary>
    public Guid FragmentId { get; set; }

    /// <summary>
    /// Fragmento curado asociado.
    /// </summary>
    public KbFragment? Fragment { get; set; }

    /// <summary>
    /// Tipo de relación causal: Interpretación (mecanismo/causa) o Recomendación (solución).
    /// </summary>
    public CausalRelationType RelationType { get; set; }
}
