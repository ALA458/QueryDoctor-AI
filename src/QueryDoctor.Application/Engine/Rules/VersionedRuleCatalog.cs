using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Engine.Rules;

/// <summary>
/// Implementación canónica del catálogo versionado de reglas del motor determinístico (RF-MOT-15, Cuadros 4.5 y 4.11 del SRS).
/// </summary>
public sealed class VersionedRuleCatalog : IRuleCatalog
{
    public const string CurrentCatalogVersion = "1.0.0";
    public const string DefaultRuleVersion = "1.0.0";

    private static readonly IReadOnlyList<RuleDescriptor> CanonicalDescriptors = BuildCanonicalDescriptors();
    private static readonly IReadOnlyDictionary<string, RuleDescriptor> DescriptorsById =
        CanonicalDescriptors.ToDictionary(d => d.RuleId, StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyList<IRule> _activeRules;

    public VersionedRuleCatalog(IEnumerable<IRule>? activeRules = null)
    {
        // Orden determinista por RuleId y versión para que la salida nunca dependa del orden de registro en DI (RF-MOT-02).
        _activeRules = (activeRules ?? [])
            .OrderBy(r => r.Descriptor.RuleId, StringComparer.Ordinal)
            .ThenBy(r => r.Descriptor.Version, StringComparer.Ordinal)
            .ToArray();
    }

    public string CatalogVersion => CurrentCatalogVersion;

    public static IReadOnlyList<RuleDescriptor> BuiltInDescriptors => CanonicalDescriptors;

    public IReadOnlyList<RuleDescriptor> GetAllDescriptors() => CanonicalDescriptors;

    public RuleDescriptor? TryGetDescriptor(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return null;
        }

        return DescriptorsById.GetValueOrDefault(ruleId.Trim());
    }

    public RuleDescriptor GetDescriptor(string ruleId) =>
        TryGetDescriptor(ruleId)
        ?? throw new KeyNotFoundException($"No existe la regla '{ruleId}' en el catálogo v{CatalogVersion}.");

    public IReadOnlyList<IRule> GetActiveRules() => _activeRules;

    /// <summary>
    /// Construye las entidades <see cref="RuleCatalogEntry"/> con identificadores fijos para el sembrado determinista en EF Core.
    /// </summary>
    public static IReadOnlyList<RuleCatalogEntry> CreateSeedEntries()
    {
        var entries = new List<RuleCatalogEntry>(CanonicalDescriptors.Count);
        for (var i = 0; i < CanonicalDescriptors.Count; i++)
        {
            var descriptor = CanonicalDescriptors[i];
            var deterministicGuid = new Guid($"7a2d4c0e-9b63-4e1a-8d2f-3b8e5f6a{(i + 1):D4}");
            entries.Add(new RuleCatalogEntry
            {
                Id = deterministicGuid,
                RuleId = descriptor.RuleId,
                CatalogVersion = CurrentCatalogVersion,
                RuleVersion = descriptor.Version,
                Name = descriptor.Name,
                Description = descriptor.Description,
                DocumentationSource = descriptor.DocumentationSource,
                RuleClass = descriptor.RuleClass,
                EvidenceClass = descriptor.RequiredEvidenceClass,
                BaseSeverity = descriptor.BaseSeverity,
                Priority = descriptor.Priority,
                DefaultThresholdsJson = RuleThresholds.SerializeDictionaryToCanonicalJson(descriptor.DefaultThresholds),
            });
        }

        return entries;
    }

