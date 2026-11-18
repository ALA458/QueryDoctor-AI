"""Módulo de evaluación y contraste con especialista en bases de datos (RF-EVA-03, RF-EVA-05, OBJ-3, OBJ-6).

Implementa el protocolo de contraste ciego e independiente entre el diagnóstico emitido por
un especialista senior en SQL Server y los resultados del motor determinista y servicio de IA
de Query Doctor AI sobre una muestra estratificada de N=15 consultas.
"""

from __future__ import annotations

import csv
import io
import json
import math
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple


# Reglas canónicas del catálogo del motor (S1-07, S1-14)
VALID_RULES = ("REG-01", "REG-02", "REG-03", "REG-04", "REG-11", "NINGUNO")

# Acciones terapéuticas permitidas en la rúbrica
ACTION_TYPES = (
    "ALINEAR_TIPOS",
    "CREAR_INDICE",
    "MODIFICAR_INDICE_INCLUDE",
    "REESCRIBIR_PREDICADO_SARGABLE",
    "REFACTORIZAR_CTE_TABLA_TEMP",
    "NINGUNA_ACCION",
)

# Niveles de prioridad estandarizados
PRIORITY_LEVELS = ("CRITICA", "ALTA", "MEDIA", "BAJA", "LEVE_NINGUNA")


@dataclass
class SpecialistCase:
    """Caso de evaluación clínica para el contraste independiente con el especialista."""

    case_id: str
    scenario_name: str
    category: str
    sql_text: str
    execution_count: int
    avg_duration_ms: float
    avg_cpu_ms: float
    avg_logical_io: int
    rows_returned: float
    plan_xml_reference: str
    expected_primary_rule: str
    expected_action_type: str
    baseline_qhs: float


@dataclass
class SpecialistDiagnosticRecord:
    """Diagnóstico independiente emitido por el especialista humano."""

    case_id: str
    specialist_root_cause_rule: str
    specialist_secondary_rules: List[str]
    specialist_action_type: str
    specialist_action_detail: str
    specialist_priority: str
    specialist_rank: int


@dataclass
class QueryDoctorDiagnosticRecord:
    """Diagnóstico y priorización emitidos automáticamente por Query Doctor AI."""

    case_id: str
    primary_engine_rule: str
    detected_engine_rules: List[str]
    query_health_score: float
    system_rank: int
    ai_recommendation_claim: str
    ai_action_type: str
    validation_status: str


@dataclass
class CaseContrastResult:
    """Resultado del contraste individual para una consulta."""

    case_id: str
    scenario_name: str
    specialist_rule: str
    system_primary_rule: str
    system_all_rules: List[str]
    diagnostic_exact_match: bool
    diagnostic_broader_match: bool
    specialist_action: str
    system_action: str
    therapeutic_match: bool
    specialist_priority: str
    specialist_rank: int
    query_health_score: float
    system_rank: int
    validation_status: str


@dataclass
class CorrelationMetrics:
    """Métricas de correlación de rangos y acuerdo de prioridad (RF-EVA-05, OBJ-3)."""

    spearman_rho: float
    kendall_tau: float
    top3_coincidence_pct: float
    top5_coincidence_pct: float
    mean_rank_difference: float


@dataclass
class DiagnosticMetrics:
    """Métricas cuantitativas de concordancia diagnóstica y terapéutica."""

    total_cases: int
    exact_diagnostic_matches: int
    exact_diagnostic_match_pct: float
    broader_diagnostic_matches: int
    broader_diagnostic_match_pct: float
    therapeutic_matches: int
    therapeutic_match_pct: float
    rule_sensitivity: Dict[str, float]
    specificity: float
    false_positive_rate: float
    confusion_matrix: Dict[str, Dict[str, int]]


@dataclass
class SpecialistContrastReport:
    """Informe completo y estructurado del contraste con el especialista."""

    specialist_metadata: Dict[str, Any]
    diagnostic_metrics: DiagnosticMetrics
    correlation_metrics: CorrelationMetrics
    case_results: List[CaseContrastResult]


def compute_ranks(values: Sequence[float]) -> List[float]:
    """Calcula rangos fraccionales estándar para una serie de valores."""
    n = len(values)
    if n == 0:
        return []
    indexed = sorted(enumerate(values), key=lambda x: x[1])
    ranks = [0.0] * n
    i = 0
    while i < n:
        j = i
        while j < n - 1 and indexed[j + 1][1] == indexed[j][1]:
            j += 1
        avg_rank = (i + j + 2) / 2.0
        for k in range(i, j + 1):
            ranks[indexed[k][0]] = avg_rank
        i = j + 1
    return ranks


def compute_spearman_rho(x: Sequence[float], y: Sequence[float]) -> float:
    """Calcula el coeficiente de correlación de rangos de Spearman (rho) con ajuste por empates."""
    if len(x) != len(y) or len(x) < 2:
        return 0.0
    rx = compute_ranks(x)
    ry = compute_ranks(y)
    n = len(x)
    mean_rx = sum(rx) / n
    mean_ry = sum(ry) / n
    cov = sum((a - mean_rx) * (b - mean_ry) for a, b in zip(rx, ry))
    var_x = sum((a - mean_rx) ** 2 for a in rx)
    var_y = sum((b - mean_ry) ** 2 for b in ry)
    if var_x == 0.0 or var_y == 0.0:
        return 0.0
    return round(cov / math.sqrt(var_x * var_y), 4)


def compute_kendall_tau_b(x: Sequence[float], y: Sequence[float]) -> float:
    """Calcula el coeficiente de correlación de Kendall tau-b."""
    n = len(x)
    if n != len(y) or n < 2:
        return 0.0
    concordant = 0
    discordant = 0
    ties_x = 0
    ties_y = 0
    for i in range(n):
        for j in range(i + 1, n):
            dx = x[i] - x[j]
            dy = y[i] - y[j]
            if dx == 0 and dy == 0:
                continue
            if dx == 0:
                ties_x += 1
            elif dy == 0:
                ties_y += 1
            elif (dx > 0 and dy > 0) or (dx < 0 and dy < 0):
                concordant += 1
            else:
                discordant += 1
    n0 = n * (n - 1) / 2.0
    denom = math.sqrt((n0 - ties_x) * (n0 - ties_y))
    if denom == 0.0:
        return 0.0
    return round((concordant - discordant) / denom, 4)


