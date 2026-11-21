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
    comparative_report_to_dict,
    prepare_blind_audit_bundle,
    prepare_comparative_audit_bundle,
    render_audit_markdown,
    render_comparative_audit_markdown,
    report_to_dict,
    score_comparative_audit,
    score_manual_audit,
)

_DEFAULT_EVAL_REPORT = "lab/results/ai_evaluation_report.json"
_DEFAULT_AUDIT_DIR = "lab/results/auditoria"
_DEFAULT_MANIFEST = "lab/results/auditoria/audit_manifest.json"
_DEFAULT_R1_CSV = "lab/results/auditoria/planilla_revisor_1.csv"
_DEFAULT_R2_CSV = "lab/results/auditoria/planilla_revisor_2.csv"
_DEFAULT_ADJ_CSV = "lab/results/auditoria/desacuerdos_resueltos.csv"
_DEFAULT_OUT_MD = "lab/results/S3-03-auditoria-manual.md"
_DEFAULT_OUT_JSON = "lab/results/raw/s3-03-audit-metrics.json"


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


def _add_score_arguments(subparser: argparse.ArgumentParser) -> None:
    subparser.add_argument(
        "--manifest",
        type=str,
        default=_DEFAULT_MANIFEST,
        help="Ruta al manifiesto generado por prepare",
    )
    subparser.add_argument(
        "--r1",
        type=str,
        default=_DEFAULT_R1_CSV,
        help="Planilla completada por el Revisor 1",
    )
    subparser.add_argument(
        "--r2",
        type=str,
        default=_DEFAULT_R2_CSV,
        help="Planilla completada por el Revisor 2",
    )
    subparser.add_argument(
        "--adjudication",
        type=str,
        default=None,
        help="CSV opcional con consenso documentado sobre discrepancias",
    )
    subparser.add_argument(
        "--out-md",
        type=str,
        default=_DEFAULT_OUT_MD,
        help="Ruta de salida para el informe en Markdown",
    )
    subparser.add_argument(
        "--out-json",
        type=str,
        default=_DEFAULT_OUT_JSON,
        help="Ruta de salida para métricas crudas en JSON",
    )


def _resolve_score_paths(
    repo_root: Path, args: argparse.Namespace
) -> tuple[Path, Path, Path, Path | None, Path, Path]:
    manifest_path = _resolve_repo_path(repo_root, args.manifest, _DEFAULT_MANIFEST)
    r1_path = _resolve_repo_path(repo_root, args.r1, _DEFAULT_R1_CSV)
    r2_path = _resolve_repo_path(repo_root, args.r2, _DEFAULT_R2_CSV)
    adj_path = _resolve_repo_path(repo_root, args.adjudication, _DEFAULT_ADJ_CSV) if args.adjudication else None
    out_md = _resolve_repo_path(repo_root, args.out_md, _DEFAULT_OUT_MD)
    out_json = _resolve_repo_path(repo_root, args.out_json, _DEFAULT_OUT_JSON)
    return manifest_path, r1_path, r2_path, adj_path, out_md, out_json


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Herramienta de auditoría manual por dos revisores independientes (S3-03 / RNF-FUN-03)"
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    prep = subparsers.add_parser("prepare", help="Genera cuaderno ciego, rúbrica y dos planillas CSV vacías")
    prep.add_argument("--report", type=str, default=_DEFAULT_EVAL_REPORT, help="Ruta al reporte JSON de run_ai_harness.py")
    prep.add_argument("--out-dir", type=str, default=_DEFAULT_AUDIT_DIR, help="Directorio de salida")
    prep.add_argument("--seed", type=int, default=20261117, help="Semilla reproducible")

    score = subparsers.add_parser("score", help="Calcula Kappa de Cohen y tasa de falsos pasantes")
    _add_score_arguments(score)

    prep_cmp = subparsers.add_parser(
        "prepare-comparative",
        help="Genera cuaderno doble ciego A/B mezclando RAG Híbrido y Control (sin IDs internos)",
    )
    prep_cmp.add_argument("--report", type=str, default=_DEFAULT_EVAL_REPORT, help="Ruta al reporte JSON comparativo")
    prep_cmp.add_argument("--out-dir", type=str, default=_DEFAULT_AUDIT_DIR, help="Directorio de salida")
    prep_cmp.add_argument("--seed", type=int, default=20261120, help="Semilla reproducible de barajado")
    prep_cmp.add_argument("--r1-name", type=str, default="Joel González", help="Nombre del Revisor 1")
    prep_cmp.add_argument("--r2-name", type=str, default="Maykol Madrid", help="Nombre del Revisor 2")

    score_cmp = subparsers.add_parser(
        "score-comparative",
        help="Calcula Kappa por brazo, falsos pasantes RNF-FUN-03 y prueba pareada de McNemar",
    )
    _add_score_arguments(score_cmp)
    return parser


def main() -> None:
    repo_root = root_dir.parent.resolve()
    args = _build_parser().parse_args()

    if args.command == "prepare":
        report_path = _resolve_repo_path(repo_root, args.report, _DEFAULT_EVAL_REPORT)
        out_dir = _resolve_repo_path(repo_root, args.out_dir, _DEFAULT_AUDIT_DIR)
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

    if args.command == "prepare-comparative":
        report_path = _resolve_repo_path(repo_root, args.report, _DEFAULT_EVAL_REPORT)
        out_dir = _resolve_repo_path(repo_root, args.out_dir, _DEFAULT_AUDIT_DIR)
        manifest = prepare_comparative_audit_bundle(
            report_path,
            out_dir,
            seed=args.seed,
            reviewer_1_name=args.r1_name,
            reviewer_2_name=args.r2_name,
            allowed_root=repo_root,
        )
        print(
            f"[OK] Paquete doble ciego A/B generado en {out_dir}: "
            f"{manifest['total_items']} ítems ({manifest['total_claims']} afirmaciones, seed={manifest['seed']})."
        )
        return

    manifest_path, r1_path, r2_path, adj_path, out_md, out_json = _resolve_score_paths(repo_root, args)

    if args.command == "score-comparative":
        cmp_report = score_comparative_audit(manifest_path, r1_path, r2_path, adjudication_csv=adj_path)
        _write_repo_text_file(repo_root, out_md, render_comparative_audit_markdown(cmp_report))
        _write_repo_text_file(
            repo_root,
            out_json,
            json.dumps(comparative_report_to_dict(cmp_report), indent=2, ensure_ascii=False),
        )
        print(
            f"[OK] S3-03 v2 puntuado: kappa(global)={cmp_report.case_kappa_all.kappa:.4f}, "
            f"McNemar causa_raiz p={cmp_report.root_cause_mcnemar.p_value_exact:.6g}, "
            f"falsos_pasantes={cmp_report.strict_false_passer_ci.successes}/{cmp_report.first_attempt_pass_count}"
        )
        print(f"     Informe Markdown: {out_md}")
        print(f"     Métricas JSON:    {out_json}")
        return

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
