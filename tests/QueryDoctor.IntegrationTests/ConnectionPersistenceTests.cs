using System.Security.Cryptography;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.Extensions.Options;
using Npgsql;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Connections;
using QueryDoctor.Infrastructure.Persistence;
using QueryDoctor.Infrastructure.Security;

namespace QueryDoctor.IntegrationTests;

// S1-09 contra PostgreSQL real (Testcontainers): unicidad sin distinguir mayúsculas, credenciales cifradas,
// perfil de capacidades y argumentos del trabajo Hangfire.
[Collection(PostgresGroup.Name)]
public sealed class ConnectionPersistenceTests(PostgresFixture postgres) : IDisposable
{
    private const string Password = "S3creta!Lab#2026";

    private readonly SecretProtector _encryptor = new(Options.Create(new SecretProtectorOptions
    {
        MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    }));

    public void Dispose() => _encryptor.Dispose();

    private static RegisterConnectionCommand Command(string server, string database) =>
        new("SqlServer", server, database, "SqlAuthentication", "Mandatory", "qd_reader", Password);

    private RegisterConnection Register(AppDbContext db) =>
        new(new EfConnectionRepository(db), new NullScheduler(), _encryptor, TimeProvider.System);

    [Fact]
    public async Task UnaConexionDuplicadaConDistintaCapitalizacionSeRechaza()
    {
        await using (var db = postgres.CreateContext())
        {
            var first = await Register(db).ExecuteAsync(Command("SRV-CASE,1433", "QD_Lab"));
            Assert.IsType<RegisterConnectionResult.Accepted>(first);
        }

        await using (var db = postgres.CreateContext())
        {
            var second = await Register(db).ExecuteAsync(Command("tcp:srv-case", "qd_LAB"));
            Assert.IsType<RegisterConnectionResult.Conflict>(second);
        }

        await using var check = postgres.CreateContext();
        var rows = check.Connections.Where(c => c.Server == "srv-case").ToList();
        var row = Assert.Single(rows);
        Assert.Equal("QD_Lab", row.DatabaseName);
    }

    [Fact]
    public async Task LaClaveUnicaGanaUnaCarreraYElRepositorioLaTraduce()
    {
        // Dos contextos que se saltan la búsqueda previa: solo la UK puede detener al segundo.
        await using var db1 = postgres.CreateContext();
        await using var db2 = postgres.CreateContext();
        var repo1 = new EfConnectionRepository(db1);
        var repo2 = new EfConnectionRepository(db2);

        repo1.Add(NewConnection("srv-race", "Ventas"));
        await repo1.SaveChangesAsync(default);

        repo2.Add(NewConnection("srv-race", "VENTAS"));
        await Assert.ThrowsAsync<DuplicateConnectionException>(() => repo2.SaveChangesAsync(default));
    }

