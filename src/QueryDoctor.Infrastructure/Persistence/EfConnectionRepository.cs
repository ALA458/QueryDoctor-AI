using Microsoft.EntityFrameworkCore;
using Npgsql;
using QueryDoctor.Application.Connections;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Persistence.Configurations;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>Implementación EF Core de <see cref="IConnectionRepository"/> sobre core.connection (S1-09).</summary>
public sealed class EfConnectionRepository : IConnectionRepository
{
    /// <summary>Nombre de la UK de la migración EstadoDeSondeoYUnicidadDeConexion.</summary>
    public const string AddressUniqueIndex = "IX_connection_server_database_name_key";

    private const string UniqueViolation = "23505";

    private readonly AppDbContext _db;

    public EfConnectionRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<Connection?> FindByAddressAsync(string normalizedServer, string databaseName, CancellationToken cancellationToken)
    {
        // Misma regla que la UK, y calculada por el MISMO motor: database_name_key = lower(@db) en PostgreSQL.
        // Auditoría B2: con ToLowerInvariant() de C# la búsqueda y la UK podían discrepar en nombres no ASCII (según
        // la intercalación de la base), y el 409 salía sin encontrar la fila que lo causó. Consulta parametrizada.
        return _db.Connections
            .FromSql($"SELECT * FROM core.connection WHERE server = {normalizedServer} AND database_name_key = lower({databaseName})")
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Connection?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Connections.FindAsync([id], cancellationToken);

    public void Add(Connection connection) => _db.Connections.Add(connection);

    public Task<CapabilityProfile?> GetProfileAsync(Guid connectionId, CancellationToken cancellationToken) =>
        _db.CapabilityProfiles.FirstOrDefaultAsync(p => p.ConnectionId == connectionId, cancellationToken);

    public void AddProfile(CapabilityProfile profile) => _db.CapabilityProfiles.Add(profile);

    public async Task<bool> MarkProbeFailedIfPendingAsync(
        Guid id,
        ProbeFailureCause cause,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // UPDATE ... WHERE id = @id AND status = 'Pending': atómico, no pisa un resultado que otro proceso ya guardó.
        var rows = await _db.Connections
            .Where(c => c.Id == id && c.Status == ConnectionStatus.Pending)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(c => c.Status, ConnectionStatus.Failed)
                    .SetProperty(c => c.ProbeFailureCause, cause)
                    .SetProperty(c => c.LastProbedAt, now)
                    .SetProperty(c => c.UpdatedAt, now),
                cancellationToken);

        return rows > 0;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: UniqueViolation,
            ConstraintName: AddressUniqueIndex,
        })
        {
            // La fila que perdió la carrera no debe quedar en el rastreador: el caso de uso vuelve a consultar.
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            throw new DuplicateConnectionException("La base ya está registrada en ese servidor.", ex);
        }
    }
}
