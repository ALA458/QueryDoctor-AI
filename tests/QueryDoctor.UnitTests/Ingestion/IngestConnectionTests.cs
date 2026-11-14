using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;
using QueryDoctor.UnitTests.Connections;
using static QueryDoctor.UnitTests.Ingestion.IngestionSamples;

namespace QueryDoctor.UnitTests.Ingestion;

// Caso de uso de S1-11 con dobles: decisiones de omitir, fallar, reintentar y qué se escribe.
public class IngestConnectionTests
{
    private const string ShowPlan = "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\"/>";

    private readonly FakeEncryptor _encryptor = new();
    private readonly FakeQueryStoreReader _reader = new();
    private readonly FakeIngestionLock _lock = new();
    private readonly InMemoryIngestionStore _store = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly Connection _connection;

    public IngestConnectionTests()
    {
        _connection = new Connection
        {
            Id = Guid.NewGuid(),
            Server = "lab",
            DatabaseName = "QD_Lab",
            Status = ConnectionStatus.Active,
            EncryptedCredentials = ConnectionCredentialsCodec.Encrypt(new ConnectionCredentials("qd_reader", "S3creta!"), _encryptor),
        };
        _store.Connection = _connection;
        _store.Profile = new CapabilityProfile { ConnectionId = _connection.Id, OmittedFeaturesJson = "[]" };
    }

    private IngestConnection UseCase(IngestionOptions? options = null) =>
        new(_store, _reader, _lock, _encryptor, _clock, options ?? new IngestionOptions(), new ProbeOptions());

    private void AddRow(long query, long text, long plan, long interval, long count = 1, double avg = 10d, bool closed = true) =>
        _reader.Rows.Add(new QueryStoreStatRow(query, text, "AB", "CD", "16.0", "dbo.usp", Row(plan, interval, Metrics(count, avg), closed)));

    [Fact]
    public async Task UnaIngestaCorrectaCifraTextoYPlanYAvanzaLaMarca()
    {
        AddRow(query: 1, text: 10, plan: 100, interval: 5, count: 1, avg: 10d);
        AddRow(query: 1, text: 10, plan: 100, interval: 5, count: 3, avg: 30d);
        AddRow(query: 1, text: 10, plan: 100, interval: 6, closed: false);
        _reader.Texts[10] = "SELECT 1";
        _reader.Plans[100] = ShowPlan;

        var outcome = await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal(IngestionStatus.Succeeded, outcome.Status);
        Assert.Equal(5, outcome.WatermarkAfter);
        Assert.Null(outcome.WatermarkBefore);
        var batch = Assert.Single(_store.Batches);
        Assert.Null(batch.ExpectedWatermark);
        Assert.Equal(5, batch.NewWatermark);
        Assert.Equal(2, batch.Stats.Count);
        Assert.Equal(25d, batch.Stats[0].Metrics.AvgDurationMicroseconds, 9);
        Assert.Equal(_encryptor.Encrypt("SELECT 1"), Assert.Single(batch.Texts).EncryptedSqlText);
        var plan = Assert.Single(batch.Plans);
        Assert.Equal(_encryptor.Encrypt(ShowPlan), plan.EncryptedPlanXml);
        Assert.Equal(PlanClassification.Pending, plan.Classification);
        Assert.Equal(1, _lock.Released);
        Assert.Equal(IngestionStatus.Succeeded, Assert.Single(_store.Runs).Status);
    }

    [Fact]
    public async Task LaPrimeraIngestaUsaLaVentanaInicialYLasSiguientesLaMarca()
    {
        var useCase = UseCase(new IngestionOptions { TopN = 7, InitialLookbackHours = 3 });

        await useCase.ExecuteAsync(_connection.Id, IngestionTrigger.Manual);
        _connection.LastConsolidatedIntervalId = 41;
        await useCase.ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal(new QueryStoreReadRequest(0, _clock.Now.AddHours(-3), 7, true), _reader.Requests[0]);
        Assert.Equal(new QueryStoreReadRequest(41, DateTimeOffset.MinValue, 7, true), _reader.Requests[1]);
    }

    [Fact]
    public async Task TextosYPlanesYaGuardadosNoSeVuelvenALeerNiCifrar()
    {
        AddRow(1, 10, 100, 5);
        AddRow(2, 20, 200, 5);
        _reader.Texts[20] = "SELECT 2";
        _reader.Plans[200] = ShowPlan;
        _store.KnownTexts.Add(10);
        _store.KnownPlans.Add(100);

        await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal([20L], _reader.TextReads);
        Assert.Equal([200L], _reader.PlanReads);
        var batch = Assert.Single(_store.Batches);
        Assert.Equal(2, batch.Queries.Count);
        Assert.Equal(200, Assert.Single(batch.Plans).QueryStorePlanId);
    }

