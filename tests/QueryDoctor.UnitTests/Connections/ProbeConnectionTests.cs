using System.Diagnostics;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Connections;

// S1-09: trabajo probe_connection (RF-CFG-02 a 04, RF-CFG-07, RNF-COM-04; CA-HU01.1–01.3, CA-HU02.1).
// Las mismas situaciones se repiten contra el SQL Server de laboratorio en P8.
public class ProbeConnectionTests
{
    private static readonly string[] Columns2022 = ["plan_id", "avg_duration", "avg_tempdb_space_used", "avg_log_bytes_used"];

    private readonly InMemoryConnectionRepository _repository = new();
    private readonly FakeEncryptor _encryptor = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeCapabilityProbe _probe = new();
    private readonly ProbeOptions _options = new() { TimeoutSeconds = 15 };

    private ProbeConnection UseCase() => new(_repository, _probe, _encryptor, _clock, _options);

    private Connection Pending(ConnectionPlatform platform = ConnectionPlatform.SqlServer, string server = "sqlserver")
    {
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = platform,
            Server = server,
            DatabaseName = "QD_Lab",
            Status = ConnectionStatus.Pending,
            EncryptedCredentials = ConnectionCredentialsCodec.Encrypt(new("qd_reader", "S3creta!"), _encryptor),
        };
        _repository.Stored.Add(connection);
        return connection;
    }

    [Fact]
    public async Task CuentaDeSoloLecturaSobre2022QuedaActivaConVersionYEstadoDeQueryStore()
    {
        var connection = Pending();

        var outcome = await UseCase().ExecuteAsync(connection.Id);

        Assert.Equal(new ProbeOutcome(connection.Id, true, ConnectionStatus.Active, null), outcome);
        Assert.Equal(ConnectionStatus.Active, connection.Status);
        Assert.Equal(_clock.Now, connection.LastProbedAt);

        // CA-HU01.1: versión detectada. CA-HU02.1: estado, intervalo, modo de captura y retención.
        var profile = Assert.Single(_repository.Profiles);
        Assert.Equal(connection.Id, profile.ConnectionId);
        Assert.Equal("16.0.4315.2", profile.EngineVersion);
        Assert.Equal("Developer Edition (64-bit)", profile.Edition);
        Assert.Equal(SupportLevel.Tested, profile.SupportLevel);
        Assert.Equal("READ_WRITE", profile.QueryStoreActualState);
        Assert.Equal("READ_WRITE", profile.QueryStoreDesiredState);
        Assert.Equal("0", profile.QueryStoreReadOnlyReason);
        Assert.Equal(1, profile.IntervalLengthMinutes);
        Assert.Equal("ALL", profile.CaptureMode);
        Assert.Equal(30, profile.RetentionDays);
        Assert.Equal("[]", profile.OmittedFeaturesJson);
        Assert.True(profile.HasViewDatabaseState);
        Assert.Equal(_clock.Now, profile.ProbedAt);

        Assert.Equal(["engine", "permissions", "queryStore", "columns"], _probe.Calls);
    }

    [Fact]
    public async Task LasCredencialesSeDescifranSoloParaElSondeo()
    {
        var connection = Pending();

        await UseCase().ExecuteAsync(connection.Id);

        Assert.Equal("qd_reader", _probe.LastTarget!.UserId);
        Assert.Equal("S3creta!", _probe.LastTarget.Password);
        Assert.Equal("sqlserver", _probe.LastTarget.Server);
        Assert.Equal("QD_Lab", _probe.LastTarget.Database);
    }

    [Fact]
    public async Task UnaVersionAnteriorA2016SeRechazaBorraLasCredencialesYNoConsultaQueryStore()
    {
        var connection = Pending();
        _probe.Engine = new EngineInfo("12.0.6024.0", "Standard Edition", 2);

        var outcome = await UseCase().ExecuteAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Rejected, outcome.Status);
        Assert.Equal(ProbeFailureCause.UnsupportedVersion, connection.ProbeFailureCause);
        Assert.Null(connection.EncryptedCredentials);
        Assert.Empty(_repository.Profiles);
        Assert.Equal(["engine"], _probe.Calls);
    }

    [Fact]
    public async Task AzureSqlDatabaseCon12xQuedaActiva()
    {
        var connection = Pending(ConnectionPlatform.AzureSqlDatabase, "srv.database.windows.net");
        _probe.Engine = new EngineInfo("12.0.2000.8", "SQL Azure", 5);

        var outcome = await UseCase().ExecuteAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Active, outcome.Status);
        Assert.Equal(SupportLevel.Tested, Assert.Single(_repository.Profiles).SupportLevel);
    }

    [Fact]
    public async Task PlataformaDeclaradaQueNoCoincideFalla()
    {
        var connection = Pending(ConnectionPlatform.AzureSqlDatabase);

        var outcome = await UseCase().ExecuteAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Failed, outcome.Status);
        Assert.Equal(ProbeFailureCause.PlatformMismatch, outcome.FailureCause);
    }

    [Theory]
    [InlineData(TelemetryFailureKind.InvalidCredentials, ProbeFailureCause.InvalidCredentials)]
    [InlineData(TelemetryFailureKind.ServerUnreachable, ProbeFailureCause.ServerUnreachable)]
    [InlineData(TelemetryFailureKind.TlsCertificateInvalid, ProbeFailureCause.TlsCertificateInvalid)]
    [InlineData(TelemetryFailureKind.PermissionDenied, ProbeFailureCause.MissingPermission)]
    [InlineData(TelemetryFailureKind.Timeout, ProbeFailureCause.Timeout)]
    [InlineData(TelemetryFailureKind.Unknown, ProbeFailureCause.Unknown)]
    public async Task UnFalloAlConectarDejaLaConexionFailedConSuCodigo(TelemetryFailureKind kind, ProbeFailureCause expected)
    {
        // CA-HU01.2: muestra la causa y no activa la conexión.
        var connection = Pending();
        _probe.EngineError = new TelemetrySourceException(kind, null);

        var outcome = await UseCase().ExecuteAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Failed, connection.Status);
        Assert.Equal(expected, connection.ProbeFailureCause);
        Assert.Equal(expected, outcome.FailureCause);
        Assert.NotNull(connection.EncryptedCredentials);
        Assert.Empty(_repository.Profiles);
    }

    [Fact]
    public async Task UnaCuentaSinPermisosFallaConMissingPermissionSinLeerQueryStore()
    {
        // qd_noperm: solo CONNECT (D2).
        var connection = Pending();
        _probe.Permissions = ["CONNECT"];

        var outcome = await UseCase().ExecuteAsync(connection.Id);

        Assert.Equal(ProbeFailureCause.MissingPermission, outcome.FailureCause);
        Assert.Equal(["engine", "permissions"], _probe.Calls);
    }

    [Fact]
    public async Task En2022BastaViewDatabasePerformanceState()
    {
        var connection = Pending();
        _probe.Permissions = ["CONNECT", "VIEW DATABASE PERFORMANCE STATE"];

        Assert.Equal(ConnectionStatus.Active, (await UseCase().ExecuteAsync(connection.Id)).Status);
    }

    [Fact]
    public async Task SiQueryStoreNoSePuedeLeerFallaConQueryStoreUnavailable()
    {
        var connection = Pending();
        _probe.QueryStoreError = new TelemetrySourceException(TelemetryFailureKind.PermissionDenied, 262);

        Assert.Equal(ProbeFailureCause.QueryStoreUnavailable, (await UseCase().ExecuteAsync(connection.Id)).FailureCause);
    }

    [Fact]
    public async Task SiLaVistaDeQueryStoreNoDevuelveFilasFallaConQueryStoreUnavailable()
    {
        var connection = Pending();
        _probe.QueryStore = null;

        Assert.Equal(ProbeFailureCause.QueryStoreUnavailable, (await UseCase().ExecuteAsync(connection.Id)).FailureCause);
    }

    [Fact]
    public async Task ConQueryStoreDeshabilitadoQuedaActivaYSeAvisaQueNoSeIngerira()
    {
        // §6.1: la conexión queda registrada y se avisa que la ingesta se omitirá (CA-HU02.2 la omite en S1-11).
        var connection = Pending();
        _probe.QueryStore = new QueryStoreOptions("OFF", "OFF", 0, 60, "AUTO", 30);

        var outcome = await UseCase().ExecuteAsync(connection.Id);

        Assert.Equal(ConnectionStatus.Active, outcome.Status);
        var profile = Assert.Single(_repository.Profiles);
        Assert.Equal("OFF", profile.QueryStoreActualState);
        Assert.False(QueryStoreState.CanIngest(profile.QueryStoreActualState));
    }

    [Fact]
    public async Task En2016SeOmitenTempDbYBytesDeLog()
    {
        var connection = Pending();
        _probe.Engine = new EngineInfo("13.0.7000.253", "Standard Edition", 2);
        _probe.Columns = ["plan_id", "avg_duration"];

        await UseCase().ExecuteAsync(connection.Id);

        var profile = Assert.Single(_repository.Profiles);
        Assert.Equal(SupportLevel.CompatibleUntested, profile.SupportLevel);
        Assert.Equal("[\"TempDbAndLogBytes\"]", profile.OmittedFeaturesJson);
    }

    [Fact]
    public async Task SiSuperaElTiempoLimiteQuedaFailedConTimeoutYNoPending()
    {
        // RF-CFG-02 / punto 2 de la aprobación: el probe cuelga; a los 1 s la conexión pasa a Failed/Timeout.
        _options.TimeoutSeconds = 1;
        var connection = Pending();
        _probe.HangOnEngine = true;
        var watch = Stopwatch.StartNew();

        var outcome = await UseCase().ExecuteAsync(connection.Id);

        watch.Stop();
        Assert.Equal(ConnectionStatus.Failed, outcome.Status);
        Assert.Equal(ProbeFailureCause.Timeout, connection.ProbeFailureCause);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(5));
        Assert.Equal(1, _repository.SaveCount);
    }

    [Fact]
    public async Task ElTiempoLimiteTambienAcotaElTimeoutDeConexionDelGateway()
    {
        _options.TimeoutSeconds = 7;
        var connection = Pending();

        await UseCase().ExecuteAsync(connection.Id);

        Assert.Equal(7, _probe.LastTarget!.ConnectionTimeoutSeconds);
    }

    [Fact]
    public async Task UnaExcepcionInesperadaQuedaComoUnknown()
    {
        var connection = Pending();
        _probe.EngineError = new FormatException("columna inesperada");

        Assert.Equal(ProbeFailureCause.Unknown, (await UseCase().ExecuteAsync(connection.Id)).FailureCause);
        Assert.Equal(ConnectionStatus.Failed, connection.Status);
    }

    [Theory]
    [InlineData(ConnectionStatus.Active)]
    [InlineData(ConnectionStatus.Failed)]
    [InlineData(ConnectionStatus.Rejected)]
    [InlineData(ConnectionStatus.Disabled)]
    public async Task SoloSeSondeaLoQueSigueEnPending(ConnectionStatus status)
    {
        var connection = Pending();
        connection.Status = status;

        var outcome = await UseCase().ExecuteAsync(connection.Id);

        Assert.False(outcome.Ran);
        Assert.Empty(_probe.Calls);
        Assert.Equal(0, _repository.SaveCount);
    }

    [Fact]
    public async Task UnaConexionInexistenteNoHaceNada()
    {
        var outcome = await UseCase().ExecuteAsync(Guid.NewGuid());

        Assert.False(outcome.Ran);
        Assert.Empty(_probe.Calls);
    }

    [Fact]
    public async Task UnNuevoSondeoSobrescribeElPerfilExistente()
    {
        var connection = Pending();
        _repository.Profiles.Add(new CapabilityProfile { Id = Guid.NewGuid(), ConnectionId = connection.Id, EngineVersion = "15.0" });

        await UseCase().ExecuteAsync(connection.Id);

        Assert.Equal("16.0.4315.2", Assert.Single(_repository.Profiles).EngineVersion);
    }

    [Fact]
    public async Task ElCertificadoAutofirmadoSoloSeAceptaParaServidoresDeLaboratorioListados()
    {
        _options.LabTrustedCertificateServers = ["sqlserver"];
        var lab = Pending(server: "sqlserver");
        await UseCase().ExecuteAsync(lab.Id);
        Assert.True(_probe.LastTarget!.TrustServerCertificate);
        Assert.True(_probe.LastTarget.AllowLocalTestCertificates);

        var other = Pending(server: "prod-sql.empresa.local");
        await UseCase().ExecuteAsync(other.Id);
        Assert.False(_probe.LastTarget!.TrustServerCertificate);
        Assert.False(_probe.LastTarget.AllowLocalTestCertificates);
    }

    [Fact]
    public void RechazaUnTiempoLimiteMenorQueUnSegundo()
    {
        _options.TimeoutSeconds = 0;

        Assert.Throws<ArgumentOutOfRangeException>(UseCase);
    }

    private sealed class FakeCapabilityProbe : ICapabilityProbe
    {
        public List<string> Calls { get; } = [];

        public TargetDatabaseConnection? LastTarget { get; private set; }

        public EngineInfo Engine { get; set; } = new("16.0.4315.2", "Developer Edition (64-bit)", 3);

        public Exception? EngineError { get; set; }

        public bool HangOnEngine { get; set; }

        public IReadOnlyList<string> Permissions { get; set; } = ["CONNECT", "VIEW DATABASE STATE", "VIEW DATABASE PERFORMANCE STATE"];

        public QueryStoreOptions? QueryStore { get; set; } = new("READ_WRITE", "READ_WRITE", 0, 1, "ALL", 30);

        public Exception? QueryStoreError { get; set; }

        public IReadOnlyList<string> Columns { get; set; } = Columns2022;

        public async Task<EngineInfo> GetEngineAsync(TargetDatabaseConnection target, CancellationToken cancellationToken)
        {
            Calls.Add("engine");
            LastTarget = target;
            if (HangOnEngine)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return EngineError is null ? Engine : throw EngineError;
        }

        public Task<IReadOnlyList<string>> GetDatabasePermissionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken)
        {
            Calls.Add("permissions");
            return Task.FromResult(Permissions);
        }

        public Task<QueryStoreOptions?> GetQueryStoreOptionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken)
        {
            Calls.Add("queryStore");
            return QueryStoreError is null ? Task.FromResult(QueryStore) : throw QueryStoreError;
        }

        public Task<IReadOnlyList<string>> GetRuntimeStatsColumnsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken)
        {
            Calls.Add("columns");
            return Task.FromResult(Columns);
        }
    }
}
