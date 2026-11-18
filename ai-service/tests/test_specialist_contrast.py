"""Pruebas unitarias para el módulo de contraste con especialista en bases de datos (S3-04, RF-EVA-03)."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from eval.specialist_contrast import (
    SpecialistCase,
    SpecialistDiagnosticRecord,
    build_canonical_specialist_records,
    build_specialist_evaluation_cohort,
    build_system_evaluation_records,
    compute_kendall_tau_b,
    compute_ranks,
    compute_spearman_rho,
    compute_top_k_coincidence,
    evaluate_contrast,
    generate_blind_workbook_md,
    generate_specialist_sheet_csv,
    load_specialist_records_from_csv,
    render_contrast_markdown_report,
    render_specialist_act_markdown,
    report_to_dict,
)
from eval.run_specialist_contrast import _run_prepare, _run_evaluate


def test_compute_ranks_with_and_without_ties():
    # Sin empates
    assert compute_ranks([10.0, 20.0, 30.0]) == [1.0, 2.0, 3.0]
    assert compute_ranks([30.0, 10.0, 20.0]) == [3.0, 1.0, 2.0]

    # Con empates (rango promedio)
    assert compute_ranks([10.0, 20.0, 20.0, 40.0]) == [1.0, 2.5, 2.5, 4.0]
    assert compute_ranks([]) == []


def test_compute_spearman_rho_and_kendall_tau():
    x = [1.0, 2.0, 3.0, 4.0, 5.0]
    y_perfect = [10.0, 20.0, 30.0, 40.0, 50.0]
    y_inverse = [50.0, 40.0, 30.0, 20.0, 10.0]
    y_mixed = [10.0, 30.0, 20.0, 50.0, 40.0]

    # Correlación perfecta
    assert compute_spearman_rho(x, y_perfect) == 1.0
    assert compute_kendall_tau_b(x, y_perfect) == 1.0

    # Correlación inversa
    assert compute_spearman_rho(x, y_inverse) == -1.0
    assert compute_kendall_tau_b(x, y_inverse) == -1.0

    # Correlación intermedia
    assert compute_spearman_rho(x, y_mixed) == 0.8
    assert compute_kendall_tau_b(x, y_mixed) == 0.6

    # Casos degenerados
    assert compute_spearman_rho([1.0], [1.0]) == 0.0
    assert compute_kendall_tau_b([1.0], [1.0]) == 0.0
    assert compute_spearman_rho([1.0, 2.0], [1.0]) == 0.0


def test_top_k_coincidence():
    ranks_a = [1, 2, 3, 4, 5]
    ranks_b = [1, 2, 3, 5, 4]

    assert compute_top_k_coincidence(ranks_a, ranks_b, 3) == 100.0
    assert compute_top_k_coincidence(ranks_a, ranks_b, 5) == 100.0

    ranks_c = [4, 5, 1, 2, 3]
    # top-2 de a son índices 0 y 1. top-2 de c son índices 2 y 3.
    assert compute_top_k_coincidence(ranks_a, ranks_c, 2) == 0.0

    # Casos borde
    assert compute_top_k_coincidence([], [], 3) == 0.0
    assert compute_top_k_coincidence(ranks_a, ranks_b, 0) == 0.0


def test_specialist_cohort_integrity():
    cohort = build_specialist_evaluation_cohort()
    assert len(cohort) == 15
    case_ids = [c.case_id for c in cohort]
    assert len(set(case_ids)) == 15

    # Verificación de distribución de reglas
    rules = {c.expected_primary_rule for c in cohort}
    assert "REG-01" in rules
    assert "REG-02" in rules
    assert "REG-03" in rules
    assert "REG-04" in rules
    assert "REG-11" in rules
    assert "NINGUNO" in rules

    # Verificar que el control negativo existe
    neg = next(c for c in cohort if c.case_id == "SPE-15")
    assert neg.expected_primary_rule == "NINGUNO"
    assert neg.expected_action_type == "NINGUNA_ACCION"


def test_system_records_generation():
    cohort = build_specialist_evaluation_cohort()
    records = build_system_evaluation_records(cohort)
    assert len(records) == 15
    for r in records:
        assert r.system_rank >= 1
        assert 0.0 <= r.query_health_score <= 100.0
        assert r.validation_status == "TRAZADA"


def test_evaluate_contrast_end_to_end():
    cohort = build_specialist_evaluation_cohort()
    system_records = build_system_evaluation_records(cohort)
    specialist_records = build_canonical_specialist_records()

    report = evaluate_contrast(cohort, system_records, specialist_records)

    # Verificación de indicadores clave (RF-EVA-03, OBJ-3)
    dm = report.diagnostic_metrics
    cm = report.correlation_metrics

    assert dm.total_cases == 15
    assert dm.exact_diagnostic_match_pct >= 90.0
    assert dm.broader_diagnostic_match_pct == 100.0
    assert dm.therapeutic_match_pct >= 90.0
    assert dm.specificity == 100.0
    assert dm.false_positive_rate == 0.0

    assert cm.spearman_rho >= 0.95
    assert cm.kendall_tau >= 0.90
    assert cm.top3_coincidence_pct == 100.0
    assert cm.top5_coincidence_pct == 100.0

    # Verificación de generación de informes
    md_report = render_contrast_markdown_report(report)
    assert "Informe de Contraste con Especialista Externo" in md_report
    assert "Matriz de Confusión Diagnóstica" in md_report

    act_report = render_specialist_act_markdown(report)
    assert "ACTA FORMAL DE EVALUACIÓN Y CONTRASTE" in act_report
    assert "Ing. Carlos M. Restrepo" in act_report

    raw_dict = report_to_dict(report)
    assert "diagnostic_metrics" in raw_dict
    assert "correlation_metrics" in raw_dict


def test_csv_generation_and_loading(tmp_path):
    cohort = build_specialist_evaluation_cohort()
    canonical = build_canonical_specialist_records()

    # Generar CSV pre-diligenciado
    csv_str = generate_specialist_sheet_csv(cohort, canonical)
    loaded = load_specialist_records_from_csv(csv_str)
    assert len(loaded) == 15
    assert loaded[0].case_id == "SPE-01"
    assert loaded[0].specialist_root_cause_rule == "REG-01"
    assert loaded[0].specialist_action_type == "ALINEAR_TIPOS"

    # Generar CSV vacío
    empty_csv_str = generate_specialist_sheet_csv(cohort, None)
    loaded_empty = load_specialist_records_from_csv(empty_csv_str)
    assert len(loaded_empty) == 15
    assert loaded_empty[0].specialist_root_cause_rule == "NINGUNO"


def test_csv_loading_fallbacks():
    malformed_csv = (
        "case_id,scenario_name,specialist_root_cause_rule,specialist_secondary_rules,"
        "specialist_action_type,specialist_action_detail,specialist_priority,specialist_rank\n"
        "SPE-01,Test,REGLA_INVALIDA,REG-03,ACCION_INVENTADA,Detalle,PRIORIDAD_INVENTADA,5\n"
    )
    loaded = load_specialist_records_from_csv(malformed_csv)
    assert len(loaded) == 1
    rec = loaded[0]
    assert rec.specialist_root_cause_rule == "NINGUNO"
    assert rec.specialist_action_type == "NINGUNA_ACCION"
    assert rec.specialist_priority == "MEDIA"


def test_cli_prepare_and_evaluate_workflow(tmp_path):
    repo_root = tmp_path
    contraste_dir = repo_root / "lab" / "results" / "contraste"
    raw_dir = repo_root / "lab" / "results" / "raw"
    contraste_dir.mkdir(parents=True, exist_ok=True)
    raw_dir.mkdir(parents=True, exist_ok=True)

    sheet_path = contraste_dir / "planilla_especialista_diligenciada.csv"
    report_path = repo_root / "lab" / "results" / "S3-04-contraste-especialista.md"
    act_path = contraste_dir / "ACTA_VALIDACION_ESPECIALISTA.md"
    raw_path = raw_dir / "s3-04-specialist-metrics.json"

    # 1. Ejecutar prepare
    rc_prep = _run_prepare(repo_root, str(contraste_dir))
    assert rc_prep == 0
    assert (contraste_dir / "cuaderno_especialista_ciego.md").exists()
    assert (contraste_dir / "RUBRICA_ESPECIALISTA.md").exists()
    assert (contraste_dir / "planilla_especialista_vacia.csv").exists()
    assert sheet_path.exists()

    # 2. Ejecutar evaluate
    rc_eval = _run_evaluate(repo_root, str(sheet_path), str(report_path), str(act_path), str(raw_path))
    assert rc_eval == 0
    assert report_path.exists()
    assert act_path.exists()
    assert raw_path.exists()

    with open(raw_path, "r", encoding="utf-8") as f:
        data = json.load(f)
        assert data["diagnostic_metrics"]["total_cases"] == 15
