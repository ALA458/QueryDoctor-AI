namespace QueryDoctor.Domain.Ai;

/// <summary>
/// Fragmento curado de la base de conocimiento técnica de SQL Server (RF-IA-04, RNF-FUN-02).
/// Contiene texto técnico explicativo con citas formales a fuentes oficiales (Microsoft Learn o libros de internals).
/// </summary>
public sealed class KbFragment
{
    public Guid Id { get; set; }

    /// <summary>
    /// Identificador de la regla a la que pertenece principalmente (ej. "REG-01", "REG-02", "REG-03", "REG-04").
    /// </summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    /// Título conciso del fragmento de conocimiento.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Contenido técnico explicativo curado.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Título formal de la fuente oficial o bibliográfica citada.
    /// </summary>
    public string SourceTitle { get; set; } = string.Empty;

    /// <summary>
    /// URL canónica o identificador de acceso a la fuente oficial.
    /// </summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Sección, capítulo o cita textual de la fuente oficial.
    /// </summary>
    public string SourceCitation { get; set; } = string.Empty;

    /// <summary>
    /// Fecha y hora de creación o versión del fragmento.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Relaciones causa-efecto asociadas a este fragmento.
    /// </summary>
    public ICollection<KbCausalRelation> CausalRelations { get; set; } = new List<KbCausalRelation>();
}
