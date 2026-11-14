namespace QueryDoctor.Domain.Ai;

/// <summary>
/// Tipo de relación causal entre una regla del motor de diagnóstico y un fragmento de conocimiento (RF-IA-03, criterio c).
/// </summary>
public enum CausalRelationType
{
    /// <summary>
    /// Explica el mecanismo técnico o causa por la que se produce el problema detectado por la regla.
    /// </summary>
    Interpretacion = 1,

    /// <summary>
    /// Explica la acción técnica de remediación, mitigación o solución recomendada para corregir el problema.
    /// </summary>
    Recomendacion = 2,
}
