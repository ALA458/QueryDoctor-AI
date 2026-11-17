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
| LiteralMasker (ScriptDom 180.154.0, `TSql170Parser`) | RNF-SEG-05/09 | `IMasker` | S2-04 (Álvaro) |
| ExplanationContextBuilder + caché (SHA-256 de JSON canónico) | RF-IA-01/04/07 | — | S2-05 (Álvaro) |
| ExplanationValidator | RF-IA-02/03/10/12 | función pura | S2-03 (Joel) |
| TraceLog / AuditLog (solo adición, encadenados) | RF-TRZ-01..05 | — | S2-06 (Álvaro) |
| SecretProtector (sobre AES-256-GCM) | RNF-SEG-04/14 | `IFieldEncryptor` | S1-05 (Maykol) |
| Hangfire (cliente en Api, servidor en Worker) | ADR-04, DA-03 | `BackgroundJob`, `RecurringJob` | S1-10 (Maykol) |
| Identity, políticas `DbaOnly` / `ConnectionReader` + filtro por conexión (`IAccessScope`) | RNF-SEG-11..13 | `IAccessScope` | S1-06, S2-12 |
| ai: retriever, prompt_registry, llm_client, output_schema (Pydantic) | RF-IA-04/06/08/09, RNF-SEG-06/08 | `LLMProvider.generate(request, schema)` | S1-17/18, S2-07 (Maykol) |
| SPA (6 vistas, §5.4) | RF-DASH-*, RNF-SEG-07 | OpenAPI | S1-15/16, S2-09..11 (Daniel) |

Trabajos Hangfire: `probe_connection`, `ingest`, `analyze`, `score`, `explain`, `retention` (S2-13, recurrente diario).
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
| DA-12 | Resuelta | 2016 "Compatible sin pruebas". Probado (aceptación ejecutada en CI, S3-06): SQL Server 2019 y 2022. Azure SQL DB, 2017 y 2025+: Compatible sin pruebas. Detalle en `docs/COMPATIBILIDAD.md`; el Cuadro 4.16 del SRS aún dice 2019 "Compatible sin pruebas" (nota para Maykol, S1-19) |
| DA-13 | **Abierta en el SRS** (tabla 8.1) | El equipo la tomó como riesgo residual el 28-oct (§8.2), pero falta ratificarla. Mientras tanto los nombres de objetos viajan sin seudonimizar (S2-04 los conserva) |
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
| D4 | Matriz del Cuadro 4.16: < 13.x `NotSupported` (`Rejected`). **Vigente tras S1-19 y S3-06:** 15 y 16 `Tested` (aceptación en CI); 13, 14, 17+ y Azure SQL DB `CompatibleUntested`; Managed Instance `NotSupported`. Debe coincidir con `docs/COMPATIBILIDAD.md` (`SupportMatrixDocumentTests`) |
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

## Decisiones de implementación de S2-04 (LiteralMasker)
Aprobadas por el responsable (Álvaro) en el PUNTO 1. No cierran ninguna DA. Código en `QueryDoctor.Application.Masking`
y el puerto hacia la IA en `QueryDoctor.Application.Ai`.

| ID | Decisión |
|---|---|
| D1 | Fallo cerrado por componente y RA-03: si el SQL no tokeniza, la solicitud viaja con `sql_text = ""` y con los hallazgos enmascarados. Si no queda ningún hallazgo enviable, no se envía nada (RF-IA-01) |
| D2 | Marcadores tipados `@qd_str_N`, `@qd_num_N`, `@qd_bin_N`, `@qd_date_N` y `@qd_guid_N`, numerados por orden de aparición. El prefijo `qd_` evita chocar con los `@p1` reales de Query Store |
| D3 | Dentro de una solicitud, el mismo tipo con el mismo texto exacto recibe el mismo marcador, en el SQL, el plan y la evidencia. Revela igualdad, nunca el valor |
| D4 | El texto entre comillas dobles (`AsciiStringOrQuotedIdentifier`) se enmascara siempre, porque con `QUOTED_IDENTIFIER OFF` es una cadena |
| D5 | Cada clave de evidencia está clasificada en `EvidenceMasker`: fragmento SQL, conservada o descartada (`cause` de REG-11). Una clave sin clasificar deja fuera ese hallazgo, y una prueba guardiana corre el catálogo real |
| D6 | Plan estricto: cualquier fallo deja el plan entero no enviable. Hay denegación por defecto de atributos (corrección de H1): portadores que pasan por el lexer, lista blanca de atributos conservados, y cualquier otro atributo da `PlanUnknownAttribute` |
| D7 | `TSql170Parser` en lugar del `TSql160Parser` de §5.1: para tokenizar solo agrega palabras clave y cubre Azure SQL |
| D8 | Límite de 1 M caracteres para el SQL, como constante y no configurable. El plan usa los límites de `PlanParserOptions` |
| D9 | `MaskingArchitectureTests` (archivo nuevo): `Masking` es puro, sin logging, `Configuration` ni `Options`, y ScriptDom solo se usa dentro de `Masking` |

