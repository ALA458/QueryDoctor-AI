using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.LabTests;

/// <summary>
/// S1-09 contra SQL Server 2022 real (D7). Requiere: compose sqlserver arriba, Run-Lab build + login y
/// lab/sql/11-probe-fixtures.sql. Cada prueba crea su propia conexión en memoria: no dependen del orden.
/// </summary>
public sealed class CapabilityProbeLabTests
{
    private static readonly string[] ExpectedProbeStatements =
    [
        TelemetryStatementWhitelist.Keys.DetectEngineVersion,
        TelemetryStatementWhitelist.Keys.CheckUserPermissions,
        TelemetryStatementWhitelist.Keys.GetQueryStoreOptions,
        TelemetryStatementWhitelist.Keys.GetQueryStoreViewColumns,
    ];

    // CA-HU01.1 + CA-HU02.1 + RF-CFG-07: qd_reader (VIEW DATABASE STATE) queda Active con el perfil completo.
    [LabFact]
    public async Task QdReaderEnQdLabQuedaActiveConPerfilDeSqlServer2022()
    {
        var lab = new LabProbeHarness();
        var connection = lab.AddPending(LabEnvironment.Database, LabEnvironment.Reader, LabEnvironment.ReaderPassword());

        var outcome = await lab.ProbeAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Active, outcome.Status);
        Assert.Null(outcome.FailureCause);

        var profile = Assert.Single(lab.Repository.Profiles);
        Assert.StartsWith("16.", profile.EngineVersion, StringComparison.Ordinal);
        Assert.Contains("Developer", profile.Edition, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SupportLevel.Tested, profile.SupportLevel);
        Assert.True(profile.HasViewDatabaseState);

        // Lo que configura lab/sql/01-database.sql.
        Assert.Equal("READ_WRITE", profile.QueryStoreActualState);
        Assert.Equal(1, profile.IntervalLengthMinutes);
        Assert.Equal("ALL", profile.CaptureMode);

