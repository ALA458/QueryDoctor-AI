using System.Security;
using QueryDoctor.Application.Telemetry;

namespace QueryDoctor.UnitTests.Telemetry;

public class TelemetryStatementWhitelistTests
{
    [Fact]
    public void AllRegisteredStatementsStartWithSelect()
    {
        var registeredKeys = new[]
        {
            TelemetryStatementWhitelist.Keys.DetectEngineVersion,
            TelemetryStatementWhitelist.Keys.GetQueryStoreOptions,
            TelemetryStatementWhitelist.Keys.CheckUserPermissions,
            TelemetryStatementWhitelist.Keys.GetTopQueriesByTotalDuration,
            TelemetryStatementWhitelist.Keys.GetPlanXmlByPlanId,
            TelemetryStatementWhitelist.Keys.GetQueryStoreViewColumns,
            TelemetryStatementWhitelist.Keys.GetIngestionRuntimeStats,
            TelemetryStatementWhitelist.Keys.GetIngestionRuntimeStatsWithoutTempDbLog,
            TelemetryStatementWhitelist.Keys.GetQueryTextById
        };

        foreach (var key in registeredKeys)
        {
            Assert.True(TelemetryStatementWhitelist.ContainsKey(key), $"La clave '{key}' debería estar registrada.");

            var sql = TelemetryStatementWhitelist.GetSql(key);
            Assert.False(string.IsNullOrWhiteSpace(sql), $"El SQL para '{key}' no puede estar vacío.");

            var trimmed = sql.TrimStart();
            Assert.True(
                trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase),
                $"La consulta para '{key}' debe iniciar con SELECT o WITH.");

            // Validar que pase la regla estricta de solo lectura (CA-HU15.2)
            TelemetryStatementWhitelist.EnsureReadOnlySelectStatement(sql);
        }
    }

    [Theory]
    [InlineData("actual_state_desc")]
    [InlineData("desired_state_desc")]
    [InlineData("readonly_reason")]
    [InlineData("interval_length_minutes")]
    [InlineData("query_capture_mode_desc")]
    [InlineData("stale_query_threshold_days")]
    public void QueryStoreOptionsReadsEveryFieldRequiredByRfCfg04(string column)
    {
        // RF-CFG-04 / CA-HU02.1: estado actual y deseado, motivo de solo lectura, intervalo, modo de captura y retención.
        Assert.Contains(column, TelemetryStatementWhitelist.GetQueryStoreOptionsSql, StringComparison.Ordinal);
    }

    public static TheoryData<string> IngestionStatementKeys =>
    [
        TelemetryStatementWhitelist.Keys.GetIngestionRuntimeStats,
        TelemetryStatementWhitelist.Keys.GetIngestionRuntimeStatsWithoutTempDbLog,
        TelemetryStatementWhitelist.Keys.GetQueryTextById,
    ];

    [Theory]
    [MemberData(nameof(IngestionStatementKeys))]
    public void IngestionStatementsStartWithSelectAndNotWith(string key)
    {
        // S1-11: statement_log de la ingesta solo muestra SELECT (CA-HU15.2); por eso el Top-N es subconsulta, no CTE.
        var sql = TelemetryStatementWhitelist.GetSql(key);

        Assert.StartsWith("SELECT ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH ", sql, StringComparison.OrdinalIgnoreCase);
        TelemetryStatementWhitelist.ValidateStatementOrThrow(key, sql);
    }

    [Theory]
    [InlineData("rs.count_executions")]
    [InlineData("rs.avg_duration")]
    [InlineData("rs.avg_cpu_time")]
    [InlineData("rs.avg_logical_io_reads")]
    [InlineData("rs.avg_physical_io_reads")]
    [InlineData("rs.avg_logical_io_writes")]
    [InlineData("rs.avg_rowcount")]
    [InlineData("rs.avg_dop")]
    [InlineData("rs.avg_query_max_used_memory")]
    [InlineData("rs.execution_type")]
    [InlineData("rs.runtime_stats_interval_id")]
    [InlineData("i.start_time")]
    [InlineData("i.end_time")]
    public void IngestionStatementsReadEveryMetricOfRfIng02(string column)
    {
        // RF-ING-02: las dos variantes leen las mismas métricas obligatorias por plan, tipo de ejecución e intervalo.
        Assert.Contains(column, TelemetryStatementWhitelist.GetIngestionRuntimeStatsSql, StringComparison.Ordinal);
        Assert.Contains(column, TelemetryStatementWhitelist.GetIngestionRuntimeStatsWithoutTempDbLogSql, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheFullIngestionStatementReadsTempDbAndLogColumns()
    {
        // RF-ING-03 / RNF-COM-04: en 2016 esas columnas no existen y nombrarlas haría fallar la sentencia.
        foreach (var column in new[] { "rs.avg_tempdb_space_used", "rs.avg_log_bytes_used" })
        {
            Assert.Contains(column, TelemetryStatementWhitelist.GetIngestionRuntimeStatsSql, StringComparison.Ordinal);
            Assert.DoesNotContain(column, TelemetryStatementWhitelist.GetIngestionRuntimeStatsWithoutTempDbLogSql, StringComparison.Ordinal);
        }

        Assert.Contains(
            "CAST(NULL AS float) AS avg_tempdb_space_used, CAST(NULL AS float) AS avg_log_bytes_used",
            TelemetryStatementWhitelist.GetIngestionRuntimeStatsWithoutTempDbLogSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IngestionTopNRanksByTotalDurationWithDeterministicTieBreak()
    {
        // RF-ING-10 + D1 (S1-11): Top-N por duración total ponderada por ejecuciones, desempate por query_id.
        var sql = TelemetryStatementWhitelist.GetIngestionRuntimeStatsSql;

        Assert.Contains("SELECT TOP (@TopCount) p2.query_id", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY SUM(rs2.avg_duration * rs2.count_executions) DESC, p2.query_id)", sql, StringComparison.Ordinal);
        Assert.Contains("rs2.runtime_stats_interval_id > @AfterIntervalId", sql, StringComparison.Ordinal);
        Assert.Contains("rs.runtime_stats_interval_id > @AfterIntervalId", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryStoreViewColumnsUsesAllCatalogViews()
    {
        // S1-07: las vistas sys.query_store_* solo aparecen en sys.all_objects / sys.all_columns.
        var sql = TelemetryStatementWhitelist.GetQueryStoreViewColumnsSql;

        Assert.Contains("FROM sys.all_columns c", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN sys.all_objects o", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("sys.columns ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("sys.objects ", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateStatementAllowsExactWhitelistedQuery()
    {
        var key = TelemetryStatementWhitelist.Keys.DetectEngineVersion;
        var sql = TelemetryStatementWhitelist.DetectEngineVersionSql;

        // No debe lanzar excepción
        TelemetryStatementWhitelist.ValidateStatementOrThrow(key, sql);
    }

    [Fact]
    public void ValidateStatementRejectsUnregisteredStatementKey()
    {
        var ex = Assert.Throws<SecurityException>(() =>
            TelemetryStatementWhitelist.GetSql("UnregisteredArbitraryKey"));

        Assert.Contains("no forma parte de la lista blanca", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateStatementRejectsAlteredQueryText()
    {
        var key = TelemetryStatementWhitelist.Keys.DetectEngineVersion;
        var alteredSql = "SELECT @@VERSION; -- Consulta alterada no idéntica a la constante";

        var ex = Assert.Throws<SecurityException>(() =>
            TelemetryStatementWhitelist.ValidateStatementOrThrow(key, alteredSql));

        Assert.Contains("no coincide exactamente", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("INSERT INTO sys.query_store_plan VALUES (1, 1, '<plan/>');")]
    [InlineData("UPDATE sys.database_query_store_options SET actual_state = 1;")]
    [InlineData("DELETE FROM sys.query_store_query;")]
    [InlineData("MERGE INTO target USING source ON target.id = source.id WHEN MATCHED THEN DELETE;")]
    public void EnsureReadOnlyRejectsDmlOperations(string dmlSql)
    {
        var ex = Assert.Throws<SecurityException>(() =>
            TelemetryStatementWhitelist.EnsureReadOnlySelectStatement(dmlSql));

        Assert.Contains("RNF-SEG-01", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("DROP TABLE production_orders;")]
    [InlineData("ALTER DATABASE CURRENT SET QUERY_STORE = OFF;")]
    [InlineData("CREATE PROCEDURE sp_malicious AS SELECT 1;")]
    [InlineData("TRUNCATE TABLE audit_trail;")]
    public void EnsureReadOnlyRejectsDdlOperations(string ddlSql)
    {
        var ex = Assert.Throws<SecurityException>(() =>
            TelemetryStatementWhitelist.EnsureReadOnlySelectStatement(ddlSql));

        Assert.Contains("RNF-SEG-01", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("EXEC sp_query_store_force_plan @query_id = 1, @plan_id = 1;")]
    [InlineData("EXECUTE sp_executesql N'SELECT * FROM users';")]
    [InlineData("SHUTDOWN WITH NOWAIT;")]
    [InlineData("BACKUP DATABASE master TO DISK = 'C:\\evil.bak';")]
    [InlineData("RESTORE DATABASE master FROM DISK = 'C:\\evil.bak';")]
    [InlineData("DBCC FREEPROCCACHE;")]
    public void EnsureReadOnlyRejectsAdministrativeCommands(string adminSql)
    {
        var ex = Assert.Throws<SecurityException>(() =>
            TelemetryStatementWhitelist.EnsureReadOnlySelectStatement(adminSql));

        Assert.Contains("RNF-SEG-01", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EnsureReadOnlyRejectsEmptyOrWhitespace(string? emptySql)
    {
        Assert.Throws<SecurityException>(() =>
            TelemetryStatementWhitelist.EnsureReadOnlySelectStatement(emptySql!));
    }
}
