using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Infrastructure.Traceability;

/// <summary>Log de auditoría con inserción serializada (S2-06, RF-TRZ-05). Cada evento, una transacción.</summary>
public sealed class EfAuditLog : IAuditLog
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public EfAuditLog(AppDbContext db, TimeProvider clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task AppendAsync(AuditEventDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await ChainWriter.AppendAuditAsync(
            _db,
            new AuditEvent
            {
                EventType = draft.EventType,
                UserId = draft.UserId,
                UserName = draft.UserName is { Length: > 256 } name ? name[..256] : draft.UserName,
                SubjectType = draft.SubjectType,
                SubjectId = draft.SubjectId,
                Outcome = draft.Outcome,
            },
            _clock.GetUtcNow(),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

/// <summary>Consultas de solo lectura y verificación de las cadenas (RF-TRZ-03, "Hecho cuando" de S2-06).</summary>
public sealed class EfTraceabilityQueries : ITraceabilityQueries
{
    /// <summary>Tamaño del lote al verificar: la cadena se recorre completa sin cargarla entera en memoria.</summary>
    public const int VerifyBatchSize = 5_000;

    private readonly AppDbContext _db;

    public EfTraceabilityQueries(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<TraceEntry>> SearchTraceAsync(TraceFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = _db.TraceEntries.AsNoTracking();
        if (filter.QueryId is { } queryId)
        {
            query = query.Where(e => e.QueryId == queryId);
        }

        if (filter.ExplanationId is { } explanationId)
        {
            query = query.Where(e => e.ExplanationId == explanationId);
        }

        // Npgsql solo escribe timestamptz con offset 0: un filtro en hora local (-05:00) daba 500 (preauditoría).
        if (filter.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            query = query.Where(e => e.RecordedAt >= fromUtc);
        }

        if (filter.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            query = query.Where(e => e.RecordedAt <= toUtc);
        }

        return await query.OrderByDescending(e => e.Seq).Take(filter.Take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEvent>> SearchAuditAsync(AuditFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = _db.AuditEvents.AsNoTracking();
        if (filter.EventType is { } type)
        {
            query = query.Where(e => e.EventType == type);
        }

        // Npgsql solo escribe timestamptz con offset 0: un filtro en hora local (-05:00) daba 500 (preauditoría).
        if (filter.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            query = query.Where(e => e.RecordedAt >= fromUtc);
        }

        if (filter.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            query = query.Where(e => e.RecordedAt <= toUtc);
        }

        return await query.OrderByDescending(e => e.Seq).Take(filter.Take).ToListAsync(cancellationToken);
    }

    public Task<ChainVerification> VerifyTraceAsync(CancellationToken cancellationToken) =>
        VerifyAsync(
            (after, take) => _db.TraceEntries.AsNoTracking().Where(e => e.Seq > after).OrderBy(e => e.Seq).Take(take).ToListAsync(cancellationToken),
            e => e.Seq,
            e => e.PrevHash,
            e => e.EntryHash,
            HashChain.Compute);

    public Task<ChainVerification> VerifyAuditAsync(CancellationToken cancellationToken) =>
        VerifyAsync(
            (after, take) => _db.AuditEvents.AsNoTracking().Where(e => e.Seq > after).OrderBy(e => e.Seq).Take(take).ToListAsync(cancellationToken),
            e => e.Seq,
            e => e.PrevHash,
            e => e.EntryHash,
            HashChain.Compute);

    private static async Task<ChainVerification> VerifyAsync<T>(
        Func<long, int, Task<List<T>>> nextBatch,
        Func<T, long> seq,
        Func<T, string> prevHash,
        Func<T, string> entryHash,
        Func<string, T, string> recompute)
    {
        var expectedPrev = HashChain.Genesis;
        long lastSeq = 0;
        long count = 0;
        while (true)
        {
            var batch = await nextBatch(lastSeq, VerifyBatchSize);
            if (batch.Count == 0)
            {
                return ChainVerification.Intact(count);
            }

            foreach (var entry in batch)
            {
                count++;
                if (!string.Equals(prevHash(entry), expectedPrev, StringComparison.Ordinal))
                {
                    return new ChainVerification(false, count, seq(entry), ChainVerifier.ReasonBrokenLink);
                }

                if (!string.Equals(recompute(prevHash(entry), entry), entryHash(entry), StringComparison.Ordinal))
                {
                    return new ChainVerification(false, count, seq(entry), ChainVerifier.ReasonHashMismatch);
                }

                expectedPrev = entryHash(entry);
                lastSeq = seq(entry);
            }
        }
    }
}
