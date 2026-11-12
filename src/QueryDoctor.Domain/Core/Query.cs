namespace QueryDoctor.Domain.Core;

/// <summary>
/// Consulta identificada en Query Store (RF-ING-01). Tabla core.query.
/// </summary>
public sealed class Query
{
    public Guid Id { get; set; }

    public Guid ConnectionId { get; set; }

    /// <summary>query_id en Query Store.</summary>
    public long QueryStoreQueryId { get; set; }

    public Guid? QueryTextId { get; set; }

    /// <summary>query_hash de Query Store en hexadecimal; sirve para deduplicar sin descifrar el texto.</summary>
    public string? QueryHash { get; set; }

    /// <summary>Contexto de origen (por ejemplo, el objeto que contiene la consulta).</summary>
    public string? OriginContext { get; set; }

    public DateTimeOffset IngestedAt { get; set; }
}
