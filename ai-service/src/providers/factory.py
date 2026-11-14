"""Fábrica de proveedores de LLM según el perfil activo (ADR-10 §8.3)."""

from typing import Optional
import httpx
from src.config import Settings
from src.providers.base import BaseLLMProvider
from src.providers.gemini import GeminiProvider
from src.providers.mock import MockProvider
from src.providers.ollama import OllamaProvider
from src.providers.openrouter import OpenRouterProvider


def get_llm_provider(settings: Settings, client: Optional[httpx.AsyncClient] = None) -> BaseLLMProvider:
    """Instancia el adaptador adecuado según LLM_PROFILE."""
    profile = settings.LLM_PROFILE.strip().lower()

    if profile in ("openrouter_paid", "openrouter_free"):
        return OpenRouterProvider(settings, client=client)
    elif profile == "gemini_free":
        return GeminiProvider(settings, client=client)
    elif profile == "ollama_local":
        return OllamaProvider(settings, client=client)
    elif profile == "mock":
        return MockProvider(settings)
    else:
        raise ValueError(f"Perfil de LLM no soportado por la fábrica: '{profile}'")
