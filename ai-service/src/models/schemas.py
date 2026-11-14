"""Esquemas de afirmaciones y solicitudes para el servicio de IA (RF-IA-01, RF-IA-02, RF-TRZ-01)."""

from enum import Enum
from typing import Any, Dict, List, Optional
from pydantic import BaseModel, Field, model_validator


class ClaimType(str, Enum):
    """Tipos de afirmación tipada requeridos por RF-IA-02 (v1.1)."""

    OBSERVACION = "OBSERVACION"
    INTERPRETACION = "INTERPRETACION"
    RECOMENDACION = "RECOMENDACION"


class FindingInput(BaseModel):
    """Hallazgo determinístico producido por el motor (RF-MOT-xx)."""

    finding_id: str = Field(description="Identificador único del hallazgo (hash determinístico)")
    rule_id: str = Field(description="Identificador de la regla, e.g. REG-01")
    rule_name: str = Field(description="Nombre legible de la regla violada")
    evidence_class: str = Field(default="ESTIMADA", description="Clase de evidencia: ESTIMADA, EJECUCION_AGREGADA, EXACTA")
    severity: str = Field(default="ALTA", description="Severidad: CRITICA, ALTA, MEDIA, BAJA")
    target_nodes: List[str] = Field(default_factory=list, description="Nodos del plan afectados")
    metrics: Dict[str, Any] = Field(default_factory=dict, description="Métricas asociadas al hallazgo")
    description: str = Field(description="Descripción del problema encontrado")


class KbFragmentInput(BaseModel):
    """Fragmento curado de la base de conocimiento oficial (RF-IA-04, S1-17)."""

    fragment_id: str = Field(description="Identificador del fragmento curado, e.g. KB-REG01-01")
    rule_id: str = Field(description="Identificador de la regla asociada, e.g. REG-01")
    title: str = Field(description="Título del fragmento técnico")
    content: str = Field(description="Contenido técnico curado")
    source_citation: str = Field(description="Cita oficial de Microsoft Learn u origen verificado")


class ExplainRequest(BaseModel):
    """Solicitud de explicación diagnóstica enviada por el Worker (CU-05, RF-IA-01)."""

    query_id: str = Field(description="Identificador de la consulta en Query Doctor")
    sql_text: str = Field(description="Texto SQL con literales ya enmascarados por LiteralMasker")
    findings: List[FindingInput] = Field(min_length=1, description="Lista de hallazgos determinísticos (mínimo 1 según RF-IA-01)")
    kb_fragments: List[KbFragmentInput] = Field(default_factory=list, description="Fragmentos curados recuperados para el contexto")
    metrics: Optional[Dict[str, Any]] = Field(default=None, description="Métricas de ejecución generales de la consulta")
    user_role: Optional[str] = Field(default="DBA", description="Rol de destino: DBA o Desarrollador (RF-IA-09)")
    correlation_id: Optional[str] = Field(default=None, description="Identificador de correlación para observabilidad (RNF-MAN-05)")


class ClaimOutput(BaseModel):
    """Afirmación tipada individual según el esquema formal de RF-IA-02 (v1.1)."""

    claim_type: ClaimType = Field(description="Tipo de afirmación: OBSERVACION, INTERPRETACION, RECOMENDACION")
    statement: str = Field(description="Texto de la afirmación técnica en español")
    finding_ids: List[str] = Field(description="Identificadores de hallazgos o métricas que sustentan la afirmación (mínimo 1)")
    kb_fragment_id: Optional[str] = Field(default=None, description="Identificador del fragmento curado citado (obligatorio en INTERPRETACION y RECOMENDACION)")

    @model_validator(mode="after")
    def validate_claim_rules(self) -> "ClaimOutput":
        """Valida las reglas formales de RF-IA-02."""
        # Toda afirmación debe referenciar al menos un hallazgo o métrica del contexto
        if not self.finding_ids:
            raise ValueError("RF-IA-02: Toda afirmación debe referenciar al menos un finding_id del contexto.")

        # Limpiar espacios en los finding_ids
        clean_finding_ids = [fid.strip() for fid in self.finding_ids if fid and fid.strip()]
        if not clean_finding_ids:
            raise ValueError("RF-IA-02: Los finding_ids no pueden estar vacíos.")
        self.finding_ids = clean_finding_ids

        # INTERPRETACION y RECOMENDACION exigen kb_fragment_id de forma obligatoria
        if self.claim_type in (ClaimType.INTERPRETACION, ClaimType.RECOMENDACION):
            if not self.kb_fragment_id or not self.kb_fragment_id.strip():
                raise ValueError(
                    f"RF-IA-02: Las afirmaciones de tipo {self.claim_type.value} deben referenciar "
                    "obligatoriamente el identificador de un fragmento de la base de conocimiento (kb_fragment_id)."
                )
            self.kb_fragment_id = self.kb_fragment_id.strip()

        return self


class ExplainMetadata(BaseModel):
    """Metadatos de auditoría y trazabilidad de la explicación generada (RF-TRZ-01)."""

    llm_profile: str = Field(description="Perfil de LLM configurado")
    model: str = Field(description="Nombre exacto del modelo ejecutado")
    prompt_version: str = Field(description="Versión de la plantilla de prompt utilizada")
    served_by: str = Field(description="Proveedor o endpoint que sirvió la inferencia")
    data_policy: str = Field(description="Política de datos del perfil aplicado")
    execution_time_seconds: float = Field(description="Tiempo de inferencia en segundos")


class ExplainResponse(BaseModel):
    """Respuesta completa del servicio de IA que cumple con el esquema de afirmaciones (RF-IA-02 v1.1)."""

    query_id: str = Field(description="Identificador de la consulta explicada")
    summary: str = Field(description="Resumen global conciso del diagnóstico")
    claims: List[ClaimOutput] = Field(min_length=1, description="Lista de afirmaciones tipadas validadas")
    metadata: ExplainMetadata = Field(description="Metadatos de trazabilidad y gobernanza de la IA")
