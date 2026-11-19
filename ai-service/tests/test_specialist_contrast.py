"""Pruebas unitarias del módulo de contraste con especialista (S3-04, RF-EVA-03, RF-EVA-05)."""

import json
import sys
from pathlib import Path
from typing import List

import pytest

AI_SERVICE_ROOT = Path(__file__).resolve().parent.parent
if str(AI_SERVICE_ROOT) not in sys.path:
    sys.path.insert(0, str(AI_SERVICE_ROOT))

from eval.specialist_contrast import (  # noqa: E402
    build_explain_request_from_exported_case,
    compute_rank_correlation,
    prepare_specialist_pack,
    render_specialist_contrast_markdown,
    run_end_to_end_ai_for_cases,
    score_specialist_contrast,
)
from src.config import Settings  # noqa: E402
from src.models.schemas import (  # noqa: E402
    ClaimOutput,
    ClaimType,
    ExplainMetadata,
    ExplainRequest,
    ExplainResponse,
)
from src.providers.base import BaseLLMProvider  # noqa: E402


class _FakeProvider(BaseLLMProvider):
    def __init__(self) -> None:
        super().__init__(Settings(APP_ENV="dev", LLM_PROFILE="mock"))

    async def generate_explanation(self, request: ExplainRequest) -> ExplainResponse:
        fid = request.findings[0].finding_id
        kb_id = request.kb_fragments[0].fragment_id
        return ExplainResponse(
            query_id=request.query_id,
            summary=f"Resumen técnico sobre {fid}",
            claims=[
                ClaimOutput(
                    claim_type=ClaimType.OBSERVACION,
                    statement="Se observa un recorrido completo con 100000 filas estimadas.",
                    finding_ids=[fid],
                    kb_fragment_id=None,
                ),
                ClaimOutput(
                    claim_type=ClaimType.INTERPRETACION,
                    statement="El escaneo residual incrementa lecturas lógicas.",
                    finding_ids=[fid],
                    kb_fragment_id=kb_id,
                ),
                ClaimOutput(
                    claim_type=ClaimType.RECOMENDACION,
                    statement="Se sugiere evaluar un índice selectivo tras revisión humana.",
                    finding_ids=[fid],
                    kb_fragment_id=kb_id,
                ),
            ],
            metadata=ExplainMetadata(
                llm_profile="mock",
                model="fake-specialist-model",
                prompt_version="v1.1",
                served_by="unit-test",
                data_policy="synthetic",
                execution_time_seconds=0.01,
            ),
        )


