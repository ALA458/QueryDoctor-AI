# Notas de S1-09 para el equipo (conexiones + CapabilityProbe)

> **De:** Álvaro · **Rama:** `feat/S1-09-conexiones` · Se va completando por partes (P0…P10).
> Qué cambió en S1-09 que **te afecta**, por persona y por parte. Si algo de aquí choca con tu tarea,
> avísame antes de cambiarlo. El contrato HTTP completo está en `docs/CONTRATO_API_CONEXIONES.md`.

## Resumen por persona

| Quién | Lo que tienes que saber | Partes |
|---|---|---|
| **Maykol** | Cambié dos sentencias de tu lista blanca (aprobadas) | P0 |
| **Daniel** | Toqué 4 archivos tuyos de S1-04 (estados, columnas, UK, prueba del modelo); hay contrato nuevo para el PR #35 | P2, P4 |
| **Joel** | `lab/sql/10-whitelist-validation.sql` quedó desactualizado frente a P0; la matriz de soporte y `CanIngest` son funciones puras de Domain | P0, P3 |
| **Todos** | Application ya no puede depender de EF Core, Npgsql, Hangfire ni de Infrastructure (NetArchTest) | P1 |

---

## P0 — Lista blanca de telemetría · `fix(S1-09)` · afecta a **Maykol** y **Joel**

Archivo: `src/QueryDoctor.Application/Telemetry/TelemetryStatementWhitelist.cs` (módulo de Maykol, S1-08).

- **`GetQueryStoreOptionsSql`** ahora lee también `desired_state_desc`, `interval_length_minutes` y
  `stale_query_threshold_days` (retención). Los exige RF-CFG-04 / CA-HU02.1. Existen desde 2016 y en Azure SQL DB.
- **`GetQueryStoreViewColumnsSql`** usa `sys.all_columns` + `sys.all_objects`. Con `sys.columns` devolvía 0 filas
  (hallazgo de S1-07). Verificado con `qd_reader`: 74 columnas, incluidas `avg_tempdb_space_used` y `avg_log_bytes_used`.
- No cambió ninguna clave ni se agregaron sentencias. Hay pruebas nuevas que fallan si alguien vuelve a `sys.columns`.

**Ten en cuenta**
- **Joel:** `lab/sql/10-whitelist-validation.sql` es una copia de las 6 sentencias y aún tiene las versiones viejas
  (secciones 2 y 6). No lo toqué porque es tuyo; conviene sincronizarlo para que el laboratorio pruebe lo mismo que el código.
- **Maykol:** si cambias el texto de una sentencia, `ValidateStatementOrThrow` compara contra la constante; el texto
  de laboratorio no se actualiza solo.
- PowerShell 5.1 antepone un BOM al pasar un archivo por tubería a `sqlcmd` (`Incorrect syntax near '﻿'`).
  Usa `docker compose cp archivo sqlserver:/tmp/x.sql` y `sqlcmd -i /tmp/x.sql`.

## P1 — Regla de arquitectura de Application · `test(S1-09)` · afecta a **todos**

Archivo: `tests/QueryDoctor.ArchitectureTests/LayerDependencyTests.cs`.

- Nueva prueba: ningún tipo de `QueryDoctor.Application` puede depender de `QueryDoctor.Infrastructure`,
  `QueryDoctor.Api`, `QueryDoctor.Worker`, `Microsoft.EntityFrameworkCore`, `Npgsql` ni `Hangfire`.

**Ten en cuenta**
- En Application se trabaja con **interfaces** (`IConnectionRepository`, `IProbeJobScheduler`…) y la implementación
  va en Infrastructure. Si necesitas persistir o encolar desde un caso de uso, define la interfaz en Application.
- NetArchTest detecta **tipos** que usan el paquete, no el `PackageReference`: agregar el paquete sin usarlo no rompe la prueba.
- **Joel (S1-03):** no toqué la regla "SqlClient solo en el namespace del Gateway"; sigue siendo tuya.

## P2 — Estados, causa del fallo y unicidad · `feat(S1-09)` · afecta a **Daniel**

Archivos de Daniel (S1-04) modificados: `Domain/Core/CoreEnums.cs`, `Domain/Core/Connection.cs`,
`Infrastructure/Persistence/Configurations/ConnectionConfiguration.cs`, `tests/.../Persistence/CoreModelTests.cs`.
Migración nueva: `20261113044005_EstadoDeSondeoYUnicidadDeConexion`.

- `ConnectionStatus` suma `Pending`, `Failed` y `Rejected` (al final del enum; se guarda como texto).
- `connection` suma `probe_failure_cause varchar(32)` (**código** del enum cerrado `ProbeFailureCause`, nunca el
  mensaje de la excepción) y `last_probed_at`.
- La UK pasa de `(server, database_name)` a **`(server, database_name_key)`**, donde `database_name_key` es una
  columna generada `lower(database_name) STORED`. Verificado en PostgreSQL: `srv01/qd_lab` tras `srv01/QD_Lab` se rechaza.
- Normalizador `ConnectionAddress` (Domain): el servidor va en minúsculas, sin espacios, sin `tcp:`, sin `,1433` y
  sin punto final; conserva instancia (`srv\inst`) y otros puertos; **rechaza `np:`, `lpc:` y `admin:`** (DAC).
  La base solo se recorta y **conserva mayúsculas** (intercalaciones sensibles a mayúsculas).