    [Theory]
    [InlineData(false, null, PlanNotAnalyzableCause.PlanNotFound)]
    [InlineData(true, null, PlanNotAnalyzableCause.NullXml)]
    [InlineData(true, "<ShowPlanXML><trunc", PlanNotAnalyzableCause.MalformedXml)]
    public async Task UnPlanIrrecuperableQuedaNoAnalizableYLaIngestaContinua(bool found, string? xml, PlanNotAnalyzableCause cause)
    {
        // CA-HU03.3.
        AddRow(1, 10, 100, 5);
        AddRow(2, 20, 200, 5);
        _reader.Texts[10] = "SELECT 1";
        _reader.Texts[20] = "SELECT 2";
        if (found)
        {
            _reader.Plans[100] = xml;
        }

        _reader.Plans[200] = ShowPlan;

        var outcome = await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal(IngestionStatus.Succeeded, outcome.Status);
        var plans = Assert.Single(_store.Batches).Plans;
        var bad = Assert.Single(plans, p => p.QueryStorePlanId == 100);
        Assert.Equal(PlanAnalysisStatus.NotAnalyzable, bad.Classification.Status);
        Assert.Equal(cause, bad.Classification.Cause);
        Assert.Equal(PlanAnalysisStatus.Pending, Assert.Single(plans, p => p.QueryStorePlanId == 200).Classification.Status);
    }

    [Fact]
    public async Task UnPlanDemasiadoGrandeQuedaNoAnalizableSinGuardarSuXml()
    {
        // Auditoría B2: TooLarge guarda solo la causa; los demás no analizables conservan el XML cifrado.
        AddRow(1, 10, 100, 5);
        AddRow(2, 20, 200, 5);
        _reader.Texts[10] = "SELECT 1";
        _reader.Texts[20] = "SELECT 2";
        _reader.Plans[100] = ShowPlan + new string(' ', 100);
        _reader.Plans[200] = "<ShowPlanXML><trunc";

        await UseCase(new IngestionOptions { MaxPlanCharacters = ShowPlan.Length + 10 }).ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        var plans = Assert.Single(_store.Batches).Plans;
        var large = Assert.Single(plans, p => p.QueryStorePlanId == 100);
        Assert.Equal(PlanNotAnalyzableCause.TooLarge, large.Classification.Cause);
        Assert.Null(large.EncryptedPlanXml);
        var truncated = Assert.Single(plans, p => p.QueryStorePlanId == 200);
        Assert.Equal(PlanNotAnalyzableCause.MalformedXml, truncated.Classification.Cause);
        Assert.NotNull(truncated.EncryptedPlanXml);
    }

    [Theory]
    [InlineData("OFF", IngestionCause.QueryStoreDisabled)]
    [InlineData("ERROR", IngestionCause.QueryStoreNotReadable)]
    [InlineData(null, IngestionCause.QueryStoreNotReadable)]
    public async Task ConQueryStoreSinLecturaSeOmiteConCausaYSinEscribirDatos(string? state, IngestionCause cause)
    {
        // CA-HU02.2 / RF-ING-08.
        _connection.LastConsolidatedIntervalId = 9;
        _reader.Options = new QueryStoreOptions(state, state, null, 60, "AUTO", 30);
        AddRow(1, 10, 100, 12);

        var outcome = await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal(IngestionStatus.Skipped, outcome.Status);
        Assert.Equal(cause, outcome.Cause);
        Assert.False(outcome.ShouldRetry);
        Assert.Empty(_store.Batches);
        Assert.Empty(_reader.Requests);
        Assert.Equal(9, _connection.LastConsolidatedIntervalId);
        var run = Assert.Single(_store.Runs);
        Assert.Equal(IngestionStatus.Skipped, run.Status);
        Assert.Equal(cause.ToString(), run.ErrorCause);
    }

    [Fact]
    public async Task SinFilasEnLaVistaDeOpcionesSeOmite()
    {
        _reader.Options = null;

        var outcome = await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal(IngestionCause.QueryStoreNotReadable, outcome.Cause);
    }

    [Theory]
    [InlineData("[\"TempDbAndLogBytes\"]", false)]
    [InlineData("[]", true)]
    [InlineData(null, true)]
    public async Task ElPerfilDecideSiSeLeenTempDbYBytesDeLog(string? omittedJson, bool include)
    {
        // RNF-COM-04: se omite con aviso, la ingesta no falla.
        _store.Profile!.OmittedFeaturesJson = omittedJson;

        var outcome = await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal(IngestionStatus.Succeeded, outcome.Status);
        Assert.Equal(include, Assert.Single(_reader.Requests).IncludeTempDbAndLog);
        Assert.Equal(include, !outcome.OmittedFeatures.Contains(OmittedFeature.TempDbAndLogBytes));
    }

    [Fact]
    public async Task SinPerfilSeUsaLaSentenciaSinColumnasOpcionales()
    {
        _store.Profile = null;

        await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.False(Assert.Single(_reader.Requests).IncludeTempDbAndLog);
    }

