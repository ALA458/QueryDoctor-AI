# Notas de S1-09 para el equipo (conexiones + CapabilityProbe)

> **De:** Álvaro · **Rama:** `feat/S1-09-conexiones` · Completa (P0…P10).
> Qué cambió en S1-09 que **te afecta**, por persona y por parte. Si algo de aquí choca con tu tarea,
> avísame antes de cambiarlo. El contrato HTTP completo está en `docs/CONTRATO_API_CONEXIONES.md`.
> Empieza por el **changelog** (qué entró en cada commit) y el **resumen por persona**. Para probarlo tú mismo,
> ve a la **guía de punta a punta** al final.

## Changelog de la rama (en orden de commit)

| Commit | Tipo | Qué entró | Parte |
|---|---|---|---|
| `16f7f87` | fix | Lista blanca: opciones de Query Store completas y columnas desde `sys.all_*` | P0 |
| `f9d8ceb` | test | NetArchTest: Application no depende de Infrastructure, EF Core, Npgsql ni Hangfire | P1 |
| `28ae9b3` | feat | Estados `Pending`/`Failed`/`Rejected`, causa del fallo como código cerrado, unicidad sin distinguir mayúsculas (migración) | P2 |
| `126e71e` | feat | Matriz de soporte (Cuadro 4.16) y funciones omitidas | P3 |
| `4c35c8e` | feat | Caso de uso `RegisterConnection`: credenciales cifradas, sondeo encolado solo por id | P4 |
| `47b89ef` | docs | `docs/CONTRATO_API_CONEXIONES.md` para S1-15 | P4 |
| `55419f5` | docs | Esta nota, P0 a P4 | — |
| `55fa451` | feat | `CapabilityProbe`, `ProbeConnection`, errores tipados del Gateway y tiempo límite | P5 |
| `c7cdcc1` | feat | Repositorio EF, trabajo `probe_connection`, cola `probe` con sondeo de 1 s, Gateway solo en el Worker | P6 |
| `8cc1413` | fix | **S1-06:** la sesión con cambio de contraseña pendiente conserva la restricción en cada petición | P7 |
| `193b620` | feat | Endpoints `/connections` solo DBA, CSRF en todo método que cambia estado | P7 |
| `68e1996` | fix | Connect Timeout = 2/3 del límite del sondeo; error 258 = servidor inalcanzable | P8 |
| `ff02473` | test | `tests/QueryDoctor.LabTests` (11 pruebas contra SQL Server 2022) y `lab/sql/11-probe-fixtures.sql` | P8 |
| `e9345ff` | feat | Compose: Worker con PostgreSQL, clave maestra como secreto, `docker-compose.lab.yml` con candado | P9 |
| `7f088d2` | docs | Esta nota: changelog y guía de punta a punta | — |
| `2980842` | docs | Cierre: CA-HU02.2 a S1-11, HANDOFF, decisiones D1–D8 en ARCHITECTURE_PLAN | P10 |
| `f7a2ca0` | fix | Hallazgos de la auditoría: M1, B2, B4, B5 y B6 | Auditoría |

## Resumen por persona

| Quién | Lo que tienes que saber | Partes |
|---|---|---|
| **Maykol** | El Gateway rechaza conectar sin usuario en vez de usar `IntegratedSecurity` (auditoría B5) y el worker recibe `APP_ENV` (B6). Agregué al compose las variables `Postgres__*` del worker y el secreto `master_encryption_key` (P9). Cambié dos sentencias de tu lista blanca (aprobadas); el Gateway ahora lanza `TelemetrySourceException` tipada en vez de `SqlException`; el Gateway solo se registra en el Worker y la cola `probe` tiene su propio servidor Hangfire; SqlClient reintenta 10 s ante 4060 y un puerto cerrado tarda todo el `Connect Timeout` (medido en el laboratorio) | P0, P5, P6, P8, P9 |
| **Daniel** | `Connection.Status` ahora vale `Pending` por defecto (auditoría B5). Toqué 4 archivos tuyos de S1-04 (estados, columnas, UK, prueba del modelo); hay contrato nuevo para el PR #35; **arreglé un fallo de seguridad de S1-06** (la sesión con cambio de contraseña pendiente perdía la restricción) y el CSRF ahora cubre todo método que cambia estado | P2, P4, P7 |
| **Joel** | `lab/sql/10-whitelist-validation.sql` quedó desactualizado frente a P0; la matriz de soporte y `CanIngest` son funciones puras de Domain; agregué `lab/sql/11-probe-fixtures.sql` (base sin Query Store y login `qd_perf`) y hay que correrlo después de `build` y `login` | P0, P3, P8 |
| **Todos** | Application ya no puede depender de EF Core, Npgsql, Hangfire ni de Infrastructure (NetArchTest); en Windows usen `127.0.0.1`, no `localhost`, para llegar al SQL Server del compose; **generen `secrets/master_encryption_key.txt`** o el compose no arranca | P1, P8, P9 |

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

