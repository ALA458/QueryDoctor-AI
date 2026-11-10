using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using QueryDoctor.Application.Telemetry;

namespace QueryDoctor.Infrastructure.Telemetry;

/// <summary>
/// Pasarela de telemetría hacia SQL Server y Azure SQL objetivo (S1-08, RNF-SEG-01, RNF-SEG-03, RF-TRZ-04, HU-15).
/// Ejecuta exclusivamente consultas de solo lectura de la lista blanca con TLS estricto, LOCK_TIMEOUT corto y registro auditable.
/// </summary>
public sealed partial class QueryStoreGateway : ITelemetrySource
{
    private readonly IStatementLogWriter _statementLogWriter;
    private readonly ILogger<QueryStoreGateway>? _logger;

    public QueryStoreGateway(
        IStatementLogWriter statementLogWriter,
        ILogger<QueryStoreGateway>? logger = null)
    {
        _statementLogWriter = statementLogWriter ?? throw new ArgumentNullException(nameof(statementLogWriter));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync(
        TargetDatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        ValidateSecurityPolicies(connection);

        var connectionString = BuildConnectionString(connection);

        await using var sqlConnection = new SqlConnection(connectionString);
        await sqlConnection.OpenAsync(cancellationToken);

        // Validar que podemos aplicar las políticas de sesión de solo lectura
        await using var sessionCmd = sqlConnection.CreateCommand();
        sessionCmd.CommandText = BuildSessionSetupSql(connection.LockTimeoutMs);
        await sessionCmd.ExecuteNonQueryAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        TargetDatabaseConnection connection,
        string statementKey,
        Func<IDataRecord, TResult> mapRecord,
        IDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(statementKey);
        ArgumentNullException.ThrowIfNull(mapRecord);

        ValidateSecurityPolicies(connection);

        // Obtener y validar que la consulta esté en la lista blanca estricta (RNF-SEG-01)
        var sqlText = TelemetryStatementWhitelist.GetSql(statementKey);
        TelemetryStatementWhitelist.ValidateStatementOrThrow(statementKey, sqlText);

        var executedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var rowsRead = 0;
        var success = false;
        string? errorMessage = null;
        var results = new List<TResult>();

        var connectionString = BuildConnectionString(connection);

        try
        {
            await using var sqlConnection = new SqlConnection(connectionString);
            await sqlConnection.OpenAsync(cancellationToken);

            // Aplicar parámetros de sesión preventivos (P1, RNF-SEG-01)
            await using (var sessionCmd = sqlConnection.CreateCommand())
            {
                sessionCmd.CommandText = BuildSessionSetupSql(connection.LockTimeoutMs);
                await sessionCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var cmd = sqlConnection.CreateCommand())
            {
                cmd.CommandText = sqlText;
                cmd.CommandTimeout = connection.ConnectionTimeoutSeconds;

                if (parameters != null)
                {
                    foreach (var (paramName, paramValue) in parameters)
                    {
                        var formattedName = paramName.StartsWith('@') ? paramName : "@" + paramName;
                        cmd.Parameters.AddWithValue(formattedName, paramValue ?? DBNull.Value);
                    }
                }

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rowsRead++;
                    results.Add(mapRecord(reader));
                }
            }

            success = true;
            return results;
        }
        catch (Exception ex)
        {
            errorMessage = SanitizeError(ex.Message, connection.Password);
            if (_logger != null)
            {
                LogTelemetryExecutionFailed(_logger, ex, statementKey, connection.TargetDescriptor, errorMessage);
            }

            throw;
        }
        finally
        {
            stopwatch.Stop();

            // Auditoría obligatoria en statement_log (RF-TRZ-04, CA-HU15.2)
            var logEntry = new StatementLogEntry(
                Guid.NewGuid(),
                connection.Server,
                connection.Database,
                statementKey,
                sqlText,
                executedAtUtc,
                stopwatch.ElapsedMilliseconds,
                rowsRead,
                success,
                errorMessage);

            try
            {
                await _statementLogWriter.WriteAsync(logEntry, cancellationToken);
            }
            catch (Exception auditEx)
            {
                if (_logger != null)
                {
                    LogAuditWriteFailed(_logger, auditEx, statementKey);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<TResult?> ExecuteScalarAsync<TResult>(
        TargetDatabaseConnection connection,
        string statementKey,
        IDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var records = await ExecuteReaderAsync(
            connection,
            statementKey,
            record =>
            {
                if (record.FieldCount == 0 || record.IsDBNull(0))
                {
                    return default;
                }

                var value = record.GetValue(0);
                return (TResult)Convert.ChangeType(value, typeof(TResult), CultureInfo.InvariantCulture);
            },
            parameters,
            cancellationToken);

        return records.Count > 0 ? records[0] : default;
    }

    /// <summary>
    /// Construye y valida la cadena de conexión garantizando TLS obligatorio y deshabilitando certificados no confiables (RNF-SEG-03).
    /// </summary>
    public static string BuildConnectionString(TargetDatabaseConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        ValidateSecurityPolicies(connection);

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = connection.Server,
            InitialCatalog = connection.Database,
            ConnectTimeout = connection.ConnectionTimeoutSeconds,
            ApplicationName = "QueryDoctor-AI Telemetry Gateway",
            TrustServerCertificate = connection.TrustServerCertificate
        };

        if (!string.IsNullOrWhiteSpace(connection.UserId))
        {
            builder.UserID = connection.UserId;
            builder.Password = connection.Password ?? string.Empty;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        builder.Encrypt = connection.EncryptionMode switch
        {
            TargetEncryptionMode.Strict => SqlConnectionEncryptOption.Strict,
            _ => SqlConnectionEncryptOption.Mandatory
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Valida que la conexión cumpla las políticas estrictas de TLS y prevención de certificados no confiables (RNF-SEG-03).
    /// </summary>
    public static void ValidateSecurityPolicies(TargetDatabaseConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // RNF-SEG-03: Rechazo estricto de TrustServerCertificate=true a menos que sea entorno de prueba local autorizado
        if (connection.TrustServerCertificate && !connection.AllowLocalTestCertificates)
        {
            throw new SecurityException(
                "Conexión rechazada: TrustServerCertificate=true está estrictamente prohibido en producción (RNF-SEG-03). " +
                "Configure un certificado TLS válido o habilite AllowLocalTestCertificates únicamente para entornos locales de pruebas.");
        }
    }

    private static string BuildSessionSetupSql(int lockTimeoutMs) =>
        $"SET LOCK_TIMEOUT {lockTimeoutMs}; SET DEADLOCK_PRIORITY LOW;";

    private static string SanitizeError(string rawError, string? secret)
    {
        if (string.IsNullOrEmpty(rawError))
        {
            return string.Empty;
        }

        return !string.IsNullOrEmpty(secret)
            ? rawError.Replace(secret, "********", StringComparison.Ordinal)
            : rawError;
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Error ejecutando consulta de telemetría '{StatementKey}' en '{Target}': {Message}")]
    private static partial void LogTelemetryExecutionFailed(
        ILogger logger,
        Exception ex,
        string statementKey,
        string target,
        string? message);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Error,
        Message = "Falla al registrar auditoría en statement_log para '{StatementKey}'.")]
    private static partial void LogAuditWriteFailed(
        ILogger logger,
        Exception ex,
        string statementKey);
}
