# Notas de S1-09 para el equipo (conexiones + CapabilityProbe)

> **De:** Álvaro · **Rama:** `feat/S1-09-conexiones` · Se va completando por partes (P0…P10).
> Qué cambió en S1-09 que **te afecta**, por persona y por parte. Si algo de aquí choca con tu tarea,
> avísame antes de cambiarlo. El contrato HTTP completo está en `docs/CONTRATO_API_CONEXIONES.md`.

## Resumen por persona

| Quién | Lo que tienes que saber | Partes |
|---|---|---|
| **Maykol** | Agregué al compose las variables `Postgres__*` del worker y el secreto `master_encryption_key` (P9). Cambié dos sentencias de tu lista blanca (aprobadas); el Gateway ahora lanza `TelemetrySourceException` tipada en vez de `SqlException`; el Gateway solo se registra en el Worker y la cola `probe` tiene su propio servidor Hangfire; SqlClient reintenta 10 s ante 4060 y un puerto cerrado tarda todo el `Connect Timeout` (medido en el laboratorio) | P0, P5, P6, P8, P9 |
| **Daniel** | Toqué 4 archivos tuyos de S1-04 (estados, columnas, UK, prueba del modelo); hay contrato nuevo para el PR #35; **arreglé un fallo de seguridad de S1-06** (la sesión con cambio de contraseña pendiente perdía la restricción) y el CSRF ahora cubre todo método que cambia estado | P2, P4, P7 |
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

## Pendiente de completar en esta nota
P10 (cierre).
