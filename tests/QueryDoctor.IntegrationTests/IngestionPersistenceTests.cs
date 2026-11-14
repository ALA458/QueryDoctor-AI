using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Ingestion;
using QueryDoctor.Infrastructure.Persistence;
using QueryDoctor.Infrastructure.Security;
using QueryDoctor.UnitTests.Ingestion;
using static QueryDoctor.UnitTests.Ingestion.IngestionSamples;

namespace QueryDoctor.IntegrationTests;

// S1-11 contra PostgreSQL real (Testcontainers): idempotencia (CA-HU03.2), atomicidad de upsert + marca de agua
// (CA-HU03.4, RNF-FIA-01), bloqueo por conexión y Query Store OFF sin borrar datos (CA-HU02.2).
[Collection(PostgresGroup.Name)]
public sealed class IngestionPersistenceTests(PostgresFixture postgres) : IDisposable
{
    private const string ShowPlan = "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\"><BatchSequence/></ShowPlanXML>";

    private readonly SecretProtector _encryptor = new(Options.Create(new SecretProtectorOptions
    {
        MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    }));

    public void Dispose() => _encryptor.Dispose();

    private async Task<Guid> SeedConnectionAsync()
    {
        await using var db = postgres.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = "ing-" + Guid.NewGuid().ToString("N")[..12],
            DatabaseName = "QD_Lab",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = ConnectionCredentialsCodec.Encrypt(new ConnectionCredentials("qd_reader", "S3creta!"), _encryptor),
            Status = ConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Connections.Add(connection);
        db.CapabilityProfiles.Add(new CapabilityProfile
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            EngineVersion = "16.0.4315.2",
            SupportLevel = SupportLevel.Tested,
            OmittedFeaturesJson = "[]",
            ProbedAt = now,
        });
        await db.SaveChangesAsync();
        return connection.Id;
    }

    private static FakeQueryStoreReader ReaderWithData()
    {
        var reader = new FakeQueryStoreReader();
        void Add(long query, long text, long plan, long interval, long count, double avg, bool closed = true, double? optional = 2d) =>
            reader.Rows.Add(new QueryStoreStatRow(query, text, "0A1B2C3D4E5F6071", "8899AABBCCDDEEFF", "16.0.4315.2", "dbo.usp_ESC01",
                Row(plan, interval, Metrics(count, avg, optional), closed)));

        // Intervalo 5 con dos filas del mismo plan (RF-ING-04), intervalo 6 cerrado y 7 activo.
        Add(1, 10, 100, 5, 1, 10d);
        Add(1, 10, 100, 5, 3, 30d);
        Add(1, 10, 100, 6, 2, 50d);
        Add(2, 20, 200, 6, 4, 80d, optional: null);
        Add(2, 20, 201, 7, 1, 5d, closed: false);
        reader.Texts[10] = "SELECT * FROM dbo.Orders WHERE CustomerCode = @code";
        reader.Texts[20] = "SELECT 2";
        reader.Plans[100] = ShowPlan;
        reader.Plans[200] = ShowPlan;
        reader.Plans[201] = "<ShowPlanXML><RelOp"; // truncado (RF-ING-09)
        return reader;
    }

    private IngestConnection UseCase(AppDbContext db, FakeQueryStoreReader reader) =>
        new(new EfIngestionStore(db), reader, new PostgresIngestionLock(postgres.ConnectionString), _encryptor,
            TimeProvider.System, new IngestionOptions(), new ProbeOptions());

    private async Task<IngestionOutcome> IngestAsync(Guid connectionId, FakeQueryStoreReader reader, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString);
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        await using var db = new AppDbContext(options.Options);
        return await UseCase(db, reader).ExecuteAsync(connectionId, IngestionTrigger.Manual);
    }

    private async Task<Snapshot> SnapshotAsync(Guid connectionId)
    {
        await using var db = postgres.CreateContext();
        return new Snapshot(
            (await db.Connections.AsNoTracking().SingleAsync(c => c.Id == connectionId)).LastConsolidatedIntervalId,
            await db.QueryTexts.AsNoTracking().Where(t => t.ConnectionId == connectionId).OrderBy(t => t.QueryStoreTextId)
                .Select(t => $"{t.Id}|{t.QueryStoreTextId}|{t.EncryptedSqlText}|{t.IngestedAt:O}").ToListAsync(),
            await db.Queries.AsNoTracking().Where(q => q.ConnectionId == connectionId).OrderBy(q => q.QueryStoreQueryId)
                .Select(q => $"{q.Id}|{q.QueryStoreQueryId}|{q.QueryTextId}|{q.QueryHash}|{q.OriginContext}|{q.IngestedAt:O}").ToListAsync(),
            await db.Plans.AsNoTracking().Where(p => p.ConnectionId == connectionId).OrderBy(p => p.QueryStorePlanId)
                .Select(p => $"{p.Id}|{p.QueryStorePlanId}|{p.QueryId}|{p.EncryptedPlanXml}|{p.AnalysisStatus}|{p.NotAnalyzableCause}|{p.IngestedAt:O}").ToListAsync(),
            await db.RuntimeStats.AsNoTracking().Where(r => r.ConnectionId == connectionId)
                .OrderBy(r => r.IntervalId).ThenBy(r => r.PlanId).ThenBy(r => r.ExecutionType)
                .Select(r => $"{r.Id}|{r.PlanId}|{r.ExecutionType}|{r.IntervalId}|{r.ExecutionCount}|{r.AvgDurationMicroseconds}|{r.AvgTempDbSpacePages}|{r.IngestedAt:O}")
                .ToListAsync());
    }

    private async Task<List<IngestionRun>> RunsAsync(Guid connectionId)
    {
        await using var db = postgres.CreateContext();
        return await db.IngestionRuns.AsNoTracking().Where(r => r.ConnectionId == connectionId).OrderBy(r => r.StartedAt).ToListAsync();
    }

    [Fact]
    public async Task DosIngestasSeguidasNoDuplicanNiCambianValores()
    {
        // CA-HU03.2.
        var id = await SeedConnectionAsync();
        var reader = ReaderWithData();

        var first = await IngestAsync(id, reader);
        var afterFirst = await SnapshotAsync(id);
        var second = await IngestAsync(id, reader);
        var afterSecond = await SnapshotAsync(id);

        Assert.Equal(IngestionStatus.Succeeded, first.Status);
        Assert.Equal(IngestionStatus.Succeeded, second.Status);
        Assert.Equal(6, afterFirst.Watermark);
        Assert.Equal(2, afterFirst.Texts.Count);
        Assert.Equal(2, afterFirst.Queries.Count);
        Assert.Equal(3, afterFirst.Plans.Count);
        Assert.Equal(4, afterFirst.Stats.Count);

        // Mismas filas, mismos ids y mismos valores (también ingested_at y el texto cifrado).
        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(0, second.RowsUpserted);
        Assert.Equal(2, (await RunsAsync(id)).Count(r => r.Status == IngestionStatus.Succeeded));
    }

    [Fact]
    public async Task LosDatosQuedanCifradosYPonderadosYElPlanTruncadoNoAnalizable()
    {
        // RF-ING-04, RF-ING-07, RF-ING-09, RNF-SEG-14.
        var id = await SeedConnectionAsync();
        await IngestAsync(id, ReaderWithData());

        await using var db = postgres.CreateContext();
        var text = await db.QueryTexts.SingleAsync(t => t.ConnectionId == id && t.QueryStoreTextId == 10);
        Assert.DoesNotContain("Orders", text.EncryptedSqlText, StringComparison.Ordinal);
        Assert.Equal("SELECT * FROM dbo.Orders WHERE CustomerCode = @code", _encryptor.Decrypt(text.EncryptedSqlText!));

        var plan100 = await db.Plans.SingleAsync(p => p.ConnectionId == id && p.QueryStorePlanId == 100);
        var stat = await db.RuntimeStats.SingleAsync(r => r.PlanId == plan100.Id && r.IntervalId == 5);
        Assert.Equal(4, stat.ExecutionCount);
        Assert.Equal(25d, stat.AvgDurationMicroseconds, 9);
        Assert.Equal(PlanAnalysisStatus.Pending, plan100.AnalysisStatus);
        Assert.Equal(ShowPlan, _encryptor.Decrypt(plan100.EncryptedPlanXml!));

        var truncated = await db.Plans.SingleAsync(p => p.ConnectionId == id && p.QueryStorePlanId == 201);
        Assert.Equal(PlanAnalysisStatus.NotAnalyzable, truncated.AnalysisStatus);
        Assert.Equal(nameof(PlanNotAnalyzableCause.MalformedXml), truncated.NotAnalyzableCause);

        var query = await db.Queries.SingleAsync(q => q.ConnectionId == id && q.QueryStoreQueryId == 1);
        Assert.Equal(text.Id, query.QueryTextId);
        Assert.Equal("0A1B2C3D4E5F6071", query.QueryHash);
        Assert.Null((await db.RuntimeStats.SingleAsync(r => r.ConnectionId == id && r.IntervalId == 6 && r.ExecutionCount == 4)).AvgTempDbSpacePages);
    }

    [Fact]
    public async Task ElIntervaloActivoSeActualizaYElCerradoNo()
    {
        // RF-ING-05: el activo se vuelve a leer y el upsert lo actualiza sin duplicar.
        var id = await SeedConnectionAsync();
        var reader = ReaderWithData();
        await IngestAsync(id, reader);
        var before = await SnapshotAsync(id);

        // Siguiente ingesta: Query Store ya solo devuelve lo posterior a la marca (6): el intervalo 7, con más ejecuciones.
        reader.Rows.RemoveAll(r => r.Stat.IntervalId <= 6);
        var open = reader.Rows.Single();
        reader.Rows[0] = open with { Stat = open.Stat with { Metrics = Metrics(3, 7d), IntervalClosed = true } };

        var outcome = await IngestAsync(id, reader);
        var after = await SnapshotAsync(id);

        Assert.Equal(6, Assert.Single(reader.Requests.Skip(1)).AfterIntervalId);
        Assert.Equal(7, after.Watermark);
        Assert.Equal(1, outcome.RowsUpserted);
        Assert.Equal(before.Stats.Count, after.Stats.Count);
        Assert.Equal(before.Stats.Take(3), after.Stats.Take(3));
        Assert.Contains("|7|3|7|", after.Stats[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnFalloAMitadNoAvanzaLaMarcaNiDejaDatosYLaSiguienteIngestaCompleta()
    {
        // CA-HU03.4 / RNF-FIA-01: el fallo llega DESPUÉS del upsert y del avance de la marca, dentro de la transacción.
        var id = await SeedConnectionAsync();
        var reader = ReaderWithData();

        var failed = await IngestAsync(id, reader, new FailOnSucceededRunUpdate());
        var afterFailure = await SnapshotAsync(id);

        Assert.Equal(IngestionStatus.Failed, failed.Status);
        Assert.Equal(IngestionCause.Unknown, failed.Cause);
        Assert.True(failed.ShouldRetry);
        Assert.Null(afterFailure.Watermark);
        Assert.Empty(afterFailure.Texts);
        Assert.Empty(afterFailure.Queries);
        Assert.Empty(afterFailure.Plans);
        Assert.Empty(afterFailure.Stats);

        var retried = await IngestAsync(id, reader);
        var afterRetry = await SnapshotAsync(id);

        Assert.Equal(IngestionStatus.Succeeded, retried.Status);
        Assert.Equal(6, afterRetry.Watermark);
        Assert.Equal(4, afterRetry.Stats.Count);
        Assert.Equal(0, reader.Requests[1].AfterIntervalId);

        var runs = await RunsAsync(id);
        Assert.Equal([IngestionStatus.Failed, IngestionStatus.Succeeded], runs.Select(r => r.Status));
        Assert.Equal(nameof(IngestionCause.Unknown), runs[0].ErrorCause);
    }

    [Fact]
    public async Task DosIngestasConcurrentesDeLaMismaConexionSoloCorreUna()
    {
        var id = await SeedConnectionAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = ReaderWithData();
        slow.Gate = release.Task;

        var first = IngestAsync(id, slow);
        await slow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var other = ReaderWithData();
        var second = await IngestAsync(id, other);
        release.SetResult();
        var firstOutcome = await first;

        Assert.Equal(IngestionStatus.Skipped, second.Status);
        Assert.Equal(IngestionCause.AlreadyRunning, second.Cause);
        Assert.Empty(other.Targets);
        Assert.Equal(IngestionStatus.Succeeded, firstOutcome.Status);

        // Liberado el bloqueo, la siguiente corre.
        Assert.Equal(IngestionStatus.Succeeded, (await IngestAsync(id, ReaderWithData())).Status);
    }

    [Fact]
    public async Task ElBloqueoEsPorConexion()
    {
        var ingestionLock = new PostgresIngestionLock(postgres.ConnectionString);
        var a = Guid.NewGuid();

        await using (var held = await ingestionLock.TryAcquireAsync(a, CancellationToken.None))
        {
            Assert.NotNull(held);
            Assert.Null(await ingestionLock.TryAcquireAsync(a, CancellationToken.None));
            await using var other = await ingestionLock.TryAcquireAsync(Guid.NewGuid(), CancellationToken.None);
            Assert.NotNull(other);
        }

        await using var again = await ingestionLock.TryAcquireAsync(a, CancellationToken.None);
        Assert.NotNull(again);
        Assert.Equal(PostgresIngestionLock.KeyFor(a), PostgresIngestionLock.KeyFor(a));
    }

    [Fact]
    public async Task ConQueryStoreOffSeOmiteConCausaYSeConservanLosDatos()
    {
        // CA-HU02.2 / RF-ING-08.
        var id = await SeedConnectionAsync();
        var reader = ReaderWithData();
        await IngestAsync(id, reader);
        var before = await SnapshotAsync(id);

        reader.Options = new QueryStoreOptions("OFF", "OFF", null, 60, "AUTO", 30);
        var outcome = await IngestAsync(id, reader);

        Assert.Equal(IngestionStatus.Skipped, outcome.Status);
        Assert.Equal(IngestionCause.QueryStoreDisabled, outcome.Cause);
        Assert.Equal(before, await SnapshotAsync(id));

        var last = (await RunsAsync(id))[^1];
        Assert.Equal(IngestionStatus.Skipped, last.Status);
        Assert.Equal(nameof(IngestionCause.QueryStoreDisabled), last.ErrorCause);
        Assert.NotNull(last.FinishedAt);
    }

    private sealed record Snapshot(long? Watermark, List<string> Texts, List<string> Queries, List<string> Plans, List<string> Stats)
    {
        public bool Equals(Snapshot? other) =>
            other is not null
            && Watermark == other.Watermark
            && Texts.SequenceEqual(other.Texts)
            && Queries.SequenceEqual(other.Queries)
            && Plans.SequenceEqual(other.Plans)
            && Stats.SequenceEqual(other.Stats);

        public override int GetHashCode() => HashCode.Combine(Watermark, Texts.Count, Stats.Count);
    }

    /// <summary>Simula un fallo justo antes de confirmar: al cerrar la ejecución, que va después de la marca de agua.</summary>
    private sealed class FailOnSucceededRunUpdate : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE core.ingestion_run", StringComparison.Ordinal)
                && command.CommandText.Contains("rows_upserted", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Fallo simulado a mitad de la ingesta.");
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
