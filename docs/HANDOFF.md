# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-14 · **Por:** Álvaro (con agente) · **Rama:** `feat/S2-04-literal-masker` (sale de `main` en `8557ef9`)

## Estado
- ✅ En `main`: S1-01 a S1-18 (salvo S1-19 y S1-20), S1-03 (NetArchTest), S2-01 (REG-03). Detalle en `docs/sprints/SPRINTS.md`.
- 🔄 **S2-04** (Álvaro, esta rama, PR por abrir): `LiteralMasker` con ScriptDom 180.154.0 en
  `QueryDoctor.Application.Masking`. Enmascara literales y borra comentarios del SQL, los valores del plan XML
  (con lista blanca de atributos) y la evidencia de los hallazgos. Falla cerrado con causa tipada y no se puede desactivar.
  `/verify` VERDE: 573 unitarias, 14 de arquitectura y 36 de integración. Preauditoría hecha en la misma sesión,
  con H1 corregido (`7cf4b8b`). **Falta la revisión por ejes en otra sesión.**
- Decisiones D1–D9 y deuda: `docs/ARCHITECTURE_PLAN.md` §S2-04.

## Cómo probarla
- `dotnet test tests/QueryDoctor.UnitTests --filter "FullyQualifiedName~Masking"`. La aceptación de CA-HU21.1 es
  `SeededLiteralsAcceptanceTests`: un doble del cliente de IA captura el JSON exacto de `POST /explain`.
- Para la suite completa: `dotnet test -m:1`. En paralelo hay pruebas que interfieren entre proyectos (issue abierto).

## Lo que necesita saber cada uno
**Álvaro (S2-05)**
- Usa `IMasker.Mask(new MaskingInput(sql, plan, findings))` y luego `MaskedExplanationRequest.TryCreate(...)`.
  Implementa `IAiExplanationClient` en `Infrastructure.Ai` (solo en el Worker), pon la `description` real y agrega
  `kb_fragments` y métricas. Repite la suite sembrada contra el cuerpo HTTP real.
- Nunca registres `MaskingInput` ni el texto enmascarado. Lo único registrable es `MaskedContext.Metrics` con su causa.
**Joel (S2-03 y reglas nuevas)**
- Toda clave de evidencia nueva debe clasificarse en `EvidenceMasker.Policy`. Si no, la prueba guardiana
  `LiteralMaskerEvidenceTests` se pone en rojo y el hallazgo no viaja a la IA.
- Los números de la evidencia viajan intactos: son estimaciones del motor, no literales.
**Maykol (S2-07, ai-service)**
- Los comentarios llegan borrados, pero los identificadores entre corchetes no (DA-13), y pueden llevar instrucciones.
  Delimítalos como datos no confiables. Si el SQL no tokenizó, `sql_text` llega vacío (RA-03).
**Daniel (S2-11)**
- Ninguna pantalla puede ofrecer apagar el enmascarado (CA-HU21.4). `MaskingCannotBeDisabledTests` revisa la
  configuración, pero no `web/`.

## Deuda y límites conocidos
- DA-13 sigue **abierta** en el SRS: los nombres de objetos viajan sin seudonimizar. Hay que ratificarla en S1-20.
- Siguen pendientes de S2-04: H2 a H6 de la preauditoría (`ARCHITECTURE_PLAN.md` §S2-04). La lista blanca del plan solo
  incluye atributos vistos en SQL Server 2022.
- Siguen pendientes de S1-11: el `RecurringJob` de la ingesta (RF-ING-06), la prueba de corte de red (RNF-FIA-01),
  `statement_log` en memoria sin límite (B9) y `Run-Lab.ps1 -Step workload` con BOM desde PowerShell.
- `dotnet list package --vulnerable` de toda la solución agotó el tiempo contra nuget.org: hay que repetirlo en la CI.

## Siguiente paso
Revisión por ejes de S2-04 en una sesión aparte, abrir el PR `feat/S2-04-literal-masker` → `main` y seguir con
**S2-05** (`ExplanationContextBuilder` y trabajo `explain`), que ya no tiene bloqueos.
