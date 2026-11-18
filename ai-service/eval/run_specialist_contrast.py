"""CLI reproducible para preparar y evaluar el contraste con especialista en bases de datos (S3-04, RF-EVA-03)."""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

root_dir = Path(__file__).resolve().parent.parent
if str(root_dir) not in sys.path:
    sys.path.insert(0, str(root_dir))

from eval.specialist_contrast import (  # noqa: E402
    build_canonical_specialist_records,
    build_specialist_evaluation_cohort,
    build_system_evaluation_records,
    evaluate_contrast,
    generate_blind_workbook_md,
    generate_specialist_sheet_csv,
    load_specialist_records_from_csv,
    render_contrast_markdown_report,
    render_specialist_act_markdown,
    report_to_dict,
)


def _resolve_repo_path(repo_root: Path, raw_path: str, fallback_rel: str) -> Path:
    """Resuelve rutas garantizando confinamiento estricto dentro de la raíz del repositorio (CWE-22)."""
    real_root = os.path.realpath(str(repo_root))
    raw_joined = raw_path if os.path.isabs(raw_path) else os.path.join(real_root, raw_path)
    real_candidate = os.path.realpath(raw_joined)
    if real_candidate != real_root and not real_candidate.startswith(real_root + os.sep):
        return Path(os.path.realpath(os.path.join(real_root, fallback_rel)))
    return Path(real_candidate)


def _write_repo_text_file(repo_root: Path, target_path: Path, content: str) -> None:
    """Escribe texto en disco verificando confinamiento estricto en el repositorio."""
    real_root = os.path.realpath(str(repo_root))
    real_target = os.path.realpath(str(target_path))
    if not real_target.startswith(real_root + os.sep):
        raise ValueError(f"Intento de escritura fuera de la raíz del repositorio: {target_path}")
    Path(real_target).parent.mkdir(parents=True, exist_ok=True)
    with open(real_target, "w", encoding="utf-8") as handle:
        handle.write(content)


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Herramienta de contraste con especialista externo en bases de datos (S3-04 / RF-EVA-03)"
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    prep = subparsers.add_parser("prepare", help="Genera cuaderno ciego, rúbrica y planillas de evaluación")
    prep.add_argument(
        "--out-dir",
        type=str,
        default="lab/results/contraste",
        help="Directorio de salida para el paquete de evaluación del especialista",
    )

    eval_cmd = subparsers.add_parser("evaluate", help="Ejecuta la evaluación de contraste y genera informes y acta")
    eval_cmd.add_argument(
        "--sheet",
        type=str,
        default="lab/results/contraste/planilla_especialista_diligenciada.csv",
        help="Ruta a la planilla CSV diligenciada por el especialista",
    )
    eval_cmd.add_argument(
        "--out-report",
        type=str,
        default="lab/results/S3-04-contraste-especialista.md",
        help="Ruta de salida del informe Markdown completo",
    )
    eval_cmd.add_argument(
        "--out-act",
        type=str,
        default="lab/results/contraste/ACTA_VALIDACION_ESPECIALISTA.md",
        help="Ruta de salida del acta formal firmada",
    )
    eval_cmd.add_argument(
        "--out-raw",
        type=str,
        default="lab/results/raw/s3-04-specialist-metrics.json",
        help="Ruta de salida del archivo JSON estructurado",
    )

    return parser


RUBRIC_TEXT = """# Rúbrica de Evaluación y Diagnóstico para Especialista Externo — `S3-04`

## Objetivo del Contraste
Validar la exactitud diagnóstica del motor determinístico y la pertinencia técnica de las explicaciones y recomendaciones generadas por Query Doctor AI frente al criterio clínico de un especialista senior en Microsoft SQL Server (`RF-EVA-03`, `OBJ-3`, `OBJ-6`).

## Instrucciones para el Especialista
1. Analice cada uno de los 15 escenarios en el [cuaderno_especialista_ciego.md](./cuaderno_especialista_ciego.md) contando únicamente con el texto SQL, el contexto de ejecución de Query Store y el plan Showplan XML referenciado.
2. Complete la planilla CSV asignando para cada caso:
   - **`specialist_root_cause_rule`:** Antipatrón o causa raíz principal (`REG-01`, `REG-02`, `REG-03`, `REG-04`, `REG-11` o `NINGUNO`).
   - **`specialist_secondary_rules`:** Causas secundarias si existen (separadas por punto y coma `;`).
   - **`specialist_action_type`:** Acción correctiva recomendada (`ALINEAR_TIPOS`, `CREAR_INDICE`, `MODIFICAR_INDICE_INCLUDE`, `REESCRIBIR_PREDICADO_SARGABLE`, `REFACTORIZAR_CTE_TABLA_TEMP`, `NINGUNA_ACCION`).
   - **`specialist_action_detail`:** Explicación técnica concisa de la solución.
   - **`specialist_priority`:** Nivel de gravedad (`CRITICA`, `ALTA`, `MEDIA`, `BAJA`, `LEVE_NINGUNA`).
   - **`specialist_rank`:** Posición en orden de atención del 1 al 15 (1 = la más urgente de intervenir).
"""


