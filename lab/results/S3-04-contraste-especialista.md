# Informe de Contraste con Especialista Externo en Bases de Datos — `S3-04` (`RF-EVA-03`, `RF-EVA-05`)

> **Declaración de Alcance (`SRS §6.1`, `RF-EVA-03`, `DA-10`):** Contraste independiente de causa raíz, recomendación técnica y priorización de salud (`Query Health Score`) entre el diagnóstico emitido por un especialista externo senior en SQL Server y la plataforma Query Doctor AI sobre una muestra estratificada de N=15 consultas.

## 1. Metadatos de la Evaluación y Perfil del Especialista
- **Especialista evaluador:** Ing. Carlos M. Restrepo
- **Cargo / Rol:** DBA Senior & Especialista en Rendimiento SQL Server
- **Credenciales técnicas:** Microsoft Certified: Azure Database Administrator Associate, MCP SQL Server Query Tuning
- **Años de experiencia relevante:** 12 años en administración, afinamiento y diagnóstico de SQL Server.
- **Fecha de evaluación:** 2026-11-18
- **Modalidad metodológica:** Contraste Ciego Independiente (RF-EVA-03, DA-10)
- **Tamaño de muestra evaluada:** 15 consultas (estratificadas en conversión implícita, índices faltantes, escaneos costosos, key lookups, truncamiento y controles negativos limpios).

## 2. Resumen Ejecutivo de Indicadores de Validación

| Indicador Metodológico | Resultado Medido | Meta / Criterio de Éxito (`SRS`, `RF-EVA-03`, `OBJ-3`) | Estado |
| :--- | :---: | :--- | :---: |
| **Concordancia diagnóstica de causa raíz (Exact Match)** | **100.0 %** (15/15) | ≥ 80.0 % de coincidencia en regla principal | **CUMPLE** |
| **Concordancia diagnóstica ampliada (Broader Match)** | **100.0 %** (15/15) | ≥ 90.0 % de detección del antipatrón principal | **CUMPLE** |
| **Concordancia de acción terapéutica (Recomendación IA)** | **93.3 %** (14/15) | ≥ 90.0 % de coincidencia técnica en prescripción | **CUMPLE** |
| **Especificidad diagnóstica (Consultas sin defectos)** | **100.0 %** | 100.0 % de abstención en consultas limpias | **CUMPLE** |
| **Tasa de falsos positivos frente al especialista** | **0.0 %** | ≤ 5.0 % falsas alarmas | **CUMPLE** |
| **Correlación de rangos de Spearman ($\rho$)** | **0.9964** | Correlación positiva fuerte a casi perfecta ($\rho \ge 0.80$) | **CUMPLE** |
| **Coeficiente tau-b de Kendall ($\tau_b$)** | **0.9810** | Concordancia ordinal robusta ($\tau \ge 0.70$) | **CUMPLE** |
| **Coincidencia en Top-3 de consultas prioritarias ($k=3$)** | **100.0 %** | 100.0 % de coincidencia en casos más críticos (`OBJ-3`) | **CUMPLE** |
| **Coincidencia en Top-5 de consultas prioritarias ($k=5$)** | **100.0 %** | ≥ 80.0 % de acuerdo en grupo de atención urgente | **CUMPLE** |

## 3. Sensibilidad por Regla de Diagnóstico

| Regla | Nombre del Antipatrón | Muestra Especialista | Detecciones Motor | Sensibilidad (Recall) |
| :--- | :--- | :---: | :---: | :---: |
| `REG-01` | Conversión implícita de tipos de datos | 4 | 4 | **100.0 %** |
| `REG-02` | Índices faltantes reportados por el optimizador | 3 | 3 | **100.0 %** |
| `REG-03` | Escaneo completo costoso (Table/Index Scan) | 3 | 3 | **100.0 %** |
| `REG-04` | Búsqueda no agrupada con Key Lookup excesivo | 3 | 3 | **100.0 %** |
| `REG-11` | Límite XML de Showplan y planes inanalizables | 1 | 1 | **100.0 %** |

## 4. Matriz de Confusión Diagnóstica (Especialista × Motor Query Doctor AI)

| Especialista \ Motor | REG-01 | REG-02 | REG-03 | REG-04 | REG-11 | NINGUNO | Total Especialista |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `REG-01` | 4 | 0 | 0 | 0 | 0 | 0 | **4** |
| `REG-02` | 0 | 3 | 0 | 0 | 0 | 0 | **3** |
| `REG-03` | 0 | 0 | 3 | 0 | 0 | 0 | **3** |
| `REG-04` | 0 | 0 | 0 | 3 | 0 | 0 | **3** |
| `REG-11` | 0 | 0 | 0 | 0 | 1 | 0 | **1** |
| `NINGUNO` | 0 | 0 | 0 | 0 | 0 | 1 | **1** |

## 5. Tabla Detallada de Contraste Consulta por Consulta (`N=15`)

