# S1-07 — Verificación empírica de Query Store (Apéndice A del SRS)

- **Tarea:** S1-07 (cierra también S0-10 y aporta la evidencia empírica requerida por DA-07 y RF-EVA-04)
- **Responsable:** Joel
- **Fecha de ejecución:** 2026-11-11
- **Entorno verificado:**
  - Imagen Docker: `mcr.microsoft.com/mssql/server:2022-latest`
  - Digest de la imagen: `sha256:11927f5edc5122658e90b3a80ee36e8aef779310bf771415c1d89271859c73fd`
  - `@@VERSION`: `Microsoft SQL Server 2022 (RTM-CU27-GDR) (KB5129300) - 16.0.4315.2 (X64) — Developer Edition (64-bit) on Linux (Ubuntu 22.04.5 LTS)`
  - Nivel de compatibilidad de `QD_Lab`: `160` (SQL Server 2022)
- **Declaración de alcance (RF-EVA-04, Invariante 10):** Este anexo documenta únicamente qué estructuras y métricas quedan almacenadas en Query Store frente al plan de ejecución real en los escenarios controlados del laboratorio sobre SQL Server 2022 Developer. **No afirma eficacia, precisión ni exhaustividad del motor de reglas ni del servicio de IA** (esas métricas corresponden a los arneses S2-14 y S3-01). Tampoco sustituye la verificación en SQL Server 2019 ni en Azure SQL Database.

> **Nota terminológica:** En este documento, **F1–F8** y **H1–H5** se refieren siempre a los *hallazgos documentales e hipótesis del Apéndice A del SRS*, nunca a las fronteras de confianza F1–F4 de `docs/arquitectura/ARQUITECTURA_v0.1.md`.

---

## 1. Qué aparece y qué no en el plan guardado por Query Store (por escenario)

En cada escenario se comparó:
- **Vista QS (`*.qs.*.sqlplan`):** el plan recuperado de `sys.query_store_plan.query_plan` leído como `nvarchar(max)` con la cuenta de mínimo privilegio `qd_reader` (`VIEW DATABASE STATE`).
- **Vista Real (`*.actual.sqlplan`):** el plan capturado en el mismo momento con `SET STATISTICS XML ON` (verdad de terreno, clase C, solo laboratorio).