**Mecánica que conviene conocer:**
- `IMasker.Mask(MaskingInput)` es una sola llamada pura. Recorre en orden fijo: primero el SQL, luego el plan en orden de documento, y por último los hallazgos por `finding_id` con sus claves en orden ordinal.
- Los comentarios se eliminan completos. Nunca se propaga ni se registra el mensaje de ScriptDom ni el de XmlException, porque repiten el texto de entrada.
- `MaskedExplanationRequest` solo se construye desde un `MaskedContext`, y su `ToWireJson()` refleja `ExplainRequest` de S1-18. En el cuerpo, `description` lleva por ahora el nombre de la regla. El plan enmascarado no viaja todavía, porque S1-18 no tiene ese campo.
- El Worker registra el enmascarador con `AddLiteralMasking()`, que no recibe configuración (CA-HU21.4).

**Deuda de S2-04:** la seudonimización de nombres depende de DA-13. Las estimaciones (`TOP (n)`, que da `EstimateRows = n`) pueden
reproducir un literal (H2). Los identificadores entre corchetes pueden llevar instrucciones (H3, lo cubre S2-07). La regla
de arquitectura no le impide a la Api usar `Application.Ai` ni `Masking` (H4). La barrera por tipos es `internal`, así que
cualquier tipo de Application puede saltarla (H5). Falta una prueba de contrato contra el esquema Pydantic (H6). Falta
repetir la suite con el cliente HTTP real (S2-05). La lista blanca del plan viene de los planes de SQL Server 2022:
un plan de 2019 o Azure con atributos nuevos queda no enviable hasta catalogarlos.

## Decisiones de implementación de S2-05 (contexto, caché y trabajo explain)
Aprobadas por el responsable (Álvaro) en el PUNTO 1, más tres ajustes para seguir la arquitectura. No cierran ninguna DA.

| ID | Decisión |
|---|---|
| E1 | El servicio de IA ya hace 60 s y 2 reintentos hacia el LLM (RNF-FIA-02). .NET hace **un solo intento** con tiempo total `AiService:TimeoutSeconds` = 200, y `explain` lleva `AutomaticRetry(Attempts = 0)`, para no multiplicar las llamadas al LLM |
| E2 | La caché (RF-IA-07, Debería) **no se recortó**. La cola `explain` tiene su propio servidor Hangfire, que sondea cada 1 s, para CA-HU09.3. Solo se reutilizan explicaciones TRAZADA |
| E3 | Hash SHA-256 del contexto canónico YA enmascarado, más catálogo, versión de cada regla, `prompt_version` y `llm_profile` (los dos leídos de `GET /info`). Sin `correlation_id`. Si `/info` no responde, no se usa la caché |
| E4 | `ai.explanation`, más `ai.claim` (texto cifrado) y `ai.claim_reference` (hallazgo y nodos), como en §7. El cuerpo cifrado lleva estado, aviso, resumen y fallos del validador |
| E5 | 422 del servicio o respuesta que no parsea → IA_NO_DISPONIBLE / `InvalidOutput`. Si la respuesta parsea pero no supera el validador → NO_VERIFICADA |
| E6 | Las métricas del contexto salen del último `HealthScore`: ejecuciones, promedios por ejecución de duración, CPU y lecturas, consumo ponderado y score, redondeados a 4 decimales |
| E7 | `GET …/latest` siempre devuelve los hallazgos del último análisis, junto con el aviso (CA-HU12.1). Cada afirmación trae sus hallazgos y nodos del plan (RF-DASH-05) |
| E8 | `HttpClient` único con `SocketsHttpHandler`, sin paquetes nuevos. La dirección es `AiService:BaseUrl` (`http://ai:8000/` en el compose) |
| E9 | Deuda de S2-04 cerrada: `description` del catálogo, prueba de contrato con `ExplainRequest` (H6), regla de arquitectura de la Api (H4) y suite de centinelas sobre el cuerpo HTTP real |
| E10 | Si hay una explicación pendiente o en curso reciente, `POST` responde 409. Pasados 10 min se da por abandonada (`Abandoned`) |

