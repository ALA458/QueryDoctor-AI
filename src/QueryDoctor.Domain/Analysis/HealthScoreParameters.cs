using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace QueryDoctor.Domain.Analysis;

/// <summary>
/// Parámetros configurables del puntaje compuesto de salud de consulta <c>H(q)</c> con los valores por defecto
/// definidos en las Ecuaciones 4.1 a 4.5 del SRS v1.1 (RF-SCO-01, RF-SCO-02, RF-SCO-06, CA-HU08.1..08.3, DA-01).
/// Todos los pesos son provisionales (†) y se persisten junto con cada puntaje calculado para trazabilidad.
/// </summary>
public sealed record HealthScoreParameters
{
    public const string SectionName = "HealthScore";

    public const double DefaultCpuWeight = 0.4;
    public const double DefaultDurationWeight = 0.3;
    public const double DefaultLogicalReadsWeight = 0.3;
    public const double DefaultSaturationCapTau = 0.05;
    public const double DefaultBasePriorityAlpha = 0.5;

    public const double DefaultLowSeverityWeight = 0.2;
    public const double DefaultMediumSeverityWeight = 0.5;
    public const double DefaultHighSeverityWeight = 0.8;

    public const double DefaultEstimatedConfidenceWeight = 0.6;
    public const double DefaultAggregatedExecutionConfidenceWeight = 0.8;
    public const double DefaultActualPlanConfidenceWeight = 1.0;

    public const double DefaultCriticalUpperExclusive = 40.0;
    public const double DefaultAttentionUpperExclusive = 70.0;

    /// <summary>
    /// Dirección normativa de la escala del puntaje (RF-SCO-01, CA-HU08.3).
    /// </summary>
    public const string ScaleDirection =
        "100 es saludable; los valores bajos indican mayor prioridad de revisión";

    /// <summary>
    /// Advertencia normativa de uso del puntaje hasta completar RF-EVA-05 (SRS §4.4, pág. 42).
    /// </summary>
    public const string UsageDisclaimer =
        "Hasta que el requisito RF-EVA-05 reporte resultados, el score se presenta como un índice de prioridad de revisión y no como una medida de la calidad de una consulta.";

    private const double WeightSumTolerance = 1e-6;

    /// <summary>Instancia inmutable con todos los parámetros por defecto del SRS v1.1 (Ecuaciones 4.1–4.5).</summary>
    public static HealthScoreParameters Default { get; } = new();

    /// <summary>Peso de la participación en CPU <c>w_CPU</c> (por defecto 0.4 †, Ecuación 4.2).</summary>
    public double CpuWeight { get; init; } = DefaultCpuWeight;

    /// <summary>Peso de la participación en duración <c>w_DUR</c> (por defecto 0.3 †, Ecuación 4.2).</summary>
    public double DurationWeight { get; init; } = DefaultDurationWeight;

    /// <summary>Peso de la participación en lecturas lógicas <c>w_LECT</c> (por defecto 0.3 †, Ecuación 4.2).</summary>
    public double LogicalReadsWeight { get; init; } = DefaultLogicalReadsWeight;

    /// <summary>Tope de saturación de impacto <c>tau</c> en (0, 1] (por defecto 0.05 = 5 % †, Ecuación 4.2).</summary>
    public double SaturationCapTau { get; init; } = DefaultSaturationCapTau;

    /// <summary>Prioridad base por consumo <c>alpha</c> en [0, 1] (por defecto 0.5 †, Ecuación 4.4).</summary>
    public double BasePriorityAlpha { get; init; } = DefaultBasePriorityAlpha;

    /// <summary>Peso de severidad para hallazgos de severidad Baja (por defecto 0.2, Ecuación 4.3).</summary>
    public double LowSeverityWeight { get; init; } = DefaultLowSeverityWeight;

