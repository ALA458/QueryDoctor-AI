using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.KnowledgeBase;
using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>
/// Implementación de <see cref="IKnowledgeBaseRepository"/> sobre PostgreSQL mediante Entity Framework Core (S1-17).
/// Provee acceso de solo lectura y alto rendimiento a los fragmentos y relaciones curadas.
/// </summary>
public sealed class EfKnowledgeBaseRepository : IKnowledgeBaseRepository
{
    private readonly AppDbContext _db;

    public EfKnowledgeBaseRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KbFragment>> GetFragmentsByRuleAsync(
        string ruleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        return await _db.KbFragments
            .AsNoTracking()
            .Where(f => f.RuleId == ruleId)
            .OrderBy(f => f.Title)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<KbFragment?> GetFragmentByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _db.KbFragments
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KbCausalRelation>> GetCausalRelationsAsync(
        string ruleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        return await _db.KbCausalRelations
            .AsNoTracking()
            .Include(r => r.Fragment)
            .Where(r => r.RuleId == ruleId)
            .OrderBy(r => r.RelationType)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HasCausalRelationAsync(
        string ruleId,
        Guid fragmentId,
        CausalRelationType relationType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        return await _db.KbCausalRelations
            .AsNoTracking()
            .AnyAsync(
                r => r.RuleId == ruleId &&
                     r.FragmentId == fragmentId &&
                     r.RelationType == relationType,
                cancellationToken);
    }
}
