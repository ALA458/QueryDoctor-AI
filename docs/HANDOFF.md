# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-11 · **Por:** Joel · **Rama:** `feat/S1-07-laboratorio`

## Estado de tareas en `main` + rama actual
- ✅ **S1-01** (Álvaro): Solución .NET 10, reglas de build, NetArchTest base y CI.
- ✅ **S1-02** (Maykol): Docker Compose con redes segmentadas, `sqlserver` 2022 y Docker secrets.
- ✅ **S1-04** (Daniel): Entidades y migración inicial EF Core de los esquemas `core` y `ops`.
- ✅ **S1-05** (Maykol): `SecretProtector` AES-256-GCM por campo.
- ✅ **S1-08** (Maykol): `QueryStoreGateway` + `TelemetryStatementWhitelist` + `statement_log`.
- ✅ **S1-07 / S0-10** (Joel, en esta rama): Laboratorio SQL Server 2022 (`lab/`), 7 escenarios sembrados (`ESC01`..`ESC06` + `ESC04B`), 16 fixtures `.sqlplan` en `tests/fixtures/plans/`, validación empírica de F1–F8 y H1–H5 en `lab/results/S1-07-verificacion.md` (3 corridas idénticas).

## Lo que necesita saber cada uno
**Joel**
- **S1-12 (`PlanParser`) y S1-14 (`REG-01/02/04`):** Ya tienes los fixtures reales en `tests/fixtures/plans/` (`ESC01.qs.1.sqlplan`..`ESC06.qs.1.sqlplan` y sus pares `.actual.sqlplan`).
- **Gotcha descubierto en S1-07 para `PlanParser`:** En compatibilidad 160 (PSP optimization), SQL Server genera planes *Dispatcher* (`ESC04.qs.1.sqlplan`) con `<Dispatcher><ParameterSensitivePredicate>` y **0 nodos `<RelOp>`**. `PlanParser` debe aceptarlos sin fallar.
- **S1-03:** Extender `tests/QueryDoctor.ArchitectureTests/LayerDependencyTests.cs` (motor sin E/S, Api sin Gateway ni cliente IA, SqlClient restringido al namespace del Gateway).

**Maykol y Álvaro**
- **Hallazgo en `TelemetryStatementWhitelist.GetQueryStoreViewColumnsSql` (S1-08 / S1-09):** Probadas las 6 sentencias contra SQL Server 2022 real con `qd_reader` (`VIEW DATABASE STATE`), 5 funcionan y **`GetQueryStoreViewColumns` devuelve 0 filas** porque las vistas `sys.query_store_*` no están en `sys.objects`/`sys.columns` sino en `sys.all_objects`/`sys.all_columns` (74 columnas). Corregir en S1-09 o PR puntual de `TelemetryStatementWhitelist`.
- **S1-09 / S1-11 (Álvaro):** El script `lab/sql/04-readonly-login.sql` ya crea `qd_reader` (solo `VIEW DATABASE STATE`) y `qd_noperm` (caso negativo) para probar `CapabilityProbe` e `IngestionService` contra `QD_Lab`.
- **S1-19 (Maykol):** Usar `lab/results/S1-07-verificacion.md` para pasar el Apéndice A.5 a "verificado" en SQL Server 2022 y anotar que `avg_query_max_used_memory` es el nombre real de la columna de memoria en `sys.query_store_runtime_stats`, y que `VIEW DATABASE PERFORMANCE STATE` basta en SQL Server 2022.

**Daniel**
- **S1-20 (reunión con el tutor):** Llevar la propuesta de cierre de **DA-07** respaldada por `lab/results/S1-07-verificacion.md`, además de ratificar DA-01/06/10 y actualizar el estado de ADR-05/06/11 en `ARQUITECTURA_v0.1.md`.
- Sincronizar `docs/QueryDoctor_Sprints (1).xlsx` con los estados actualizados en `docs/sprints/SPRINTS.md`.

## Siguiente paso
Ejecutar `/verify`, abrir PR de `feat/S1-07-laboratorio` → `main` y continuar con **S1-03** y **S1-12**.
