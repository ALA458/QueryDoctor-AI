using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Ai;
using QueryDoctor.Application.Explanations;
using QueryDoctor.Application.Validation;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Infrastructure.Explanations;

/// <summary>
/// Persistencia de <c>ai.explanation</c> y lectura del material de una consulta (S2-05). "Último análisis" es la
/// ejecución <see cref="AnalysisRunStatus.Succeeded"/> más reciente de la consulta (la que dejó S2-02).
/// </summary>
public sealed class EfExplanationStore : IExplanationStore, IExplanationSourceReader
{
    private readonly AppDbContext _db;

    public EfExplanationStore(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<ExplanationTarget?> FindTargetAsync(Guid queryId, CancellationToken cancellationToken)
    {
        var connectionId = await _db.Queries.AsNoTracking()
            .Where(q => q.Id == queryId)
            .Select(q => (Guid?)q.ConnectionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (connectionId is null)
        {
            return null;
        }

        var runId = await LatestRunIdAsync(queryId, cancellationToken);
        var findings = runId is null
            ? 0
            : await _db.Findings.AsNoTracking().CountAsync(f => f.AnalysisRunId == runId, cancellationToken);

        return new ExplanationTarget(queryId, connectionId.Value, findings);
    }

    public async Task<IReadOnlyList<ExplanationFindingSummary>> GetLatestFindingsAsync(Guid queryId, CancellationToken cancellationToken)
    {
        var runId = await LatestRunIdAsync(queryId, cancellationToken);
        if (runId is null)
        {
            return [];
        }

        return await _db.Findings.AsNoTracking()
            .Where(f => f.AnalysisRunId == runId)
            .OrderBy(f => f.FindingCode)
            .Select(f => new ExplanationFindingSummary(f.FindingCode, f.RuleId, f.Severity, f.EvidenceClass))
            .ToListAsync(cancellationToken);
    }

    public Task<Explanation?> GetLatestAsync(Guid queryId, CancellationToken cancellationToken) =>
        _db.Explanations.AsNoTracking()
            .Where(e => e.QueryId == queryId)
            .OrderByDescending(e => e.RequestedAt)
            .ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Explanation?> GetAsync(Guid explanationId, CancellationToken cancellationToken) =>
        _db.Explanations.FirstOrDefaultAsync(e => e.Id == explanationId, cancellationToken);

    public Task<Explanation?> FindTracedByContextHashAsync(string contextHash, CancellationToken cancellationToken) =>
        _db.Explanations.AsNoTracking()
            .Where(e => e.ContextHash == contextHash
                && e.State == ExplanationState.Completed
                && e.ValidationStatus == ExplanationValidationConstants.StatusTrazadaCode
                && e.EncryptedBody != null)
            .OrderByDescending(e => e.CompletedAt)
            .ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(Explanation explanation, CancellationToken cancellationToken)
    {
        _db.Explanations.Add(explanation);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(Explanation explanation, IReadOnlyList<ExplanationClaim> claims, CancellationToken cancellationToken)
    {
        if (_db.Entry(explanation).State == EntityState.Detached)
        {
            _db.Explanations.Update(explanation);
        }

        _db.ExplanationClaims.AddRange(claims);

        // Una sola llamada a SaveChanges: explicación, afirmaciones y referencias quedan juntas o no quedan.
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExplanationClaim>> GetClaimsAsync(Guid explanationId, CancellationToken cancellationToken) =>
        await _db.ExplanationClaims.AsNoTracking()
            .Include(c => c.References)
            .Where(c => c.ExplanationId == explanationId)
            .OrderBy(c => c.ClaimIndex)
            .ToListAsync(cancellationToken);

    public async Task SaveAsync(Explanation explanation, CancellationToken cancellationToken)
    {
        if (_db.Entry(explanation).State == EntityState.Detached)
        {
            _db.Explanations.Update(explanation);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<EncryptedExplanationSource?> LoadAsync(Guid queryId, CancellationToken cancellationToken)
    {
        var query = await _db.Queries.AsNoTracking().FirstOrDefaultAsync(q => q.Id == queryId, cancellationToken);
        if (query is null)
        {
            return null;
        }

        var run = await _db.AnalysisRuns.AsNoTracking()
            .Where(r => r.QueryId == queryId && r.Status == AnalysisRunStatus.Succeeded)
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return null;
        }

        var plan = await _db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == run.PlanId, cancellationToken);
        var sqlText = query.QueryTextId is null
            ? null
            : await _db.QueryTexts.AsNoTracking()
                .Where(t => t.Id == query.QueryTextId)
                .Select(t => t.EncryptedSqlText)
                .FirstOrDefaultAsync(cancellationToken);

        var findings = await _db.Findings.AsNoTracking()
            .Where(f => f.AnalysisRunId == run.Id)
            .OrderBy(f => f.FindingCode)
            .ToListAsync(cancellationToken);

        var score = await _db.HealthScores.AsNoTracking()
            .Where(h => h.QueryId == queryId)
            .OrderByDescending(h => h.CalculatedAt)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new EncryptedExplanationSource(
            queryId,
            query.QueryStoreQueryId,
            plan?.QueryStorePlanId ?? 0,
            sqlText,
            plan?.EncryptedPlanXml,
            plan?.EngineVersion ?? run.EngineVersion,
            findings,
            score);
    }

    private Task<Guid?> LatestRunIdAsync(Guid queryId, CancellationToken cancellationToken) =>
        _db.AnalysisRuns.AsNoTracking()
            .Where(r => r.QueryId == queryId && r.Status == AnalysisRunStatus.Succeeded)
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