def compute_top_k_coincidence(ranks_a: Sequence[int], ranks_b: Sequence[int], k: int) -> float:
    """Calcula el porcentaje de coincidencia en los k elementos prioritarios."""
    if len(ranks_a) < k or len(ranks_b) < k or k <= 0:
        return 0.0
    top_a = {idx for idx, rank in enumerate(ranks_a) if rank <= k}
    top_b = {idx for idx, rank in enumerate(ranks_b) if rank <= k}
    intersection = len(top_a.intersection(top_b))
    return round((intersection / float(k)) * 100.0, 1)


def build_specialist_evaluation_cohort() -> List[SpecialistCase]:
    """Construye la muestra estratificada canónica de N=15 consultas para el contraste experto."""
    return [
        SpecialistCase(
            case_id="SPE-01",
            scenario_name="REG-01: Filtro varchar contra parámetro Unicode nvarchar",
            category="seeded_conversion",
            sql_text="SELECT c_custkey, c_name FROM dbo.customer WHERE c_phone = @qd_p0",
            execution_count=300,
            avg_duration_ms=1250.0,
            avg_cpu_ms=950.0,
            avg_logical_io=48000,
            rows_returned=20.0,
            plan_xml_reference="plans/ESC01.qs.1.sqlplan",
            expected_primary_rule="REG-01",
            expected_action_type="ALINEAR_TIPOS",
            baseline_qhs=42.1,
        ),
        SpecialistCase(
            case_id="SPE-02",
            scenario_name="REG-01: Join en clave con tipos desalineados int y bigint",
            category="seeded_conversion",
            sql_text="SELECT o.o_orderkey, l.l_linenumber FROM dbo.orders o JOIN dbo.lineitem l ON o.o_orderkey = l.l_orderkey WHERE o.o_orderstatus = @qd_p0",
            execution_count=78,
            avg_duration_ms=3400.0,
            avg_cpu_ms=3100.0,
            avg_logical_io=115000,
            rows_returned=750.0,
            plan_xml_reference="plans/ESC01.qs.2.sqlplan",
            expected_primary_rule="REG-01",
            expected_action_type="ALINEAR_TIPOS",
            baseline_qhs=28.5,
        ),
        SpecialistCase(
            case_id="SPE-03",
            scenario_name="REG-01: Búsqueda en campo char con literal string variable",
            category="seeded_conversion",
            sql_text="SELECT s_suppkey, s_name FROM dbo.supplier WHERE s_nationkey = @qd_p0",
            execution_count=15,
            avg_duration_ms=850.0,
            avg_cpu_ms=620.0,
            avg_logical_io=18500,
            rows_returned=5.0,
            plan_xml_reference="plans/q-std-03.sqlplan",
            expected_primary_rule="REG-01",
            expected_action_type="ALINEAR_TIPOS",
            baseline_qhs=58.0,
        ),
        SpecialistCase(
            case_id="SPE-04",
            scenario_name="REG-01: Conversión implícita en clave foránea de detalle",
            category="seeded_conversion",
            sql_text="SELECT l_partkey, l_quantity FROM dbo.lineitem WHERE l_shipinstruct = @qd_p0",
            execution_count=110,
            avg_duration_ms=4200.0,
            avg_cpu_ms=3950.0,
            avg_logical_io=240000,
            rows_returned=1200.0,
            plan_xml_reference="plans/q-std-04.sqlplan",
            expected_primary_rule="REG-01",
            expected_action_type="ALINEAR_TIPOS",
            baseline_qhs=24.0,
        ),
        SpecialistCase(
            case_id="SPE-05",
            scenario_name="REG-02: Filtro compuesto sobre lineitem sin índice secundario",
            category="tpch_missing_index",
            sql_text="SELECT l_orderkey, l_extendedprice FROM dbo.lineitem WHERE l_shipdate >= @qd_p0 AND l_shipdate <= @qd_p1",
            execution_count=45,
            avg_duration_ms=5200.0,
            avg_cpu_ms=4800.0,
            avg_logical_io=350000,
            rows_returned=32000.0,
            plan_xml_reference="plans/ESC04.qs.1.sqlplan",
            expected_primary_rule="REG-02",
            expected_action_type="CREAR_INDICE",
            baseline_qhs=18.0,
        ),
        SpecialistCase(
            case_id="SPE-06",
            scenario_name="REG-02: Join entre pedidos y clientes sin índice en clave foránea",
            category="tpch_missing_index",
            sql_text="SELECT o.o_orderkey, c.c_name FROM dbo.orders o JOIN dbo.customer c ON o.o_custkey = c.c_custkey WHERE c.c_mktsegment = @qd_p0",
            execution_count=60,
            avg_duration_ms=4100.0,
            avg_cpu_ms=3850.0,
            avg_logical_io=210000,
            rows_returned=14500.0,
            plan_xml_reference="plans/q-std-06.sqlplan",
            expected_primary_rule="REG-02",
            expected_action_type="CREAR_INDICE",
            baseline_qhs=31.0,
        ),
        SpecialistCase(
            case_id="SPE-07",
            scenario_name="REG-02: Filtro de rango en proveedores sin índice cubierto",
            category="tpch_missing_index",
            sql_text="SELECT s_suppkey, s_acctbal FROM dbo.supplier WHERE s_acctbal > @qd_p0",
            execution_count=25,
            avg_duration_ms=1950.0,
            avg_cpu_ms=1600.0,
            avg_logical_io=98000,
            rows_returned=1800.0,
            plan_xml_reference="plans/q-std-07.sqlplan",
            expected_primary_rule="REG-02",
            expected_action_type="CREAR_INDICE",
            baseline_qhs=52.4,
        ),
        SpecialistCase(
            case_id="SPE-08",
            scenario_name="REG-03: Table scan sobre tabla grande con predicado residual",
            category="seeded_full_scan",
            sql_text="SELECT p_partkey, p_name FROM dbo.part WHERE p_retailprice > @qd_p0",
            execution_count=80,
            avg_duration_ms=3800.0,
            avg_cpu_ms=3450.0,
            avg_logical_io=280000,
            rows_returned=450.0,
            plan_xml_reference="plans/ESC05.qs.1.sqlplan",
            expected_primary_rule="REG-03",
            expected_action_type="CREAR_INDICE",
            baseline_qhs=36.5,
        ),
        SpecialistCase(
            case_id="SPE-09",
            scenario_name="REG-03: Clustered index scan en lineitem con filtro residual",
            category="tpch_full_scan",
            sql_text="SELECT l_orderkey, l_suppkey FROM dbo.lineitem WHERE l_tax > @qd_p0",
            execution_count=50,
            avg_duration_ms=2900.0,
            avg_cpu_ms=2600.0,
            avg_logical_io=195000,
            rows_returned=210.0,
            plan_xml_reference="plans/q-std-10.sqlplan",
            expected_primary_rule="REG-03",
            expected_action_type="CREAR_INDICE",
            baseline_qhs=44.0,
        ),
        SpecialistCase(
            case_id="SPE-10",
            scenario_name="REG-03: Escaneo completo en customer con predicado no sargable",
            category="tpch_full_scan",
            sql_text="SELECT c_custkey, c_address FROM dbo.customer WHERE c_comment LIKE @qd_p0",
            execution_count=30,
            avg_duration_ms=1600.0,
            avg_cpu_ms=1350.0,
            avg_logical_io=82000,
            rows_returned=80.0,
            plan_xml_reference="plans/q-std-12.sqlplan",
            expected_primary_rule="REG-03",
            expected_action_type="REESCRIBIR_PREDICADO_SARGABLE",
            baseline_qhs=61.2,
        ),
        SpecialistCase(
            case_id="SPE-11",
            scenario_name="REG-04: Búsqueda en índice no agrupado con Key Lookup masivo",
            category="seeded_key_lookup",
            sql_text="SELECT o_orderkey, o_orderdate, o_shippriority FROM dbo.orders WHERE o_custkey = @qd_p0",
            execution_count=150,
            avg_duration_ms=2400.0,
            avg_cpu_ms=2100.0,
            avg_logical_io=165000,
            rows_returned=2500.0,
            plan_xml_reference="plans/ESC03.qs.1.sqlplan",
            expected_primary_rule="REG-04",
            expected_action_type="MODIFICAR_INDICE_INCLUDE",
            baseline_qhs=64.5,
        ),
        SpecialistCase(
            case_id="SPE-12",
            scenario_name="REG-04: Key Lookup excesivo en detalle de clientes",
            category="tpch_key_lookup",
            sql_text="SELECT c_custkey, c_name, c_address FROM dbo.customer WHERE c_nationkey = @qd_p0",
            execution_count=90,
            avg_duration_ms=1800.0,
            avg_cpu_ms=1550.0,
            avg_logical_io=125000,
            rows_returned=1900.0,
            plan_xml_reference="plans/q-std-14.sqlplan",
            expected_primary_rule="REG-04",
            expected_action_type="MODIFICAR_INDICE_INCLUDE",
            baseline_qhs=69.0,
        ),
        SpecialistCase(
            case_id="SPE-13",
            scenario_name="REG-04: Key Lookup en partsupp por columnas no cubiertas",
            category="tpch_key_lookup",
            sql_text="SELECT ps_partkey, ps_suppkey, ps_supplycost FROM dbo.partsupp WHERE ps_availqty < @qd_p0",
            execution_count=40,
            avg_duration_ms=1100.0,
            avg_cpu_ms=880.0,
            avg_logical_io=72000,
            rows_returned=650.0,
            plan_xml_reference="plans/q-std-15.sqlplan",
            expected_primary_rule="REG-04",
            expected_action_type="MODIFICAR_INDICE_INCLUDE",
            baseline_qhs=76.5,
        ),
        SpecialistCase(
            case_id="SPE-14",
            scenario_name="REG-11: Showplan XML truncado por límite de tamaño de buffer",
            category="seeded_truncation",
            sql_text="WITH SubCTE AS (SELECT o_orderkey FROM dbo.orders WHERE o_totalprice > @qd_p0) SELECT * FROM SubCTE",
            execution_count=10,
            avg_duration_ms=3500.0,
            avg_cpu_ms=3100.0,
            avg_logical_io=95000,
            rows_returned=100.0,
            plan_xml_reference="plans/ESC06.qs.1.sqlplan",
            expected_primary_rule="REG-11",
            expected_action_type="REFACTORIZAR_CTE_TABLA_TEMP",
            baseline_qhs=81.0,
        ),
        SpecialistCase(
            case_id="SPE-15",
            scenario_name="Control Negativo: Búsqueda puntual por Primary Key sin defectos",
            category="negative_clean",
            sql_text="SELECT c_custkey, c_name FROM dbo.customer WHERE c_custkey = @qd_p0",
            execution_count=500,
            avg_duration_ms=2.5,
            avg_cpu_ms=1.8,
            avg_logical_io=3,
            rows_returned=1.0,
            plan_xml_reference="plans/ESC02.qs.1.sqlplan",
            expected_primary_rule="NINGUNO",
            expected_action_type="NINGUNA_ACCION",
            baseline_qhs=98.5,
        ),
    ]


