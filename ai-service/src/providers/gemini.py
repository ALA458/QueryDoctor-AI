"""Adaptador para el perfil gemini_free (ADR-10 §8.3)."""

import asyncio
import json
import time
from typing import Any, Optional

import httpx

from src.config import Settings
from src.models.schemas import (
    ClaimOutput,
    ClaimType,
    ExplainMetadata,
    ExplainRequest,
    ExplainResponse,
)
from src.prompts.prompt_v1 import build_prompt_v1
from src.providers.base import BaseLLMProvider, _MARKDOWN_JSON_FENCE_RE
from src.security_lock import get_data_policy


_GEMINI_RESPONSE_SCHEMA: dict[str, Any] = {
    "type": "OBJECT",
    "required": ["summary", "claims"],
    "properties": {
        "summary": {"type": "STRING"},
        "claims": {
            "type": "ARRAY",
            "minItems": 1,
            "items": {
                "type": "OBJECT",
                "required": ["claim_type", "statement", "finding_ids", "kb_fragment_id"],
                "properties": {
                    "claim_type": {
                        "type": "STRING",
                        "enum": ["OBSERVACION", "INTERPRETACION", "RECOMENDACION"],
                    },
                    "statement": {"type": "STRING"},
                    "finding_ids": {
                        "type": "ARRAY",
                        "minItems": 1,
                        "items": {"type": "STRING"},
                    },
                    "kb_fragment_id": {"type": "STRING", "nullable": True},
                },
            },
        },
    },
}


