"""Adaptador para el perfil ollama_local (ADR-10 §8.3)."""

import asyncio
import time
from typing import Optional
import httpx
from src.config import Settings
from src.models.schemas import ExplainRequest, ExplainResponse
from src.prompts.prompt_v1 import build_prompt_v1
from src.providers.base import BaseLLMProvider


class OllamaProvider(BaseLLMProvider):
    """Adaptador para una instancia local de Ollama (datos 100% locales, sin salida a Internet)."""

    def __init__(self, settings: Settings, client: Optional[httpx.AsyncClient] = None):
        super().__init__(settings)
        self.model_name = self.settings.OLLAMA_MODEL
        self._client = client

    async def generate_explanation(self, request: ExplainRequest) -> ExplainResponse:
        """Invoca la API compatible de Ollama en local."""
        system_prompt, user_prompt = build_prompt_v1(request)

        url = f"{self.settings.OLLAMA_BASE_URL.rstrip('/')}/chat/completions"

        payload = {
            "model": self.model_name,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt},
            ],
            "format": "json",
            "stream": False,
            "options": {
                "temperature": 0.1,
                "num_predict": self.settings.MAX_TOKENS_PER_REQUEST,
            },
        }

        start_time = time.perf_counter()
        timeout = httpx.Timeout(self.settings.LLM_TIMEOUT_SECONDS, connect=5.0)

        should_close_client = False
        client = self._client
        if client is None:
            client = httpx.AsyncClient(timeout=timeout)
            should_close_client = True

        max_attempts = 1 + self.settings.LLM_MAX_RETRIES

        try:
            for attempt in range(1, max_attempts + 1):
                try:
                    response = await client.post(url, json=payload)
                    response.raise_for_status()
                    data = response.json()
                    raw_content = data["choices"][0]["message"]["content"]

                    return self.parse_and_validate_llm_json(
                        raw_text=raw_content,
                        request=request,
                        model_name=self.model_name,
                        served_by="ollama-local",
                        start_time=start_time,
                    )
                except (httpx.RequestError, httpx.HTTPStatusError) as exc:
                    if attempt < max_attempts:
                        await asyncio.sleep(1.0 * (2 ** (attempt - 1)))
                        continue
                    raise RuntimeError(f"Fallo al conectar con Ollama en {url} tras {max_attempts} intentos: {exc}") from exc

            raise RuntimeError("Fallo inesperado al invocar Ollama.")
        finally:
            if should_close_client and client is not None:
                await client.aclose()
