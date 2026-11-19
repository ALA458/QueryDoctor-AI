"""CLI reproducible para preparar y puntuar el contraste con el especialista (S3-04, RF-EVA-03)."""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from pathlib import Path
from typing import Any, Dict

root_dir = Path(__file__).resolve().parent.parent
if str(root_dir) not in sys.path:
    sys.path.insert(0, str(root_dir))

try:
    from dotenv import load_dotenv

    load_dotenv(root_dir.parent / ".env")
except ImportError:
    pass

from eval.specialist_contrast import (  # noqa: E402
    prepare_specialist_pack,
    read_specialist_csv,
    render_specialist_contrast_markdown,
    run_end_to_end_ai_for_cases,
    score_specialist_contrast,
)
from src.config import Settings  # noqa: E402
from src.providers.factory import get_llm_provider  # noqa: E402
from src.security_lock import enforce_security_lock  # noqa: E402


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
        description="Herramienta de contraste independiente con especialista en BD (S3-04 / RF-EVA-03)"
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    prep = subparsers.add_parser(
        "prepare",
        help="Genera explicaciones IA sobre casos exportados del motor y crea el paquete ciego (Etapa A/B)",
    )
    prep.add_argument(
        "--engine-export",
        type=str,
        default="lab/results/raw/s3-04-engine-export.json",
        help="Ruta al JSON exportado por QueryDoctor.EngineHarness --export-cases",
    )
    prep.add_argument(
        "--outputs-json",
        type=str,
        default="lab/results/raw/s3-04-system-outputs.json",
        help="Ruta de salida para las respuestas consolidadas del motor e IA",
    )
    prep.add_argument(
        "--out-dir",
        type=str,
        default="lab/results/especialista",
        help="Directorio de salida para cuadernos y planillas del especialista",
    )
    prep.add_argument(
        "--profile",
        type=str,
        default=os.getenv("LLM_PROFILE", "gemini_free"),
        choices=["mock", "openrouter_free", "openrouter_paid", "gemini_free", "ollama_local"],
        help="Perfil de LLM para explicar los casos reales",
    )
    prep.add_argument(
        "--env",
        type=str,
        default=os.getenv("APP_ENV", "lab"),
        help="Entorno (dev | lab | prod)",
    )
    prep.add_argument(
        "--allow-sharing",
        action="store_true",
        default=os.getenv("ALLOW_DATA_SHARING_PROVIDER", "true").lower() == "true",
        help="Autoriza proveedores gratuitos en lab",
    )
    prep.add_argument(
        "--delay",
        type=float,
        default=None,
        help="Pausa en segundos entre llamadas LLM (por defecto 6.5 s en gemini_free)",
    )
    prep.add_argument(
        "--checkpoint",
        type=str,
        default="lab/results/raw/s3-04-ai-checkpoint.json",
        help="Ruta JSON para guardar avance incremental",
    )
    prep.add_argument(
        "--reuse-outputs",
        action="store_true",
        help="Reutiliza outputs-json existente sin volver a llamar al LLM",
    )
    prep.add_argument(
        "--seed",
        type=int,
        default=20261119,
        help="Semilla para barajar el orden ciego",
    )

    score = subparsers.add_parser(
        "score",
        help="Calcula concordancia, validez IA y correlación de prioridad a partir de planillas diligenciadas",
    )
    score.add_argument(
        "--manifest",
        type=str,
        default="lab/results/especialista/specialist_manifest.json",
        help="Ruta a specialist_manifest.json",
    )
    score.add_argument(
        "--stage-a",
        type=str,
        default="lab/results/especialista/planilla_etapa_a.csv",
        help="Planilla CSV diligenciada de Etapa A",
    )
    score.add_argument(
        "--stage-b",
        type=str,
        default="lab/results/especialista/planilla_etapa_b.csv",
        help="Planilla CSV diligenciada de Etapa B",
    )
    score.add_argument(
        "--specialist",
        type=str,
        default="Aisner Marrugo",
        help="Nombre del especialista evaluador",
    )
    score.add_argument(
        "--out-md",
        type=str,
        default="lab/results/S3-04-contraste-especialista.md",
        help="Ruta de salida para informe Markdown",
    )
    score.add_argument(
        "--out-json",
        type=str,
        default="lab/results/raw/s3-04-contrast-metrics.json",
        help="Ruta de salida para métricas JSON",
    )
    return parser


