"""Pruebas directas de los criterios de aceptación de S1-18:

Criterio 1: Con openrouter_free y un contexto de ejemplo devuelve JSON válido con afirmaciones tipadas.
Criterio 2: El candado bloquea APP_ENV=prod.
"""

import json
import pytest
import httpx
from httpx import ASGITransport, AsyncClient
from src.config import Settings, get_settings
from src.main import app
from src.models.schemas import ClaimType, ExplainRequest
from src.security_lock import SecurityLockViolationError, enforce_security_lock


@pytest.mark.asyncio
async def test_s1_18_acceptance_openrouter_free_returns_valid_json(sample_request: ExplainRequest):
    """Criterio de aceptación 1: Con openrouter_free y un contexto de ejemplo devuelve JSON válido."""
    dev_settings = Settings(
        APP_ENV="dev",
        LLM_PROFILE="openrouter_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
        OPENROUTER_API_KEY="sk-test-acceptance-key",
    )

    # Simular la respuesta de OpenRouter (Gemma 2 9B :free)
    llm_output_json = {
        "summary": "Se detectó Parameter Sniffing severo y lectura residual en la consulta q-tpc-h-01.",
        "claims": [
            {
                "claim_type": "OBSERVACION",
                "statement": "El operador Index Seek estimó 1 fila pero ejecutó 45,000 filas reales (ratio 45,000x).",
                "finding_ids": ["find-hash-reg01-001"],
                "kb_fragment_id": None,
            },
            {
                "claim_type": "INTERPRETACION",
                "statement": "El optimizador de consultas compiló un plan para un parámetro infrecuente según el mecanismo documentado en KB-REG01-01.",
                "finding_ids": ["find-hash-reg01-001"],
                "kb_fragment_id": "KB-REG01-01",
            },
            {
                "claim_type": "RECOMENDACION",
                "statement": "Se recomienda al DBA evaluar la sugerencia OPTION (OPTIMIZE FOR UNKNOWN) o recompilación condicional.",
                "finding_ids": ["find-hash-reg01-001"],
                "kb_fragment_id": "KB-REG01-01",
            },
            {
                "claim_type": "OBSERVACION",
                "statement": "Se escanearon 500,000 filas en Clustered Index Scan produciendo solo 12 filas tras el predicado residual.",
                "finding_ids": ["find-hash-reg04-002"],
                "kb_fragment_id": None,
            },
            {
                "claim_type": "INTERPRETACION",
                "statement": "El predicado residual consume CPU y lecturas lógicas por ausencia de índice compuesto según KB-REG04-01.",
                "finding_ids": ["find-hash-reg04-002"],
                "kb_fragment_id": "KB-REG04-01",
            },
            {
                "claim_type": "RECOMENDACION",
                "statement": "Crear un índice que cubra las columnas l_shipdate y l_orderkey para permitir una búsqueda directa.",
                "finding_ids": ["find-hash-reg04-002"],
                "kb_fragment_id": "KB-REG04-01",
            },
        ],
    }

    def mock_openrouter_transport(req: httpx.Request):
        return httpx.Response(
            200,
            json={
                "choices": [
                    {
                        "message": {
                            "content": json.dumps(llm_output_json)
                        }
                    }
                ]
            },
        )

    # Inyectar el mock de OpenRouter
    mock_client = httpx.AsyncClient(transport=httpx.MockTransport(mock_openrouter_transport))

    # Sobrescribir dependencias en la app FastAPI
    from src.main import get_provider
    from src.providers.openrouter import OpenRouterProvider

    provider = OpenRouterProvider(dev_settings, client=mock_client)
    app.dependency_overrides[get_settings] = lambda: dev_settings
    app.dependency_overrides[get_provider] = lambda: provider

    try:
        transport = ASGITransport(app=app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            resp = await client.post("/explain", json=sample_request.model_dump())
            assert resp.status_code == 200, f"Error devuelto: {resp.text}"

            data = resp.json()
            assert data["query_id"] == "q-tpc-h-01"
            assert "Parameter Sniffing" in data["summary"]
            assert len(data["claims"]) == 6

            # Validar esquema de afirmaciones (RF-IA-02)
            for claim in data["claims"]:
                assert claim["claim_type"] in [
                    ClaimType.OBSERVACION.value,
                    ClaimType.INTERPRETACION.value,
                    ClaimType.RECOMENDACION.value,
                ]
                assert len(claim["finding_ids"]) >= 1
                if claim["claim_type"] in [ClaimType.INTERPRETACION.value, ClaimType.RECOMENDACION.value]:
                    assert claim["kb_fragment_id"] in ["KB-REG01-01", "KB-REG04-01"]

            # Metadatos del perfil de OpenRouter
            meta = data["metadata"]
            assert meta["llm_profile"] == "openrouter_free"
            assert meta["model"] == dev_settings.OPENROUTER_MODEL_FREE
            assert meta["served_by"] == "openrouter.ai"
            assert meta["data_policy"] == "provider_may_retain"
    finally:
        app.dependency_overrides.clear()
        await mock_client.aclose()


def test_s1_18_acceptance_candado_bloquea_prod():
    """Criterio de aceptación 2: El candado bloquea APP_ENV=prod con perfiles gratuitos."""
    # 1. Con openrouter_free
    prod_openrouter_free = Settings(
        APP_ENV="prod",
        LLM_PROFILE="openrouter_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
    )
    with pytest.raises(SecurityLockViolationError) as exc_info:
        enforce_security_lock(prod_openrouter_free)
    assert "CANDADO DE DATOS" in str(exc_info.value)
    assert "estrictamente prohibido en entorno de producción" in str(exc_info.value)

    # 2. Con gemini_free
    prod_gemini_free = Settings(
        APP_ENV="prod",
        LLM_PROFILE="gemini_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
    )
    with pytest.raises(SecurityLockViolationError) as exc_info:
        enforce_security_lock(prod_gemini_free)
    assert "estrictamente prohibido en entorno de producción" in str(exc_info.value)
