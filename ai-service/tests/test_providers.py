"""Pruebas unitarias de adaptadores de proveedores LLM (ADR-10, RNF-FIA-02)."""

import json
import pytest
import httpx
from src.config import Settings
from src.models.schemas import ClaimType, ExplainRequest
from src.providers.factory import get_llm_provider
from src.providers.gemini import GeminiProvider
from src.providers.mock import MockProvider
from src.providers.ollama import OllamaProvider
from src.providers.openrouter import OpenRouterProvider


@pytest.mark.asyncio
async def test_mock_provider_generates_valid_response(sample_request: ExplainRequest):
    """El MockProvider genera una respuesta completa válida conforme al esquema formal."""
    settings = Settings(APP_ENV="dev", LLM_PROFILE="mock")
    provider = MockProvider(settings)

    response = await provider.generate_explanation(sample_request)

    assert response.query_id == sample_request.query_id
    assert len(response.claims) >= len(sample_request.findings)
    assert response.metadata.llm_profile == "mock"
    assert response.metadata.served_by == "mock-engine"
    assert response.metadata.data_policy == "mock_deterministic"

    # Verificar que cada afirmación cumpla con RF-IA-02
    for claim in response.claims:
        assert len(claim.finding_ids) > 0
        if claim.claim_type in (ClaimType.INTERPRETACION, ClaimType.RECOMENDACION):
            assert claim.kb_fragment_id is not None
            assert claim.kb_fragment_id.startswith("KB-")


def test_base_provider_accepts_clean_fenced_json(sample_request: ExplainRequest):
    """Acepta cercado markdown ```json puro cuando el esquema y todos los IDs son válidos."""
    provider = MockProvider(Settings(APP_ENV="dev", LLM_PROFILE="mock"))
    raw_text = "```json\n" + json.dumps(
        {
            "summary": "Diagnóstico estructurado dentro de bloque json.",
            "claims": [
                {
                    "claim_type": "INTERPRETACION",
                    "statement": "El parámetro sesgado induce estimaciones erróneas.",
                    "finding_ids": [sample_request.findings[0].finding_id],
                    "kb_fragment_id": sample_request.kb_fragments[0].fragment_id,
                }
            ],
        }
    ) + "\n   ```"

    response = provider.parse_and_validate_llm_json(
        raw_text=raw_text,
        request=sample_request,
        model_name="test-model",
        served_by="test-provider",
        start_time=0.0,
    )

    assert response.summary == "Diagnóstico estructurado dentro de bloque json."
    assert len(response.claims) == 1
    assert response.claims[0].claim_type == ClaimType.INTERPRETACION
    assert response.claims[0].finding_ids == [sample_request.findings[0].finding_id]
    assert response.claims[0].kb_fragment_id == sample_request.kb_fragments[0].fragment_id


@pytest.mark.parametrize(
    ("raw_payload", "error_pattern"),
    [
        (
            {
                "summary": "Resumen válido.",
                "claims": [
                    {
                        "claim_type": "UNKNOWN",
                        "statement": "Afirmación inválida.",
                        "finding_ids": ["find-hash-reg01-001"],
                        "kb_fragment_id": "KB-REG01-01",
                    }
                ],
            },
            "no permitido",
        ),
        (
            {
                "summary": "Resumen válido.",
                "claims": [
                    {
                        "claim_type": "OBSERVACION",
                        "statement": "Hallazgo inventado.",
                        "finding_ids": ["invented-finding"],
                    }
                ],
            },
            "finding_id inexistente",
        ),
        (
            {
                "summary": "Resumen válido.",
                "claims": [
                    {
                        "claim_type": "INTERPRETACION",
                        "statement": "Fragmento inexistente.",
                        "finding_ids": ["find-hash-reg01-001"],
                        "kb_fragment_id": "unknown-fragment",
                    }
                ],
            },
            "kb_fragment_id inexistente",
        ),
        (
            {
                "summary": "Resumen válido.",
                "claims": [
                    {
                        "claim_type": "RECOMENDACION",
                        "statement": "Sin fragmento.",
                        "finding_ids": ["find-hash-reg01-001"],
                    }
                ],
            },
            "requiere obligatoriamente 'kb_fragment_id'",
        ),
        (
            {
                "summary": "",
                "claims": [
                    {
                        "claim_type": "OBSERVACION",
                        "statement": "Sin resumen.",
                        "finding_ids": ["find-hash-reg01-001"],
                    }
                ],
            },
            "summary",
        ),
    ],
)
def test_base_provider_strictly_rejects_manipulated_or_incomplete_claims(
    sample_request: ExplainRequest,
    raw_payload: dict,
    error_pattern: str,
):
    """El parseador estricto no repara silenciosamente tipos, hallazgos ni fragmentos inventados (RNF-SEG-06)."""
    provider = MockProvider(Settings(APP_ENV="dev", LLM_PROFILE="mock"))

    with pytest.raises(ValueError, match=error_pattern):
        provider.parse_and_validate_llm_json(
            raw_text=json.dumps(raw_payload),
            request=sample_request,
            model_name="test-model",
            served_by="test-provider",
            start_time=0.0,
        )


