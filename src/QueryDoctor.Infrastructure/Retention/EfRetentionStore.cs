using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Retention;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Infrastructure.Persistence;
using QueryDoctor.Infrastructure.Traceability;

namespace QueryDoctor.Infrastructure.Retention;

/// <summary>
/// Implementación EF Core / PostgreSQL de la retención y el borrado de SQL y planes (S2-13, RNF-SEG-10).
/// </summary>
public sealed class EfRetentionStore : IRetentionStore
{
    public const string SystemActorName = "system:retention";

    private readonly AppDbContext _db;

    public EfRetentionStore(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<RetentionPurgeOutcome> PurgeExpiredAsync(
        DateTimeOffset cutoffUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var utcCutoff = cutoffUtc.ToUniversalTime();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var textsCleared = await _db.QueryTexts
            .Where(t => t.EncryptedSqlText != null && t.IngestedAt <= utcCutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.EncryptedSqlText, (string?)null), cancellationToken);

        var plansCleared = await _db.Plans
            .Where(p => p.EncryptedPlanXml != null && p.IngestedAt <= utcCutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.EncryptedPlanXml, (string?)null), cancellationToken);

        if (textsCleared > 0 || plansCleared > 0)
        {
            await ChainWriter.AppendAuditAsync(
                _db,
                EfAuditLog.ToEvent(new AuditEventDraft(
                    AuditEventType.DataDeleted,
                    UserId: null,
                    UserName: SystemActorName,
                    Outcome: AuditEventDraft.OutcomeOk,
                    SubjectType: AuditEventDraft.SubjectRetention,
                    SubjectId: null)),
                nowUtc,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RetentionPurgeOutcome(textsCleared, plansCleared);
    }

    public async Task<DeleteConnectionTelemetryOutcome> DeleteConnectionDataAsync(
        Guid connectionId,
        Guid? actorUserId,
        string? actorUserName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!await _db.Connections.AsNoTracking().AnyAsync(c => c.Id == connectionId, cancellationToken))
        {
            return new DeleteConnectionTelemetryOutcome.NotFound();
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var textsCleared = await _db.QueryTexts
            .Where(t => t.ConnectionId == connectionId && t.EncryptedSqlText != null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.EncryptedSqlText, (string?)null), cancellationToken);

        var plansCleared = await _db.Plans
            .Where(p => p.ConnectionId == connectionId && p.EncryptedPlanXml != null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.EncryptedPlanXml, (string?)null), cancellationToken);

        // RF-TRZ-05: incluso si la conexión no tenía texto/planes activos, la acción del DBA queda auditada en la misma transacción.
        await ChainWriter.AppendAuditAsync(
            _db,
            EfAuditLog.ToEvent(new AuditEventDraft(
                AuditEventType.DataDeleted,
                UserId: actorUserId,
                UserName: actorUserName,
                Outcome: AuditEventDraft.OutcomeOk,
                SubjectType: AuditEventDraft.SubjectConnection,
                SubjectId: connectionId)),
            nowUtc,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new DeleteConnectionTelemetryOutcome.Found(connectionId, textsCleared, plansCleared);
    }
}