## P5 — Sondeo, errores tipados y tiempo límite · `feat(S1-09)` · afecta a **Maykol** y a quien use el Gateway (**S1-11**)

Archivos de Maykol modificados: `Infrastructure/Telemetry/QueryStoreGateway.cs`.
Nuevos: `Application/Telemetry/TelemetrySourceException.cs`, `Infrastructure/Telemetry/SqlErrorClassifier.cs`,
`Application/Connections/ICapabilityProbe.cs` (+ `ProbeOptions`), `Application/Connections/ProbeConnection.cs`,
`Infrastructure/Connections/CapabilityProbe.cs`.

- **El Gateway ya no deja escapar `SqlException`.** Los fallos de conexión o de ejecución salen como
  `TelemetrySourceException` con `Kind` (`InvalidCredentials`, `ServerUnreachable`, `TlsCertificateInvalid`,
  `PermissionDenied`, `Timeout`, `Unknown`) y `SqlErrorNumber`. **Sin** `InnerException` y con mensaje fijo:
  el texto del motor puede traer servidor o usuario (RNF-SEG-09).
- La clasificación (`SqlErrorClassifier`) es por **número de error** y por la cadena de `InnerException`
  (`AuthenticationException` / `Win32Exception` para TLS, `SocketException`), nunca por el texto.
- El log del Gateway (EventId 2001) y `StatementLogEntry.ErrorMessage` guardan solo `Kind (número)`, p. ej.
  `InvalidCredentials (18456)`. Se eliminó `SanitizeError`.
- `OperationCanceledException` y la `SecurityException` de la política TLS / lista blanca **siguen sin envolverse**.
- `CapabilityProbe` usa solo `ITelemetrySource` y 4 claves de la lista blanca; **no** referencia SqlClient.
- `ProbeConnection` (el caso de uso del trabajo) sigue este orden y se detiene en el primer fallo:
  versión/plataforma → permiso mínimo → opciones de Query Store → columnas. Así una versión anterior a 2016 se
  rechaza por versión y no por "no existe `sys.database_query_store_options`".
- **Tiempo límite (RF-CFG-02):** `Probe:TimeoutSeconds` (15 por defecto) acota **todo** el sondeo y también el
  Connect Timeout / CommandTimeout de cada sentencia. Si se agota: `Failed` + `Timeout`. La conexión **nunca**
  queda en `Pending`.

**Ten en cuenta**
- **S1-11 (ingesta) y cualquiera que llame al Gateway:** atrapa `TelemetrySourceException` y decide por `Kind`;
  ya no hay `SqlException` que atrapar (y fuera del Gateway no se puede referenciar SqlClient).
- **Maykol:** el número 4060 ("no puede abrir la base") se clasifica como `PermissionDenied`, porque cubre tanto
  "la base no existe" como "el login no tiene usuario en esa base". Si prefieres separarlos, hace falta un código nuevo.
- `CapabilityProfile.HasExcessivePermissions` se guarda en `false`, que en S1-09 significa **"no evaluado"**
  (la columna no admite null). La API lo expone como `null`. RF-CFG-05 lo llena en el Sprint 2.
- `QueryStoreReadOnlyReason` se guarda como el número de `readonly_reason` en texto (`"0"` = ninguno).
- `omitted_features_json` es un arreglo JSON de códigos: `[]` o `["TempDbAndLogBytes"]`.
- Con Query Store en `OFF` la conexión queda **`Active`** (se avisa que la ingesta se omitirá); no es un fallo.
- `Probe:LabTrustedCertificateServers` es la **única** vía para aceptar el certificado autofirmado del laboratorio
  (invariante 3). Vacía por defecto; el Worker la rechaza fuera de Development (P9).

## P6 — Repositorio EF, trabajo Hangfire y Worker · `feat(S1-09)` · afecta a **Maykol** (S1-10) y a **todos** (DI)

