using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Application.KnowledgeBase;

/// <summary>
/// Contrato para el acceso a fragmentos y relaciones causa-efecto curadas de la base de conocimiento (RF-IA-04, RF-IA-03).
/// Utilizado por el recuperador (retriever) del contexto de IA y por el validador determinista (ExplanationValidator).
/// </summary>
public interface IKnowledgeBaseRepository
{
    /// <summary>
    /// Obtiene todos los fragmentos curados asociados a una regla específica.
    /// </summary>
    Task<IReadOnlyList<KbFragment>> GetFragmentsByRuleAsync(string ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene un fragmento curado por su identificador único.
    /// </summary>
    Task<KbFragment?> GetFragmentByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene todas las relaciones causa-efecto curadas definidas para una regla específica.
    /// </summary>
    Task<IReadOnlyList<KbCausalRelation>> GetCausalRelationsAsync(string ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica si existe una relación causa-efecto curada válida para el par (regla, fragmento, tipo).
    /// Este método es el soporte directo para el criterio (c) de validación de explicaciones (RF-IA-03 v1.1).
    /// </summary>
    Task<bool> HasCausalRelationAsync(
        string ruleId,
        Guid fragmentId,
        CausalRelationType relationType,
        CancellationToken cancellationToken = default);
}