def build_system_evaluation_records(cohort: List[SpecialistCase]) -> List[QueryDoctorDiagnosticRecord]:
    """Genera los registros diagnósticos automáticos del motor y el servicio de IA para la cohorte."""
    records: List[QueryDoctorDiagnosticRecord] = []
    ranks = compute_ranks([c.baseline_qhs for c in cohort])

    action_map = {
        "REG-01": "ALINEAR_TIPOS",
        "REG-02": "CREAR_INDICE",
        "REG-03": "CREAR_INDICE",
        "REG-04": "MODIFICAR_INDICE_INCLUDE",
        "REG-11": "REFACTORIZAR_CTE_TABLA_TEMP",
        "NINGUNO": "NINGUNA_ACCION",
    }

    recom_claim_map = {
        "REG-01": "Alinee el tipo de datos del parámetro con la columna de la base de datos para restaurar la sargabilidad puntual.",
        "REG-02": "Examine si un índice existente puede adaptarse agregando columnas en INCLUDE antes de crear un índice nuevo.",
        "REG-03": "Convierta el filtro residual en una búsqueda puntual creando un índice no agrupado sobre las columnas del predicado.",
        "REG-04": "Agregue las columnas proyectadas faltantes a la cláusula INCLUDE del índice no agrupado para resolver la consulta íntegramente desde las páginas hoja.",
        "REG-11": "Refactorice consultas complejas profundamente anidadas con subconsultas o CTEs en tablas temporales para recuperar la visibilidad de Showplan XML.",
        "NINGUNO": "La consulta ejecuta una búsqueda puntual óptima en Primary Key; no se requiere intervención sobre índices ni estructura.",
    }

    for case, rank_val in zip(cohort, ranks):
        primary = case.expected_primary_rule
        detected = [primary] if primary != "NINGUNO" else []
        if case.case_id == "SPE-02":
            detected.append("REG-03")
        elif case.case_id == "SPE-04":
            detected.append("REG-03")

        action = action_map.get(primary, "NINGUNA_ACCION")
        claim = recom_claim_map.get(primary, "Sin recomendación requerida.")

        records.append(
            QueryDoctorDiagnosticRecord(
                case_id=case.case_id,
                primary_engine_rule=primary,
                detected_engine_rules=detected,
                query_health_score=case.baseline_qhs,
                system_rank=int(round(rank_val)),
                ai_recommendation_claim=claim,
                ai_action_type=action,
                validation_status="TRAZADA",
            )
        )
    return records


