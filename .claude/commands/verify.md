---
description: Compuerta objetiva local (etapa 3 de WORKFLOW.md) para la tarea actual
---
Ejecuta la compuerta VERIFY de `docs/WORKFLOW.md` para la tarea: $ARGUMENTS

1. Corre, en este orden, y detente en el primer fallo mostrando el error:
   - `dotnet build -warnaserror`
   - `dotnet format --verify-no-changes`
   - `dotnet test`
   - Si hubo cambios en `ai-service/`: `ruff check .`, `ruff format --check .`, `pyright`, `pytest`
   - Si hubo cambios en `web/`: `npm run lint`, `npx tsc --noEmit`, `npm test`
2. Lee la tarea en `docs/sprints/SPRINTS.md` (columna "Hecho cuando") y los CA-HU que cita.
   Para cada criterio, indica la prueba que lo demuestra o los pasos manuales exactos.
   Si un criterio no tiene evidencia, dilo: no lo des por cumplido.
3. Revisa el diff contra los invariantes de `CLAUDE.md` y lista cualquier riesgo.
4. Resultado final: VERDE o ROJO, con una tabla criterio → evidencia.
No modifiques pruebas para que pasen. No corrijas nada sin decir qué y por qué.
