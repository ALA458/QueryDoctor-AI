# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-13 · **Por:** Joel · **Rama:** `feat/S1-03-pruebas-arquitectura`

## Estado de tareas en `main` + rama actual
- ✅ **S1-01** (Álvaro): Solución .NET 10, reglas de build, NetArchTest base y CI.
- ✅ **S1-02** (Maykol): Docker Compose con redes segmentadas, `sqlserver` 2022 y Docker secrets.
- ✅ **S1-04** (Daniel): Entidades y migración inicial EF Core de los esquemas `core` y `ops`.
- ✅ **S1-05** (Maykol): `SecretProtector` AES-256-GCM por campo.
- ✅ **S1-06** (Daniel): Identity mínimo, login, bloqueo por intentos y cambio obligatorio de contraseña.
- ✅ **S1-07 / S0-10** (Joel): Laboratorio SQL Server 2022 (`lab/`), 16 fixtures `.sqlplan` y validación empírica F1–F8.
- ✅ **S1-08** (Maykol): `QueryStoreGateway` + `TelemetryStatementWhitelist` + `statement_log`.
- ✅ **S1-10** (Maykol): Hangfire sobre PostgreSQL con cliente en `Api` y servidor en `Worker`.
- ✅ **S1-12 / S1-13 / S1-14** (Joel, en PR): `ShowplanXmlParser`, `DeterministicRuleEngine`, esquema `analysis` y reglas `REG-01`, `REG-02`, `REG-04`.
- ✅ **S1-03** (Joel, en esta rama): Suite completa de `NetArchTest` en `QueryDoctor.ArchitectureTests` (`LayerDependencyTests` + `ArchitectureRuleCanaryTests`):
  1. Pureza de `Domain` y del motor determinista (`Engine` / `Scoring` / `Validation`) sin EF Core, `SqlClient`, ASP.NET ni E/S, y dirección estricta de capas.
  2. Aislamiento entre módulos verticales de `Application` e `Infrastructure` (`RNF-MAN-01`) y separación de procesos en `Api` (sin acceso al Gateway ni al cliente de IA).
  3. Exclusividad de `Microsoft.Data.SqlClient` restringida únicamente a `QueryStoreGateway` (`RNF-SEG-01`, `HU-15`).
  4. Pruebas negativas con tipos canario que demuestran automáticamente que cada una de las 3 reglas falla cuando se rompe a propósito.

## Lo que necesita saber cada uno
**Daniel**
- Fix de seguridad de S1-06 (`8cc1413`): la sesión con cambio de contraseña pendiente perdía la restricción desde
  la segunda petición. Un claim propio va en `MustChangePasswordClaimsPrincipalFactory`, no en el login.
- S1-15: contrato en `docs/CONTRATO_API_CONEXIONES.md` (202 + `Pending`, estados nuevos, `failureCause`).
- Sincronizar `docs/QueryDoctor_Sprints (1).xlsx` con SPRINTS.md (S1-09 Hecho, CA-HU02.2 en S1-11).
- S1-20: pedir al tutor que ratifique el traslado de CA-HU02.2 a S1-11.
- `Connection.Status` vale `Pending` por defecto (B5). B7: `OriginCheck` solo compara el host del `Origin`.

**Maykol**
- Compose: el worker ya tiene `Postgres__*` y el secreto `master_encryption_key`; la excepción de TLS vive solo en
  `docker-compose.lab.yml`, con candado (`LabCertificateGuard`).
- Gateway: lanza `TelemetrySourceException` con un código; solo se registra en el Worker (`AddTargetTelemetry`).
- S1-10/S1-11: SqlClient reintenta 4060 tras 10 s (`ConnectRetryInterval`). El Gateway rechaza conectar sin usuario (B5).

**Joel**
- **S2-01 (`REG-03`) y S2-02 (`HealthScoreCalculator`):** Siguientes tareas del motor y score sobre la base de `S1-14`.
- Las reglas de `NetArchTest` en `S1-03` ya vigilan `QueryDoctor.Application.Engine`, `QueryDoctor.Application.Scoring` y `QueryDoctor.Application.Validation`.

**Maykol y Álvaro**
- **S1-09 / S1-11 (Álvaro):** Cualquier nuevo servicio en `Infrastructure` (p. ej. `CapabilityProbe` o `IngestionService`) debe consumir `ITelemetrySource` y **nunca** referenciar `Microsoft.Data.SqlClient` directamente, ya que `SoloQueryStoreGatewayUsaSqlClientEnTodaLaSolucion` bloqueará el build si otro tipo importa `Microsoft.Data.SqlClient`.

**Daniel**
- **S1-15 / S1-16 y S1-20:** Los endpoints de `QueryDoctor.Api` nunca deben referenciar `QueryDoctor.Infrastructure.Telemetry`, `QueryDoctor.Application.Telemetry` ni clientes de IA (`EvaluateApiProcessSeparation`).

## Siguiente paso
Abrir y fusionar PR de `feat/S1-03-pruebas-arquitectura` → `main` y continuar con **S2-01** (`REG-03`) y **S2-02** (`HealthScoreCalculator`).
