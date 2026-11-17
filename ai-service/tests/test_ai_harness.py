"""Pruebas unitarias y de integración para el arnés de evaluación de IA (RF-EVA-02, CA-HU20.2)."""

import pytest
from eval.control_condition import ControlConditionEvaluator
from eval.dataset import build_evaluation_dataset
from eval.harness import AiEvaluationHarness, CheckpointPausedError
from eval.metrics import (
    _validate_finding_references,
    calculate_aggregate_metrics,
    evaluate_explanation,
    extract_numbers_from_text,
    is_number_concordant,
)
from src.config import Settings
from src.models.schemas import ClaimOutput, ClaimType, ExplainMetadata, ExplainResponse
from src.providers.mock import MockProvider


def test_dataset_contains_at_least_30_queries_with_coverage():
    """El conjunto de evaluación debe contener al menos 30 consultas y cubrir todas las reglas y casos adversariales."""
    dataset = build_evaluation_dataset()
    assert len(dataset) >= 30, f"Se esperaban al menos 30 consultas, pero hay {len(dataset)}"

    categories = {case.category for case in dataset}
    assert "standard_diagnostic" in categories
    assert "adversarial_injection" in categories
    assert "seeded_literals" in categories

    rules_covered = set()
    for case in dataset:
        rules_covered.update(case.ground_truth_rules)

    for required_rule in ["REG-01", "REG-02", "REG-03", "REG-04", "REG-11"]:
        assert required_rule in rules_covered, f"Regla {required_rule} no cubierta en el dataset"


def test_extract_numbers_and_concordance():
    """Valida la extracción y concordancia numérica de cifras citadas en las afirmaciones."""
    text = "El operador Index Scan leyó 45,000 filas con un tiempo de CPU de 1,250.5 ms y ratio de 45000.0x."
    numbers = extract_numbers_from_text(text)
    assert 45000.0 in numbers
    assert 1250.5 in numbers

    ground_truth = [45000.0, 1250.5, 950.0]
    assert is_number_concordant(45000.0, ground_truth)
    assert is_number_concordant(1250.5, ground_truth)
    assert not is_number_concordant(99999.0, ground_truth)


def test_groundedness_supported_vs_unsupported():
    """Distingue afirmaciones sustentadas frente a alucinaciones de IDs o fragmentos no curados."""
    dataset = build_evaluation_dataset()
    case = dataset[0]  # REG-01

    # Respuesta válida sustentada
    valid_response = ExplainResponse(
        query_id=case.request.query_id,
        summary="Diagnóstico válido de conversión implícita.",
        claims=[
            ClaimOutput(
                claim_type=ClaimType.OBSERVACION,
                statement=f"Se detectó escaneo con {case.request.findings[0].finding_id}.",
                finding_ids=[case.ground_truth_findings[0]],
            ),
            ClaimOutput(
                claim_type=ClaimType.INTERPRETACION,
                statement="Degradación de sargabilidad por tipos desalineados.",
                finding_ids=[case.ground_truth_findings[0]],
                kb_fragment_id=case.expected_fragments[0],
            ),
        ],
        metadata=ExplainMetadata(
            llm_profile="mock",
            model="mock",
            prompt_version="v1.0",
            served_by="mock",
            data_policy="mock",
            execution_time_seconds=0.01,
        ),
    )

    res_valid = evaluate_explanation(valid_response, case)
    assert res_valid.supported_claims == 2
    assert res_valid.unsupported_claims == 0
    assert res_valid.supported_rate_pct == 100.0
    assert res_valid.is_trazada is True

    # Respuesta con alucinación de IDs
    hallucinated_response = ExplainResponse(
        query_id=case.request.query_id,
        summary="Diagnóstico con IDs inventados.",
        claims=[
            ClaimOutput(
                claim_type=ClaimType.OBSERVACION,
                statement="Afirmación con ID falso.",
                finding_ids=["finding-inventado-999"],
            ),
            ClaimOutput(
                claim_type=ClaimType.INTERPRETACION,
                statement="Interpretación con fragmento falso.",
                finding_ids=[case.ground_truth_findings[0]],
                kb_fragment_id="KB-INVENTADO-88",
            ),
        ],
        metadata=valid_response.metadata,
    )

    res_hallucinated = evaluate_explanation(hallucinated_response, case)
    assert res_hallucinated.supported_claims == 0
    assert res_hallucinated.unsupported_claims == 2
    assert res_hallucinated.supported_rate_pct == 0.0
    assert res_hallucinated.is_trazada is False


@pytest.mark.asyncio
async def test_control_condition_measures_hallucination_delta():
    """La condición de control sin contexto produce alucinaciones y demuestra la reducción del enfoque híbrido."""
    dataset = build_evaluation_dataset()
    settings = Settings(APP_ENV="dev", LLM_PROFILE="mock")
    provider = MockProvider(settings)

    control_evaluator = ControlConditionEvaluator(provider, settings)
    control_results = await control_evaluator.run_all(dataset[:5])
    control_metrics = calculate_aggregate_metrics(control_results)

    # En la condición de control, el 100% de afirmaciones carecen de sustento determinista
    assert control_metrics.overall_unsupported_rate_pct > 80.0
    assert control_metrics.trazada_cases_count == 0