    [Fact]
    public async Task ConElBloqueoTomadoSeOmiteSinLeerLaBase()
    {
        _lock.Busy = true;

        var outcome = await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal(IngestionCause.AlreadyRunning, outcome.Cause);
        Assert.Empty(_reader.Targets);
        Assert.Equal(nameof(IngestionCause.AlreadyRunning), Assert.Single(_store.Runs).ErrorCause);
    }

    [Theory]
    [InlineData(ConnectionStatus.Pending)]
    [InlineData(ConnectionStatus.Failed)]
    [InlineData(ConnectionStatus.Disabled)]
    public async Task UnaConexionNoActivaNoSeIngiere(ConnectionStatus status)
    {
        _connection.Status = status;

        var outcome = await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Scheduled);

        Assert.Equal(IngestionCause.ConnectionNotActive, outcome.Cause);
        Assert.Empty(_reader.Targets);
    }

    [Fact]
    public async Task UnaConexionInexistenteNoDejaRegistro()
    {
        var outcome = await UseCase().ExecuteAsync(Guid.NewGuid(), IngestionTrigger.Manual);

        Assert.Null(outcome.Status);
        Assert.Empty(_store.Runs);
    }

    [Theory]
    [InlineData(TelemetryFailureKind.ServerUnreachable, IngestionCause.ServerUnreachable, true)]
    [InlineData(TelemetryFailureKind.Timeout, IngestionCause.Timeout, true)]
    [InlineData(TelemetryFailureKind.Unknown, IngestionCause.Unknown, true)]
    [InlineData(TelemetryFailureKind.PermissionDenied, IngestionCause.MissingPermission, false)]
    [InlineData(TelemetryFailureKind.InvalidCredentials, IngestionCause.InvalidCredentials, false)]
    [InlineData(TelemetryFailureKind.TlsCertificateInvalid, IngestionCause.TlsCertificateInvalid, false)]
    public async Task LosFallosDeTelemetriaQuedanConCodigoYSoloLosTransitoriosSeReintentan(
        TelemetryFailureKind kind, IngestionCause cause, bool retry)
    {
        _connection.LastConsolidatedIntervalId = 7;
        _reader.FailStats = new TelemetrySourceException(kind, 10054);

        var outcome = await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal(IngestionStatus.Failed, outcome.Status);
        Assert.Equal(cause, outcome.Cause);
        Assert.Equal(retry, outcome.ShouldRetry);
        Assert.Equal(7, outcome.WatermarkAfter);
        Assert.Empty(_store.Batches);
        var run = Assert.Single(_store.Runs);
        Assert.Equal(cause.ToString(), run.ErrorCause);
    }

    [Fact]
    public async Task UnFalloDePersistenciaNoGuardaElTextoDeLaExcepcion()
    {
        _store.FailApply = new InvalidOperationException("Host=db;Password=secreta");

        var outcome = await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal(IngestionCause.Unknown, outcome.Cause);
        Assert.True(outcome.ShouldRetry);
        Assert.Equal(nameof(IngestionCause.Unknown), Assert.Single(_store.Runs).ErrorCause);
    }

    [Fact]
    public async Task UnaEjecucionColgadaSeCierraComoInterrumpida()
    {
        _store.Runs.Add(new IngestionRun { Id = Guid.NewGuid(), ConnectionId = _connection.Id, Status = IngestionStatus.Running });

        await UseCase().ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        Assert.Equal(1, _store.InterruptedClosed);
        Assert.Equal(nameof(IngestionCause.Interrupted), _store.Runs[0].ErrorCause);
    }

    [Fact]
    public async Task LaExcepcionDeTlsDelLaboratorioSeAplicaComoEnElSondeo()
    {
        var useCase = new IngestConnection(_store, _reader, _lock, _encryptor, _clock, new IngestionOptions(),
            new ProbeOptions { LabTrustedCertificateServers = ["lab"] });

        await useCase.ExecuteAsync(_connection.Id, IngestionTrigger.Manual);

        var target = Assert.Single(_reader.Targets);
        Assert.True(target.TrustServerCertificate);
        Assert.Equal(60, target.ConnectionTimeoutSeconds);
    }

    [Theory]
    [InlineData(0, 24, 60, 600)]
    [InlineData(5001, 24, 60, 600)]
    [InlineData(10, 0, 60, 600)]
    [InlineData(10, 24, 0, 600)]
    [InlineData(10, 24, 60, 30)]
    public void LasOpcionesInvalidasSeRechazan(int topN, int lookback, int statementTimeout, int timeout)
    {
        var options = new IngestionOptions
        {
            TopN = topN,
            InitialLookbackHours = lookback,
            StatementTimeoutSeconds = statementTimeout,
            TimeoutSeconds = timeout,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => UseCase(options));
    }
}
