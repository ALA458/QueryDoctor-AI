namespace QueryDoctor.Domain.Analysis;

/// <summary>
/// Hallazgo estructurado emitido por una regla del motor determinístico al evaluar un plan (RF-MOT-03, RF-MOT-17, E-05). Tabla analysis.finding.
/// </summary>
public sealed class Finding
{
    public Guid Id { get; set; }

    public Guid AnalysisRunId { get; set; }

    public Guid ConnectionId { get; set; }

    public Guid QueryId { get; set; }

    public Guid PlanId { get; set; }

    /// <summary>
    /// Identificador determinístico del hallazgo (finding_id, formato F-XXXXXXXXXXXX) calculado a partir del hash de (plan, regla, versión, nodos) (Arquitectura §5.1, RF-MOT-02).
    /// </summary>
    public string FindingCode { get; set; } = string.Empty;

    /// <summary>Identificador de la regla que originó el hallazgo (por ejemplo, REG-01..REG-11).</summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>Versión semántica de la regla aplicada.</summary>
    public string RuleVersion { get; set; } = string.Empty;

    /// <summary>Versión del catálogo de reglas.</summary>
    public string CatalogVersion { get; set; } = string.Empty;

    /// <summary>Versión del esquema del contrato JSON del hallazgo (RF-MOT-17).</summary>
    public string SchemaVersion { get; set; } = string.Empty;

    /// <summary>Naturaleza del hallazgo como señal, indicio o plan no analizable (RF-MOT-03, RF-MOT-16).</summary>
    public FindingKind Kind { get; set; } = FindingKind.Senal;

    public FindingSeverity Severity { get; set; }

    public EvidenceClass EvidenceClass { get; set; }

    /// <summary>Lista JSON de los nodos del plan involucrados: [{ "node_id": 5, "physical_op": "..." }] (RF-MOT-03).</summary>
    public string NodesJson { get; set; } = "[]";

    /// <summary>
    /// Objeto JSON de evidencia YA cifrado con IFieldEncryptor (RNF-SEG-14, Arquitectura §7), ya que puede contener expresiones con literales del plan.
    /// </summary>
    public string EncryptedEvidenceJson { get; set; } = string.Empty;

    /// <summary>Datos numéricos de contexto usados para evaluar el hallazgo (ejecuciones, consumo %, filas estimadas) en JSON (RF-MOT-03).</summary>
    public string ContextJson { get; set; } = "{}";

    /// <summary>Umbrales aplicados por la regla en este hallazgo en JSON canónico (RF-MOT-14).</summary>
    public string ThresholdsUsedJson { get; set; } = "{}";

    /// <summary>Versión del motor de base de datos que compiló el plan (RNF-COM-02).</summary>
    public string? EngineVersion { get; set; }

    public DateTimeOffset AnalyzedAt { get; set; }
}
