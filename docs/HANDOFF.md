# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-12 · **Por:** Joel · **Rama:** `feat/S1-13-catalogo-reglas`

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

## Lo que necesita saber cada uno
**Joel**
- **S1-14 (`REG-01`, `REG-02`, `REG-04`):** Solo debes implementar `IRule` para cada regla en `QueryDoctor.Application.Engine.Rules` usando `VersionedRuleCatalog.BuiltInDescriptors` y registrarlas en `DependencyInjection.cs` (`services.AddSingleton<IRule, ...>()`).
- **S2-02 (`HealthScoreCalculator`):** Las enumeraciones `FindingSeverity` y `EvidenceClass` ya están en `QueryDoctor.Domain.Analysis`; falta agregar la entidad `HealthScore` y su migración para `analysis.health_score`.

**Álvaro**
- **S1-11 (`IngestionService`) y trabajo `analyze`:** Puedes inyectar `IRuleEngine` (`QueryDoctor.Application.Engine.Rules`) y persistir `AnalysisRun` y `Finding` en `AppDbContext` (cifrando `FindingContractJson.SerializeEvidence(finding.Evidence)` con `IFieldEncryptor.Encrypt` en `Finding.EncryptedEvidenceJson`).

**Daniel**
- **S1-16 y S2-09 (listado y detalle de hallazgos):** El contrato JSON versionado del hallazgo está en `FindingContract` (`QueryDoctor.Application.Engine.Rules`), alineado con el Listing B.1 del SRS.

## Siguiente paso
Abrir y fusionar el PR de `feat/S1-13-catalogo-reglas` y continuar con **S1-14** (`REG-01`, `REG-02`, `REG-04`).