@pytest.mark.asyncio
async def test_full_ai_evaluation_harness_execution():
    """Ejecuta el arnés de IA completo sobre las 30 consultas y verifica la generación de métricas (CA-HU20.2)."""
    settings = Settings(APP_ENV="dev", LLM_PROFILE="mock")
    provider = MockProvider(settings)
    harness = AiEvaluationHarness(settings=settings, provider=provider)

    report = await harness.run_evaluation(include_control=True)

    # 1. Verificación de tamaño de muestra
    assert report.rag_metrics.total_cases >= 30
    assert report.rag_metrics.total_claims > 60

    # 2. Verificación de métricas RF-EVA-02 en RAG híbrido
    assert report.rag_metrics.overall_supported_rate_pct >= 95.0
    assert report.rag_metrics.overall_unsupported_rate_pct <= 5.0
    assert report.rag_metrics.overall_numeric_concordance_pct >= 90.0

    # 3. Distribución por tipo
    dist = report.rag_metrics.type_distribution_pct
    assert "OBSERVACION" in dist
    assert "INTERPRETACION" in dist
    assert "RECOMENDACION" in dist
    assert sum(dist.values()) == pytest.approx(100.0, abs=0.5)

    # 4. Condición de control y delta de reducción
    assert report.control_comparison is not None
    assert report.control_comparison.hallucination_reduction_delta_pct > 80.0

    # 5. Salida de reportes
    summary_text = report.to_console_summary()
    assert "QUERY DOCTOR AI" in summary_text
    assert "REDUCCIÓN DE ALUCINACIONES" in summary_text

    md_text = report.to_markdown()
    assert "# Informe de Evaluación del Componente de IA" in md_text
    assert "Condición RAG Híbrida" in md_text


def test_extract_numbers_ignores_query_ids_sql_types_and_hyphenated_tokens():
    """Evita falsos negativos al citar query_id (q-std-06, q-adv-08), miles con punto o tipos SQL."""
    text = (
        "Diagnóstico para la consulta q-std-06 y q-adv-08 con varchar(20) en modelo "
        "gemma-2: escanea 1.500.000 filas, 25000 lecturas lógicas y ratio 14.5."
    )
    numbers = extract_numbers_from_text(text)
    assert numbers == [1500000.0, 25000.0, 14.5]


def test_empty_finding_ids_marks_claim_unsupported():
    """La validación defensiva de métricas rechaza una lista vacía de finding_ids (alineado con ExplanationValidator.cs)."""
    is_valid, reasons = _validate_finding_references([], {"find-q-std-01-reg01"}, 1, "OBSERVACION")
    assert is_valid is False
    assert len(reasons) == 1
    assert "finding_id" in reasons[0]


def test_dataset_reg02_tables_and_evidence_classes_match_contract():
    """Verifica que las tablas de REG-02 coincidan con su SQL y que evidence_class pertenezca al contrato."""
    allowed_evidence = {"ESTIMADA", "EJECUCION_AGREGADA", "PLAN_REAL", "NO_APLICA"}
    dataset = build_evaluation_dataset()

    for case in dataset:
        for finding in case.request.findings:
            assert finding.evidence_class in allowed_evidence
            if finding.rule_id == "REG-02":
                table_tokens = [t.strip().split(".")[-1] for t in str(finding.metrics.get("table", "")).split(",")]
                assert any(tok and tok.lower() in case.request.sql_text.lower() for tok in table_tokens), (
                    f"La tabla '{finding.metrics.get('table')}' de {case.case_id} no aparece en su SQL."
                )


@pytest.mark.asyncio
async def test_harness_checkpoint_resumes_in_batches(tmp_path):
    """El arnés guarda cada respuesta en checkpoint y reanuda por lotes sin repetir llamadas previas."""
    settings = Settings(APP_ENV="dev", LLM_PROFILE="mock")
    provider = MockProvider(settings)
    harness = AiEvaluationHarness(settings=settings, provider=provider)
    checkpoint = tmp_path / "ai_checkpoint.json"

    with pytest.raises(CheckpointPausedError) as paused:
        await harness.run_evaluation(
            include_control=True,
            limit=2,
            checkpoint_path=checkpoint,
            max_requests=3,
        )

    assert paused.value.rag_done == 2
    assert paused.value.control_done == 1

    report = await harness.run_evaluation(
        include_control=True,
        limit=2,
        checkpoint_path=checkpoint,
        max_requests=3,
    )
    assert report.rag_metrics.total_cases == 2
    assert report.control_comparison is not None
