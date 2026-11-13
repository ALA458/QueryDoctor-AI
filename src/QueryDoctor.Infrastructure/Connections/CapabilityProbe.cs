using System.Data;
using System.Globalization;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Telemetry;

namespace QueryDoctor.Infrastructure.Connections;

/// <summary>
/// Sondeo de capacidades sobre <see cref="ITelemetrySource"/> (S1-09). No usa SqlClient: cada lectura es una
/// clave de <see cref="TelemetryStatementWhitelist"/>, así que solo emite SELECT y queda en statement_log.
/// </summary>
public sealed class CapabilityProbe : ICapabilityProbe
{
    /// <summary>Vista cuyas columnas deciden las funciones omitidas (RF-ING-03).</summary>
    public const string RuntimeStatsView = "query_store_runtime_stats";

    private readonly ITelemetrySource _telemetry;

    public CapabilityProbe(ITelemetrySource telemetry)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public async Task<EngineInfo> GetEngineAsync(TargetDatabaseConnection target, CancellationToken cancellationToken)
    {
        var rows = await _telemetry.ExecuteReaderAsync(
            target,
            TelemetryStatementWhitelist.Keys.DetectEngineVersion,
            r => new EngineInfo(
                GetString(r, "ProductVersion"),
                GetString(r, "Edition"),
                GetInt(r, "EngineEdition") ?? 0),
            cancellationToken: cancellationToken);

        return rows.Count > 0 ? rows[0] : new EngineInfo(null, null, 0);
    }

    public async Task<IReadOnlyList<string>> GetDatabasePermissionsAsync(
        TargetDatabaseConnection target,
        CancellationToken cancellationToken)
    {
        var rows = await _telemetry.ExecuteReaderAsync(
            target,
            TelemetryStatementWhitelist.Keys.CheckUserPermissions,
            r => GetString(r, "permission_name") ?? string.Empty,
            cancellationToken: cancellationToken);

        return rows.Where(p => p.Length > 0).ToList();
    }

    public async Task<QueryStoreOptions?> GetQueryStoreOptionsAsync(
        TargetDatabaseConnection target,
        CancellationToken cancellationToken)
    {
        var rows = await _telemetry.ExecuteReaderAsync(
            target,
            TelemetryStatementWhitelist.Keys.GetQueryStoreOptions,
            r => new QueryStoreOptions(
                GetString(r, "actual_state_desc"),
                GetString(r, "desired_state_desc"),
                GetInt(r, "readonly_reason"),
                GetInt(r, "interval_length_minutes"),
                GetString(r, "query_capture_mode_desc"),
                GetInt(r, "stale_query_threshold_days")),
            cancellationToken: cancellationToken);

        return rows.Count > 0 ? rows[0] : null;
    }

    public Task<IReadOnlyList<string>> GetRuntimeStatsColumnsAsync(
        TargetDatabaseConnection target,
        CancellationToken cancellationToken) =>
        _telemetry.ExecuteReaderAsync(
            target,
            TelemetryStatementWhitelist.Keys.GetQueryStoreViewColumns,
            r => GetString(r, "column_name") ?? string.Empty,
            new Dictionary<string, object?> { ["ViewName"] = RuntimeStatsView },
            cancellationToken);

    private static string? GetString(IDataRecord record, string column)
    {
        var ordinal = record.GetOrdinal(column);
        return record.IsDBNull(ordinal) ? null : Convert.ToString(record.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    // smallint, int o bigint según la versión: se normaliza a int.
    private static int? GetInt(IDataRecord record, string column)
    {
        var ordinal = record.GetOrdinal(column);
        return record.IsDBNull(ordinal) ? null : Convert.ToInt32(record.GetValue(ordinal), CultureInfo.InvariantCulture);
    }
}
