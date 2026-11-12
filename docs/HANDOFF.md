# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-11 · **Por:** agente · **Rama:** `chore/S1-04-seguimiento`

## S1-04 — cerrada (#26) + seguimiento en esta rama
- ✅ Esquemas `core` y `ops` con migración `Inicial` (en `main`).
- ✅ Migración `IndicesDeHashes`: IX no únicos `(connection_id, query_hash)` en `core.query` y
  `(connection_id, query_plan_hash)` en `core.plan` (arquitectura §7).
- ✅ `docs/arquitectura/MODELO_DATOS_v0.2.md` describe el esquema real; su §6 dice qué
  decisiones MD-01..MD-11 quedaron aplicadas. **Si cambias el esquema, actualízalo.**
- `AppDbContext` solo se registra si existe `ConnectionStrings:QueryDoctor`.
  `dotnet ef` usa `QD_DB_CONNECTION` (nunca contraseñas en el repo).

## Lo que S1-04 deja para cada tarea
**S1-06 (Identity, Daniel)**
- Tablas de Identity en `ops` (`users`, `roles`, `user_roles`…). `ops.users` trae
  `must_change_password` (primer ingreso, RNF-SEG-13) y `created_at`.
- Roles `DBA` y `Desarrollador` ya sembrados con Id fijo; usa `RoleNames`, no cadenas sueltas.
- `EmailIndex` **no** es único (MD-09 no aplicada): decidir `RequireUniqueEmail`.
- Falta registrar Identity (`AddIdentityCore` + stores de EF) en la Api.

**S1-09 (registro de conexión + CapabilityProbe, Álvaro)**
- `core.connection`: credenciales en `encrypted_credentials` vía `IFieldEncryptor`;
  `encryption` es `Mandatory|Strict` (RNF-SEG-03).
- UK `(server, database_name)` **distingue mayúsculas**: normalizar aquí (ver pendientes).
- `core.capability_profile` tiene UK `(connection_id)`: el sondeo **sobrescribe** el perfil.

**S1-11 (IngestionService, Álvaro)**
- Upsert idempotente sobre las UK: `(connection_id, query_store_text_id|query_id|plan_id)` y
  `(connection_id, plan_id, execution_type, interval_id)` en `runtime_stat` (RF-ING-05).
- Marca de agua en `connection.last_consolidated_interval_id`; `ingestion_run` no la copia.
- No hay índice que impida dos ingestas `Running` a la vez (MD-11): el bloqueo consultivo
  por conexión (§6.2) es la única barrera.
- `query.query_text_id` es nulo y la FK es `SET NULL` (la purga borra el texto, no la consulta).

## Pendientes
1. **Prueba de integración contra PostgreSQL** que demuestre que las UK rechazan duplicados
   (hoy solo hay pruebas unitarias y de arquitectura).
2. **Mayúsculas en `connection(server, database_name)`**: `SRV01/Ventas` y `srv01/ventas`
   pasan la UK. Se resuelve en **S1-09** (normalizar al registrar o índice sobre `lower(...)`).

## Por decidir (heredado; no verificado en esta sesión si ya se cerró)
- Warnings como errores en local o solo en la CI.
- Replanificar S1+S2 con la columna "Si no alcanza".
- S1-20 con el tutor: ratificar DA-01/06/10, el recorte y ADR-05/06/11.

## Siguiente paso
PR de `chore/S1-04-seguimiento` → revisión → merge. Luego S1-06 y S1-09 pueden arrancar.
