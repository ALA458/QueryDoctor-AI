using QueryDoctor.Application.Retention;
using QueryDoctor.UnitTests.Connections;

namespace QueryDoctor.UnitTests.Retention;

/// <summary>S2-13 (RNF-SEG-10): validación de opciones y cálculo de fecha límite en retención.</summary>
public sealed class RetentionUseCasesTests
{
    [Theory]
    [InlineData(0, "15 2 * * *")]
    [InlineData(-5, "15 2 * * *")]
    [InlineData(30, "")]
    [InlineData(30, "   ")]
    public void RechazaConfiguracionDeRetencionInvalida(int days, string cron)
    {
        var options = new RetentionOptions { DefaultDays = days, Cron = cron };
        Assert.ThrowsAny<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public async Task PurgaAutomaticaCalculaFechaLimiteExactaSegunDiasConfigurados()
    {
        var now = new DateTimeOffset(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var store = new SpyRetentionStore();
        var useCase = new PurgeExpiredTelemetry(store, clock, new RetentionOptions { DefaultDays = 14 });

        var outcome = await useCase.ExecuteAsync();

        Assert.Equal(2, outcome.TextsCleared);
        Assert.Equal(3, outcome.PlansCleared);
        Assert.Equal(now.AddDays(-14), store.LastCutoffUtc);
        Assert.Equal(now, store.LastNowUtc);
    }

    [Fact]
    public async Task BorradoPorDbaTransfiereActorYConexion()
    {
        var now = new DateTimeOffset(2026, 11, 15, 19, 30, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var store = new SpyRetentionStore();
        var useCase = new DeleteConnectionTelemetry(store, clock);
        var connectionId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var result = await useCase.ExecuteAsync(connectionId, actorId, "dba.principal");

        var found = Assert.IsType<DeleteConnectionTelemetryOutcome.Found>(result);
        Assert.Equal(connectionId, found.ConnectionId);
        Assert.Equal((connectionId, actorId, "dba.principal", now), store.LastDeleteCall);
    }

    private sealed class SpyRetentionStore : IRetentionStore
    {
        public DateTimeOffset? LastCutoffUtc { get; private set; }
        public DateTimeOffset? LastNowUtc { get; private set; }
        public (Guid ConnectionId, Guid? ActorId, string? ActorName, DateTimeOffset NowUtc)? LastDeleteCall { get; private set; }

        public Task<RetentionPurgeOutcome> PurgeExpiredAsync(DateTimeOffset cutoffUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            LastCutoffUtc = cutoffUtc;
            LastNowUtc = nowUtc;
            return Task.FromResult(new RetentionPurgeOutcome(2, 3));
        }

        public Task<DeleteConnectionTelemetryOutcome> DeleteConnectionDataAsync(
            Guid connectionId,
            Guid? actorUserId,
            string? actorUserName,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            LastDeleteCall = (connectionId, actorUserId, actorUserName, nowUtc);
            return Task.FromResult<DeleteConnectionTelemetryOutcome>(
                new DeleteConnectionTelemetryOutcome.Found(connectionId, 1, 1));
        }
    }
}