def _resolve_model_name(settings: Settings) -> str:
    if settings.LLM_PROFILE == "gemini_free":
        return settings.GEMINI_MODEL
    if settings.LLM_PROFILE == "openrouter_free":
        return settings.OPENROUTER_MODEL_FREE
    if settings.LLM_PROFILE == "openrouter_paid":
        return settings.OPENROUTER_MODEL_PAID
    if settings.LLM_PROFILE == "ollama_local":
        return settings.OLLAMA_MODEL
    return "deterministic-mock-v1"


def main() -> int:
    args = _build_parser().parse_args()
    repo_root = root_dir.parent

    if args.command == "prepare":
        outputs_path = _resolve_repo_path(repo_root, args.outputs_json, "lab/results/raw/s3-04-system-outputs.json")
        out_dir = _resolve_repo_path(repo_root, args.out_dir, "lab/results/especialista")

        if args.reuse_outputs and outputs_path.exists():
            with open(outputs_path, "r", encoding="utf-8") as handle:
                system_outputs: Dict[str, Any] = json.load(handle)
        else:
            export_path = _resolve_repo_path(repo_root, args.engine_export, "lab/results/raw/s3-04-engine-export.json")
            with open(export_path, "r", encoding="utf-8") as handle:
                engine_bundle = json.load(handle)

            settings = Settings(
                APP_ENV=args.env,
                LLM_PROFILE=args.profile,
                ALLOW_DATA_SHARING_PROVIDER=args.allow_sharing,
            )
            enforce_security_lock(settings)
            provider = get_llm_provider(settings)
            delay = args.delay if args.delay is not None else (6.5 if args.profile == "gemini_free" else (0.0 if args.profile == "mock" else 1.5))
            ckpt_path = _resolve_repo_path(repo_root, args.checkpoint, "lab/results/raw/s3-04-ai-checkpoint.json") if args.checkpoint else None

            system_outputs = asyncio.run(
                run_end_to_end_ai_for_cases(
                    engine_bundle=engine_bundle,
                    provider=provider,
                    profile_name=args.profile,
                    model_name=_resolve_model_name(settings),
                    prompt_version=settings.PROMPT_VERSION,
                    delay_seconds=delay,
                    checkpoint_path=ckpt_path,
                )
            )
            _write_repo_text_file(
                repo_root,
                outputs_path,
                json.dumps(system_outputs, indent=2, ensure_ascii=False),
            )

        manifest = prepare_specialist_pack(
            system_outputs=system_outputs,
            out_dir=out_dir,
            allowed_root=repo_root,
            seed=args.seed,
        )
        print(f"Paquete S3-04 generado en: {out_dir} ({manifest['total_cases']} casos)")
        return 0

    manifest_path = _resolve_repo_path(repo_root, args.manifest, "lab/results/especialista/specialist_manifest.json")
    stage_a_path = _resolve_repo_path(repo_root, args.stage_a, "lab/results/especialista/planilla_etapa_a.csv")
    stage_b_path = _resolve_repo_path(repo_root, args.stage_b, "lab/results/especialista/planilla_etapa_b.csv")
    out_md_path = _resolve_repo_path(repo_root, args.out_md, "lab/results/S3-04-contraste-especialista.md")
    out_json_path = _resolve_repo_path(repo_root, args.out_json, "lab/results/raw/s3-04-contrast-metrics.json")

    with open(manifest_path, "r", encoding="utf-8") as handle:
        manifest_data = json.load(handle)

    stage_a_rows = read_specialist_csv(
        stage_a_path,
        ("case_label", "expert_has_issue", "expert_categories", "expert_recommendation", "expert_priority_rank"),
    )
    stage_b_rows = read_specialist_csv(
        stage_b_path,
        ("case_label", "engine_agreement", "ai_validity", "ai_utility_1_to_5", "observations"),
    )
    report = score_specialist_contrast(
        manifest=manifest_data,
        stage_a_rows=stage_a_rows,
        stage_b_rows=stage_b_rows,
        specialist_name=args.specialist,
    )
    _write_repo_text_file(repo_root, out_json_path, json.dumps(report, indent=2, ensure_ascii=False))
    _write_repo_text_file(repo_root, out_md_path, render_specialist_contrast_markdown(report))
    print(f"Informe S3-04 generado en: {out_md_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
