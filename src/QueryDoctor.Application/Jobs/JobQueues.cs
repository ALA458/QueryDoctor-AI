namespace QueryDoctor.Application.Jobs;

/// <summary>
/// Nombres y prioridades de las colas de trabajos en segundo plano procesadas por Hangfire (ADR-04, DA-03).
/// </summary>
public static class JobQueues
{
    /// <summary>Detección de versión, capacidades y permisos de la base objetivo (S1-09).</summary>
    public const string Probe = "probe";

    /// <summary>Ingesta incremental de consultas, planes y estadísticas de Query Store (S1-11).</summary>
    public const string Ingest = "ingest";

    /// <summary>Ejecución del motor de reglas deterministas (S1-13).</summary>
    public const string Analyze = "analyze";

    /// <summary>Generación de explicaciones mediante el servicio de IA (S2-05).</summary>
    public const string Explain = "explain";

    /// <summary>Purga y retención periódica de planes y consultas expiradas (S2-13).</summary>
    public const string Retention = "retention";

    /// <summary>Cola por defecto para tareas auxiliares o de prueba.</summary>
    public const string Default = "default";

    /// <summary>
    /// Colas en orden estricto de prioridad de ejecución para el servidor Worker (DA-03, §5.3).
    /// </summary>
    public static readonly string[] AllInPriorityOrder =
    [
        Probe,
        Ingest,
        Analyze,
        Explain,
        Retention,
        Default
    ];
}