**Mecánica que conviene conocer:**
- La Api (`Application.Explanations`) solo guarda la solicitud y encola `explain(explanationId)`. El Worker (`Application.Ai`) descifra, enmascara, arma el contexto, consulta la caché, llama a la IA, valida y guarda. NetArchTest le impide a la Api depender de `Application.Ai`, `Masking` e `Infrastructure.Ai/Explanations`.
- Estados: `PENDIENTE`, `EN_CURSO`, `COMPLETADA` (TRAZADA o NO_VERIFICADA), `IA_NO_DISPONIBLE` (con causa `Timeout`, `ServiceUnreachable`, `ProviderError`, `InvalidOutput`, `Unexpected` o `Abandoned`) y `NO_GENERADA` (`NoFindings` o `NoSendableFindings`). Sin hallazgos, el `POST` responde `SIN_HALLAZGOS` y no encola nada.
- Lo único que se registra son ids, estado, causa, conteos y `MaskingMetrics`. Los textos del modelo se recortan al largo de su columna (`ExplanationColumnLimits`, preauditoría P1).

**Deuda de S2-05:**
- La entrada de traza encadenada de RF-TRZ-01 es de S2-06: los datos ya están en `ai.explanation`.
- CA-HU09.3 (≤ 2 s) no se midió de punta a punta.
- RNF-RND-03 (≤ 30 s p95) no se cumple con 60 s × 3. Va a DA-06.
- Dos `POST` simultáneos podrían encolar dos trabajos.
- Si alguien configura `TimeoutSeconds` por encima de 10 min, se cruza con la ventana de abandono.
- El canal Worker → `ai` es HTTP dentro de la red `backend` (el tramo hacia el LLM va con TLS).

## Diferencias con ARQUITECTURA_v0.1 que hay que llevar al documento
El código sigue estas decisiones. El documento de arquitectura aún no las refleja y debe corregirse. Ninguna contradice al SRS.

| § | El documento dice | El código hace | Por qué | Quién decide |
|---|---|---|---|---|
| §5.3 | *Retriever* RAG con pgvector dentro del servicio de IA | .NET elige los fragmentos curados por regla y los envía en `kb_fragments` | Es determinista, cumple RF-IA-04 y usa los mismos ids que el validador .NET (RF-IA-03 c). El contrato de S1-18 ya lo recibía así | Equipo (Maykol, S1-17/S1-18) |
| §5.1 | `TSql160Parser` | `TSql170Parser` (S2-04, D7) | Para tokenizar solo agrega palabras clave | Álvaro: corregir el documento |
| §8.1 | Un filtro de registros que trunca el SQL | Ningún registro contiene SQL, así que el filtro no existe | — | Transversal: pendiente |
| §8.2 | DA-13 resuelta | Abierta en el SRS (tabla 8.1) | El SRS manda | Tutor (S1-20) |
| §11 | ADR-05, 06 y 11 "Propuesta" | Aceptadas según el plan de sprints | — | Equipo: corregir el documento |
| RNF-RND-03 | ≤ 30 s p95 | 60 s × 3 por defecto (RNF-FIA-02) | Las dos metas del SRS chocan entre sí | Tutor (DA-06) |

## Decisiones de implementación de S2-06 (traza y auditoría)
Aprobadas por el responsable (Álvaro) en el PUNTO 1. No cierran ninguna DA (DA-14 sigue abierta).