    [Fact]
    public async Task LasCredencialesQuedanCifradasEnLaBase()
    {
        Guid id;
        await using (var db = postgres.CreateContext())
        {
            var result = await Register(db).ExecuteAsync(Command("srv-crypto", "QD_Lab"));
            id = Assert.IsType<RegisterConnectionResult.Accepted>(result).ConnectionId;
        }

        // Lectura cruda: la columna no contiene ni el usuario ni la contraseña (RNF-SEG-04/14).
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT encrypted_credentials FROM core.connection WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        var stored = (string)(await command.ExecuteScalarAsync())!;

        Assert.DoesNotContain(Password, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("qd_reader", stored, StringComparison.Ordinal);
        Assert.Equal(Password, ConnectionCredentialsCodec.Decrypt(stored, _encryptor).Password);
    }

    [Fact]
    public async Task ElSondeoGuardaElPerfilYUnSegundoSondeoLoSobrescribe()
    {
        Guid id;
        await using (var db = postgres.CreateContext())
        {
            var result = await Register(db).ExecuteAsync(Command("srv-profile", "QD_Lab"));
            id = Assert.IsType<RegisterConnectionResult.Accepted>(result).ConnectionId;
        }

        await RunProbeAsync(id, "16.0.4315.2");

        await using (var db = postgres.CreateContext())
        {
            var connection = (await db.Connections.FindAsync(id))!;
            Assert.Equal(ConnectionStatus.Active, connection.Status);
            connection.Status = ConnectionStatus.Pending; // como si se volviera a registrar (D3)
            await db.SaveChangesAsync();
        }

        await RunProbeAsync(id, "16.0.4320.1");

        await using var check = postgres.CreateContext();
        var profile = Assert.Single(check.CapabilityProfiles.Where(p => p.ConnectionId == id).ToList());
        Assert.Equal("16.0.4320.1", profile.EngineVersion);
        Assert.Equal("READ_WRITE", profile.QueryStoreActualState);
        Assert.Equal(30, profile.RetentionDays);
        Assert.Equal("[]", profile.OmittedFeaturesJson);
    }

    [Fact]
    public async Task UnFalloDeSondeoGuardaSoloElCodigo()
    {
        Guid id;
        await using (var db = postgres.CreateContext())
        {
            var result = await Register(db).ExecuteAsync(Command("srv-fail", "QD_Lab"));
            id = Assert.IsType<RegisterConnectionResult.Accepted>(result).ConnectionId;
        }

        await RunProbeAsync(id, "16.0.4315.2", new TelemetrySourceException(TelemetryFailureKind.InvalidCredentials, 18456));

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status, probe_failure_cause FROM core.connection WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Failed", reader.GetString(0));
        Assert.Equal("InvalidCredentials", reader.GetString(1));
    }

    [Fact]
    public async Task ElTrabajoHangfireLlevaSoloElIdDeLaConexionYVaALaColaProbe()
    {
        var options = new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true };
        var storage = new PostgreSqlStorage(new NpgsqlConnectionFactory(postgres.ConnectionString, options), options);
        var scheduler = new HangfireProbeJobScheduler(new BackgroundJobClient(storage));
        var connectionId = Guid.NewGuid();

        scheduler.EnqueueProbe(connectionId);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT j.arguments::text, j.invocationdata::text, q.queue FROM hangfire.job j " +
            "JOIN hangfire.jobqueue q ON q.jobid = j.id WHERE j.arguments::text LIKE @id",
            connection);
        command.Parameters.AddWithValue("id", $"%{connectionId}%");
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        var arguments = reader.GetString(0);
        Assert.Contains(connectionId.ToString(), arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, arguments, StringComparison.Ordinal);
        Assert.Contains(nameof(ProbeConnectionJob), reader.GetString(1), StringComparison.Ordinal);
        Assert.Equal(JobQueues.Probe, reader.GetString(2));
    }

    // Auditoría B2: la búsqueda usa el mismo lower() que la UK, así que el 409 apunta a la fila real también
    // con nombres no ASCII.
    [Fact]
    public async Task UnNombreNoAsciiConOtraCapitalizacionDa409ConLaConexionReal()
    {
        Guid firstId;
        await using (var db = postgres.CreateContext())
        {
            var first = Assert.IsType<RegisterConnectionResult.Accepted>(
                await Register(db).ExecuteAsync(Command("srv-b2", "Ärger_Ñandú")));
            firstId = first.ConnectionId;
        }

        await using (var db = postgres.CreateContext())
        {
            var conflict = Assert.IsType<RegisterConnectionResult.Conflict>(
                await Register(db).ExecuteAsync(Command("srv-b2", "äRGER_ñANDÚ")));
            Assert.Equal(firstId, conflict.ExistingConnectionId);
            Assert.Equal(ConnectionStatus.Pending, conflict.ExistingStatus);
        }
    }

    // Auditoría M1: el UPDATE de respaldo es condicional y real (ExecuteUpdate en PostgreSQL).
    [Fact]
    public async Task ElRespaldoMarcaFailedSoloLasConexionesPendientes()
    {
        var pending = NewConnection("srv-m1-pending", "QD_Lab");
        var active = NewConnection("srv-m1-active", "QD_Lab");
        active.Status = ConnectionStatus.Active;
        await using (var db = postgres.CreateContext())
        {
            db.Connections.AddRange(pending, active);
            await db.SaveChangesAsync();
        }

        var now = DateTimeOffset.UtcNow;
        await using (var db = postgres.CreateContext())
        {
            var repository = new EfConnectionRepository(db);
            Assert.True(await repository.MarkProbeFailedIfPendingAsync(pending.Id, ProbeFailureCause.Unknown, now, default));
            Assert.False(await repository.MarkProbeFailedIfPendingAsync(active.Id, ProbeFailureCause.Unknown, now, default));
            Assert.False(await repository.MarkProbeFailedIfPendingAsync(pending.Id, ProbeFailureCause.Unknown, now, default));
        }

        await using var check = postgres.CreateContext();
        var marked = await check.Connections.FindAsync(pending.Id);
        Assert.Equal(ConnectionStatus.Failed, marked!.Status);
        Assert.Equal(ProbeFailureCause.Unknown, marked.ProbeFailureCause);
        Assert.NotNull(marked.LastProbedAt);
        var untouched = await check.Connections.FindAsync(active.Id);
        Assert.Equal(ConnectionStatus.Active, untouched!.Status);
        Assert.Null(untouched.ProbeFailureCause);
    }

    private async Task RunProbeAsync(Guid id, string version, Exception? failure = null)
    {
        await using var db = postgres.CreateContext();
        var probe = new ProbeConnection(
            new EfConnectionRepository(db),
            new StubProbe(version, failure),
            _encryptor,
            TimeProvider.System,
            new ProbeOptions());
        await probe.ExecuteAsync(id);
    }

    private static Connection NewConnection(string server, string database) => new()
    {
        Id = Guid.NewGuid(),
        Platform = ConnectionPlatform.SqlServer,
        Server = server,
        DatabaseName = database,
        Status = ConnectionStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private sealed class NullScheduler : IProbeJobScheduler
    {
        public void EnqueueProbe(Guid connectionId)
        {
        }
    }

    private sealed class StubProbe(string version, Exception? failure) : ICapabilityProbe
    {
        public Task<EngineInfo> GetEngineAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            failure is null ? Task.FromResult(new EngineInfo(version, "Developer Edition (64-bit)", 3)) : throw failure;

        public Task<IReadOnlyList<string>> GetDatabasePermissionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["CONNECT", "VIEW DATABASE STATE"]);

        public Task<QueryStoreOptions?> GetQueryStoreOptionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult<QueryStoreOptions?>(new("READ_WRITE", "READ_WRITE", 0, 1, "ALL", 30));

        public Task<IReadOnlyList<string>> GetRuntimeStatsColumnsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["avg_tempdb_space_used", "avg_log_bytes_used"]);
    }
}
