"""Adaptador para el perfil gemini_free (ADR-10 §8.3)."""

import asyncio
import time
from typing import Optional
import httpx
from src.config import Settings
from src.models.schemas import ExplainRequest, ExplainResponse
from src.prompts.prompt_v1 import build_prompt_v1
from src.providers.base import BaseLLMProvider


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

        payload = {
            "system_instruction": {
                "parts": [{"text": system_prompt}]
            },
            "contents": [
                {
                    "role": "user",
                    "parts": [{"text": user_prompt}]
                }
            ],
            "generationConfig": {
                "responseMimeType": "application/json",
                "temperature": 0.1,
                "maxOutputTokens": self.settings.MAX_TOKENS_PER_REQUEST,
            },
        }

        start_time = time.perf_counter()
        timeout = httpx.Timeout(self.settings.LLM_TIMEOUT_SECONDS, connect=10.0)

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
                    if response.status_code in (429, 500, 502, 503, 504):
                        if attempt < max_attempts:
                            await asyncio.sleep(1.0 * (2 ** (attempt - 1)))
                            continue
                    response.raise_for_status()

                    data = response.json()
                    candidates = data.get("candidates", [])
                    if not candidates:
                        raise ValueError("Gemini no devolvió candidatos de respuesta.")

                    parts = candidates[0].get("content", {}).get("parts", [])
                    if not parts or "text" not in parts[0]:
                        raise ValueError("Candidato de respuesta de Gemini vacío o sin texto.")

                    raw_content = parts[0]["text"]
                    return self.parse_and_validate_llm_json(
                        raw_text=raw_content,
                        request=request,
                        model_name=self.model_name,
                        served_by="google-gemini-api",
                        start_time=start_time,
                    )
                except (httpx.RequestError, httpx.HTTPStatusError) as exc:
                    if attempt < max_attempts:
                        await asyncio.sleep(1.0 * (2 ** (attempt - 1)))
                        continue
                    raise RuntimeError(f"Fallo al conectar con Gemini tras {max_attempts} intentos: {exc}") from exc

            raise RuntimeError("Fallo inesperado al invocar Gemini.")
        finally:
            if should_close_client and client is not None:
                await client.aclose()