        // 2022 trae las columnas de RF-ING-03: no se omite nada.
        Assert.Equal("[]", profile.OmittedFeaturesJson);
    }

    // RNF-SEG-01 / S1-08: el sondeo solo emite las 4 sentencias de la lista blanca, todas SELECT, y quedan en statement_log.
    [LabFact]
    public async Task ElSondeoSoloEjecutaSelectDeLaListaBlancaYQuedanEnStatementLog()
    {
        var lab = new LabProbeHarness();
        var connection = lab.AddPending(LabEnvironment.Database, LabEnvironment.Reader, LabEnvironment.ReaderPassword());

        await lab.ProbeAsync(connection.Id);

        var entries = lab.StatementLog.GetEntries();
        Assert.Equal(ExpectedProbeStatements, entries.Select(e => e.StatementKey));
        Assert.All(entries, entry =>
        {
            Assert.True(entry.Success);
            Assert.Equal(TelemetryStatementWhitelist.GetSql(entry.StatementKey), entry.StatementText);
            Assert.StartsWith("SELECT ", entry.StatementText.TrimStart(), StringComparison.OrdinalIgnoreCase);
        });
    }

    // CA-HU02.3: el sondeo lee Query Store y no lo modifica.
    [LabFact]
    public async Task ElSondeoNoCambiaLaConfiguracionDeQueryStore()
    {
        var before = await LabProbeHarness.QueryStoreConfigurationAsync(LabEnvironment.Database);
        var lab = new LabProbeHarness();
        var connection = lab.AddPending(LabEnvironment.Database, LabEnvironment.Reader, LabEnvironment.ReaderPassword());

        await lab.ProbeAsync(connection.Id);

        var after = await LabProbeHarness.QueryStoreConfigurationAsync(LabEnvironment.Database);
        Assert.NotEmpty(before);
        Assert.Equal(before, after);
    }

    // Decisión 9: en 2022+ VIEW DATABASE PERFORMANCE STATE también alcanza (Cuadro 4.16).
    [LabFact]
    public async Task UnaCuentaConSoloViewDatabasePerformanceStateQuedaActiveEn2022()
    {
        var lab = new LabProbeHarness();
        var connection = lab.AddPending(LabEnvironment.Database, LabEnvironment.PerformanceStateOnly, LabEnvironment.ReaderPassword());

        var outcome = await lab.ProbeAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Active, outcome.Status);
        Assert.Null(outcome.FailureCause);
    }

    // CA-HU01.2: credenciales inválidas → Failed con su código, sin perfil.
    [LabFact]
    public async Task ContrasenaIncorrectaQuedaFailedPorCredencialesInvalidas()
    {
        var lab = new LabProbeHarness();
        var connection = lab.AddPending(LabEnvironment.Database, LabEnvironment.Reader, "no-es-la-contrasena-" + Guid.NewGuid());

        var outcome = await lab.ProbeAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Failed, outcome.Status);
        Assert.Equal(ProbeFailureCause.InvalidCredentials, outcome.FailureCause);
        Assert.Empty(lab.Repository.Profiles);
    }

    // CA-HU01.2: conecta pero no tiene el permiso mínimo de la matriz.
    [LabFact]
    public async Task QdNopermQuedaFailedPorPermisoFaltante()
    {
        var lab = new LabProbeHarness();
        var connection = lab.AddPending(LabEnvironment.Database, LabEnvironment.NoPermission, LabEnvironment.ReaderPassword());

        var outcome = await lab.ProbeAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Failed, outcome.Status);
        Assert.Equal(ProbeFailureCause.MissingPermission, outcome.FailureCause);
    }

    // SQL Server responde 4060 igual si la base no existe o si no hay acceso: no se distingue a propósito.
    [LabFact]
    public async Task BaseInexistenteQuedaFailedPorPermisoFaltante()
    {
        var lab = new LabProbeHarness();
        var connection = lab.AddPending("QD_No_Existe", LabEnvironment.Reader, LabEnvironment.ReaderPassword());

        var outcome = await lab.ProbeAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Failed, outcome.Status);
        Assert.Equal(ProbeFailureCause.MissingPermission, outcome.FailureCause);
    }

    // Invariante 3 / RNF-SEG-03: sin la excepción del laboratorio, el certificado autofirmado se rechaza.
    [LabFact]
    public async Task SinExcepcionDeLaboratorioElCertificadoAutofirmadoSeRechaza()
    {
        var lab = new LabProbeHarness();
        var connection = lab.AddPending(LabEnvironment.Database, LabEnvironment.Reader, LabEnvironment.ReaderPassword());

        var outcome = await lab.ProbeAsync(connection.Id, trustLabCertificate: false);

        Assert.Equal(ConnectionStatus.Failed, outcome.Status);
        Assert.Equal(ProbeFailureCause.TlsCertificateInvalid, outcome.FailureCause);
    }

    // Puerto cerrado en el mismo host: el error de red se clasifica como servidor inalcanzable.
    [LabFact]
    public async Task PuertoCerradoQuedaFailedPorServidorInalcanzable()
    {
        var lab = new LabProbeHarness();
        var connection = lab.AddPending(
            LabEnvironment.Database,
            LabEnvironment.Reader,
            LabEnvironment.ReaderPassword(),
            server: LabEnvironment.RequiredServer + ",1");

        var outcome = await lab.ProbeAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Failed, outcome.Status);
        Assert.Equal(ProbeFailureCause.ServerUnreachable, outcome.FailureCause);
    }

    // CA-HU02.3: Query Store apagado se informa tal cual, no se intenta activar, y no habilita la ingesta (D5).
    [LabFact]
    public async Task QueryStoreApagadoSeInformaSinActivarlo()
    {
        var before = await LabProbeHarness.QueryStoreConfigurationAsync(LabEnvironment.DatabaseWithoutQueryStore);
        var lab = new LabProbeHarness();
        var connection = lab.AddPending(LabEnvironment.DatabaseWithoutQueryStore, LabEnvironment.Reader, LabEnvironment.ReaderPassword());

        var outcome = await lab.ProbeAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Active, outcome.Status);
        var profile = Assert.Single(lab.Repository.Profiles);
        Assert.Equal("OFF", profile.QueryStoreActualState);
        Assert.False(QueryStoreState.CanIngest(profile.QueryStoreActualState));

        var after = await LabProbeHarness.QueryStoreConfigurationAsync(LabEnvironment.DatabaseWithoutQueryStore);
        Assert.Equal("OFF", after["actual_state_desc"]);
        Assert.Equal(before, after);
    }

    // CA-HU01.3: versión < 2016 (sustituida sobre el sondeo real) → Rejected y sin credenciales guardadas.
    [LabFact]
    public async Task VersionNoSoportadaQuedaRejectedYBorraLasCredenciales()
    {
        var lab = new LabProbeHarness();
        var connection = lab.AddPending(LabEnvironment.Database, LabEnvironment.Reader, LabEnvironment.ReaderPassword());

        var outcome = await lab.ProbeAsync(connection.Id, decorate: real => new EngineVersionOverride(real, "12.0.6024.0"));

        Assert.Equal(ConnectionStatus.Rejected, outcome.Status);
        Assert.Equal(ProbeFailureCause.UnsupportedVersion, outcome.FailureCause);
        Assert.Null(lab.Repository.Stored.Single(c => c.Id == connection.Id).EncryptedCredentials);
        Assert.Empty(lab.Repository.Profiles);

        // Se detiene tras leer la versión: no consulta permisos ni Query Store.
        Assert.Equal([TelemetryStatementWhitelist.Keys.DetectEngineVersion], lab.StatementLog.GetEntries().Select(e => e.StatementKey));
    }
}