| Escenario | Semilla en `QD_Lab` | Plan(es) en Query Store (`tests/fixtures/plans/`) | ¿Qué SÍ aparece en el plan de Query Store? | ¿Qué NO aparece en el plan de Query Store (pero sí en el plan real)? | Regla / Requisito impactado |
|---|---|---|---|---|---|
| **ESC01** — Conversión implícita | `dbo.usp_ESC01`: filtro `CustomerCode = @code` donde la columna es `varchar(20)` (con índice `IX_Orders_CustomerCode`) y el parámetro es `nvarchar(20)` | `ESC01.qs.1.sqlplan` (5 657 B) vs `ESC01.actual.sqlplan` (7 291 B) | - `<Warnings><PlanAffectingConvert ConvertIssue="Cardinality Estimate" Expression="CONVERT_IMPLICIT(nvarchar(20),[QD_Lab].[dbo].[Orders].[CustomerCode],0)"/>` y segunda advertencia `ConvertIssue="Seek Plan"`<br>- Operador `Index Scan` sobre `IX_Orders_CustomerCode` con `CONVERT_IMPLICIT` en el predicado<br>- `EstimateRows="20"`, costos estimados (`EstimatedTotalSubtreeCost`) | - `<RunTimeInformation>` / `ActualRows` (6 nodos en el plan real; **0** en QS)<br>- Tiempos y lecturas reales por operador (`ActualElapsedms`, `ActualCPUms`, `ActualLogicalReads`) | **REG-01 (Debe)** — 100 % viable con evidencia Clase A |
| **ESC02** — Key Lookup | `dbo.usp_ESC02`: filtro `CustomerId = @id` usando `IX_Orders_CustomerId` (sin `INCLUDE`) proyectando `Amount, CreatedAt` | `ESC02.qs.1.sqlplan` (6 074 B) vs `ESC02.actual.sqlplan` (7 264 B) | - `<IndexScan Lookup="1" ...>` sobre `PK_Orders` (`Clustered`) combinado mediante `Nested Loops` con el `Index Seek` sobre `IX_Orders_CustomerId`<br>- `EstimateRows="20"`, `EstimatedTotalSubtreeCost`, columnas de salida | - `<RunTimeInformation>` / `ActualRows` por operador (**0** en QS; 6 en el plan real) | **REG-04 (Debe)** — 100 % viable con evidencia Clase A (+ Clase B para ponderar por ejecuciones) |
| **ESC03** — Índice faltante | `dbo.usp_ESC03`: filtro `Region = @region AND Amount > 900` sobre columna sin índice | `ESC03.qs.1.sqlplan` (5 558 B) vs `ESC03.actual.sqlplan` (11 466 B) | - Nodo `<MissingIndexes><MissingIndexGroup Impact="96.953"><MissingIndex Database="[QD_Lab]" Schema="[dbo]" Table="[Orders]">` con columnas `EQUALITY` (`Region`) e `INEQUALITY` (`Amount`)<br>- `Clustered Index Scan` con `EstimateRows="16172.1"` | - `<RunTimeInformation>` / `ActualRows` (las 5 000 filas reales por hilo solo están en el plan real; a nivel sentencia sí están en `runtime_stats.avg_rowcount = 5000`) | **REG-02 (Debe)** — 100 % viable con evidencia Clase A |
| **ESC04** — Sesgo de cardinalidad con compat. 160 (PSP activo) | `dbo.usp_ESC04`: columna `SkewKey` (99 % = `1`, resto valores únicos `>= 1000`); se invoca con `@k = 1500` y luego con `@k = 1` | **3 planes en QS:**<br>- `ESC04.qs.1.sqlplan` (1 264 B, `Dispatcher` con `<ParameterSensitivePredicate>` sin `<RelOp>`)<br>- `ESC04.qs.2.sqlplan` (6 945 B, variante *Seek + Lookup*, `StatementEstRows="1"`)<br>- `ESC04.qs.3.sqlplan` (5 972 B, variante *Clustered Index Scan* + `MissingIndex`, `StatementEstRows="990000"`) | - En compatibilidad 160, el optimizador activa **Parameter Sensitive Plan (PSP) optimization**: guarda un plan *dispatcher* (`maxRelOpDepth=0`) y dos *query variants* separados, cada uno con estimación acorde a su rango (`est_rows=1` vs `est_rows=990000`) | - Ningún plan de QS tiene `<RunTimeInformation>`<br>- El plan `ESC04.qs.1.sqlplan` **no contiene ningún `<RelOp>`** (solo el nodo `<Dispatcher>`) | **Hallazgo clave para S1-12 (`PlanParser`):** un Showplan XML válido de SQL Server 2022 puede tener **0 nodos `<RelOp>`** cuando es un plan *Dispatcher* de PSP; `PlanParser` no debe fallar ante ello |
| **ESC04B** — Sesgo y *parameter sniffing* clásico (`QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_150`) | `dbo.usp_ESC04B`: misma consulta con hint de CE/optimizador 150; compila con `@k = 1500` (raro) y reutiliza el plan con `@k = 1` (990 000 filas) | `ESC04B.qs.1.sqlplan` (6 140 B) vs `ESC04B.actual.sqlplan` (7 479 B) | - Un único plan en QS con `Index Seek` + `Key Lookup` (`Lookup="1"`) y `StatementEstRows="1"`<br>- En `sys.query_store_runtime_stats` (`plan_id=25`) queda `avg_rowcount = 495000.5` (promedio entre 1 y 990 000 filas) frente a `StatementEstRows = 1` | - En el plan de QS **no** están las 990 000 filas reales por operador que sí aparecen en `ESC04B.actual.sqlplan`<br>- La discrepancia solo se observa comparando `StatementEstRows` (plan QS) contra `avg_rowcount` (`runtime_stats`) a nivel de **sentencia completa** | Confirma **H4** y el diseño de detección de discrepancia de cardinalidad a nivel sentencia (Clase A + Clase B) |
| **ESC05** — Sort con *spill* a TempDB | `dbo.usp_ESC05`: `ORDER BY Note DESC, Amount` sobre 1 000 000 de filas (`char(200)`) con `OPTION (MAX_GRANT_PERCENT = 0.001)` | `ESC05.qs.1.sqlplan` (4 839 B) vs `ESC05.actual.sqlplan` (17 459 B) | - Operadores `Sort` e `Index Scan`, `EstimateRows="1000000"`, `MemoryGrantInfo` estimada (`SerialRequiredMemory`, `SerialDesiredMemory`)<br>- En `runtime_stats`: `avg_tempdb_space_used = 27968` páginas (~218 MB) y `avg_query_max_used_memory = 912` (8 KB pages) | - **No aparece `<SpillToTempDb>` ni `<SortSpillDetails>` en el plan de QS** (0 ocurrencias)<br>- En `ESC05.actual.sqlplan` sí aparecen `SpillToTempDb=2` y `SortSpillDetails=2` (`GrantedMemory="912"`, escrituras/lecturas de spill) | Confirma **F2** y **F3**: las reglas que exigen advertencias de *spill* por operador son de **Clase C (Podría)**; con Clase B solo se observa uso agregado de TempDB (`avg_tempdb_space_used`) |
| **ESC06** — Plan profundamente anidado (> 128 niveles) | `dbo.usp_ESC06`: cadena de 140 `JOIN` sobre `dbo.Tiny` con `OPTION (FORCE ORDER)` (`maxRelOpDepth = 140`) | `ESC06.qs.1.sqlplan` (304 890 B) vs `ESC06.actual.sqlplan` (396 018 B) | - Leyendo `sys.query_store_plan.query_plan` como `nvarchar(max)` se recupera el Showplan XML **completo** (304 890 caracteres, 140 niveles de `<RelOp>` anidados) | - `TRY_CAST(p.query_plan AS xml)` en SQL Server devuelve **`NULL`** (supera el límite de 128 niveles del tipo `xml` de T-SQL) | Confirma **F8** y valida la regla arquitectónica (§7): leer siempre `query_plan` como `nvarchar(max)`/texto y parsear en .NET con `XmlReader` |