| ID | Decisión |
|---|---|
| T1 | El encadenado por hash **no se recortó** al Sprint 3: el "Hecho cuando" lo exige y recortarlo necesitaba al tutor |
| T2 | El estado final de una explicación, sus afirmaciones y su entrada de traza se guardan en una sola transacción (`IExplanationStore.FinishAsync` / `CompleteAsync`) |
| T3 | Todo estado final deja una entrada: completada, desde caché, IA no disponible, no generada y abandonada. Quedar en cola o en curso no deja ninguna |
| T4 | En la base: un trigger rechaza UPDATE, DELETE y TRUNCATE, y la cadena de hash detecta lo que se haga saltándolo. **Deuda:** un rol de aplicación sin UPDATE/DELETE (la app es dueña del esquema; toca el compose de S1-02) |
| T5 | Eventos: login exitoso, fallido (usuario inexistente sin id), bloqueo, logout, cambio de contraseña, registro y re-registro de conexión e ingesta manual. `ThresholdsChanged` y `DataDeleted` existen, sin emisor |
| T6 | `AuthEndpoints` (S1-06, de Daniel): solo se agregó una llamada de auditoría en cada salida |
| T7 | En el login, un fallo de la auditoría se registra (sin datos) y no impide entrar. En conexiones e ingestas la escritura es estricta y va después de la acción |
| T8 | `/trace` y `/audit` solo para DBA; un rol Auditor depende de DA-14 |
| T9 | `statement_log` (RF-TRZ-04, S1-08) no se tocó; podría reutilizar esta cadena |

**Mecánica que conviene conocer:**
- `entry_hash = SHA-256(prev_hash + "
" + JSON canónico)`, con génesis de 64 ceros. La `seq` la asigna `ChainWriter` (última + 1) bajo `pg_advisory_xact_lock`, una clave por cadena y siempre dentro de una transacción. Las fechas se recortan a microsegundos antes de hashear.
- Las tablas `audit.*` no tienen FK: la traza sobrevive a la purga de S2-13. Solo guardan ids, códigos, versiones y fechas.
- `GET /trace/verify` y `/audit/verify` recorren la cadena por lotes y devuelven la primera `seq` rota con su motivo (`hash_mismatch` o `broken_link`).
- Los filtros `from`/`to` se convierten a UTC (Npgsql no acepta otro offset en `timestamptz`).

**Deuda de S2-06:**
- Rol de base de datos sin UPDATE/DELETE (T4).
- Si la auditoría de un registro de conexión o de una ingesta falla, la acción ya ocurrió y la respuesta es 500.
- El nombre escrito en un login fallido se guarda: si alguien escribe su contraseña en el campo de usuario, queda en la auditoría.
- La verificación recorre toda la cadena; con millones de filas conviene un punto de control.

## Decisiones de implementación de S2-12 (RBAC del Desarrollador)
Aprobadas por el responsable (Álvaro) en el PUNTO 1. No cierran ninguna DA (DA-14 sigue abierta).

| ID | Decisión |
|---|---|
| D1 | El Desarrollador **pide y lee** explicaciones de sus consultas: CU-05 lo pone como actor principal y RNF-SEG-12 no reserva esa acción al DBA (el SRS manda sobre el "solo lee" inicial) |
| D2 | El Desarrollador lee `GET /connections/{id}/ingestions` de lo asignado (flujo 2a de CU-04: "sin datos ingeridos") |
| D3 | Sin filtro, a propósito: `ops.user_connection` (es la fuente del scope) y `audit.trace_entry` (`ChainWriter` lee la última `seq` de toda la cadena, también desde la Api). Las dos solo las ve el DBA |
| D4 | La caché de explicaciones no cruza conexiones **por construcción**: el `QueryId` entra al hash de contexto y cada consulta es de una sola conexión. Sin cambio de código; lo fija una prueba |
| D5 | `audit.audit_event.related_user_id` (migración `AuditoriaUsuarioRelacionado`). Entra al hash **solo si tiene valor**, así las cadenas ya escritas siguen verificando (valor dorado en `HashChainTests`) |
| D6 | Crear usuarios Desarrollador queda **fuera** de S2-12 (es Identity, de S1-06). Solo se agregó `GET /users?role=Desarrollador` para elegir a quién asignar |
| D7 | Con los dos roles gana DBA. Sin rol, con un rol desconocido o con cambio de contraseña pendiente: 403 y acceso `None` |

**Mecánica que conviene conocer:**
- `AppDbContext` exige un `IAccessScope` (`None`, `Developer`, `Dba`, `System`) y aplica el filtro con nombre
  `ConnectionAccess` a 13 entidades: `connection` (por su `id`), las 10 con `connection_id` y `claim` y
  `claim_reference` a través de su explicación.
  `AddInfrastructure` **no** registra ningún acceso: la Api usa `AddRequestAccessScope` y el Worker `AddSystemAccessScope`.
  Un host que lo olvide no arranca.
