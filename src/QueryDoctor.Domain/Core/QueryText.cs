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

    /// <summary>
    /// Cuándo se purgó el texto por retención o a pedido del DBA (S2-13, RNF-SEG-10). Distingue "evidencia expirada"
    /// de un texto que nunca llegó: con valor, <see cref="EncryptedSqlText"/> es null y no vuelve (la ingesta solo inserta).
    /// </summary>
    public DateTimeOffset? PurgedAt { get; set; }

    public DateTimeOffset IngestedAt { get; set; }
}
