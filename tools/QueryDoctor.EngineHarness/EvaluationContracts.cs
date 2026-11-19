using System.Text.Json.Serialization;
using QueryDoctor.Application.Engine.Rules;

namespace QueryDoctor.EngineHarness;

/// <summary>
/// Manifiesto versionado del conjunto de referencia anotado para el arnés de evaluación (RF-EVA-01, CA-HU20.1).
/// </summary>
public sealed record GroundTruthManifest(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("cases")] IReadOnlyList<GroundTruthCase> Cases);

/// <summary>
/// Caso individual de evaluación con ruta al Showplan XML, contexto de Query Store y hallazgos esperados.
/// </summary>
public sealed record GroundTruthCase(
    [property: JsonPropertyName("case_id")] string CaseId,
    [property: JsonPropertyName("corpus")] string Corpus,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("plan_path")] string PlanPath,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("context")] CaseExecutionContext? Context = null,
    [property: JsonPropertyName("expected_analysis_status")] string? ExpectedAnalysisStatus = null,
    [property: JsonPropertyName("expected_findings")] IReadOnlyList<ExpectedFindingSpec>? ExpectedFindings = null);

/// <summary>
/// Contexto agregado de Query Store asociado al plan (necesario para reglas contextuales REG-03 y REG-04).
/// </summary>
public sealed record CaseExecutionContext(
    [property: JsonPropertyName("query_store_query_id")] long QueryStoreQueryId = 0,
    [property: JsonPropertyName("query_store_plan_id")] long QueryStorePlanId = 0,
    [property: JsonPropertyName("query_execution_count")] long? QueryExecutionCount = null,
    [property: JsonPropertyName("query_consumption_pct")] double? QueryConsumptionPct = null,
    [property: JsonPropertyName("avg_rows_returned")] double? AvgRowsReturned = null);

/// <summary>
/// Hallazgo esperado en un caso de referencia para el cálculo de precisión y exhaustividad.
/// </summary>
public sealed record ExpectedFindingSpec(
    [property: JsonPropertyName("rule_id")] string RuleId,
    [property: JsonPropertyName("expected_nodes")] IReadOnlyList<int>? ExpectedNodes = null,
    [property: JsonPropertyName("rationale")] string? Rationale = null);

/// <summary>
/// Métricas agregadas por regla sobre un conjunto de evaluación.
/// </summary>
public sealed record RuleMetricsSummary(
    [property: JsonPropertyName("rule_id")] string RuleId,
    [property: JsonPropertyName("rule_name")] string RuleName,
    [property: JsonPropertyName("expected_positives")] int ExpectedPositives,
    [property: JsonPropertyName("emitted_total")] int EmittedTotal,
    [property: JsonPropertyName("true_positives")] int TruePositives,
    [property: JsonPropertyName("false_positives")] int FalsePositives,
    [property: JsonPropertyName("false_negatives")] int FalseNegatives,
    [property: JsonPropertyName("precision")] double? Precision,
    [property: JsonPropertyName("recall")] double? Recall,
    [property: JsonPropertyName("f1")] double? F1,
    [property: JsonPropertyName("meets_precision_target")] bool? MeetsPrecisionTarget,
    [property: JsonPropertyName("meets_recall_target")] bool? MeetsRecallTarget);

/// <summary>
/// Resumen de tolerancia a planes no analizables (REG-11 / RF-MOT-16) evaluado por separado de REG-01..REG-04.
/// </summary>
public sealed record NotAnalyzableMetrics(
    [property: JsonPropertyName("expected_not_analyzable")] int ExpectedNotAnalyzable,
    [property: JsonPropertyName("correctly_classified_reg11")] int CorrectlyClassifiedReg11,
    [property: JsonPropertyName("unexpected_not_analyzable")] int UnexpectedNotAnalyzable);

/// <summary>
/// Diferencia puntual (FP o FN) detectada en un caso, útil para calibración (S3-01) e informe (S3-05).
/// </summary>
public sealed record FindingDiffItem(
    [property: JsonPropertyName("case_id")] string CaseId,
    [property: JsonPropertyName("corpus")] string Corpus,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("rule_id")] string RuleId,
    [property: JsonPropertyName("nodes")] IReadOnlyList<int> Nodes,
    [property: JsonPropertyName("detail")] string Detail);

