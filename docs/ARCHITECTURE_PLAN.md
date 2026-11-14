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
| IngestionService | RF-ING-01..10, RNF-FIA-01 | trabajo `ingest_connection` (cola `ingest`) | S1-11 (Álvaro) |
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
| DA-07 | **Propuesta de cierre lista (S1-07)** | Protocolo empírico F1–F8 ejecutado en SQL Server 2022 (`lab/results/S1-07-verificacion.md`); pendiente ratificación en S1-20 e informe final RF-EVA-04 (S3-05) |

## Decisiones de implementación de S1-09 (conexiones + CapabilityProbe)
Aprobadas por el responsable (Álvaro). No cierran ninguna DA. Detalle en `docs/NOTAS_S1-09_EQUIPO.md` y contrato
HTTP en `docs/CONTRATO_API_CONEXIONES.md`.

| ID | Decisión |
|---|---|
| D1 | `connection` gana los estados `Pending`/`Failed`/`Rejected`, `probe_failure_cause varchar(32)` (código cerrado, nunca el texto del motor) y `last_probed_at`. Unicidad por (servidor en minúsculas, `lower(database_name)`), sin resolver DNS |
| D2 | Cuenta sin el permiso mínimo de la matriz → `Failed` / `MissingPermission`. En 2022+ basta `VIEW DATABASE PERFORMANCE STATE` |
| D3 | Volver a registrar una conexión `Failed` o `Rejected` reemplaza credenciales y vuelve a sondear; si está `Active` o `Pending`, 409 |
| D4 | Matriz del Cuadro 4.16: < 13.x `NotSupported` (`Rejected`), 13–14 `CompatibleUntested`, ≥ 15 y Azure SQL DB `Tested`. **Deuda:** Azure SQL DB y 2025 sin pruebas ejecutadas |
| D5 | Regla `QueryStoreState.CanIngest` (READ_WRITE o READ_ONLY). CA-HU02.2 pasa a S1-11 |
| D6 | Paquetes de prueba nuevos: `Testcontainers.PostgreSql`, `Microsoft.AspNetCore.Mvc.Testing` 10.x |
| D7 | `tests/QueryDoctor.LabTests` contra el SQL Server del laboratorio; sin `QD_LAB_SQL` salen omitidas, y una omitida no es evidencia |
| D8 | `lab/sql/11-probe-fixtures.sql`: base `QD_Lab_NoQS` (Query Store OFF) y login `qd_perf` |

**Mecánica que conviene conocer:**
- `probe_connection` va en la cola `probe`, atendida por un servidor Hangfire propio del Worker con sondeo de
  1 s (las demás colas, 15 s). El argumento es solo el id de la conexión (invariante 2).
- Los fallos del sondeo terminan en `Failed` sin reintento. Un fallo de persistencia marca `Failed`/`Unknown` con un
  `UPDATE` condicional y, si no puede, Hangfire reintenta 3 veces: nunca queda `Pending` (auditoría M1).
- Límite total del sondeo `Probe:TimeoutSeconds` (15 por defecto). SqlClient recibe 2/3 de ese valor como
  Connect Timeout, para que un fallo de red llegue con su causa real y no como `Timeout`.
- El Gateway ya no se registra en `AddInfrastructure`: la Api usa `AddConnectionRegistration` y el Worker
  `AddTargetTelemetry`. El Gateway traduce los fallos a `TelemetrySourceException` con un código, sin texto.
- **Excepción de TLS del laboratorio (invariante 3):** solo en `docker-compose.lab.yml`
  (`Probe__LabTrustedCertificateServers__0: sqlserver`). `LabCertificateGuard` impide arrancar el Worker con
  ella fuera de `Development`, con `APP_ENV=prod` o con un servidor distinto de `sqlserver`.
- El Gateway solo admite autenticación SQL: sin usuario rechaza, nunca usa `IntegratedSecurity`.
- La clave maestra de `SecretProtector` es el Docker secret `master_encryption_key` (api y worker).
- `UseSameOriginForms` (CSRF) cubre todo POST/PUT/PATCH/DELETE.

**Riesgo abierto (OWASP A01, SSRF):** un DBA puede registrar cualquier host:puerto, y la diferencia entre
`ServerUnreachable` e `InvalidCredentials` revela qué puertos están abiertos, también en la red `backend`
(`db`, `api`, `ai`), donde está el Worker. Hoy lo mitigan dos cosas: solo un
DBA puede registrar y solo el Worker sale a la red `targets`. Pero esa red no es `internal`. Queda por proponer
una lista de hosts permitidos o una red restringida.

## Decisiones de implementación de S1-11 (ingesta incremental)
Aprobadas por el responsable (Álvaro) en el PUNTO 1. No cierran ninguna DA. Contrato HTTP en
`docs/CONTRATO_API_CONEXIONES.md` (ingestas).

