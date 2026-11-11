# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-11 · **Por:** Joel · **Rama:** `feat/S1-12-plan-parser`

## Estado de tareas en `main` + rama actual
- ✅ **S1-01** (Álvaro): Solución .NET 10, reglas de build, NetArchTest base y CI.
- ✅ **S1-02** (Maykol): Docker Compose con redes segmentadas, `sqlserver` 2022 y Docker secrets.
- ✅ **S1-04** (Daniel): Entidades y migración inicial EF Core de los esquemas `core` y `ops`.
- ✅ **S1-05** (Maykol): `SecretProtector` AES-256-GCM por campo.
- ✅ **S1-06** (Daniel): Identity mínimo, DBA inicial desde secreto, login, cambio obligatorio de contraseña y bloqueo por intentos.
- ✅ **S1-07 / S0-10** (Joel): Laboratorio SQL Server 2022 (`lab/`), 7 escenarios sembrados, 16 fixtures `.sqlplan` y verificación empírica F1–F8.
- ✅ **S1-08** (Maykol): `QueryStoreGateway` + `TelemetryStatementWhitelist` + `statement_log`.
- ✅ **S1-12** (Joel, en esta rama): `ShowplanXmlParser` (`IPlanParser`) en memoria sin E/S con `XmlReader` seguro (sin DTD, con límites de caracteres y profundidad), modelo tipado inmutable en `QueryDoctor.Domain.Plans`, soporte para planes *Dispatcher* de PSP, y emisión de `REG-11` ante XML nulo/truncado/inválido sin detener el lote.

## Lo que necesita saber cada uno
**Joel**
- **S1-13 (`IRule`, catálogo, `analysis`) y S1-14 (`REG-01/02/04`):** El árbol tipado ya está listo en `QueryDoctor.Domain.Plans` (`ParsedPlan`, `PlanStatement`, `PlanOperatorNode`, `PlanWarning`, `MissingIndexGroup`, `ImplicitConversionInfo`). Los identificadores SQL (`Database`, `Schema`, `Table`, `Index`, `Column`) ya vienen normalizados sin corchetes exteriores `[...]`.
- **S1-03 (`NetArchTest`):** Ya puedes añadir la regla de que `QueryDoctor.Application.Engine` y `QueryDoctor.Domain.Plans` no dependen de E/S ni de infraestructura.

**Álvaro**
- **S1-11 (`IngestionService`) y S2-04 (`LiteralMasker`):** Puedes inyectar `IPlanParser` (`QueryDoctor.Application.Engine.Parsing`). Si `Parse(...)` devuelve `IsSuccess = false`, `result.Status` es `PlanAnalysisStatus.NotAnalyzable` y `result.Finding` trae la señal `REG-11` con `Reason` y `Message` para poblar `Plan.NotAnalyzableCause` (`RF-ING-09`). Para `S2-04`, `PlanStatement` y `PlanOperatorNode` exponen `StatementText`, `PredicateScalarString`, `ConstantLiterals`, `Parameters` y `Warnings`.
- **Pendiente de S1-07 en `TelemetryStatementWhitelist.GetQueryStoreViewColumnsSql`:** Cambiar `sys.columns`/`sys.objects` → `sys.all_columns`/`sys.all_objects` en S1-09.

**Daniel**
- **S2-10 (visor jerárquico):** `PlanOperatorNode` expone `NodeId`, `PhysicalOp`, `LogicalOp`, `EstimateRows`, `EstimatedTotalSubtreeCost`, `TargetObject`, `Warnings` y `Children`.

## Siguiente paso
Abrir y fusionar el PR de `feat/S1-12-plan-parser` → `main`, y continuar con **S1-13** y **S1-14** (además de **S1-03**).
