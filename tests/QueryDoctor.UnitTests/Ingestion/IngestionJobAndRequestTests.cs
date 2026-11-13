using System.Reflection;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Ingestion;
using QueryDoctor.UnitTests.Connections;

namespace QueryDoctor.UnitTests.Ingestion;

// Trabajo ingest_connection: cola, argumentos, reintentos (RNF-FIA-01) y petición manual desde la Api (RF-ING-06).
public class IngestionJobAndRequestTests
{
    private readonly FakeEncryptor _encryptor = new();
    private readonly FakeQueryStoreReader _reader = new();
    private readonly InMemoryIngestionStore _store = new();

    private Connection AddActive()
    {
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Server = "lab",
            DatabaseName = "QD_Lab",
            Status = ConnectionStatus.Active,
            EncryptedCredentials = ConnectionCredentialsCodec.Encrypt(new ConnectionCredentials("qd_reader", "S3creta!"), _encryptor),
        };
        _store.Connection = connection;
        _store.Profile = new CapabilityProfile { ConnectionId = connection.Id, OmittedFeaturesJson = "[]" };
        return connection;
    }

    private IngestConnectionJob Job() => new(
        new IngestConnection(_store, _reader, new FakeIngestionLock(), _encryptor, TimeProvider.System, new IngestionOptions(), new ProbeOptions()),
        NullLogger<IngestConnectionJob>.Instance);

    [Theory]
    [InlineData(TelemetryFailureKind.ServerUnreachable)]
    [InlineData(TelemetryFailureKind.Timeout)]
    public async Task UnFalloTransitorioSeRelanzaParaQueHangfireReintente(TelemetryFailureKind kind)
    {
        var connection = AddActive();
        _reader.FailStats = new TelemetrySourceException(kind, null);

        var ex = await Assert.ThrowsAsync<IngestionRetryException>(() => Job().RunManualAsync(connection.Id, CancellationToken.None));

        // El mensaje lleva solo el código, nunca texto del motor (RNF-SEG-09).
        Assert.DoesNotContain("lab", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(IngestionTrigger.Manual, Assert.Single(_store.Runs).Trigger);
    }

    [Theory]
    [InlineData(TelemetryFailureKind.InvalidCredentials)]
    [InlineData(TelemetryFailureKind.PermissionDenied)]
    [InlineData(TelemetryFailureKind.TlsCertificateInvalid)]
    public async Task UnFalloPermanenteNoSeReintenta(TelemetryFailureKind kind)
    {
        var connection = AddActive();
        _reader.FailStats = new TelemetrySourceException(kind, null);

        await Job().RunScheduledAsync(connection.Id, CancellationToken.None);

        var run = Assert.Single(_store.Runs);
        Assert.Equal(IngestionStatus.Failed, run.Status);
        Assert.Equal(IngestionTrigger.Scheduled, run.Trigger);
    }

    [Fact]
    public async Task QueryStoreOffNoSeReintenta()
    {
        var connection = AddActive();
        _reader.Options = new QueryStoreOptions("OFF", "OFF", null, 60, "AUTO", 30);

        await Job().RunManualAsync(connection.Id, CancellationToken.None);

        Assert.Equal(IngestionStatus.Skipped, Assert.Single(_store.Runs).Status);
    }

    [Theory]
    [InlineData(nameof(IngestConnectionJob.RunManualAsync))]
    [InlineData(nameof(IngestConnectionJob.RunScheduledAsync))]
    public void ElTrabajoVaEnLaColaIngestConReintentoExponencialYSoloRecibeElId(string method)
    {
        var info = typeof(IngestConnectionJob).GetMethod(method)!;

        Assert.Equal(JobQueues.Ingest, info.GetCustomAttribute<QueueAttribute>()!.Queue);
        var retry = info.GetCustomAttribute<AutomaticRetryAttribute>()!;
        Assert.Equal(IngestConnectionJob.RetryDelaysInSeconds.Length, retry.Attempts);
        Assert.Equal(IngestConnectionJob.RetryDelaysInSeconds, retry.DelaysInSeconds);

        // Invariante 2: argumentos solo identificadores (el token de cancelación lo pone Hangfire).
        Assert.Equal([typeof(Guid), typeof(CancellationToken)], info.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public async Task LaPeticionManualSoloEncolaConexionesActivas()
    {
        var repository = new InMemoryConnectionRepository();
        var scheduler = new RecordingIngestionScheduler();
        var useCase = new RequestIngestion(repository, scheduler);
        var active = new Connection { Id = Guid.NewGuid(), Status = ConnectionStatus.Active };
        var pending = new Connection { Id = Guid.NewGuid(), Status = ConnectionStatus.Pending };
        repository.Stored.AddRange([active, pending]);

        Assert.IsType<RequestIngestionResult.Accepted>(await useCase.ExecuteAsync(active.Id));
        Assert.Equal(ConnectionStatus.Pending, Assert.IsType<RequestIngestionResult.NotActive>(await useCase.ExecuteAsync(pending.Id)).Status);
        Assert.IsType<RequestIngestionResult.NotFound>(await useCase.ExecuteAsync(Guid.NewGuid()));
        Assert.Equal([active.Id], scheduler.Enqueued);
    }

    [Theory]
    [InlineData(nameof(IngestionCause.QueryStoreDisabled), IngestionCause.QueryStoreDisabled)]
    [InlineData("algo-que-no-es-un-codigo", IngestionCause.Unknown)]
    [InlineData(null, null)]
    public void LaVistaDeUnaEjecucionTraduceLaCausaAUnMensajeFijo(string? stored, IngestionCause? expected)
    {
        var view = IngestionRunView.From(new IngestionRun { Id = Guid.NewGuid(), Status = IngestionStatus.Skipped, ErrorCause = stored });

        Assert.Equal(expected, view.Cause);
        Assert.Equal(expected is null ? null : IngestionCauseMessages.For(expected.Value), view.CauseMessage);
    }

    [Fact]
    public void TodasLasCausasTienenMensajePropio()
    {
        var generic = IngestionCauseMessages.For(IngestionCause.Unknown);
        foreach (var cause in Enum.GetValues<IngestionCause>().Where(c => c != IngestionCause.Unknown))
        {
            Assert.NotEqual(generic, IngestionCauseMessages.For(cause));
        }
    }

    private sealed class RecordingIngestionScheduler : IIngestionJobScheduler
    {
        public List<Guid> Enqueued { get; } = [];

        public void EnqueueManual(Guid connectionId) => Enqueued.Add(connectionId);
    }
}
