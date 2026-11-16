using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Retention;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Persistence;
using QueryDoctor.Infrastructure.Traceability;

namespace QueryDoctor.Infrastructure.Retention;

/// <summary>
/// Purga de evidencia con EF Core (S2-13, RNF-SEG-10). Usa <c>ExecuteUpdateAsync</c> sobre los <c>DbSet</c>, que pasa
/// por el filtro por conexión de S2-12 (en la Api el DBA ve todo; en el Worker, el acceso Sistema). No usa SQL directo
/// ni <c>IgnoreQueryFilters</c>. Solo hace UPDATE en <c>core</c> y <c>analysis</c>; en <c>audit.*</c> solo agrega el evento.
/// </summary>
public sealed class EfEvidencePurgeStore : IEvidencePurgeStore
{
    private readonly AppDbContext _db;

    public EfEvidencePurgeStore(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<bool> ExistsAsync(EvidenceScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Kind == EvidenceScopeKind.Connection
            ? _db.Connections.AsNoTracking().AnyAsync(c => c.Id == scope.Id, cancellationToken)
            : _db.Queries.AsNoTracking().AnyAsync(q => q.Id == scope.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ConnectionsWithExpiredEvidenceAsync(
        DateTimeOffset ingestedBefore,
        CancellationToken cancellationToken)
    {
        var cutoff = ingestedBefore.ToUniversalTime();
        var fromTexts = _db.QueryTexts.AsNoTracking()
            .Where(t => t.PurgedAt == null && t.EncryptedSqlText != null && t.IngestedAt < cutoff)
            .Select(t => t.ConnectionId);
        var fromPlans = _db.Plans.AsNoTracking()
            .Where(p => p.PurgedAt == null && p.EncryptedPlanXml != null && p.IngestedAt < cutoff)
            .Select(p => p.ConnectionId);

        // Auditoría M1: evidencia que quedó en un plan ya purgado (p. ej. un recálculo que confirmó tarde). Sin esto, una
        // conexión sin otros vencidos no se volvería a visitar y esos literales no saldrían nunca.
        var fromResidualEvidence = _db.Findings.AsNoTracking()
            .Where(f => f.EncryptedEvidenceJson != string.Empty && _db.Plans.Any(p => p.Id == f.PlanId && p.PurgedAt != null))
            .Select(f => f.ConnectionId);

        return await fromTexts.Union(fromPlans).Union(fromResidualEvidence).OrderBy(id => id).ToListAsync(cancellationToken);
    }

    public async Task<EvidencePurgeCounts> PurgeAsync(
        EvidenceScope scope,
        DateTimeOffset? ingestedBefore,
        DateTimeOffset now,
        Func<EvidencePurgeCounts, AuditEventDraft?> audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(audit);

        // Npgsql solo escribe timestamptz con offset 0.
        var purgedAt = now.ToUniversalTime();
        var cutoff = ingestedBefore?.ToUniversalTime();

        // RF-TRZ-05: el borrado y su evento van juntos. Si la auditoría fallara después de confirmar, el borrado
        // quedaría sin rastro y un reintento ya no encontraría nada que auditar.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var texts = await TextsIn(scope, cutoff).ExecuteUpdateAsync(
            s => s.SetProperty(t => t.EncryptedSqlText, (string?)null).SetProperty(t => t.PurgedAt, purgedAt),
            cancellationToken);

        var plans = await PlansIn(scope, cutoff).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.EncryptedPlanXml, (string?)null).SetProperty(p => p.PurgedAt, purgedAt),
            cancellationToken);

        // D3: la evidencia lleva literales sacados del plan (predicados, valores compilados). Se vacía la de todo hallazgo
        // cuyo plan está purgado, también de corridas anteriores; el hallazgo (código, regla, severidad, nodos) queda.
        var findings = await FindingsOfPurgedPlansIn(scope).ExecuteUpdateAsync(
            s => s.SetProperty(f => f.EncryptedEvidenceJson, string.Empty),
            cancellationToken);

        var counts = new EvidencePurgeCounts(texts, plans, findings);
        if (audit(counts) is { } draft)
        {
            var entry = EfAuditLog.ToEvent(draft);
            try
            {
                await ChainWriter.AppendAuditAsync(_db, entry, now, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // La transacción se revierte; que el evento no quede pendiente en el contexto para otro guardado.
                _db.Entry(entry).State = EntityState.Detached;
                throw;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return counts;
    }

    private IQueryable<QueryText> TextsIn(EvidenceScope scope, DateTimeOffset? cutoff)
    {
        // Auditoría B1: una fila sin contenido (texto que nunca llegó) no se marca: no venció nada y no es "evidencia expirada".
        var texts = _db.QueryTexts.Where(t => t.PurgedAt == null && t.EncryptedSqlText != null);
        texts = scope.Kind == EvidenceScopeKind.Connection
            ? texts.Where(t => t.ConnectionId == scope.Id)
            // Un texto de Query Store puede ser de varias consultas: borrarlo por una lo borra para todas.
            : texts.Where(t => _db.Queries.Any(q => q.Id == scope.Id && q.QueryTextId == t.Id));
        return cutoff is { } c ? texts.Where(t => t.IngestedAt < c) : texts;
    }

    private IQueryable<Plan> PlansIn(EvidenceScope scope, DateTimeOffset? cutoff)
    {
        // Auditoría B1: un plan TooLarge nunca guardó XML (I8): no se marca, y sigue explicándolo notAnalyzableCause.
        var plans = _db.Plans.Where(p => p.PurgedAt == null && p.EncryptedPlanXml != null);
        plans = scope.Kind == EvidenceScopeKind.Connection
            ? plans.Where(p => p.ConnectionId == scope.Id)
            : plans.Where(p => p.QueryId == scope.Id);
        return cutoff is { } c ? plans.Where(p => p.IngestedAt < c) : plans;
    }

    private IQueryable<Finding> FindingsOfPurgedPlansIn(EvidenceScope scope)
    {
        var findings = _db.Findings.Where(f =>
            f.EncryptedEvidenceJson != string.Empty
            && _db.Plans.Any(p => p.Id == f.PlanId && p.PurgedAt != null));
        return scope.Kind == EvidenceScopeKind.Connection
            ? findings.Where(f => f.ConnectionId == scope.Id)
            : findings.Where(f => f.QueryId == scope.Id);
    }
}
