# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-12 · **Por:** Joel · **Rama:** `feat/S1-13-catalogo-reglas`
**Actualizado:** 2026-11-11 · **Por:** Joel · **Rama:** `feat/S1-12-plan-parser`

## Estado de tareas en `main` + rama actual
- ✅ **S1-01** (Álvaro): Solución .NET 10, reglas de build, NetArchTest base y CI.
- ✅ **S1-02** (Maykol): Docker Compose con redes segmentadas, `sqlserver` 2022 y Docker secrets.
- ✅ **S1-04** (Daniel): Entidades y migración inicial EF Core de los esquemas `core` y `ops`.
- ✅ **S1-05** (Maykol): `SecretProtector` AES-256-GCM por campo.
- ✅ **S1-06** (Daniel): Identity mínimo, DBA inicial desde secreto, login, cambio obligatorio de contraseña y bloqueo por intentos.
- ✅ **S1-07 / S0-10** (Joel): Laboratorio SQL Server 2022 (`lab/`), 7 escenarios sembrados, 16 fixtures `.sqlplan` y verificación empírica F1–F8.
- ✅ **S1-08** (Maykol): `QueryStoreGateway` + `TelemetryStatementWhitelist` + `statement_log`.
- ✅ **S1-12** (Joel): `ShowplanXmlParser` (`IPlanParser`) con `XmlReader` seguro, árbol tipado en `QueryDoctor.Domain.Plans` y emisión de `REG-11` sin detener el lote.
- ✅ **S1-13** (Joel, en esta rama): Catálogo de reglas versionado (`IRule`, `IRuleCatalog`, `VersionedRuleCatalog` con `REG-01`..`REG-11`), umbrales configurables (`RuleThresholds`), motor determinístico (`IRuleEngine`, `DeterministicRuleEngine`), contrato JSON versionado del hallazgo (`FindingContract`), entidades de `QueryDoctor.Domain.Analysis` y migración EF Core `EsquemaAnalysis` (`analysis.rule_catalog`, `analysis.threshold_set`, `analysis.analysis_run`, `analysis.finding`).
- ✅ **S1-12** (Joel, en esta rama): `ShowplanXmlParser` (`IPlanParser`) en memoria sin E/S con `XmlReader` seguro (sin DTD, con límites de caracteres y profundidad), modelo tipado inmutable en `QueryDoctor.Domain.Plans`, soporte para planes *Dispatcher* de PSP, y emisión de `REG-11` ante XML nulo/truncado/inválido sin detener el lote.
**Actualizado:** 2026-11-13 · **Por:** Álvaro · **Rama:** `feat/S1-11-ingesta` (sale de `main`, no apilada)

## Estado
- ✅ En `main`: S1-01, S1-02, S1-04 a S1-10.
- 🔄 **S1-11** (Álvaro, esta rama, PR por abrir): ingesta incremental. `/verify` VERDE con corrida real del
  laboratorio (14/14). Cierra CA-HU03.1–03.3, CA-HU03.4 con fallo inyectado (corte de red real **recortado** al
  Sprint 2) y **CA-HU02.2** (venía de S1-09). Preauditoría APROBABLE con B1, B2 y B6 ya corregidos (`cc55bf4`);
  falta la revisión por ejes en otra sesión.
- Decisiones I1–I8 y mecánica: `docs/ARCHITECTURE_PLAN.md` §S1-11. Contrato HTTP: `docs/CONTRATO_API_CONEXIONES.md`.

## Cómo probarla
- Ingesta manual: `POST /connections/{id}/ingestions` (DBA, conexión `Active`) → 202; resultado y causa en
  `GET /connections/{id}/ingestions`; `lastIngestionAt` en `GET /connections`.
- Laboratorio: carga con `05-workload.sql` y luego `QD_LAB_SQL=127.0.0.1 dotnet test tests/QueryDoctor.LabTests`
  (necesita Docker: levanta un PostgreSQL desechable).

## Lo que necesita saber cada uno
**Daniel**
- S1-15/S1-16: contrato nuevo de ingestas (`cause` + `causeMessage`, pintar como texto). `GET /queries` no es de S1-11.
- Sin migración en S1-11: no choca con `EsquemaAnalysis` de S1-13.
- `query.origin_context` llega vacío con la cuenta de mínimo privilegio (SQL Server oculta `OBJECT_NAME`).
- Sincronizar el `.xlsx` con SPRINTS.md (S1-11 Hecho, RNF-FIA-01 recortado, CA-HU02.2 cerrado aquí).

**Maykol**
- Lista blanca: 3 sentencias nuevas (`GetIngestionRuntimeStats`, `…WithoutTempDbLog`, `GetQueryTextById`).
- `InMemoryStatementLogWriter` no tiene límite y ahora crece con cada ingesta (B9): urge antes de la recurrente.
- Sprint 2: registrar el `RecurringJob` de `IngestConnectionJob.RunScheduledAsync` (RF-ING-06, DA-03).