Archivos de Maykol modificados: `Infrastructure/DependencyInjection.cs`, `Worker/Program.cs`.
Nuevos: `Infrastructure/Persistence/EfConnectionRepository.cs`, `Infrastructure/Connections/ProbeConnectionJob.cs`
(+ `HangfireProbeJobScheduler`), proyecto `tests/QueryDoctor.IntegrationTests` (Testcontainers.PostgreSql 4.16.0).

- **`AddInfrastructure` ya no registra el Gateway** (`ITelemetrySource`). La Api también lo llama y nunca debe
  poder conectarse a bases objetivo (invariante 2). Ahora hay dos métodos por host:
  - `AddConnectionRegistration()` → **Api**: `RegisterConnection` + `HangfireProbeJobScheduler`.
  - `AddTargetTelemetry(config)` → **Worker**: Gateway, `CapabilityProbe`, `ProbeOptions` (sección `Probe`),
    `ProbeConnection` y `ProbeConnectionJob`.
- `AddInfrastructure` registra `IConnectionRepository` (EF) y `TimeProvider.System`.
- **Trabajo `probe_connection`:** `ProbeConnectionJob.RunAsync(Guid connectionId, CancellationToken)`, cola `probe`,
  **sin reintentos automáticos** (`AutomaticRetry(Attempts = 0)`): un fallo ya quedó registrado con su causa, y
  reintentar con una contraseña mala solo bloquea la cuenta. Registra solo id, estado y código (EventId 3001–3003).
- **Dos servidores Hangfire en el Worker:** el de siempre atiende todas las colas **menos** `probe` (sondeo 15 s);
  uno nuevo, `QueryDoctor.Worker.Probe`, atiende **solo** `probe` con `QueuePollInterval` de **1 s** y 2 hilos,
  sobre las mismas tablas (`hangfire`). Así el sondeo cabe en los 15 s de RF-CFG-02.
- `EfConnectionRepository` busca por `server` + `database_name_key` y traduce la violación de la UK
  (`23505` en `IX_connection_server_database_name_key`) a `DuplicateConnectionException`.

**Ten en cuenta**
- **Maykol (S1-10):** si agregas una cola o cambias `JobQueues.AllInPriorityOrder`, el servidor principal excluye
  `probe` a propósito; no la vuelvas a meter ahí o dos servidores competirán por ella.
- **S1-11 (ingesta):** para el trabajo `ingest` usa el mismo patrón: el trabajo vive en Infrastructure, el caso de
  uso en Application, y se registra en `AddTargetTelemetry` (Worker), nunca en `AddInfrastructure`.
- Si un host necesita `ITelemetrySource` y no lo encuentra en DI: es porque solo el Worker lo tiene. Es intencional.
- **Pruebas de integración:** `tests/QueryDoctor.IntegrationTests` levanta `pgvector/pgvector:pg16` con
  Testcontainers y aplica las migraciones. **Necesitan Docker** (en CI hay, `ubuntu-latest`). Las del PR #35 / S2
  que toquen la base pueden reutilizar `PostgresFixture` con `[Collection(PostgresGroup.Name)]`.
- Hangfire guarda los argumentos en claro: hay una prueba que lee `hangfire.job` y comprueba que el trabajo solo
  lleva el `Guid` y no la contraseña.

## P7 — Endpoints `/connections`, CSRF y un fix de S1-06 · `fix(S1-09)` + `feat(S1-09)` · afecta a **Daniel**

Archivos de Daniel modificados: `Api/Auth/AuthEndpoints.cs`, `Api/Auth/AuthServiceCollectionExtensions.cs`,
`Api/Auth/SameOriginFormsExtensions.cs`, `Api/Program.cs`. Nuevos: `Api/Auth/MustChangePasswordClaimsPrincipalFactory.cs`,
`Api/Connections/ConnectionEndpoints.cs`, `Application/Connections/ConnectionViews.cs`,
`Infrastructure/Persistence/EfConnectionQueries.cs`, `tests/QueryDoctor.IntegrationTests/ConnectionEndpointsTests.cs`.

### ⚠️ Fix de seguridad en S1-06 (RNF-SEG-13)
- **El fallo:** un DBA con `MustChangePassword = true` (el DBA inicial) quedaba restringido solo en la **primera**
  petición. Con `SecurityStampValidatorOptions.ValidationInterval = 0`, Identity reconstruye el principal en cada
  petición con el `UserClaimsPrincipalFactory` estándar, y el claim `qd:must_change_password`, que solo se agregaba
  al iniciar sesión, se perdía. Desde la segunda petición la sesión pasaba `DbaOnly`, la `FallbackPolicy` y el
  panel de Hangfire **sin haber cambiado la contraseña**.