| ID | Decisión |
|---|---|
| I1 | Top-N (RF-ING-10) por Σ(avg_duration × count_executions) en los intervalos posteriores a la marca, con desempate por `query_id`; `Ingestion:TopN` = 100 (1–5000). Se hace en SQL Server para acotar lo leído |
| I2 | Texto SQL y plan se guardan **completos y cifrados** (RNF-SEG-14); el enmascarado de literales ocurre al salir hacia la IA (S2-04/S2-05, invariante 5). La ingesta no registra SQL en logs |
| I3 | El estado de Query Store se lee **en vivo** en cada ingesta y se decide con `QueryStoreState.CanIngest`; el perfil del sondeo solo elige la sentencia con o sin columnas de TempDB/log (RNF-COM-04) y no se reescribe |
| I4 | Solo ingesta manual (`POST /connections/{id}/ingestions`). `RunScheduledAsync` existe pero el `RecurringJob` llega en el Sprint 2: la mitad "programada" de RF-ING-06 sigue abierta |
| I5 | Query Store OFF/ilegible, ocupada o conexión inactiva → `Skipped`; credenciales/TLS/permisos → `Failed` sin reintento; red, tiempo límite e inesperados → `Failed` y Hangfire reintenta a 30/60/120/240 s. Una `Running` huérfana pasa a `Failed/Interrupted` |
| I6 | Sin migración: el esquema de S1-04 alcanza. La marca anterior y la nueva quedan en el log del trabajo |
| I7 | Primera ingesta: ventana `Ingestion:InitialLookbackHours` = 24. Después, todo lo posterior a la marca, sin ventana |
| I8 | Textos, consultas y planes solo se insertan (no cambian en Query Store y recifrar cambiaría el valor); `runtime_stat` solo se actualiza si cambió algún valor. Planes nulos, vacíos, truncados, sin raíz ShowPlanXML o > `Ingestion:MaxPlanCharacters` (32 M) → `NotAnalyzable` con causa; los `TooLarge` no guardan el XML; los demás, `Pending` para el motor |

**Mecánica que conviene conocer:**
- Lista blanca nueva: `GetIngestionRuntimeStats`, `GetIngestionRuntimeStatsWithoutTempDbLog` (2016) y
  `GetQueryTextById`. Todas empiezan por `SELECT` (sin `WITH`). Se reutilizan `GetQueryStoreOptions` y `GetPlanXmlByPlanId`.
- Agregación ponderada (RF-ING-04) en .NET, función pura `RuntimeStatAggregator`. La marca de agua es el mayor
  intervalo **cerrado** leído (`end_time <= SYSDATETIMEOFFSET()` en el servidor) y nunca retrocede.
- `EfIngestionStore.ApplyAsync`: upsert con `unnest` + `ON CONFLICT`, avance de la marca con compare-and-set y la
  ejecución en `Succeeded`, todo en **una** transacción.
- Bloqueo: `pg_try_advisory_lock` con clave = primeros 8 bytes de SHA-256(`querydoctor:ingest:` + id), en una conexión
  propia sin pool que se mantiene toda la ingesta. Se descartó `[DisableConcurrentExecution]`: bloquea por método,
  espera en vez de omitir y su candado puede quedar colgado si el Worker muere.
- `POST …/ingestions` responde 409 si hay una ejecución `Running` o un trabajo de esa conexión en la cola `ingest`
  (API de monitoreo de Hangfire, solo lectura de PostgreSQL).
- La excepción de TLS del laboratorio es la misma del sondeo (`TargetConnectionFactory`, `Probe:LabTrustedCertificateServers`).

**Deuda de S1-11:** `origin_context` vacío con la cuenta de mínimo privilegio; un `QUERY_STORE CLEAR` congela la
marca; el intervalo activo de una consulta que sale del Top-N queda con su último valor; los SELECT de la plataforma
entran en el Query Store objetivo; `statement_log` en memoria sin límite (B9, ahora
crece con cada ingesta); prueba de corte de red de RNF-FIA-01 al Sprint 2.

> **Nota sobre nomenclatura F#:** En `ARQUITECTURA_v0.1.md` §9 las siglas **F1–F4** denotan *fronteras de confianza*, mientras que en el SRS (Apéndice A) y en `lab/results/S1-07-verificacion.md` las siglas **F1–F8** denotan los *hallazgos documentales y empíricos de Query Store*.

## Riesgos que afectan al código (§14.1)
- **RA-03:** si ScriptDom no puede tokenizar el SQL, se envía solo el contexto de hallazgos,
  **sin** texto SQL. Nunca regex como respaldo.
- **RA-02:** el modelo cita valores por id de métrica; el núcleo formatea el número. Tolerancia de redondeo documentada.
- **RA-01:** curar primero `kb_causal_relation` de REG-01..04; lo demás queda NO_VERIFICADA.
- **RA-05:** la restricción de salida de `ai` por dominio no es nativa en Compose: control parcial.
