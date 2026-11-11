namespace QueryDoctor.Domain.Core;

/// <summary>
/// Una ejecución de la ingesta sobre una conexión (RF-ING-06, RF-ING-08). Tabla core.ingestion_run.
/// </summary>
public sealed class IngestionRun
{
    public Guid Id { get; set; }

    public Guid ConnectionId { get; set; }

    public IngestionTrigger Trigger { get; set; }

    public IngestionStatus Status { get; set; } = IngestionStatus.Running;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int? RowsUpserted { get; set; }

    /// <summary>Causa informada al usuario si se omitió o falló (RF-ING-08).</summary>
    public string? ErrorCause { get; set; }
}
