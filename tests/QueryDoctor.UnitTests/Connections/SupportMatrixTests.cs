using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Connections;

// S1-09: matriz de soporte del Cuadro 4.16 (RF-CFG-03, RF-CFG-07, RNF-COM-04).
public class SupportMatrixTests
{
    private const int Developer = 3;
    private const int AzureSqlDb = 5;
    private const int ManagedInstance = 8;

    [Theory]
    [InlineData("10.50.6000.34", SupportLevel.NotSupported, ProbeFailureCause.UnsupportedVersion)] // 2008 R2
    [InlineData("11.0.7001.0", SupportLevel.NotSupported, ProbeFailureCause.UnsupportedVersion)]   // 2012
    [InlineData("12.0.6024.0", SupportLevel.NotSupported, ProbeFailureCause.UnsupportedVersion)]   // 2014
    [InlineData("13.0.7000.253", SupportLevel.CompatibleUntested, null)]                           // 2016
    [InlineData("14.0.3456.2", SupportLevel.CompatibleUntested, null)]                             // 2017
    [InlineData("15.0.4382.1", SupportLevel.Tested, null)]                                         // 2019
    [InlineData("16.0.4315.2", SupportLevel.Tested, null)]                                         // 2022 (laboratorio)
    [InlineData("17.0.1000.7", SupportLevel.Tested, null)]                                         // 2025 (deuda D4)
    public void SqlServerSeClasificaPorVersionMayor(string version, SupportLevel level, ProbeFailureCause? cause)
    {
        var result = SupportMatrix.Assess(ConnectionPlatform.SqlServer, version, Developer);

        Assert.Equal(DetectedPlatform.SqlServer, result.Platform);
        Assert.Equal(level, result.SupportLevel);
        Assert.Equal(cause, result.FailureCause);
    }

    [Fact]
    public void ElRechazoPorVersionIndicaLaVersionMinima()
    {
        var result = SupportMatrix.Assess(ConnectionPlatform.SqlServer, "12.0.6024.0", Developer);

        Assert.Equal(ProbeFailureCause.UnsupportedVersion, result.FailureCause);
        Assert.Contains("SQL Server 2016 (13.x)", ProbeFailureMessages.For(result.FailureCause!.Value), StringComparison.Ordinal);
    }

    [Fact]
    public void AzureSqlDatabaseReporta12xPeroNoSeRechaza()
    {
        // Gotcha: ProductVersion de Azure SQL Database es 12.0.x; el corte < 13 solo aplica a SQL Server.
        var result = SupportMatrix.Assess(ConnectionPlatform.AzureSqlDatabase, "12.0.2000.8", AzureSqlDb);

        Assert.Equal(DetectedPlatform.AzureSqlDatabase, result.Platform);
        Assert.Equal(SupportLevel.Tested, result.SupportLevel);
        Assert.Null(result.FailureCause);
    }

    [Fact]
    public void ManagedInstanceDeclaradaComoSqlServerEsCompatibleSinPruebas()
    {
        var result = SupportMatrix.Assess(ConnectionPlatform.SqlServer, "12.0.2000.8", ManagedInstance);

        Assert.Equal(DetectedPlatform.AzureSqlManagedInstance, result.Platform);
        Assert.Equal(SupportLevel.CompatibleUntested, result.SupportLevel);
        Assert.Null(result.FailureCause);
    }