/// <summary>
/// Informe completo emitido por el arnés de evaluación del motor (RF-EVA-01).
/// </summary>
public sealed record EngineEvaluationReport(
    [property: JsonPropertyName("catalog_version")] string CatalogVersion,
    [property: JsonPropertyName("thresholds_hash")] string ThresholdsHash,
    [property: JsonPropertyName("thresholds")] IReadOnlyDictionary<string, double> Thresholds,
    [property: JsonPropertyName("corpus_filter")] string CorpusFilter,
    [property: JsonPropertyName("split_filter")] string SplitFilter,
    [property: JsonPropertyName("total_cases")] int TotalCases,
    [property: JsonPropertyName("seeded_cases")] int SeededCases,
    [property: JsonPropertyName("tpch_cases")] int TpchCases,
    [property: JsonPropertyName("rules")] IReadOnlyList<RuleMetricsSummary> Rules,
    [property: JsonPropertyName("seeded_rules")] IReadOnlyList<RuleMetricsSummary> SeededRules,
    [property: JsonPropertyName("tpch_rules")] IReadOnlyList<RuleMetricsSummary> TpchRules,
    [property: JsonPropertyName("not_analyzable")] NotAnalyzableMetrics NotAnalyzable,
    [property: JsonPropertyName("diffs")] IReadOnlyList<FindingDiffItem> Diffs,
    [property: JsonPropertyName("all_must_targets_met")] bool AllMustTargetsMet);

/// <summary>
/// Paquete exportado de casos evaluados de extremo a extremo para contraste con especialista (S3-04, RF-EVA-03).
/// </summary>
public sealed record SpecialistCaseExportBundle(
    [property: JsonPropertyName("catalog_version")] string CatalogVersion,
    [property: JsonPropertyName("thresholds_hash")] string ThresholdsHash,
    [property: JsonPropertyName("score_parameters_hash")] string ScoreParametersHash,
    [property: JsonPropertyName("thresholds")] IReadOnlyDictionary<string, double> Thresholds,
    [property: JsonPropertyName("cases")] IReadOnlyList<SpecialistExportedCase> Cases);

/// <summary>
/// Caso individual exportado con SQL, resumen del plan XML, puntaje Query Health Score y hallazgos del motor calibrado.
/// </summary>
public sealed record SpecialistExportedCase(
    [property: JsonPropertyName("case_id")] string CaseId,
    [property: JsonPropertyName("corpus")] string Corpus,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("plan_path")] string PlanPath,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("sql_text")] string SqlText,
    [property: JsonPropertyName("masked_sql_text")] string MaskedSqlText,
    [property: JsonPropertyName("context")] CaseExecutionContext Context,
    [property: JsonPropertyName("health_score")] SpecialistHealthScoreSummary HealthScore,
    [property: JsonPropertyName("plan_summary")] SpecialistPlanSummary PlanSummary,
    [property: JsonPropertyName("findings")] IReadOnlyList<FindingContract> Findings);

/// <summary>
/// Resumen determinístico del puntaje Query Health Score dentro de la ventana de contraste (RF-SCO-01..06, RF-EVA-05).
/// </summary>
public sealed record SpecialistHealthScoreSummary(
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("priority")] double Priority,
    [property: JsonPropertyName("band")] string Band,
    [property: JsonPropertyName("impact_component")] double ImpactComponent,
    [property: JsonPropertyName("severity_component")] double SeverityComponent,
    [property: JsonPropertyName("weighted_consumption_pct")] double WeightedConsumptionPct,
    [property: JsonPropertyName("rank_by_score_asc")] int RankByScoreAsc,
    [property: JsonPropertyName("rank_by_consumption_desc")] int RankByConsumptionDesc);

/// <summary>
/// Resumen estructural del Showplan XML analizado sin exponer reglas internas en Etapa A ciega.
/// </summary>
public sealed record SpecialistPlanSummary(
    [property: JsonPropertyName("engine_version")] string? EngineVersion,
    [property: JsonPropertyName("statement_optm_level")] string? StatementOptmLevel,
    [property: JsonPropertyName("statement_optm_early_abort_reason")] string? StatementOptmEarlyAbortReason,
    [property: JsonPropertyName("statement_subtree_cost")] double? StatementSubTreeCost,
    [property: JsonPropertyName("statement_est_rows")] double? StatementEstRows,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("missing_indexes")] IReadOnlyList<string> MissingIndexes,
    [property: JsonPropertyName("operators")] IReadOnlyList<SpecialistPlanOperatorSummary> Operators);

/// <summary>
/// Operador resumido dentro del árbol Showplan XML para inspección del especialista.
/// </summary>
public sealed record SpecialistPlanOperatorSummary(
    [property: JsonPropertyName("node_id")] int NodeId,
    [property: JsonPropertyName("physical_op")] string PhysicalOp,
    [property: JsonPropertyName("logical_op")] string LogicalOp,
    [property: JsonPropertyName("estimate_rows")] double EstimateRows,
    [property: JsonPropertyName("estimated_rows_read")] double? EstimatedRowsRead,
    [property: JsonPropertyName("estimated_total_subtree_cost")] double EstimatedTotalSubtreeCost,
    [property: JsonPropertyName("target_object")] string? TargetObject,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);
