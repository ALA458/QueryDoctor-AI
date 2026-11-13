using System.Reflection;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Connections;

namespace QueryDoctor.UnitTests.Connections;

/// <summary>Auditoría M1: un fallo de persistencia no puede dejar la conexión en Pending para siempre (RF-CFG-02).</summary>
public class ProbeConnectionJobTests
{
    private readonly InMemoryConnectionRepository _repository = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.Zero));

    private ProbeConnectionJob Job()
    {
        var probe = new ProbeConnection(_repository, new UnusedProbe(), new FakeEncryptor(), _clock, new ProbeOptions());
        var services = new ServiceCollection()
            .AddSingleton<IConnectionRepository>(_repository)
            .BuildServiceProvider();

        return new ProbeConnectionJob(
            probe, services.GetRequiredService<IServiceScopeFactory>(), _clock, NullLogger<ProbeConnectionJob>.Instance);
    }

    private Connection Pending()
    {
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Server = "sqlserver",
            DatabaseName = "QD_Lab",
            Status = ConnectionStatus.Pending,
        };
        _repository.Stored.Add(connection);
        return connection;
    }

    [Fact]
    public async Task SiPostgresFallaDuranteElSondeoLaConexionQuedaFailedUnknown()
    {
        var connection = Pending();
        _repository.FailReads = true;

        await Job().RunAsync(connection.Id, CancellationToken.None);

        Assert.Equal(ConnectionStatus.Failed, connection.Status);
        Assert.Equal(ProbeFailureCause.Unknown, connection.ProbeFailureCause);
        Assert.Equal(_clock.Now, connection.LastProbedAt);
    }

    [Fact]
    public async Task SiTampocoSePuedeMarcarElTrabajoFallaParaQueHangfireReintente()
    {
        var connection = Pending();
        _repository.FailReads = true;
        _repository.FailMarks = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Job().RunAsync(connection.Id, CancellationToken.None));

        Assert.Equal(ConnectionStatus.Pending, connection.Status);
    }

    [Fact]
    public async Task ElRespaldoNoPisaUnaConexionQueYaNoEstaPendiente()
    {
        var connection = Pending();
        connection.Status = ConnectionStatus.Active;
        _repository.FailReads = true;

        await Job().RunAsync(connection.Id, CancellationToken.None);

        Assert.Equal(ConnectionStatus.Active, connection.Status);
        Assert.Null(connection.ProbeFailureCause);
    }

    [Fact]
    public void HangfireReintentaSoloLosFallosQueEscapanDelSondeo()
    {
        var retry = typeof(ProbeConnectionJob)
            .GetMethod(nameof(ProbeConnectionJob.RunAsync))!
            .GetCustomAttribute<AutomaticRetryAttribute>();

        Assert.NotNull(retry);
        Assert.Equal(ProbeConnectionJob.PersistenceRetryAttempts, retry.Attempts);
        Assert.True(retry.Attempts > 0);
    }

    /// <summary>Con la lectura caída el sondeo nunca llega a SQL Server.</summary>
    private sealed class UnusedProbe : ICapabilityProbe
    {
        public Task<EngineInfo> GetEngineAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No debía sondear");

        public Task<IReadOnlyList<string>> GetDatabasePermissionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No debía sondear");

        public Task<QueryStoreOptions?> GetQueryStoreOptionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No debía sondear");

        public Task<IReadOnlyList<string>> GetRuntimeStatsColumnsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No debía sondear");
    }
}
