using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Scoring;

/// <summary>
/// Instantánea del plan más reciente de una consulta dentro de la ventana de análisis para evaluar reglas y puntaje.
/// </summary>
public sealed record WindowPlanSnapshot(
    Guid PlanId,
    long QueryStorePlanId,
    string? EncryptedPlanXml,
    string? EngineVersion,
    PlanAnalysisStatus AnalysisStatus,
    string? NotAnalyzableCause,
    long PlanExecutionCount,
    double? AvgRowsReturned,
    bool HasCurrentAnalysisRun,
    IReadOnlyList<QueryFindingSample> ExistingFindings);

/// <summary>
/// Instantánea de una consulta capturada y sus muestras de recursos en la ventana de análisis W.
/// </summary>
public sealed record WindowQuerySnapshot(
    Guid QueryId,
    long QueryStoreQueryId,
    IReadOnlyList<QueryResourceSample> ResourceSamples,
    WindowPlanSnapshot? LatestPlan);

/// <summary>
/// Instantánea completa de la ventana de análisis W de una conexión para ejecutar el análisis determinista y el cálculo de <c>H(q)</c>.
/// </summary>
public sealed record ConnectionScoringWindowSnapshot(
    Guid ConnectionId,
    Guid? IngestionRunId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string? EngineVersion,
    bool ActualPlanAvailable,
    IReadOnlyList<WindowQuerySnapshot> Queries);

/// <summary>
/// Resultado del análisis determinístico de un plan listo para persistir en <c>analysis.analysis_run</c> y <c>analysis.finding</c>.
/// </summary>
public sealed record AnalyzedPlanPersistenceItem(
    Guid PlanId,
    PlanAnalysisStatus UpdatedPlanAnalysisStatus,
    AnalysisRun AnalysisRun,
    IReadOnlyList<Finding> Findings);

/// <summary>
/// Lote atómico de persistencia de corridas de análisis, hallazgos y puntajes históricos de una conexión tras una ingesta.
/// </summary>
public sealed record ConnectionScoringPersistenceBatch(
    Guid ConnectionId,
    Guid? IngestionRunId,
    ThresholdSet ThresholdSet,
    IReadOnlyList<AnalyzedPlanPersistenceItem> AnalyzedPlans,
    IReadOnlyList<HealthScore> HealthScores);

/// <summary>
/// Resultado del caso de uso <see cref="RecalculateConnectionScores"/> tras analizar planes y recalcular los puntajes de una conexión.
/// </summary>
public sealed record ScoreRecalculationOutcome(
    Guid ConnectionId,
    Guid? IngestionRunId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    int QueriesScored,
    int PlansAnalyzed,
    IReadOnlyList<QueryHealthScoreResult> Scores);

/// <summary>
/// Puerto de persistencia para cargar la ventana de una conexión, guardar los hallazgos y puntajes calculados
/// y consultar tanto los puntajes vigentes como la serie histórica por consulta (RF-SCO-04, RF-SCO-05).
/// </summary>
public interface IScoreRecalculationStore
{
    Task<ConnectionScoringWindowSnapshot?> LoadWindowSnapshotAsync(
        Guid connectionId,
        Guid? ingestionRunId,
        TimeSpan windowDuration,
        string catalogVersion,
        string thresholdConfigurationHash,
        CancellationToken cancellationToken = default);

    Task SaveBatchAsync(
        ConnectionScoringPersistenceBatch batch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el puntaje más reciente de cada consulta de la conexión, ordenado según <paramref name="orderBy"/> (RF-SCO-04).
    /// </summary>
    Task<IReadOnlyList<HealthScore>> GetLatestConnectionScoresAsync(
        Guid connectionId,
        HealthScoreOrderBy orderBy = HealthScoreOrderBy.ScoreAscending,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el historial cronológico completo de puntajes de una consulta para mostrar su evolución en el tiempo (RF-SCO-05, CA-HU04.3).
    /// </summary>
    Task<IReadOnlyList<HealthScore>> GetQueryScoreHistoryAsync(
        Guid connectionId,
        Guid queryId,
        CancellationToken cancellationToken = default);
}
