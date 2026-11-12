namespace QueryDoctor.Domain.Core;

/// <summary>
/// Texto SQL de una consulta de Query Store (RF-ING-01, RF-ING-07). Tabla core.query_text.
/// </summary>
public sealed class QueryText
{
    public Guid Id { get; set; }

    public Guid ConnectionId { get; set; }

    /// <summary>query_sql_text_id en Query Store.</summary>
    public long QueryStoreTextId { get; set; }

    /// <summary>
    /// Texto SQL YA cifrado con IFieldEncryptor (RNF-SEG-14). Se pone en null al purgar por retención (RNF-SEG-10).
    /// </summary>
    public string? EncryptedSqlText { get; set; }

    public DateTimeOffset IngestedAt { get; set; }
}