    /// <summary>Peso de severidad para hallazgos de severidad Media (por defecto 0.5, Ecuación 4.3).</summary>
    public double MediumSeverityWeight { get; init; } = DefaultMediumSeverityWeight;

    /// <summary>Peso de severidad para hallazgos de severidad Alta (por defecto 0.8, Ecuación 4.3).</summary>
    public double HighSeverityWeight { get; init; } = DefaultHighSeverityWeight;

    /// <summary>Peso de confianza para evidencia Estimada (por defecto 0.6, Ecuación 4.3).</summary>
    public double EstimatedConfidenceWeight { get; init; } = DefaultEstimatedConfidenceWeight;

    /// <summary>Peso de confianza para evidencia de Ejecución Agregada (por defecto 0.8, Ecuación 4.3).</summary>
    public double AggregatedExecutionConfidenceWeight { get; init; } = DefaultAggregatedExecutionConfidenceWeight;

    /// <summary>Peso de confianza para evidencia de Plan Real (por defecto 1.0, Ecuación 4.3).</summary>
    public double ActualPlanConfidenceWeight { get; init; } = DefaultActualPlanConfidenceWeight;

    /// <summary>Cota superior exclusiva de la banda Crítica (por defecto 40.0, es decir 0–39 Crítica, RF-SCO-06).</summary>
    public double CriticalUpperExclusive { get; init; } = DefaultCriticalUpperExclusive;

    /// <summary>Cota superior exclusiva de la banda Atención (por defecto 70.0, es decir 40–69 Atención y 70–100 Saludable, RF-SCO-06).</summary>
    public double AttentionUpperExclusive { get; init; } = DefaultAttentionUpperExclusive;

    /// <summary>
    /// Valida que todos los parámetros cumplan las restricciones matemáticas de las Ecuaciones 4.1 a 4.5 del SRS.
    /// </summary>
    public void Validate()
    {
        ValidateUnitInterval(CpuWeight, nameof(CpuWeight));
        ValidateUnitInterval(DurationWeight, nameof(DurationWeight));
        ValidateUnitInterval(LogicalReadsWeight, nameof(LogicalReadsWeight));

        var weightSum = CpuWeight + DurationWeight + LogicalReadsWeight;
        if (Math.Abs(weightSum - 1.0) > WeightSumTolerance)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Los pesos de recursos (CpuWeight + DurationWeight + LogicalReadsWeight) deben sumar 1.0; suma actual: {weightSum:G17}."));
        }

