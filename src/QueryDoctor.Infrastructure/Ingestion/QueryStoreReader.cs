using System.Data;
using System.Globalization;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Telemetry;

namespace QueryDoctor.Infrastructure.Ingestion;

/// <summary>
/// Lecturas de la ingesta sobre <see cref="ITelemetrySource"/> (S1-11). No usa SqlClient: cada lectura es una clave
/// de <see cref="TelemetryStatementWhitelist"/>, así que solo emite SELECT y queda en statement_log (RNF-SEG-01).
/// </summary>
public sealed class QueryStoreReader : IQueryStoreReader
{
    private const int OriginContextMaxLength = 512;

    private readonly ITelemetrySource _telemetry;
    private readonly ICapabilityProbe _probe;

    public QueryStoreReader(ITelemetrySource telemetry, ICapabilityProbe probe)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    // Misma sentencia y mismo mapeo que el sondeo (GetQueryStoreOptions).
    public Task<QueryStoreOptions?> GetOptionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
        _probe.GetQueryStoreOptionsAsync(target, cancellationToken);

    public async Task<QueryStoreReadResult> ReadStatsAsync(
        TargetDatabaseConnection target,
        QueryStoreReadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = request.IncludeTempDbAndLog
            ? TelemetryStatementWhitelist.Keys.GetIngestionRuntimeStats
            : TelemetryStatementWhitelist.Keys.GetIngestionRuntimeStatsWithoutTempDbLog;

        var rows = await _telemetry.ExecuteReaderAsync(
            target,
            key,
            MapStatRow,
            new Dictionary<string, object?>
            {
                ["TopCount"] = request.TopCount,
                ["AfterIntervalId"] = request.AfterIntervalId,
                ["MinStartTime"] = request.MinStartTime,
            },
            cancellationToken);

        var known = rows.OfType<QueryStoreStatRow>().ToList();
        return new QueryStoreReadResult(known, rows.Count - known.Count);
    }

    public async Task<QueryStoreDocument> GetQueryTextAsync(TargetDatabaseConnection target, long queryStoreTextId, CancellationToken cancellationToken)
    {
        var rows = await _telemetry.ExecuteReaderAsync(
            target,
            TelemetryStatementWhitelist.Keys.GetQueryTextById,
            r => GetString(r, "query_sql_text"),
            new Dictionary<string, object?> { ["QueryTextId"] = queryStoreTextId },
            cancellationToken);

        return rows.Count > 0 ? new QueryStoreDocument(true, rows[0]) : new QueryStoreDocument(false, null);
    }

    public async Task<QueryStoreDocument> GetPlanXmlAsync(TargetDatabaseConnection target, long queryStorePlanId, CancellationToken cancellationToken)
    {
        var rows = await _telemetry.ExecuteReaderAsync(
            target,
            TelemetryStatementWhitelist.Keys.GetPlanXmlByPlanId,
            r => GetString(r, "query_plan"),
            new Dictionary<string, object?> { ["PlanId"] = queryStorePlanId },
            cancellationToken);

        return rows.Count > 0 ? new QueryStoreDocument(true, rows[0]) : new QueryStoreDocument(false, null);
    }

    /// <summary>Una fila de la sentencia de ingesta; null si execution_type no es uno de los documentados.</summary>
    public static QueryStoreStatRow? MapStatRow(IDataRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);

        if (QueryStoreExecutionType.FromCode(GetInt(r, "execution_type")) is not { } executionType)
        {
            return null;
        }

        var stat = new RuntimeStatRow(
            GetLong(r, "plan_id"),
            executionType,
            GetLong(r, "runtime_stats_interval_id"),
            GetDateTimeOffset(r, "start_time"),
            GetDateTimeOffset(r, "end_time"),
            GetInt(r, "is_closed") == 1,
            new RuntimeMetrics(
                GetLong(r, "count_executions"),
                GetDouble(r, "avg_duration"),
                GetDouble(r, "avg_cpu_time"),
                GetDouble(r, "avg_logical_io_reads"),
                GetDouble(r, "avg_physical_io_reads"),
                GetDouble(r, "avg_logical_io_writes"),
                GetDouble(r, "avg_rowcount"),
                GetDouble(r, "avg_dop"),
                GetDouble(r, "avg_query_max_used_memory"),
                GetNullableDouble(r, "avg_tempdb_space_used"),
                GetNullableDouble(r, "avg_log_bytes_used")));

        return new QueryStoreStatRow(
            GetLong(r, "query_id"),
            GetLong(r, "query_text_id"),
            GetHex(r, "query_hash"),
            GetHex(r, "query_plan_hash"),
            GetString(r, "engine_version"),
            OriginContext(GetString(r, "object_schema"), GetString(r, "object_name")),
            stat);
    }

    // Nombre del objeto que contiene la consulta (procedimiento, función...). Ad hoc o SQL dinámico: null. Con la cuenta
    // de mínimo privilegio (solo VIEW DATABASE STATE, RNF-SEG-02) SQL Server oculta los metadatos y OBJECT_NAME devuelve
    // NULL: se acepta (medido en el laboratorio, S1-11) en vez de pedir VIEW DEFINITION. DA-13: nombres sin seudonimizar.
    private static string? OriginContext(string? schema, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var value = string.IsNullOrEmpty(schema) ? name : schema + "." + name;
        return value.Length > OriginContextMaxLength ? value[..OriginContextMaxLength] : value;
    }

    private static string? GetString(IDataRecord record, string column)
    {
        var ordinal = record.GetOrdinal(column);
        return record.IsDBNull(ordinal) ? null : Convert.ToString(record.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    // Columnas NOT NULL en Query Store: un nulo aquí es un dato inesperado y hace fallar la ingesta (no se inventa 0).
    private static long GetLong(IDataRecord record, string column) =>
        Convert.ToInt64(record.GetValue(record.GetOrdinal(column)), CultureInfo.InvariantCulture);

    private static int GetInt(IDataRecord record, string column) =>
        Convert.ToInt32(record.GetValue(record.GetOrdinal(column)), CultureInfo.InvariantCulture);

    private static double GetDouble(IDataRecord record, string column) =>
        Convert.ToDouble(record.GetValue(record.GetOrdinal(column)), CultureInfo.InvariantCulture);

    private static double? GetNullableDouble(IDataRecord record, string column)
    {
        var ordinal = record.GetOrdinal(column);
        return record.IsDBNull(ordinal) ? null : Convert.ToDouble(record.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset GetDateTimeOffset(IDataRecord record, string column) => record.GetValue(record.GetOrdinal(column)) switch
    {
        DateTimeOffset value => value,
        DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
        var other => throw new InvalidCastException($"Tipo inesperado en {column}: {other.GetType().Name}."),
    };

    // binary(8) de Query Store → hexadecimal en mayúsculas sin "0x" (varchar(64) en core).
    private static string? GetHex(IDataRecord record, string column)
    {
        var ordinal = record.GetOrdinal(column);
        return record.IsDBNull(ordinal) || record.GetValue(ordinal) is not byte[] bytes ? null : Convert.ToHexString(bytes);
    }
}
