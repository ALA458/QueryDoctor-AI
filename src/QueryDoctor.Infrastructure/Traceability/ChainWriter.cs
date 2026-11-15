using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Infrastructure.Traceability;

/// <summary>
/// Inserción serializada en las cadenas de solo adición (S2-06). Debe llamarse dentro de una transacción abierta:
/// <c>pg_advisory_xact_lock</c> serializa a todos los escritores de la misma cadena (Api y Worker) hasta el commit, así
/// la seq y el prev_hash no se bifurcan nunca.
/// </summary>
public static class ChainWriter
{
    /// <summary>Claves del bloqueo consultivo, una por cadena. Valores fijos, distintos del bloqueo de la ingesta.</summary>
    public const long TraceLockKey = 0x5144_5452_4143_4501; // "QDTRACE" + 1

    public const long AuditLockKey = 0x5144_4155_4449_5401; // "QDAUDIT" + 1

    public static async Task AppendTraceAsync(AppDbContext db, TraceEntry entry, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureTransaction(db);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({TraceLockKey})", cancellationToken);

        var last = await db.TraceEntries.AsNoTracking()
            .OrderByDescending(e => e.Seq)
            .Select(e => new { e.Seq, e.EntryHash })
            .FirstOrDefaultAsync(cancellationToken);

        entry.Seq = (last?.Seq ?? 0) + 1;
        entry.PrevHash = last?.EntryHash ?? HashChain.Genesis;
        entry.RecordedAt = HashChain.Normalize(now);
        entry.EntryHash = HashChain.Compute(entry.PrevHash, entry);
        db.TraceEntries.Add(entry);
    }

    public static async Task AppendAuditAsync(AppDbContext db, AuditEvent entry, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureTransaction(db);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AuditLockKey})", cancellationToken);

        var last = await db.AuditEvents.AsNoTracking()
            .OrderByDescending(e => e.Seq)
            .Select(e => new { e.Seq, e.EntryHash })
            .FirstOrDefaultAsync(cancellationToken);

        entry.Seq = (last?.Seq ?? 0) + 1;
        entry.PrevHash = last?.EntryHash ?? HashChain.Genesis;
        entry.RecordedAt = HashChain.Normalize(now);
        entry.EntryHash = HashChain.Compute(entry.PrevHash, entry);
        db.AuditEvents.Add(entry);
    }

    private static void EnsureTransaction(AppDbContext db)
    {
        if (db.Database.CurrentTransaction is null)
        {
            // Sin transacción, el bloqueo se soltaría antes de insertar y dos escritores podrían repetir la seq.
            throw new InvalidOperationException("La cadena de solo adición se escribe dentro de una transacción.");
        }
    }
}