class GeminiProvider(BaseLLMProvider):
    """Adaptador para la API de Gemini (plan gratuito)."""

    def __init__(self, settings: Settings, client: Optional[httpx.AsyncClient] = None):
        super().__init__(settings)
        self.model_name = self.settings.GEMINI_MODEL
        self._client = client

    async def generate_explanation(self, request: ExplainRequest) -> ExplainResponse:
        """Invoca la API de Gemini para generar afirmaciones tipadas."""
        api_key = self.settings.get_gemini_api_key()
        if not api_key:
            raise ValueError(
                "Clave de API de Gemini no configurada para el perfil 'gemini_free'. "
                "Defina GEMINI_API_KEY o GEMINI_API_KEY_FILE."
            )

        system_prompt, user_prompt = build_prompt_v1(request)

        url = (
            f"https://generativelanguage.googleapis.com/v1beta/models/"
            f"{self.model_name}:generateContent?key={api_key}"
        )
        generation_config: dict[str, Any] = {
            "responseMimeType": "application/json",
            "responseSchema": _GEMINI_RESPONSE_SCHEMA,
            "temperature": 0.1,
            "maxOutputTokens": self.settings.MAX_TOKENS_PER_REQUEST,
        }
        # Los modelos gemini-2.5-* consumen maxOutputTokens con pensamiento interno por defecto;
        # al desactivar el presupuesto de pensamiento evitamos JSON truncado por MAX_TOKENS.
        if "gemini-2.5" in self.model_name.lower():
            generation_config["thinkingConfig"] = {"thinkingBudget": 0}

        payload = {
            "system_instruction": {"parts": [{"text": system_prompt}]},
            "contents": [{"role": "user", "parts": [{"text": user_prompt}]}],
            "generationConfig": generation_config,
        }

        start_time = time.perf_counter()
        timeout = httpx.Timeout(self.settings.LLM_TIMEOUT_SECONDS, connect=10.0)

        should_close_client = False
        client = self._client
        if client is None:
            client = httpx.AsyncClient(timeout=timeout)
            should_close_client = True

        try:
            data = await self._post_with_retries(client, url, payload)
            raw_content = self._extract_response_text(data)
            return self.parse_and_validate_llm_json(
                raw_text=raw_content,
                request=request,
                model_name=self.model_name,
                served_by="google-gemini-api",
                start_time=start_time,
            )
        finally:
            if should_close_client and client is not None:
                await client.aclose()

    async def generate_control_explanation(self, sql_text: str, query_id: str) -> ExplainResponse:
        """Invoca la API de Gemini sin contexto determinista para la condición de control (SRS 6.3)."""
        api_key = self.settings.get_gemini_api_key()
        if not api_key:
            raise ValueError("Clave de API de Gemini no configurada.")

        control_system = (
            "Eres un asistente de optimización de SQL Server. Analiza la siguiente consulta SQL sin ningún contexto "
            "adicional de Showplan ni hallazgos de motor de reglas. Responde estrictamente en formato JSON con "
            "'summary' y 'claims' (claim_type: OBSERVACION | INTERPRETACION | RECOMENDACION, statement, finding_ids: [], kb_fragment_id: null)."
        )
        control_user = f"Analiza esta consulta SQL:\n{sql_text}"

        url = (
            f"https://generativelanguage.googleapis.com/v1beta/models/"
            f"{self.model_name}:generateContent?key={api_key}"
        )
        generation_config: dict[str, Any] = {
            "responseMimeType": "application/json",
            "temperature": 0.1,
            "maxOutputTokens": self.settings.MAX_TOKENS_PER_REQUEST,
        }

        payload = {
            "system_instruction": {"parts": [{"text": control_system}]},
            "contents": [{"role": "user", "parts": [{"text": control_user}]}],
            "generationConfig": generation_config,
        }

        start_time = time.perf_counter()
        timeout = httpx.Timeout(self.settings.LLM_TIMEOUT_SECONDS, connect=10.0)

        should_close_client = False
        client = self._client
        if client is None:
            client = httpx.AsyncClient(timeout=timeout)
            should_close_client = True

        try:
            data = await self._post_with_retries(client, url, payload)
            raw_content = self._extract_response_text(data)
            try:
                parsed = self._parse_llm_json(raw_content)
            except Exception:
                cleaned = raw_content.strip()
                fence_match = _MARKDOWN_JSON_FENCE_RE.match(cleaned)
                if fence_match:
                    cleaned = fence_match.group("body").strip()
                try:
                    cand = json.loads(cleaned)
                    if isinstance(cand, list) and cand and isinstance(cand[0], dict):
                        parsed = cand[0]
                    else:
                        parsed = {"summary": "Diagnóstico de control no estructurado", "claims": []}
                except Exception:
                    parsed = {"summary": "Diagnóstico de control con formato inválido", "claims": []}

            summary = parsed.get("summary", "Diagnóstico de control sin contexto")
            raw_claims = parsed.get("claims", [])
            claims: list[ClaimOutput] = []
            for c in raw_claims:
                ctype_str = str(c.get("claim_type", "OBSERVACION")).upper()
                ctype = ClaimType.OBSERVACION
                if ctype_str in ClaimType.__members__:
                    ctype = ClaimType[ctype_str]
                stmt = str(c.get("statement", "")).strip() or "Afirmación de control sin evidencia."
                fids = c.get("finding_ids", [])
                if not isinstance(fids, list) or not fids:
                    fids = ["find-conjetura-generica-001"]
                kb_id = c.get("kb_fragment_id")
                if ctype in (ClaimType.INTERPRETACION, ClaimType.RECOMENDACION) and not kb_id:
                    kb_id = "KB-NO-EXISTENTE-99"
                claims.append(ClaimOutput(
                    claim_type=ctype,
                    statement=stmt,
                    finding_ids=fids,
                    kb_fragment_id=kb_id,
                ))

            if not claims:
                claims.append(ClaimOutput(
                    claim_type=ClaimType.OBSERVACION,
                    statement="La consulta carece de contexto determinista para evaluar hallazgos.",
                    finding_ids=["find-conjetura-generica-001"],
                    kb_fragment_id=None,
                ))

            duration = time.perf_counter() - start_time
            metadata = ExplainMetadata(
                llm_profile=self.settings.LLM_PROFILE,
                model=self.model_name,
                prompt_version="control-baseline",
                served_by="google-gemini-api",
                data_policy=get_data_policy(self.settings.LLM_PROFILE),
                execution_time_seconds=duration,
            )

            return ExplainResponse(
                query_id=query_id,
                summary=summary,
                claims=claims,
                metadata=metadata,
            )
        finally:
            if should_close_client and client is not None:
                await client.aclose()

    async def _post_with_retries(
        self,
        client: httpx.AsyncClient,
        url: str,
        payload: dict[str, Any],
    ) -> Any:
        max_attempts = max(5, 1 + self.settings.LLM_MAX_RETRIES)

        for attempt in range(1, max_attempts + 1):
            try:
                response = await client.post(url, json=payload)
                if (
                    response.status_code in (429, 500, 502, 503, 504)
                    and attempt < max_attempts
                ):
                    await self._wait_before_retry(attempt)
                    continue
                response.raise_for_status()
                return response.json()
            except (httpx.RequestError, httpx.HTTPStatusError) as exc:
                if attempt < max_attempts:
                    await self._wait_before_retry(attempt)
                    continue
                raise RuntimeError(
                    f"Fallo al conectar con Gemini tras {max_attempts} intentos: {exc}"
                ) from exc

        raise RuntimeError("Fallo inesperado al invocar Gemini.")

    @staticmethod
    async def _wait_before_retry(attempt: int) -> None:
        await asyncio.sleep(2.0 * (2 ** (attempt - 1)))

    @staticmethod
    def _extract_response_text(data: Any) -> str:
        candidates = data.get("candidates", [])
        if not candidates:
            raise ValueError("Gemini no devolvió candidatos de respuesta.")

        parts = candidates[0].get("content", {}).get("parts", [])
        if not parts or "text" not in parts[0]:
            raise ValueError("Candidato de respuesta de Gemini vacío o sin texto.")

        return parts[0]["text"]
