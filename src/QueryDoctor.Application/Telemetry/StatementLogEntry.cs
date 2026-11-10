namespace QueryDoctor.Application.Telemetry;

/// <summary>
/// Registro de auditoría de una sentencia ejecutada sobre una base de datos objetivo (RF-TRZ-04, RNF-SEG-01).
/// Corresponde a la entidad statement_log del esquema de auditoría.
/// </summary>
public sealed class StatementLogEntry
{
    public StatementLogEntry(
        Guid statementLogId,
        string targetServer,
        string targetDatabase,
        string statementKey,
        string statementText,
        DateTime executedAtUtc,
        long durationMs,
        int rowsRead,
        bool success,
        string? errorMessage = null)
    {
        StatementLogId = statementLogId;
        TargetServer = targetServer ?? throw new ArgumentNullException(nameof(targetServer));
        TargetDatabase = targetDatabase ?? throw new ArgumentNullException(nameof(targetDatabase));
        StatementKey = statementKey ?? throw new ArgumentNullException(nameof(statementKey));
        StatementText = statementText ?? throw new ArgumentNullException(nameof(statementText));
        ExecutedAtUtc = executedAtUtc;
        DurationMs = durationMs;
        RowsRead = rowsRead;
        Success = success;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Identificador único del evento de auditoría.
    /// </summary>
    public Guid StatementLogId { get; }

    /// <summary>
    /// Servidor objetivo donde se ejecutó la consulta.
    /// </summary>
    public string TargetServer { get; }

    /// <summary>
    /// Base de datos donde se ejecutó la consulta.
    /// </summary>
    public string TargetDatabase { get; }

    /// <summary>
    /// Clave identificadora en la lista blanca de telemetría (ej. "DetectEngineVersion").
    /// </summary>
    public string StatementKey { get; }

    /// <summary>
    /// Texto SQL ejecutado. Únicamente sentencias SELECT están permitidas (CA-HU15.2).
    /// </summary>
    public string StatementText { get; }

    /// <summary>
    /// Fecha y hora en formato UTC del inicio de ejecución.
    /// </summary>
    public DateTime ExecutedAtUtc { get; }

    /// <summary>
    /// Duración total de la ejecución en milisegundos.
    /// </summary>
    public long DurationMs { get; }

    /// <summary>
    /// Número de filas leídas del cursor o DataReader.
    /// </summary>
    public int RowsRead { get; }

    /// <summary>
    /// Indica si la sentencia culminó con éxito.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Mensaje de error sanitizado en caso de falla. Nunca debe contener contraseñas ni cadenas de conexión.
    /// </summary>
    public string? ErrorMessage { get; }
}
