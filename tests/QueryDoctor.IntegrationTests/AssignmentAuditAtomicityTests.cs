using Microsoft.EntityFrameworkCore;
using Npgsql;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;
using QueryDoctor.Infrastructure.Persistence;
using QueryDoctor.Infrastructure.Traceability;

namespace QueryDoctor.IntegrationTests;

/// <summary>
/// S2-12 (RF-TRZ-05, RNF-SEG-12): una asignación y su evento de auditoría se confirman o se pierden juntos. Para que la
/// auditoría falle a mitad de camino, otra sesión retiene el bloqueo de la cadena de auditoría y la escritura del evento
/// se cancela mientras espera. Sin la transacción compartida, la fila quedaría confirmada sin evento y el reintento,
/// idempotente, ya no lo escribiría nunca.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class AssignmentAuditAtomicityTests(PostgresFixture postgres) : IDisposable
{
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(1);

    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task SiLaAuditoriaFallaLaAsignacionNoQuedaYElReintentoDejaAmbas()
    {
        var connectionId = await SeedConnectionAsync();
        var (_, developerId) = await _api.SessionAsync([RoleNames.Developer]);
        var actor = new AssignmentActor(Guid.NewGuid(), "dba");

        await using (await HoldAuditLockAsync())
        {
            using var timeout = new CancellationTokenSource(LockWait);
            await using var db = postgres.CreateContext();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => UseCase(db).AssignAsync(connectionId, developerId, actor, timeout.Token));
        }

        Assert.False(await IsAssignedAsync(connectionId, developerId), "La asignación quedó sin su evento de auditoría.");
        Assert.Empty(await EventsAsync(connectionId));

        // El reintento ya no es un "ya existía": crea la fila y su evento.
        await using (var db = postgres.CreateContext())
        {
            var retried = await UseCase(db).AssignAsync(connectionId, developerId, actor);
            Assert.Equal(new AssignConnectionResult.Assigned(Created: true), retried);
        }

        Assert.True(await IsAssignedAsync(connectionId, developerId));
        Assert.Equal([AuditEventType.ConnectionAssigned], await EventsAsync(connectionId));
    }

    [Fact]
    public async Task SiLaAuditoriaFallaLaAsignacionSigueYElReintentoLaQuitaConSuEvento()
    {
        var connectionId = await SeedConnectionAsync();
        var (_, developerId) = await _api.SessionAsync([RoleNames.Developer]);
        var actor = new AssignmentActor(Guid.NewGuid(), "dba");
        await using (var db = postgres.CreateContext())
        {
            await UseCase(db).AssignAsync(connectionId, developerId, actor);
        }

        await using (await HoldAuditLockAsync())
        {
            using var timeout = new CancellationTokenSource(LockWait);
            await using var db = postgres.CreateContext();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => UseCase(db).UnassignAsync(connectionId, developerId, actor, timeout.Token));
        }

        Assert.True(await IsAssignedAsync(connectionId, developerId), "Se quitó la asignación sin su evento de auditoría.");
        Assert.Equal([AuditEventType.ConnectionAssigned], await EventsAsync(connectionId));

        await using (var db = postgres.CreateContext())
        {
            Assert.True(await UseCase(db).UnassignAsync(connectionId, developerId, actor));
        }

        Assert.False(await IsAssignedAsync(connectionId, developerId));
        Assert.Equal([AuditEventType.ConnectionAssigned, AuditEventType.ConnectionUnassigned], await EventsAsync(connectionId));
    }

    private static ManageConnectionAssignments UseCase(AppDbContext db) =>
        new(new EfConnectionAssignmentStore(db), TimeProvider.System);

    /// <summary>Retiene el bloqueo de la cadena de auditoría en otra sesión hasta que se libera.</summary>
    private async Task<AuditLockHold> HoldAuditLockAsync()
    {
        // Sin pool: un bloqueo de sesión no debe sobrevivir en una conexión reciclada.
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await AuditLockHold.RunAsync(connection, "SELECT pg_advisory_lock(@key)");
        return new AuditLockHold(connection);
    }

    private sealed class AuditLockHold(NpgsqlConnection connection) : IAsyncDisposable
    {
        public static async Task RunAsync(NpgsqlConnection connection, string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("key", ChainWriter.AuditLockKey);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await RunAsync(connection, "SELECT pg_advisory_unlock(@key)");
            await connection.DisposeAsync();
        }
    }

    private async Task<bool> IsAssignedAsync(Guid connectionId, Guid userId)
    {
        await using var db = postgres.CreateContext();
        return await db.UserConnections.AnyAsync(uc => uc.ConnectionId == connectionId && uc.UserId == userId);
    }

    private async Task<AuditEventType[]> EventsAsync(Guid connectionId)
    {
        await using var db = postgres.CreateContext();
        return await db.AuditEvents
            .Where(e => e.SubjectType == AuditEventDraft.SubjectConnection && e.SubjectId == connectionId)
            .OrderBy(e => e.Seq)
            .Select(e => e.EventType)
            .ToArrayAsync();
    }

    private async Task<Guid> SeedConnectionAsync()
    {
        await using var db = postgres.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = $"atm-{Guid.NewGuid():N}"[..20],
            DatabaseName = "QD_Lab",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = "no-se-usa",
            Status = ConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Connections.Add(connection);
        await db.SaveChangesAsync();
        return connection.Id;
    }
}
