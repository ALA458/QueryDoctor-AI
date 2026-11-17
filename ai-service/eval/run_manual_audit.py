"""CLI reproducible para preparar y puntuar la auditoría manual ciega (S3-03, RNF-FUN-03)."""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

root_dir = Path(__file__).resolve().parent.parent
if str(root_dir) not in sys.path:
    sys.path.insert(0, str(root_dir))

from eval.manual_audit import (  # noqa: E402
    prepare_blind_audit_bundle,
    render_audit_markdown,
    report_to_dict,
    score_manual_audit,
)


def _resolve_repo_path(repo_root: Path, raw_path: str, fallback_rel: str) -> Path:
    """Resuelve rutas garantizando contención dentro de la raíz del repositorio (CWE-22)."""
    real_root = os.path.realpath(str(repo_root))
    raw_joined = raw_path if os.path.isabs(raw_path) else os.path.join(real_root, raw_path)
    real_candidate = os.path.realpath(raw_joined)
    if real_candidate != real_root and not real_candidate.startswith(real_root + os.sep):
        return Path(os.path.realpath(os.path.join(real_root, fallback_rel)))
    return Path(real_candidate)


def _write_repo_text_file(repo_root: Path, target_path: Path, content: str) -> None:
    real_root = os.path.realpath(str(repo_root))
    real_target = os.path.realpath(str(target_path))
    if not real_target.startswith(real_root + os.sep):
        raise ValueError(f"Intento de escritura fuera de la raíz del repositorio: {target_path}")
    Path(real_target).parent.mkdir(parents=True, exist_ok=True)
    with open(real_target, "w", encoding="utf-8") as handle:
        handle.write(content)


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Herramienta de auditoría manual por dos revisores independientes (S3-03 / RNF-FUN-03)"
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    prep = subparsers.add_parser("prepare", help="Genera cuaderno ciego, rúbrica y dos planillas CSV vacías")
    prep.add_argument(
        "--report",
        type=str,
        default="lab/results/ai_evaluation_report.json",
        help="Ruta al reporte JSON generado por run_ai_harness.py",
    )
    prep.add_argument(
        "--out-dir",
        type=str,
        default="lab/results/auditoria",
        help="Directorio de salida para el paquete de revisión ciega",
    )
    prep.add_argument(
        "--seed",
        type=int,
        default=20261117,
        help="Semilla reproducible para barajar el orden de presentación",
    )

    score = subparsers.add_parser("score", help="Calcula Kappa de Cohen y tasa de falsos pasantes")
    score.add_argument(
        "--manifest",
        type=str,
        default="lab/results/auditoria/audit_manifest.json",
        help="Ruta al manifiesto generado por prepare",
    )
    score.add_argument(
        "--r1",
        type=str,
        default="lab/results/auditoria/planilla_revisor_1.csv",
        help="Planilla completada por el Revisor 1",
    )
    score.add_argument(
        "--r2",
        type=str,
        default="lab/results/auditoria/planilla_revisor_2.csv",
        help="Planilla completada por el Revisor 2",
    )
    score.add_argument(
        "--adjudication",
        type=str,
        default=None,
        help="CSV opcional con consenso documentado sobre discrepancias (audit_id,final_verdict,resolution_note)",
    )
    score.add_argument(
        "--out-md",
        type=str,
        default="lab/results/S3-03-auditoria-manual.md",
        help="Ruta de salida para el informe en Markdown",
    )
    score.add_argument(
        "--out-json",
        type=str,
        default="lab/results/raw/s3-03-audit-metrics.json",
        help="Ruta de salida para métricas crudas en JSON",
    )
    return parser


def main() -> None:
    repo_root = root_dir.parent.resolve()
    args = _build_parser().parse_args()

    if args.command == "prepare":
        report_path = _resolve_repo_path(repo_root, args.report, "lab/results/ai_evaluation_report.json")
        out_dir = _resolve_repo_path(repo_root, args.out_dir, "lab/results/auditoria")
        manifest = prepare_blind_audit_bundle(
            report_path,
            out_dir,
            seed=args.seed,
            allowed_root=repo_root,
        )
        print(
            f"[OK] Paquete ciego generado en {out_dir}: "
            f"{manifest['total_cases']} casos, {manifest['total_claims']} afirmaciones (seed={manifest['seed']})."
        )
        return

    manifest_path = _resolve_repo_path(repo_root, args.manifest, "lab/results/auditoria/audit_manifest.json")
    r1_path = _resolve_repo_path(repo_root, args.r1, "lab/results/auditoria/planilla_revisor_1.csv")
    r2_path = _resolve_repo_path(repo_root, args.r2, "lab/results/auditoria/planilla_revisor_2.csv")
    adj_path = (
        _resolve_repo_path(repo_root, args.adjudication, "lab/results/auditoria/desacuerdos_resueltos.csv")
        if args.adjudication
        else None
    )
    out_md = _resolve_repo_path(repo_root, args.out_md, "lab/results/S3-03-auditoria-manual.md")
    out_json = _resolve_repo_path(repo_root, args.out_json, "lab/results/raw/s3-03-audit-metrics.json")

    audit_report = score_manual_audit(manifest_path, r1_path, r2_path, adjudication_csv=adj_path)

    _write_repo_text_file(repo_root, out_md, render_audit_markdown(audit_report))
    _write_repo_text_file(repo_root, out_json, json.dumps(report_to_dict(audit_report), indent=2, ensure_ascii=False))

    strict_pct = audit_report.strict_false_passer_ci.proportion * 100.0
    print(
        f"[OK] S3-03 puntuado: kappa(claims)={audit_report.claim_kappa.kappa:.4f}, "
        f"kappa(cases)={audit_report.case_kappa.kappa:.4f}, "
        f"falsos_pasantes(estricto)={audit_report.strict_false_passer_count}/{audit_report.first_attempt_pass_count} "
        f"({strict_pct:.2f}%)"
    )
    print(f"     Informe Markdown: {out_md}")
    print(f"     Métricas JSON:    {out_json}")


if __name__ == "__main__":
    main()