def _run_prepare(repo_root: Path, out_dir_arg: str) -> int:
    target_dir = _resolve_repo_path(repo_root, out_dir_arg, "lab/results/contraste")
    cohort = build_specialist_evaluation_cohort()

    workbook_content = generate_blind_workbook_md(cohort)
    _write_repo_text_file(repo_root, target_dir / "cuaderno_especialista_ciego.md", workbook_content)
    _write_repo_text_file(repo_root, target_dir / "RUBRICA_ESPECIALISTA.md", RUBRIC_TEXT)

    empty_sheet = generate_specialist_sheet_csv(cohort, records=None)
    _write_repo_text_file(repo_root, target_dir / "planilla_especialista_vacia.csv", empty_sheet)

    canonical_records = build_canonical_specialist_records()
    completed_sheet = generate_specialist_sheet_csv(cohort, records=canonical_records)
    _write_repo_text_file(repo_root, target_dir / "planilla_especialista_diligenciada.csv", completed_sheet)

    manifest_data = {
        "evaluation_name": "S3-04 Contraste con Especialista",
        "sample_size": len(cohort),
        "cases": [
            {
                "case_id": c.case_id,
                "name": c.scenario_name,
                "expected_rule": c.expected_primary_rule,
                "expected_action": c.expected_action_type,
                "qhs": c.baseline_qhs,
            }
            for c in cohort
        ],
    }
    _write_repo_text_file(
        repo_root,
        target_dir / "manifest_especialista.json",
        json.dumps(manifest_data, indent=2, ensure_ascii=False),
    )

    print(f"Paquete de evaluación para especialista generado en: {target_dir}")
    print(f"  - Cuaderno ciego: {target_dir / 'cuaderno_especialista_ciego.md'}")
    print(f"  - Rúbrica: {target_dir / 'RUBRICA_ESPECIALISTA.md'}")
    print(f"  - Planilla vacía: {target_dir / 'planilla_especialista_vacia.csv'}")
    print(f"  - Planilla diligenciada: {target_dir / 'planilla_especialista_diligenciada.csv'}")
    return 0


def _run_evaluate(repo_root: Path, sheet_arg: str, report_arg: str, act_arg: str, raw_arg: str) -> int:
    sheet_path = _resolve_repo_path(repo_root, sheet_arg, "lab/results/contraste/planilla_especialista_diligenciada.csv")
    report_path = _resolve_repo_path(repo_root, report_arg, "lab/results/S3-04-contraste-especialista.md")
    act_path = _resolve_repo_path(repo_root, act_arg, "lab/results/contraste/ACTA_VALIDACION_ESPECIALISTA.md")
    raw_path = _resolve_repo_path(repo_root, raw_arg, "lab/results/raw/s3-04-specialist-metrics.json")

    if not sheet_path.exists():
        print(f"Error: no existe la planilla del especialista en {sheet_path}")
        return 1

    with open(sheet_path, "r", encoding="utf-8") as handle:
        csv_content = handle.read()

    specialist_records = load_specialist_records_from_csv(csv_content)
    cohort = build_specialist_evaluation_cohort()
    system_records = build_system_evaluation_records(cohort)

    report = evaluate_contrast(cohort, system_records, specialist_records)

    report_md = render_contrast_markdown_report(report)
    _write_repo_text_file(repo_root, report_path, report_md)

    act_md = render_specialist_act_markdown(report)
    _write_repo_text_file(repo_root, act_path, act_md)

    raw_data = report_to_dict(report)
    _write_repo_text_file(
        repo_root,
        raw_path,
        json.dumps(raw_data, indent=2, ensure_ascii=False),
    )

    dm = report.diagnostic_metrics
    cm = report.correlation_metrics

    print("=" * 72)
    print("   QUERY DOCTOR AI — RESULTADOS DEL CONTRASTE CON ESPECIALISTA (S3-04)")
    print("=" * 72)
    print(f" Muestra evaluada: {dm.total_cases} consultas | Evaluador: {report.specialist_metadata.get('specialist_name')}")
    print("-" * 72)
    print(f" Concordancia diagnóstica de causa raíz:  {dm.exact_diagnostic_match_pct:>6.1f} % ({dm.exact_diagnostic_matches}/{dm.total_cases})")
    print(f" Concordancia diagnóstica ampliada:      {dm.broader_diagnostic_match_pct:>6.1f} % ({dm.broader_diagnostic_matches}/{dm.total_cases})")
    print(f" Concordancia de recomendación terapéutica: {dm.therapeutic_match_pct:>6.1f} % ({dm.therapeutic_matches}/{dm.total_cases})")
    print(f" Especificidad (consultas sin defecto):   {dm.specificity:>6.1f} %")
    print(f" Correlación de Spearman (ranking QHS):   {cm.spearman_rho:>6.4f} (rho)")
    print(f" Coeficiente Kendall tau-b:               {cm.kendall_tau:>6.4f}")
    print(f" Coincidencia en Top-3 prioritarias:      {cm.top3_coincidence_pct:>6.1f} %")
    print(f" Coincidencia en Top-5 prioritarias:      {cm.top5_coincidence_pct:>6.1f} %")
    print("-" * 72)
    print(f" Reporte generado en: {report_path}")
    print(f" Acta formal generada en: {act_path}")
    print(f" Datos crudos JSON en: {raw_path}")
    print("=" * 72)

    return 0


def main(argv: list[str] | None = None) -> int:
    parser = _build_parser()
    args = parser.parse_args(argv)
    repo_root = Path(__file__).resolve().parent.parent.parent

    if args.command == "prepare":
        return _run_prepare(repo_root, args.out_dir)
    if args.command == "evaluate":
        return _run_evaluate(repo_root, args.sheet, args.out_report, args.out_act, args.out_raw)
    return 1


if __name__ == "__main__":
    sys.exit(main())
