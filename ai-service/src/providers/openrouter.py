"""Adaptador para el proveedor OpenRouter (ADR-10 §8.3, RNF-FIA-02, RNF-SEG-08)."""

import asyncio
import time
from typing import Any, Dict, Optional
import httpx
from src.config import Settings
from src.models.schemas import ExplainRequest, ExplainResponse
from src.prompts.prompt_v1 import build_prompt_v1
from src.providers.base import BaseLLMProvider


class OpenRouterProvider(BaseLLMProvider):
    """Adaptador para OpenRouter con soporte para perfiles openrouter_paid y openrouter_free."""

    def __init__(self, settings: Settings, client: Optional[httpx.AsyncClient] = None):
        super().__init__(settings)
        self.is_paid = self.settings.LLM_PROFILE.strip().lower() == "openrouter_paid"
        self.model_name = (
            self.settings.OPENROUTER_MODEL_PAID
            if self.is_paid
            else self.settings.OPENROUTER_MODEL_FREE
        )
        self._client = client

    async def generate_explanation(self, request: ExplainRequest) -> ExplainResponse:
        """Envía el prompt a OpenRouter y valida la respuesta contra el esquema de afirmaciones."""
        api_key = self.settings.get_openrouter_api_key()
        if not api_key:
            raise ValueError(
                f"Clave de API de OpenRouter no configurada para el perfil '{self.settings.LLM_PROFILE}'. "
                "Defina OPENROUTER_API_KEY o OPENROUTER_API_KEY_FILE."
            )

        system_prompt, user_prompt = build_prompt_v1(request)

        headers = {
            "Authorization": f"Bearer {api_key}",
            "HTTP-Referer": "https://querydoctor.ai",
            "X-Title": "Query Doctor AI",
            "Content-Type": "application/json",
        }

        # Construir cuerpo de la solicitud
        payload: Dict[str, Any] = {
            "model": self.model_name,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt},
            ],
            "temperature": 0.1,
            "max_tokens": self.settings.MAX_TOKENS_PER_REQUEST,
            "response_format": {"type": "json_object"},
        }

        self._apply_paid_provider_policy(payload)

        start_time = time.perf_counter()
        timeout = httpx.Timeout(self.settings.LLM_TIMEOUT_SECONDS, connect=10.0)

        # Política de reintentos (RNF-FIA-02: timeout configurable, máx 2 reintentos)
        last_exception: Optional[Exception] = None
        max_attempts = 1 + self.settings.LLM_MAX_RETRIES

        should_close_client = False
        client = self._client
        if client is None:
            client = httpx.AsyncClient(timeout=timeout)
            should_close_client = True

        try:
            for attempt in range(1, max_attempts + 1):
                try:
                    response = await client.post(
                        f"{self.settings.OPENROUTER_BASE_URL.rstrip('/')}/chat/completions",
                        json=payload,
                        headers=headers,
                    )

                    # Si el servidor responde 429 (rate limit) o 5xx (servidor ocupado), reintentar
                    if response.status_code in (429, 500, 502, 503, 504):
                        if attempt < max_attempts:
                            await asyncio.sleep(1.0 * (2 ** (attempt - 1)))
                            continue
                        response.raise_for_status()

                    response.raise_for_status()
                    data = response.json()
                    raw_content = data["choices"][0]["message"]["content"]

                    return self.parse_and_validate_llm_json(
                        raw_text=raw_content,
                        request=request,
                        model_name=self.model_name,
                        served_by="openrouter.ai",
                        start_time=start_time,
                    )

                except (httpx.RequestError, httpx.HTTPStatusError) as exc:
                    last_exception = exc
                    if attempt < max_attempts:
                        await asyncio.sleep(1.0 * (2 ** (attempt - 1)))
                        continue
                    raise RuntimeError(
                        f"Fallo de comunicación con OpenRouter ({self.model_name}) tras {max_attempts} intentos: {exc}"
                    ) from exc

            raise RuntimeError(f"Fallo inesperado con OpenRouter: {last_exception}")
        finally:
            if should_close_client and client is not None:
                await client.aclose()

    def _apply_paid_provider_policy(self, payload: Dict[str, Any]) -> None:
        # En el perfil de pago, exigir política de cero retención y denegación de recolección (ADR-10 §8.3)
        if self.is_paid:
            payload["provider"] = {
                "data_collection": "deny",
                "zdr": True,
            }
