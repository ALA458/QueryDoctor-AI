"""Módulo de evaluación y arnés del componente de IA (RF-EVA-02 v1.1, SRS 6.3)."""

from eval.harness import AiEvaluationHarness
from eval.metrics import evaluate_explanation, calculate_aggregate_metrics

__all__ = [
    "AiEvaluationHarness",
    "evaluate_explanation",
    "calculate_aggregate_metrics",
]