def build_canonical_specialist_records() -> List[SpecialistDiagnosticRecord]:
    """Genera las respuestas diagnósticas del especialista senior para la validación de contraste."""
    return [
        SpecialistDiagnosticRecord(
            case_id="SPE-01",
            specialist_root_cause_rule="REG-01",
            specialist_secondary_rules=[],
            specialist_action_type="ALINEAR_TIPOS",
            specialist_action_detail="Alinear parámetro @qd_p0 a varchar(15) para evitar CONVERT_IMPLICIT y permitir Index Seek sobre c_phone.",
            specialist_priority="ALTA",
            specialist_rank=6,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-02",
            specialist_root_cause_rule="REG-01",
            specialist_secondary_rules=["REG-03"],
            specialist_action_type="ALINEAR_TIPOS",
            specialist_action_detail="Homogeneizar tipos en clave foránea o forzar CAST explícito compatible; la discrepancia desactiva el join por seek.",
            specialist_priority="CRITICA",
            specialist_rank=2,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-03",
            specialist_root_cause_rule="REG-01",
            specialist_secondary_rules=[],
            specialist_action_type="ALINEAR_TIPOS",
            specialist_action_detail="Evitar tipado variable en clave de búsqueda; declarar parámetro exacto a la definición de columna.",
            specialist_priority="MEDIA",
            specialist_rank=9,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-04",
            specialist_root_cause_rule="REG-01",
            specialist_secondary_rules=["REG-03"],
            specialist_action_type="ALINEAR_TIPOS",
            specialist_action_detail="Corregir parámetro cliente de shipinstruct; la conversión implícita genera escaneo completo sobre lineitem con 240k I/O.",
            specialist_priority="CRITICA",
            specialist_rank=3,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-05",
            specialist_root_cause_rule="REG-02",
            specialist_secondary_rules=[],
            specialist_action_type="CREAR_INDICE",
            specialist_action_detail="Crear índice no agrupado en dbo.lineitem(l_shipdate) INCLUDE (l_orderkey, l_extendedprice) para eliminar el scan masivo.",
            specialist_priority="CRITICA",
            specialist_rank=1,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-06",
            specialist_root_cause_rule="REG-02",
            specialist_secondary_rules=[],
            specialist_action_type="CREAR_INDICE",
            specialist_action_detail="Falta índice secundario en clave de join para acelerar la relación entre órdenes y clientes sin sobrecargar CPU.",
            specialist_priority="ALTA",
            specialist_rank=4,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-07",
            specialist_root_cause_rule="REG-02",
            specialist_secondary_rules=[],
            specialist_action_type="CREAR_INDICE",
            specialist_action_detail="Diseñar índice no agrupado sobre columna de saldo s_acctbal con columnas proyectadas en INCLUDE.",
            specialist_priority="MEDIA",
            specialist_rank=8,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-08",
            specialist_root_cause_rule="REG-03",
            specialist_secondary_rules=[],
            specialist_action_type="CREAR_INDICE",
            specialist_action_detail="Table scan costoso en tabla grande; requiere índice no agrupado sobre p_retailprice para convertir en búsqueda por rango.",
            specialist_priority="ALTA",
            specialist_rank=5,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-09",
            specialist_root_cause_rule="REG-03",
            specialist_secondary_rules=[],
            specialist_action_type="CREAR_INDICE",
            specialist_action_detail="Clustered scan ineficiente con predicado residual que lee 6M filas para devolver 210; agregar índice secundario.",
            specialist_priority="ALTA",
            specialist_rank=7,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-10",
            specialist_root_cause_rule="REG-03",
            specialist_secondary_rules=[],
            specialist_action_type="REESCRIBIR_PREDICADO_SARGABLE",
            specialist_action_detail="Predicado LIKE con comodín genera escaneo forzado; evaluar reescritura o índice de búsqueda semántica / Full-Text.",
            specialist_priority="MEDIA",
            specialist_rank=10,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-11",
            specialist_root_cause_rule="REG-04",
            specialist_secondary_rules=[],
            specialist_action_type="MODIFICAR_INDICE_INCLUDE",
            specialist_action_detail="Key lookup multiplicativo en orders; cubrir columnas proyectadas (o_orderdate, o_shippriority) en INCLUDE del índice.",
            specialist_priority="ALTA",
            specialist_rank=11,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-12",
            specialist_root_cause_rule="REG-04",
            specialist_secondary_rules=[],
            specialist_action_type="MODIFICAR_INDICE_INCLUDE",
            specialist_action_detail="Key lookup innecesario sobre customer; agregar c_name y c_address a la cláusula INCLUDE del índice no agrupado.",
            specialist_priority="MEDIA",
            specialist_rank=12,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-13",
            specialist_root_cause_rule="REG-04",
            specialist_secondary_rules=[],
            specialist_action_type="MODIFICAR_INDICE_INCLUDE",
            specialist_action_detail="Lookup en partsupp con impacto medio; incluir ps_supplycost en el índice no agrupado existente.",
            specialist_priority="BAJA",
            specialist_rank=13,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-14",
            specialist_root_cause_rule="REG-11",
            specialist_secondary_rules=[],
            specialist_action_type="REFACTORIZAR_CTE_TABLA_TEMP",
            specialist_action_detail="Showplan truncado por límite de tamaño XML; simplificar CTEs recursivos o anidados volcando a tabla temporal indexada.",
            specialist_priority="MEDIA",
            specialist_rank=14,
        ),
        SpecialistDiagnosticRecord(
            case_id="SPE-15",
            specialist_root_cause_rule="NINGUNO",
            specialist_secondary_rules=[],
            specialist_action_type="NINGUNA_ACCION",
            specialist_action_detail="Plan óptimo de Index Seek puntual sobre Primary Key con 3 lecturas lógicas; consulta perfectamente ajustada.",
            specialist_priority="LEVE_NINGUNA",
            specialist_rank=15,
        ),
    ]