- **El arreglo:** `MustChangePasswordClaimsPrincipalFactory` agrega el claim siempre que `user.MustChangePassword`
  es `true` en la base. El login ya no usa `SignInWithClaimsAsync` (sería un claim duplicado).
- **Cómo lo detectamos:** una prueba de integración con la Api real. Las pruebas unitarias de S1-06 arman el
  `ClaimsPrincipal` a mano y por eso no lo veían. La prueba repite la petición 3 veces a propósito.

**Ten en cuenta (Daniel)**
- Si agregas otro claim propio de la sesión, va en `MustChangePasswordClaimsPrincipalFactory.GenerateClaimsAsync`,
  no en el login: todo lo que solo se agrega al iniciar sesión se pierde en la siguiente petición.
- Para probar autorización usa `ApiFactory` (en `ConnectionEndpointsTests.cs`): levanta la Api real sobre
  PostgreSQL con Testcontainers, crea usuarios con rol e inicia sesión por `/auth/login`. Usa `https://localhost`
  porque la cookie es `Secure`.

### CSRF
- `UseSameOriginForms` ahora cubre **todo** `POST`, `PUT`, `PATCH` y `DELETE`, no solo `/auth/*`. Un endpoint nuevo
  queda protegido por defecto. Las peticiones sin `Origin` ni `Sec-Fetch-Site` (curl) siguen permitidas, como antes.
- El panel de Hangfire (`/hangfire`) también hace POST desde el mismo origen: sigue funcionando.

### Endpoints
- `POST /connections` → 202 + `Location` / 400 por campo / 409 / 401 / 403. `GET /connections` y
  `GET /connections/{id}` → 200 / 404. Todo con `DbaOnly`. Contrato: `docs/CONTRATO_API_CONEXIONES.md`.
- Las respuestas usan `JsonStringEnumConverter` (enumerados como texto) y `AddProblemDetails`.
- `capabilities`, `supportLevel`, `engineVersion` y `queryStoreState` solo se muestran si la conexión está
  `Active` o `Disabled`: tras un reemplazo (D3) puede quedar un perfil viejo en la base.
- `hasExcessivePermissions` siempre es `null` (no evaluado; RF-CFG-05 va al Sprint 2).
- Hay una prueba que comprueba que la Api **no puede resolver** `ITelemetrySource` ni `ICapabilityProbe`.

**Ten en cuenta**
- **S2-12 (Desarrollador):** hoy todo `/connections` es solo DBA. El filtro por conexión asignada va en
  `IConnectionQueries` (en la consulta, RNF-SEG-12), no en la UI ni en el endpoint.
- `Program` de la Api ahora es `public partial` para `WebApplicationFactory`. No lo quites.

---

## P8 — Pruebas contra el laboratorio · `test(S1-09)` + `fix(S1-09)` · afecta a **Joel**, **Maykol** y **todos**

Proyecto nuevo `tests/QueryDoctor.LabTests`: el sondeo real del Worker (`ProbeConnection` → `CapabilityProbe` →
`QueryStoreGateway` → SqlClient) contra el SQL Server 2022 del compose. Solo el repositorio y el cifrado son dobles.

**Cómo correrlas**
1. `docker compose up -d sqlserver`, `Run-Lab.ps1 -Step build` y `-Step login` (si no lo hiciste ya).
2. `lab/sql/11-probe-fixtures.sql` como `sa`, con `READER_PASSWORD` (igual que el 04). **Hay que repetirlo
   cada vez que se corre `build`**, porque `01-database.sql` recrea `QD_Lab` y borra sus usuarios.
3. `$env:QD_LAB_SQL = "127.0.0.1"` y `dotnet test tests/QueryDoctor.LabTests`.

Sin `QD_LAB_SQL` las 11 pruebas salen **Omitidas** (así corren en CI). **Omitida no es evidencia** (D7): la
evidencia es la corrida local. Si `QD_LAB_SQL` está definida pero falta el archivo de contraseña, la prueba
**falla**, no se omite. El skip es un `[LabFact]` propio (xUnit 2): no se agregó ningún paquete.

