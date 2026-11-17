"""Condición de control para la evaluación empírica (RF-EVA-02, SRS 6.3, D-Bot baseline).

Evalúa el modelo recibiendo ÚNICAMENTE el texto SQL sin contexto determinista
(sin hallazgos del motor ni fragmentos curados), demostrando empíricamente
la reducción de alucinaciones gracias al enfoque híbrido de Query Doctor AI.
"""

import asyncio
from dataclasses import dataclass
from typing import List, Optional
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
    """Ejecuta y evalúa la condición de control sobre las consultas del conjunto de evaluación (SRS 6.3 punto 4)."""

    def __init__(self, provider: BaseLLMProvider, settings: Settings):
        self.provider = provider
        self.settings = settings

    async def evaluate_case_control(self, case: EvaluationCase) -> CaseEvaluationResult:
        """Evalúa un caso bajo la condición de control ejecutando el LLM sin contexto determinista."""
        response = await self.provider.generate_control_explanation(
            sql_text=case.request.sql_text,
            query_id=case.case_id,
        )
        return evaluate_explanation(response, case)

    async def run_all(
        self,
        dataset: List[EvaluationCase],
        delay_seconds: Optional[float] = None,
    ) -> List[CaseEvaluationResult]:
        """Ejecuta la condición de control para todos los casos del conjunto."""
        if delay_seconds is not None:
            effective_delay = delay_seconds
        elif self.settings.LLM_PROFILE != "mock":
            effective_delay = 1.0
        else:
            effective_delay = 0.0
        results: List[CaseEvaluationResult] = []
        for case in dataset:
            res = await self.evaluate_case_control(case)
            results.append(res)
            if effective_delay > 0.0:
                await asyncio.sleep(effective_delay)
        return results