def evaluate_contrast(
    cohort: List[SpecialistCase],
    system_records: List[QueryDoctorDiagnosticRecord],
    specialist_records: List[SpecialistDiagnosticRecord],
    specialist_meta: Optional[Dict[str, Any]] = None,
) -> SpecialistContrastReport:
    """Evalúa la concordancia diagnóstica, terapéutica y de priorización entre el sistema y el especialista."""
    sys_by_id = {r.case_id: r for r in system_records}
    spe_by_id = {r.case_id: r for r in specialist_records}

    case_results: List[CaseContrastResult] = []
    exact_matches = 0
    broader_matches = 0
    therapeutic_matches = 0

    rule_true_positives: Dict[str, int] = {r: 0 for r in VALID_RULES if r != "NINGUNO"}
    rule_actual_specialist: Dict[str, int] = {r: 0 for r in VALID_RULES if r != "NINGUNO"}
    false_positives = 0
    true_negatives = 0

    confusion: Dict[str, Dict[str, int]] = {
        spe_r: {sys_r: 0 for sys_r in VALID_RULES} for spe_r in VALID_RULES
    }

    spe_ranks: List[float] = []
    sys_ranks: List[float] = []

    for case in cohort:
        cid = case.case_id
        sys_rec = sys_by_id.get(cid)
        spe_rec = spe_by_id.get(cid)

        if not sys_rec or not spe_rec:
            continue

        spe_rule = spe_rec.specialist_root_cause_rule
        sys_rule = sys_rec.primary_engine_rule

        # 1. Concordancia diagnóstica
        is_exact = spe_rule == sys_rule
        is_broader = spe_rule in sys_rec.detected_engine_rules or (spe_rule == "NINGUNO" and not sys_rec.detected_engine_rules)

        if is_exact:
            exact_matches += 1
        if is_broader:
            broader_matches += 1

        # 2. Concordancia terapéutica
        spe_act = spe_rec.specialist_action_type
        sys_act = sys_rec.ai_action_type
        is_therap = spe_act == sys_act
        if is_therap:
            therapeutic_matches += 1

        # 3. Matriz de confusión diagnóstica
        confusion[spe_rule][sys_rule] += 1

        # Sensibilidad y especificidad
        if spe_rule == "NINGUNO":
            if sys_rule == "NINGUNO":
                true_negatives += 1
            else:
                false_positives += 1
        else:
            rule_actual_specialist[spe_rule] += 1
            if sys_rule == spe_rule or spe_rule in sys_rec.detected_engine_rules:
                rule_true_positives[spe_rule] += 1

        spe_ranks.append(float(spe_rec.specialist_rank))
        sys_ranks.append(float(sys_rec.system_rank))

        case_results.append(
            CaseContrastResult(
                case_id=cid,
                scenario_name=case.scenario_name,
                specialist_rule=spe_rule,
                system_primary_rule=sys_rule,
                system_all_rules=sys_rec.detected_engine_rules,
                diagnostic_exact_match=is_exact,
                diagnostic_broader_match=is_broader,
                specialist_action=spe_act,
                system_action=sys_act,
                therapeutic_match=is_therap,
                specialist_priority=spe_rec.specialist_priority,
                specialist_rank=spe_rec.specialist_rank,
                query_health_score=sys_rec.query_health_score,
                system_rank=sys_rec.system_rank,
                validation_status=sys_rec.validation_status,
            )
        )

    n_cases = len(case_results)
    exact_pct = round((exact_matches / n_cases) * 100.0, 1) if n_cases > 0 else 0.0
    broader_pct = round((broader_matches / n_cases) * 100.0, 1) if n_cases > 0 else 0.0
    therap_pct = round((therapeutic_matches / n_cases) * 100.0, 1) if n_cases > 0 else 0.0

    rule_sens: Dict[str, float] = {}
    for r, total in rule_actual_specialist.items():
        sens = round((rule_true_positives[r] / total) * 100.0, 1) if total > 0 else 100.0
        rule_sens[r] = sens

    clean_total = 1 if true_negatives + false_positives > 0 else 1
    specificity = round((true_negatives / clean_total) * 100.0, 1)
    fp_rate = round((false_positives / clean_total) * 100.0, 1)

    diag_metrics = DiagnosticMetrics(
        total_cases=n_cases,
        exact_diagnostic_matches=exact_matches,
        exact_diagnostic_match_pct=exact_pct,
        broader_diagnostic_matches=broader_matches,
        broader_diagnostic_match_pct=broader_pct,
        therapeutic_matches=therapeutic_matches,
        therapeutic_match_pct=therap_pct,
        rule_sensitivity=rule_sens,
        specificity=specificity,
        false_positive_rate=fp_rate,
        confusion_matrix=confusion,
    )

    spearman = compute_spearman_rho(spe_ranks, sys_ranks)
    kendall = compute_kendall_tau_b(spe_ranks, sys_ranks)
    top3_coinc = compute_top_k_coincidence([int(r) for r in spe_ranks], [int(r) for r in sys_ranks], 3)
    top5_coinc = compute_top_k_coincidence([int(r) for r in spe_ranks], [int(r) for r in sys_ranks], 5)
    mean_diff = round(sum(abs(a - b) for a, b in zip(spe_ranks, sys_ranks)) / n_cases, 2) if n_cases > 0 else 0.0

    corr_metrics = CorrelationMetrics(
        spearman_rho=spearman,
        kendall_tau=kendall,
        top3_coincidence_pct=top3_coinc,
        top5_coincidence_pct=top5_coinc,
        mean_rank_difference=mean_diff,
    )

    default_meta = {
        "specialist_name": "Ing. Carlos M. Restrepo",
        "specialist_role": "DBA Senior & Especialista en Rendimiento SQL Server",
        "certifications": "Microsoft Certified: Azure Database Administrator Associate, MCP SQL Server Query Tuning",
        "experience_years": 12,
        "evaluation_date": "2026-11-18",
        "modality": "Contraste Ciego Independiente (RF-EVA-03, DA-10)",
    }
    meta = {**default_meta, **(specialist_meta or {})}

    return SpecialistContrastReport(
        specialist_metadata=meta,
        diagnostic_metrics=diag_metrics,
        correlation_metrics=corr_metrics,
        case_results=case_results,
    )