**Joel**
- **S1-14 (`REG-01`, `REG-02`, `REG-04`):** Solo debes implementar `IRule` para cada regla en `QueryDoctor.Application.Engine.Rules` usando `VersionedRuleCatalog.BuiltInDescriptors` y registrarlas en `DependencyInjection.cs` (`services.AddSingleton<IRule, ...>()`).
- **S2-02 (`HealthScoreCalculator`):** Las enumeraciones `FindingSeverity` y `EvidenceClass` ya están en `QueryDoctor.Domain.Analysis`; falta agregar la entidad `HealthScore` y su migración para `analysis.health_score`.

**Álvaro**
- **S1-11 (`IngestionService`) y trabajo `analyze`:** Puedes inyectar `IRuleEngine` (`QueryDoctor.Application.Engine.Rules`) y persistir `AnalysisRun` y `Finding` en `AppDbContext` (cifrando `FindingContractJson.SerializeEvidence(finding.Evidence)` con `IFieldEncryptor.Encrypt` en `Finding.EncryptedEvidenceJson`).

**Daniel**
- **S1-16 y S2-09 (listado y detalle de hallazgos):** El contrato JSON versionado del hallazgo está en `FindingContract` (`QueryDoctor.Application.Engine.Rules`), alineado con el Listing B.1 del SRS.

## Siguiente paso
Abrir y fusionar el PR de `feat/S1-13-catalogo-reglas` y continuar con **S1-14** (`REG-01`, `REG-02`, `REG-04`).
- **S1-13 (`IRule`, catálogo, `analysis`) y S1-14 (`REG-01/02/04`):** El árbol tipado ya está listo en `QueryDoctor.Domain.Plans` (`ParsedPlan`, `PlanStatement`, `PlanOperatorNode`, `PlanWarning`, `MissingIndexGroup`, `ImplicitConversionInfo`). Los identificadores SQL (`Database`, `Schema`, `Table`, `Index`, `Column`) ya vienen normalizados sin corchetes exteriores `[...]`.
- **S1-03 (`NetArchTest`):** Ya puedes añadir la regla de que `QueryDoctor.Application.Engine` y `QueryDoctor.Domain.Plans` no dependen de E/S ni de infraestructura.

**Álvaro**
- **S1-11 (`IngestionService`) y S2-04 (`LiteralMasker`):** Puedes inyectar `IPlanParser` (`QueryDoctor.Application.Engine.Parsing`). Si `Parse(...)` devuelve `IsSuccess = false`, `result.Status` es `PlanAnalysisStatus.NotAnalyzable` y `result.Finding` trae la señal `REG-11` con `Reason` y `Message` para poblar `Plan.NotAnalyzableCause` (`RF-ING-09`). Para `S2-04`, `PlanStatement` y `PlanOperatorNode` exponen `StatementText`, `PredicateScalarString`, `ConstantLiterals`, `Parameters` y `Warnings`.
- **Pendiente de S1-07 en `TelemetryStatementWhitelist.GetQueryStoreViewColumnsSql`:** Cambiar `sys.columns`/`sys.objects` → `sys.all_columns`/`sys.all_objects` en S1-09.

**Daniel**
- **S2-10 (visor jerárquico):** `PlanOperatorNode` expone `NodeId`, `PhysicalOp`, `LogicalOp`, `EstimateRows`, `EstimatedTotalSubtreeCost`, `TargetObject`, `Warnings` y `Children`.

## Siguiente paso
Abrir y fusionar el PR de `feat/S1-12-plan-parser` → `main`, y continuar con **S1-13** y **S1-14** (además de **S1-03**).
- Planes guardados como `Pending`; los irrecuperables, `NotAnalyzable` con causa (`not_analyzable_cause`). El analyze
  todavía no se encola al terminar la ingesta.
- `Run-Lab.ps1 -Step workload` falló desde PowerShell con `Msg 102` en la línea 1 (parece BOM en la tubería);
  `05-workload.sql` con el mismo `sqlcmd` desde bash funciona.

**Álvaro / S2-04-05**
- El texto SQL está cifrado pero **sin enmascarar** (I2): el contexto de IA debe pasar por `LiteralMasker`.

## Deuda y límites conocidos
- RF-ING-06: falta la ingesta programada (Sprint 2). RNF-FIA-01: falta la prueba de corte de red real (Sprint 2).
- `QUERY_STORE CLEAR` congela la marca de agua; el intervalo activo de una consulta que sale del Top-N queda con
  su último valor; los SELECT de la plataforma entran en el Query Store objetivo.
- De S1-09 siguen: D4 (Azure SQL y 2025 sin pruebas), SSRF B1, B3, B8, B9.

## Siguiente paso
Revisión por ejes en una sesión aparte, abrir el PR `feat/S1-11-ingesta` → `main` y pedir al tutor (S1-20) que
ratifique los recortes de RF-ING-06 (programada) y RNF-FIA-01 (prueba de corte de red).
