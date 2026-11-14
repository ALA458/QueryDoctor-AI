"""Pruebas del candado de seguridad de datos (ADR-10, RES-06, RNF-SEG-05)."""

import pytest
from src.config import Settings
from src.security_lock import (
    SecurityLockViolationError,
    enforce_security_lock,
    get_data_policy,
)


def test_prod_blocks_openrouter_free_even_with_flag():
    """En producción, openrouter_free DEBE ser bloqueado aunque la bandera sea True (ADR-10 / RES-06)."""
    settings = Settings(
        APP_ENV="prod",
        LLM_PROFILE="openrouter_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
    )
    with pytest.raises(SecurityLockViolationError) as exc_info:
        enforce_security_lock(settings)
    assert "CANDADO DE DATOS" in str(exc_info.value)
    assert "producción" in str(exc_info.value)


def test_prod_blocks_gemini_free_even_with_flag():
    """En producción, gemini_free DEBE ser bloqueado aunque la bandera sea True (ADR-10 / RES-06)."""
    settings = Settings(
        APP_ENV="production",
        LLM_PROFILE="gemini_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
    )
    with pytest.raises(SecurityLockViolationError) as exc_info:
        enforce_security_lock(settings)
    assert "estrictamente prohibido en entorno de producción" in str(exc_info.value)


def test_prod_allows_openrouter_paid():
    """En producción, openrouter_paid es permitido porque no entrena ni retiene datos."""
    settings = Settings(
        APP_ENV="prod",
        LLM_PROFILE="openrouter_paid",
        ALLOW_DATA_SHARING_PROVIDER=False,
    )
    # No debe lanzar excepción
    enforce_security_lock(settings)


def test_prod_allows_ollama_local():
    """En producción, ollama_local es permitido porque los datos nunca salen del host."""
    settings = Settings(
        APP_ENV="prod",
        LLM_PROFILE="ollama_local",
        ALLOW_DATA_SHARING_PROVIDER=False,
    )
    enforce_security_lock(settings)


def test_prod_allows_mock():
    """En producción o pruebas, mock es permitido para validaciones determinísticas."""
    settings = Settings(
        APP_ENV="prod",
        LLM_PROFILE="mock",
        ALLOW_DATA_SHARING_PROVIDER=False,
    )
    enforce_security_lock(settings)


def test_dev_blocks_openrouter_free_without_flag():
    """En dev, openrouter_free exige ALLOW_DATA_SHARING_PROVIDER=true."""
    settings = Settings(
        APP_ENV="dev",
        LLM_PROFILE="openrouter_free",
        ALLOW_DATA_SHARING_PROVIDER=False,
    )
    with pytest.raises(SecurityLockViolationError) as exc_info:
        enforce_security_lock(settings)
    assert "ALLOW_DATA_SHARING_PROVIDER=true" in str(exc_info.value)


def test_dev_allows_openrouter_free_with_flag():
    """En dev, openrouter_free funciona si ALLOW_DATA_SHARING_PROVIDER=true."""
    settings = Settings(
        APP_ENV="dev",
        LLM_PROFILE="openrouter_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
    )
    enforce_security_lock(settings)


def test_lab_blocks_gemini_free_without_flag():
    """En lab, gemini_free exige ALLOW_DATA_SHARING_PROVIDER=true."""
    settings = Settings(
        APP_ENV="lab",
        LLM_PROFILE="gemini_free",
        ALLOW_DATA_SHARING_PROVIDER=False,
    )
    with pytest.raises(SecurityLockViolationError):
        enforce_security_lock(settings)


def test_lab_allows_gemini_free_with_flag():
    """En lab, gemini_free funciona si la bandera explícita está activa."""
    settings = Settings(
        APP_ENV="lab",
        LLM_PROFILE="gemini_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
    )
    enforce_security_lock(settings)


def test_unknown_profile_raises_error():
    """Un perfil de LLM no reconocido debe lanzar SecurityLockViolationError."""
    settings = Settings(
        APP_ENV="dev",
        LLM_PROFILE="unknown_cloud_provider",
    )
    with pytest.raises(SecurityLockViolationError) as exc_info:
        enforce_security_lock(settings)
    assert "Perfil de LLM desconocido" in str(exc_info.value)


def test_data_policy_reporting():
    """Verifica que cada perfil informe la política de datos exacta de ADR-10."""
    assert get_data_policy("openrouter_paid") == "no_training_zero_retention"
    assert get_data_policy("openrouter_free") == "provider_may_retain"
    assert get_data_policy("gemini_free") == "google_product_improvement"
    assert get_data_policy("ollama_local") == "local_only_no_egress"
    assert get_data_policy("mock") == "mock_deterministic"
    assert get_data_policy("inexistente") == "unknown"
