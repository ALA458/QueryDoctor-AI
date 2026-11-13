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
        // Misma regla que la UK: server exacto y database_name_key = lower(database_name).
        var key = ConnectionAddress.DatabaseNameKey(databaseName);

        return _db.Connections.FirstOrDefaultAsync(
            c => c.Server == normalizedServer
                && EF.Property<string>(c, ConnectionConfiguration.DatabaseNameKeyProperty) == key,
            cancellationToken);
    }

    public async Task<Connection?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Connections.FindAsync([id], cancellationToken);

    public void Add(Connection connection) => _db.Connections.Add(connection);

    public Task<CapabilityProfile?> GetProfileAsync(Guid connectionId, CancellationToken cancellationToken) =>
        _db.CapabilityProfiles.FirstOrDefaultAsync(p => p.ConnectionId == connectionId, cancellationToken);

    public void AddProfile(CapabilityProfile profile) => _db.CapabilityProfiles.Add(profile);

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