---

## 2. Veredicto sobre las hipótesis H1–H5 y los hallazgos F1–F8 (Apéndice A)

| ID | Enunciado en el Apéndice A | Veredicto | Evidencia empírica en `lab/results/raw/` y `tests/fixtures/plans/` |
|---|---|---|---|
| **F1** (H1) | El plan de Query Store es un plan estimado equivalente a `SHOWPLAN_XML`. | **CONFIRMADO** | En los 9 archivos `*.qs.*.sqlplan` hay `0` nodos `<RunTimeInformation>` y `0` atributos `ActualRows`, mientras que todos los `*.actual.sqlplan` los incluyen. |
| **F2** (H1) | Un plan estimado no contiene información de ejecución ni advertencias de *spill* (`SpillToTempDb`, `SortSpillDetails`, `HashSpillDetails`). | **CONFIRMADO** | En `ESC05.actual.sqlplan` aparecen `SpillToTempDb=2` y `SortSpillDetails=2`; en `ESC05.qs.1.sqlplan` aparecen `0` veces (`checks.txt`). |
| **F3** (H3) | `sys.query_store_runtime_stats` ofrece métricas agregadas por `(plan_id, execution_type, runtime_stats_interval_id)` —incluyendo `avg_rowcount`, `avg_query_max_used_memory`, `avg_dop`, `avg_log_bytes_used` y `avg_tempdb_space_used`— pero ningún contador de *spill* ni filas por operador, y genera múltiples filas por plan a lo largo de los intervalos. | **CONFIRMADO** (con precisión de nombre de columna) | Inspección de las 74 columnas de `sys.query_store_runtime_stats` (`checks.txt`, líneas 3–49): ninguna columna contiene `spill`. Nota técnica: la columna de memoria se llama **`avg_query_max_used_memory`** (no `avg_max_used_memory`). Se observaron múltiples filas por `plan_id` en distintos `runtime_stats_interval_id`, confirmando la necesidad de agregación ponderada por `count_executions` (RF-ING-02). |
| **F4** | Consultar Query Store exige `VIEW DATABASE STATE` (o, en versiones recientes, `VIEW DATABASE PERFORMANCE STATE`) y ningún permiso de escritura. | **CONFIRMADO** | `checks.txt` (líneas 89–136):<br>1. `qd_reader` con solo `VIEW DATABASE STATE` lee todas las vistas de Query Store.<br>2. Un `INSERT` con `qd_reader` falla con error `229` (*The INSERT permission was denied*).<br>3. `qd_noperm` sin permisos falla al leer `sys.query_store_plan` con error `262` (*VIEW DATABASE PERFORMANCE STATE permission denied*).<br>4. Al otorgar únicamente `GRANT VIEW DATABASE PERFORMANCE STATE TO qd_noperm`, la lectura de `sys.query_store_plan` funciona sin otorgar `VIEW DATABASE STATE` completo. |
| **F5** | Disponibilidad y valores por defecto en bases recién creadas en SQL Server 2022: Query Store activo, intervalo de 60 min y retención de 30 días. | **CONFIRMADO** | `F5-defaults.txt` sobre una base `QD_Defaults` recién creada con `CREATE DATABASE` sin tocar opciones: `compatibility_level = 160`, `is_query_store_on = 1`, `actual_state_desc = READ_WRITE`, `interval_length_minutes = 60`, `stale_query_threshold_days = 30`, `query_capture_mode_desc = AUTO`, `max_storage_size_mb = 1000`, `flush_interval_seconds = 900`. |
| **F6** (H5) | El último plan real (`sys.dm_exec_query_plan_stats`) es opcional (`LAST_QUERY_PLAN_STATS`), exige permiso a nivel de servidor y es volátil (vive en la caché de planes). | **CONFIRMADO** (con matiz de comportamiento en 2022) | `checks.txt` (líneas 62–76 y 120–123):<br>1. Por defecto `LAST_QUERY_PLAN_STATS = 0`. Con la opción en `0`, `sys.dm_exec_query_plan_stats` devuelve el XML del plan en caché **sin** `ActualRows` (`filas_reales = no`); solo al activar `ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = ON` incluye `ActualRows` (`filas_reales = si`).<br>2. Al ejecutar `CLEAR PROCEDURE_CACHE`, la fila desaparece inmediatamente (dato volátil).<br>3. Al invocar `sys.dm_exec_query_plan_stats` con `qd_reader` (`VIEW DATABASE STATE`), SQL Server rechaza la consulta con **error 300**: `VIEW SERVER PERFORMANCE STATE permission was denied on object 'server', database 'master'`. |
| **F7** (H2) | La evidencia de compilación (`PlanAffectingConvert` con expresión exacta, `MissingIndexes`, `Key Lookup` con `Lookup="1"`, `Index Scan`/`Seek`, `EstimateRows`) sí está en el plan almacenado en Query Store. | **CONFIRMADO** | Verificado en `ESC01.qs.1.sqlplan`, `ESC02.qs.1.sqlplan`, `ESC03.qs.1.sqlplan` y `ESC04B.qs.1.sqlplan`. |
| **F8** | El tipo `xml` de SQL Server falla al superar 128 niveles de anidamiento; el plan debe leerse como `nvarchar(max)` y tolerarse cuando no sea analizable. | **CONFIRMADO** (con matiz entre `query_store_plan` y `dm_exec_query_plan`) | En `ESC06` (140 niveles de `<RelOp>`):<br>- `TRY_CAST(p.query_plan AS xml)` sobre `sys.query_store_plan` devuelve **`NULL`** (`checks.txt`, línea 53).<br>- `CAST(p.query_plan AS nvarchar(max))` devuelve los **304 890 caracteres** íntegros (`ESC06.qs.1.sqlplan`), que un `XmlReader` con `MaxCharactersInDocument` y sin límite de 128 niveles puede procesar en .NET. |
| **H4** | La discrepancia entre filas estimadas y reales solo puede evaluarse, por defecto, a nivel de sentencia completa (`StatementEstRows` vs `avg_rowcount`), no por operador individual. | **CONFIRMADO** | `checks.txt` (líneas 77–87): en `ESC04B` (`plan_id = 25`), `StatementEstRows = 1.0` mientras `avg_rowcount_ponderado = 495000.5`, sin que existan filas reales por operador en `ESC04B.qs.1.sqlplan`. |

