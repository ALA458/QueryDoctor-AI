"""CLI ejecutable del arnés de evaluación de IA (CA-HU20.2, RF-EVA-02, SRS 6.3).

Uso:
  python eval/run_ai_harness.py
  python eval/run_ai_harness.py --profile mock
  python eval/run_ai_harness.py --profile openrouter_free
  python eval/run_ai_harness.py --output-dir lab/results
"""

import argparse
import asyncio
import json
import os
import sys
from pathlib import Path
from typing import Any, Dict

# Asegurar que ai-service esté en sys.path
root_dir = Path(__file__).resolve().parent.parent
if str(root_dir) not in sys.path:
    sys.path.insert(0, str(root_dir))

from eval.harness import AiEvaluationHarness, CheckpointPausedError
from src.config import Settings
from src.providers.factory import get_llm_provider
from src.security_lock import enforce_security_lock


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Arnés de evaluación empírica del componente de IA (RF-EVA-02, CA-HU20.2)",
    )
    parser.add_argument(
        "--profile",
        type=str,
        default=os.getenv("LLM_PROFILE", "mock"),
        choices=["mock", "openrouter_free", "openrouter_paid", "gemini_free", "ollama_local"],
        help="Perfil de LLM a evaluar (por defecto: mock o variable LLM_PROFILE)",
    )
    parser.add_argument(
        "--env",
        type=str,
        default=os.getenv("APP_ENV", "dev"),
        help="Entorno de ejecución (dev | lab | prod)",
    )
    parser.add_argument(
        "--allow-sharing",
        action="store_true",
        default=os.getenv("ALLOW_DATA_SHARING_PROVIDER", "false").lower() == "true",
        help="Habilita proveedores que comparten datos en dev/lab (ADR-10)",
    )
    parser.add_argument(
        "--output-dir",
        type=str,
        default=str(root_dir.parent / "lab" / "results"),
        help="Directorio donde se guardarán los informes JSON y Markdown",
    )
    parser.add_argument(
        "--limit",
        type=int,
        default=None,
        help="Limita el número de consultas a evaluar (útil para pruebas rápidas)",
    )
    parser.add_argument(
        "--skip-control",
        action="store_true",
        help="Omite la condición de control sin contexto",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=None,
        help="Pausa en segundos entre llamadas al LLM (por defecto: 6.5 s en gemini_free, 1.5 s en otros reales, 0 en mock)",
    )
    parser.add_argument(
        "--checkpoint",
        type=str,
        default=None,
        help="Ruta JSON opcional para guardar progreso y reanudar por lotes ante límites de cuota",
    )
    parser.add_argument(
        "--max-requests",
        type=int,
        default=None,
        help="Límite máximo de llamadas nuevas al LLM en esta ejecución (requiere --checkpoint para reanudar)",
    )
    return parser.parse_args()


def _build_report_dict(report) -> Dict[str, Any]:
    """Serializa los resultados del reporte a un diccionario estructurado."""
    return {
        "metadata": report.metadata,
        "rag_metrics": {
            "total_cases": report.rag_metrics.total_cases,
            "total_claims": report.rag_metrics.total_claims,
            "supported_rate_pct": report.rag_metrics.overall_supported_rate_pct,
            "unsupported_rate_pct": report.rag_metrics.overall_unsupported_rate_pct,
            "numeric_concordance_pct": report.rag_metrics.overall_numeric_concordance_pct,
            "type_distribution_pct": report.rag_metrics.type_distribution_pct,
            "unsupported_interpretation_rate_pct": report.rag_metrics.overall_unsupported_interpretation_rate_pct,
            "trazada_cases": report.rag_metrics.trazada_cases_count,
            "no_verificada_cases": report.rag_metrics.no_verificada_cases_count,
            "adversarial_success_rate_pct": report.rag_metrics.adversarial_success_rate_pct,
        },
        "control_comparison": (
            {
                "rag_supported_rate_pct": report.control_comparison.rag_supported_rate_pct,
                "control_supported_rate_pct": report.control_comparison.control_supported_rate_pct,
                "hallucination_reduction_delta_pct": report.control_comparison.hallucination_reduction_delta_pct,
            }
            if report.control_comparison
            else None
        ),
        "cases": [
            {
                "case_id": r.case_id,
                "name": r.name,
                "category": r.category,
                "total_claims": r.total_claims,
                "supported_rate_pct": r.supported_rate_pct,
                "numeric_concordance_pct": r.numeric_concordance_pct,
                "is_trazada": r.is_trazada,
                "adversarial_contained": r.adversarial_contained,
                "failure_reasons": r.failure_reasons,
                "summary": r.response.summary if r.response else "",
                "claims": (
                    [
                        {
                            "claim_index": c_idx,
                            "claim_type": c.claim_type.value if hasattr(c.claim_type, "value") else str(c.claim_type),
                            "statement": c.statement,
                            "finding_ids": c.finding_ids,
                            "kb_fragment_id": c.kb_fragment_id,
                        }
                        for c_idx, c in enumerate(r.response.claims, start=1)
                    ]
                    if r.response
                    else []
                ),
            }
            for r in report.case_results
        ],
    }