**Qué cubre (11 pruebas)**: `qd_reader` → `Active` con perfil 16.x / Developer / `Tested` / `READ_WRITE` / nada
omitido; las 4 sentencias del sondeo son las de la lista blanca, todas `SELECT`, y quedan en `statement_log`; la
configuración de Query Store es idéntica antes y después; `qd_perf` (solo `VIEW DATABASE PERFORMANCE STATE`) →
`Active`; contraseña mala → `InvalidCredentials`; `qd_noperm` → `MissingPermission`; base inexistente →
`MissingPermission` (SQL Server da 4060 en ambos casos, no los distingue a propósito); **sin la excepción de TLS
del laboratorio el certificado autofirmado se rechaza** (`TlsCertificateInvalid`, invariante 3 probado de verdad);
puerto cerrado → `ServerUnreachable`; `QD_Lab_NoQS` → `Active` con `OFF`, `CanIngest` falso y sin activarlo;
versión 12.x sustituida sobre el sondeo real → `Rejected`, credenciales borradas y una sola sentencia ejecutada.

### Lo que el laboratorio encontró (y no habrían encontrado los dobles)
1. **`localhost` en Windows se cuelga.** Resuelve primero a `::1` y el compose publica `127.0.0.1:1433`; SqlClient
   espera en IPv6 hasta agotar el tiempo. Usen `127.0.0.1`. Dentro del compose (`sqlserver`) no pasa.
2. **Carrera entre dos relojes** (corregido en `ProbeConnection`, código mío). Ante un puerto cerrado SqlClient
   reintenta hasta agotar su `Connect Timeout`, que era igual al límite del sondeo: salía `ServerUnreachable` o
   `Timeout` según qué reloj ganara. Ahora SqlClient recibe **2/3 de `Probe:TimeoutSeconds`** (15 → 10 s).
3. **Error 258** (`WAIT_TIMEOUT`): es lo que devuelve ese puerto cerrado cuando SqlClient corta por su propio
   tiempo (antes era 10061). Agregado a `SqlErrorClassifier` como `ServerUnreachable`.
4. **Para Maykol (S1-10/S1-11):** SqlClient trata 4060 como transitorio y reintenta tras `ConnectRetryInterval`
   (10 s). En el sondeo ya no ocurre porque no le queda tiempo, pero en la ingesta un 4060 tardará 10 s más.
   En Azure ese reintento es útil; no lo toqué.

**Ten en cuenta**
- Si alguien corre `lab/sql/09-grant-perf-state.sql`, `qd_noperm` gana permiso y su prueba falla. Vuelve a
  correr `-Step login` para dejarlo sin permisos.
- La lectura de Query Store para comparar antes y después la hace **la prueba** con SqlClient, no la plataforma;
  no es una sentencia nueva de la lista blanca.

---

## P9 — Compose: Worker conectado, clave maestra y excepción TLS aparte · `feat(S1-09)` · afecta a **Maykol** y a **todos**

En `docker-compose.yml` (de Maykol) **solo se agregó**, no se cambió nada existente:
- `worker`: variables `Postgres__*`. Sin ellas el Worker no registraba **ningún** servidor Hangfire y nada se
  ejecutaba. Ahora también espera a que `api` esté sana, porque la api aplica las migraciones.
- `api` y `worker`: secreto nuevo `master_encryption_key` + `Security__SecretProtector__MasterKeyPath`. Sin él,
  `POST /connections` daba 500 al cifrar las credenciales.

**Cada uno genera su clave una sola vez** (32 bytes aleatorios en Base64; `secrets/` está en `.gitignore`):

```powershell
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [IO.File]::WriteAllText("$PWD\secrets\master_encryption_key.txt", [Convert]::ToBase64String($b))
```

⚠️ **No la regeneres** con datos cifrados en tu PostgreSQL: lo cifrado con la clave anterior no se puede descifrar.
La CI no necesita nada: ya crea un archivo de relleno por cada secreto que declara el compose.

### Excepción de TLS del laboratorio (invariante 3)
- Vive **solo** en `docker-compose.lab.yml`, que agrega `Probe__LabTrustedCertificateServers__0: sqlserver` al
  worker. El compose principal no la tiene. Para el laboratorio:
  `docker compose -f docker-compose.yml -f docker-compose.lab.yml up -d --build`.
- Sin ese archivo, una conexión al `sqlserver` del compose queda `Failed` con `TlsCertificateInvalid`. Es el
  comportamiento correcto: el certificado es autofirmado.
- **Candado:** `LabCertificateGuard` hace que el Worker **no arranque** si la excepción está puesta y el entorno no
  es `Development` (`APP_ENV`). No lo debiliten (mismo espíritu que el candado de ADR-10 en el servicio de IA).

