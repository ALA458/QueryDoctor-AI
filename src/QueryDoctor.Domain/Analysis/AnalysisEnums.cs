namespace QueryDoctor.Domain.Analysis;

/// <summary>
/// Clase de evidencia técnica definida en el Apéndice A.4 y la Tabla 4.11 del SRS.
/// </summary>
public enum RuleClass
{
    ClassA,
    ClassB,
    ClassC,
    ClassD,
    None,
}

/// <summary>
/// Categoría que indica el origen de la evidencia de un hallazgo (SRS §1.3, §2.8, RF-MOT-03, CA-HU05.4).
/// </summary>
public enum EvidenceClass
{
    Estimada,
    EjecucionAgregada,
    PlanReal,
    NoAplica,
}

/// <summary>
/// Severidad base o evaluada de un hallazgo del motor determinístico (RF-MOT-03, RF-SCO-02, Tabla 4.11).
/// </summary>
public enum FindingSeverity
{
    Informativa,
    Baja,
    Media,
    Alta,
}

/// <summary>
/// Naturaleza declarada de un hallazgo (RF-MOT-03, RF-MOT-16, Apéndice B Listing B.1).
/// Un hallazgo es una señal a investigar o un indicio, nunca un diagnóstico concluyente.
/// </summary>
public enum FindingKind
{
    Senal,
    Indicio,
    PlanNoAnalizable,
}

/// <summary>
/// Prioridad MoSCoW declarada por cada regla en el catálogo (Tabla 4.11 del SRS).
/// </summary>
public enum RulePriority
{
    Must,
    Should,
    Could,
}

/// <summary>
/// Estado de una corrida de análisis de un plan (tabla analysis.analysis_run).
/// </summary>
public enum AnalysisRunStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// Banda de interpretación del puntaje de salud H(q) (RF-SCO-06, CA-HU08.3, E-07):
/// 0–39 Crítica, 40–69 Atención, 70–100 Saludable.
/// </summary>
public enum HealthScoreBand
{
    Critica,
    Atencion,
    Saludable,
}

/// <summary>
/// Conversiones canónicas entre las enumeraciones del motor y las etiquetas normativas del contrato JSON del SRS (RF-MOT-03, RF-MOT-17, RF-SCO-06, Apéndice B).
/// </summary>
public static class AnalysisEnumCodes
{
    public static string ToContractCode(this EvidenceClass evidenceClass) =>
        evidenceClass switch
        {
            EvidenceClass.Estimada => "ESTIMADA",
            EvidenceClass.EjecucionAgregada => "EJECUCION_AGREGADA",
            EvidenceClass.PlanReal => "PLAN_REAL",
            EvidenceClass.NoAplica => "NO_APLICA",
            _ => "NO_APLICA",
        };

    public static EvidenceClass ParseEvidenceClass(string? code) =>
        code?.Trim().ToUpperInvariant() switch
        {
            "ESTIMADA" or "ESTIMATED" => EvidenceClass.Estimada,
            "EJECUCION_AGREGADA" or "AGGREGATEDEXECUTION" => EvidenceClass.EjecucionAgregada,
            "PLAN_REAL" or "ACTUALPLAN" => EvidenceClass.PlanReal,
            _ => EvidenceClass.NoAplica,
        };

    public static string ToContractCode(this FindingSeverity severity) =>
        severity switch
        {
            FindingSeverity.Informativa => "INFORMATIVA",
            FindingSeverity.Baja => "BAJA",
            FindingSeverity.Media => "MEDIA",
            FindingSeverity.Alta => "ALTA",
            _ => "INFORMATIVA",
        };

    public static FindingSeverity ParseFindingSeverity(string? code) =>
        code?.Trim().ToUpperInvariant() switch
        {
            "ALTA" or "HIGH" => FindingSeverity.Alta,
            "MEDIA" or "MEDIUM" => FindingSeverity.Media,
            "BAJA" or "LOW" => FindingSeverity.Baja,
            _ => FindingSeverity.Informativa,
        };

    public static string ToContractCode(this FindingKind kind) =>
        kind switch
        {
            FindingKind.Senal => "SENAL",
            FindingKind.Indicio => "INDICIO",
            FindingKind.PlanNoAnalizable => "PLAN_NO_ANALIZABLE",
            _ => "SENAL",
        };

    public static FindingKind ParseFindingKind(string? code) =>
        code?.Trim().ToUpperInvariant() switch
        {
            "INDICIO" => FindingKind.Indicio,
            "PLAN_NO_ANALIZABLE" => FindingKind.PlanNoAnalizable,
            _ => FindingKind.Senal,
        };

    public static string ToContractCode(this RuleClass ruleClass) =>
        ruleClass switch
        {
            RuleClass.ClassA => "A",
            RuleClass.ClassB => "B",
            RuleClass.ClassC => "C",
            RuleClass.ClassD => "D",
            RuleClass.None => "-",
            _ => "-",
        };

    public static string ToContractCode(this HealthScoreBand band) =>
        band switch
        {
            HealthScoreBand.Critica => "CRITICA",
            HealthScoreBand.Atencion => "ATENCION",
            HealthScoreBand.Saludable => "SALUDABLE",
            _ => "SALUDABLE",
        };

    public static HealthScoreBand ParseHealthScoreBand(string? code) =>
        code?.Trim().ToUpperInvariant() switch
        {
            "CRITICA" or "CRÍTICA" or "CRITICAL" => HealthScoreBand.Critica,
            "ATENCION" or "ATENCIÓN" or "ATTENTION" => HealthScoreBand.Atencion,
            "SALUDABLE" or "HEALTHY" => HealthScoreBand.Saludable,
            _ => HealthScoreBand.Saludable,
        };
}
