# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-15 · **Por:** Álvaro (con agente) · **Rama:** `feat/S2-13-retencion` (sale de `main` en `68f38f1`)

## Estado
- ✅ En `main`: S1-01 a S1-19 (salvo S1-20), S2-01 a S2-12.
- 🔄 **S2-13** (Álvaro, esta rama, sin commits; PR por abrir): retención (RNF-SEG-10). El trabajo recurrente
  `retention` del Worker purga a diario el texto SQL, los planes y la evidencia de sus hallazgos cuando vence
  `Retention:SqlTextAndPlanDays` (30 por defecto). El DBA puede borrarlos a pedido con
  `DELETE /queries/{id}/evidence` y `DELETE /connections/{id}/evidence`, y cada borrado queda en `/audit` como `DataDeleted`.
  La Api expone "evidencia expirada". Migración `RetencionEvidencia` (`purged_at`).
- El recorte del plan ("Solo borrado a pedido") **no** se aplicó: va contra SRS 5.3 punto 4 (D1).
- Decisiones D1–D10, mecánica y deuda: `docs/ARCHITECTURE_PLAN.md` §S2-13. Contrato: `docs/CONTRATO_FRONT_CONSULTAS.md`
  (sección S2-13).
- Auditoría (`/audit`, sesión aparte) hecha: sin bloqueantes. Corregidos M1 (un recálculo concurrente podía dejar
  evidencia con literales en un plan purgado) y B1 (un `TooLarge` salía expirado después de purgar), con pruebas.

## Cómo probarla
- `dotnet test tests/QueryDoctor.IntegrationTests --filter "FullyQualifiedName~RetentionTests"`.
- Unitarias: `EvidenceRetentionTests`, `ExplainQueryTests.ConLaEvidenciaExpirada…`,
  `RecalculateConnectionScoresTests.UnPlanPurgado…`, `GetQueryPlanTreeTests.PlanTooLarge…`.
- Suite completa: `dotnet test -m:1`.
- Rotura a propósito: si se quita el evento de auditoría de `EfEvidencePurgeStore.PurgeAsync`, las pruebas de
  `RetentionTests` que miran la auditoría quedan en rojo.
- Plazo en el compose: `QD_RETENTION_DAYS` (1–3650). Con 0, el Worker no arranca.

## Lo que necesita saber cada uno
**Daniel**
- `sqlExpired` y `planExpired` ahora salen de la marca de purga. Un plan `TooLarge` ya no aparece como expirado, y un
  texto que nunca llegó muestra `"[Sin texto SQL]"`. Hay campos nuevos: `sqlExpiredAt`, `plans[].planExpired`/`expiredAt`,
  `findings[].evidenceExpired`, `planExpiredAt` y la causa de explicación `EvidenceExpired`. **Falta en la web** marcar la
  tarjeta del hallazgo con `evidenceExpired` y mostrar la causa nueva. Si quieres el botón de borrar (solo DBA), usa
  los dos `DELETE …/evidence`.
- Toqué contratos y casos de uso de `Application/Queries` (S2-09/S2-10, tuyos): solo agregué campos y cambié de dónde
  sale "expirado". Las rutas nuevas están en un archivo aparte (`Api/Retention/EvidenceEndpoints.cs`).
**Joel**
- `RecalculateConnectionScores`: un plan purgado conserva sus hallazgos y no se vuelve a analizar.
  `EfScoreRecalculationStore.SaveBatchAsync` ahora bloquea las filas de los planes (`FOR UPDATE`) y no escribe corrida
  ni hallazgos en un plan que se purgó mientras corría el recálculo (auditoría M1).
**Todos**
- Nada borra en `audit.*`. La traza y la auditoría verifican después de la purga (`RetentionTests`).

## Deuda y límites conocidos
- Web sin `evidenceExpired` (Daniel). Un texto compartido por varias consultas se purga para todas. Sin índice para la
  búsqueda de lo vencido (medir en S3-07). Los conteos de la purga van solo al log.
- Sigue abierto: `/queries` es solo DBA aunque RNF-SEG-12 deja al Desarrollador ver lo asignado (S2-09, decisión pendiente).
- Heredado de S2-12: sin alta de Desarrolladores (D6) y sin RLS. DA-14, DA-06 y DA-13 siguen abiertas.

## Siguiente paso
Revisión por ejes de S2-13 en una sesión aparte (`/audit`) y PR `feat/S2-13-retencion` → `main`. Repetir
`dotnet list package --vulnerable`.
