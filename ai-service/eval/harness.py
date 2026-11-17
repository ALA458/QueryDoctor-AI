"""Orquestador principal del arnés de evaluación de IA (RF-EVA-02, CA-HU20.2, SRS 6.3)."""

import json
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional
from eval.control_condition import ControlConditionComparison, ControlConditionEvaluator
from eval.dataset import EvaluationCase, build_evaluation_dataset
from eval.metrics import (
    AggregateMetrics,
    CaseEvaluationResult,
    calculate_aggregate_metrics,
    evaluate_explanation,
)
from src.config import Settings, get_settings
from src.providers.base import BaseLLMProvider
from src.providers.factory import get_llm_provider


@dataclass
class EvaluationReport:
    """Informe completo generado por el arnés de IA para auditoría y laboratorio (RF-EVA-04)."""

    metadata: Dict[str, Any]
    rag_metrics: AggregateMetrics
    control_comparison: Optional[ControlConditionComparison]
    case_results: List[CaseEvaluationResult]

    def _format_markdown_comparison_table(self) -> List[str]:
        if self.control_comparison:
            c = self.control_comparison
            return [
                f"| **Afirmaciones sustentadas en evidencia** | **{c.rag_supported_rate_pct:.1f} %** | {c.control_supported_rate_pct:.1f} % | **+{c.rag_supported_rate_pct - c.control_supported_rate_pct:.1f} %** |",
                f"| **Afirmaciones sin sustento (Alucinación)** | **{c.rag_unsupported_rate_pct:.1f} %** | {c.control_unsupported_rate_pct:.1f} % | **-{c.hallucination_reduction_delta_pct:.1f} %** (Reducción) |",
                f"| **Concordancia de valores numéricos** | **{c.rag_numeric_concordance_pct:.1f} %** | {c.control_numeric_concordance_pct:.1f} % | **+{c.rag_numeric_concordance_pct - c.control_numeric_concordance_pct:.1f} %** |",
            ]
        m = self.rag_metrics
        return [
            f"| **Afirmaciones sustentadas en evidencia** | **{m.overall_supported_rate_pct:.1f} %** | N/A | N/A |",
            f"| **Afirmaciones sin sustento (Alucinación)** | **{m.overall_unsupported_rate_pct:.1f} %** | N/A | N/A |",
            f"| **Concordancia de valores numéricos** | **{m.overall_numeric_concordance_pct:.1f} %** | N/A | N/A |",
        ]

    def _format_markdown_case_summary_table(self) -> List[str]:
        lines = [
            "",
            "## 5. Detalle por Consulta Evaluada",
            "",
            "| ID | Nombre del Escenario | Categoría | Afirmaciones | Sustentadas | Concordancia Num. | Estado |",
            "| :--- | :--- | :--- | :---: | :---: | :---: | :---: |",
        ]
        for r in self.case_results:
            estado = "TRAZADA" if r.is_trazada else "NO_VERIFICADA"
            lines.append(
                f"| `{r.case_id}` | {r.name[:45]} | `{r.category}` | {r.total_claims} | {r.supported_rate_pct:.0f} % | {r.numeric_concordance_pct:.0f} % | `{estado}` |"
            )
        return lines

    @staticmethod
    def _format_single_case_audit(r: CaseEvaluationResult) -> List[str]:
        estado = "TRAZADA" if r.is_trazada else "NO_VERIFICADA"
        lines = [
            f"### Consulta `{r.case_id}`: {r.name}",
            f"- **Categoría:** `{r.category}` | **Estado:** `{estado}` | **Sustento:** {r.supported_rate_pct:.1f} % | **Concordancia:** {r.numeric_concordance_pct:.1f} %",
        ]
        if r.response:
            lines.append(f"- **Resumen LLM:** {r.response.summary}")
            lines.append("- **Afirmaciones (Claims):**")
            for c_idx, c in enumerate(r.response.claims, start=1):
                fids = ", ".join(c.finding_ids) if c.finding_ids else "[Ninguno]"
                kbid = c.kb_fragment_id or "[Sin KB]"
                lines.append(f"  {c_idx}. `[{c.claim_type.value}]` {c.statement} *(Hallazgos: {fids} | KB: {kbid})*")
        if r.failure_reasons:
            lines.append(f"- **Motivos de falla:** {'; '.join(r.failure_reasons)}")
        lines.append("")
        return lines

    def _format_markdown_audit_section(self) -> List[str]:
        lines = [
            "",
            "## 6. Registro Completo de Explicaciones para Auditoría Humana (S3-03 / RNF-FUN-03)",
            "> Salidas completas para la planilla de revisión independiente de dos auditores (cálculo de Kappa de Cohen y falsos pasantes).",
            "",
        ]
        for r in self.case_results:
            lines.extend(self._format_single_case_audit(r))
        return lines

    def to_markdown(self) -> str:
        """Formatea el informe en Markdown técnico para su inclusión en la memoria o reporte."""
        m = self.rag_metrics
        meta = self.metadata

        lines = [
            "# Informe de Evaluación del Componente de IA — Query Doctor AI",
            "",
            "> Generado por el arnés automatizado de evaluación de IA (`RF-EVA-02`, `CA-HU20.2`, SRS 6.3).",
            "",
            "## 1. Metadatos de la Corrida",
            f"- **Perfil LLM:** `{meta.get('llm_profile')}`",
            f"- **Entorno:** `{meta.get('app_env')}`",
            f"- **Política de datos:** `{meta.get('data_policy')}`",
            f"- **Versión de prompt:** `{meta.get('prompt_version')}`",
            f"- **Tamaño de muestra:** {m.total_cases} consultas ({m.total_cases - m.adversarial_cases_count} estándar + {m.adversarial_cases_count} adversariales)",
            f"- **Total de afirmaciones evaluadas:** {m.total_claims}",
            "",
            "## 2. Resumen de Métricas de Fidelidad (RAG Híbrido frente a Condición de Control)",
            "",
            "| Métrica | Condición RAG Híbrida | Condición de Control (Sin Contexto) | Delta de Mejora (Aporte Híbrido) |",
            "| :--- | :---: | :---: | :---: |",
            *self._format_markdown_comparison_table(),
            "",
            "## 3. Distribución de Afirmaciones por Tipo",
            f"- **OBSERVACION:** {m.type_distribution_pct.get('OBSERVACION', 0.0):.1f} %",
            f"- **INTERPRETACION:** {m.type_distribution_pct.get('INTERPRETACION', 0.0):.1f} %",
            f"- **RECOMENDACION:** {m.type_distribution_pct.get('RECOMENDACION', 0.0):.1f} %",
            f"- **Tasa de interpretaciones no sustentadas por KB:** {m.overall_unsupported_interpretation_rate_pct:.1f} %",
            "",
            "## 4. Estado de Validación y Robustez Adversarial",
            f"- **Consultas con estado TRAZADA:** {m.trazada_cases_count} / {m.total_cases} ({(m.trazada_cases_count/m.total_cases*100.0):.1f} %)",
            f"- **Consultas con estado NO_VERIFICADA:** {m.no_verificada_cases_count} / {m.total_cases}",
            f"- **Tasa de éxito en contención adversarial (RNF-SEG-06):** {m.adversarial_success_rate_pct:.1f} %",
            *self._format_markdown_case_summary_table(),
            *self._format_markdown_audit_section(),
        ]

        return "\n".join(lines)

    def to_console_summary(self) -> str:
        """Genera una tabla visual limpia en texto para consola que cumple con CA-HU20.2."""
        m = self.rag_metrics
        c = self.control_comparison

        header = "=" * 78
        lines = [
            header,
            "     QUERY DOCTOR AI — RESULTADOS DEL ARNÉS DE EVALUACIÓN DE IA (RF-EVA-02)",
            header,
            f" Perfil LLM: {self.metadata.get('llm_profile')} | Muestra: {m.total_cases} consultas ({m.total_claims} afirmaciones)",
            "-" * 78,
            f" {'MÉTRICA DE FIDELIDAD (RAGAS / RF-EVA-02)':<45} | {'RAG HÍBRIDO':<12} | {'CONTROL':<12}",
            "-" * 78,
        ]

        if c:
            lines.extend([
                f" {'Afirmaciones sustentadas en evidencia (%)':<45} | {c.rag_supported_rate_pct:>10.1f} % | {c.control_supported_rate_pct:>10.1f} %",
                f" {'Afirmaciones sin sustento / Alucinación (%)':<45} | {c.rag_unsupported_rate_pct:>10.1f} % | {c.control_unsupported_rate_pct:>10.1f} %",
                f" {'Concordancia numérica con evidencia (%)':<45} | {c.rag_numeric_concordance_pct:>10.1f} % | {c.control_numeric_concordance_pct:>10.1f} %",
                f" {'REDUCCIÓN DE ALUCINACIONES (Delta de aporte)':<45} | {c.hallucination_reduction_delta_pct:>10.1f} % | {'[Línea Base]':<12}",
            ])
        else:
            lines.extend([
                f" {'Afirmaciones sustentadas en evidencia (%)':<45} | {m.overall_supported_rate_pct:>10.1f} % | {'N/A':<12}",
                f" {'Afirmaciones sin sustento / Alucinación (%)':<45} | {m.overall_unsupported_rate_pct:>10.1f} % | {'N/A':<12}",
                f" {'Concordancia numérica con evidencia (%)':<45} | {m.overall_numeric_concordance_pct:>10.1f} % | {'N/A':<12}",
            ])

        lines.extend([
            "-" * 78,
            " DISTRIBUCIÓN DE AFIRMACIONES:",
            f"   - OBSERVACIÓN:     {m.type_distribution_pct.get('OBSERVACION', 0.0):>5.1f} %",
            f"   - INTERPRETACIÓN:  {m.type_distribution_pct.get('INTERPRETACION', 0.0):>5.1f} % (Sin KB curada: {m.overall_unsupported_interpretation_rate_pct:.1f} %)",
            f"   - RECOMENDACIÓN:   {m.type_distribution_pct.get('RECOMENDACION', 0.0):>5.1f} %",
            "-" * 78,
            f" ESTADOS: TRAZADA = {m.trazada_cases_count} ({m.trazada_cases_count/m.total_cases*100:.1f}%) | NO_VERIFICADA = {m.no_verificada_cases_count}",
            f" ROBUSTEZ ADVERSARIAL (RNF-SEG-06): {m.adversarial_success_rate_pct:.1f} % de contención exitosa",
            header,
        ])
        return "\n".join(lines)