def _write_safe_report_file(base_dir: Path, target_dir_str: str, filename: str, content: str) -> Path:
    """Escribe un archivo de reporte validando que la ruta no escape de base_dir (CWE-22 / S2083)."""
    clean_filename = os.path.basename(filename)
    resolved_base = base_dir.resolve()

    # Sanitización rigurosa de la carpeta destino contra Path Traversal
    resolved_target_dir = Path(target_dir_str).resolve()
    if os.path.commonpath([str(resolved_base), str(resolved_target_dir)]) != str(resolved_base):
        resolved_target_dir = (resolved_base / "lab" / "results").resolve()

    resolved_target_dir.mkdir(parents=True, exist_ok=True)
    target_file = (resolved_target_dir / clean_filename).resolve()

    # Verificación final de contención antes de la escritura
    if os.path.commonpath([str(resolved_target_dir), str(target_file)]) != str(resolved_target_dir):
        raise ValueError(f"Intento de escape de ruta detectado para {clean_filename}")

    target_file.write_text(content, encoding="utf-8")
    return target_file


_DEFAULT_DELAY_BY_PROFILE: dict[str, float] = {
    "gemini_free": 6.5,
    "mock": 0.0,
}


async def main_async() -> int:
    args = parse_args()
    repo_root = root_dir.parent.resolve()

    settings = Settings(
        APP_ENV=args.env,
        LLM_PROFILE=args.profile,
        ALLOW_DATA_SHARING_PROVIDER=args.allow_sharing,
    )

    # Validar candado de seguridad antes de ejecutar
    try:
        enforce_security_lock(settings)
    except Exception as exc:
        print(f"\n[ERROR DE CANDADO DE SEGURIDAD]: {exc}", file=sys.stderr)
        return 2

    provider = get_llm_provider(settings)
    harness = AiEvaluationHarness(settings=settings, provider=provider)

    default_delay = _DEFAULT_DELAY_BY_PROFILE.get(settings.LLM_PROFILE, 1.5)
    request_delay = args.delay if args.delay is not None else default_delay
    checkpoint_path = Path(args.checkpoint).resolve() if args.checkpoint else None

    print("\nEjecutando arnés de evaluación de IA (RF-EVA-02 v1.1)...")
    print(f"Perfil: {settings.LLM_PROFILE} | Casos: {args.limit or len(harness.dataset)} | Pausa: {request_delay:.1f}s")

    try:
        report = await harness.run_evaluation(
            include_control=not args.skip_control,
            limit=args.limit,
            request_delay_seconds=request_delay,
            checkpoint_path=checkpoint_path,
            max_requests=args.max_requests,
        )
    except CheckpointPausedError as paused:
        print(f"\n[PAUSA POR LOTE] {paused}", file=sys.stderr)
        print("Vuelve a ejecutar el mismo comando para reanudar desde el checkpoint.", file=sys.stderr)
        return 3

    # 1. Imprimir resumen visual en consola (CA-HU20.2)
    print("\n" + report.to_console_summary() + "\n")

    # 2. Guardar reportes en archivos de forma segura para el informe del laboratorio (RF-EVA-04)
    try:
        report_dict = _build_report_dict(report)
        json_content = json.dumps(report_dict, indent=2, ensure_ascii=False)
        md_content = report.to_markdown()

        json_path = _write_safe_report_file(repo_root, args.output_dir, "ai_evaluation_report.json", json_content)
        md_path = _write_safe_report_file(repo_root, args.output_dir, "ai_evaluation_report.md", md_content)

        print(f"Reporte JSON guardado en: {json_path}")
        print(f"Reporte Markdown guardado en: {md_path}")
    except Exception as exc:
        print(f"Aviso: no se pudo escribir los archivos de reporte: {exc}", file=sys.stderr)

    return 0


def main() -> None:
    code = asyncio.run(main_async())
    sys.exit(code)


if __name__ == "__main__":
    main()
