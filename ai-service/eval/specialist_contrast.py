"""Preparación del paquete ciego y cálculo reproducible del contraste con especialista (S3-04, RF-EVA-03, RF-EVA-05)."""

from __future__ import annotations

import asyncio
import csv
import json
import math
import os
import random
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Awaitable, Callable, Dict, List, Optional, Sequence, Tuple

from eval.dataset import EvaluationCase, KB_FRAGMENTS
from eval.metrics import evaluate_explanation, extract_numbers_from_text
from src.models.schemas import ExplainRequest, ExplainResponse, FindingInput, KbFragmentInput
from src.providers.base import BaseLLMProvider

_MAX_CASES = 50
_WILSON_Z_95 = 1.959963984540054

EXPERT_ISSUE_CATEGORIES: Tuple[str, ...] = (
    "implicit_conversion",
    "missing_index",
    "costly_full_scan",
    "key_lookup",
    "parameter_sniffing",
    "none",
    "other",
)
ENGINE_AGREEMENT_LABELS: Tuple[str, ...] = (
    "total",
    "parcial",
    "desacuerdo",
    "falso_positivo",
    "omision",
)
AI_VALIDITY_LABELS: Tuple[str, ...] = (
    "valida",
    "parcialmente_valida",
    "incorrecta",
    "no_aplica",
)

_ALLOWLISTED_FILENAMES = frozenset(
    {
        "INSTRUCCIONES_ESPECIALISTA.md",
        "cuaderno_etapa_a_ciego.md",
        "planilla_etapa_a.csv",
        "cuaderno_etapa_b_contraste.md",
        "planilla_etapa_b.csv",
        "ACTA_ESPECIALISTA.md",
        "specialist_manifest.json",
    }
)

_CATEGORY_TO_RULES: Dict[str, Tuple[str, ...]] = {
    "implicit_conversion": ("REG-01",),
    "missing_index": ("REG-02",),
    "costly_full_scan": ("REG-03",),
    "key_lookup": ("REG-04",),
}

_DEFAULT_INSTRUCTIONS_MD = """# Guía de Evaluación para el Especialista — `S3-04` (`RF-EVA-03`)

**Especialista designado:** Aisner Marrugo  
**Muestra de evaluación:** {total_cases} consultas reales de SQL Server (`QD_Lab` y carga `TPC-H`)

Muchas gracias por participar en el contraste diagnóstico independiente de **Query Doctor AI**.  
Para garantizar rigor metodológico y evitar sesgo de confirmación, el protocolo se divide en **dos etapas secuenciales**:

---

## Paso 1 — Etapa A (Evaluación CIEGA)

1. Abre **únicamente** [cuaderno_etapa_a_ciego.md](./cuaderno_etapa_a_ciego.md) y [planilla_etapa_a.csv](./planilla_etapa_a.csv). *(No abras todavía los archivos de Etapa B).*
2. Cada caso (`CASO-01` .. `CASO-{total_cases:02d}`) muestra el texto SQL, las métricas de ejecución y el resumen de operadores del plan Showplan XML (junto con la ruta al `.sqlplan` original por si deseas abrirlo en SSMS o Azure Data Studio).
3. En `planilla_etapa_a.csv` completa por cada caso:
   - **`expert_has_issue`**: `si` o `no` (¿tiene esta consulta un problema accionable de rendimiento?).
   - **`expert_categories`**: uno o varios códigos separados por `;` de la lista:
     - `implicit_conversion` · `missing_index` · `costly_full_scan` · `key_lookup` · `parameter_sniffing` · `none` · `other`
   - **`expert_target_objects`**: nodo(s), tabla(s) o índice(s) críticos observados.
   - **`expert_recommendation`**: recomendación técnica principal y precauciones operativas (p. ej. costo de escritura de índices).
   - **`expert_priority_rank`** *(opcional, paso exploratorio `RF-EVA-05`)*: ordena los casos del **`1`** (más urgente/crítica de atender) al **`{total_cases}`** (menor prioridad), sin repetir números si es posible.
   - **`notes`**: observaciones libres.

---

## Paso 2 — Etapa B (Contraste frente a Query Doctor AI)

1. Una vez terminada la Etapa A, abre [cuaderno_etapa_b_contraste.md](./cuaderno_etapa_b_contraste.md) y [planilla_etapa_b.csv](./planilla_etapa_b.csv).
2. Compara tu criterio previo con los hallazgos determinísticos, el *Query Health Score* y la explicación generada por IA:
   - **`engine_agreement`**: `total` | `parcial` | `desacuerdo` | `falso_positivo` | `omision`
   - **`ai_validity`**: `valida` | `parcialmente_valida` | `incorrecta` | `no_aplica` *(usar `no_aplica` cuando el motor no emitió hallazgos ni explicación IA)*
   - **`ai_utility_1_to_5`**: entero del `1` (poco útil) al `5` (muy útil para diagnosticar), o `no_aplica`
   - **`observations`**: matices faltantes, falsos positivos o discrepancias técnicas.

---

## Paso 3 — Cierre formal

Revisa y confirma [ACTA_ESPECIALISTA.md](./ACTA_ESPECIALISTA.md) junto con ambas planillas CSV diligenciadas.
"""


def _resolve_contained_dir(target_dir: Path, allowed_root: Path) -> Path:
    """Garantiza contención estricta de directorios dentro de la raíz permitida (CWE-22)."""
    real_root = os.path.realpath(str(allowed_root))
    real_target = os.path.realpath(str(target_dir))
    if real_target != real_root and not real_target.startswith(real_root + os.sep):
        raise ValueError(f"Ruta fuera del directorio permitido: {target_dir}")
    Path(real_target).mkdir(parents=True, exist_ok=True)
    return Path(real_target)


