"""Configuración y accesorios (fixtures) comunes para las pruebas de ai-service."""

import sys
from pathlib import Path

# Asegurar que el directorio ai-service esté en sys.path
ai_service_root = Path(__file__).resolve().parent.parent
if str(ai_service_root) not in sys.path:
    sys.path.insert(0, str(ai_service_root))

import pytest
from src.config import Settings
from src.models.schemas import ExplainRequest, FindingInput, KbFragmentInput


@pytest.fixture
def sample_findings() -> list[FindingInput]:
    """Lista de hallazgos determinísticos de ejemplo (REG-01 y REG-04)."""
    return [
        FindingInput(
            finding_id="find-hash-reg01-001",
            rule_id="REG-01",
            rule_name="Compilación con parámetros atípicos (Parameter Sniffing)",
            evidence_class="ESTIMADA",
            severity="ALTA",
            target_nodes=["Index Seek", "Nested Loops"],
            metrics={
                "estimated_rows": 1,
                "actual_rows": 45000,
                "ratio": 45000.0,
            },
            description="Discrepancia crítica entre filas estimadas (1) y reales (45,000) por compilación con parámetro atípico.",
        ),
        FindingInput(
            finding_id="find-hash-reg04-002",
            rule_id="REG-04",
            rule_name="Búsqueda residual en clave no indexada",
            evidence_class="EXACTA",
            severity="MEDIA",
            target_nodes=["Clustered Index Scan"],
            metrics={
                "actual_rows": 12,
                "scanned_rows": 500000,
            },
            description="Filtro no sargable evaluado como predicado residual tras escaneo completo.",
        ),
    ]


@pytest.fixture
def sample_kb_fragments() -> list[KbFragmentInput]:
    """Fragmentos curados de la base de conocimiento de ejemplo."""
    return [
        KbFragmentInput(
            fragment_id="KB-REG01-01",
            rule_id="REG-01",
            title="Mecanismo de Parameter Sniffing en SQL Server",
            content="SQL Server compila y almacena en caché un plan optimizado para los valores suministrados en la primera ejecución.",
            source_citation="Microsoft Learn - Parameter Sniffing",
        ),
        KbFragmentInput(
            fragment_id="KB-REG04-01",
            rule_id="REG-04",
            title="Predicados residuales en operadores de escaneo",
            content="Un predicado residual filtra filas después de leerlas, aumentando las lecturas lógicas y el consumo de CPU.",
            source_citation="Microsoft Learn - Understanding Predicates and Scans",
        ),
    ]


@pytest.fixture
def sample_request(sample_findings, sample_kb_fragments) -> ExplainRequest:
    """Solicitud de diagnóstico completa de ejemplo."""
    return ExplainRequest(
        query_id="q-tpc-h-01",
        sql_text="SELECT l_orderkey, SUM(l_extendedprice) FROM lineitem WHERE l_shipdate >= @P0 GROUP BY l_orderkey",
        findings=sample_findings,
        kb_fragments=sample_kb_fragments,
        metrics={
            "avg_duration_ms": 3200.5,
            "avg_cpu_time_ms": 2850.0,
            "avg_logical_io_reads": 142000,
            "execution_count": 85,
        },
        user_role="DBA",
        correlation_id="corr-test-123",
    )
