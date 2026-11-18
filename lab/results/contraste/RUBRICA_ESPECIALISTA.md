# Rúbrica de Evaluación y Diagnóstico para Especialista Externo — `S3-04`

## Objetivo del Contraste
Validar la exactitud diagnóstica del motor determinístico y la pertinencia técnica de las explicaciones y recomendaciones generadas por Query Doctor AI frente al criterio clínico de un especialista senior en Microsoft SQL Server (`RF-EVA-03`, `OBJ-3`, `OBJ-6`).

## Instrucciones para el Especialista
1. Analice cada uno de los 15 escenarios en el [cuaderno_especialista_ciego.md](./cuaderno_especialista_ciego.md) contando únicamente con el texto SQL, el contexto de ejecución de Query Store y el plan Showplan XML referenciado.
2. Complete la planilla CSV asignando para cada caso:
   - **`specialist_root_cause_rule`:** Antipatrón o causa raíz principal (`REG-01`, `REG-02`, `REG-03`, `REG-04`, `REG-11` o `NINGUNO`).
   - **`specialist_secondary_rules`:** Causas secundarias si existen (separadas por punto y coma `;`).
   - **`specialist_action_type`:** Acción correctiva recomendada (`ALINEAR_TIPOS`, `CREAR_INDICE`, `MODIFICAR_INDICE_INCLUDE`, `REESCRIBIR_PREDICADO_SARGABLE`, `REFACTORIZAR_CTE_TABLA_TEMP`, `NINGUNA_ACCION`).
   - **`specialist_action_detail`:** Explicación técnica concisa de la solución.
   - **`specialist_priority`:** Nivel de gravedad (`CRITICA`, `ALTA`, `MEDIA`, `BAJA`, `LEVE_NINGUNA`).
   - **`specialist_rank`:** Posición en orden de atención del 1 al 15 (1 = la más urgente de intervenir).