---

## 3. Reproducibilidad (3 corridas consecutivas)

El paso `.\lab\Run-Lab.ps1 -Step repro` ejecutó la carga de trabajo completa, forzó `sp_query_store_flush_db` y extrajo todos los planes de Query Store en 3 corridas seguidas (`lab/results/raw/reproducibility-3runs.txt`). Los hashes SHA-256 (primeros 12 caracteres) y longitudes en bytes fueron idénticos en las 3 corridas:

| Archivo fixture | Bytes | SHA-256 (prefijo) | Corrida 1 | Corrida 2 | Corrida 3 |
|---|---:|---|---|---|---|
| `ESC01.qs.1.sqlplan` | 5 657 | `D61288256087` | igual | igual | igual |
| `ESC02.qs.1.sqlplan` | 6 074 | `A11F27264E94` | igual | igual | igual |
| `ESC03.qs.1.sqlplan` | 5 558 | `98F7F4C5F7CD` | igual | igual | igual |
| `ESC04.qs.1.sqlplan` | 1 264 | `3C01E4419A27` | igual | igual | igual |
| `ESC04.qs.2.sqlplan` | 6 945 | `5D1743E39044` | igual | igual | igual |
| `ESC04.qs.3.sqlplan` | 5 972 | `B10A6C677DF6` | igual | igual | igual |
| `ESC04B.qs.1.sqlplan` | 6 140 | `B3BFAA5419D6` | igual | igual | igual |
| `ESC05.qs.1.sqlplan` | 4 839 | `9E96107B5E6A` | igual | igual | igual |
| `ESC06.qs.1.sqlplan` | 304 890 | `F84D0BA2B32A` | igual | igual | igual |

