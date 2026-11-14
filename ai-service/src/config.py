"""Configuración del microservicio de IA de Query Doctor."""

from functools import lru_cache
from pathlib import Path
from typing import Optional
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Parámetros de configuración del servicio de IA (ADR-02, ADR-10, RNF-FIA-02, RNF-SEG-08)."""

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    # Entorno de despliegue: dev | lab | prod (ADR-10, RES-06)
    APP_ENV: str = "dev"

    # Perfil activo de LLM: openrouter_paid | openrouter_free | gemini_free | ollama_local | mock (ADR-10 §8.3)
    LLM_PROFILE: str = "openrouter_free"

    # Bandera obligatoria en dev/lab para habilitar proveedores que comparten datos (ADR-10)
    ALLOW_DATA_SHARING_PROVIDER: bool = False

    # Credenciales y modelos de OpenRouter
    OPENROUTER_API_KEY: Optional[str] = None
    OPENROUTER_API_KEY_FILE: Optional[str] = None
    OPENROUTER_BASE_URL: str = "https://openrouter.ai/api/v1"
    OPENROUTER_MODEL_PAID: str = "google/gemma-2-27b-it"
    OPENROUTER_MODEL_FREE: str = "google/gemma-2-9b-it:free"

    # Credenciales y modelo de Gemini
    GEMINI_API_KEY: Optional[str] = None
    GEMINI_API_KEY_FILE: Optional[str] = None
    GEMINI_MODEL: str = "gemini-1.5-flash"

    # Configuración de Ollama local (ADR-10)
    OLLAMA_BASE_URL: str = "http://localhost:11434/v1"
    OLLAMA_MODEL: str = "gemma2:9b"

    # Resiliencia y límites (RNF-FIA-02, RNF-SEG-08)
    LLM_TIMEOUT_SECONDS: float = 60.0
    LLM_MAX_RETRIES: int = 2
    MAX_TOKENS_PER_REQUEST: int = 2048
    REQUESTS_PER_MINUTE_LIMIT: int = 60

    # Versión de plantilla de prompt (RNF-SEG-06)
    PROMPT_VERSION: str = "v1.0"

    # Servidor FastAPI
    HOST: str = "0.0.0.0"
    PORT: int = 8000

    def get_openrouter_api_key(self) -> Optional[str]:
        """Obtiene la clave de OpenRouter desde secreto de archivo o variable directa."""
        if self.OPENROUTER_API_KEY_FILE:
            secret_path = Path(self.OPENROUTER_API_KEY_FILE)
            if secret_path.is_file():
                return secret_path.read_text(encoding="utf-8").strip()
        return self.OPENROUTER_API_KEY

    def get_gemini_api_key(self) -> Optional[str]:
        """Obtiene la clave de Gemini desde secreto de archivo o variable directa."""
        if self.GEMINI_API_KEY_FILE:
            secret_path = Path(self.GEMINI_API_KEY_FILE)
            if secret_path.is_file():
                return secret_path.read_text(encoding="utf-8").strip()
        return self.GEMINI_API_KEY

    @property
    def is_production(self) -> bool:
        """Determina si el entorno actual es considerado producción."""
        return self.APP_ENV.strip().lower() in ("prod", "production")


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    """Devuelve la instancia singleton en caché de Settings."""
    return Settings()
