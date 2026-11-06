---
description: Auditoría (etapa 4 de WORKFLOW.md) del diff actual. Usar en una sesión que NO escribió el código.
---
Audita el diff de la rama actual contra `main` (`git diff main...HEAD`). Contexto: $ARGUMENTS

1. **Determinista primero.** Reúne los resultados de CodeQL (alertas del PR), los
   analizadores de .NET, NetArchTest, `dotnet list package --vulnerable`, `npm audit`
   y `pip-audit`. Clasifica cada hallazgo: corregir / falso positivo (con razón).
2. **Revisión por ejes** según la tabla de `docs/WORKFLOW.md`: invariantes, seguridad
   (mapear a `docs/OWASP_TOP10.md`), corrección, atomicidad, eficiencia, mantenibilidad.
3. Para cada hallazgo: archivo:línea, eje, severidad (Alta/Media/Baja), escenario
   concreto de fallo y corrección propuesta. No reportes estilo si el formateador ya lo cubre.
4. No edites código. Entrega la lista ordenada por severidad y un veredicto:
   APROBABLE / CAMBIOS REQUERIDOS.