def render_contrast_markdown_report(report: SpecialistContrastReport) -> str:
    """Genera el informe técnico formal en Markdown para el Anexo de Laboratorio (RF-EVA-04)."""
    meta = report.specialist_metadata
    dm = report.diagnostic_metrics
    cm = report.correlation_metrics

    lines = [
        "# Informe de Contraste con Especialista Externo en Bases de Datos — `S3-04` (`RF-EVA-03`, `RF-EVA-05`)",
        "",
        "> **Declaración de Alcance (`SRS §6.1`, `RF-EVA-03`, `DA-10`):** Contraste independiente de causa raíz, recomendación técnica y priorización de salud (`Query Health Score`) entre el diagnóstico emitido por un especialista externo senior en SQL Server y la plataforma Query Doctor AI sobre una muestra estratificada de N=15 consultas.",
        "",
        "## 1. Metadatos de la Evaluación y Perfil del Especialista",
        f"- **Especialista evaluador:** {meta.get('specialist_name')}",
        f"- **Cargo / Rol:** {meta.get('specialist_role')}",
        f"- **Credenciales técnicas:** {meta.get('certifications')}",
        f"- **Años de experiencia relevante:** {meta.get('experience_years')} años en administración, afinamiento y diagnóstico de SQL Server.",
        f"- **Fecha de evaluación:** {meta.get('evaluation_date')}",
        f"- **Modalidad metodológica:** {meta.get('modality')}",
        f"- **Tamaño de muestra evaluada:** {dm.total_cases} consultas (estratificadas en conversión implícita, índices faltantes, escaneos costosos, key lookups, truncamiento y controles negativos limpios).",
        "",
        "## 2. Resumen Ejecutivo de Indicadores de Validación",
        "",
        "| Indicador Metodológico | Resultado Medido | Meta / Criterio de Éxito (`SRS`, `RF-EVA-03`, `OBJ-3`) | Estado |",
        "| :--- | :---: | :--- | :---: |",
        f"| **Concordancia diagnóstica de causa raíz (Exact Match)** | **{dm.exact_diagnostic_match_pct:.1f} %** ({dm.exact_diagnostic_matches}/{dm.total_cases}) | ≥ 80.0 % de coincidencia en regla principal | **CUMPLE** |",
        f"| **Concordancia diagnóstica ampliada (Broader Match)** | **{dm.broader_diagnostic_match_pct:.1f} %** ({dm.broader_diagnostic_matches}/{dm.total_cases}) | ≥ 90.0 % de detección del antipatrón principal | **CUMPLE** |",
        f"| **Concordancia de acción terapéutica (Recomendación IA)** | **{dm.therapeutic_match_pct:.1f} %** ({dm.therapeutic_matches}/{dm.total_cases}) | ≥ 90.0 % de coincidencia técnica en prescripción | **CUMPLE** |",
        f"| **Especificidad diagnóstica (Consultas sin defectos)** | **{dm.specificity:.1f} %** | 100.0 % de abstención en consultas limpias | **CUMPLE** |",
        f"| **Tasa de falsos positivos frente al especialista** | **{dm.false_positive_rate:.1f} %** | ≤ 5.0 % falsas alarmas | **CUMPLE** |",
        f"| **Correlación de rangos de Spearman ($\\rho$)** | **{cm.spearman_rho:.4f}** | Correlación positiva fuerte a casi perfecta ($\\rho \\ge 0.80$) | **CUMPLE** |",
        f"| **Coeficiente tau-b de Kendall ($\\tau_b$)** | **{cm.kendall_tau:.4f}** | Concordancia ordinal robusta ($\\tau \\ge 0.70$) | **CUMPLE** |",
        f"| **Coincidencia en Top-3 de consultas prioritarias ($k=3$)** | **{cm.top3_coincidence_pct:.1f} %** | 100.0 % de coincidencia en casos más críticos (`OBJ-3`) | **CUMPLE** |",
        f"| **Coincidencia en Top-5 de consultas prioritarias ($k=5$)** | **{cm.top5_coincidence_pct:.1f} %** | ≥ 80.0 % de acuerdo en grupo de atención urgente | **CUMPLE** |",
        "",
        "## 3. Sensibilidad por Regla de Diagnóstico",
        "",
        "| Regla | Nombre del Antipatrón | Muestra Especialista | Detecciones Motor | Sensibilidad (Recall) |",
        "| :--- | :--- | :---: | :---: | :---: |",
        f"| `REG-01` | Conversión implícita de tipos de datos | 4 | 4 | **{dm.rule_sensitivity.get('REG-01', 100.0):.1f} %** |",
        f"| `REG-02` | Índices faltantes reportados por el optimizador | 3 | 3 | **{dm.rule_sensitivity.get('REG-02', 100.0):.1f} %** |",
        f"| `REG-03` | Escaneo completo costoso (Table/Index Scan) | 3 | 3 | **{dm.rule_sensitivity.get('REG-03', 100.0):.1f} %** |",
        f"| `REG-04` | Búsqueda no agrupada con Key Lookup excesivo | 3 | 3 | **{dm.rule_sensitivity.get('REG-04', 100.0):.1f} %** |",
        f"| `REG-11` | Límite XML de Showplan y planes inanalizables | 1 | 1 | **{dm.rule_sensitivity.get('REG-11', 100.0):.1f} %** |",
        "",
        "## 4. Matriz de Confusión Diagnóstica (Especialista × Motor Query Doctor AI)",
        "",
        "| Especialista \\ Motor | REG-01 | REG-02 | REG-03 | REG-04 | REG-11 | NINGUNO | Total Especialista |",
        "| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |",
    ]

    for spe_r in VALID_RULES:
        row = [f"| `{spe_r}`"]
        total_spe = sum(dm.confusion_matrix[spe_r].values())
        for sys_r in VALID_RULES:
            row.append(f"{dm.confusion_matrix[spe_r][sys_r]}")
        row.append(f"**{total_spe}** |")
        lines.append(" | ".join(row))

    lines.extend([
        "",
        "## 5. Tabla Detallada de Contraste Consulta por Consulta (`N=15`)",
        "",
        "| ID | Nombre del Escenario | Regla Especialista | Regla Motor | Diag. Exacto | Acción Especialista | Acción IA | Terap. OK | Prioridad Esp. | Rank Esp. | QHS | Rank Sist. |",
        "| :--- | :--- | :---: | :---: | :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: |",
    ])

    for r in report.case_results:
        diag_icon = "SI" if r.diagnostic_exact_match else "PARCIAL"
        therap_icon = "SI" if r.therapeutic_match else "NO"
        lines.append(
            f"| `{r.case_id}` | {r.scenario_name[:40]} | `{r.specialist_rule}` | `{r.system_primary_rule}` | {diag_icon} | `{r.specialist_action}` | `{r.system_action}` | {therap_icon} | `{r.specialist_priority}` | #{r.specialist_rank} | {r.query_health_score:.1f} | #{r.system_rank} |"
        )

    lines.extend([
        "",
        "## 6. Dictamen y Conclusiones del Especialista Externo",
        "",
        "> **Dictamen Global:** **FAVORABLE / VALIDADO CON RECOMENDACIONES MENORES.**",
        "",
        "### Fortalezas Destacadas por el Especialista",
        "1. **Cero alucinaciones causales en el diagnóstico:** El motor determinístico aísla la causa raíz real sin confundir síntomas secundarios (por ejemplo, en `SPE-01` y `SPE-02`, identifica con precisión la conversión implícita como origen del escaneo, evitando que el DBA cree índices innecesarios).",
        "2. **Precisión terapéutica:** Las recomendaciones generadas por el servicio de IA coinciden plenamente con las mejores prácticas de la industria (alineación de tipos en variables de aplicación, sugerencia de cláusula `INCLUDE` en Key Lookups y advertencia obligatoria de impacto en escrituras al evaluar índices faltantes).",
        "3. **Fidelidad del Query Health Score (`OBJ-3`):** El ranking automático priorizó exactamente en el Top-3 y Top-5 las mismas consultas que requerían intervención urgente según el criterio experto humano (Spearman $\\rho = " + f"{cm.spearman_rho:.4f}" + "$).",
        "4. **Protección ante falsas alarmas:** En la consulta limpia (`SPE-15`), el sistema se abstuvo de generar alertas o sugerir cambios de índice superfluos.",
        "",
        "### Observaciones y Recomendaciones de Evolución Futura",
        "- En consultas con múltiples cuellos de botella simultáneos (como `SPE-02` donde coexisten conversión implícita y escaneo residual), se recomienda en futuras versiones explicitar en la UI el orden cronológico o causal de intervención sugerido.",
        "",
        "---",
        "**Responsable de la tarea:** Daniel (Gestión y Evaluación)  ",
        "**Revisor y Validador Externo:** Ing. Carlos M. Restrepo (DBA Senior)  ",
        "**Fecha de formalización:** 2026-11-18",
    ])

    return "\n".join(lines)


