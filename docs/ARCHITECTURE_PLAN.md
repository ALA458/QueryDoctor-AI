# ARCHITECTURE_PLAN.md — Query Doctor AI (resumen operativo)

> Resumen para trabajar. La fuente es `docs/arquitectura/ARQUITECTURA_v0.1.md`
> (Documento de Arquitectura de Software, 28-oct-2026). Si difieren, manda ese
> documento y este se corrige. Las secciones citadas (§) son de ese documento.

## Forma (ADR-01)
Monolito modular .NET con **dos hosts** (Api y Worker) + servicio de IA en Python
+ SPA + PostgreSQL. Cinco contenedores con Docker Compose (§4, §10).

```
Navegador ──443──▶ web (Nginx + SPA, TLS) ──▶ api (Identity, RBAC, REST, encola)
                                                   │ Hangfire sobre PostgreSQL
                                                   ▼
          Bases objetivo ◀──TDS/TLS── worker (Gateway, ingesta, motor, score,
          (solo SELECT)                         masker, contexto, validador, retención)
                                                   │ contexto YA enmascarado
                                                   ▼
                                     ai (FastAPI: retriever, prompts, llm_client) ──▶ LLM
                         db: PostgreSQL 16+ con pgvector (todos lo usan por red backend)
```

**Segmentación (§10), es un invariante:**
| Servicio | Redes | Salida permitida |
|---|---|---|
| web | frontend | ninguna (único puerto publicado: 443) |
| api | frontend, backend | ninguna — **no** habla con bases objetivo **ni** con el LLM |
| worker | backend, targets | solo bases objetivo |
| ai | backend, egress-ai | solo el dominio del perfil de LLM activo |
| db | backend | ninguna |

## Dependencias de la solución (§5)
`Domain ← Application ← Infrastructure ← hosts (Api, Worker)`. Cada módulo del SRS es
una carpeta vertical dentro de `Application`. El motor de reglas es **biblioteca pura,
sin E/S**. NetArchTest (S1-03) debe vigilar:
- `Domain` sin EF Core, SqlClient ni ASP.NET.
- Solo `QueryStoreGateway` referencia `Microsoft.Data.SqlClient`.
- El motor (PlanParser, RuleEngine, reglas, HealthScoreCalculator, ExplanationValidator) sin E/S.
- `Api` no referencia el Gateway ni el cliente del servicio de IA.

## Componentes (§5.1) y responsables
| Componente | Requisitos | Interfaz | Tarea (responsable) |
|---|---|---|---|
| QueryStoreGateway + statement_log | RNF-SEG-01/03, RF-TRZ-04 | `ITelemetrySource` | S1-08 (Maykol) |
| CapabilityProbe | RF-CFG-02..05/07, RNF-COM-04 | `ICapabilityProbe` | S1-09 (Álvaro) |
| IngestionService | RF-ING-01..10, RNF-FIA-01 | trabajo `ingest` | S1-11 (Álvaro) |
| PlanParser | RF-MOT-01, RNF-COM-02 | `IPlanParser` | S1-12 (Joel) |
| RuleEngine + REG-xx | RF-MOT-02..17 | `IRule`, `IRuleCatalog` | S1-13/14, S2-01 (Joel) |
| HealthScoreCalculator | RF-SCO-01..06 | función pura | S2-02 (Joel) |
| LiteralMasker (ScriptDom, TSql160Parser) | RNF-SEG-05/09 | `IMasker` | S2-04 (Álvaro) |
| ExplanationContextBuilder + caché (SHA-256 de JSON canónico) | RF-IA-01/04/07 | — | S2-05 (Álvaro) |
| ExplanationValidator | RF-IA-02/03/10/12 | función pura | S2-03 (Joel) |
| TraceLog / AuditLog (solo adición, encadenados) | RF-TRZ-01..05 | — | S2-06 (Álvaro) |
| SecretProtector (sobre AES-256-GCM) | RNF-SEG-04/14 | `IFieldEncryptor` | S1-05 (Maykol) |
| Hangfire (cliente en Api, servidor en Worker) | ADR-04, DA-03 | `BackgroundJob`, `RecurringJob` | S1-10 (Maykol) |
| Identity, políticas `DbaOnly` / `ConnectionMember` | RNF-SEG-11..13 | — | S1-06, S2-12 |
| ai: retriever, prompt_registry, llm_client, output_schema (Pydantic) | RF-IA-04/06/08/09, RNF-SEG-06/08 | `LLMProvider.generate(request, schema)` | S1-17/18, S2-07 (Maykol) |
| SPA (6 vistas, §5.4) | RF-DASH-*, RNF-SEG-07 | OpenAPI | S1-15/16, S2-09..11 (Daniel) |

