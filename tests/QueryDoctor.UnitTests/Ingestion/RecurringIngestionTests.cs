using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Connections;
using QueryDoctor.UnitTests.Connections;

namespace QueryDoctor.UnitTests.Ingestion;

/// <summary>S1-10 / S1-11 (RF-ING-06, DA-03): programación de ingesta periódica tras sondeo activo.</summary>
public sealed class RecurringIngestionTests
{
    [Theory]
    [InlineData(null, "5 * * * *")]
    [InlineData(0, "5 * * * *")]
    [InlineData(1, "* * * * *")]
    [InlineData(5, "4,9,14,19,24,29,34,39,44,49,54,59 * * * *")]
    [InlineData(10, "5,15,25,35,45,55 * * * *")]
    [InlineData(15, "5,20,35,50 * * * *")]
    [InlineData(30, "5,35 * * * *")]
    [InlineData(60, "5 * * * *")]
    [InlineData(120, "5 */2 * * *")]
    [InlineData(1440, "5 0 * * *")]
    public void CalculaExpresionCronSegunIntervaloDeQueryStore(int? intervalMinutes, string expectedCron)
    {
        var actual = IngestionCronSchedule.FromIntervalMinutes(intervalMinutes);
        Assert.Equal(expectedCron, actual);
    }

    [Fact]
    public async Task SondeoExitosoProgramaIngestaPeriodicaConIntervaloDetectado()
    {
        var repo = new InMemoryConnectionRepository();
        var encryptor = new FakeEncryptor();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = "sql.internal",
            DatabaseName = "AppDb",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = ConnectionCredentialsCodec.Encrypt(new ConnectionCredentials("reader", "pass"), encryptor),
            Status = ConnectionStatus.Pending,
        };
        repo.Stored.Add(connection);

        var scheduler = new RecordingRecurringScheduler();
        var probe = new ProbeConnection(repo, new ActiveSql2022Probe(intervalMinutes: 15), encryptor, clock, new ProbeOptions());
        var services = new ServiceCollection().AddSingleton<IConnectionRepository>(repo).BuildServiceProvider();
        var job = new ProbeConnectionJob(
            probe,
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<ProbeConnectionJob>.Instance,
            scheduler);

        await job.RunAsync(connection.Id, CancellationToken.None);

        Assert.Equal(ConnectionStatus.Active, connection.Status);
        var scheduled = Assert.Single(scheduler.Scheduled);
        Assert.Equal((connection.Id, 15), scheduled);
        Assert.Empty(scheduler.Removed);
    }

    [Fact]
    public async Task SondeoFallidoDesprogramaIngestaPeriodica()
    {
        var repo = new InMemoryConnectionRepository();
        var encryptor = new FakeEncryptor();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = "sql.internal",
            DatabaseName = "AppDb",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = null,
            Status = ConnectionStatus.Pending,
        };
        repo.Stored.Add(connection);

        var scheduler = new RecordingRecurringScheduler();
        var probe = new ProbeConnection(repo, new ActiveSql2022Probe(intervalMinutes: 60), encryptor, clock, new ProbeOptions());
        var services = new ServiceCollection().AddSingleton<IConnectionRepository>(repo).BuildServiceProvider();
        var job = new ProbeConnectionJob(
            probe,
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<ProbeConnectionJob>.Instance,
            scheduler);

        await job.RunAsync(connection.Id, CancellationToken.None);

        Assert.Equal(ConnectionStatus.Failed, connection.Status);
        Assert.Empty(scheduler.Scheduled);
        Assert.Equal([connection.Id], scheduler.Removed);
    }

    private sealed class RecordingRecurringScheduler : IRecurringIngestionScheduler
    {
        public List<(Guid ConnectionId, int? IntervalMinutes)> Scheduled { get; } = [];
        public List<Guid> Removed { get; } = [];

        public void ScheduleOrUpdate(Guid connectionId, int? intervalLengthMinutes) =>
            Scheduled.Add((connectionId, intervalLengthMinutes));

        public void Remove(Guid connectionId) => Removed.Add(connectionId);
    }

    private sealed class ActiveSql2022Probe(int intervalMinutes) : ICapabilityProbe
    {
        public Task<EngineInfo> GetEngineAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult(new EngineInfo("16.0.4315.2", "Developer Edition (64-bit)", 3));

        public Task<IReadOnlyList<string>> GetDatabasePermissionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["VIEW DATABASE PERFORMANCE STATE"]);

        public Task<QueryStoreOptions?> GetQueryStoreOptionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult<QueryStoreOptions?>(new QueryStoreOptions("READ_WRITE", "READ_WRITE", null, intervalMinutes, "AUTO", 30));

        public Task<IReadOnlyList<string>> GetRuntimeStatsColumnsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["avg_tempdb_space_used", "avg_log_bytes_used"]);
    }
}
