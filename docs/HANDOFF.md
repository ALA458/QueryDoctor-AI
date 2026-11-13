# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-13 · **Por:** Álvaro · **Rama:** `feat/S1-11-ingesta` (sale de `main`, no apilada)

## Estado
- ✅ En `main`: S1-01, S1-02, S1-04 a S1-10.
- 🔄 **S1-11** (Álvaro, esta rama, PR por abrir): ingesta incremental. `/verify` VERDE con corrida real del
  laboratorio (14/14). Cierra CA-HU03.1–03.3, CA-HU03.4 con fallo inyectado (corte de red real **recortado** al
  Sprint 2) y **CA-HU02.2** (venía de S1-09). Preauditoría APROBABLE; falta la revisión por ejes en otra sesión.
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
- POST repetidos encolan varias ingestas (se serializan por el bloqueo); un plan `TooLarge` se guarda igual cifrado.
- De S1-09 siguen: D4 (Azure SQL y 2025 sin pruebas), SSRF B1, B3, B8, B9.

## Siguiente paso
Revisión por ejes en una sesión aparte, abrir el PR `feat/S1-11-ingesta` → `main` y pedir al tutor (S1-20) que
ratifique los recortes de RF-ING-06 (programada) y RNF-FIA-01 (prueba de corte de red).