    [Theory]
    [InlineData(ConnectionPlatform.SqlServer, AzureSqlDb)]
    [InlineData(ConnectionPlatform.AzureSqlDatabase, Developer)]
    [InlineData(ConnectionPlatform.AzureSqlDatabase, ManagedInstance)]
    [InlineData(ConnectionPlatform.SqlServer, 6)]  // Synapse
    [InlineData(ConnectionPlatform.SqlServer, 9)]  // Edge
    public void PlataformaDeclaradaDistintaDeLaDetectadaFalla(ConnectionPlatform declared, int engineEdition)
    {
        var result = SupportMatrix.Assess(declared, "16.0.4315.2", engineEdition);

        Assert.Equal(ProbeFailureCause.PlatformMismatch, result.FailureCause);
        Assert.Equal(SupportLevel.NotSupported, result.SupportLevel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void VersionIlegibleFallaComoDesconocida(string? version)
    {
        var result = SupportMatrix.Assess(ConnectionPlatform.SqlServer, version, Developer);

        Assert.Null(result.MajorVersion);
        Assert.Equal(ProbeFailureCause.Unknown, result.FailureCause);
    }

    [Fact]
    public void EsDeterminista()
    {
        var a = SupportMatrix.Assess(ConnectionPlatform.SqlServer, "15.0.4382.1", Developer);
        var b = SupportMatrix.Assess(ConnectionPlatform.SqlServer, "15.0.4382.1", Developer);

        Assert.Equal(a, b);
    }

    [Theory]
    // 2022+: vale cualquiera de los dos (punto 9 de la aprobación).
    [InlineData("16.0.4315.2", Developer, new[] { "CONNECT", "VIEW DATABASE STATE" }, true)] // qd_reader
    [InlineData("16.0.4315.2", Developer, new[] { "CONNECT", "VIEW DATABASE PERFORMANCE STATE" }, true)]
    [InlineData("17.0.1000.7", Developer, new[] { "VIEW DATABASE PERFORMANCE STATE" }, true)]
    // Antes de 2022 VIEW DATABASE PERFORMANCE STATE no existe: solo vale VIEW DATABASE STATE.
    [InlineData("15.0.4382.1", Developer, new[] { "VIEW DATABASE STATE" }, true)]
    [InlineData("15.0.4382.1", Developer, new[] { "VIEW DATABASE PERFORMANCE STATE" }, false)]
    // Azure SQL Database: "A verificar" en la matriz; se aceptan ambos.
    [InlineData("12.0.2000.8", AzureSqlDb, new[] { "VIEW DATABASE PERFORMANCE STATE" }, true)]
    // qd_noperm: solo CONNECT.
    [InlineData("16.0.4315.2", Developer, new[] { "CONNECT" }, false)]
    [InlineData("16.0.4315.2", Developer, new string[0], false)]
    public void PermisoMinimoSegunLaMatriz(string version, int engineEdition, string[] permissions, bool expected)
    {
        var declared = engineEdition == AzureSqlDb ? ConnectionPlatform.AzureSqlDatabase : ConnectionPlatform.SqlServer;
        var assessment = SupportMatrix.Assess(declared, version, engineEdition);

        Assert.Equal(expected, SupportMatrix.HasMinimumPermission(assessment, permissions));
    }

    [Fact]
    public void ConLasColumnasDe2017NoSeOmiteNada()
    {
        var columns = new[] { "plan_id", "avg_duration", "avg_tempdb_space_used", "avg_log_bytes_used" };

        Assert.Empty(SupportMatrix.OmittedFeatures(columns));
    }

    [Theory]
    [InlineData("plan_id,avg_duration")]          // 2016
    [InlineData("plan_id,avg_log_bytes_used")]    // falta una
    [InlineData("")]                              // no se pudieron leer
    public void SinLasColumnasDeTempDbYLogSeOmiteRfIng03(string columnsCsv)
    {
        var columns = columnsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var omitted = SupportMatrix.OmittedFeatures(columns);

        Assert.Equal([OmittedFeature.TempDbAndLogBytes], omitted);
        Assert.Contains("TempDB", SupportMatrix.MessageFor(OmittedFeature.TempDbAndLogBytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("READ_WRITE", true)]
    [InlineData("read_only", true)]
    [InlineData("OFF", false)]
    [InlineData("ERROR", false)]
    [InlineData("READ_CAPTURE_SECONDARY", false)]
    [InlineData(null, false)]
    public void SoloSeIngiereConQueryStoreLegible(string? state, bool expected)
    {
        Assert.Equal(expected, QueryStoreState.CanIngest(state));
    }
}
