using System.Security;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Infrastructure.Telemetry;

namespace QueryDoctor.UnitTests.Telemetry;

public class QueryStoreGatewayExecutionTests
{
    [Fact]
    public async Task ExecuteReaderRejectsUnwhitelistedStatementKeyWithoutConnecting()
    {
        var logWriter = new InMemoryStatementLogWriter();
        var gateway = new QueryStoreGateway(logWriter);

        var connection = new TargetDatabaseConnection(
            server: "unreachable.server.local",
            database: "TestDB",
            userId: "test_user",
            password: "test_password",
            trustServerCertificate: false);

        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            gateway.ExecuteReaderAsync(
                connection,
                "ArbitraryCustomKey",
                record => record.GetString(0)));

        Assert.Contains("no forma parte de la lista blanca", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(logWriter.GetEntries());
    }

    [Fact]
    public async Task ExecuteReaderRejectsUntrustedCertificateWithoutConnecting()
    {
        var logWriter = new InMemoryStatementLogWriter();
        var gateway = new QueryStoreGateway(logWriter);

        var connection = new TargetDatabaseConnection(
            server: "unreachable.server.local",
            database: "TestDB",
            userId: "test_user",
            password: "test_password",
            trustServerCertificate: true,
            allowLocalTestCertificates: false);

        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            gateway.ExecuteReaderAsync(
                connection,
                TelemetryStatementWhitelist.Keys.DetectEngineVersion,
                record => record.GetString(0)));

        Assert.Contains("RNF-SEG-03", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(logWriter.GetEntries());
    }

    [Fact]
    public async Task ExecuteScalarRejectsUnwhitelistedStatementKey()
    {
        var logWriter = new InMemoryStatementLogWriter();
        var gateway = new QueryStoreGateway(logWriter);

        var connection = new TargetDatabaseConnection("server", "db");

        await Assert.ThrowsAsync<SecurityException>(() =>
            gateway.ExecuteScalarAsync<int>(connection, "NonExistentKey"));
    }

    [Fact]
    public async Task StatementLogOnlyContainsSelectStatements()
    {
        // Criterio de aceptación S1-08 / CA-HU15.2: "statement_log solo contiene SELECT"
        var logWriter = new InMemoryStatementLogWriter();

        var entry1 = new StatementLogEntry(
            Guid.NewGuid(),
            "sql2022.lab",
            "WideWorldImporters",
            TelemetryStatementWhitelist.Keys.DetectEngineVersion,
            TelemetryStatementWhitelist.DetectEngineVersionSql,
            DateTime.UtcNow,
            durationMs: 12,
            rowsRead: 1,
            success: true);

        var entry2 = new StatementLogEntry(
            Guid.NewGuid(),
            "sql2022.lab",
            "WideWorldImporters",
            TelemetryStatementWhitelist.Keys.GetQueryStoreOptions,
            TelemetryStatementWhitelist.GetQueryStoreOptionsSql,
            DateTime.UtcNow,
            durationMs: 8,
            rowsRead: 1,
            success: true);

        await logWriter.WriteAsync(entry1);
        await logWriter.WriteAsync(entry2);

        var loggedEntries = logWriter.GetEntries();
        Assert.Equal(2, loggedEntries.Count);

        foreach (var entry in loggedEntries)
        {
            // Validar que cada sentencia registrada en el log sea estrictamente un SELECT
            TelemetryStatementWhitelist.EnsureReadOnlySelectStatement(entry.StatementText);
            Assert.StartsWith("SELECT", entry.StatementText.TrimStart(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task InMemoryStatementLogWriterRecordsAndClearsEntries()
    {
        var logWriter = new InMemoryStatementLogWriter();

        var entry = new StatementLogEntry(
            Guid.NewGuid(),
            "server1",
            "db1",
            "DetectEngineVersion",
            "SELECT 1;",
            DateTime.UtcNow,
            10,
            1,
            true);

        await logWriter.WriteAsync(entry);
        Assert.Single(logWriter.GetEntries());

        logWriter.Clear();
        Assert.Empty(logWriter.GetEntries());
    }

    [Fact]
    public void TargetDatabaseConnectionSanitizesTargetDescriptor()
    {
        var connection = new TargetDatabaseConnection("prod-sql.internal,1433", "OrdersDB", "dba", "SecretPass");

        Assert.Equal("prod-sql.internal,1433/OrdersDB", connection.TargetDescriptor);
        Assert.DoesNotContain("SecretPass", connection.TargetDescriptor, StringComparison.Ordinal);
        Assert.DoesNotContain("dba", connection.TargetDescriptor, StringComparison.Ordinal);
    }
}
