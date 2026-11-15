"""Clase base abstracta para proveedores de LLM con validación estricta anti-inyección (ADR-10, RNF-FIA-02, RNF-SEG-06)."""

import json
import re
import time
from abc import ABC, abstractmethod
from typing import Any
from src.config import Settings
from src.models.schemas import (
    ClaimOutput,
    ClaimType,
    ExplainMetadata,
    ExplainRequest,
    ExplainResponse,
)
from src.security_lock import get_data_policy

_MARKDOWN_JSON_FENCE_RE = re.compile(
    r"^\s*```(?:json)?[ \t]*\r?\n(?P<body>.*)\r?\n[ \t]*```\s*$",
    re.DOTALL | re.IGNORECASE,
)


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
        """Valida estrictamente el JSON devuelto por el LLM sin tolerar prosa extra ni coercionar campos inválidos (RF-IA-02, RNF-SEG-06)."""
        data = self._parse_llm_json(raw_text)

        raw_summary = data.get("summary")
        if not isinstance(raw_summary, str) or not raw_summary.strip():
            raise ValueError("El JSON generado debe contener un campo 'summary' de texto no vacío.")
        summary = raw_summary.strip()

        raw_claims = data.get("claims")
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
    def _parse_llm_json(raw_text: str) -> dict[str, Any]:
        if not isinstance(raw_text, str) or not raw_text.strip():
            raise ValueError("El LLM devolvió una respuesta vacía.")

        cleaned = raw_text.strip()
        fence_match = _MARKDOWN_JSON_FENCE_RE.match(cleaned)
        if fence_match:
            cleaned = fence_match.group("body").strip()

        if not cleaned.startswith("{") or not cleaned.endswith("}"):
            raise ValueError(
                f"El LLM incluyó texto fuera del objeto JSON esperado (posible ruptura de formato). Salida cruda: {raw_text[:200]}"
            )

        try:
            parsed = json.loads(cleaned)
        except json.JSONDecodeError as exc:
            raise ValueError(
                f"El LLM no devolvió un JSON válido: {exc}. Salida cruda: {raw_text[:200]}"
            ) from exc

        if not isinstance(parsed, dict):
            raise ValueError("La raíz de la respuesta del LLM debe ser un objeto JSON.")

        return parsed

    def _build_claims(
        self,
        raw_claims: list[Any],
        request: ExplainRequest,
        valid_finding_ids: set[str],
        valid_fragment_ids: set[str],
    ) -> list[ClaimOutput]:
        claims: list[ClaimOutput] = []
        for idx, item in enumerate(raw_claims):
            if not isinstance(item, dict):
                raise ValueError(f"La afirmación #{idx} debe ser un objeto JSON válido.")
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
        raw_claim_type = item.get("claim_type")
        if not isinstance(raw_claim_type, str) or not raw_claim_type.strip():
            raise ValueError(f"La afirmación #{idx} carece de 'claim_type' válido.")

        claim_type = self._parse_strict_claim_type(raw_claim_type, idx)

        raw_statement = item.get("statement")
        if not isinstance(raw_statement, str) or not raw_statement.strip():
            raise ValueError(f"La afirmación #{idx} tiene 'statement' vacío o inválido.")
        statement = raw_statement.strip()

        finding_ids = self._validate_finding_ids(
            item.get("finding_ids"),
            idx,
            valid_finding_ids,
        )
        kb_fragment_id = self._validate_kb_fragment_id(
            item.get("kb_fragment_id"),
            idx,
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
    def _parse_strict_claim_type(raw_claim_type: str, idx: int) -> ClaimType:
        normalized = raw_claim_type.strip().upper()
        try:
            return ClaimType(normalized)
        except ValueError as exc:
            raise ValueError(
                f"La afirmación #{idx} tiene un 'claim_type' no permitido: '{raw_claim_type}'."
            ) from exc

    @staticmethod
    def _validate_finding_ids(
        finding_ids: Any,
        idx: int,
        valid_finding_ids: set[str],
    ) -> list[str]:
        if not isinstance(finding_ids, list) or len(finding_ids) == 0:
            raise ValueError(
                f"La afirmación #{idx} debe incluir una lista no vacía en 'finding_ids'."
            )

        validated: list[str] = []
        for raw_fid in finding_ids:
            if not isinstance(raw_fid, str) or not raw_fid.strip():
                raise ValueError(f"La afirmación #{idx} contiene un finding_id vacío o no textual.")
            fid = raw_fid.strip()
            if fid not in valid_finding_ids:
                raise ValueError(
                    f"La afirmación #{idx} referencia un finding_id inexistente en el contexto: '{fid}'."
                )
            if fid not in validated:
                validated.append(fid)

        return validated

    @staticmethod
    def _validate_kb_fragment_id(
        kb_fragment_id: Any,
        idx: int,
        claim_type: ClaimType,
        request: ExplainRequest,
        valid_fragment_ids: set[str],
    ) -> str | None:
        if kb_fragment_id is None:
            normalized_frag = None
        elif isinstance(kb_fragment_id, str):
            stripped = kb_fragment_id.strip()
            normalized_frag = stripped if stripped else None
        else:
            raise ValueError(f"El 'kb_fragment_id' en la afirmación #{idx} debe ser cadena o null.")

        requires_fragment = claim_type in (
            ClaimType.INTERPRETACION,
            ClaimType.RECOMENDACION,
        )

        if requires_fragment and normalized_frag is None:
            raise ValueError(
                f"La afirmación #{idx} ({claim_type.value}) requiere obligatoriamente 'kb_fragment_id'."
            )

        if normalized_frag is not None and request.kb_fragments and normalized_frag not in valid_fragment_ids:
            raise ValueError(
                f"La afirmación #{idx} referencia un kb_fragment_id inexistente en los fragmentos suministrados: '{normalized_frag}'."
            )

        return normalized_frag
