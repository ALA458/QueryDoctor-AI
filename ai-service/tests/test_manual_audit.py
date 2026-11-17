"""Pruebas unitarias e integración para la auditoría manual ciega (S3-03 / RNF-FUN-03)."""

import csv
from pathlib import Path

import pytest

from eval.manual_audit import (
    aggregate_case_verdict,
    compute_cohen_kappa,
    prepare_blind_audit_bundle,
    render_audit_markdown,
    score_manual_audit,
    wilson_confidence_interval,
)


def test_cohen_kappa_matches_hand_calculated_example():
    """Valida kappa no ponderado con cálculo manual clásico sobre 20 elementos."""
    # Matriz: R1\R2: v-v=8, v-p=1, v-i=1 (tot 10); p-v=2, p-p=3, p-i=0 (tot 5); i-v=0, i-p=1, i-i=4 (tot 5)
    # Po = (8 + 3 + 4) / 20 = 15/20 = 0.75
    # Cols: v=10, p=5, i=5 -> Pe = (10*10 + 5*5 + 5*5) / 400 = 150/400 = 0.375
    # Kappa = (0.75 - 0.375) / (1 - 0.375) = 0.375 / 0.625 = 0.60
    r1 = ["verdadera"] * 8 + ["verdadera", "verdadera"] + ["parcialmente_verdadera"] * 2 + ["parcialmente_verdadera"] * 3 + ["incorrecta"] + ["incorrecta"] * 4
    r2 = ["verdadera"] * 8 + ["parcialmente_verdadera", "incorrecta"] + ["verdadera"] * 2 + ["parcialmente_verdadera"] * 3 + ["parcialmente_verdadera"] + ["incorrecta"] * 4

    res = compute_cohen_kappa(r1, r2)
    assert res.total_items == 20
    assert res.observed_agreement == pytest.approx(0.75, abs=1e-6)
    assert res.expected_agreement == pytest.approx(0.375, abs=1e-6)
    assert res.kappa == pytest.approx(0.60, abs=1e-6)
    assert res.degenerate_single_class is False
    assert res.linear_weighted_kappa > res.kappa  # Desacuerdos parciales ponderan más que saltos de 2 niveles


def test_cohen_kappa_handles_single_class_perfect_agreement():
    """Si ambos auditores marcan 100 % en una sola clase, Pe=1.0 se marca como caso degenerado sin dividir por 0."""
    r1 = ["verdadera"] * 30
    r2 = ["verdadera"] * 30
    res = compute_cohen_kappa(r1, r2)
    assert res.observed_agreement == pytest.approx(1.0)
    assert res.expected_agreement == pytest.approx(1.0)
    assert res.kappa == pytest.approx(1.0)
    assert res.degenerate_single_class is True


def test_wilson_confidence_interval_for_zero_and_small_counts():
    """Verifica intervalo de Wilson al 95 % para N=30 con 0 y 2 falsos pasantes."""
    ci_zero = wilson_confidence_interval(0, 30)
    assert ci_zero.proportion == pytest.approx(0.0)
    assert ci_zero.lower == pytest.approx(0.0)
    assert 0.11 <= ci_zero.upper <= 0.12  # ~11.35 % para 0/30

    ci_two = wilson_confidence_interval(2, 30)
    assert ci_two.proportion == pytest.approx(2 / 30, abs=1e-5)
    assert 0.01 <= ci_two.lower < ci_two.proportion < ci_two.upper <= 0.25


def test_aggregate_case_verdict_applies_worst_claim_or_summary_error():
    """Basta una afirmación incorrecta o error grave de summary para clasificar la explicación como incorrecta."""
    assert aggregate_case_verdict(["verdadera", "verdadera"]) == "verdadera"
    assert aggregate_case_verdict(["verdadera", "parcialmente_verdadera"]) == "parcialmente_verdadera"
    assert aggregate_case_verdict(["verdadera", "parcialmente_verdadera", "incorrecta"]) == "incorrecta"
    assert aggregate_case_verdict(["verdadera", "verdadera"], summary_error=True) == "incorrecta"


