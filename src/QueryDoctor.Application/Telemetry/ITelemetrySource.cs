using System.Data;

namespace QueryDoctor.Application.Telemetry;

/// <summary>
/// Abstracción del proveedor de telemetría hacia bases de datos relacionales monitoreadas (RNF-FLE-02, P1).
/// Permite extender el soporte hacia otros motores sin acoplar la aplicación a un cliente específico.
/// </summary>
public interface ITelemetrySource
{
    /// <summary>
    /// Verifica conectividad, cifrado TLS y credenciales hacia la base de datos objetivo.
    /// </summary>
    /// <param name="connection">Parámetros de conexión.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>True si la conexión fue exitosa y cumple los requisitos de seguridad.</returns>
    Task<bool> TestConnectionAsync(
        TargetDatabaseConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta una consulta SELECT preaprobada de la lista blanca y proyecta sus resultados (RNF-SEG-01, RF-TRZ-04).
    /// </summary>
    /// <typeparam name="TResult">Tipo del objeto de resultado proyectado.</typeparam>
    /// <param name="connection">Parámetros de conexión.</param>
    /// <param name="statementKey">Clave de la consulta en la lista blanca.</param>
    /// <param name="mapRecord">Función de mapeo para cada registro del lector.</param>
    /// <param name="parameters">Parámetros de la consulta (valores escalares, sin SQL dinámico).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Lista inmutable de elementos proyectados.</returns>
    Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        TargetDatabaseConnection connection,
        string statementKey,
        Func<IDataRecord, TResult> mapRecord,
        IDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta una consulta SELECT preaprobada de la lista blanca y retorna un único valor escalar (RNF-SEG-01, RF-TRZ-04).
    /// </summary>
    /// <typeparam name="TResult">Tipo esperado del valor escalar.</typeparam>
    /// <param name="connection">Parámetros de conexión.</param>
    /// <param name="statementKey">Clave de la consulta en la lista blanca.</param>
    /// <param name="parameters">Parámetros de la consulta.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Valor escalar obtenido o default si es nulo o vacío.</returns>
    Task<TResult?> ExecuteScalarAsync<TResult>(
        TargetDatabaseConnection connection,
        string statementKey,
        IDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);
}
