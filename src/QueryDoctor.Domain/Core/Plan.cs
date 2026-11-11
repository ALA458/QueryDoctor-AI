namespace QueryDoctor.Domain.Core;

/// <summary>
/// Plan de ejecución guardado por Query Store (RF-ING-01, RF-ING-09). Tabla core.plan.
/// </summary>
public sealed class Plan
{
    public Guid Id { get; set; }

    public Guid ConnectionId { get; set; }

    public Guid QueryId { get; set; }

    /// <summary>plan_id en Query Store.</summary>
    public long QueryStorePlanId { get; set; }

    /// <summary>query_plan_hash de Query Store en hexadecimal.</summary>
    public string? QueryPlanHash { get; set; }

    /// <summary>
    /// Showplan XML YA cifrado con IFieldEncryptor (RNF-SEG-14). Se lee como texto, nunca como xml (hallazgo F8).
    /// Se pone en null al purgar por retención o si el plan no se pudo recuperar.
    /// </summary>
    public string? EncryptedPlanXml { get; set; }

    public string? EngineVersion { get; set; }

    public PlanAnalysisStatus AnalysisStatus { get; set; } = PlanAnalysisStatus.Pending;

    /// <summary>Causa cuando el plan es no analizable (RF-ING-09, REG-11).</summary>
    public string? NotAnalyzableCause { get; set; }

    public DateTimeOffset IngestedAt { get; set; }
}
