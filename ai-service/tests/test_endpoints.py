"""Pruebas de integración de endpoints FastAPI (RF-IA-02, ADR-02, ADR-10)."""

import pytest
from httpx import ASGITransport, AsyncClient
from src.config import Settings, get_settings
from src.main import app
from src.models.schemas import ExplainRequest
from src.security_lock import SecurityLockViolationError, enforce_security_lock


@pytest.fixture
def mock_settings():
    """Configuración predeterminada de prueba usando perfil mock."""
    return Settings(
        APP_ENV="dev",
        LLM_PROFILE="mock",
        ALLOW_DATA_SHARING_PROVIDER=False,
    )


@pytest.mark.asyncio
async def test_health_endpoint():
    """El endpoint /health responde 200 con status healthy."""
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "healthy"}


@pytest.mark.asyncio
async def test_info_endpoint(mock_settings):
    """El endpoint /info devuelve la metadata y política de datos del servicio."""
    app.dependency_overrides[get_settings] = lambda: mock_settings
    try:
        transport = ASGITransport(app=app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            response = await client.get("/info")
            assert response.status_code == 200
            data = response.json()
            assert data["app_name"] == "QueryDoctor-AI-Service"
            assert data["llm_profile"] == "mock"
            assert data["data_policy"] == "mock_deterministic"
            assert data["prompt_version"] == "v1.0"
    finally:
        app.dependency_overrides.clear()


@pytest.mark.asyncio
async def test_explain_endpoint_with_mock_profile(sample_request: ExplainRequest, mock_settings):
    """El endpoint /explain con perfil mock devuelve JSON válido y afirma los hallazgos."""
    app.dependency_overrides[get_settings] = lambda: mock_settings
    try:
        transport = ASGITransport(app=app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            response = await client.post("/explain", json=sample_request.model_dump())
            assert response.status_code == 200
            data = response.json()

            assert data["query_id"] == sample_request.query_id
            assert "summary" in data
            assert len(data["claims"]) > 0

            # Verificar afirmaciones
            for claim in data["claims"]:
                assert claim["claim_type"] in ["OBSERVACION", "INTERPRETACION", "RECOMENDACION"]
                assert len(claim["finding_ids"]) >= 1
                if claim["claim_type"] in ["INTERPRETACION", "RECOMENDACION"]:
                    assert claim["kb_fragment_id"] is not None
                    assert len(claim["kb_fragment_id"]) > 0

            # Metadatos
            assert data["metadata"]["llm_profile"] == "mock"
            assert data["metadata"]["served_by"] == "mock-engine"
    finally:
        app.dependency_overrides.clear()


@pytest.mark.asyncio
async def test_explain_endpoint_empty_findings_returns_422(mock_settings):
    """Una solicitud sin hallazgos es rechazada con código HTTP 422 (RF-IA-01)."""
    app.dependency_overrides[get_settings] = lambda: mock_settings
    try:
        transport = ASGITransport(app=app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            invalid_payload = {
                "query_id": "q-empty",
                "sql_text": "SELECT 1",
                "findings": [],  # Violación: min_length=1
            }
            response = await client.post("/explain", json=invalid_payload)
            assert response.status_code == 422
    finally:
        app.dependency_overrides.clear()


def test_lifespan_enforces_security_lock_on_prod_with_free_profile():
    """El candado en el ciclo de vida bloquea el inicio si APP_ENV=prod y el perfil es gratuito (ADR-10)."""
    prod_free_settings = Settings(
        APP_ENV="prod",
        LLM_PROFILE="openrouter_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
    )
    with pytest.raises(SecurityLockViolationError):
        enforce_security_lock(prod_free_settings)
