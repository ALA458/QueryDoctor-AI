namespace QueryDoctor.Application.Telemetry;

/// <summary>
/// Contrato para el registro persistente o en memoria de sentencias ejecutadas (RF-TRZ-04, RNF-SEG-01).
/// Permite auditar que QueryStoreGateway únicamente emite sentencias SELECT de la lista blanca.
/// </summary>
public interface IStatementLogWriter
{
    /// <summary>
    /// Escribe una entrada de auditoría para una sentencia de telemetría ejecutada.
    /// </summary>
    /// <param name="entry">Detalles de la sentencia ejecutada.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task WriteAsync(StatementLogEntry entry, CancellationToken cancellationToken = default);
}