- En la Api, `AccessScopeMiddleware` (después de `UseAuthorization`) lee las asignaciones de la base **en cada
  solicitud** de un Desarrollador. No hay caché ni claims en la cookie. El DBA no hace ninguna consulta extra.
- El acceso vive en la capa de datos, no en el endpoint: una conexión no asignada no se encuentra y el endpoint
  responde el 404 que ya tenía para "no existe". Las rutas de escritura agregan `DbaOnly` encima de `ConnectionReader`.
- `IgnoreQueryFilters` está prohibido en todo `src/` (`AccessScopeArchitectureTests`, revisa el IL). El acceso Sistema
  ya ve todo, así que no hace falta en ningún lado.
- `ConnectionAccessFilterCoverageTests` recorre el modelo de EF: una entidad nueva con `ConnectionId`, o hija de una
  filtrada, sin filtro deja la prueba en rojo.
- `ExecuteSql*` **no** pasa por el filtro. Hoy solo lo usan el Worker (ingesta), el bloqueo de cadena y el
  `INSERT … ON CONFLICT` de las asignaciones (DBA). `FromSql` sobre un `DbSet` sí lo pasa: EF aplica el filtro encima
  (el único uso es la detección de duplicados del alta, solo DBA).
- Asignar y quitar escriben el cambio y su evento de auditoría en **una** transacción (`EfConnectionAssignmentStore`):
  si la auditoría falla no queda ninguno, y el reintento deja los dos (`AssignmentAuditAtomicityTests`).
- La política `ConnectionReader` corta primero (403); el acceso `None` del middleware es la segunda barrera y se prueba
  en aislado (`AccessScopeMiddlewareTests`).
- `docs/arquitectura/ARQUITECTURA_v0.1.md` §autorización todavía llama `ConnectionMember` a la política que el código
  llama `ConnectionReader`: error de nombre del documento de arquitectura, reportado y sin corregir aquí.

**Deuda de S2-12:**
- No hay alta de usuarios Desarrollador (D6): para la demo hay que sembrarlos a mano. Corresponde a Identity (S1-06).
- RLS en PostgreSQL como segunda barrera que cubra también el SQL directo. Necesita el rol de aplicación de T4.
- El listado de conexiones del Desarrollador hace una consulta más por solicitud (sus asignaciones). Medirlo en S3-07.
- Una asignación a un usuario que después pierde el rol Desarrollador queda en la tabla, inerte.

## Decisiones de implementación de S2-13 (retención, RNF-SEG-10)
Aprobadas por el responsable (Álvaro) en el PUNTO 1. No cierran ninguna DA.

| ID | Decisión |
|---|---|
| D1 | El recorte del plan ("Solo borrado a pedido del DBA") **no se aplicó**. RNF-SEG-10 es Debe, el punto 4 de SRS 5.3 prohíbe recortar "el tratamiento seguro de datos" y §5.1 dice que los recortes no incluyen la seguridad. Se hizo el borrado automático y el borrado a pedido |
| D2 | El plazo es `Retention:SqlTextAndPlanDays` (configuración o `Retention__SqlTextAndPlanDays` en el compose, `QD_RETENTION_DAYS`): 30 por defecto, rango 1–3650, contado desde `ingested_at`. Fuera de rango, el Worker no arranca. No se configura desde la UI |
| D3 | Se borran `core.query_text.encrypted_sql_text`, `core.plan.encrypted_plan_xml` y `analysis.finding.encrypted_evidence_json` de los hallazgos de esos planes (la evidencia lleva literales del plan). Quedan las filas, ids, hashes, estados, `runtime_stat`, `health_score`, los metadatos del hallazgo, las explicaciones y todo `audit.*` |
| D4 | Migración `RetencionEvidencia`: `purged_at` en `core.query_text` y `core.plan`. "Expirado" sale de la marca y no del nulo, así un plan `TooLarge` deja de verse como expirado (bug de S2-09/S2-10) |
| D5 | Lo purgado no vuelve: la ingesta solo inserta (`ON CONFLICT DO NOTHING`, I8) |
| D6 | Un plan purgado conserva sus hallazgos y no se vuelve a analizar. Una explicación pedida con la evidencia expirada termina en `NO_GENERADA` / `EvidenceExpired`, sin llamar a la IA y con su entrada de traza |
| D7 | `DELETE /queries/{id}/evidence` y `DELETE /connections/{id}/evidence`: solo DBA, CSRF por mismo origen, 204 (idempotente) o 404. El borrado y el evento `DataDeleted` (con usuario, `outcome` `ok` o `nothing_to_delete`) van en una transacción. No borra conexiones ni usuarios, así que no se crea el choque de FK de `AssignAsync` |
| D8 | La purga automática deja un `DataDeleted` por conexión purgada, sin `user_id` y con `user_name = "system:retention"`, en la misma transacción que esa purga. Los conteos van al log y no al evento |
| D9 | Contrato "evidencia expirada" en `docs/CONTRATO_FRONT_CONSULTAS.md`. La parte web queda para Daniel |
| D10 | Trabajo recurrente `retention` en su cola, diario a las 03:00 UTC, sin argumentos y sin reintento automático (la corrida siguiente retoma). **Ajuste:** no hay lotes de 1000, sino un UPDATE por tabla y por conexión, dentro de la transacción de su evento: con lotes, cada lote necesitaría su propio evento o una transacción que los abarque todos, y eso no acorta los bloqueos |