@pytest.mark.parametrize(
    ("claims", "error"),
    [
        ([], "lista no vacía de 'claims'"),
        ([None], "debe ser un objeto JSON válido"),
    ],
)
def test_base_provider_rejects_empty_valid_claims(
    sample_request: ExplainRequest,
    claims: list[object],
    error: str,
):
    provider = MockProvider(Settings(APP_ENV="dev", LLM_PROFILE="mock"))

    with pytest.raises(ValueError, match=error):
        provider.parse_and_validate_llm_json(
            raw_text=json.dumps({"summary": "Resumen válido.", "claims": claims}),
            request=sample_request,
            model_name="test-model",
            served_by="test-provider",
            start_time=0.0,
        )


@pytest.mark.asyncio
async def test_openrouter_provider_missing_key_raises_value_error(sample_request: ExplainRequest):
    """OpenRouter sin clave configurada lanza ValueError inmediatamente."""
    settings = Settings(
        APP_ENV="dev",
        LLM_PROFILE="openrouter_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
        OPENROUTER_API_KEY=None,
    )
    provider = OpenRouterProvider(settings)
    with pytest.raises(ValueError) as exc_info:
        await provider.generate_explanation(sample_request)
    assert "Clave de API de OpenRouter no configurada" in str(exc_info.value)


@pytest.mark.asyncio
async def test_openrouter_paid_sends_privacy_headers_and_zdr(sample_request: ExplainRequest):
    """OpenRouter pago DEBE enviar la política de privacidad zdr y data_collection: deny (ADR-10 §8.3)."""
    settings = Settings(
        APP_ENV="prod",
        LLM_PROFILE="openrouter_paid",
        OPENROUTER_API_KEY="sk-test-paid-key",
    )

    captured_request = {}

    def mock_transport(req: httpx.Request):
        captured_request["headers"] = dict(req.headers)
        captured_request["body"] = json.loads(req.read().decode())

        mock_payload = {
            "choices": [
                {
                    "message": {
                        "content": json.dumps({
                            "summary": "Consulta con Parameter Sniffing severo.",
                            "claims": [
                                {
                                    "claim_type": "OBSERVACION",
                                    "statement": "Discrepancia entre estimación y filas reales.",
                                    "finding_ids": ["find-hash-reg01-001"],
                                    "kb_fragment_id": None,
                                },
                                {
                                    "claim_type": "INTERPRETACION",
                                    "statement": "El plan se compiló con un parámetro sesgado según KB-REG01-01.",
                                    "finding_ids": ["find-hash-reg01-001"],
                                    "kb_fragment_id": "KB-REG01-01",
                                },
                                {
                                    "claim_type": "RECOMENDACION",
                                    "statement": "Sugerir revisión de OPTION (RECOMPILE) al DBA.",
                                    "finding_ids": ["find-hash-reg01-001"],
                                    "kb_fragment_id": "KB-REG01-01",
                                },
                            ],
                        })
                    }
                }
            ]
        }
        return httpx.Response(200, json=mock_payload)

    client = httpx.AsyncClient(transport=httpx.MockTransport(mock_transport))
    provider = OpenRouterProvider(settings, client=client)

    response = await provider.generate_explanation(sample_request)

    # Validar que se enviaron los parámetros de privacidad de ADR-10
    body = captured_request["body"]
    assert "provider" in body
    assert body["provider"]["zdr"] is True
    assert body["provider"]["data_collection"] == "deny"
    assert response.metadata.llm_profile == "openrouter_paid"
    assert response.metadata.data_policy == "no_training_zero_retention"
    assert len(response.claims) == 3


