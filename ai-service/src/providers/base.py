"""Clase base abstracta para proveedores de LLM (ADR-10, RNF-FIA-02)."""

import json
import re
import time
from abc import ABC, abstractmethod
from typing import Any, Dict
from src.config import Settings
from src.models.schemas import (
    ClaimOutput,
    ClaimType,
    ExplainMetadata,
    ExplainRequest,
    ExplainResponse,
)
from src.security_lock import get_data_policy


class BaseLLMProvider(ABC):
    """Adaptador base para interactuar con modelos de lenguaje."""

    def __init__(self, settings: Settings):
        self.settings = settings

    @abstractmethod
    async def generate_explanation(self, request: ExplainRequest) -> ExplainResponse:
        """Genera una explicación estructurada en afirmaciones para la solicitud dada."""
        pass

    def parse_and_validate_llm_json(
        self,
        raw_text: str,
        request: ExplainRequest,
        model_name: str,
        served_by: str,
        start_time: float,
    ) -> ExplainResponse:
        """Limpia la salida del LLM, extrae el JSON y valida el esquema de afirmaciones (RF-IA-02)."""
        cleaned = raw_text.strip()

        # Remover bloques de código markdown si el LLM los incluyó
        if cleaned.startswith("```"):
            cleaned = re.sub(r"^```[a-zA-Z0-9_-]*\s*", "", cleaned)
            cleaned = re.sub(r"\s*```$", "", cleaned)
            cleaned = cleaned.strip()

        # Si aún hay texto antes o después del JSON, extraer el primer bloque {...}
        json_match = re.search(r"\{.*\}", cleaned, re.DOTALL)
        if json_match:
            cleaned = json_match.group(0)

        try:
            data = json.loads(cleaned)
        except json.JSONDecodeError as exc:
            raise ValueError(f"El LLM no devolvió un JSON válido: {exc}. Salida cruda: {raw_text[:200]}") from exc

        summary = data.get("summary", "Diagnóstico de rendimiento de consulta SQL Server.")
        raw_claims = data.get("claims", [])
        if not isinstance(raw_claims, list) or len(raw_claims) == 0:
            raise ValueError("El JSON generado debe contener una lista no vacía de 'claims'.")

        valid_finding_ids = {f.finding_id for f in request.findings}
        valid_fragment_ids = {kb.fragment_id for kb in request.kb_fragments}

        validated_claims: list[ClaimOutput] = []
        for idx, item in enumerate(raw_claims):
            if not isinstance(item, dict):
                continue

            claim_type_str = item.get("claim_type", "").upper()
            try:
                claim_type = ClaimType(claim_type_str)
            except ValueError:
                # Si el modelo devolvió un tipo no exacto, inferir o mapear
                if "OBSERV" in claim_type_str:
                    claim_type = ClaimType.OBSERVACION
                elif "RECOMEND" in claim_type_str:
                    claim_type = ClaimType.RECOMENDACION
                else:
                    claim_type = ClaimType.INTERPRETACION

            statement = item.get("statement", "").strip()
            if not statement:
                statement = f"Afirmación diagnóstica generada #{idx + 1}"

            finding_ids = item.get("finding_ids", [])
            if isinstance(finding_ids, str):
                finding_ids = [finding_ids]
            # Filtrar solo ids válidos o si el modelo inventó uno, asociar al primer finding válido
            filtered_finding_ids = [fid for fid in finding_ids if fid in valid_finding_ids]
            if not filtered_finding_ids and request.findings:
                filtered_finding_ids = [request.findings[0].finding_id]

            kb_fragment_id = item.get("kb_fragment_id")
            if kb_fragment_id:
                kb_fragment_id = kb_fragment_id.strip()

            # En INTERPRETACION y RECOMENDACION, asegurar fragment_id válido
            if claim_type in (ClaimType.INTERPRETACION, ClaimType.RECOMENDACION):
                if not kb_fragment_id or (valid_fragment_ids and kb_fragment_id not in valid_fragment_ids):
                    # Asignar el primer fragmento disponible si existe
                    if request.kb_fragments:
                        kb_fragment_id = request.kb_fragments[0].fragment_id
                    else:
                        kb_fragment_id = "KB-GENERAL-01"

            claim = ClaimOutput(
                claim_type=claim_type,
                statement=statement,
                finding_ids=filtered_finding_ids,
                kb_fragment_id=kb_fragment_id,
            )
            validated_claims.append(claim)

        if not validated_claims:
            raise ValueError("No se pudieron validar afirmaciones válidas a partir de la salida del LLM.")

        execution_time = round(time.perf_counter() - start_time, 4)
        metadata = ExplainMetadata(
            llm_profile=self.settings.LLM_PROFILE,
            model=model_name,
            prompt_version=self.settings.PROMPT_VERSION,
            served_by=served_by,
            data_policy=get_data_policy(self.settings.LLM_PROFILE),
            execution_time_seconds=execution_time,
        )

        return ExplainResponse(
            query_id=request.query_id,
            summary=summary,
            claims=validated_claims,
            metadata=metadata,
        )
