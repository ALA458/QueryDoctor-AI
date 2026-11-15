# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-15 · **Por:** Álvaro (con agente) · **Rama:** `feat/S2-12-rbac-desarrollador` (sale de `main` en `8346a02`)

## Estado
- ✅ En `main`: S1-01 a S1-19 (salvo S1-20) y S2-01 a S2-08.
- 🔄 **S2-12** (Álvaro, esta rama, PR por abrir): rol Desarrollador con filtro por conexión asignada en la capa de
  datos (filtro global de EF con `IAccessScope`, denegación por defecto). Lo no asignado da 404 también por id directo;
  escribir es solo DBA (403). El DBA asigna y quita con `/connections/{id}/assignments`, y cada cambio queda auditado.
  El Worker declara acceso Sistema. Migración `AuditoriaUsuarioRelacionado`.
- Decisiones D1–D7, mecánica y deuda: `docs/ARCHITECTURE_PLAN.md` §S2-12. Contrato: `docs/CONTRATO_API_CONEXIONES.md`.

## Cómo probarla
- `dotnet test tests/QueryDoctor.IntegrationTests --filter "FullyQualifiedName~DeveloperAccess|FullyQualifiedName~Assignment|FullyQualifiedName~ConnectionAccessFilter|FullyQualifiedName~WorkerSystemAccess"`.
- Unitarias: `ConnectionAccessFilterCoverageTests`, `AccessScopeMiddlewareTests` (None sin la política delante) y
  `AccessScopeArchitectureTests`.
- Suite completa: `dotnet test -m:1`.
- Rotura a propósito: quitar el filtro de `Finding` o hacer que `None` vea todo pone pruebas en rojo (ver el PR).
  También: confirmar la asignación antes de auditar (`AssignmentAuditAtomicityTests`) o ignorar el cambio de
  contraseña pendiente en el middleware (`AccessScopeMiddlewareTests`).
- Auditoría (`/audit`, sesión aparte) hecha: sin bloqueantes. Corregidos la transacción única asignación + auditoría
  (RF-TRZ-05), las pruebas del middleware (D7) y la documentación. `ARQUITECTURA_v0.1.md` sigue diciendo
  `ConnectionMember` (reportado, sin tocar).

## Lo que necesita saber cada uno
**Daniel**
- El Desarrollador ya puede usar `GET /connections`, `/{id}`, `/{id}/ingestions` y las explicaciones, pero solo ve lo
  asignado. Lo demás da **404** con el mismo `detail` que "no existe". Registrar e ingerir le dan **403**: oculta
  esos botones si `/auth/me` no trae el rol DBA.
- Pantalla de asignaciones (DBA): `GET /users?role=Desarrollador`, `GET/PUT/DELETE /connections/{id}/assignments[/{userId}]`.
  `GET /audit` trae el campo nuevo `related_user_id`.
- Toqué `AuthConstants.cs` y `AuthServiceCollectionExtensions.cs` (tuyos, S1-06): solo agregué la política
  `ConnectionReader`. `DbaOnly` y la `FallbackPolicy` no cambiaron.
- **No hay alta de usuarios Desarrollador** (D6): toca Identity. Hay que acordar quién la hace antes de la demo.
**Maykol**
- RLS en PostgreSQL como segunda barrera depende del rol de aplicación de T4 (S2-06).
**Todos**
- Toda entidad nueva con `ConnectionId` necesita su `HasQueryFilter` en `AppDbContext`; si falta, la prueba de
  cobertura queda en rojo. `IgnoreQueryFilters` está prohibido en `src/`.
- Un host nuevo que use `AppDbContext` debe declarar su `IAccessScope`; sin eso no arranca.

## Deuda y límites conocidos
- Sin alta de Desarrolladores (D6). Sin RLS (el SQL directo no pasa por el filtro; hoy solo lo usan el Worker y las
  asignaciones del DBA). Una consulta extra por solicitud del Desarrollador (medir en S3-07).
- DA-14 (rol Auditor) abierta: `/trace` y `/audit` siguen solo para DBA. DA-06 y DA-13 siguen abiertas (S1-20).
- Heredado: rol de base sin UPDATE/DELETE en `audit.*` (T4); CA-HU09.3 sin medir; `RecurringJob` de S1-11.

## Siguiente paso
Revisión por ejes de S2-12 en una sesión aparte (`/audit`) y abrir el PR `feat/S2-12-rbac-desarrollador` → `main`.
Si `main` recibió otra migración, regenerar `AuditoriaUsuarioRelacionado` encima (al 2026-11-15 no la recibió).
Repetir `dotnet list package --vulnerable` (en la auditoría NuGet no respondió). Después, **S2-13** (retención).