**Mecánica que conviene conocer:**
- `EfEvidencePurgeStore` usa `ExecuteUpdateAsync` sobre los `DbSet`, que pasa por el filtro por conexión de S2-12. No
  usa SQL directo ni `IgnoreQueryFilters`. La Api borra con el acceso del DBA; el Worker, con el acceso Sistema.
- La evidencia que se vacía es la de **todo** hallazgo cuyo plan tiene `purged_at`, también de corridas anteriores.
- Solo se marca lo que tenía contenido: un plan `TooLarge` o un texto que nunca llegó no reciben `purged_at` y no salen
  "expirados" (auditoría B1). Un borrado a pedido sobre ellos audita `nothing_to_delete`.
- Purga contra recálculo (auditoría M1): `EfScoreRecalculationStore.SaveBatchAsync` bloquea las filas de los planes
  (`SELECT … FOR UPDATE`) y relee `purged_at`. Un plan ya purgado no recibe corrida ni hallazgos nuevos, y una purga que
  llega después espera ese bloqueo. Como respaldo, `retention` también visita las conexiones que tienen evidencia no
  vacía en planes purgados, la vacía y lo audita.
- El borrado a pedido corre en la Api y no en Hangfire: solo toca PostgreSQL, ni bases objetivo ni el LLM (invariante 2).
- La traza (`audit.trace_entry`) no tiene FK y no se toca, así que `/trace/verify` y `/audit/verify` siguen en verde
  después de la purga (`RetentionTests`).

**Deuda de S2-13:**
- La web todavía no marca `findings[].evidenceExpired` ni la causa `EvidenceExpired` (Daniel).
- Un texto compartido por varias consultas se purga para todas cuando el DBA borra una.
- Sin índice para la búsqueda de lo vencido (`ingested_at` con `purged_at IS NULL`). Medirlo en S3-07 con volumen real.
- La auditoría guarda el evento y no los conteos: el detalle de cuántas filas se purgaron está solo en el log del Worker.
- Un plan purgado mientras seguía `Pending` (venció antes de su primer análisis) queda `Pending` y puntúa sin hallazgos
  (auditoría B3; improbable con 30 días y `score` tras cada ingesta).
- Los registros fuente (`QueryFindingSource`, `WindowPlanSnapshot`, …) traen el campo nuevo con valor por omisión: un
  lector futuro que no lo llene mostraría "no expirado" (auditoría B3).

## Decisiones de implementación de S3-06 (aceptación por plataforma, RNF-COM-01)
Aprobadas por el responsable (Álvaro) en el PUNTO 1. No cierran ninguna DA (actualizan la resolución de DA-12).