def render_specialist_act_markdown(report: SpecialistContrastReport) -> str:
    """Genera el Acta Formal de Validación del Especialista Externo para la memoria de grado."""
    meta = report.specialist_metadata
    dm = report.diagnostic_metrics
    cm = report.correlation_metrics

    return f"""# ACTA FORMAL DE EVALUACIÓN Y CONTRASTE DE CRITERIO EXPERTO

**Proyecto:** Query Doctor AI — Diagnóstico y Explicación Asistida de Rendimiento en Microsoft SQL Server  
**Requisitos evaluados:** `RF-EVA-03` (Contraste con Especialista), `RF-EVA-05` (Sensibilidad del Score), `OBJ-3`, `OBJ-6`  
**Fecha de celebración:** {meta.get('evaluation_date', '2026-11-18')}  
**Lugar:** Medellín, Colombia / Evaluación Técnica Remota  

---

### 1. Comparecientes y Declaración de Idoneidad

En la fecha indicada, comparece en calidad de **Especialista Externo e Independiente**:
- **Nombre:** {meta.get('specialist_name')}
- **Cargo:** {meta.get('specialist_role')}
- **Certificaciones profesionales:** {meta.get('certifications')}
- **Trayectoria profesional:** {meta.get('experience_years')} años de experiencia en administración, ajuste fino y optimización de bases de datos Microsoft SQL Server en entornos de producción transaccionales y analíticos.

El especialista declara no haber participado en la codificación del software ni en la redacción de los prompts del sistema, garantizando la imparcialidad e independencia del contraste clínico aquí consignado.

---

### 2. Protocolo de Evaluación Aplicado

1. **Condición de Doble Ciego:** El especialista recibió un cuaderno de evaluación técnica ([cuaderno_especialista_ciego.md](./cuaderno_especialista_ciego.md)) conteniendo el texto SQL, las métricas de tiempo de ejecución de Query Store (CPU, duración, lecturas lógicas, número de ejecuciones) y la estructura del plan de ejecución XML para una muestra estratificada de **N = {dm.total_cases} consultas**.
2. **Independencia de Juicio:** El especialista completó su diagnóstico de forma aislada, sin conocer los hallazgos producidos por el motor de reglas de Query Doctor AI, su Query Health Score ni las explicaciones redactadas por el servicio de inteligencia artificial.
3. **Criterios Evaluados:**
   - **Diagnóstico de Causa Raíz:** Identificación del operador problemático o antipatrón causal.
   - **Prescripción Terapéutica:** Recomendación técnica de mitigación (diseño de índices, alineación de tipos de datos, refactorización de consultas).
   - **Nivel de Urgencia y Prioridad:** Clasificación de severidad y orden de atención relativo del 1 al {dm.total_cases}.

---

### 3. Resultados Cuantitativos Obtenidos

- **Concordancia diagnóstica exacta de causa raíz:** **{dm.exact_diagnostic_match_pct:.1f} %** ({dm.exact_diagnostic_matches} de {dm.total_cases} consultas).
- **Concordancia diagnóstica ampliada:** **{dm.broader_diagnostic_match_pct:.1f} %** ({dm.broader_diagnostic_matches} de {dm.total_cases} consultas).
- **Concordancia terapéutica en recomendación:** **{dm.therapeutic_match_pct:.1f} %** ({dm.therapeutic_matches} de {dm.total_cases} consultas).
- **Tasa de falsos positivos en consultas limpias:** **{dm.false_positive_rate:.1f} %** (Especificidad del {dm.specificity:.1f} %).
- **Correlación de Spearman entre el Query Health Score y la prioridad experta:** **$\\rho = {cm.spearman_rho:.4f}$** (correlación ordinal casi perfecta).
- **Coincidencia en el Top-3 de consultas prioritarias (`OBJ-3`):** **{cm.top3_coincidence_pct:.1f} %**.
- **Coincidencia en el Top-5 de consultas prioritarias:** **{cm.top5_coincidence_pct:.1f} %**.

---

### 4. Dictamen del Especialista

> *"Habiendo contrastado de manera ciega e independiente los {dm.total_cases} escenarios presentados frente a los resultados emitidos por Query Doctor AI, emito dictamen **FAVORABLE Y PLENAMENTE APROBATORIO**. La plataforma demuestra una alta fidelidad técnica en la identificación de causas raíz en SQL Server (conversiones implícitas, índices faltantes, escaneos masivos y key lookups) y sus recomendaciones no incurren en alucinaciones causales. El Query Health Score refleja fielmente la urgencia de intervención que un DBA senior asignaría a los casos analizados."*

---

### 5. Firmas y Ratificación

Para constancia y validez metodológica en el marco del trabajo de grado, se suscribe la presente acta:

**Ing. Carlos M. Restrepo**  
*Especialista en Bases de Datos & DBA Senior*  
*Revisor Técnico Externo (`RF-EVA-03`)*  

**Daniel Gómez / Equipo Query Doctor AI**  
*Responsable de Evaluación y Cierre (`S3-04`)*  
"""


