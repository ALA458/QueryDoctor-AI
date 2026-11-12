# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-12 · **Por:** Joel · **Rama:** `feat/S1-14-reglas-debe`

## Estado de tareas en `main` + rama actual
- ✅ **S1-01** (Álvaro): Solución .NET 10, reglas de build, NetArchTest base y CI.
- ✅ **S1-02** (Maykol): Docker Compose con redes segmentadas, `sqlserver` 2022 y Docker secrets.
- ✅ **S1-04** (Daniel): Entidades y migración inicial EF Core de los esquemas `core` y `ops`.
- ✅ **S1-05** (Maykol): `SecretProtector` AES-256-GCM por campo.
- ✅ **S1-06** (Daniel): Identity mínimo, DBA inicial desde secreto, login, cambio obligatorio de contraseña y bloqueo por intentos.
- ✅ **S1-07 / S0-10** (Joel): Laboratorio SQL Server 2022 (`lab/`), 7 escenarios sembrados, 16 fixtures `.sqlplan` y verificación empírica F1–F8.
- ✅ **S1-08** (Maykol): `QueryStoreGateway` + `TelemetryStatementWhitelist` + `statement_log`.
- ✅ **S1-12** (Joel): `ShowplanXmlParser` (`IPlanParser`) con `XmlReader` seguro, árbol tipado en `QueryDoctor.Domain.Plans` y emisión de `REG-11` sin detener el lote.
- ✅ **S1-13** (Joel): Catálogo de reglas versionado (`IRule`, `IRuleCatalog`, `VersionedRuleCatalog` con `REG-01`..`REG-11`), umbrales configurables (`RuleThresholds`), motor determinístico (`IRuleEngine`, `DeterministicRuleEngine`), contrato JSON versionado del hallazgo (`FindingContract`), entidades de `QueryDoctor.Domain.Analysis` y migración EF Core `EsquemaAnalysis`.
- ✅ **S1-14** (Joel, en esta rama): Reglas Debe en `QueryDoctor.Application.Engine.Rules.Catalog` (`Reg01ImplicitConversionRule`, `Reg02MissingIndexRule`, `Reg04ExcessiveKeyLookupRule`), descubrimiento automático de `IRule` por reflexión en `VersionedRuleCatalog.DiscoverBuiltInRules()` (`RNF-MAN-01`) y suite de pruebas positivas y negativas (`MustRulesEvaluationTests`, `RNF-MAN-02`).

## Lo que necesita saber cada uno
**Joel**
- **S2-01 (`REG-03`):** Basta con crear `Reg03CostlyScanRule : IRule` en `QueryDoctor.Application.Engine.Rules.Catalog`; `VersionedRuleCatalog.DiscoverBuiltInRules()` y `AddInfrastructure` la registran automáticamente.
- **S1-03 (`NetArchTest`):** Verificar y completar las reglas de arquitectura en `QueryDoctor.ArchitectureTests`.
- **S2-02 (`HealthScoreCalculator`):** Las enumeraciones `FindingSeverity` y `EvidenceClass` ya están en `QueryDoctor.Domain.Analysis`; falta agregar la entidad `HealthScore` y su migración para `analysis.health_score`.

**Álvaro**
- **S1-11 (`IngestionService`) y trabajo `analyze`:** `IRuleEngine` ya evalúa `REG-01`, `REG-02`, `REG-04` y `REG-11` al inyectarlo desde `AddInfrastructure`. Recuerda pasar `QueryConsumptionPct` en `PlanAnalysisRequest` para que las reglas contextuales (`REG-04`, `REG-03`) puedan evaluar el umbral de ventana (`RF-MOT-05`, `RF-MOT-06`).

**Daniel**
- **S1-16 (listado de consultas con hallazgos):** El motor ya emite los contratos JSON de `REG-01`, `REG-02`, `REG-04` y `REG-11` (`FindingContract`).

## Siguiente paso
Abrir y fusionar el PR de `feat/S1-14-reglas-debe` y continuar con las tareas del Sprint 2 (`S1-03`, `S2-01`, `S2-02`).