| ID | Decisión |
|---|---|
| A1 | Suite `tests/QueryDoctor.AcceptanceTests`: la Api real (`WebApplicationFactory`) encola en Hangfire y un Worker armado con `AddWorkerServices` (la misma composición que `Worker/Program.cs`) consume del mismo PostgreSQL. Todo por la Api pública. Sin `QD_LAB_SQL` sale Skipped |
| A2 | La IA es un doble HTTP debajo del `HttpAiExplanationClient` real (JSON, enmascarado y validador reales). Las métricas del modelo son del arnés S3-02 |
| A3 | Plataformas: SQL Server 2022 y 2019, matriz de CI. 2019 pasó y queda **Probado**; Azure SQL DB queda Compatible sin pruebas (sin acceso). `docs/COMPATIBILIDAD.md` es la matriz declarada y `SupportMatrixDocumentTests` exige que coincida con `SupportMatrix` |
| A4 | Job `acceptance` en `ci.yml`. SQL Server con `docker run` y no `services:`: así la contraseña de sa se genera y enmascara en cada corrida, sin ir al YAML (invariante 4) ni a un secreto manual |
| A5 | Cada prueba lleva el trait `Criterio` (CA-HU / RNF), también las LabTests; los `.trx` por plataforma son artefactos de la CI |
| A6 | `lab/sql/12-acceptance-scenarios.sql` (marca `QD-ACC`, no la toca `Run-Lab -Step extract`): ACC01 (Key Lookup sobre el umbral de REG-04), ACC02 (misma consulta con dos planes, CA-HU07.2) y la base `QD_Lab_Acc` para los negativos del sondeo |
| A7 | `01` usa el nivel de compatibilidad máximo de la versión (160 en 2022, 150 en 2019) y `11` solo da `VIEW DATABASE PERFORMANCE STATE` desde 16.x. En 2022 nada cambia |

**Mecánica que conviene conocer:**
- `QD_LAB_PLATFORM` (`sql2022` por omisión, `sql2019`) fija la versión y el nivel esperados (`LabPlatform`).
  `[LabFact(MinimumMajorVersion = 16)]` omite, con su motivo, lo que no existe en la versión bajo prueba.
- `lab/ci/prepare-acceptance-lab.sh <contenedor>` arma el laboratorio desde cero (01, 04, 02, 03, 11 y la carga 05 + 12
  dos veces con 65 s entre ellas, para tener dos intervalos). Quita el BOM UTF-8 de los scripts antes de pasarlos a sqlcmd.
- La identidad de una conexión es (servidor, base) **normalizados** (`ConnectionAddress` quita `tcp:` y el puerto 1433):
  por eso los negativos usan otra base (`QD_Lab_Acc`) y no un alias del servidor.
- En Windows, `QD_LAB_SQL=127.0.0.1` (con `localhost` resuelve primero a IPv6 y el compose publica 1433 solo en IPv4).

**Hallazgos y deuda de S3-06:**
- **La ingesta queda `Succeeded` antes de que terminen el análisis y el score** (corren después, en el mismo trabajo y con
  el bloqueo tomado). Mientras tanto el listado muestra consultas sin hallazgos ni score, y una ingesta pedida en ese
  momento sale `Skipped/AlreadyRunning`. La suite espera un score posterior a `finishedAt`. Deuda de S1-11 y S2-02.
- **REG-04 no se dispara en ESC02 con los umbrales por defecto** (unos 20 lookups y 0,03 % de consumo frente a 1000 y 1 %).
  Lo cubre ACC01. Dato para S3-01 (DA-02) y para el "Hecho cuando" de S1-16.
- CA-HU01.4 (eliminar una conexión y sus credenciales) **no está implementado**: no hay `DELETE /connections/{id}`.
- Sin prueba de punta a punta de CA-HU03.4 con un corte de red real (RNF-FIA-01); sigue simulado en IntegrationTests.
- Solo la edición Developer en Linux. El Cuadro 4.16 del SRS debe pasar 2019 a Probado (S1-19, Maykol).

## Riesgos que afectan al código (§14.1)
- **RA-03:** si ScriptDom no puede tokenizar el SQL, se envía solo el contexto de hallazgos,
  **sin** texto SQL. Nunca regex como respaldo.
- **RA-02:** el modelo cita valores por id de métrica; el núcleo formatea el número. Tolerancia de redondeo documentada.
- **RA-01:** curar primero `kb_causal_relation` de REG-01..04; lo demás queda NO_VERIFICADA.
- **RA-05:** la restricción de salida de `ai` por dominio no es nativa en Compose: control parcial.
