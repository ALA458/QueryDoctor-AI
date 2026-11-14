namespace QueryDoctor.Domain.Analysis;

/// <summary>
/// Ejecución del motor determinístico sobre un plan de ejecución (S1-13, RF-MOT-02, RF-MOT-14, Arquitectura §6.3). Tabla analysis.analysis_run.
/// </summary>
public sealed class AnalysisRun
{
    public Guid Id { get; set; }

    public Guid ConnectionId { get; set; }

    public Guid QueryId { get; set; }

    public Guid PlanId { get; set; }

    public Guid ThresholdSetId { get; set; }

    /// <summary>Versión del catálogo de reglas empleada en este análisis (RF-MOT-02, RF-MOT-15).</summary>
    public string CatalogVersion { get; set; } = string.Empty;

    /// <summary>Versión del motor SQL Server con la que se compiló el plan analizado (RNF-COM-02).</summary>
    public string? EngineVersion { get; set; }

    /// <summary>Instantánea en JSON canónico de los valores de umbral utilizados en este análisis (RF-MOT-14).</summary>
    public string ThresholdsUsedJson { get; set; } = "{}";

    public AnalysisRunStatus Status { get; set; } = AnalysisRunStatus.Running;

    public int FindingsCount { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Causa de error si la ejecución falló (sin texto SQL ni literales, RNF-SEG-09).</summary>
    public string? ErrorCause { get; set; }
}