def _write_allowlisted_text(safe_dir: Path, filename: str, content: str, encoding: str = "utf-8") -> Path:
    """Escribe exclusivamente nombres de archivo en lista blanca dentro de `safe_dir` (S2083)."""
    if filename not in _ALLOWLISTED_FILENAMES:
        raise ValueError(f"Nombre de archivo no permitido en paquete especialista: {filename!r}")
    target = safe_dir / filename
    with open(target, "w", encoding=encoding, newline="" if filename.endswith(".csv") else None) as handle:
        handle.write(content)
    return target


def _write_csv_utf8_bom(
    safe_dir: Path,
    filename: str,
    fieldnames: Sequence[str],
    rows: Sequence[Dict[str, Any]],
) -> Path:
    if filename not in _ALLOWLISTED_FILENAMES:
        raise ValueError(f"Archivo CSV no permitido: {filename!r}")
    target = safe_dir / filename
    with open(target, "w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        for row in rows:
            writer.writerow({k: row.get(k, "") for k in fieldnames})
    return target


def read_specialist_csv(csv_path: Path, expected_columns: Sequence[str]) -> List[Dict[str, str]]:
    """Lee una planilla CSV verificando sus columnas requeridas."""
    if not csv_path.exists():
        raise FileNotFoundError(f"No se encontró la planilla CSV: {csv_path}")
    with open(csv_path, "r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        if not reader.fieldnames:
            raise ValueError(f"La planilla {csv_path} no contiene encabezados.")
        missing = [c for c in expected_columns if c not in reader.fieldnames]
        if missing:
            raise ValueError(f"Faltan columnas requeridas en {csv_path}: {missing}")
        rows = [{k: (v or "").strip() for k, v in row.items()} for row in reader]
    if not rows or len(rows) > _MAX_CASES:
        raise ValueError(f"Cantidad inválida de filas en {csv_path}: {len(rows)}")
    return rows


def _collect_kb_fragments_for_rules(rule_ids: Sequence[str]) -> List[KbFragmentInput]:
    selected: List[KbFragmentInput] = []
    seen_ids = set()
    for rule_id in sorted(set(rule_ids)):
        for suffix in ("INTERP", "RECOM"):
            frag = KB_FRAGMENTS.get(f"{rule_id}-{suffix}")
            if frag and frag.fragment_id not in seen_ids:
                selected.append(frag)
                seen_ids.add(frag.fragment_id)
    return selected


def _extract_numeric_evidence(exported_case: Dict[str, Any]) -> Dict[str, float]:
    """Extrae cifras verificables reales del caso exportado para validar concordancia numérica."""
    nums: Dict[str, float] = {}
    ctx = exported_case.get("context") or {}
    for key in ("query_execution_count", "query_consumption_pct", "avg_rows_returned"):
        val = ctx.get(key)
        if isinstance(val, (int, float)):
            nums[key] = float(val)

    for finding in exported_case.get("findings", []):
        f_ctx = finding.get("context") or {}
        for k, v in f_ctx.items():
            if isinstance(v, (int, float)):
                nums[f"{finding.get('finding_id')}.{k}"] = float(v)
        ev = finding.get("evidence") or {}
        for k, v in ev.items():
            if isinstance(v, (int, float)):
                nums[f"{finding.get('finding_id')}.ev.{k}"] = float(v)
            elif isinstance(v, str):
                for num_idx, parsed_num in enumerate(extract_numbers_from_text(v)):
                    nums[f"{finding.get('finding_id')}.ev.{k}.{num_idx}"] = parsed_num
    return nums


def build_explain_request_from_exported_case(exported_case: Dict[str, Any]) -> Optional[Tuple[ExplainRequest, EvaluationCase]]:
    """Convierte un caso exportado por QueryDoctor.EngineHarness en ExplainRequest + EvaluationCase."""
    raw_findings = list(exported_case.get("findings") or [])
    if not raw_findings:
        return None

    finding_inputs: List[FindingInput] = []
    rule_ids: List[str] = []
    finding_ids: List[str] = []

    for f in raw_findings:
        rule_id = f["rule"]["id"]
        rule_ids.append(rule_id)
        fid = f["finding_id"]
        finding_ids.append(fid)

        nodes = [
            f"Nodo #{n['node_id']} ({n['physical_op']})"
            for n in (f.get("nodes") or [])
        ]
        if not nodes and rule_id == "REG-02":
            nodes = ["MissingIndexes"]

        metrics: Dict[str, Any] = {}
        for k, v in (f.get("context") or {}).items():
            if v is not None:
                metrics[k] = v
        for k, v in (f.get("evidence") or {}).items():
            if v is not None and str(v).strip():
                metrics[k] = v

        desc = (
            f"Regla {rule_id} ({f['rule']['name']}) en {', '.join(nodes)} "
            f"[severidad={f['severity']}, evidencia={f['evidence_class']}]."
        )
        finding_inputs.append(
            FindingInput(
                finding_id=fid,
                rule_id=rule_id,
                rule_name=f["rule"]["name"],
                evidence_class=f["evidence_class"],
                severity=f["severity"],
                target_nodes=nodes,
                metrics=metrics,
                description=desc,
            )
        )

    kb_frags = _collect_kb_fragments_for_rules(rule_ids)
    ctx = exported_case.get("context") or {}
    query_metrics: Dict[str, Any] = {}
    if ctx.get("query_execution_count") is not None:
        query_metrics["execution_count"] = ctx["query_execution_count"]
    if ctx.get("query_consumption_pct") is not None:
        query_metrics["query_consumption_pct"] = ctx["query_consumption_pct"]
    if ctx.get("avg_rows_returned") is not None:
        query_metrics["avg_rows_returned"] = ctx["avg_rows_returned"]

    req = ExplainRequest(
        query_id=exported_case["case_id"],
        sql_text=exported_case.get("masked_sql_text") or exported_case["sql_text"],
        findings=finding_inputs,
        kb_fragments=kb_frags,
        metrics=query_metrics or None,
        user_role="DBA",
        correlation_id=f"s3-04-{exported_case['case_id']}",
    )

    eval_case = EvaluationCase(
        case_id=exported_case["case_id"],
        name=exported_case.get("description", exported_case["case_id"]),
        category="specialist_contrast",
        request=req,
        ground_truth_findings=finding_ids,
        ground_truth_rules=sorted(set(rule_ids)),
        ground_truth_metrics=_extract_numeric_evidence(exported_case),
        expected_fragments=[frag.fragment_id for frag in kb_frags],
    )
    return req, eval_case


async def run_end_to_end_ai_for_cases(
    engine_bundle: Dict[str, Any],
    provider: BaseLLMProvider,
    profile_name: str,
    model_name: str,
    prompt_version: str = "v1.1",
    delay_seconds: float = 0.0,
    checkpoint_path: Optional[Path] = None,
    sleep_fn: Callable[[float], Awaitable[None]] = asyncio.sleep,
) -> Dict[str, Any]:
    """Ejecuta el servicio de IA sobre los casos reales exportados por el motor .NET."""
    raw_cases = list(engine_bundle.get("cases") or [])
    if not raw_cases or len(raw_cases) > _MAX_CASES:
        raise ValueError(f"Número inválido de casos en bundle del motor: {len(raw_cases)}")

    cached_by_id: Dict[str, Dict[str, Any]] = {}
    if checkpoint_path and checkpoint_path.exists():
        with open(checkpoint_path, "r", encoding="utf-8") as handle:
            ckpt = json.load(handle)
        for entry in ckpt.get("cases", []):
            if entry.get("case_id"):
                cached_by_id[entry["case_id"]] = entry

    output_cases: List[Dict[str, Any]] = []
    calls_made = 0

    for exported in raw_cases:
        cid = exported["case_id"]
        if cid in cached_by_id and cached_by_id[cid].get("ai_status") in ("TRAZADA", "NO_VERIFICADA", "OMITIDA_SIN_HALLAZGOS"):
            output_cases.append(cached_by_id[cid])
            continue

        built = build_explain_request_from_exported_case(exported)
        if built is None:
            record = {
                **exported,
                "ai_status": "OMITIDA_SIN_HALLAZGOS",
                "ai_explanation": None,
                "ai_validation": None,
            }
            output_cases.append(record)
            cached_by_id[cid] = record
            continue

        req, eval_case = built
        if calls_made > 0 and delay_seconds > 0:
            await sleep_fn(delay_seconds)

        response: ExplainResponse = await provider.generate_explanation(req)
        calls_made += 1

        validation = evaluate_explanation(response, eval_case)
        ai_status = "TRAZADA" if validation.is_trazada else "NO_VERIFICADA"

        record = {
            **exported,
            "ai_status": ai_status,
            "ai_explanation": response.model_dump(mode="json"),
            "ai_validation": {
                "supported_rate_pct": validation.supported_rate_pct,
                "numeric_concordance_pct": validation.numeric_concordance_pct,
                "is_trazada": validation.is_trazada,
                "failure_reasons": list(validation.failure_reasons),
            },
        }
        output_cases.append(record)
        cached_by_id[cid] = record

        if checkpoint_path:
            checkpoint_path.parent.mkdir(parents=True, exist_ok=True)
            with open(checkpoint_path, "w", encoding="utf-8") as handle:
                json.dump({"cases": list(cached_by_id.values())}, handle, indent=2, ensure_ascii=False)

    return {
        "schema_version": "1.0.0",
        "metadata": {
            "llm_profile": profile_name,
            "llm_model": model_name,
            "prompt_version": prompt_version,
            "catalog_version": engine_bundle.get("catalog_version", "1.0.0"),
            "thresholds_hash": engine_bundle.get("thresholds_hash", ""),
            "score_parameters_hash": engine_bundle.get("score_parameters_hash", ""),
            "total_cases": len(output_cases),
        },
        "cases": output_cases,
    }


def _format_blind_stage_a_case(blind_id: str, case: Dict[str, Any]) -> List[str]:
    ctx = case.get("context") or {}
    plan = case.get("plan_summary") or {}
    corpus_label = (
        "Carga analítica TPC-H"
        if str(case.get("corpus", "")).lower() == "tpch"
        else "Escenario de laboratorio SQL Server"
    )
    lines: List[str] = [
        f"## {blind_id}",
        "",
        f"- **Entorno:** {corpus_label}",
        f"- **Métricas de ejecución:** ejecuciones=`{ctx.get('query_execution_count')}`, "
        f"consumo ponderado en ventana=`{ctx.get('query_consumption_pct')}` %, "
        f"filas devueltas promedio=`{ctx.get('avg_rows_returned')}`",
        f"- **Costo total estimado del subárbol:** `{plan.get('statement_subtree_cost')}` | "
        f"**Nivel optimización:** `{plan.get('statement_optm_level') or 'N/A'}` "
        f"(aborto temprano: `{plan.get('statement_optm_early_abort_reason') or 'ninguno'}`)",
        "",
        "### Consulta SQL",
        "```sql",
        case.get("sql_text", "").strip(),
        "```",
        "",
        "### Resumen estructural del plan Showplan XML",
        "",
        "| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |",
        "|---:|---|---|---:|---:|---:|---|",
    ]
    for op in (plan.get("operators") or [])[:25]:
        est_reads = op.get("estimated_rows_read")
        reads_str = f"{est_reads:.1f}" if isinstance(est_reads, (int, float)) else "-"
        lines.append(
            f"| #{op['node_id']} | `{op['physical_op']}` | `{op['logical_op']}` | "
            f"{op['estimate_rows']:.1f} | {reads_str} | {op['estimated_total_subtree_cost']:.4f} | "
            f"`{op.get('target_object') or '-'}` |"
        )

    warnings = list(plan.get("warnings") or [])
    if warnings:
        lines.append("")
        lines.append("**Advertencias XML presentes en el plan:**")
        for w in warnings:
            lines.append(f"- `{w}`")

    missing = list(plan.get("missing_indexes") or [])
    if missing:
        lines.append("")
        lines.append("**Sugerencias XML `<MissingIndexes>` del optimizador SQL Server:**")
        for m in missing:
            lines.append(f"- `{m}`")

    lines.extend(["", "---", ""])
    return lines


def _format_stage_b_contrast_case(blind_id: str, case: Dict[str, Any]) -> List[str]:
    hs = case.get("health_score") or {}
    findings = list(case.get("findings") or [])
    ai_status = case.get("ai_status", "NO_GENERADA")
    ai_expl = case.get("ai_explanation")

    lines: List[str] = [
        f"## {blind_id} (`{case['case_id']}`)",
        "",
        f"- **Query Health Score:** `{hs.get('score')}` / 100 | **Banda:** `{hs.get('band')}` | "
        f"**Ranking de prioridad del sistema:** `#{hs.get('rank_by_score_asc')}` de {case.get('window_size', 10)}",
        "",
        "### 1. Hallazgos del motor determinístico (`S3-01` calibrado)",
    ]

    if not findings:
        lines.append("- *El motor determinístico no emitió hallazgos del catálogo activo (`REG-01`..`REG-04`) para este plan.*")
    else:
        for f in findings:
            nodes = ", ".join(f"Nodo #{n['node_id']} ({n['physical_op']})" for n in (f.get("nodes") or [])) or "Plan global"
            lines.append(
                f"- **`{f['finding_id']}` · `{f['rule']['id']}` ({f['rule']['name']})** — "
                f"Severidad: `{f['severity']}` | Evidencia: `{f['evidence_class']}` | Nodos: {nodes}"
            )
            ev = f.get("evidence") or {}
            if ev:
                ev_str = ", ".join(f"`{k}={v}`" for k, v in ev.items() if v is not None)
                lines.append(f"  - Evidencia extraída: {ev_str}")

    lines.extend(["", f"### 2. Explicación generada por IA (Estado: `{ai_status}`)"])
    if not ai_expl:
        lines.append("- *Explicación omitida por diseño (`RF-IA-01`): no se generan afirmaciones cuando no existen hallazgos determinísticos.*")
    else:
        lines.append(f"- **Resumen:** {ai_expl.get('summary', '')}")
        lines.append("")
        lines.append("| # | Tipo | Afirmación | Evidencia (`finding_ids`) | Fragmento KB |")
        lines.append("|---:|---|---|---|---|")
        for idx, claim in enumerate(ai_expl.get("claims") or [], start=1):
            fids = ", ".join(claim.get("finding_ids") or [])
            kbid = claim.get("kb_fragment_id") or "-"
            stmt = str(claim.get("statement", "")).replace("|", "\\|").replace("\n", " ")
            lines.append(f"| {idx} | `{claim['claim_type']}` | {stmt} | `{fids}` | `{kbid}` |")

    lines.extend(["", "---", ""])
    return lines


def _build_charter_template_md(meta: Dict[str, Any], total_cases: int) -> str:
    return "\n".join(
        [
            "# Acta de Validación Diagnóstica por Especialista — `S3-04` (`RF-EVA-03`, `DA-10`)",
            "",
            "## 1. Identificación de la sesión",
            "",
            "| Campo | Valor |",
            "| :--- | :--- |",
            "| **Especialista evaluador** | Aisner Marrugo *(Director / Docente experto en Bases de Datos)* |",
            "| **Proyecto evaluado** | Query Doctor AI (Versión 1.0 — Proyecto de Ingeniería I, UTB) |",
            "| **Modalidad de evaluación** | Revisión independiente asíncrona en 2 etapas (Etapa A ciega $\\rightarrow$ Etapa B contraste) |",
            f"| **Tamaño de muestra (`DA-10`)** | **{total_cases} consultas** reales (`5` escenarios sembrados `QD_Lab` + `5` carga `TPC-H`) |",
            f"| **Versión catálogo motor / umbrales** | `{meta.get('catalog_version', '1.0.0')}` / `{meta.get('thresholds_hash', '')[:16]}...` |",
            f"| **Perfil IA / modelo / prompt** | `{meta.get('llm_profile', '')}` / `{meta.get('llm_model', '')}` / `{meta.get('prompt_version', '')}` |",
            "| **Fecha de diligenciamiento** | `____ / ____ / 2026` |",
            "",
            "---",
            "",
            "## 2. Declaración metodológica",
            "",
            "1. Durante la **Etapa A**, el especialista examinó cada consulta (`CASO-01` .. `CASO-10`), su plan Showplan XML y métricas agregadas **sin conocer** los códigos `REG-xx`, puntajes *Query Health Score* ni explicaciones generadas por **Query Doctor AI**.",
            "2. Durante la **Etapa B**, el especialista contrastó sus anotaciones iniciales contra las salidas reales del motor determinístico calibrado y del servicio de IA trazado.",
            "",
            "---",
            "",
            "## 3. Síntesis y observaciones del especialista",
            "",
            "- **Concordancia general observada (motor determinístico):** `[Completar tras ejecutar score o resumir valoración cualitativa]`",
            "- **Valoración técnica y utilidad de las explicaciones de IA:** `[Indicar observaciones sobre precisión, tono y seguridad operativa]`",
            "- **Limitaciones o casos fuera de catálogo identificados:** `[Ej. fenómenos como parameter sniffing o recomendaciones adicionales]`",
            "- **Criterio frente al ordenamiento de prioridad exploratorio (`RF-EVA-05`):** `[Indicar acuerdo o diferencias respecto al Query Health Score]`",
            "",
            "---",
            "",
            "## 4. Aval de cierre de `S3-04` (`RF-EVA-03`)",
            "",
            "- **Nombre y rol:** Aisner Marrugo — Especialista / Director de Proyecto",
            "- **Aprobación:** `[ ] Aprobado con observaciones registradas   [ ] Requiere ajustes`",
            "- **Firma / registro de conformidad:** `______________________________`",
            "",
        ]
    )


def prepare_specialist_pack(
    system_outputs: Dict[str, Any],
    out_dir: Path,
    allowed_root: Path,
    seed: int = 20261119,
) -> Dict[str, Any]:
    """Genera el paquete ciego (Etapa A) y de contraste (Etapa B) para el especialista."""
    cases = list(system_outputs.get("cases") or [])
    if not cases or len(cases) > _MAX_CASES:
        raise ValueError(f"Número de casos inválido: {len(cases)}")

    safe_dir = _resolve_contained_dir(out_dir, allowed_root)
    shuffled = list(cases)
    rng = random.Random(seed)
    rng.shuffle(shuffled)

    stage_a_lines: List[str] = [
        "# Cuaderno de Evaluación Diagnóstica (Etapa A — Ciega) — `S3-04`",
        "",
        "> **Importante:** Evalúa cada caso y registra tu criterio en `planilla_etapa_a.csv` **antes** de abrir los archivos de la Etapa B. "
        "El orden ha sido aleatorizado y no expone identificadores de reglas ni diagnósticos de Query Doctor AI.",
        "",
    ]
    stage_b_lines: List[str] = [
        "# Cuaderno de Contraste frente a Query Doctor AI (Etapa B) — `S3-04`",
        "",
        "> **Instrucción:** Abre este cuaderno **después** de completar `planilla_etapa_a.csv`. "
        "Califica la concordancia en `planilla_etapa_b.csv`.",
        "",
    ]

    stage_a_rows: List[Dict[str, Any]] = []
    stage_b_rows: List[Dict[str, Any]] = []
    manifest_entries: List[Dict[str, Any]] = []
    total_cases = len(shuffled)

    for idx, case in enumerate(shuffled, start=1):
        blind_id = f"CASO-{idx:02d}"
        case_with_win = {**case, "window_size": total_cases}
        stage_a_lines.extend(_format_blind_stage_a_case(blind_id, case_with_win))
        stage_b_lines.extend(_format_stage_b_contrast_case(blind_id, case_with_win))

        has_ai = case.get("ai_explanation") is not None
        stage_a_rows.append(
            {
                "case_label": blind_id,
                "expert_has_issue": "",
                "expert_categories": "",
                "expert_target_objects": "",
                "expert_recommendation": "",
                "expert_priority_rank": "",
                "notes": "",
            }
        )
        stage_b_rows.append(
            {
                "case_label": blind_id,
                "engine_agreement": "",
                "ai_validity": "" if has_ai else "no_aplica",
                "ai_utility_1_to_5": "" if has_ai else "no_aplica",
                "observations": "",
            }
        )

        emitted_rules = sorted({f["rule"]["id"] for f in (case.get("findings") or [])})
        hs = case.get("health_score") or {}
        manifest_entries.append(
            {
                "case_label": blind_id,
                "case_id": case["case_id"],
                "corpus": case.get("corpus", ""),
                "split": case.get("split", ""),
                "emitted_rules": emitted_rules,
                "has_findings": len(emitted_rules) > 0,
                "has_ai_explanation": has_ai,
                "system_health_score": hs.get("score"),
                "system_priority_rank": hs.get("rank_by_score_asc"),
            }
        )

    _write_allowlisted_text(
        safe_dir,
        "INSTRUCCIONES_ESPECIALISTA.md",
        _DEFAULT_INSTRUCTIONS_MD.format(total_cases=total_cases),
    )
    _write_allowlisted_text(safe_dir, "cuaderno_etapa_a_ciego.md", "\n".join(stage_a_lines))
    _write_allowlisted_text(safe_dir, "cuaderno_etapa_b_contraste.md", "\n".join(stage_b_lines))
    _write_csv_utf8_bom(
        safe_dir,
        "planilla_etapa_a.csv",
        (
            "case_label",
            "expert_has_issue",
            "expert_categories",
            "expert_target_objects",
            "expert_recommendation",
            "expert_priority_rank",
            "notes",
        ),
        stage_a_rows,
    )
    _write_csv_utf8_bom(
        safe_dir,
        "planilla_etapa_b.csv",
        ("case_label", "engine_agreement", "ai_validity", "ai_utility_1_to_5", "observations"),
        stage_b_rows,
    )
    _write_allowlisted_text(
        safe_dir,
        "ACTA_ESPECIALISTA.md",
        _build_charter_template_md(system_outputs.get("metadata") or {}, total_cases),
    )

    manifest = {
        "seed": seed,
        "total_cases": total_cases,
        "metadata": dict(system_outputs.get("metadata") or {}),
        "entries": manifest_entries,
    }
    _write_allowlisted_text(
        safe_dir,
        "specialist_manifest.json",
        json.dumps(manifest, indent=2, ensure_ascii=False),
    )
    return manifest


def _wilson_ci_95(successes: int, total: int) -> Tuple[float, float]:
    if total <= 0:
        return (0.0, 0.0)
    z2 = _WILSON_Z_95 ** 2
    p = successes / float(total)
    denom = 1.0 + z2 / total
    center = (p + z2 / (2.0 * total)) / denom
    half = (_WILSON_Z_95 * math.sqrt((p * (1.0 - p) + z2 / (4.0 * total)) / total)) / denom
    return (max(0.0, (center - half) * 100.0), min(100.0, (center + half) * 100.0))


def _binary_kappa(a_labels: Sequence[bool], b_labels: Sequence[bool]) -> Tuple[float, Optional[float]]:
    n = len(a_labels)
    if n == 0:
        return (0.0, None)
    agree = sum(1 for a, b in zip(a_labels, b_labels) if a == b)
    po = agree / float(n)
    pa_true = sum(1 for a in a_labels if a) / float(n)
    pb_true = sum(1 for b in b_labels if b) / float(n)
    pe = (pa_true * pb_true) + ((1.0 - pa_true) * (1.0 - pb_true))
    if math.isclose(1.0 - pe, 0.0, abs_tol=1e-12):
        return (po, 1.0 if math.isclose(po, 1.0, abs_tol=1e-12) else None)
    return (po, (po - pe) / (1.0 - pe))


def _average_ranks(values: Sequence[float]) -> List[float]:
    n = len(values)
    indexed = sorted(enumerate(values), key=lambda x: x[1])
    ranks = [0.0] * n
    i = 0
    while i < n:
        j = i
        while j + 1 < n and math.isclose(indexed[j + 1][1], indexed[i][1], abs_tol=1e-9):
            j += 1
        avg_r = ((i + 1) + (j + 1)) / 2.0
        for k in range(i, j + 1):
            ranks[indexed[k][0]] = avg_r
        i = j + 1
    return ranks


def _pearson(xs: Sequence[float], ys: Sequence[float]) -> Optional[float]:
    if len(xs) < 2 or len(xs) != len(ys):
        return None
    mx = sum(xs) / len(xs)
    my = sum(ys) / len(ys)
    num = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
    denx = math.sqrt(sum((x - mx) ** 2 for x in xs))
    deny = math.sqrt(sum((y - my) ** 2 for y in ys))
    if math.isclose(denx * deny, 0.0, abs_tol=1e-12):
        return None
    return num / (denx * deny)


def compute_rank_correlation(
    system_ranks: Sequence[float],
    expert_ranks: Sequence[float],
    case_ids: Sequence[str],
) -> Optional[Dict[str, Any]]:
    """Calcula Spearman rho, Kendall tau-b y coincidencia Top-k para ordenamiento exploratorio (RF-EVA-05)."""
    n = len(system_ranks)
    if n < 3 or len(expert_ranks) != n or len(case_ids) != n:
        return None

    rx = _average_ranks(system_ranks)
    ry = _average_ranks(expert_ranks)
    spearman = _pearson(rx, ry)

    concordant = 0
    discordant = 0
    ties_x = 0
    ties_y = 0
    for i in range(n):
        for j in range(i + 1, n):
            dx = system_ranks[i] - system_ranks[j]
            dy = expert_ranks[i] - expert_ranks[j]
            if math.isclose(dx, 0.0, abs_tol=1e-9) and math.isclose(dy, 0.0, abs_tol=1e-9):
                continue
            if math.isclose(dx, 0.0, abs_tol=1e-9):
                ties_x += 1
            elif math.isclose(dy, 0.0, abs_tol=1e-9):
                ties_y += 1
            elif dx * dy > 0:
                concordant += 1
            else:
                discordant += 1

    total_pairs = n * (n - 1) // 2
    denom = math.sqrt(max(0, (total_pairs - ties_x) * (total_pairs - ties_y)))
    kendall_tau_b = ((concordant - discordant) / denom) if denom > 0 else None

    sys_order = [cid for _, cid in sorted(zip(system_ranks, case_ids))]
    exp_order = [cid for _, cid in sorted(zip(expert_ranks, case_ids))]
    top3_overlap = len(set(sys_order[:3]) & set(exp_order[:3])) if n >= 3 else 0
    top5_overlap = len(set(sys_order[:5]) & set(exp_order[:5])) if n >= 5 else len(set(sys_order) & set(exp_order))

    return {
        "evaluated_cases": n,
        "spearman_rho": round(spearman, 4) if spearman is not None else None,
        "kendall_tau_b": round(kendall_tau_b, 4) if kendall_tau_b is not None else None,
        "top_3_overlap": top3_overlap,
        "top_5_overlap": top5_overlap,
    }


def score_specialist_contrast(
    manifest: Dict[str, Any],
    stage_a_rows: Sequence[Dict[str, str]],
    stage_b_rows: Sequence[Dict[str, str]],
    specialist_name: str = "Aisner Marrugo",
) -> Dict[str, Any]:
    """Calcula las métricas cuantitativas y cualitativas del contraste con especialista (RF-EVA-03)."""
    entries = {e["case_label"]: e for e in (manifest.get("entries") or [])}
    if not entries or len(entries) > _MAX_CASES:
        raise ValueError("Manifiesto inválido o vacío para S3-04.")

    a_by_label = {r["case_label"]: r for r in stage_a_rows}
    b_by_label = {r["case_label"]: r for r in stage_b_rows}
    if set(a_by_label.keys()) != set(entries.keys()) or set(b_by_label.keys()) != set(entries.keys()):
        raise ValueError("Las planillas de Etapa A y Etapa B deben coincidir exactamente con specialist_manifest.json.")

    sys_issues: List[bool] = []
    exp_issues: List[bool] = []
    agreement_counts: Dict[str, int] = dict.fromkeys(ENGINE_AGREEMENT_LABELS, 0)
    ai_validity_counts: Dict[str, int] = dict.fromkeys(AI_VALIDITY_LABELS, 0)
    utilities: List[int] = []

    sys_ranks: List[float] = []
    exp_ranks: List[float] = []
    rank_case_ids: List[str] = []
    case_details: List[Dict[str, Any]] = []

    for label in sorted(entries.keys()):
        entry = entries[label]
        ra = a_by_label[label]
        rb = b_by_label[label]

        exp_issue_raw = ra["expert_has_issue"].lower()
        if exp_issue_raw not in ("si", "sí", "no"):
            raise ValueError(f"Valor inválido de expert_has_issue en {label}: {ra['expert_has_issue']!r}")
        exp_issue = exp_issue_raw in ("si", "sí")

        raw_cats = [c.strip().lower() for c in ra["expert_categories"].split(";") if c.strip()]
        invalid_cats = [c for c in raw_cats if c not in EXPERT_ISSUE_CATEGORIES]
        if invalid_cats:
            raise ValueError(f"Categorías inválidas en {label}: {invalid_cats}")

        eng_agree = rb["engine_agreement"].lower()
        if eng_agree not in ENGINE_AGREEMENT_LABELS:
            raise ValueError(f"Valor inválido de engine_agreement en {label}: {rb['engine_agreement']!r}")
        agreement_counts[eng_agree] += 1

        ai_val = rb["ai_validity"].lower()
        if ai_val not in AI_VALIDITY_LABELS:
            raise ValueError(f"Valor inválido de ai_validity en {label}: {rb['ai_validity']!r}")
        ai_validity_counts[ai_val] += 1

        raw_util = rb["ai_utility_1_to_5"].lower()
        util_val: Optional[int] = None
        if raw_util and raw_util != "no_aplica":
            util_val = int(raw_util)
            if util_val < 1 or util_val > 5:
                raise ValueError(f"Utilidad fuera de rango 1..5 en {label}: {util_val}")
            utilities.append(util_val)

        if ra["expert_priority_rank"] and entry.get("system_priority_rank") is not None:
            exp_rank = float(ra["expert_priority_rank"])
            exp_ranks.append(exp_rank)
            sys_ranks.append(float(entry["system_priority_rank"]))
            rank_case_ids.append(entry["case_id"])

        sys_issues.append(bool(entry["has_findings"]))
        exp_issues.append(exp_issue)

        inferred_rules = sorted(
            {rule for cat in raw_cats for rule in _CATEGORY_TO_RULES.get(cat, ())}
        )
        case_details.append(
            {
                "case_label": label,
                "case_id": entry["case_id"],
                "corpus": entry["corpus"],
                "emitted_rules": entry["emitted_rules"],
                "expert_has_issue": exp_issue,
                "expert_categories": raw_cats,
                "inferred_catalog_rules": inferred_rules,
                "engine_agreement": eng_agree,
                "ai_validity": ai_val,
                "ai_utility_1_to_5": util_val,
                "expert_priority_rank": ra["expert_priority_rank"] or None,
                "system_priority_rank": entry.get("system_priority_rank"),
                "expert_recommendation": ra["expert_recommendation"],
                "observations": rb["observations"],
            }
        )

    po_binary, kappa_binary = _binary_kappa(sys_issues, exp_issues)
    ai_evaluated = sum(ai_validity_counts[k] for k in ("valida", "parcialmente_valida", "incorrecta"))
    ai_valid_strict = ai_validity_counts["valida"]
    ai_valid_acceptable = ai_validity_counts["valida"] + ai_validity_counts["parcialmente_valida"]
    ai_valid_pct = (ai_valid_strict / ai_evaluated * 100.0) if ai_evaluated > 0 else 0.0
    ai_ci_low, ai_ci_high = _wilson_ci_95(ai_valid_strict, ai_evaluated)

    engine_ok = agreement_counts["total"] + agreement_counts["parcial"]
    total_cases = len(entries)

    return {
        "metadata": {
            **dict(manifest.get("metadata") or {}),
            "specialist_name": specialist_name,
            "total_cases": total_cases,
        },
        "engine_vs_specialist": {
            "binary_issue_agreement_pct": round(po_binary * 100.0, 2),
            "binary_issue_kappa": round(kappa_binary, 4) if kappa_binary is not None else None,
            "agreement_counts": agreement_counts,
            "exact_or_partial_agreement_pct": round((engine_ok / total_cases) * 100.0, 2),
        },
        "ai_vs_specialist": {
            "evaluated_explanations": ai_evaluated,
            "validity_counts": ai_validity_counts,
            "strict_valid_pct": round(ai_valid_pct, 2),
            "acceptable_pct": round((ai_valid_acceptable / ai_evaluated * 100.0) if ai_evaluated > 0 else 0.0, 2),
            "strict_valid_wilson_95_ci_pct": [round(ai_ci_low, 2), round(ai_ci_high, 2)],
            "mean_utility_1_to_5": round(sum(utilities) / len(utilities), 2) if utilities else None,
        },
        "priority_ranking_exploratory_rf_eva_05": compute_rank_correlation(
            sys_ranks, exp_ranks, rank_case_ids
        ),
        "cases": case_details,
    }


def render_specialist_contrast_markdown(report: Dict[str, Any]) -> str:
    """Genera el informe Markdown formal de resultados de S3-04 (RF-EVA-03, RF-EVA-04)."""
    meta = report["metadata"]
    eng = report["engine_vs_specialist"]
    ai = report["ai_vs_specialist"]
    rank = report.get("priority_ranking_exploratory_rf_eva_05")
    ci = ai["strict_valid_wilson_95_ci_pct"]

    lines: List[str] = [
        "# Informe de Contraste Independiente con Especialista — `S3-04` (`RF-EVA-03` v1.1)",
        "",
        f"> **Especialista evaluador:** {meta.get('specialist_name')} | **Muestra:** `N = {meta.get('total_cases')}` "
        f"(`llm_profile={meta.get('llm_profile')}`, `model={meta.get('llm_model')}`, `prompt={meta.get('prompt_version')}`).",
        "",
        "## 1. Resumen Ejecutivo (`RF-EVA-03`)",
        "",
        "| Dimensión evaluada | Resultado medido | Detalle |",
        "| :--- | :---: | :--- |",
        f"| Concordancia binaria detección problema (Motor vs. Especialista) | **{eng['binary_issue_agreement_pct']:.2f} %** | "
        f"Kappa $\\kappa$ = `{eng['binary_issue_kappa']}` |",
        f"| Acuerdo diagnóstico total o parcial del motor | **{eng['exact_or_partial_agreement_pct']:.2f} %** | "
        f"`total={eng['agreement_counts']['total']}`, `parcial={eng['agreement_counts']['parcial']}`, "
        f"`desacuerdo={eng['agreement_counts']['desacuerdo']}`, `falso_positivo={eng['agreement_counts']['falso_positivo']}`, "
        f"`omision={eng['agreement_counts']['omision']}` |",
        f"| Validez técnica estricta de explicaciones IA (`valida`) | **{ai['strict_valid_pct']:.2f} %** | "
        f"{ai['validity_counts']['valida']}/{ai['evaluated_explanations']} evaluadas (IC 95 % Wilson `[{ci[0]:.2f} %, {ci[1]:.2f} %]`) |",
        f"| Validez aceptable (`valida` + `parcialmente_valida`) | **{ai['acceptable_pct']:.2f} %** | "
        f"`incorrecta={ai['validity_counts']['incorrecta']}` |",
        f"| Utilidad diagnóstica promedio (escala 1–5) | **{ai['mean_utility_1_to_5']}** | "
        "Valoración directa del especialista en Etapa B |",
    ]

    if rank:
        lines.extend(
            [
                "",
                "## 2. Exploración de Priorización — *Query Health Score* vs. Especialista (`RF-EVA-05`)",
                "",
                f"- **Consultas ordenadas:** `{rank['evaluated_cases']}`",
                f"- **Correlación de Spearman ($\\rho$):** `{rank['spearman_rho']}`",
                f"- **Correlación de Kendall ($\\tau_b$):** `{rank['kendall_tau_b']}`",
                f"- **Coincidencia Top-3 / Top-5:** `{rank['top_3_overlap']}/3` · `{rank['top_5_overlap']}/5`",
            ]
        )

    lines.extend(
        [
            "",
            "## 3. Matriz de Contraste por Consulta",
            "",
            "| Etiqueta | Caso | Motor | Experto (`has_issue` / categorías) | Acuerdo motor | Validez IA | Utilidad | Observaciones |",
            "| :--- | :--- | :--- | :--- | :---: | :---: | :---: | :--- |",
        ]
    )
    for c in report["cases"]:
        rules_str = ", ".join(c["emitted_rules"]) or "*(ninguna)*"
        exp_str = f"{'si' if c['expert_has_issue'] else 'no'} ({'; '.join(c['expert_categories'])})"
        util_str = str(c["ai_utility_1_to_5"]) if c["ai_utility_1_to_5"] is not None else "N/A"
        obs = (c["observations"] or "").replace("|", "\\|")
        lines.append(
            f"| `{c['case_label']}` | `{c['case_id']}` | `{rules_str}` | {exp_str} | "
            f"`{c['engine_agreement']}` | `{c['ai_validity']}` | `{util_str}` | {obs} |"
        )

    return "\n".join(lines) + "\n"