def _fill_csv(path: Path, overrides: dict[tuple[str, int], str]) -> None:
    with path.open("r", encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))
        fieldnames = list(rows[0].keys())
    for row in rows:
        key = (row["audit_id"], int(row["claim_index"]))
        row["verdict"] = overrides.get(key, "verdadera")
    with path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def test_prepare_and_score_end_to_end_pipeline(tmp_path: Path):
    """El flujo prepare -> planillas independientes -> score calcula kappa y falsos pasantes sin sesgo."""
    repo_root = Path(__file__).resolve().parents[2]
    report_json = repo_root / "lab" / "results" / "ai_evaluation_report.json"
    audit_dir = tmp_path / "auditoria"

    manifest = prepare_blind_audit_bundle(report_json, audit_dir, seed=12345)
    assert manifest["total_cases"] == 30
    assert manifest["total_claims"] == 91

    notebook_text = (audit_dir / "cuaderno_evidencia_ciega.md").read_text(encoding="utf-8")
    assert "AUD-01" in notebook_text
    assert "TRAZADA" not in notebook_text
    assert "NO_VERIFICADA" not in notebook_text

    r1_csv = audit_dir / "planilla_revisor_1.csv"
    r2_csv = audit_dir / "planilla_revisor_2.csv"

    # R1 y R2 coinciden en que AUD-01 claim 1 es incorrecta (falso pasante estricto),
    # R1 marca AUD-02 claim 2 como incorrecta mientras R2 la marca parcialmente_verdadera.
    _fill_csv(r1_csv, {("AUD-01", 1): "incorrecta", ("AUD-02", 2): "incorrecta"})
    _fill_csv(r2_csv, {("AUD-01", 1): "incorrecta", ("AUD-02", 2): "parcialmente_verdadera"})

    scored = score_manual_audit(audit_dir / "audit_manifest.json", r1_csv, r2_csv)
    assert scored.total_cases == 30
    assert scored.first_attempt_pass_count == 30
    assert scored.strict_false_passer_count == 1
    assert scored.conservative_false_passer_count == 2
    assert len(scored.claim_disagreements) == 1
    assert len(scored.case_disagreements) == 1

    md = render_audit_markdown(scored)
    assert "Kappa de Cohen" in md
    assert "IC 95 % Wilson" in md

    # Verifica soporte de adjudicación opcional sobre el desacuerdo
    adj_csv = audit_dir / "adjudication.csv"
    adj_csv.write_text(
        "audit_id,final_verdict,resolution_note\nAUD-02,parcialmente_verdadera,Consenso tras revisión conjunta\n",
        encoding="utf-8",
    )
    scored_adj = score_manual_audit(
        audit_dir / "audit_manifest.json", r1_csv, r2_csv, adjudication_csv=adj_csv
    )
    assert scored_adj.adjudicated_false_passer_count == 1
    assert scored_adj.case_kappa.kappa == scored.case_kappa.kappa  # Kappa pre-consenso no cambia


def test_score_rejects_incomplete_reviewer_sheet(tmp_path: Path):
    """Falla explícitamente si falta un veredicto o se usa una etiqueta fuera de catálogo."""
    repo_root = Path(__file__).resolve().parents[2]
    report_json = repo_root / "lab" / "results" / "ai_evaluation_report.json"
    audit_dir = tmp_path / "auditoria"
    prepare_blind_audit_bundle(report_json, audit_dir, seed=99)

    r1_csv = audit_dir / "planilla_revisor_1.csv"
    r2_csv = audit_dir / "planilla_revisor_2.csv"
    _fill_csv(r1_csv, {})
    # r2_csv queda vacío sin veredictos

    with pytest.raises(ValueError, match="Veredicto inválido o vacío"):
        score_manual_audit(audit_dir / "audit_manifest.json", r1_csv, r2_csv)