class AiEvaluationHarness:
    """Orquestador para ejecutar la batería de pruebas y calcular las métricas de RF-EVA-02."""

    def __init__(
        self,
        settings: Optional[Settings] = None,
        provider: Optional[BaseLLMProvider] = None,
        dataset: Optional[List[EvaluationCase]] = None,
    ):
        self.settings = settings or get_settings()
        self.provider = provider or get_llm_provider(self.settings)
        self.dataset = dataset or build_evaluation_dataset()

    async def run_evaluation(
        self,
        include_control: bool = True,
        limit: Optional[int] = None,
    ) -> EvaluationReport:
        """Ejecuta el protocolo de evaluación sobre el dataset y calcula todas las métricas requeridas."""
        cases_to_evaluate = self.dataset[:limit] if limit else self.dataset

        rag_results: List[CaseEvaluationResult] = []
        for case in cases_to_evaluate:
            response = await self.provider.generate_explanation(case.request)
            case_result = evaluate_explanation(response, case)
            rag_results.append(case_result)

        rag_metrics = calculate_aggregate_metrics(rag_results)

        control_comparison: Optional[ControlConditionComparison] = None
        if include_control:
            control_evaluator = ControlConditionEvaluator(self.provider, self.settings)
            control_results = await control_evaluator.run_all(cases_to_evaluate)
            ctrl_metrics = calculate_aggregate_metrics(control_results)

            reduction_delta = ctrl_metrics.overall_unsupported_rate_pct - rag_metrics.overall_unsupported_rate_pct
            control_comparison = ControlConditionComparison(
                rag_supported_rate_pct=rag_metrics.overall_supported_rate_pct,
                rag_unsupported_rate_pct=rag_metrics.overall_unsupported_rate_pct,
                control_supported_rate_pct=ctrl_metrics.overall_supported_rate_pct,
                control_unsupported_rate_pct=ctrl_metrics.overall_unsupported_rate_pct,
                hallucination_reduction_delta_pct=round(max(0.0, reduction_delta), 2),
                rag_numeric_concordance_pct=rag_metrics.overall_numeric_concordance_pct,
                control_numeric_concordance_pct=ctrl_metrics.overall_numeric_concordance_pct,
            )

        metadata = {
            "app_env": self.settings.APP_ENV,
            "llm_profile": self.settings.LLM_PROFILE,
            "data_policy": getattr(self.provider, "data_policy", "deterministic"),
            "prompt_version": self.settings.PROMPT_VERSION,
            "total_cases_evaluated": len(cases_to_evaluate),
        }

        return EvaluationReport(
            metadata=metadata,
            rag_metrics=rag_metrics,
            control_comparison=control_comparison,
            case_results=rag_results,
        )
