namespace QueryDoctor.Domain.Analysis;

/// <summary>
/// Entrada versionada del catálogo de reglas del motor determinístico (RF-MOT-15, E-06, Tabla 4.11). Tabla analysis.rule_catalog.
/// </summary>
public sealed class RuleCatalogEntry
{
    public Guid Id { get; set; }

    /// <summary>Identificador canónico de la regla (por ejemplo, REG-01..REG-11).</summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>Versión del catálogo al que pertenece la definición (por ejemplo, 1.0.0).</summary>
    public string CatalogVersion { get; set; } = string.Empty;

    /// <summary>Versión semántica de la regla individual (por ejemplo, 1.0.0).</summary>
    public string RuleVersion { get; set; } = string.Empty;

    /// <summary>Nombre descriptivo en español según la Tabla 4.11 del SRS.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Descripción técnica de la señal que activa la regla.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Fuente documental oficial de Microsoft o literatura técnica que sustenta la regla (RF-MOT-15).</summary>
    public string DocumentationSource { get; set; } = string.Empty;

    /// <summary>Clase técnica de evidencia del Apéndice A.4 (ClassA, ClassB, ClassC, ClassD, None).</summary>
    public RuleClass RuleClass { get; set; }

    /// <summary>Etiqueta de clase de evidencia requerida (Estimada, EjecucionAgregada, PlanReal, NoAplica).</summary>
    public EvidenceClass EvidenceClass { get; set; }

    /// <summary>Severidad base provisional propuesta en la Tabla 4.11 del SRS (DA-02).</summary>
    public FindingSeverity BaseSeverity { get; set; }

    /// <summary>Prioridad MoSCoW en el SRS (Must, Should, Could).</summary>
    public RulePriority Priority { get; set; }

    /// <summary>Umbrales por defecto de la regla en JSON canónico (Cuadro 4.5 del SRS, RF-MOT-14, RF-MOT-15).</summary>
    public string DefaultThresholdsJson { get; set; } = "{}";
}