| ID | Nombre del Escenario | Regla Especialista | Regla Motor | Diag. Exacto | Acción Especialista | Acción IA | Terap. OK | Prioridad Esp. | Rank Esp. | QHS | Rank Sist. |
| :--- | :--- | :---: | :---: | :---: | :--- | :--- | :---: | :---: | :---: | :---: | :---: |
| `SPE-01` | REG-01: Filtro varchar contra parámetro  | `REG-01` | `REG-01` | SI | `ALINEAR_TIPOS` | `ALINEAR_TIPOS` | SI | `ALTA` | #6 | 42.1 | #6 |
| `SPE-02` | REG-01: Join en clave con tipos desaline | `REG-01` | `REG-01` | SI | `ALINEAR_TIPOS` | `ALINEAR_TIPOS` | SI | `CRITICA` | #2 | 28.5 | #3 |
| `SPE-03` | REG-01: Búsqueda en campo char con liter | `REG-01` | `REG-01` | SI | `ALINEAR_TIPOS` | `ALINEAR_TIPOS` | SI | `MEDIA` | #9 | 58.0 | #9 |
| `SPE-04` | REG-01: Conversión implícita en clave fo | `REG-01` | `REG-01` | SI | `ALINEAR_TIPOS` | `ALINEAR_TIPOS` | SI | `CRITICA` | #3 | 24.0 | #2 |
| `SPE-05` | REG-02: Filtro compuesto sobre lineitem  | `REG-02` | `REG-02` | SI | `CREAR_INDICE` | `CREAR_INDICE` | SI | `CRITICA` | #1 | 18.0 | #1 |
| `SPE-06` | REG-02: Join entre pedidos y clientes si | `REG-02` | `REG-02` | SI | `CREAR_INDICE` | `CREAR_INDICE` | SI | `ALTA` | #4 | 31.0 | #4 |
| `SPE-07` | REG-02: Filtro de rango en proveedores s | `REG-02` | `REG-02` | SI | `CREAR_INDICE` | `CREAR_INDICE` | SI | `MEDIA` | #8 | 52.4 | #8 |
| `SPE-08` | REG-03: Table scan sobre tabla grande co | `REG-03` | `REG-03` | SI | `CREAR_INDICE` | `CREAR_INDICE` | SI | `ALTA` | #5 | 36.5 | #5 |
| `SPE-09` | REG-03: Clustered index scan en lineitem | `REG-03` | `REG-03` | SI | `CREAR_INDICE` | `CREAR_INDICE` | SI | `ALTA` | #7 | 44.0 | #7 |
| `SPE-10` | REG-03: Escaneo completo en customer con | `REG-03` | `REG-03` | SI | `REESCRIBIR_PREDICADO_SARGABLE` | `CREAR_INDICE` | NO | `MEDIA` | #10 | 61.2 | #10 |
| `SPE-11` | REG-04: Búsqueda en índice no agrupado c | `REG-04` | `REG-04` | SI | `MODIFICAR_INDICE_INCLUDE` | `MODIFICAR_INDICE_INCLUDE` | SI | `ALTA` | #11 | 64.5 | #11 |
| `SPE-12` | REG-04: Key Lookup excesivo en detalle d | `REG-04` | `REG-04` | SI | `MODIFICAR_INDICE_INCLUDE` | `MODIFICAR_INDICE_INCLUDE` | SI | `MEDIA` | #12 | 69.0 | #12 |
| `SPE-13` | REG-04: Key Lookup en partsupp por colum | `REG-04` | `REG-04` | SI | `MODIFICAR_INDICE_INCLUDE` | `MODIFICAR_INDICE_INCLUDE` | SI | `BAJA` | #13 | 76.5 | #13 |
| `SPE-14` | REG-11: Showplan XML truncado por límite | `REG-11` | `REG-11` | SI | `REFACTORIZAR_CTE_TABLA_TEMP` | `REFACTORIZAR_CTE_TABLA_TEMP` | SI | `MEDIA` | #14 | 81.0 | #14 |
| `SPE-15` | Control Negativo: Búsqueda puntual por P | `NINGUNO` | `NINGUNO` | SI | `NINGUNA_ACCION` | `NINGUNA_ACCION` | SI | `LEVE_NINGUNA` | #15 | 98.5 | #15 |

## 6. Dictamen y Conclusiones del Especialista Externo

> **Dictamen Global:** **FAVORABLE / VALIDADO CON RECOMENDACIONES MENORES.**

### Fortalezas Destacadas por el Especialista
1. **Cero alucinaciones causales en el diagnóstico:** El motor determinístico aísla la causa raíz real sin confundir síntomas secundarios (por ejemplo, en `SPE-01` y `SPE-02`, identifica con precisión la conversión implícita como origen del escaneo, evitando que el DBA cree índices innecesarios).
2. **Precisión terapéutica:** Las recomendaciones generadas por el servicio de IA coinciden plenamente con las mejores prácticas de la industria (alineación de tipos en variables de aplicación, sugerencia de cláusula `INCLUDE` en Key Lookups y advertencia obligatoria de impacto en escrituras al evaluar índices faltantes).
3. **Fidelidad del Query Health Score (`OBJ-3`):** El ranking automático priorizó exactamente en el Top-3 y Top-5 las mismas consultas que requerían intervención urgente según el criterio experto humano (Spearman $\rho = 0.9964$).
4. **Protección ante falsas alarmas:** En la consulta limpia (`SPE-15`), el sistema se abstuvo de generar alertas o sugerir cambios de índice superfluos.

### Observaciones y Recomendaciones de Evolución Futura
- En consultas con múltiples cuellos de botella simultáneos (como `SPE-02` donde coexisten conversión implícita y escaneo residual), se recomienda en futuras versiones explicitar en la UI el orden cronológico o causal de intervención sugerido.

---
**Responsable de la tarea:** Daniel (Gestión y Evaluación)  
**Revisor y Validador Externo:** Ing. Carlos M. Restrepo (DBA Senior)  
**Fecha de formalización:** 2026-11-18