using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Common.Security;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>
/// Asignaciones de un usuario leídas de la base en cada solicitud (S2-12, RNF-SEG-12). <c>ops.user_connection</c> no
/// lleva el filtro por conexión: es la fuente del propio scope.
/// </summary>
public sealed class EfAssignedConnectionsReader : IAssignedConnectionsReader
{
    private readonly AppDbContext _db;

    public EfAssignedConnectionsReader(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<Guid[]> ForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        _db.UserConnections.AsNoTracking()
            .Where(uc => uc.UserId == userId)
            .Select(uc => uc.ConnectionId)
            .ToArrayAsync(cancellationToken);
}
