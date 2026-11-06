# WORKFLOW.md — Pipeline de desarrollo de Query Doctor AI

> Describe **cómo** se hace cada cambio, no qué se cambia. Lo mismo para humanos y
> para el agente. Unidad de trabajo = una tarea del plan de sprints (S1-xx, S2-xx…).

```
 GUARDRAILES: invariantes de CLAUDE.md · verificar en el código, no en el tracker ·
              cuestionar y proponer · nunca dejar pruebas en rojo
 ┌────────┐  ┌────────┐  ┌──────────┐  ┌────────────────────┐  ┌──────────┐  ┌────────┐
 │1. PLAN │─▶│2. BUILD│─▶│3. VERIFY │─▶│4. AUDIT            │─▶│5. PR     │─▶│6. MERGE│
 │ tarea  │  │+ tests │  │ local    │  │ SAST → triage →    │  │ qué/por  │  │ CI +   │
 │ + CA   │  │+ doc   │  │ (gates)  │  │ revisión 6 ejes    │  │ qué/CA   │  │ humano │
 └────────┘  └───▲────┘  └────┬─────┘  └─────────┬──────────┘  └──────────┘  └────────┘
                 └────────────┴──── falla / hallazgo ┘
```

## 1. PLAN
- Tomar **una** tarea con su criterio "Hecho cuando" y los CA-HUxx que cita.
- Leer el código involucrado antes de escribir.
- Si hay trade-off de diseño o toca un DA-xx abierto: plantearlo y recomendar antes de codear.

## 2. BUILD
- Primero la prueba que expresa el criterio de aceptación; después el código.
- Reglas del motor: cada regla es una unidad `IRule` independiente (RNF-MAN-01), con
  un plan positivo y uno negativo en la suite (RNF-MAN-02).
- Dependencias hacia adentro: `Domain` no conoce EF Core ni SqlClient (lo vigila NetArchTest).
- Nada de `catch {}` silencioso: degradar con aviso explícito (RNF-COM-04, RNF-FIA-02).
- Comentarios solo para el **porqué** (invariante, gotcha, requisito). XML-doc en API pública.

## 3. VERIFY — compuerta objetiva local (`/verify`)
- `dotnet build -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes`
- `ai-service`: ruff + pyright + pytest · `web`: lint + `tsc --noEmit` + tests
- Recorrer cada CA de la tarea y decir cómo se demostró (prueba o pasos manuales).
- Build y tipos no prueban el runtime: si aplica, probar contra el SQL Server de laboratorio.

## 4. AUDIT — determinista primero, juicio después (`/audit`, en sesión separada)
1. **Determinista** (no se discute, se corrige): CodeQL, analizadores Roslyn, NetArchTest,
   secret scanning, `dotnet list package --vulnerable`, `npm audit`, `pip-audit`.
2. **Triage** de lo que reporten: arreglar, o descartar con justificación escrita en el PR.
3. **Revisión por ejes** (heurística; la hace una sesión que **no** escribió el código):

| Eje | Pregunta guía |
|---|---|
| Invariantes | ¿Respeta los invariantes de CLAUDE.md? ¿Alguna ruta nueva hacia SqlClient, el LLM o los logs? |
| Seguridad | ¿AuthZ deny-by-default? ¿Datos sensibles expuestos? ¿Inyección (SQL, prompt)? Mapear a `OWASP_TOP10.md` |
| Corrección | Casos borde (null, vacío, XML inválido, versión sin capacidad, concurrencia). ¿Determinismo? |
| Atomicidad | ¿Todo-o-nada? ¿Ingesta idempotente y reanudable? ¿Estados intermedios? |
| Eficiencia | Carga sobre el servidor monitoreado, consultas N+1, trabajo repetido |
| Mantenibilidad | Duplicación, acoplamiento, nombres; ¿lo entiende el dueño del módulo? |

Hallazgo real → volver a BUILD.

## 5. PR
Plantilla `.github/pull_request_template.md`: tarea, requisitos, qué y **por qué**,
alternativas descartadas, evidencia de cada CA, hallazgos de auditoría y su resolución.

## 6. MERGE
CI en verde + aprobación de un compañero (idealmente el dueño del módulo vecino).
Actualizar `docs/HANDOFF.md` y, si hubo decisión, `docs/ARCHITECTURE_PLAN.md` §Decisiones.

## Definition of Done
- [ ] Una tarea, código leído antes de editar
- [ ] Prueba de cada CA, en verde
- [ ] `/verify` limpio; CI en verde
- [ ] SAST sin hallazgos abiertos sin justificar; revisión por ejes hecha por otra sesión
- [ ] PR con qué/por qué/evidencia; revisión humana aprobada
- [ ] HANDOFF actualizado
