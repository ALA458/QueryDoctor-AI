"""Adaptador para el perfil gemini_free (ADR-10 §8.3)."""

import asyncio
import re
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
from src.providers.base import BaseLLMProvider
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
        generation_config = self._build_generation_config(schema=_GEMINI_RESPONSE_SCHEMA)

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

        control_system = self.get_control_system_prompt()
        control_user = f"Analiza esta consulta SQL:\n{sql_text}"

        url = (
            f"https://generativelanguage.googleapis.com/v1beta/models/"
            f"{self.model_name}:generateContent?key={api_key}"
        )
        payload = {
            "system_instruction": {"parts": [{"text": control_system}]},
            "contents": [{"role": "user", "parts": [{"text": control_user}]}],
            "generationConfig": self._build_generation_config(schema=None),
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
            return self.parse_control_response(
                raw_content=raw_content,
                query_id=query_id,
                model_name=self.model_name,
                served_by="google-gemini-api",
                start_time=start_time,
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
                    await self._wait_before_retry(
                        attempt,
                        status_code=response.status_code,
                        retry_after=response.headers.get("Retry-After"),
                    )
                    continue
                response.raise_for_status()
                return response.json()
            except (httpx.RequestError, httpx.HTTPStatusError) as exc:
                if attempt < max_attempts:
                    err_resp = exc.response if isinstance(exc, httpx.HTTPStatusError) else None
                    await self._wait_before_retry(
                        attempt,
                        status_code=err_resp.status_code if err_resp is not None else None,
                        retry_after=err_resp.headers.get("Retry-After") if err_resp is not None else None,
                    )
                    continue
                safe_exc = re.sub(r"key=[^&\s'\"]+", "key=***", str(exc))
                raise RuntimeError(
                    f"Fallo al conectar con Gemini tras {max_attempts} intentos: {safe_exc}"
                ) from None

        raise RuntimeError("Fallo inesperado al invocar Gemini.")

    @staticmethod
    async def _wait_before_retry(
        attempt: int,
        status_code: Optional[int] = None,
        retry_after: Optional[str] = None,
    ) -> None:
        if status_code == 429:
            parsed_retry = float(retry_after) if retry_after and retry_after.isdigit() else 0.0
            delay = max(parsed_retry, min(65.0, 15.0 * attempt))
            await asyncio.sleep(delay)
            return
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

    def _build_generation_config(self, schema: Optional[dict[str, Any]] = None) -> dict[str, Any]:
        """Construye la configuración de generación compartida (desactiva thinkingBudget en gemini-2.5-*)."""
        config: dict[str, Any] = {
            "responseMimeType": "application/json",
            "temperature": 0.1,
            "maxOutputTokens": self.settings.MAX_TOKENS_PER_REQUEST,
        }
        if schema is not None:
            config["responseSchema"] = schema
        if "gemini-2.5" in self.model_name.lower():
            config["thinkingConfig"] = {"thinkingBudget": 0}
        return config