        if (double.IsNaN(SaturationCapTau) || double.IsInfinity(SaturationCapTau) || SaturationCapTau <= 0.0 || SaturationCapTau > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SaturationCapTau),
                SaturationCapTau,
                "El tope de saturación tau debe pertenecer al intervalo (0, 1].");
        }

        ValidateUnitInterval(BasePriorityAlpha, nameof(BasePriorityAlpha));
        ValidateUnitInterval(LowSeverityWeight, nameof(LowSeverityWeight));
        ValidateUnitInterval(MediumSeverityWeight, nameof(MediumSeverityWeight));
        ValidateUnitInterval(HighSeverityWeight, nameof(HighSeverityWeight));
        ValidateUnitInterval(EstimatedConfidenceWeight, nameof(EstimatedConfidenceWeight));
        ValidateUnitInterval(AggregatedExecutionConfidenceWeight, nameof(AggregatedExecutionConfidenceWeight));
        ValidateUnitInterval(ActualPlanConfidenceWeight, nameof(ActualPlanConfidenceWeight));

        if (double.IsNaN(CriticalUpperExclusive) ||
            double.IsNaN(AttentionUpperExclusive) ||
            CriticalUpperExclusive <= 0.0 ||
            CriticalUpperExclusive >= AttentionUpperExclusive ||
            AttentionUpperExclusive > 100.0)
        {
            throw new InvalidOperationException(
                "Los umbrales de banda deben cumplir 0 < CriticalUpperExclusive < AttentionUpperExclusive <= 100.");
        }
    }

    /// <summary>
    /// Devuelve el peso numérico <c>sev_i</c> asociado a la severidad de un hallazgo (Ecuación 4.3).
    /// </summary>
    public double GetSeverityWeight(FindingSeverity severity) =>
        severity switch
        {
            FindingSeverity.Baja => LowSeverityWeight,
            FindingSeverity.Media => MediumSeverityWeight,
            FindingSeverity.Alta => HighSeverityWeight,
            FindingSeverity.Informativa => 0.0,
            _ => 0.0,
        };

    /// <summary>
    /// Devuelve el peso numérico de confianza <c>conf_i</c> asociado a la clase de evidencia de un hallazgo (Ecuación 4.3).
    /// </summary>
    public double GetConfidenceWeight(EvidenceClass evidenceClass) =>
        evidenceClass switch
        {
            EvidenceClass.Estimada => EstimatedConfidenceWeight,
            EvidenceClass.EjecucionAgregada => AggregatedExecutionConfidenceWeight,
            EvidenceClass.PlanReal => ActualPlanConfidenceWeight,
            EvidenceClass.NoAplica => 0.0,
            _ => 0.0,
        };

    /// <summary>
    /// Clasifica un puntaje <c>H(q)</c> en su banda de interpretación normativa (RF-SCO-06, CA-HU08.3):
    /// 0–39 Crítica (<c>&lt; CriticalUpperExclusive</c>), 40–69 Atención (<c>&lt; AttentionUpperExclusive</c>), 70–100 Saludable.
    /// </summary>
    public HealthScoreBand ResolveBand(double score)
    {
        if (score < CriticalUpperExclusive)
        {
            return HealthScoreBand.Critica;
        }

        if (score < AttentionUpperExclusive)
        {
            return HealthScoreBand.Atencion;
        }

        return HealthScoreBand.Saludable;
    }

    /// <summary>
    /// Devuelve todos los parámetros de configuración como diccionario ordenado canónico.
    /// </summary>
    public IReadOnlyDictionary<string, double> ToDictionary() =>
        new SortedDictionary<string, double>(StringComparer.Ordinal)
        {
            ["actual_plan_confidence_weight"] = ActualPlanConfidenceWeight,
            ["aggregated_execution_confidence_weight"] = AggregatedExecutionConfidenceWeight,
            ["attention_upper_exclusive"] = AttentionUpperExclusive,
            ["base_priority_alpha"] = BasePriorityAlpha,
            ["cpu_weight"] = CpuWeight,
            ["critical_upper_exclusive"] = CriticalUpperExclusive,
            ["duration_weight"] = DurationWeight,
            ["estimated_confidence_weight"] = EstimatedConfidenceWeight,
            ["high_severity_weight"] = HighSeverityWeight,
            ["logical_reads_weight"] = LogicalReadsWeight,
            ["low_severity_weight"] = LowSeverityWeight,
            ["medium_severity_weight"] = MediumSeverityWeight,
            ["saturation_cap_tau"] = SaturationCapTau,
        };

    /// <summary>
    /// Representación JSON canónica (claves ordenadas, sin espacios, cultura invariante) para persistencia en <c>analysis.health_score</c> (RF-SCO-02).
    /// </summary>
    public string ToCanonicalJson() => RuleThresholds.SerializeDictionaryToCanonicalJson(ToDictionary());

    /// <summary>
    /// Calcula el hash SHA-256 en hexadecimal (64 caracteres en mayúsculas) sobre el JSON canónico de los parámetros (RF-SCO-02).
    /// </summary>
    public string ComputeConfigurationHash()
    {
        var bytes = Encoding.UTF8.GetBytes(ToCanonicalJson());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static void ValidateUnitInterval(double value, string paramName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"El parámetro {paramName} debe pertenecer al intervalo [0, 1].");
        }
    }
}
