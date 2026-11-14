namespace QueryDoctor.Application.Engine.Rules;

/// <summary>
/// Catálogo versionado de reglas del motor determinístico (RF-MOT-15, Arquitectura §5.1).
/// Mantiene las definiciones declarativas de todas las reglas del catálogo (<c>REG-01</c>..<c>REG-11</c>) y las instancias <see cref="IRule"/> activas.
/// </summary>
public interface IRuleCatalog
{
    /// <summary>Versión semántica del catálogo de reglas (por ejemplo, <c>1.0.0</c>).</summary>
    string CatalogVersion { get; }

    /// <summary>Lista completa y ordenada por <c>RuleId</c> de los descriptores del catálogo (Cuadro 4.11 del SRS).</summary>
    IReadOnlyList<RuleDescriptor> GetAllDescriptors();

    /// <summary>Busca el descriptor de una regla por su identificador (<c>REG-01</c>..<c>REG-11</c>).</summary>
    RuleDescriptor? TryGetDescriptor(string ruleId);

    /// <summary>Obtiene el descriptor de una regla o lanza <see cref="KeyNotFoundException"/> si no existe.</summary>
    RuleDescriptor GetDescriptor(string ruleId);

    /// <summary>
    /// Devuelve las reglas activas registradas en el contenedor de dependencias, ordenadas de forma determinista por <c>RuleId</c>.
    /// </summary>
    IReadOnlyList<IRule> GetActiveRules();
}
