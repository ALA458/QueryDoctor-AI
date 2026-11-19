# Guía de Evaluación para el Especialista — `S3-04` (`RF-EVA-03`)

**Especialista designado:** Aisner Marrugo  
**Muestra de evaluación:** 10 consultas reales de SQL Server (`QD_Lab` y carga `TPC-H`)

Muchas gracias por participar en el contraste diagnóstico independiente de **Query Doctor AI**.  
Para garantizar rigor metodológico y evitar sesgo de confirmación, el protocolo se divide en **dos etapas secuenciales**:

---

## Paso 1 — Etapa A (Evaluación CIEGA)

1. Abre **únicamente** [cuaderno_etapa_a_ciego.md](./cuaderno_etapa_a_ciego.md) y [planilla_etapa_a.csv](./planilla_etapa_a.csv). *(No abras todavía los archivos de Etapa B).*
2. Cada caso (`CASO-01` .. `CASO-10`) muestra el texto SQL, las métricas de ejecución y el resumen de operadores del plan Showplan XML (junto con la ruta al `.sqlplan` original por si deseas abrirlo en SSMS o Azure Data Studio).
3. En `planilla_etapa_a.csv` completa por cada caso:
   - **`expert_has_issue`**: `si` o `no` (¿tiene esta consulta un problema accionable de rendimiento?).
   - **`expert_categories`**: uno o varios códigos separados por `;` de la lista:
     - `implicit_conversion` · `missing_index` · `costly_full_scan` · `key_lookup` · `parameter_sniffing` · `none` · `other`
   - **`expert_target_objects`**: nodo(s), tabla(s) o índice(s) críticos observados.
   - **`expert_recommendation`**: recomendación técnica principal y precauciones operativas (p. ej. costo de escritura de índices).
   - **`expert_priority_rank`** *(opcional, paso exploratorio `RF-EVA-05`)*: ordena los casos del **`1`** (más urgente/crítica de atender) al **`10`** (menor prioridad), sin repetir números si es posible.
   - **`notes`**: observaciones libres.

---

## Paso 2 — Etapa B (Contraste frente a Query Doctor AI)

1. Una vez terminada la Etapa A, abre [cuaderno_etapa_b_contraste.md](./cuaderno_etapa_b_contraste.md) y [planilla_etapa_b.csv](./planilla_etapa_b.csv).
2. Compara tu criterio previo con los hallazgos determinísticos, el *Query Health Score* y la explicación generada por IA:
   - **`engine_agreement`**: `total` | `parcial` | `desacuerdo` | `falso_positivo` | `omision`
   - **`ai_validity`**: `valida` | `parcialmente_valida` | `incorrecta` | `no_aplica` *(usar `no_aplica` cuando el motor no emitió hallazgos ni explicación IA)*
   - **`ai_utility_1_to_5`**: entero del `1` (poco útil) al `5` (muy útil para diagnosticar), o `no_aplica`
   - **`observations`**: matices faltantes, falsos positivos o discrepancias técnicas.

---

## Paso 3 — Cierre formal

Revisa y confirma [ACTA_ESPECIALISTA.md](./ACTA_ESPECIALISTA.md) junto con ambas planillas CSV diligenciadas.
