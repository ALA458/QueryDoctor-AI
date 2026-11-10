using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QueryDoctor.Application.Telemetry;

namespace QueryDoctor.Infrastructure.Telemetry;

/// <summary>
/// Implementación en memoria y con registro estructurado de <see cref="IStatementLogWriter"/> (RF-TRZ-04, RNF-SEG-01).
/// Utilizada para pruebas, desarrollo y como fallback antes de la migración del esquema de auditoría en PostgreSQL.
/// </summary>
public sealed partial class InMemoryStatementLogWriter : IStatementLogWriter
{
    private readonly ConcurrentQueue<StatementLogEntry> _entries = new();
    private readonly ILogger<InMemoryStatementLogWriter>? _logger;

    public InMemoryStatementLogWriter(ILogger<InMemoryStatementLogWriter>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task WriteAsync(StatementLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entries.Enqueue(entry);

        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
        {
            LogStatementAudit(
                _logger,
                entry.TargetServer,
                entry.TargetDatabase,
                entry.StatementKey,
                entry.DurationMs,
                entry.RowsRead,
                entry.Success);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retorna una copia instantánea de todas las sentencias registradas en memoria.
    /// </summary>
    public IReadOnlyList<StatementLogEntry> GetEntries() =>
        _entries.ToArray();

    /// <summary>
    /// Limpia el almacén en memoria de entradas de auditoría.
    /// </summary>
    public void Clear() =>
        _entries.Clear();

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "StatementLog: [Target={TargetServer}/{TargetDatabase}] [Key={Key}] [Duration={Duration}ms] [Rows={Rows}] [Success={Success}]")]
    private static partial void LogStatementAudit(
        ILogger logger,
        string targetServer,
        string targetDatabase,
        string key,
        long duration,
        int rows,
        bool success);
}
