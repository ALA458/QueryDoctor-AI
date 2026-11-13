# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-13 · **Por:** Álvaro · **Rama:** `feat/S1-09-conexiones`

## Estado
- ✅ En `main`: S1-01, S1-02, S1-04, S1-05, S1-06, S1-07, S1-08 y S1-10.
- 🔄 **S1-09** (Álvaro, esta rama, PR por abrir): alta de conexiones y CapabilityProbe. `/verify` VERDE con una
  corrida real del laboratorio (11 pruebas) y un alta de punta a punta en compose. **Cierra 5 de 6 criterios**:
  CA-HU01.1–01.3, CA-HU02.1 y CA-HU02.3. **CA-HU02.2 pasa a S1-11** (D5; SPRINTS.md ya actualizado).
- Auditoría por ejes (otra sesión): APROBABLE; M1 y B2/B4/B5/B6 corregidos en `f7a2ca0`, el resto queda como deuda.
- Detalle por persona, changelog y guía de punta a punta: `docs/NOTAS_S1-09_EQUIPO.md`.

## Antes de levantar el compose (todos)
- Crea `secrets/master_encryption_key.txt` (comando en las notas, P9) y **no la regeneres** si ya cifraste datos.
- `secrets/initial_dba_password.txt` debe ser un **archivo** con 12+ caracteres (mayúscula, minúscula, número,
  símbolo). Si Docker lo creó como directorio, bórralo y créalo.
- Laboratorio: `docker compose -f docker-compose.yml -f docker-compose.lab.yml up -d --build`. En Windows usa
  `127.0.0.1`, no `localhost`, para llegar al SQL Server desde el host.

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
- `lab/sql/11-probe-fixtures.sql` se corre después de `-Step build` y `-Step login`.
  `lab/sql/10-whitelist-validation.sql` quedó desactualizado frente a P0.

**Álvaro (S1-11)**
- Usar `QueryStoreState.CanIngest` para cerrar CA-HU02.2 con una prueba de laboratorio sobre `QD_Lab_NoQS`.

## Deuda y límites conocidos
- D4: Azure SQL Database y SQL Server 2025 figuran `Tested` según la matriz, sin pruebas ejecutadas contra ellos.
- Unicidad de conexión sin resolver DNS: un nombre y su IP cuentan como dos conexiones.
- RF-CFG-05 (advertencia de permisos de escritura) pasa al Sprint 2: `hasExcessivePermissions` es siempre `null`.
- OWASP A01 (SSRF, B1): el sondeo revela qué puertos están abiertos, también en `backend` (`db`, `api`, `ai`), y
  `targets` no es `internal`. Falta una lista de hosts permitidos.
- B3: 4060 no distingue "no existe" de "sin acceso". B8/B9: alta sin `audit_event` y `statement_log` sin límite (S2-06).

## Siguiente paso
Abrir el PR `feat/S1-09-conexiones` → `main` (revisión humana y CodeQL) y seguir con S1-11.
