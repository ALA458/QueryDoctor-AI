# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-05 · **Por:** Álvaro · **Rama:** `feat/S1-01-solucion-ci`

## S1-01 (Álvaro) — en curso
- ✅ Parte 1: `QueryDoctor.slnx` con 5 proyectos (`src/`) + 2 de pruebas (`tests/`) y
  referencias Domain ← Application ← Infrastructure ← Api/Worker.
- ✅ Parte 2: `Directory.Build.props` (net10.0, nullable, **warnings = errores**, analizadores
  `latest-recommended`), `global.json`, `.editorconfig`, `.gitattributes`. Plantillas limpiadas;
  la Api solo expone `/health` y OpenAPI.
- ✅ Parte 3: NetArchTest con 2 reglas mínimas (Domain sin otras capas ni frameworks;
  SqlClient prohibido fuera de Infrastructure).
- ⬜ Parte 4: CI en GitHub Actions (build, format, pruebas, paquetes vulnerables, CodeQL).
- ⬜ Parte 5: protección de `main` (PR + 1 revisión + CI en verde).
- Nota: `Microsoft.OpenApi` fijado a 2.7.5 en la Api por GHSA-v5pm-xwqc-g5wc (venía 2.0.0
  transitivo). Quitar la referencia cuando `Microsoft.AspNetCore.OpenApi` traiga ≥ 2.7.5.

## Lo que necesita saber cada uno
**Joel**
- S1-03: ya existe `tests/QueryDoctor.ArchitectureTests/LayerDependencyTests.cs`. Extiéndelo,
  no empieces de cero. Pendiente: motor sin E/S, Api sin referencias al Gateway ni al cliente
  de IA, y afinar la regla de SqlClient al namespace del Gateway cuando exista (S1-08).
- Los analizadores tratan warnings como errores: el código del motor debe compilar limpio.

**Maykol**
- S1-08: el Gateway vive en `Infrastructure` (único lugar con `Microsoft.Data.SqlClient`).
  Acordar con Álvaro la interfaz `ITelemetrySource` en `Application` **antes** de implementarla:
  S1-09 la consume.
- S1-10: el Worker hoy es un host vacío (`Program.cs` sin servicios). Ahí se monta el servidor
  de Hangfire; en la Api, solo el cliente.
- S1-18: integrar en `ai-service/` el prototipo del adaptador LLM (S0-08) **con sus 13 pruebas**.

**Daniel**
- S1-04: las migraciones salen de las entidades de `core`. Ver "Por decidir" (quién las escribe).

**Todos**
- Instalar la extensión EditorConfig de VS Code.
- `dotnet format` antes de cada commit; la CI corre `--verify-no-changes`.

## Por decidir en la reunión del equipo
1. **Entidades del esquema `core`** (Connection, CapabilityProfile, Query, QueryText, Plan,
   RuntimeStat, IngestionRun): ninguna tarea las asigna y S1-04 depende de ellas.
   Propuesta: las escribe Álvaro (ingesta y persistencia), Daniel genera la migración encima.
2. **Warnings como errores** en local (propuesta actual) o solo en la CI.
3. **Commits con Conventional Commits** `tipo(S?-??): descripción` (propuesta).
4. Replanificar S1+S2 con la columna "Si no alcanza" (S1 cerró con 0/16; quedan ~10 días).
5. S1-20 con el tutor: ratificar DA-01/06/10, el recorte y ADR-05/06/11 (el documento de
   arquitectura aún los marca "Propuesta").

## Siguiente paso
Partes 4 y 5 de S1-01 → PR → merge. Hasta entonces nadie más debería abrir PR.