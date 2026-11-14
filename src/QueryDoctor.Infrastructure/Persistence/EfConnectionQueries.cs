using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Connections;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>Lecturas de conexiones con EF Core, sin rastreo (S1-09).</summary>
public sealed class EfConnectionQueries : IConnectionQueries
{
    private readonly AppDbContext _db;

    public EfConnectionQueries(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<ConnectionSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var connections = await _db.Connections.AsNoTracking()
            .OrderBy(c => c.Server).ThenBy(c => c.DatabaseName)
            .ToListAsync(cancellationToken);

        var profiles = await _db.CapabilityProfiles.AsNoTracking()
            .ToDictionaryAsync(p => p.ConnectionId, cancellationToken);

        var lastIngestions = await LastIngestionsAsync(null, cancellationToken);

        return connections
            .Select(c => ConnectionViewMapper.ToSummary(
                c,
                profiles.GetValueOrDefault(c.Id),
                lastIngestions.TryGetValue(c.Id, out var at) ? at : null))
            .ToList();
    }

    public async Task<ConnectionDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var connection = await _db.Connections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
        {
            return null;
        }

        var profile = await _db.CapabilityProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.ConnectionId == id, cancellationToken);
        var lastIngestions = await LastIngestionsAsync(id, cancellationToken);

        return ConnectionViewMapper.ToDetail(connection, profile, lastIngestions.TryGetValue(id, out var at) ? at : null);
    }

    // Última ingesta correcta por conexión (la llena S1-11; hoy siempre vacío).
    private async Task<Dictionary<Guid, DateTimeOffset?>> LastIngestionsAsync(Guid? connectionId, CancellationToken cancellationToken) =>
        await _db.IngestionRuns.AsNoTracking()
            .Where(r => r.Status == IngestionStatus.Succeeded && r.FinishedAt != null)
            .Where(r => connectionId == null || r.ConnectionId == connectionId)
            .GroupBy(r => r.ConnectionId)
            .Select(g => new { ConnectionId = g.Key, Last = g.Max(r => r.FinishedAt) })
            .ToDictionaryAsync(x => x.ConnectionId, x => x.Last, cancellationToken);
}
