using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Connections;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>Asignaciones con EF Core (S2-12). Solo la usa la Api, con un DBA: el filtro por conexión le deja ver todo.</summary>
public sealed class EfConnectionAssignmentStore : IConnectionAssignmentStore
{
    private static readonly string DeveloperRole = RoleNames.Developer.ToUpperInvariant();

    private readonly AppDbContext _db;

    public EfConnectionAssignmentStore(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<bool> ConnectionExistsAsync(Guid connectionId, CancellationToken cancellationToken) =>
        _db.Connections.AsNoTracking().AnyAsync(c => c.Id == connectionId, cancellationToken);

    public async Task<AssignmentCandidate> GetCandidateAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, cancellationToken))
        {
            return AssignmentCandidate.NotFound;
        }

        return await DeveloperIds().AnyAsync(id => id == userId, cancellationToken)
            ? AssignmentCandidate.Developer
            : AssignmentCandidate.NotDeveloper;
    }

    public async Task<IReadOnlyList<AssignmentView>> ListAsync(Guid connectionId, CancellationToken cancellationToken) =>
        await _db.UserConnections.AsNoTracking()
            .Where(uc => uc.ConnectionId == connectionId)
            .Join(_db.Users, uc => uc.UserId, u => u.Id, (uc, u) => new { u.Id, u.UserName, uc.AssignedAt })
            .OrderBy(x => x.UserName)
            .Select(x => new AssignmentView(x.Id, x.UserName ?? string.Empty, x.AssignedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> AssignAsync(Guid connectionId, Guid userId, DateTimeOffset assignedAt, CancellationToken cancellationToken)
    {
        // Idempotente también ante dos PUT a la vez: la clave (user_id, connection_id) decide, sin leer antes.
        var inserted = await _db.Database.ExecuteSqlAsync(
            $"INSERT INTO ops.user_connection (user_id, connection_id, assigned_at) VALUES ({userId}, {connectionId}, {assignedAt}) ON CONFLICT DO NOTHING",
            cancellationToken);
        return inserted == 1;
    }

    public async Task<bool> UnassignAsync(Guid connectionId, Guid userId, CancellationToken cancellationToken) =>
        await _db.UserConnections
            .Where(uc => uc.ConnectionId == connectionId && uc.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken) == 1;

    public async Task<IReadOnlyList<DeveloperView>> ListDevelopersAsync(CancellationToken cancellationToken) =>
        await _db.Users.AsNoTracking()
            .Where(u => DeveloperIds().Contains(u.Id))
            .OrderBy(u => u.UserName)
            .Select(u => new DeveloperView(u.Id, u.UserName ?? string.Empty))
            .ToListAsync(cancellationToken);

    private IQueryable<Guid> DeveloperIds() =>
        _db.UserRoles
            .Join(_db.Roles.Where(r => r.NormalizedName == DeveloperRole), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId);
}