### Alta real de punta a punta (2026-11-13, compose + `docker-compose.lab.yml`)
Login del DBA → `POST /api/connections` (`sqlserver` / `QD_Lab` / `qd_reader`) → `202 Pending` → el Worker
registró `probe_connection <id>: Active` (EventId 3001) → `GET /api/connections/{id}` a los 3 s: `Active`,
16.0.4305.5 Developer, `Tested`, `READ_WRITE`, `canIngest: true`, nada omitido. Buscar la contraseña de
`qd_reader` en los logs de `api` y `worker` no devuelve nada.

**Ten en cuenta (para Daniel, S1-06)**
- Si se levanta el compose **antes** de crear `secrets/initial_dba_password.txt`, Docker crea un **directorio**
  con ese nombre y el DBA inicial nunca se crea. Hay que borrarlo y crear el archivo.
- Una contraseña inicial que no cumple la política tumba la api al arrancar (solo muestra los códigos, bien) y
  `restart: unless-stopped` la reinicia en bucle: cuando se corrige el archivo, el DBA se crea en uno de esos
  reinicios, así que la línea "DBA inicial creado" puede no estar en los logs del contenedor actual.
- En PowerShell 5.1 los argumentos vacíos no llegan a `curl.exe`: una variable sin definir desordena todo el
  comando. Comprueben las variables antes.

---

## Guía: probar el alta de punta a punta y leer la respuesta

**Requisitos:**
- `secrets/master_encryption_key.txt` y `secrets/initial_dba_password.txt` existen como **archivos**, no
  como directorios (ver P9).
- El laboratorio está construido con `Run-Lab.ps1 -Step build` y `-Step login`.
- El compose está levantado con el override:
  `docker compose -f docker-compose.yml -f docker-compose.lab.yml up -d --build`.
