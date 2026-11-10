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
            TelemetryStatementWhitelist.Keys.GetQueryStoreViewColumns
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
