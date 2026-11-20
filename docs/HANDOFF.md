# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-20 · **Por:** Álvaro (con agente) · **Rama:** `feat/S3-06-aceptacion` (PR #72, con `main` en `6124860` mergeado)

## Estado
- ✅ En `main`: Sprint 1 y 2 (salvo S1-20), S2-13 (#67) y S2-14 (#69).
- 🔄 **S3-06** (Álvaro, PR #72): aceptación de punta a punta (RNF-COM-01). `tests/QueryDoctor.AcceptanceTests`
  (27 pruebas): la Api real encola y un Worker armado con `AddWorkerServices` consume, contra un SQL Server real; la IA
  es un doble HTTP. Job `acceptance` en la CI con SQL Server 2022 y 2019: **27/27 en las dos** (corrida 44061839147).
- **SQL Server 2019 pasa a Probado** (`SupportMatrix`). Azure SQL DB sigue Compatible sin pruebas (sin acceso).
  Matriz declarada en `docs/COMPATIBILIDAD.md`; `SupportMatrixDocumentTests` exige que coincida con el código.
- Decisiones A1–A7, mecánica y deuda: `docs/ARCHITECTURE_PLAN.md` §S3-06.
- Auditoría (en la misma sesión, no independiente): M1, M2 y B4 corregidos en `b7ea843`, CI en verde. Abierto M3
  (SRS); B1 y B5 quedan como deuda. Detalle en el cuerpo del PR #72.

## Cómo probarla
- Local con el compose: refrescar la carga (`05` y `lab/sql/12`) y `QD_LAB_SQL=127.0.0.1 dotnet test -m:1`
  (en Windows, nunca `localhost`). Sin `QD_LAB_SQL`, LabTests y AcceptanceTests salen Skipped.
- Por criterio: `dotnet test tests/QueryDoctor.AcceptanceTests --filter Criterio=CA-HU02.2`.
- Otra versión: `lab/ci/prepare-acceptance-lab.sh <contenedor>` y `QD_LAB_PLATFORM=sql2019` (ver `lab/README.md` §4).
- Roturas: `GRANT VIEW DATABASE STATE TO qd_noperm` en `QD_Lab_Acc` deja CA-HU01.2 en rojo; cambiar un nivel en
  `docs/COMPATIBILIDAD.md` deja en rojo `SupportMatrixDocumentTests`.

## Lo que necesita saber cada uno
**Maykol**
- El Cuadro 4.16 del SRS debe pasar SQL Server 2019 a **Probado** (y la nota (d) y DA-12). Celdas exactas en
  `docs/COMPATIBILIDAD.md`, "Nota para Maykol". El host del Worker (S1-10) ahora llama a `AddWorkerServices`.
**Joel**
- REG-04 **no** se dispara en ESC02 con los umbrales por defecto (unos 20 lookups y 0,03 % de consumo). Dato para S3-01.
- La ingesta queda `Succeeded` antes de terminar el análisis y el score (S2-02 / S1-11): durante ese rato el listado
  sale sin hallazgos ni score, y una ingesta pedida entonces se omite como `AlreadyRunning`.
- Toqué `lab/sql/01` (compatibilidad 160 o 150 según la versión) y `11` (permiso de `qd_perf` solo desde 2022).
  En 2022 nada cambia; los fixtures de S1-07 son los mismos. Los escenarios nuevos van en `12` con la marca `QD-ACC`.
**Daniel**
- CA-HU11.1 (aviso "no ha sido aplicada…") se comprueba en la web (`tests/web`); por la Api solo `requires_human_review`.

## Deuda y límites conocidos
- CA-HU01.4 (eliminar conexión y credenciales) no está implementado: falta `DELETE /connections/{id}`.
- CA-HU03.4 sin prueba con corte de red real. CA-HU04.2 es de S3-07. HU-20: S2-14 en CI, S3-02 pendiente.
- Solo la edición Developer en Linux. Azure SQL DB sin aceptación. El SRS todavía dice 2019 "Compatible sin pruebas".
- Sigue de antes: sin RLS, DA-14, DA-06 y DA-13 abiertas; web sin `evidenceExpired`.
- Auditoría: B1 (prueba de IL que impida a la Api llamar `AddWorkerServices`) y B5 (notas A02/A03 en `OWASP_TOP10.md`).

## Siguiente paso
Revisión independiente del PR #72 (otra sesión o un compañero) y aprobación → `main`. Antes de fusionar: Maykol
actualiza el Cuadro 4.16, la nota (d) y DA-12 del SRS, o el revisor acepta por escrito la diferencia temporal (M3).