**Ten en cuenta**
- **Daniel:** cambió tu aserción de la UK en `CoreModelTests`; ahora espera `(server, database_name_key)`.
- `database_name_key` la llena PostgreSQL: **no la escribas desde C#** (es una propiedad sombra de EF).
- Al consultar por base sin distinguir mayúsculas, compara contra `database_name_key` o usa
  `ConnectionAddress.DatabaseNameKey(...)`; no uses `database_name` directo.
- **Limitación:** no se resuelve DNS. `srv`, `srv.dominio` y su IP cuentan como conexiones distintas.
- La migración no reescribe filas viejas con `server` en mayúsculas (en dev no debería haber).
- Al aplicar la migración en una base vacía, EF muestra `Failed executing DbCommand ... __EFMigrationsHistory`:
  es normal (la tabla aún no existe) y sigue con `Done.`.

## P3 — Matriz de soporte · `feat(S1-09)` · afecta a **Joel** y a quien haga **S1-11**

Archivo nuevo: `src/QueryDoctor.Domain/Core/SupportMatrix.cs` (función pura, sin E/S). Matriz = Cuadro 4.16 del SRS v1.1.

| Motor | Nivel |
|---|---|
| SQL Server anterior a 2016 (< 13.x) | `NotSupported` → la conexión queda `Rejected` con la versión mínima |
| 2016 (13.x) y 2017 (14.x) | `CompatibleUntested` |
| 2019 (15.x), 2022 (16.x), 2025 (17.x) | `Tested` |
| Azure SQL Database (`EngineEdition` 5) | `Tested`, **aunque reporte 12.x** |
| Managed Instance (`EngineEdition` 8), declarada como SQL Server | `CompatibleUntested` |
| Plataforma declarada ≠ detectada, Synapse, Edge | `Failed` con `PlatformMismatch` |

- **Permiso mínimo:** `VIEW DATABASE STATE` siempre; `VIEW DATABASE PERFORMANCE STATE` solo vale en 2022+ y Azure.
- **Funciones omitidas** según las columnas reales de `sys.query_store_runtime_stats`, no según la versión. Hoy solo
  `TempDbAndLogBytes` (RF-ING-03).
- **`QueryStoreState.CanIngest(actual_state_desc)`:** `READ_WRITE` y `READ_ONLY` sí; `OFF`, `ERROR`,
  `READ_CAPTURE_SECONDARY` o sin dato, no.

**Ten en cuenta**
- **Deuda:** Azure SQL Database y SQL Server 2025 quedan `Tested` porque así lo dice la matriz, **sin pruebas
  corridas en esas plataformas**. No afirmarlo como probado en UI ni informes (RF-EVA-04).
- **S1-11 (ingesta):** usa `QueryStoreState.CanIngest` para omitir la ingesta con Query Store OFF. CA-HU02.2
  pasó de S1-09 a S1-11 (aprobado).
- **S1-11:** para saber si llenar `avg_temp_db_space_pages` / `avg_log_bytes_used`, mira `omitted_features_json`
  del perfil, no la versión.

## P4 — Caso de uso de alta · `feat(S1-09)` + `docs(S1-09)` · afecta a **Daniel** y **Maykol**

Archivos nuevos en `src/QueryDoctor.Application/Connections/`: `RegisterConnection`, `IConnectionRepository`,
`IProbeJobScheduler`, `DuplicateConnectionException`, `ConnectionCredentials` (+ codec).
Contrato HTTP: `docs/CONTRATO_API_CONEXIONES.md`.

- El alta valida por campo, normaliza, cifra usuario y contraseña en `encrypted_credentials` (un JSON cifrado con
  `IFieldEncryptor`), guarda en `Pending` y encola el sondeo **solo con el id**.
- Misma base en `Active`, `Pending` o `Disabled` → conflicto (409). En `Failed` o `Rejected` → se reemplaza con las
  credenciales nuevas (mismo id) y se vuelve a sondear.
- Si Hangfire no puede encolar, la conexión queda `Failed/Unknown`: **nunca queda `Pending` para siempre**.

**Ten en cuenta**
- **Daniel (PR #35):** el alta responde **202** (tu `response.ok` ya lo acepta), hay **409** nuevo, tres estados
  nuevos, `failureCause` es un código con `failureMessage` fijo, `hasExcessivePermissions` llega **`null`**
  (= no evaluado; RF-CFG-05 va al Sprint 2) y hay `GET /connections/{id}` para el detalle. Todo en el contrato.
- **Maykol / quien descifre credenciales:** usa `ConnectionCredentialsCodec.Decrypt`; no inventes otro formato.
  `ConnectionCredentials.ToString()` oculta la contraseña, pero igual **no la registres** en logs.
- Los enumerados se aceptan **solo por nombre** (`"SqlServer"`), no por número.

---

## Pendiente de completar en esta nota
P5/P6 (probe, errores tipados del Gateway, tiempo límite, trabajo Hangfire), P7 (endpoints, CSRF),
P8 (pruebas de laboratorio), P9 (compose), P10 (cierre).
