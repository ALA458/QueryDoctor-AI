using System.Security;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Infrastructure.Telemetry;

namespace QueryDoctor.UnitTests.Telemetry;

public class QueryStoreGatewaySecurityTests
{
    [Fact]
    public void BuildConnectionStringRejectsTrustServerCertificateByDefault()
    {
        var connection = new TargetDatabaseConnection(
            server: "sqlserver.corp.local",
            database: "AdventureWorks",
            userId: "qd_reader",
            password: "SecretPassword123!",
            trustServerCertificate: true,
            allowLocalTestCertificates: false);

        var ex = Assert.Throws<SecurityException>(() =>
            QueryStoreGateway.BuildConnectionString(connection));

        Assert.Contains("RNF-SEG-03", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrustServerCertificate=true está estrictamente prohibido", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConnectionStringAllowsTrustServerCertificateWhenExplicitlyFlaggedForLocalTest()
    {
        var connection = new TargetDatabaseConnection(
            server: "localhost,1433",
            database: "AdventureWorks",
            userId: "sa",
            password: "LocalTestPassword!",
            trustServerCertificate: true,
            allowLocalTestCertificates: true);

        var connectionString = QueryStoreGateway.BuildConnectionString(connection);

        Assert.Contains("Trust Server Certificate=True", connectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Encrypt=True", connectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConnectionStringEnforcesMandatoryEncryptionByDefault()
    {
        var connection = new TargetDatabaseConnection(
            server: "sqlserver.corp.local",
            database: "SalesDB",
            userId: "monitor",
            password: "ComplexPassword456!",
            trustServerCertificate: false);

        var connectionString = QueryStoreGateway.BuildConnectionString(connection);

        Assert.Contains("Encrypt=True", connectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trust Server Certificate=False", connectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Connect Timeout=15", connectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConnectionStringEnforcesStrictEncryptionWhenConfigured()
    {
        var connection = new TargetDatabaseConnection(
            server: "sqlserver.corp.local",
            database: "SalesDB",
            userId: "monitor",
            password: "ComplexPassword456!",
            encryptionMode: TargetEncryptionMode.Strict,
            trustServerCertificate: false);

        var connectionString = QueryStoreGateway.BuildConnectionString(connection);

        Assert.Contains("Encrypt=Strict", connectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trust Server Certificate=False", connectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TargetDatabaseConnectionRejectsEmptyServerOrDatabase()
    {
        Assert.Throws<ArgumentException>(() =>
            new TargetDatabaseConnection(string.Empty, "db"));

        Assert.Throws<ArgumentException>(() =>
            new TargetDatabaseConnection("server", "   "));
    }

    [Fact]
    public void TargetDatabaseConnectionEnforcesDefaultTimeouts()
    {
        var connection = new TargetDatabaseConnection("localhost", "db");

        Assert.Equal(5000, connection.LockTimeoutMs);
        Assert.Equal(15, connection.ConnectionTimeoutSeconds);
        Assert.False(connection.TrustServerCertificate);
        Assert.False(connection.AllowLocalTestCertificates);
    }
}