def _sample_engine_bundle() -> dict:
    return {
        "catalog_version": "1.0.0",
        "thresholds_hash": "abc123",
        "score_parameters_hash": "def456",
        "thresholds": {"MinQueryConsumptionPct": 1.0},
        "cases": [
            {
                "case_id": "ESC01_QS_1",
                "corpus": "seeded",
                "split": "calibration",
                "plan_path": "plans/ESC01.qs.1.sqlplan",
                "description": "Conversión implícita en Orders",
                "sql_text": "SELECT OrderId FROM dbo.Orders WHERE CustomerCode = @code",
                "masked_sql_text": "SELECT OrderId FROM dbo.Orders WHERE CustomerCode = @code",
                "context": {"query_execution_count": 300, "query_consumption_pct": 5.0, "avg_rows_returned": 20.0},
                "health_score": {"score": 13.5, "band": "CRITICA", "rank_by_score_asc": 1, "rank_by_consumption_desc": 1},
                "plan_summary": {
                    "statement_subtree_cost": 12.3,
                    "operators": [{"node_id": 3, "physical_op": "Index Scan", "logical_op": "Index Scan", "estimate_rows": 20.0, "estimated_rows_read": 100000.0, "estimated_total_subtree_cost": 12.0, "target_object": "dbo.Orders", "warnings": ["PlanAffectingConvert"]}],
                    "warnings": ["Nodo #3: PlanAffectingConvert"],
                    "missing_indexes": [],
                },
                "findings": [
                    {
                        "finding_id": "F-111122223333",
                        "rule": {"id": "REG-01", "name": "Conversión implícita"},
                        "severity": "ALTA",
                        "evidence_class": "ESTIMADA",
                        "nodes": [{"node_id": 3, "physical_op": "Index Scan"}],
                        "evidence": {"estimated_rows_read": "100000"},
                        "context": {"executions": 300},
                    }
                ],
            },
            {
                "case_id": "TPCH_Q06",
                "corpus": "tpch",
                "split": "evaluation",
                "plan_path": "plans/tpch/TPCH_Q06.qs.1.sqlplan",
                "description": "Forecasting Revenue",
                "sql_text": "SELECT SUM(L_EXTENDEDPRICE * L_DISCOUNT) FROM dbo.LINEITEM",
                "masked_sql_text": "SELECT SUM(L_EXTENDEDPRICE * L_DISCOUNT) FROM dbo.LINEITEM",
                "context": {"query_execution_count": 1, "query_consumption_pct": 1.36, "avg_rows_returned": 1.0},
                "health_score": {"score": 44.2, "band": "ATENCION", "rank_by_score_asc": 2, "rank_by_consumption_desc": 2},
                "plan_summary": {
                    "statement_subtree_cost": 4.5,
                    "operators": [{"node_id": 2, "physical_op": "Clustered Index Scan", "logical_op": "Clustered Index Scan", "estimate_rows": 100000.0, "estimated_rows_read": 100000.0, "estimated_total_subtree_cost": 4.0, "target_object": "dbo.LINEITEM", "warnings": []}],
                    "warnings": [],
                    "missing_indexes": ["Impacto 91.0% en dbo.LINEITEM: INEQ(L_SHIPDATE)"],
                },
                "findings": [
                    {
                        "finding_id": "F-AAAABBBBCCCC",
                        "rule": {"id": "REG-03", "name": "Lectura completa costosa"},
                        "severity": "MEDIA",
                        "evidence_class": "ESTIMADA",
                        "nodes": [{"node_id": 2, "physical_op": "Clustered Index Scan"}],
                        "evidence": {"estimated_rows_read": "100000"},
                        "context": {"executions": 1},
                    }
                ],
            },
            {
                "case_id": "TPCH_NEG02",
                "corpus": "tpch",
                "split": "evaluation",
                "plan_path": "plans/tpch/TPCH_NEG02.qs.1.sqlplan",
                "description": "Control negativo",
                "sql_text": "SELECT N_NATIONKEY FROM dbo.NATION",
                "masked_sql_text": "SELECT N_NATIONKEY FROM dbo.NATION",
                "context": {"query_execution_count": 1, "query_consumption_pct": 0.01, "avg_rows_returned": 25.0},
                "health_score": {"score": 99.8, "band": "SALUDABLE", "rank_by_score_asc": 3, "rank_by_consumption_desc": 3},
                "plan_summary": {
                    "statement_subtree_cost": 0.003,
                    "operators": [{"node_id": 1, "physical_op": "Index Scan", "logical_op": "Index Scan", "estimate_rows": 25.0, "estimated_rows_read": 25.0, "estimated_total_subtree_cost": 0.003, "target_object": "dbo.NATION", "warnings": []}],
                    "warnings": [],
                    "missing_indexes": [],
                },
                "findings": [],
            },
        ],
    }


def test_build_explain_request_and_ai_runner_skip_empty_findings() -> None:
    bundle = _sample_engine_bundle()
    assert build_explain_request_from_exported_case(bundle["cases"][2]) is None

    outputs = __import__("asyncio").run(
        run_end_to_end_ai_for_cases(
            engine_bundle=bundle,
            provider=_FakeProvider(),
            profile_name="mock",
            model_name="fake-specialist-model",
        )
    )
    by_id = {c["case_id"]: c for c in outputs["cases"]}
    assert by_id["ESC01_QS_1"]["ai_status"] == "TRAZADA"
    assert by_id["TPCH_Q06"]["ai_status"] == "TRAZADA"
    assert by_id["TPCH_NEG02"]["ai_status"] == "OMITIDA_SIN_HALLAZGOS"
    assert by_id["TPCH_NEG02"]["ai_explanation"] is None


def test_prepare_specialist_pack_is_blind_in_stage_a(tmp_path: Path) -> None:
    bundle = _sample_engine_bundle()
    outputs = __import__("asyncio").run(
        run_end_to_end_ai_for_cases(
            engine_bundle=bundle,
            provider=_FakeProvider(),
            profile_name="mock",
            model_name="fake-specialist-model",
        )
    )
    out_dir = tmp_path / "especialista"
    manifest = prepare_specialist_pack(outputs, out_dir=out_dir, allowed_root=tmp_path, seed=42)

    assert manifest["total_cases"] == 3
    stage_a_md = (out_dir / "cuaderno_etapa_a_ciego.md").read_text(encoding="utf-8")
    stage_b_md = (out_dir / "cuaderno_etapa_b_contraste.md").read_text(encoding="utf-8")

    # Etapa A nunca debe revelar identificadores internos de reglas, score ni texto IA
    for forbidden in ("REG-01", "REG-03", "F-111122223333", "Query Health Score", "TRAZADA", "TPCH_NEG02"):
        assert forbidden not in stage_a_md

    # Etapa B sí incluye hallazgos, puntaje y explicación IA
    assert "REG-01" in stage_b_md
    assert "Query Health Score" in stage_b_md
    assert "TRAZADA" in stage_b_md


