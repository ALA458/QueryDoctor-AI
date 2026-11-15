using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;
using QueryDoctor.Infrastructure;
using QueryDoctor.UnitTests.Ingestion;
using QueryDoctor.UnitTests.Security;

namespace QueryDoctor.IntegrationTests;

/// <summary>
/// S2-12: el Worker es un proceso de la plataforma y, con su acceso Sistema declarado, sigue sondeando e ingiriendo
/// todas las conexiones, también las que ningún Desarrollador tiene asignadas. Se arma con la misma DI que
/// <c>QueryDoctor.Worker/Program.cs</c>; solo el lector de Query Store y el sondeo son dobles (no hay SQL Server en CI).
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class WorkerSystemAccessTests(PostgresFixture postgres)
{
    private const string ShowPlan = "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\"><BatchSequence/></ShowPlanXML>";

    [Fact]
    public async Task ConAccesoSistemaElWorkerSondeaEIngiereAmbasConexiones()
    {
        await using var worker = Worker(access: null);
        var a = await SeedPendingAsync(worker);
        var b = await SeedPendingAsync(worker);
        await AssignToSomeDeveloperAsync(a);

        foreach (var id in new[] { a, b })
        {
            using var scope = worker.CreateScope();
            var probe = await scope.ServiceProvider.GetRequiredService<ProbeConnection>().ExecuteAsync(id);
            Assert.True(probe.Ran, $"El Worker no sondeó {id}.");
            Assert.Equal(ConnectionStatus.Active, probe.Status);
        }

        foreach (var id in new[] { a, b })
        {
            using var scope = worker.CreateScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<IngestConnection>().ExecuteAsync(id, IngestionTrigger.Scheduled);
            Assert.Equal(IngestionStatus.Succeeded, outcome.Status);
            Assert.True(outcome.RowsUpserted > 0);
        }

        await using var db = postgres.CreateContext();
        Assert.Equal(2, db.IngestionRuns.Count(r => (r.ConnectionId == a || r.ConnectionId == b) && r.Status == IngestionStatus.Succeeded));
        Assert.Equal(2, db.Queries.Count(q => q.ConnectionId == a || q.ConnectionId == b));
    }

    [Fact]
    public async Task SinAccesoSistemaElMismoTrabajoNoVeLaConexion()
    {
        // Contraprueba: si un host declarara un acceso sin usuario (None), el trabajo no vería nada. Por eso el Worker
        // declara Sistema de forma explícita y nunca se deduce de que "no hay usuario".
        await using var system = Worker(access: null);
        var id = await SeedPendingAsync(system);
        await using var denied = Worker(access: TestAccessScope.None);

        using var scope = denied.CreateScope();
        var probe = await scope.ServiceProvider.GetRequiredService<ProbeConnection>().ExecuteAsync(id);

        Assert.False(probe.Ran);
        Assert.Null(probe.Status);
        await using var db = postgres.CreateContext();
        Assert.Equal(ConnectionStatus.Pending, db.Connections.Single(c => c.Id == id).Status);
    }

    /// <summary>Composición del Worker. <paramref name="access"/> nulo = AddSystemAccessScope, como en Program.</summary>
    private ServiceProvider Worker(IAccessScope? access)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:QueryDoctor"] = postgres.ConnectionString,
            ["Security:SecretProtector:MasterKey"] = MasterKey,
        }).Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration);
        if (access is null)
        {
            services.AddSystemAccessScope();
        }
        else
        {
            services.AddSingleton(access);
        }

        services
            .AddTargetTelemetry(configuration)
            .AddIngestion(configuration)
            .AddScoped<ICapabilityProbe, StubProbe>()
            .AddScoped<IQueryStoreReader>(_ => ReaderWithData());

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static readonly string MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static FakeQueryStoreReader ReaderWithData()
    {
        var reader = new FakeQueryStoreReader();
        reader.Rows.Add(new QueryStoreStatRow(1, 10, "0A1B2C3D4E5F6071", "8899AABBCCDDEEFF", "16.0.4315.2", null,
            IngestionSamples.Row(100, 5, IngestionSamples.Metrics(2, 10d, 2d), closed: true)));
        reader.Texts[10] = "SELECT 1";
        reader.Plans[100] = ShowPlan;
        return reader;
    }

    private async Task<Guid> SeedPendingAsync(ServiceProvider worker)
    {
        var encryptor = worker.GetRequiredService<IFieldEncryptor>();
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = "wrk-" + Guid.NewGuid().ToString("N")[..12],
            DatabaseName = "QD_Lab",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = ConnectionCredentialsCodec.Encrypt(new ConnectionCredentials("qd_reader", "S3creta!"), encryptor),
            Status = ConnectionStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await using var db = postgres.CreateContext();
        db.Connections.Add(connection);
        await db.SaveChangesAsync();
        return connection.Id;
    }

    // Que A tenga un Desarrollador y B no, para mostrar que al Worker eso no le cambia nada.
    private async Task AssignToSomeDeveloperAsync(Guid connectionId)
    {
        await using var db = postgres.CreateContext();
        var user = new Infrastructure.Persistence.ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"dev{Guid.NewGuid():N}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.UserConnections.Add(new UserConnection { UserId = user.Id, ConnectionId = connectionId, AssignedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    private sealed class StubProbe : ICapabilityProbe
    {
        public Task<EngineInfo> GetEngineAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult(new EngineInfo("16.0.4315.2", "Developer Edition (64-bit)", 3));

        public Task<IReadOnlyList<string>> GetDatabasePermissionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["CONNECT", "VIEW DATABASE STATE"]);

        public Task<QueryStoreOptions?> GetQueryStoreOptionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult<QueryStoreOptions?>(new("READ_WRITE", "READ_WRITE", 0, 1, "ALL", 30));

        public Task<IReadOnlyList<string>> GetRuntimeStatsColumnsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["avg_tempdb_space_used", "avg_log_bytes_used"]);
    }
}
