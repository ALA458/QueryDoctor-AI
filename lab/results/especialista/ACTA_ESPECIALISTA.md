# Acta de Validación Diagnóstica por Especialista — `S3-04` (`RF-EVA-03`, `DA-10`)

## 1. Identificación de la sesión

| Campo | Valor |
| :--- | :--- |
| **Especialista evaluador** | Aisner Marrugo *(Director / Docente experto en Bases de Datos)* |
| **Proyecto evaluado** | Query Doctor AI (Versión 1.0 — Proyecto de Ingeniería I, UTB) |
| **Modalidad de evaluación** | Revisión independiente asíncrona en 2 etapas (Etapa A ciega $\rightarrow$ Etapa B contraste) |
| **Tamaño de muestra (`DA-10`)** | **10 consultas** reales (`5` escenarios sembrados `QD_Lab` + `5` carga `TPC-H`) |
| **Versión catálogo motor / umbrales** | `1.0.0` / `641FC0A9B5A4B476...` |
| **Perfil IA / modelo / prompt** | `gemini_free` / `gemini-2.5-flash` / `v1.1` |
| **Fecha de diligenciamiento** | `____ / ____ / 2026` |

---

## 2. Declaración metodológica

1. Durante la **Etapa A**, el especialista examinó cada consulta (`CASO-01` .. `CASO-10`), su plan Showplan XML y métricas agregadas **sin conocer** los códigos `REG-xx`, puntajes *Query Health Score* ni explicaciones generadas por **Query Doctor AI**.
2. Durante la **Etapa B**, el especialista contrastó sus anotaciones iniciales contra las salidas reales del motor determinístico calibrado y del servicio de IA trazado.

---

## 3. Síntesis y observaciones del especialista

- **Concordancia general observada (motor determinístico):** `[Completar tras ejecutar score o resumir valoración cualitativa]`
- **Valoración técnica y utilidad de las explicaciones de IA:** `[Indicar observaciones sobre precisión, tono y seguridad operativa]`
- **Limitaciones o casos fuera de catálogo identificados:** `[Ej. fenómenos como parameter sniffing o recomendaciones adicionales]`
- **Criterio frente al ordenamiento de prioridad exploratorio (`RF-EVA-05`):** `[Indicar acuerdo o diferencias respecto al Query Health Score]`

---

## 4. Aval de cierre de `S3-04` (`RF-EVA-03`)

- **Nombre y rol:** Aisner Marrugo — Especialista / Director de Proyecto
- **Aprobación:** `[ ] Aprobado con observaciones registradas   [ ] Requiere ajustes`
- **Firma / registro de conformidad:** `______________________________`