---

## 4. Validación empírica de la lista blanca de S1-08 (`TelemetryStatementWhitelist`)

Se ejecutaron las 6 sentencias constantes de `src/QueryDoctor.Application/Telemetry/TelemetryStatementWhitelist.cs` contra `QD_Lab` usando la cuenta de mínimo privilegio `qd_reader` (`lab/sql/10-whitelist-validation.sql`, salida en `lab/results/raw/checks.txt` líneas 138–190):

| Clave en `TelemetryStatementWhitelist.Keys` | Resultado con `qd_reader` | Observación |
|---|---|---|
| `DetectEngineVersion` | **OK** (`16.0.4315.2`, `RTM`, `Developer Edition (64-bit)`, `EngineEdition = 3`) | Lista para S1-09 (`CapabilityProbe`). |
| `GetQueryStoreOptions` | **OK** (`actual_state = 2`, `READ_WRITE`, `ALL`, `AUTO`) | Lista para S1-09. |
| `CheckUserPermissions` | **OK** (devuelve `CONNECT`, `VIEW DATABASE STATE`, `VIEW DATABASE PERFORMANCE STATE`, etc.) | Lista para S1-09. |
| `GetTopQueriesByTotalDuration` | **OK** (devuelve el Top-N con `ESC05`, `ESC04`, `ESC04B`, `ESC01`, etc.) | Nota para S1-11: con `QUERY_CAPTURE_MODE = ALL` también entran en Query Store las sentencias DDL/mantenimiento (`CREATE INDEX`, `UPDATE STATISTICS`) si se ejecutaron con Query Store activo. |
| `GetPlanXmlByPlanId` | **OK** (`plan_id = 15`, `5 657` caracteres como `nvarchar(max)`) | Lista para S1-11. |
| `GetQueryStoreViewColumns` | **HALLAZGO DE DEFECTO (devuelve 0 filas)** | La consulta usa `sys.columns` + `sys.objects`. En SQL Server, las vistas del sistema en el esquema `sys` (`sys.query_store_runtime_stats`, `sys.query_store_plan`, etc.) **no aparecen en `sys.objects` ni en `sys.columns`** (`filas_con_sys_columns = 0`), sino en **`sys.all_columns` + `sys.all_objects`** (`filas_con_sys_all_columns = 74`). |

> **Acción recomendada para S1-08 / S1-09 (Maykol y Álvaro):** En una tarea/PR de `TelemetryStatementWhitelist`, cambiar `sys.columns` → `sys.all_columns` y `sys.objects` → `sys.all_objects` en `GetQueryStoreViewColumnsSql` (y ajustar su prueba unitaria). No se modificó en S1-07 para respetar la propiedad de módulos (`CLAUDE.md`).
