using System.Data;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Infrastructure.Connections;

namespace QueryDoctor.UnitTests.Connections;

// S1-09: CapabilityProbe solo usa claves de la lista blanca a través de ITelemetrySource (RNF-SEG-01).
public class CapabilityProbeTests
{
    private static readonly TargetDatabaseConnection Target = new("sqlserver", "QD_Lab", "qd_reader", "x");

    [Fact]
    public async Task LeeVersionEdicionYEngineEdition()
    {
        var telemetry = new TableTelemetrySource(Table(
            ("ProductVersion", typeof(string)), ("ProductLevel", typeof(string)), ("Edition", typeof(string)), ("EngineEdition", typeof(int)),
            new object[] { "16.0.4315.2", "RTM", "Developer Edition (64-bit)", 3 }));

        var engine = await new CapabilityProbe(telemetry).GetEngineAsync(Target, default);

        Assert.Equal(new("16.0.4315.2", "Developer Edition (64-bit)", 3), engine);
        Assert.Equal([TelemetryStatementWhitelist.Keys.DetectEngineVersion], telemetry.Keys);
    }

    [Fact]
    public async Task LeeLasOpcionesDeQueryStoreConTiposDeCadaVersion()
    {
        // stale_query_threshold_days es bigint y actual_state smallint en SQL Server: se normalizan a int.
        var telemetry = new TableTelemetrySource(Table(
            ("actual_state", typeof(short)), ("actual_state_desc", typeof(string)), ("desired_state_desc", typeof(string)),
            ("readonly_reason", typeof(int)), ("interval_length_minutes", typeof(long)), ("stale_query_threshold_days", typeof(long)),
            ("current_storage_size_mb", typeof(long)), ("max_storage_size_mb", typeof(long)),
            ("query_capture_mode_desc", typeof(string)), ("size_based_cleanup_mode_desc", typeof(string)),
            new object[] { (short)2, "READ_WRITE", "READ_WRITE", 0, 1L, 30L, 1L, 512L, "ALL", "AUTO" }));

        var options = await new CapabilityProbe(telemetry).GetQueryStoreOptionsAsync(Target, default);

        Assert.Equal(new("READ_WRITE", "READ_WRITE", 0, 1, "ALL", 30), options);
        Assert.Equal([TelemetryStatementWhitelist.Keys.GetQueryStoreOptions], telemetry.Keys);
    }

    [Fact]
    public async Task SinFilasDeQueryStoreDevuelveNull()
    {
        var telemetry = new TableTelemetrySource(Table(("actual_state_desc", typeof(string))));

        Assert.Null(await new CapabilityProbe(telemetry).GetQueryStoreOptionsAsync(Target, default));
    }

    [Fact]
    public async Task PideLasColumnasDeRuntimeStatsConParametroYNoConSqlDinamico()
    {
        var telemetry = new TableTelemetrySource(Table(("column_name", typeof(string)), new object[] { "plan_id" }, new object[] { "avg_tempdb_space_used" }));

        var columns = await new CapabilityProbe(telemetry).GetRuntimeStatsColumnsAsync(Target, default);

        Assert.Equal(["plan_id", "avg_tempdb_space_used"], columns);
        Assert.Equal([TelemetryStatementWhitelist.Keys.GetQueryStoreViewColumns], telemetry.Keys);
        Assert.Equal("query_store_runtime_stats", telemetry.LastParameters!["ViewName"]);
    }

    [Fact]
    public async Task LeeLosPermisosEfectivos()
    {
        var telemetry = new TableTelemetrySource(Table(("permission_name", typeof(string)), new object[] { "CONNECT" }, new object[] { "VIEW DATABASE STATE" }));

        var permissions = await new CapabilityProbe(telemetry).GetDatabasePermissionsAsync(Target, default);

        Assert.Equal(["CONNECT", "VIEW DATABASE STATE"], permissions);
        Assert.Equal([TelemetryStatementWhitelist.Keys.CheckUserPermissions], telemetry.Keys);
    }

    private static DataTable Table(params object[] columnsThenRows)
    {
        var table = new DataTable();
        foreach (var item in columnsThenRows)
        {
            switch (item)
            {
                case ValueTuple<string, Type> column:
                    table.Columns.Add(column.Item1, column.Item2);
                    break;
                case object[] row:
                    table.Rows.Add(row);
                    break;
            }
        }

        return table;
    }

    private sealed class TableTelemetrySource(DataTable table) : ITelemetrySource
    {
        public List<string> Keys { get; } = [];

        public IDictionary<string, object?>? LastParameters { get; private set; }

        public Task<bool> TestConnectionAsync(TargetDatabaseConnection connection, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            TargetDatabaseConnection connection,
            string statementKey,
            Func<IDataRecord, TResult> mapRecord,
            IDictionary<string, object?>? parameters = null,
            CancellationToken cancellationToken = default)
        {
            TelemetryStatementWhitelist.GetSql(statementKey); // misma comprobación que el Gateway
            Keys.Add(statementKey);
            LastParameters = parameters;

            var results = new List<TResult>();
            using var reader = table.CreateDataReader();
            while (reader.Read())
            {
                results.Add(mapRecord(reader));
            }

            return Task.FromResult<IReadOnlyList<TResult>>(results);
        }

        public Task<TResult?> ExecuteScalarAsync<TResult>(
            TargetDatabaseConnection connection,
            string statementKey,
            IDictionary<string, object?>? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
