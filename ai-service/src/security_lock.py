"""Candado de seguridad de datos para proveedores de LLM (ADR-10, RES-06, RNF-SEG-05)."""

from typing import Dict, Set
from src.config import Settings


class SecurityLockViolationError(RuntimeError):
    """Excepción lanzada cuando una configuración viola el candado de seguridad de datos."""
    pass


# Políticas de datos declaradas por perfil según ADR-10 (§8.3)
PROFILE_DATA_POLICIES: Dict[str, str] = {
    "openrouter_paid": "no_training_zero_retention",
    "openrouter_free": "provider_may_retain",
    "gemini_free": "google_product_improvement",
    "ollama_local": "local_only_no_egress",
    "mock": "mock_deterministic",
}

# Perfiles cuya política permite uso o retención externa de datos
DATA_SHARING_PROFILES: Set[str] = {
    "openrouter_free",
    "gemini_free",
}

# Entornos reconocidos
PROD_ENVIRONMENTS: Set[str] = {"prod", "production"}
DEV_LAB_ENVIRONMENTS: Set[str] = {"dev", "lab", "development", "test"}


def get_data_policy(profile: str) -> str:
    """Devuelve la política de datos asociada a un perfil de LLM."""
    normalized_profile = profile.strip().lower()
    return PROFILE_DATA_POLICIES.get(normalized_profile, "unknown")


def enforce_security_lock(settings: Settings) -> None:
    """Aplica el candado en el código exigido por ADR-10 y RES-06.

    Reglas:
    1. Un perfil que comparte datos (openrouter_free, gemini_free) NUNCA puede
       arrancar en producción (APP_ENV=prod), incluso si ALLOW_DATA_SHARING_PROVIDER=true.
    2. En entornos dev o lab, un perfil que comparte datos exige explícitamente
       ALLOW_DATA_SHARING_PROVIDER=true.
    3. Perfiles seguros (openrouter_paid, ollama_local, mock) pueden ejecutarse en
       cualquier entorno.

    Lanza:
        SecurityLockViolationError: Si la configuración infringe el candado.
    """
    profile = settings.LLM_PROFILE.strip().lower()
    env = settings.APP_ENV.strip().lower()

    if profile not in PROFILE_DATA_POLICIES:
        raise SecurityLockViolationError(
            f"Perfil de LLM desconocido: '{settings.LLM_PROFILE}'. "
            f"Opciones válidas: {list(PROFILE_DATA_POLICIES.keys())}"
        )

    # Candado estricto en producción: NUNCA permitir perfiles que comparten datos
    if env in PROD_ENVIRONMENTS:
        if profile in DATA_SHARING_PROFILES:
            raise SecurityLockViolationError(
                f"[CANDADO DE DATOS - ADR-10 / RES-06 BLOQUEADO] "
                f"El perfil '{profile}' comparte datos con el proveedor y está estrictamente "
                f"prohibido en entorno de producción (APP_ENV={settings.APP_ENV}). "
                f"Utilice 'openrouter_paid' o 'ollama_local'."
            )

    # Candado en entornos de desarrollo y laboratorio: exige confirmación explícita
    if profile in DATA_SHARING_PROFILES:
        if not settings.ALLOW_DATA_SHARING_PROVIDER:
            raise SecurityLockViolationError(
                f"[CANDADO DE DATOS - ADR-10] "
                f"El perfil '{profile}' puede retener o usar datos para entrenamiento. "
                f"Para usarlo en entorno '{settings.APP_ENV}', debe establecer explícitamente "
                f"ALLOW_DATA_SHARING_PROVIDER=true en su configuración."
            )
