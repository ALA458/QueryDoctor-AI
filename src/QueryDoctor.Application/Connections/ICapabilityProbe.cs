using QueryDoctor.Application.Telemetry;

namespace QueryDoctor.Application.Connections;

/// <summary>SERVERPROPERTY: versión, edición y EngineEdition (RF-CFG-03).</summary>
public sealed record EngineInfo(string? ProductVersion, string? Edition, int EngineEdition);

/// <summary>Fila de sys.database_query_store_options (RF-CFG-04, CA-HU02.1).</summary>
public sealed record QueryStoreOptions(
    string? ActualState,
    string? DesiredState,
    int? ReadOnlyReason,
    int? IntervalLengthMinutes,
    string? CaptureMode,
    int? RetentionDays);

/// <summary>
/// Lecturas del sondeo de capacidades, cada una una sentencia de la lista blanca (S1-09, RNF-SEG-01).
/// Son pasos separados para que <see cref="ProbeConnection"/> decida el orden: si la versión es anterior a 2016
/// no se consulta Query Store (no existe) y el rechazo no queda tapado por otro error.
/// Los fallos llegan como <see cref="TelemetrySourceException"/>.
/// </summary>
public interface ICapabilityProbe
{
    Task<EngineInfo> GetEngineAsync(TargetDatabaseConnection target, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetDatabasePermissionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken);

    /// <summary>null si la vista no devolvió filas.</summary>
    Task<QueryStoreOptions?> GetQueryStoreOptionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetRuntimeStatsColumnsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken);
}

/// <summary>Configuración del sondeo (sección Probe).</summary>
public sealed class ProbeOptions
{
    public const string SectionName = "Probe";

    /// <summary>Tiempo límite de todo el sondeo, en segundos (RF-CFG-02; 15 por defecto).</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// SOLO LABORATORIO (invariante 3): servidores normalizados cuyo certificado autofirmado se acepta.
    /// Vacío por defecto; el Worker rechaza arrancar con valores fuera de Development (P9).
    /// </summary>
    public IReadOnlyList<string> LabTrustedCertificateServers { get; set; } = [];
}
