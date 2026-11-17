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

# Asegurar que ai-service esté en sys.path
root_dir = Path(__file__).resolve().parent.parent
if str(root_dir) not in sys.path:
    sys.path.insert(0, str(root_dir))

from eval.harness import AiEvaluationHarness
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
    return parser.parse_args()


async def main_async() -> int:
    args = parse_args()

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

    print("\nEjecutando arnés de evaluación de IA (RF-EVA-02 v1.1)...")
    print(f"Perfil: {settings.LLM_PROFILE} | Casos: {args.limit or len(harness.dataset)}")

    report = await harness.run_evaluation(
        include_control=not args.skip_control,
        limit=args.limit,
    )

    # 1. Imprimir resumen visual en consola (CA-HU20.2)
    print("\n" + report.to_console_summary() + "\n")

    # 2. Guardar reportes en archivos para el informe del laboratorio (RF-EVA-04)
    out_dir = Path(args.output_dir)
    try:
        out_dir.mkdir(parents=True, exist_ok=True)

        json_path = out_dir / "ai_evaluation_report.json"
        md_path = out_dir / "ai_evaluation_report.md"

        # Exportar JSON
        report_dict = {
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
                }
                for r in report.case_results
            ],
        }

        json_path.write_text(json.dumps(report_dict, indent=2, ensure_ascii=False), encoding="utf-8")
        md_path.write_text(report.to_markdown(), encoding="utf-8")

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
