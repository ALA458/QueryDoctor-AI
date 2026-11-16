# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-15 · **Rama:** `chore/pre-sprint3-bloqueantes-y-docs` (sale de `main` en `543af12`)

## Estado
- ✅ **Sprint 1 y Sprint 2 completos en `main`** (`#1`–`#64`): ingesta, parser, catálogo `REG-01`–`04`/`REG-11`, *Health Score*, `LiteralMasker` (`TSql170Parser`), servicio IA (`openrouter_*`, `gemini_free`), `ExplanationValidator`, traza/auditoría SHA-256, RBAC `Desarrollador` y vistas web base.
- ✅ Corregidos avisos CSS: valores SVG `rx`/`r` con unidad, `border-top-width` redundante eliminado y `word-break: break-word` sustituido por `overflow-wrap: anywhere`.
- ✅ Consolidados los selectores duplicados `.panel`, `.panel-head h2`, `.score-dial`, `.sql-code`, `.charts-grid`, `.chart-card`, `.chart-dot` y `.finding-list` en `web/assets/css/consulta.css`, preservando sus estilos efectivos.
- ✅ La transición y el desplazamiento de la flecha de los botones respetan `prefers-reduced-motion`.
- ✅ El desplazamiento de nodos del plan respeta `prefers-reduced-motion`.
- ✅ `[hidden]` prevalece sin `!important`; los selectores de `display` no se aplican a `.btn`, `.drawer` o `.chip-bar` ocultos.
- ✅ El aviso de consulta usa `<output>` con `aria-live="polite"` para anunciar mensajes accesiblemente.
- ✅ Los landmarks `aside` de las páginas identifican explícitamente la barra lateral de navegación.
- ✅ El drawer de conexión usa el elemento `<dialog>` y su comportamiento modal nativo.
- ✅ Eliminadas etiquetas vacías no asociadas a controles en los filtros de trazabilidad y auditoría.
- ✅ **Bloqueantes pre-Sprint 3 cerrados en esta rama:**
  - Fix `CA1825` en `ConnectionAccessFilterTests.cs` (`QueryDoctor.slnx` compila limpio con `0` warnings/errors) y ampliado `.github/workflows/ci.yml` (`web` y `ai-service`).
  - **Ingesta programada (`RF-ING-06`):** `IRecurringIngestionScheduler` / `HangfireRecurringIngestionScheduler` programa `RunScheduledAsync` tras cada sondeo `Active`.
  - **Retención (`S2-13`, `RNF-SEG-10`):** `RetentionOptions`, `RetentionPurgeJob` (Hangfire `retention`) y `DELETE /connections/{id}/data` (DBA); limpia `EncryptedSqlText`/`EncryptedPlanXml` manteniendo `audit.*` verificable y `SqlExpired`/`PlanExpired`.
  - **RBAC y Auth (`S2-12`, `A07`):** `QueryEndpoints` usa `ConnectionReaderPolicy` (Desarrollador lee `/queries` y `/plans` asignados), alta `POST /users` (DBA crea Desarrollador con `MustChangePassword = true` y auditoría) y *rate limit* en `POST /auth/login`.
  - **Documentación sincronizada + documento final:** actualizados `SPRINTS.md`, `OWASP_TOP10.md`, `ARQUITECTURA_v0.1.md`, `docs/Query Doctor AI.md` y generado `docs/Query_Doctor_AI_Final.tex`.
  - **Verificación en verde:** `UnitTests` (`778/778`), `IntegrationTests` (`114/114`), `ArchitectureTests` (`21/21`), `ai-service` `pytest` (`68/68`), `tests/web/explanation_view.test.mjs` (`3/3`), `dotnet format` y `docker compose config`.

## Deuda conocida (fuera de este PR)
- **UI/UX:** rediseño visual + Wizard de primera conexión + controles en `web/` para ingesta manual, asignaciones, alta de usuarios y vistas `/trace` y `/audit`.
- **Seguridad/Infra:** rol PostgreSQL restringido sin `UPDATE`/`DELETE` en `audit.*` (`T4`), RLS, mitigación SSRF en `targets`, TLS `:443` en Nginx y `dotnet list package --vulnerable` con salida a red.
- **Gestión (`S1-20`):** reunión/acta con tutor para ratificar `DA-01`, `DA-06`, `DA-10`, `DA-13`, `DA-14`.

## Siguiente paso
1. Abrir y fusionar este PR (`chore/pre-sprint3-bloqueantes-y-docs` → `main`).
2. Ejecutar rediseño UI/UX + Wizard de primera conexión en rama dedicada antes de medir usabilidad (`RNF-INT-01`).
3. Iniciar **Sprint 3** (`S2-14` arnés motor + TPC-H, `S3-01` calibración, `S3-02`–`S3-07` evaluación e informe final).