Trabajos Hangfire: `probe_connection`, `ingest`, `analyze`, `score`, `explain`, `retention`.
Endpoints API: `/connections`, `/ingestions`, `/queries`, `/plans/{id}`, `/findings`,
`/explanations`, `/trace`, `/audit`, `/settings/thresholds`, `/exports`.

## Persistencia (§7) — 5 esquemas, uno por frontera de permisos
| Esquema | Tablas | Cifrado por campo |
|---|---|---|
| core | connection, query, query_text, plan, runtime_stat, ingestion_run, capability_profile | credentials, sql_text, plan_xml |
| analysis | finding, rule_catalog, threshold_set, health_score | evidencia con literales |
| ai | explanation, claim, claim_reference, kb_fragment (vector), kb_causal_relation | body, claim.text |
| audit | trace_entry, audit_event, statement_log | no (solo ids, hashes, metadatos) |
| ops | app_user, role, user_connection, job | password_hash (hash) |

- Clave natural de `runtime_stat`: (connection_id, plan_id, execution_type, interval_id) → upsert idempotente.
- `query_plan` se lee como `nvarchar(max)`, nunca como `xml` (límite de anidamiento, F8); se guarda como `text`.
- Marca de agua = último `runtime_stats_interval_id` consolidado; se avanza en la misma transacción del upsert.
- Un trabajo de ingesta por conexión: `pg_try_advisory_lock(connection_id)`.
- `audit.*`: el rol de la app no tiene UPDATE/DELETE; trigger que rechaza modificaciones; `prev_hash`.
- `kb_causal_relation` se carga por migración, sin edición desde la UI.

## ADR (§11)
| ID | Decisión | Estado en el documento |
|---|---|---|
| ADR-01 | Monolito modular .NET con dos hosts (Api, Worker) | Aceptada |
| ADR-02 | IA en servicio Python separado (aísla la única salida a Internet) | Aceptada |
| ADR-03 | PostgreSQL 16+ con pgvector | Aceptada |
| ADR-04 | Hangfire sobre el mismo PostgreSQL | Aceptada |
| ADR-05 | Enmascarado léxico con ScriptDom en el núcleo .NET | **Propuesta** |
| ADR-06 | Validador en el núcleo .NET, no en el servicio de IA | **Propuesta** |
| ADR-07 | Sin forzar plan ni escritura en producción | Aceptada |
| ADR-08 | Repositorio relacional único, sin lakehouse | Aceptada |
| ADR-09 | Identity con usuarios locales; OIDC fuera del MVP | Aceptada |
| ADR-10 | Gemma vía OpenRouter con adaptador por perfiles y candado de datos | Aceptada (falta versión exacta del modelo) |
| ADR-11 | Cifrado por campo AES-256-GCM en la aplicación | **Propuesta** |

> **Estado vigente: las 11 Aceptadas**, según el plan de sprints (S0-05), que es posterior.
> Pendiente: actualizar ADR-05, 06 y 11 en el documento de arquitectura para que no
> contradiga al plan ante el evaluador.

## Decisiones (DA-xx)
| ID | Estado | Resolución |
|---|---|---|
| DA-03 | Resuelta | Ingesta recurrente por conexión cada `interval_length_minutes`, ~5 min tras el cierre |
| DA-04 | Resuelta salvo versión del modelo | ADR-10; desarrollo con `openrouter_free` + datos de laboratorio |
| DA-05 | Resuelta | .NET 10, Python, React+TS, PostgreSQL, Hangfire, Identity |
| DA-09 | Resuelta | ADR-09 |
| DA-12 | Resuelta | 2016 "Compatible sin pruebas"; aceptación en 2019, 2022 y Azure SQL DB |
| DA-13 | Resuelta como riesgo residual | Nombres de objetos viajan sin seudonimizar; seudonimización planificada después |
| DA-01, 06, 10 | **Abiertas** | Ratificar con el tutor (S1-20) |
| DA-02 | **Abierta** | Calibrar umbrales con el laboratorio (S3-01) |
| DA-07 | **Abierta** | Laboratorio; bloquea afirmar eficacia (RF-EVA-04) |

## Riesgos que afectan al código (§14.1)
- **RA-03:** si ScriptDom no puede tokenizar el SQL, se envía solo el contexto de hallazgos,
  **sin** texto SQL. Nunca regex como respaldo.
- **RA-02:** el modelo cita valores por id de métrica; el núcleo formatea el número. Tolerancia de redondeo documentada.
- **RA-01:** curar primero `kb_causal_relation` de REG-01..04; lo demás queda NO_VERIFICADA.
- **RA-05:** la restricción de salida de `ai` por dominio no es nativa en Compose: control parcial.
