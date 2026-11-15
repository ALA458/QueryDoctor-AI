using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using QueryDoctor.Domain.Audit;

namespace QueryDoctor.Application.Traceability;

/// <summary>
/// Cadena de hash de los registros de solo adición (S2-06, RF-TRZ-02, ARQUITECTURA §7). Cada entrada guarda el hash
/// de la anterior y <c>SHA-256(prev_hash + JSON canónico de la entrada)</c>: alterar o borrar una fila a mano rompe la
/// cadena desde ahí. Puro y determinista.
/// </summary>
public static class HashChain
{
    /// <summary>Hash anterior de la primera entrada.</summary>
    public static readonly string Genesis = new('0', 64);

    private static readonly JsonSerializerOptions Canonical = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// PostgreSQL guarda <c>timestamptz</c> con microsegundos y .NET con ticks de 100 ns. Si se hashea el valor sin
    /// recortar, el que vuelve de la base no coincide y la verificación falla siempre: se recorta antes de hashear y
    /// antes de guardar.
    /// </summary>
    public static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }

    public static string Compute(string prevHash, TraceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Compute(prevHash, new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["cause"] = entry.Cause,
            ["catalog_version"] = entry.CatalogVersion,
            ["connection_id"] = Id(entry.ConnectionId),
            ["context_hash"] = entry.ContextHash,
            ["data_policy"] = entry.DataPolicy,
            ["explanation_id"] = Id(entry.ExplanationId),
            ["finding_codes"] = entry.FindingCodesJson,
            ["llm_profile"] = entry.LlmProfile,
            ["model"] = entry.Model,
            ["prompt_version"] = entry.PromptVersion,
            ["query_id"] = Id(entry.QueryId),
            ["recorded_at"] = Timestamp(entry.RecordedAt),
            ["reused_from_id"] = entry.ReusedFromId is { } reused ? Id(reused) : null,
            ["seq"] = entry.Seq.ToString(CultureInfo.InvariantCulture),
            ["served_by"] = entry.ServedBy,
            ["state"] = entry.State,
            ["validation_status"] = entry.ValidationStatus,
        });
    }

    public static string Compute(string prevHash, AuditEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Compute(prevHash, new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["event_type"] = entry.EventType.ToString(),
            ["outcome"] = entry.Outcome,
            ["recorded_at"] = Timestamp(entry.RecordedAt),
            ["seq"] = entry.Seq.ToString(CultureInfo.InvariantCulture),
            ["subject_id"] = entry.SubjectId is { } subject ? Id(subject) : null,
            ["subject_type"] = entry.SubjectType,
            ["user_id"] = entry.UserId is { } user ? Id(user) : null,
            ["user_name"] = entry.UserName,
        });
    }

    private static string Compute(string prevHash, SortedDictionary<string, string?> fields)
    {
        ArgumentException.ThrowIfNullOrEmpty(prevHash);
        var payload = prevHash + "\n" + JsonSerializer.Serialize(fields, Canonical);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset value) =>
        Normalize(value).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>Resultado de verificar una cadena: la primera entrada rota y por qué.</summary>
public sealed record ChainVerification(bool IsIntact, long EntriesChecked, long? FirstBrokenSeq, string? Reason)
{
    public static ChainVerification Intact(long checkedCount) => new(true, checkedCount, null, null);
}

/// <summary>
/// Verifica una cadena leída en orden de <c>seq</c>. Detecta una fila alterada (el hash no coincide), una fila
/// borrada o insertada en medio (el <c>prev_hash</c> no enlaza) y un corte al inicio. Es lo que pide el "Hecho cuando"
/// de S2-06.
/// </summary>
public static class ChainVerifier
{
    public const string ReasonHashMismatch = "hash_mismatch";
    public const string ReasonBrokenLink = "broken_link";

    public static ChainVerification Verify<T>(IEnumerable<T> entriesInSeqOrder, Func<T, long> seq, Func<T, string> prevHash, Func<T, string> entryHash, Func<string, T, string> recompute)
    {
        ArgumentNullException.ThrowIfNull(entriesInSeqOrder);
        var expectedPrev = HashChain.Genesis;
        long count = 0;
        foreach (var entry in entriesInSeqOrder)
        {
            count++;
            if (!string.Equals(prevHash(entry), expectedPrev, StringComparison.Ordinal))
            {
                return new ChainVerification(false, count, seq(entry), ReasonBrokenLink);
            }

            if (!string.Equals(recompute(prevHash(entry), entry), entryHash(entry), StringComparison.Ordinal))
            {
                return new ChainVerification(false, count, seq(entry), ReasonHashMismatch);
            }

            expectedPrev = entryHash(entry);
        }

        return ChainVerification.Intact(count);
    }

    public static ChainVerification Verify(IEnumerable<TraceEntry> entries) =>
        Verify(entries, e => e.Seq, e => e.PrevHash, e => e.EntryHash, HashChain.Compute);

    public static ChainVerification Verify(IEnumerable<AuditEvent> entries) =>
        Verify(entries, e => e.Seq, e => e.PrevHash, e => e.EntryHash, HashChain.Compute);
}