- El DBA ya hizo su primer ingreso en el navegador (http://localhost:8080/login), porque ese ingreso obliga a
  cambiar la contraseña.

Los comandos son para PowerShell 5.1:
- Se usa `curl.exe` porque en PowerShell `curl` es otro comando.
- El cuerpo va en un archivo porque PowerShell 5.1 quita las comillas de los argumentos.
- La contraseña se pide con `-AsSecureString` para que **no quede en pantalla** ni en lo que copies al chat.

```powershell
$jar = "$env:TEMP\qd.jar"
$s = Read-Host "Contraseña del DBA" -AsSecureString
$dba = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
"jar=$jar | la contraseña tiene $($dba.Length) caracteres"     # si sale 0, no sigas
curl.exe -s -o NUL -w "login %{http_code} -> %{redirect_url}`n" -c $jar --data-urlencode "usuario=dba" --data-urlencode "password=$dba" http://localhost:8080/api/auth/login
curl.exe -s -b $jar http://localhost:8080/api/auth/me
$reader = (Get-Content secrets\mssql_reader_password.txt -Raw).Trim()
[IO.File]::WriteAllText("$env:TEMP\qd-body.json", (@{ platform="SqlServer"; server="sqlserver"; databaseName="QD_Lab"; authenticationMethod="SqlAuthentication"; username="qd_reader"; password=$reader } | ConvertTo-Json -Compress))
$resp = curl.exe -s -b $jar -H "Content-Type: application/json" --data-binary "@$env:TEMP\qd-body.json" http://localhost:8080/api/connections; Remove-Item "$env:TEMP\qd-body.json"; $resp
$id = ($resp | ConvertFrom-Json).id; Start-Sleep -Seconds 3; curl.exe -s -b $jar "http://localhost:8080/api/connections/$id"
docker compose logs api worker | Select-String -SimpleMatch $reader   # no debe imprimir nada
```

**Lo esperado, paso a paso:**
1. El login responde `302 -> http://localhost:8080/`. Si va a `/cambiar-contrasena`, falta el primer ingreso.
2. `/auth/me` muestra el rol `DBA` y `mustChangePassword: false`.
3. El `POST` responde `{"id":…,"status":"Pending"}`.
4. Tres segundos después, el `GET` devuelve `Active`.

Si la conexión ya existía, el `POST` responde **409** con `existingConnectionId`.

### Cómo leer `GET /connections/{id}` (corrida real del 2026-11-13)

Ese `GET` recorre todo el camino real, sin dobles:
1. La api recibe el alta.
2. PostgreSQL guarda las credenciales cifradas.
3. Hangfire encola el trabajo.
4. El Worker lo toma y sondea con `QueryStoreGateway`, por TLS, contra SQL Server.
5. El perfil queda guardado y la api lo devuelve.

Cubre CA-HU01.1, CA-HU02.1 y RF-CFG-07.

**Estado de la conexión**

| Campo | Valor real | Qué significa |
|---|---|---|
| `status` | `Active` | El Worker sondeó y todo salió bien. Si no, sale `Failed`, o `Rejected` si la versión es anterior a 2016. **Nunca se queda en `Pending`** (RF-CFG-02). |
| `failureCause` / `failureMessage` | `null` | No hubo fallo. Si lo hay, sale un código cerrado (`InvalidCredentials`, `TlsCertificateInvalid`, …) con su mensaje fijo, nunca el texto de SQL Server. |
| `lastProbedAt` | 14:59:54 | Cuándo terminó el sondeo: 2 o 3 s después del `POST`, gracias a la cola `probe` que revisa cada 1 s. |
| `minimumSupportedVersion` | SQL Server 2016 (13.x) | Siempre presente, para que se pueda explicar un rechazo (CA-HU01.3). |
| `lastIngestionAt` | `null` | Todavía no hay ingesta (S1-10/S1-11). |

**Lo que detectó el sondeo (`capabilities`, RF-CFG-03)**

| Campo | Valor real | De dónde sale |
|---|---|---|
| `engineVersion` | `16.0.4305.5` | `SERVERPROPERTY('ProductVersion')`: SQL Server 2022 |
| `edition` | Developer Edition (64-bit) | `SERVERPROPERTY('Edition')` |
| `supportLevel` | `Tested` | La matriz del Cuadro 4.16 (P3) |
| `hasExcessivePermissions` | `null` | **No evaluado**: RF-CFG-05 va al Sprint 2. `null` no significa "la cuenta está bien". |

**Query Store (`capabilities.queryStore`, CA-HU02.1, RF-CFG-04).** Se lee de `sys.database_query_store_options`
y coincide con lo que configura `lab/sql/01-database.sql`.

| Campo | Valor real | Qué significa |
|---|---|---|
| `actualState` / `desiredState` | `READ_WRITE` / `READ_WRITE` | Está activo, y así se quiere |
| `readOnlyReason` | `0` | No está forzado a solo lectura |
| `intervalLengthMinutes` | `1` | Configuración del laboratorio (el valor por defecto es 60) |
| `captureMode` | `ALL` | Configuración del laboratorio (el valor por defecto es `AUTO`) |
| `retentionDays` | `30` | `stale_query_threshold_days` |
| `canIngest` | `true` | Regla D5: vale `READ_WRITE` o `READ_ONLY`. S1-11 la usa para CA-HU02.2 |

**`omittedFeatures: []`** (RF-CFG-07, RNF-COM-04): la vista `query_store_runtime_stats` de 2022 trae las columnas
de RF-ING-03, así que no se omite nada. En una versión que no las tenga sale `TempDbAndLogBytes` con su mensaje.

### La última línea: buscar la contraseña en los logs

`Select-String -SimpleMatch $reader` busca **la contraseña real de `qd_reader`, como texto literal**, en todos
los logs de `api` y `worker`. Esa contraseña llegó en el `POST`, se cifró, el Worker la descifró y la usó para
conectarse a SQL Server. Si en algún punto se hubiera registrado (en el cuerpo de la petición, en la cadena de
conexión o en un error de SqlClient), la búsqueda imprimiría esa línea.

**Que no imprima nada es la evidencia de RNF-SEG-09.** Solo vale después de un alta real.

Para comprobar que la búsqueda sí funciona, busca algo que tiene que aparecer, como el id de la conexión:
`docker compose logs worker | Select-String -SimpleMatch $id` imprime `probe_connection <id>: Active`
(EventId 3001).

### Romperlo a propósito: sin la excepción de laboratorio

1. Recrea el worker solo con el compose principal: `docker compose up -d --force-recreate worker`.
2. Repite el `POST` con `databaseName="QD_Lab_NoQS"`. Debe terminar `Failed` con
   `failureCause: TlsCertificateInvalid`.
3. Restaura el override:
   `docker compose -f docker-compose.yml -f docker-compose.lab.yml up -d --force-recreate worker`.
4. Repite el mismo `POST`. Ahora reemplaza la conexión fallida (D3) y debe quedar `Active` con
   `actualState: OFF` y `canIngest: false`.

---

## P10 — Cierre · `docs(S1-09)` · afecta a **todos**

- `/verify` **VERDE** (2026-11-13): build y format limpios; 265 unitarias, 3 de arquitectura, 18 de integración y
  **11 de laboratorio contra SQL Server 2022 real**, además del alta de punta a punta en compose.
- **S1-09 cierra 5 de 6 criterios.** CA-HU02.2 pasa a **S1-11** (ya está en `docs/sprints/SPRINTS.md`). Daniel:
  sincroniza el `.xlsx` y lleva el traslado al tutor en S1-20.
- `docs/HANDOFF.md` y `docs/ARCHITECTURE_PLAN.md` (sección "Decisiones de implementación de S1-09", D1–D8)
  están actualizados.

**Deuda que queda abierta**
- D4: Azure SQL Database y SQL Server 2025 figuran `Tested` sin pruebas ejecutadas contra ellos.
- OWASP A01 (SSRF): un DBA puede registrar cualquier host:puerto y el sondeo revela qué puertos están abiertos;
  la red `targets` no es `internal`.
- Unicidad sin resolver DNS · RF-CFG-05 en el Sprint 2 · `statement_log` en memoria hasta S2-06.

**Falta antes de fusionar:** revisión humana del PR (la auditoría por ejes ya se hizo, ver abajo).

---

## Auditoría por ejes · `fix(S1-09)` `f7a2ca0` · afecta a **Daniel**, **Maykol** y a quien haga **S1-11**

La hizo **otra sesión**, que solo leyó el código, como pide la Definition of Done. Veredicto: **APROBABLE con una
condición** (corregir M1). Corregimos M1 y cuatro hallazgos bajos.

| Hallazgo | Qué pasaba | Corrección |
|---|---|---|
| **M1** (media) | Si PostgreSQL fallaba fuera del `try` del sondeo, el trabajo (sin reintentos) terminaba y la conexión quedaba en `Pending` **para siempre**. Además, el alta responde 409 a una `Pending`, así que el DBA no podía reintentar | `ProbeConnectionJob` marca la conexión `Failed`/`Unknown` en un scope nuevo, con `UPDATE … WHERE status = 'Pending'`. Si ni eso funciona, Hangfire reintenta (3 veces); el reintento solo sondea lo que siga en `Pending`. Los fallos del sondeo en sí siguen sin reintentarse (bloqueo de cuentas). Log nuevo: EventId 3004, solo el tipo de excepción |
| **B2** | La búsqueda de duplicados usaba `ToLowerInvariant()` de C#, pero la UK usa `lower()` de PostgreSQL; con nombres no ASCII podían discrepar y el 409 salía sin id | La búsqueda calcula `lower()` en PostgreSQL (`FromSql`, parametrizado) |
| **B4** | Un error `Unknown` al leer Query Store se mostraba como `QueryStoreUnavailable` | Solo `PermissionDenied` es `QueryStoreUnavailable`; `Unknown` queda `Unknown` |
| **B5** | `Connection.Status` valía `Active` por defecto, y el Gateway usaba `IntegratedSecurity` (la identidad del Worker) si faltaba el usuario | `Pending` por defecto; el Gateway lanza `SecurityException` sin usuario |
| **B6** | El candado de TLS solo miraba el nombre del entorno, y el compose pone `Development` si falta `APP_ENV` | También se niega con `APP_ENV=prod`, y solo admite el servidor exacto `sqlserver` |

**Ten en cuenta**
- **Daniel:** si en algún lado se creaba un `Connection` contando con que naciera `Active`, ahora nace `Pending`.
  No había ninguno en el repo.
- **Maykol / S1-11:** el Gateway ya no acepta conexiones sin usuario: el MVP solo tiene autenticación SQL.
- **Todos:** el compose pasa `APP_ENV` al worker. En producción tiene que ser `APP_ENV=prod`.

**Queda como deuda (baja severidad, con el PR):**
- **B1, SSRF (OWASP A01):** el Worker está en `targets` **y en `backend`**, así que un DBA puede sondear también
  `db`, `api` o `ai` y deducir qué puertos están abiertos por el `failureCause`. Falta una lista de hosts permitidos
  que niegue también la red interna.
- **B3:** el error 4060 aparece igual si la base no existe o si no hay acceso, y se muestra como `MissingPermission`.
  Para S1-15: que el mensaje diga "no existe o sin acceso".
- **B7 (Daniel):** `OriginCheck` compara solo el host del `Origin`, no el esquema ni el puerto. Mitiga que la cookie
  sea `SameSite=Strict`.
- **B8 (S2-06):** el alta y el reemplazo de una conexión no dejan registro de auditoría.
- **B9 (S2-06):** el `statement_log` en memoria no tiene límite de tamaño.
