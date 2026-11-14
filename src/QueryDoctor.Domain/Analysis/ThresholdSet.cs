namespace QueryDoctor.Domain.Analysis;

/// <summary>
/// Conjunto inmutable de umbrales del motor determinístico registrado para reproducibilidad (RF-MOT-02, RF-MOT-14). Tabla analysis.threshold_set.
/// </summary>
public sealed class ThresholdSet
{
    public Guid Id { get; set; }

    /// <summary>Versión del catálogo de reglas asociada a este conjunto de umbrales.</summary>
    public string CatalogVersion { get; set; } = string.Empty;

    /// <summary>Hash SHA-256 en hexadecimal del JSON canónico de umbrales para deduplicar e identificar configuraciones idénticas.</summary>
    public string ConfigurationHash { get; set; } = string.Empty;

    /// <summary>Valores de umbral serializados en JSON canónico (Cuadro 4.5 del SRS).</summary>
    public string ThresholdsJson { get; set; } = "{}";

    /// <summary>Indica si corresponde al conjunto de umbrales por defecto del catálogo.</summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
