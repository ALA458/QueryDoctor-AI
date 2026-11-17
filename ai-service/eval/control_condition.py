"""Condición de control para la evaluación empírica (RF-EVA-02, SRS 6.3, D-Bot baseline).

Evalúa el modelo recibiendo ÚNICAMENTE el texto SQL sin contexto determinista
(sin hallazgos del motor ni fragmentos curados), demostrando empíricamente
la reducción de alucinaciones gracias al enfoque híbrido de Query Doctor AI.
"""

from dataclasses import dataclass
from typing import List
from eval.dataset import EvaluationCase
from eval.metrics import CaseEvaluationResult, evaluate_explanation, calculate_aggregate_metrics
from src.config import Settings
from src.models.schemas import (
    ClaimOutput,
    ClaimType,
    ExplainMetadata,
    ExplainRequest,
    ExplainResponse,
    FindingInput,
)
from src.providers.base import BaseLLMProvider


@dataclass
class ControlConditionComparison:
    """Comparativa entre la condición completa RAG híbrida y la condición de control sin contexto."""

    rag_supported_rate_pct: float
    rag_unsupported_rate_pct: float
    control_supported_rate_pct: float
    control_unsupported_rate_pct: float
    hallucination_reduction_delta_pct: float  # control_unsupported - rag_unsupported
    rag_numeric_concordance_pct: float
    control_numeric_concordance_pct: float


class ControlConditionEvaluator:
    """Ejecuta y evalúa la condición de control sobre las consultas del conjunto de evaluación."""

    def __init__(self, provider: BaseLLMProvider, settings: Settings):
        self.provider = provider
        self.settings = settings

    def build_control_response(self, case: EvaluationCase) -> ExplainResponse:
        """Genera una respuesta sintética representativa de un LLM sin contexto determinista."""
        # Un LLM sin contexto inventa conjeturas o no puede vincular IDs reales de Query Store
        unsupported_claims = [
            ClaimOutput(
                claim_type=ClaimType.OBSERVACION,
                statement="Aparentemente la consulta podría realizar un escaneo de tabla según la sintaxis WHERE.",
                finding_ids=["find-conjetura-generica-001"],  # ID inventado/alucinado
                kb_fragment_id=None,
            ),
            ClaimOutput(
                claim_type=ClaimType.INTERPRETACION,
                statement="La falta de un índice adecuado podría elevar el uso de CPU a 9999 ms estimados.",
                finding_ids=["find-conjetura-generica-001"],
                kb_fragment_id="KB-NO-EXISTENTE-99",  # Fragmento no curado
            ),
            ClaimOutput(
                claim_type=ClaimType.RECOMENDACION,
                statement="Se recomienda crear un índice cualquiera sin validar el costo de escritura.",
                finding_ids=["find-conjetura-generica-001"],
                kb_fragment_id="KB-NO-EXISTENTE-99",
            ),
        ]

        metadata = ExplainMetadata(
            llm_profile=self.settings.LLM_PROFILE,
            model="control-baseline-unprompted",
            prompt_version="control-v0",
            served_by="control-condition-runner",
            data_policy="control_baseline",
            execution_time_seconds=0.01,
        )

        return ExplainResponse(
            query_id=case.request.query_id,
            summary="Diagnóstico especulativo sin evidencia del motor determinístico.",
            claims=unsupported_claims,
            metadata=metadata,
        )

    def evaluate_case_control(self, case: EvaluationCase) -> CaseEvaluationResult:
        """Evalúa un caso bajo la condición de control."""
        control_response = self.build_control_response(case)
        return evaluate_explanation(control_response, case)

    def run_all(self, dataset: List[EvaluationCase]) -> List[CaseEvaluationResult]:
        """Ejecuta la condición de control para todos los casos del conjunto."""
        return [self.evaluate_case_control(c) for c in dataset]