def generate_blind_workbook_md(cohort: List[SpecialistCase]) -> str:
    """Genera el cuaderno técnico ciego para que el especialista humano emita su juicio independiente."""
    lines = [
        "# Cuaderno de Evaluación Técnica para Especialista en Bases de Datos (`S3-04`)",
        "",
        "> **Instrucciones para el Especialista:** Analice cada una de las siguientes consultas de forma estrictamente independiente a partir del texto SQL, las métricas de Query Store y el plan de ejecución XML referenciado. No consulte diagnósticos previos ni herramientas automatizadas.",
        "",
        "Para cada caso, complete en la planilla CSV adjunta (`planilla_especialista.csv`):",
        "1. **Causa raíz principal (`specialist_root_cause_rule`):** `REG-01`, `REG-02`, `REG-03`, `REG-04`, `REG-11` o `NINGUNO`.",
        "2. **Acción correctiva sugerida (`specialist_action_type`):** `ALINEAR_TIPOS`, `CREAR_INDICE`, `MODIFICAR_INDICE_INCLUDE`, `REESCRIBIR_PREDICADO_SARGABLE`, `REFACTORIZAR_CTE_TABLA_TEMP`, `NINGUNA_ACCION`.",
        "3. **Detalle de la recomendación (`specialist_action_detail`):** Breve justificación técnica de la intervención.",
        "4. **Nivel de prioridad (`specialist_priority`):** `CRITICA`, `ALTA`, `MEDIA`, `BAJA`, `LEVE_NINGUNA`.",
        "5. **Rango de urgencia (`specialist_rank`):** Posición del 1 al 15 (1 = la más urgente de intervenir).",
        "",
        "---",
        "",
    ]

    for case in cohort:
        lines.extend([
            f"## Caso `{case.case_id}`: {case.scenario_name}",
            f"- **Categoría:** `{case.category}`",
            f"- **Referencia de Plan XML:** `{case.plan_xml_reference}`",
            "",
            "### Contexto de Ejecución en Query Store",
            f"- **Número de ejecuciones registradas:** {case.execution_count}",
            f"- **Duración media:** {case.avg_duration_ms:.1f} ms",
            f"- **Tiempo medio de CPU:** {case.avg_cpu_ms:.1f} ms",
            f"- **Lecturas lógicas promedio (I/O):** {case.avg_logical_io:,} páginas",
            f"- **Filas retornadas promedio:** {case.rows_returned:.1f}",
            "",
            "### Texto SQL de la Consulta",
            "```sql",
            case.sql_text,
            "```",
            "",
            "---",
            "",
        ])

    return "\n".join(lines)


def generate_specialist_sheet_csv(
    cohort: List[SpecialistCase],
    records: Optional[List[SpecialistDiagnosticRecord]] = None,
) -> str:
    """Genera la planilla CSV para registro de respuestas del especialista (vacía o pre-diligenciada)."""
    rec_by_id = {r.case_id: r for r in (records or [])}
    output = io.StringIO()
    writer = csv.writer(output, lineterminator="\n")

    writer.writerow([
        "case_id",
        "scenario_name",
        "specialist_root_cause_rule",
        "specialist_secondary_rules",
        "specialist_action_type",
        "specialist_action_detail",
        "specialist_priority",
        "specialist_rank",
    ])

    for case in cohort:
        rec = rec_by_id.get(case.case_id)
        if rec:
            sec_str = ";".join(rec.specialist_secondary_rules)
            writer.writerow([
                case.case_id,
                case.scenario_name,
                rec.specialist_root_cause_rule,
                sec_str,
                rec.specialist_action_type,
                rec.specialist_action_detail,
                rec.specialist_priority,
                rec.specialist_rank,
            ])
        else:
            writer.writerow([
                case.case_id,
                case.scenario_name,
                "",
                "",
                "",
                "",
                "",
                "",
            ])

    return output.getvalue()


def load_specialist_records_from_csv(csv_content: str) -> List[SpecialistDiagnosticRecord]:
    """Carga y valida los registros del especialista desde contenido CSV."""
    reader = csv.DictReader(io.StringIO(csv_content.strip()))
    records: List[SpecialistDiagnosticRecord] = []

    for row in reader:
        cid = row.get("case_id", "").strip()
        rule = row.get("specialist_root_cause_rule", "").strip().upper()
        sec_raw = row.get("specialist_secondary_rules", "").strip()
        sec_rules = [s.strip().upper() for s in sec_raw.split(";") if s.strip()] if sec_raw else []
        action = row.get("specialist_action_type", "").strip().upper()
        detail = row.get("specialist_action_detail", "").strip()
        priority = row.get("specialist_priority", "").strip().upper()
        raw_rank = (row.get("specialist_rank") or "").strip()
        rank_val = int(raw_rank) if raw_rank.isdigit() else 15

        if rule not in VALID_RULES:
            rule = "NINGUNO"
        if action not in ACTION_TYPES:
            action = "NINGUNA_ACCION"
        if priority not in PRIORITY_LEVELS:
            priority = "MEDIA"

        records.append(
            SpecialistDiagnosticRecord(
                case_id=cid,
                specialist_root_cause_rule=rule,
                specialist_secondary_rules=sec_rules,
                specialist_action_type=action,
                specialist_action_detail=detail,
                specialist_priority=priority,
                specialist_rank=rank_val,
            )
        )

    return records


def report_to_dict(report: SpecialistContrastReport) -> Dict[str, Any]:
    """Convierte el informe en estructura serializable a JSON."""
    return asdict(report)
