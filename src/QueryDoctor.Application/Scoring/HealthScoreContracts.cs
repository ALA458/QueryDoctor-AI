using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Scoring;

/// <summary>
/// Muestra de consumo de recursos de un intervalo de ejecución en Query Store dentro de la ventana W (Ecuación 4.1).
/// </summary>
public sealed record QueryResourceSample(
    long ExecutionCount,
    double AvgCpuTimeMicroseconds,
    double AvgDurationMicroseconds,
    double AvgLogicalIoReads);

/// <summary>
/// Hallazgo determinístico asociado a una consulta que participa en el cálculo de severidad <c>S(q)</c> (Ecuación 4.3).
/// </summary>
public sealed record QueryFindingSample(
    string RuleId,
    string FindingCode,
    FindingSeverity Severity,
    EvidenceClass EvidenceClass,
    FindingKind Kind = FindingKind.Senal);

/// <summary>
/// Datos de entrada puros de una consulta dentro de la ventana de análisis W para calcular su puntaje de salud <c>H(q)</c>.
/// </summary>
public sealed record QueryScoreInput(
    Guid QueryId,
    long QueryStoreQueryId,
    IReadOnlyList<QueryResourceSample> ResourceSamples,
    IReadOnlyList<QueryFindingSample> Findings);

/// <summary>
/// Criterio de ordenamiento del listado de puntajes de salud (RF-SCO-04).
/// </summary>
public enum HealthScoreOrderBy
{
    /// <summary>Ordenar por puntaje <c>H(q)</c> ascendente (peor primero, mayor prioridad de revisión).</summary>
    ScoreAscending,

    /// <summary>Ordenar por porcentaje de consumo ponderado descendente sin ponderar por severidad.</summary>
    ConsumptionDescending,
}

/// <summary>
/// Desglose del aporte individual de un hallazgo determinístico al componente de severidad <c>S(q)</c> (RF-SCO-03, Ecuación 4.3).
/// </summary>
public sealed record FindingSeverityContribution(
    [property: JsonPropertyName("rule_id")] string RuleId,
    [property: JsonPropertyName("finding_code")] string FindingCode,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("evidence_class")] string EvidenceClass,
    [property: JsonPropertyName("severity_weight")] double SeverityWeight,
    [property: JsonPropertyName("confidence_weight")] double ConfidenceWeight,
    [property: JsonPropertyName("combined_weight")] double CombinedWeight);

/// <summary>
/// Participación de consumo de recursos de una consulta en la ventana W (Ecuaciones 4.1 y 4.2).
/// </summary>
public sealed record QueryConsumptionShare(
    Guid QueryId,
    long QueryStoreQueryId,
    long TotalExecutions,
    double TotalCpuMicroseconds,
    double TotalDurationMicroseconds,
    double TotalLogicalReads,
    double CpuShare,
    double DurationShare,
    double LogicalReadsShare,
    double WeightedResourceShare,
    double WeightedConsumptionPct);

/// <summary>
/// Resultado puro y determinista del cálculo del puntaje de salud <c>H(q)</c> y su desglose completo (RF-SCO-01..06, CA-HU08.1..08.3).
/// </summary>
public sealed record QueryHealthScoreResult(
    Guid QueryId,
    long QueryStoreQueryId,
    double Score,
    double Priority,
    HealthScoreBand Band,
    string BandCode,
    double ImpactComponent,
    double SeverityComponent,
    double AlphaUsed,
    double CpuShare,
    double DurationShare,
    double LogicalReadsShare,
    double WeightedConsumptionPct,
    long TotalExecutions,
    double TotalCpuMicroseconds,
    double TotalDurationMicroseconds,
    double TotalLogicalReads,
    int FindingsCount,
    IReadOnlyList<FindingSeverityContribution> ContributingFindings,
    string ContributingFindingsJson,
    HealthScoreParameters ParametersUsed,
    string ParametersJson,
    string ParametersHash,
    string ScaleDirection,
    string UsageDisclaimer)
{
    /// <summary>
    /// Convierte este resultado puro en una entidad de dominio <see cref="HealthScore"/> lista para persistir en <c>analysis.health_score</c>.
    /// </summary>
    public HealthScore ToEntity(
        Guid connectionId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        DateTimeOffset calculatedAt,
        Guid? ingestionRunId = null,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            ConnectionId = connectionId,
            QueryId = QueryId,
            IngestionRunId = ingestionRunId,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            Score = Score,
            Priority = Priority,
            Band = Band,
            ImpactComponent = ImpactComponent,
            SeverityComponent = SeverityComponent,
            AlphaUsed = AlphaUsed,
            CpuShare = CpuShare,
            DurationShare = DurationShare,
            LogicalReadsShare = LogicalReadsShare,
            WeightedConsumptionPct = WeightedConsumptionPct,
            TotalExecutions = TotalExecutions,
            TotalCpuMicroseconds = TotalCpuMicroseconds,
            TotalDurationMicroseconds = TotalDurationMicroseconds,
            TotalLogicalReads = TotalLogicalReads,
            FindingsCount = FindingsCount,
            ContributingFindingsJson = ContributingFindingsJson,
            ParametersJson = ParametersJson,
            ParametersHash = ParametersHash,
            CalculatedAt = calculatedAt,
        };
}

/// <summary>
/// Serializador determinista para el desglose JSON de hallazgos que contribuyen a <c>S(q)</c>.
/// </summary>
public static class HealthScoreJson
{
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string SerializeContributingFindings(IReadOnlyList<FindingSeverityContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        if (contributions.Count == 0)
        {
            return "[]";
        }

        return JsonSerializer.Serialize(contributions, CompactOptions);
    }

    public static IReadOnlyList<FindingSeverityContribution> DeserializeContributingFindings(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<FindingSeverityContribution>>(json, CompactOptions) ?? [];
    }
}

/// <summary>
/// Calculadora pura y determinista del puntaje compuesto de salud de consulta <c>H(q)</c> (RF-SCO-01..06, Invariante 7).
/// </summary>
public interface IHealthScoreCalculator
{
    /// <summary>
    /// Calcula las participaciones relativas de consumo <c>s_CPU, s_DUR, s_LECT</c> y el porcentaje de consumo ponderado
    /// para todas las consultas de la ventana W (Ecuación 4.1).
    /// </summary>
    IReadOnlyDictionary<Guid, QueryConsumptionShare> CalculateConsumptionShares(
        IReadOnlyList<QueryScoreInput> workloadQueries,
        HealthScoreParameters? parameters = null);

    /// <summary>
    /// Calcula de forma determinista el puntaje de salud <c>H(q)</c>, el índice de prioridad <c>P(q)</c> y el desglose
    /// completo para cada consulta de la ventana W según las Ecuaciones 4.1 a 4.5 del SRS v1.1.
    /// </summary>
    IReadOnlyList<QueryHealthScoreResult> CalculateWindow(
        IReadOnlyList<QueryScoreInput> workloadQueries,
        HealthScoreParameters? parameters = null,
        HealthScoreOrderBy orderBy = HealthScoreOrderBy.ScoreAscending);
}
