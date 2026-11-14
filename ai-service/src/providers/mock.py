"""Proveedor simulado (Mock) determinístico para pruebas automáticas y CI sin conexión externa."""

import time
from src.config import Settings
from src.models.schemas import (
    ClaimOutput,
    ClaimType,
    ExplainMetadata,
    ExplainRequest,
    ExplainResponse,
)
from src.providers.base import BaseLLMProvider
from src.security_lock import get_data_policy


class MockProvider(BaseLLMProvider):
    """Generador determinístico que produce respuestas válidas conforme a RF-IA-02."""

    def __init__(self, settings: Settings):
        super().__init__(settings)

    async def generate_explanation(self, request: ExplainRequest) -> ExplainResponse:
        """Crea una respuesta simulada válida con afirmaciones tipadas que sustentan los hallazgos."""
        start_time = time.perf_counter()
        claims: list[ClaimOutput] = []

        # Crear mapa de fragmentos curados por rule_id
        fragments_by_rule = {kb.rule_id: kb for kb in request.kb_fragments}
        fallback_fragment = request.kb_fragments[0] if request.kb_fragments else None

        for finding in request.findings:
            # 1. OBSERVACION
            nodes_desc = ", ".join(finding.target_nodes) if finding.target_nodes else "la consulta"
            obs_claim = ClaimOutput(
                claim_type=ClaimType.OBSERVACION,
                statement=(
                    f"El motor determinístico detectó {finding.rule_id} ({finding.rule_name}) "
                    f"con severidad {finding.severity} sobre {nodes_desc}. {finding.description}"
                ),
                finding_ids=[finding.finding_id],
                kb_fragment_id=None,
            )
            claims.append(obs_claim)

            # Fragmento de base de conocimiento asociado
            kb = fragments_by_rule.get(finding.rule_id) or fallback_fragment
            frag_id = kb.fragment_id if kb else "KB-MOCK-01"

            # 2. INTERPRETACION (exige fragment_id por RF-IA-02)
            interp_claim = ClaimOutput(
                claim_type=ClaimType.INTERPRETACION,
                statement=(
                    f"La presencia de {finding.rule_id} produce ineficiencia en el operador debido "
                    f"a discrepancias entre filas estimadas y reales o lecturas excesivas, según documenta {frag_id}."
                ),
                finding_ids=[finding.finding_id],
                kb_fragment_id=frag_id,
            )
            claims.append(interp_claim)

            # 3. RECOMENDACION (exige fragment_id por RF-IA-02 y sujeción a revisión humana por RF-IA-05)
            recom_claim = ClaimOutput(
                claim_type=ClaimType.RECOMENDACION,
                statement=(
                    f"Se sugiere al DBA revisar los índices de soporte y actualizar estadísticas para mitigar "
                    f"{finding.rule_id}, siguiendo las recomendaciones de {frag_id} (sujeto a revisión humana)."
                ),
                finding_ids=[finding.finding_id],
                kb_fragment_id=frag_id,
            )
            claims.append(recom_claim)

        execution_time = round(time.perf_counter() - start_time, 4)
        metadata = ExplainMetadata(
            llm_profile=self.settings.LLM_PROFILE,
            model="mock-gemma-2",
            prompt_version=self.settings.PROMPT_VERSION,
            served_by="mock-engine",
            data_policy=get_data_policy(self.settings.LLM_PROFILE),
            execution_time_seconds=execution_time,
        )

        summary = (
            f"Se identificaron {len(request.findings)} hallazgo(s) crítico(s) que afectan el rendimiento "
            f"de la consulta {request.query_id}."
        )

        return ExplainResponse(
            query_id=request.query_id,
            summary=summary,
            claims=claims,
            metadata=metadata,
        )
