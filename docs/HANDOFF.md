# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-15 · **Por:** Álvaro (con agente) · **Rama:** `feat/S2-05-explanation-context` (sale de `main` en `f731ff3`)

## Estado
- ✅ En `main`: S1-01 a S1-19 (salvo S1-20), S2-01 (REG-03), S2-02 (HealthScore), S2-03 (validador), S2-04 (LiteralMasker).
- 🔄 **S2-05** (Álvaro, esta rama, PR por abrir): contexto enmascarado con hash SHA-256, caché de explicaciones TRAZADA,
  trabajo `explain` en el Worker con degradación si la IA falla, tablas `ai.explanation`, `ai.claim` y
  `ai.claim_reference`, y endpoints `POST /queries/{id}/explanations` y `GET …/latest`. `/verify` VERDE: 690
  unitarias, 43 de integración y 18 de arquitectura. Preauditoría en la misma sesión, con P1 corregido (`671d804`).
  **Falta la revisión por ejes en otra sesión** y medir CA-HU09.3 (≤ 2 s) con el stack levantado.
- Decisiones E1–E10, deuda y diferencias con el documento de arquitectura: `docs/ARCHITECTURE_PLAN.md` §S2-05.

## Cómo probarla
- `dotnet test tests/QueryDoctor.UnitTests --filter "FullyQualifiedName~UnitTests.Ai."` y
  `dotnet test tests/QueryDoctor.IntegrationTests --filter "FullyQualifiedName~ExplanationEndpointsTests"`.
- Stack: `LLM_PROFILE=mock docker compose up -d`, ingerir una conexión del laboratorio y esperar el análisis.
  Luego `POST /queries/{id}/explanations` y `GET …/latest`. Repetirlo y comprobar `from_cache: true`.
- Suite completa: `dotnet test -m:1`. En paralelo hay pruebas que interfieren entre proyectos (issue abierto).

## Lo que necesita saber cada uno
**Daniel (S2-09, S2-11)**
- Contrato: `GET /queries/{id}/explanations/latest` devuelve `status`, `cause`, `validation_status`, `notice`,
  `summary`, `claims[]` (con `references[].finding_code` y `node_ids` para los enlaces de RF-DASH-05) y `findings[]`.
  Pinta todo como texto (RNF-SEG-07). `notice` ya trae el aviso de RF-IA-10 o el de indisponibilidad.
- `POST` responde 202 (PENDIENTE), 200 `SIN_HALLAZGOS` con aviso, 409 si ya hay una en curso y 404.
  Mientras esté `PENDIENTE` o `EN_CURSO`, consulta `latest` cada 1–2 s.
**Maykol (ai-service, S2-07)**
- El Worker llama a `GET /info` (usa `prompt_version` y `llm_profile`) y a `POST /explain` con `kb_fragments`
  (ids Guid) y `metrics`. No cambies esos campos sin avisar: el hash de la caché depende de ellos.
- El Worker no reintenta: los 2 reintentos y los 60 s de RNF-FIA-02 quedan en el servicio.
- Decidir con el equipo si §5.3 (retriever con pgvector) se corrige en el documento: hoy los fragmentos los elige .NET.
**Joel**
- Un hallazgo nuevo viaja a la IA solo si sus claves de evidencia están en `EvidenceMasker.Policy` (S2-04).

## Deuda y límites conocidos
- S2-06 (mío): escribir la entrada de traza de RF-TRZ-01 a partir de `ai.explanation`, que ya tiene los datos.
- DA-06 con el tutor: RNF-RND-03 (≤ 30 s) frente a RNF-FIA-02 (60 s × 3). DA-13 sigue abierta (S1-20).
- Corregir en el documento de arquitectura: §5.3, §5.1, §8.2 y §11 (`ARCHITECTURE_PLAN.md` §S2-05).
- Siguen de S2-04 los hallazgos H2, H3 y H5. Siguen de S1-11: `RecurringJob` de la ingesta, corte de red y `statement_log` en memoria.
- `dotnet list package --vulnerable` de toda la solución agota el tiempo contra nuget.org: repetirlo en la CI.

## Siguiente paso
Revisión por ejes de S2-05 en una sesión aparte, abrir el PR `feat/S2-05-explanation-context` → `main` y seguir con
**S2-06** (traza y auditoría de solo adición).