@pytest.mark.asyncio
async def test_openrouter_retries_on_transient_failure(sample_request: ExplainRequest):
    """OpenRouter reintenta automáticamente ante un error 503 transitorio (RNF-FIA-02)."""
    settings = Settings(
        APP_ENV="dev",
        LLM_PROFILE="openrouter_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
        OPENROUTER_API_KEY="sk-test-free-key",
        LLM_MAX_RETRIES=2,
    )

    attempt_count = 0

    def mock_transport(req: httpx.Request):
        nonlocal attempt_count
        attempt_count += 1
        if attempt_count == 1:
            return httpx.Response(503, json={"error": "Service Temporarily Unavailable"})

        mock_payload = {
            "choices": [
                {
                    "message": {
                        "content": json.dumps({
                            "summary": "Explicación tras reintento exitoso.",
                            "claims": [
                                {
                                    "claim_type": "OBSERVACION",
                                    "statement": "Escaneo residual detectado.",
                                    "finding_ids": ["find-hash-reg04-002"],
                                }
                            ],
                        })
                    }
                }
            ]
        }
        return httpx.Response(200, json=mock_payload)

    client = httpx.AsyncClient(transport=httpx.MockTransport(mock_transport))
    provider = OpenRouterProvider(settings, client=client)

    response = await provider.generate_explanation(sample_request)

    assert attempt_count == 2
    assert response.summary == "Explicación tras reintento exitoso."


@pytest.mark.asyncio
async def test_gemini_provider_retries_and_parses_response(
    sample_request: ExplainRequest,
):
    """Gemini reintenta ante un 503 y valida la respuesta estructurada."""
    settings = Settings(
        APP_ENV="dev",
        LLM_PROFILE="gemini_free",
        ALLOW_DATA_SHARING_PROVIDER=True,
        GEMINI_API_KEY="test-gemini-key",
        LLM_MAX_RETRIES=1,
    )
    attempt_count = 0
    captured_request = {}
    llm_response = json.dumps(
        {
            "summary": "Respuesta de Gemini.",
            "claims": [
                {
                    "claim_type": "OBSERVACION",
                    "statement": "Se encontró REG-01.",
                    "finding_ids": [sample_request.findings[0].finding_id],
                    "kb_fragment_id": None,
                }
            ],
        }
    )

    def mock_transport(req: httpx.Request):
        nonlocal attempt_count
        attempt_count += 1
        if attempt_count == 1:
            return httpx.Response(
                503, json={"error": "Service Temporarily Unavailable"}
            )

        captured_request["url"] = str(req.url)
        captured_request["body"] = json.loads(req.content)
        return httpx.Response(
            200,
            json={
                "candidates": [{"content": {"parts": [{"text": llm_response}]}}]
            },
        )

    async with httpx.AsyncClient(
        transport=httpx.MockTransport(mock_transport)
    ) as client:
        provider = GeminiProvider(settings, client=client)
        response = await provider.generate_explanation(sample_request)

    assert attempt_count == 2
    assert f"models/{settings.GEMINI_MODEL}:generateContent" in captured_request["url"]
    assert captured_request["url"].endswith("?key=test-gemini-key")
    assert (
        captured_request["body"]["generationConfig"]["responseMimeType"]
        == "application/json"
    )
    assert response.query_id == sample_request.query_id
    assert response.summary == "Respuesta de Gemini."
    assert response.metadata.served_by == "google-gemini-api"


@pytest.mark.asyncio
async def test_factory_returns_correct_providers():
    """La fábrica de proveedores instancia el adaptador correspondiente según el perfil."""
    s_openrouter = Settings(APP_ENV="dev", LLM_PROFILE="openrouter_free", ALLOW_DATA_SHARING_PROVIDER=True)
    assert isinstance(get_llm_provider(s_openrouter), OpenRouterProvider)

    s_mock = Settings(APP_ENV="dev", LLM_PROFILE="mock")
    assert isinstance(get_llm_provider(s_mock), MockProvider)

    s_gemini = Settings(APP_ENV="dev", LLM_PROFILE="gemini_free", ALLOW_DATA_SHARING_PROVIDER=True)
    assert isinstance(get_llm_provider(s_gemini), GeminiProvider)

    s_ollama = Settings(APP_ENV="dev", LLM_PROFILE="ollama_local")
    assert isinstance(get_llm_provider(s_ollama), OllamaProvider)