    private static IReadOnlyList<RuleDescriptor> BuildCanonicalDescriptors()
    {
        var defaults = RuleThresholds.Default;

        return
        [
            new RuleDescriptor(
                RuleId: "REG-01",
                Name: "Conversión implícita que afecta el plan",
                Version: DefaultRuleVersion,
                Description: "Advertencia PlanAffectingConvert del plan, con el tipo de problema (ConvertIssue) y la expresión afectada.",
                DocumentationSource: "Microsoft Learn: Showplan XML Warnings (PlanAffectingConvert) [4, 11]",
                RuleClass: RuleClass.ClassA,
                RequiredEvidenceClass: EvidenceClass.Estimada,
                BaseSeverity: FindingSeverity.Alta,
                Priority: RulePriority.Must,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-01")),
            new RuleDescriptor(
                RuleId: "REG-02",
                Name: "Índice faltante",
                Version: DefaultRuleVersion,
                Description: "Grupo MissingIndexes del plan, con el impacto estimado y las columnas por tipo de uso, acompañado de la advertencia de costo de escritura.",
                DocumentationSource: "Microsoft Learn: Tune nonclustered indexes with missing index suggestions [30]",
                RuleClass: RuleClass.ClassA,
                RequiredEvidenceClass: EvidenceClass.Estimada,
                BaseSeverity: FindingSeverity.Media,
                Priority: RulePriority.Must,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-02")),
            new RuleDescriptor(
                RuleId: "REG-03",
                Name: "Lectura completa costosa (contextual)",
                Version: DefaultRuleVersion,
                Description: "Operadores de recorrido completo de tabla o de índice que cumplen una condición de contexto: predicado con alta razón entre filas leídas y devueltas estimadas, o volumen estimado alto en una consulta con consumo relevante en la ventana.",
                DocumentationSource: "Microsoft Learn: Display the estimated execution plan & Table/Index Scan operators [4, 6]",
                RuleClass: RuleClass.ClassA,
                RequiredEvidenceClass: EvidenceClass.Estimada,
                BaseSeverity: FindingSeverity.Media,
                Priority: RulePriority.Must,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-03")),
            new RuleDescriptor(
                RuleId: "REG-04",
                Name: "Búsqueda por clave excesiva (contextual)",
                Version: DefaultRuleVersion,
                Description: "Operador de búsqueda por clave (Key Lookup o RID Lookup) con un número estimado de ejecuciones por encima de un umbral configurable, en una consulta con consumo relevante en la ventana.",
                DocumentationSource: "Microsoft Learn: Showplan Logical and Physical Operators Reference (Key Lookup / RID Lookup) [4, 6]",
                RuleClass: RuleClass.ClassA,
                RequiredEvidenceClass: EvidenceClass.Estimada,
                BaseSeverity: FindingSeverity.Media,
                Priority: RulePriority.Must,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-04")),
            new RuleDescriptor(
                RuleId: "REG-05",
                Name: "Ausencia de estadísticas",
                Version: DefaultRuleVersion,
                Description: "Advertencia ColumnsWithNoStatistics en el plan compilado, con las columnas afectadas.",
                DocumentationSource: "Microsoft Learn: Showplan XML Warnings (ColumnsWithNoStatistics) [4, 11]",
                RuleClass: RuleClass.ClassA,
                RequiredEvidenceClass: EvidenceClass.Estimada,
                BaseSeverity: FindingSeverity.Media,
                Priority: RulePriority.Should,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-05")),
            new RuleDescriptor(
                RuleId: "REG-06",
                Name: "Unión sin predicado",
                Version: DefaultRuleVersion,
                Description: "Advertencia de unión sin predicado explícito de cruce (NoJoinPredicate) en un operador del plan.",
                DocumentationSource: "Microsoft Learn: Showplan XML Warnings (NoJoinPredicate) [4, 11]",
                RuleClass: RuleClass.ClassA,
                RequiredEvidenceClass: EvidenceClass.Estimada,
                BaseSeverity: FindingSeverity.Alta,
                Priority: RulePriority.Should,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-06")),
            new RuleDescriptor(
                RuleId: "REG-07",
                Name: "Discrepancia de cardinalidad a nivel de sentencia",
                Version: DefaultRuleVersion,
                Description: "Comparación entre las filas estimadas de la sentencia y las filas devueltas promedio de las estadísticas de ejecución en Query Store.",
                DocumentationSource: "Microsoft Learn: How Query Store collects data & sys.query_store_runtime_stats [3, 5]",
                RuleClass: RuleClass.ClassB,
                RequiredEvidenceClass: EvidenceClass.EjecucionAgregada,
                BaseSeverity: FindingSeverity.Media,
                Priority: RulePriority.Should,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-07")),
            new RuleDescriptor(
                RuleId: "REG-08",
                Name: "Regresión de rendimiento entre planes",
                Version: DefaultRuleVersion,
                Description: "Comparación de las estadísticas agregadas de duración y CPU entre múltiples planes de una misma consulta a lo largo del tiempo.",
                DocumentationSource: "Microsoft Learn: sys.query_store_plan & sys.query_store_runtime_stats [4, 5]",
                RuleClass: RuleClass.ClassD,
                RequiredEvidenceClass: EvidenceClass.EjecucionAgregada,
                BaseSeverity: FindingSeverity.Alta,
                Priority: RulePriority.Should,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-08")),
            new RuleDescriptor(
                RuleId: "REG-09",
                Name: "Desbordamiento a almacenamiento temporal y advertencias de memoria",
                Version: DefaultRuleVersion,
                Description: "Advertencias de desbordamiento presentes en un plan real; de no existir plan real, un uso elevado del espacio de almacenamiento temporal en las estadísticas agregadas se reporta únicamente como indicio.",
                DocumentationSource: "Microsoft Learn: sys.dm_exec_query_plan_stats & TempDB spill events in Showplan [7, 10]",
                RuleClass: RuleClass.ClassC,
                RequiredEvidenceClass: EvidenceClass.PlanReal,
                BaseSeverity: FindingSeverity.Media,
                Priority: RulePriority.Could,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-09")),
            new RuleDescriptor(
                RuleId: "REG-10",
                Name: "Sensibilidad a parámetros (indicio)",
                Version: DefaultRuleVersion,
                Description: "Variabilidad significativa de duración entre múltiples planes compilados de la misma consulta reportada como indicio no confirmado.",
                DocumentationSource: "Microsoft Learn: How Query Store collects data [3, 5]",
                RuleClass: RuleClass.ClassD,
                RequiredEvidenceClass: EvidenceClass.EjecucionAgregada,
                BaseSeverity: FindingSeverity.Baja,
                Priority: RulePriority.Could,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-10")),
            new RuleDescriptor(
                RuleId: "REG-11",
                Name: "Plan no analizable",
                Version: DefaultRuleVersion,
                Description: "Estructura XML nula, inválida, truncada o no reconocida por el analizador Showplan.",
                DocumentationSource: "Microsoft Learn: sys.dm_exec_query_plan_stats (XML nesting limit, F8) [7]",
                RuleClass: RuleClass.None,
                RequiredEvidenceClass: EvidenceClass.NoAplica,
                BaseSeverity: FindingSeverity.Informativa,
                Priority: RulePriority.Must,
                DefaultThresholds: defaults.GetThresholdsForRule("REG-11")),
        ];
    }
}