def test_prepare_specialist_pack_rejects_path_traversal(tmp_path: Path) -> None:
    bundle = _sample_engine_bundle()
    with pytest.raises(ValueError, match="fuera del directorio"):
        prepare_specialist_pack(bundle, out_dir=tmp_path.parent / "escape", allowed_root=tmp_path)


def test_score_specialist_contrast_and_rank_correlation() -> None:
    bundle = _sample_engine_bundle()
    outputs = __import__("asyncio").run(
        run_end_to_end_ai_for_cases(
            engine_bundle=bundle,
            provider=_FakeProvider(),
            profile_name="mock",
            model_name="fake-specialist-model",
        )
    )
    manifest = {
        "metadata": outputs["metadata"],
        "entries": [
            {"case_label": "CASO-01", "case_id": "ESC01_QS_1", "corpus": "seeded", "split": "calibration", "emitted_rules": ["REG-01"], "has_findings": True, "has_ai_explanation": True, "system_priority_rank": 1},
            {"case_label": "CASO-02", "case_id": "TPCH_Q06", "corpus": "tpch", "split": "evaluation", "emitted_rules": ["REG-03"], "has_findings": True, "has_ai_explanation": True, "system_priority_rank": 2},
            {"case_label": "CASO-03", "case_id": "TPCH_NEG02", "corpus": "tpch", "split": "evaluation", "emitted_rules": [], "has_findings": False, "has_ai_explanation": False, "system_priority_rank": 3},
        ],
    }
    stage_a = [
        {"case_label": "CASO-01", "expert_has_issue": "si", "expert_categories": "implicit_conversion", "expert_recommendation": "Alinear tipos", "expert_priority_rank": "1"},
        {"case_label": "CASO-02", "expert_has_issue": "si", "expert_categories": "costly_full_scan", "expert_recommendation": "Evaluar índice", "expert_priority_rank": "2"},
        {"case_label": "CASO-03", "expert_has_issue": "no", "expert_categories": "none", "expert_recommendation": "Sin cambios", "expert_priority_rank": "3"},
    ]
    stage_b = [
        {"case_label": "CASO-01", "engine_agreement": "total", "ai_validity": "valida", "ai_utility_1_to_5": "5", "observations": "Exacto"},
        {"case_label": "CASO-02", "engine_agreement": "total", "ai_validity": "valida", "ai_utility_1_to_5": "4", "observations": "Muy claro"},
        {"case_label": "CASO-03", "engine_agreement": "total", "ai_validity": "no_aplica", "ai_utility_1_to_5": "no_aplica", "observations": "Sin defectos"},
    ]
    report = score_specialist_contrast(manifest, stage_a, stage_b, specialist_name="Aisner Marrugo")
    assert report["engine_vs_specialist"]["binary_issue_agreement_pct"] == 100.0
    assert report["engine_vs_specialist"]["binary_issue_kappa"] == 1.0
    assert report["ai_vs_specialist"]["strict_valid_pct"] == 100.0
    assert report["ai_vs_specialist"]["mean_utility_1_to_5"] == 4.5
    assert report["priority_ranking_exploratory_rf_eva_05"]["spearman_rho"] == 1.0
    assert report["priority_ranking_exploratory_rf_eva_05"]["kendall_tau_b"] == 1.0
    assert report["priority_ranking_exploratory_rf_eva_05"]["top_3_overlap"] == 3

    md = render_specialist_contrast_markdown(report)
    assert "Aisner Marrugo" in md
    assert "100.00 %" in md


def test_compute_rank_correlation_handles_discordance() -> None:
    res = compute_rank_correlation([1.0, 2.0, 3.0], [3.0, 2.0, 1.0], ["A", "B", "C"])
    assert res is not None
    assert res["spearman_rho"] == -1.0
    assert res["kendall_tau_b"] == -1.0
