# CLAUDE.md — Query Doctor AI

Plataforma de diagnóstico de consultas SQL Server / Azure SQL vía Query Store:
motor determinístico de reglas sobre Showplan XML → Query Health Score → explicación
con IA **trazada** a los hallazgos del motor. Proyecto de Ingeniería I, UTB 2026-2.

**Fuentes de verdad (cada una manda en su terreno):**
| Pregunta | Manda |
|---|---|
| Qué debe hacer el sistema / qué está prohibido | SRS v1.0 + Adenda v1.1 (`docs/SRS_Query_Doctor_AI (2).pdf`) — **siempre gana** |
| Quién, cuándo, qué se recorta | Plan de sprints (`docs/sprints/SPRINTS.md`, exportado del .xlsx) |
| Cómo se construye | `docs/arquitectura/ARQUITECTURA_v0.1.md` |

Conflictos: arquitectura vs. SRS → gana el SRS y se reporta como error de arquitectura.
Un recorte del plan solo vale si sigue SRS 5.3 y está aprobado por el tutor; nunca elimina
un Debe ni toca SRS 5.3 punto 4. Los `.md` de `docs/` son derivados: si contradicen a las
fuentes, se corrigen ellos. La descripción del Project en claude.ai (diagrama preliminar
con lakehouse y forzar plan) está desactualizada: ignórala.

## Leer antes de trabajar
1. `docs/HANDOFF.md` — estado actual y siguiente paso (léelo SIEMPRE primero).
2. `docs/WORKFLOW.md` — cómo se hace cada cambio y la Definition of Done.
3. `docs/ARCHITECTURE_PLAN.md` — resumen operativo: contenedores, redes, componentes,
   esquemas, ADR y DA. Para el detalle (flujos, tablas de seguridad) ve a
   `docs/arquitectura/ARQUITECTURA_v0.1.md` en la sección que necesites, no entero.
4. `docs/OWASP_TOP10.md` — si el cambio toca auth, datos, IA, red o dependencias.

## Invariantes (no negociables — SRS 5.3 punto 4 y principios P1–P8 de la arquitectura)
Si una tarea parece requerir romper alguno: **detente y pregunta**, no busques atajos.
(Abajo, "los invariantes" = estos 10.)
1. **Solo lectura** contra la BD monitoreada: solo `SELECT` constantes de la lista blanca
   (RNF-SEG-01), sin SQL dinámico. Solo `QueryStoreGateway` usa `SqlClient`, y corre en el
   **Worker**. Nunca DDL/DML/config; la plataforma no crea cuentas ni permisos (RNF-SEG-02).
2. **Separación de procesos:** la `api` nunca abre conexiones a bases objetivo ni llama al
   LLM; todo pasa por Hangfire al Worker. Solo `ai` sale a Internet. Los argumentos de
   trabajos Hangfire son **solo identificadores**, nunca SQL, planes ni secretos.
3. **TLS 1.2+ con validación de certificado** hacia SQL Server y el LLM (RNF-SEG-03):
   `Encrypt=Strict|Mandatory`, `TrustServerCertificate=false`. Excepción solo en el
   contenedor de laboratorio local, explícita y fuera de la ruta de producción.
4. **Secretos** cifrados (SecretProtector, AES-256-GCM por sobre) y como Docker secrets;
   jamás en el repo, en config versionada ni en logs (RNF-SEG-04, RNF-SEG-09).
5. **Enmascarado de literales** con ScriptDom (no regex) en el núcleo .NET, antes de que el
   contexto salga al servicio de IA; sin opción para desactivarlo (RNF-SEG-05). Si ScriptDom
   falla, se envían solo los hallazgos, sin texto SQL (RA-03).
6. **La IA solo explica evidencia que el motor ya produjo.** Explicaciones = lista de
   afirmaciones tipadas; el `ExplanationValidator` corre en .NET, **no** en el servicio de IA;
   estado TRAZADA o NO_VERIFICADA. Prohibido "verificada"/"validada" (RF-IA-02/03/10).
   La SPA renderiza la salida del LLM como texto, nunca HTML (RNF-SEG-07).
7. **Motor determinista y sin E/S:** mismo plan + misma versión de reglas + mismos umbrales =
   mismos hallazgos (RF-MOT-02). Un hallazgo es una **señal**, no un diagnóstico (RF-MOT-03).
8. **AuthN obligatoria y RBAC con denegación por defecto**; el filtro por conexión asignada
   va en la consulta, no en la UI (RNF-SEG-11/12). SQL, planes y explicaciones **cifrados en
   reposo** (RNF-SEG-14). `audit.*` es **solo adición** (sin UPDATE/DELETE, encadenado por hash).
9. **Perfiles de LLM gratuitos solo con datos de laboratorio:** con `APP_ENV=prod` el
   servicio de IA no arranca con un perfil que comparte datos (ADR-10). No debilitar el candado.
10. Ningún texto (README, UI, informe) afirma eficacia/precisión sin remitir al informe
   del laboratorio (RF-EVA-04).

## Fuera del MVP (no implementar aunque aparezca en diagramas viejos)
Forzar plan (ADR-07), cualquier escritura en producción, lakehouse (ADR-08), agente
recolector instalado en el servidor del cliente, broker de mensajes, microservicios por
módulo, OIDC (ADR-09), seudonimización de nombres de objetos (DA-13, posterior),
plan real (Podría), exportar PDF, umbrales desde la UI.

## Stack
- `src/` — .NET 10: `Domain`, `Application`, `Infrastructure`, `Api` (ASP.NET Core),
  `Worker` (servidor Hangfire). EF Core + PostgreSQL (pgvector). ASP.NET Core Identity.
- `ai-service/` — Python, FastAPI, adaptador de LLM por perfiles (ADR-10).
- `web/` — React + TypeScript.
- `docker-compose.yml` — web (Nginx), api, worker, ai, db (pgvector) + SQL Server 2022
  Developer de laboratorio; redes frontend / backend / targets / egress-ai.
- Logs: Serilog JSON con `correlation_id` por trabajo, propagado al servicio de IA.
- Plataformas de aceptación: SQL Server 2019, 2022 y Azure SQL Database (2016 sin pruebas).

## Comandos
```bash
dotnet build -warnaserror && dotnet test                     # backend + NetArchTest
dotnet format --verify-no-changes
cd ai-service && ruff check . && ruff format --check . && pyright && pytest
cd web && npm run lint && npx tsc --noEmit && npm test
docker compose up -d                                         # entorno completo
powershell -ExecutionPolicy Bypass -File lab/Run-Lab.ps1     # laboratorio SQL Server 2022 (S1-07)
```

## Reglas de trabajo para el agente
- Una tarea del plan de sprints (S1-xx) por rama y por PR. No toques módulos de otro
  responsable sin que la tarea lo diga (dueños en `docs/ARCHITECTURE_PLAN.md`).
- Lee el código real antes de editar; no asumas desde este archivo.
- Cita IDs de requisito (RF-/RNF-/CA-HU) en commits y PR; no inventes IDs.
- Decisiones abiertas (DA-xx) no las cierras tú: proponlas y espera.
- Nunca dejes pruebas en rojo ni las desactives para pasar la CI.
- Al terminar: corre `/verify`, actualiza `docs/HANDOFF.md`.
