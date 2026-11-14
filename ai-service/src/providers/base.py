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
        data = self._parse_llm_json(raw_text)
        summary = data.get("summary", "Diagnóstico de rendimiento de consulta SQL Server.")
        raw_claims = data.get("claims", [])
        if not isinstance(raw_claims, list) or len(raw_claims) == 0:
            raise ValueError("El JSON generado debe contener una lista no vacía de 'claims'.")

        valid_finding_ids = {f.finding_id for f in request.findings}
        valid_fragment_ids = {kb.fragment_id for kb in request.kb_fragments}
        validated_claims = self._build_claims(
            raw_claims,
            request,
            valid_finding_ids,
            valid_fragment_ids,
        )

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

    @staticmethod
    def _parse_llm_json(raw_text: str) -> Any:
        cleaned = raw_text.strip()

        if cleaned.startswith("```"):
            cleaned = re.sub(r"^```[a-zA-Z0-9_-]*\s*", "", cleaned)
            if cleaned.endswith("```"):
                cleaned = cleaned[:-3].rstrip()
            cleaned = cleaned.strip()

        json_match = re.search(r"\{.*\}", cleaned, re.DOTALL)
        if json_match:
            cleaned = json_match.group(0)

        try:
            return json.loads(cleaned)
        except json.JSONDecodeError as exc:
            raise ValueError(
                f"El LLM no devolvió un JSON válido: {exc}. Salida cruda: {raw_text[:200]}"
            ) from exc

    def _build_claims(
        self,
        raw_claims: list[Any],
        request: ExplainRequest,
        valid_finding_ids: set[str],
        valid_fragment_ids: set[str],
    ) -> list[ClaimOutput]:
        claims: list[ClaimOutput] = []
        for idx, item in enumerate(raw_claims):
            if isinstance(item, dict):
                claims.append(
                    self._build_claim(
                        item,
                        idx,
                        request,
                        valid_finding_ids,
                        valid_fragment_ids,
                    )
                )
        return claims

    def _build_claim(
        self,
        item: dict[str, Any],
        idx: int,
        request: ExplainRequest,
        valid_finding_ids: set[str],
        valid_fragment_ids: set[str],
    ) -> ClaimOutput:
        claim_type_str = item.get("claim_type", "").upper()
        claim_type = self._normalize_claim_type(claim_type_str)

        statement = item.get("statement", "").strip()
        if not statement:
            statement = f"Afirmación diagnóstica generada #{idx + 1}"

        finding_ids = self._normalize_finding_ids(
            item.get("finding_ids", []),
            request,
            valid_finding_ids,
        )
        kb_fragment_id = self._normalize_kb_fragment_id(
            item.get("kb_fragment_id"),
            claim_type,
            request,
            valid_fragment_ids,
        )
        return ClaimOutput(
            claim_type=claim_type,
            statement=statement,
            finding_ids=finding_ids,
            kb_fragment_id=kb_fragment_id,
        )

    @staticmethod
    def _normalize_claim_type(claim_type_str: str) -> ClaimType:
        try:
            return ClaimType(claim_type_str)
        except ValueError:
            if "OBSERV" in claim_type_str:
                return ClaimType.OBSERVACION
            if "RECOMEND" in claim_type_str:
                return ClaimType.RECOMENDACION
            return ClaimType.INTERPRETACION

    @staticmethod
    def _normalize_finding_ids(
        finding_ids: Any,
        request: ExplainRequest,
        valid_finding_ids: set[str],
    ) -> list[str]:
        if isinstance(finding_ids, str):
            finding_ids = [finding_ids]
        filtered_finding_ids = [fid for fid in finding_ids if fid in valid_finding_ids]
        if not filtered_finding_ids and request.findings:
            return [request.findings[0].finding_id]
        return filtered_finding_ids

    @staticmethod
    def _normalize_kb_fragment_id(
        kb_fragment_id: Any,
        claim_type: ClaimType,
        request: ExplainRequest,
        valid_fragment_ids: set[str],
    ) -> str | None:
        if kb_fragment_id:
            kb_fragment_id = kb_fragment_id.strip()

        requires_fragment = claim_type in (
            ClaimType.INTERPRETACION,
            ClaimType.RECOMENDACION,
        )
        if not requires_fragment:
            return kb_fragment_id

        if kb_fragment_id and (
            not valid_fragment_ids or kb_fragment_id in valid_fragment_ids
        ):
            return kb_fragment_id
        if request.kb_fragments:
            return request.kb_fragments[0].fragment_id
        return "KB-GENERAL-01"
