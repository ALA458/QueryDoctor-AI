namespace QueryDoctor.Domain.Core;

/// <summary>
/// Resultado del último sondeo de capacidades de una conexión (RF-CFG-02 a RF-CFG-07).
/// Una fila por conexión; se sobrescribe en cada sondeo. Tabla core.capability_profile.
/// </summary>
public sealed class CapabilityProfile
{
    public Guid Id { get; set; }

    public Guid ConnectionId { get; set; }

    public string EngineVersion { get; set; } = string.Empty;

    public string? Edition { get; set; }

    public SupportLevel SupportLevel { get; set; }

    public bool HasViewDatabaseState { get; set; }

    /// <summary>La cuenta tiene permisos de escritura y se debe advertir (RF-CFG-05).</summary>
    public bool HasExcessivePermissions { get; set; }

    public string? QueryStoreActualState { get; set; }

    public string? QueryStoreDesiredState { get; set; }

    public string? QueryStoreReadOnlyReason { get; set; }

    public int? IntervalLengthMinutes { get; set; }

    public string? CaptureMode { get; set; }

    public int? RetentionDays { get; set; }

    /// <summary>Funciones omitidas por la versión detectada, como JSON de texto (RF-CFG-07).</summary>
    public string? OmittedFeaturesJson { get; set; }

    public DateTimeOffset ProbedAt { get; set; }
}
