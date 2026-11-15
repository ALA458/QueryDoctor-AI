# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-15 · **Por:** Álvaro (con agente) · **Rama:** `feat/S2-06-traza-auditoria` (sale de `main` en `3f74f8f`)

## Estado
- ✅ En `main`: S1-01 a S1-19 (salvo S1-20), S2-01 a S2-05.
- 🔄 **S2-06** (Álvaro, esta rama, PR por abrir): `audit.trace_entry` y `audit.audit_event` de solo adición (trigger
  que rechaza UPDATE/DELETE/TRUNCATE), encadenadas por SHA-256 con inserción serializada (`pg_advisory_xact_lock`).
  Traza de cada explicación en la misma transacción que su estado final. Auditoría de login, conexiones e ingestas
  manuales. Endpoints `GET /trace`, `/audit` y `/…/verify`. `/verify` VERDE: 709 unitarias, 78 de integración y 19
  de arquitectura. **Falta la revisión por ejes en otra sesión.**
- Ajustes puntuales en la UI: `web/nginx/consulta.html` usa `<output>` para el aviso; los estilos de SQL usan
  `overflow-wrap: anywhere` en lugar de `word-break: break-word`. La validación npm no está configurada:
  `web/` no contiene `package.json`.
- Decisiones T1–T9 y deuda: `docs/ARCHITECTURE_PLAN.md` §S2-06.

## Cómo probarla
- `dotnet test tests/QueryDoctor.IntegrationTests --filter "FullyQualifiedName~Traceability|FullyQualifiedName~AuditEvents|FullyQualifiedName~ExplanationTrace"`.
  Las pruebas alteran filas a mano con `session_replication_role = replica` y las restauran.
- A mano: `GET /trace?queryId=…`, `GET /audit?type=LoginFailed` y `GET /trace/verify` con sesión de DBA.
- Suite completa: `dotnet test -m:1`.

## Lo que necesita saber cada uno
**Daniel**
- `AuthEndpoints.cs` (tuyo, S1-06) ganó una llamada de auditoría en cada salida del login, del logout y del cambio
  de contraseña. La lógica no cambió. Revísalo en el PR.
- Vista de trazabilidad (CU-06): `GET /trace?queryId=&explanationId=&from=&to=&take=` y `GET /audit?type=&from=&to=`.
  Ninguna acción para modificar o borrar (CA-HU14.2). Las fechas aceptan cualquier offset.
**Maykol**
- T4: queda pendiente un rol de PostgreSQL para la app sin UPDATE/DELETE sobre `audit.*` (toca el compose y el arranque).
- `statement_log` (RF-TRZ-04) podría reutilizar `ChainWriter` cuando deje de estar en memoria.
**Quien haga S2-13 (borrado de datos)**
- Emite `AuditEventType.DataDeleted` con `IAuditLog`. La traza no tiene FK: sobrevive a la purga, y así debe seguir.

## Deuda y límites conocidos
- DA-14 (rol Auditor) abierta: hoy solo el DBA consulta la traza. DA-06 y DA-13 siguen abiertas (S1-20).
- `ThresholdsChanged` sin emisor (umbrales desde la interfaz fuera del MVP); `DataDeleted` llega con S2-13.
- Si falla la auditoría de un registro de conexión o de una ingesta, la acción ya ocurrió y la respuesta es 500.
- El nombre escrito en un login fallido se guarda tal cual: si es una contraseña escrita por error, queda en la auditoría.
- De S2-05: medir CA-HU09.3 (≤ 2 s) con el stack levantado. De S1-11: `RecurringJob`, corte de red, `statement_log` en memoria.

## Siguiente paso
Revisión por ejes de S2-06 en una sesión aparte, abrir el PR `feat/S2-06-traza-auditoria` → `main` y seguir con la
siguiente tarea mía del plan: **S2-12** (RBAC del Desarrollador) o **S2-13** (retención).
