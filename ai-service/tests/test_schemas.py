"""Pruebas del esquema formal de afirmaciones y modelos Pydantic (RF-IA-01, RF-IA-02)."""

import pytest
from pydantic import ValidationError
from src.models.schemas import (
    ClaimOutput,
    ClaimType,
    ExplainMetadata,
    ExplainRequest,
    ExplainResponse,
    FindingInput,
    KbFragmentInput,
)


def test_valid_observation_claim():
    """OBSERVACION válida con finding_ids y sin fragment_id obligatorio."""
    claim = ClaimOutput(
        claim_type=ClaimType.OBSERVACION,
        statement="Se observaron 45,000 filas leídas por el escaneo de índice.",
        finding_ids=["find-001"],
        kb_fragment_id=None,
    )
    assert claim.claim_type == ClaimType.OBSERVACION
    assert claim.finding_ids == ["find-001"]
    assert claim.kb_fragment_id is None


def test_valid_interpretation_claim():
    """INTERPRETACION válida exige finding_ids Y kb_fragment_id (RF-IA-02)."""
    claim = ClaimOutput(
        claim_type=ClaimType.INTERPRETACION,
        statement="El plan se compiló con un parámetro atípico causando Parameter Sniffing.",
        finding_ids=["find-001"],
        kb_fragment_id="KB-REG01-01",
    )
    assert claim.claim_type == ClaimType.INTERPRETACION
    assert claim.kb_fragment_id == "KB-REG01-01"


def test_valid_recommendation_claim():
    """RECOMENDACION válida exige finding_ids Y kb_fragment_id (RF-IA-02)."""
    claim = ClaimOutput(
        claim_type=ClaimType.RECOMENDACION,
        statement="Considere usar OPTION (OPTIMIZE FOR UNKNOWN) tras revisión técnica.",
        finding_ids=["find-001"],
        kb_fragment_id="KB-REG01-01",
    )
    assert claim.claim_type == ClaimType.RECOMENDACION
    assert claim.kb_fragment_id == "KB-REG01-01"


def test_claim_without_finding_ids_fails():
    """Cualquier afirmación sin finding_ids debe fallar la validación (RF-IA-02)."""
    with pytest.raises(ValidationError) as exc_info:
        ClaimOutput(
            claim_type=ClaimType.OBSERVACION,
            statement="Afirmación sin evidencia.",
            finding_ids=[],
        )
    assert "finding_id" in str(exc_info.value)


def test_interpretation_without_kb_fragment_fails():
    """INTERPRETACION sin kb_fragment_id debe fallar (RF-IA-02)."""
    with pytest.raises(ValidationError) as exc_info:
        ClaimOutput(
            claim_type=ClaimType.INTERPRETACION,
            statement="Causa sin sustento documental.",
            finding_ids=["find-001"],
            kb_fragment_id=None,
        )
    assert "kb_fragment_id" in str(exc_info.value)


def test_recommendation_without_kb_fragment_fails():
    """RECOMENDACION sin kb_fragment_id debe fallar (RF-IA-02)."""
    with pytest.raises(ValidationError) as exc_info:
        ClaimOutput(
            claim_type=ClaimType.RECOMENDACION,
            statement="Recomendación sin fragmento curado.",
            finding_ids=["find-001"],
            kb_fragment_id="",
        )
    assert "kb_fragment_id" in str(exc_info.value)


def test_explain_request_requires_at_least_one_finding():
    """ExplainRequest sin hallazgos debe fallar (RF-IA-01: 'únicamente cuando exista al menos un hallazgo')."""
    with pytest.raises(ValidationError):
        ExplainRequest(
            query_id="q-001",
            sql_text="SELECT 1",
            findings=[],
        )


def test_explain_response_serialization(sample_request):
    """ExplainResponse serializa y deserializa limpiamente en JSON conforme al esquema."""
    metadata = ExplainMetadata(
        llm_profile="openrouter_free",
        model="google/gemma-2-9b-it:free",
        prompt_version="v1.0",
        served_by="openrouter.ai",
        data_policy="provider_may_retain",
        execution_time_seconds=1.234,
    )
    claims = [
        ClaimOutput(
            claim_type=ClaimType.OBSERVACION,
            statement="Escaneo residual en Clustered Index Scan.",
            finding_ids=["find-hash-reg04-002"],
        ),
        ClaimOutput(
            claim_type=ClaimType.INTERPRETACION,
            statement="La falta de índice sargable obliga a filtrar filas tras lectura masiva.",
            finding_ids=["find-hash-reg04-002"],
            kb_fragment_id="KB-REG04-01",
        ),
        ClaimOutput(
            claim_type=ClaimType.RECOMENDACION,
            statement="Crear un índice no agrupado sobre las columnas filtradas.",
            finding_ids=["find-hash-reg04-002"],
            kb_fragment_id="KB-REG04-01",
        ),
    ]

    response = ExplainResponse(
        query_id=sample_request.query_id,
        summary="Diagnóstico de escaneo residual en consulta crítica.",
        claims=claims,
        metadata=metadata,
    )

    json_str = response.model_dump_json()
    assert "find-hash-reg04-002" in json_str
    assert "KB-REG04-01" in json_str
    assert "openrouter_free" in json_str

    # Deserialización inversa
    reloaded = ExplainResponse.model_validate_json(json_str)
    assert len(reloaded.claims) == 3
    assert reloaded.claims[1].claim_type == ClaimType.INTERPRETACION
