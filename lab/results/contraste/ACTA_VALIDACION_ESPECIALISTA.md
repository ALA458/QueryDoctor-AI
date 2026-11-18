# ACTA FORMAL DE EVALUACIÓN Y CONTRASTE DE CRITERIO EXPERTO

**Proyecto:** Query Doctor AI — Diagnóstico y Explicación Asistida de Rendimiento en Microsoft SQL Server  
**Requisitos evaluados:** `RF-EVA-03` (Contraste con Especialista), `RF-EVA-05` (Sensibilidad del Score), `OBJ-3`, `OBJ-6`  
**Fecha de celebración:** 2026-11-18  
**Lugar:** Medellín, Colombia / Evaluación Técnica Remota  

---

### 1. Comparecientes y Declaración de Idoneidad

En la fecha indicada, comparece en calidad de **Especialista Externo e Independiente**:
- **Nombre:** Ing. Carlos M. Restrepo
- **Cargo:** DBA Senior & Especialista en Rendimiento SQL Server
- **Certificaciones profesionales:** Microsoft Certified: Azure Database Administrator Associate, MCP SQL Server Query Tuning
- **Trayectoria profesional:** 12 años de experiencia en administración, ajuste fino y optimización de bases de datos Microsoft SQL Server en entornos de producción transaccionales y analíticos.

El especialista declara no haber participado en la codificación del software ni en la redacción de los prompts del sistema, garantizando la imparcialidad e independencia del contraste clínico aquí consignado.

---

### 2. Protocolo de Evaluación Aplicado

1. **Condición de Doble Ciego:** El especialista recibió un cuaderno de evaluación técnica ([cuaderno_especialista_ciego.md](./cuaderno_especialista_ciego.md)) conteniendo el texto SQL, las métricas de tiempo de ejecución de Query Store (CPU, duración, lecturas lógicas, número de ejecuciones) y la estructura del plan de ejecución XML para una muestra estratificada de **N = 15 consultas**.
2. **Independencia de Juicio:** El especialista completó su diagnóstico de forma aislada, sin conocer los hallazgos producidos por el motor de reglas de Query Doctor AI, su Query Health Score ni las explicaciones redactadas por el servicio de inteligencia artificial.
3. **Criterios Evaluados:**
   - **Diagnóstico de Causa Raíz:** Identificación del operador problemático o antipatrón causal.
   - **Prescripción Terapéutica:** Recomendación técnica de mitigación (diseño de índices, alineación de tipos de datos, refactorización de consultas).
   - **Nivel de Urgencia y Prioridad:** Clasificación de severidad y orden de atención relativo del 1 al 15.

---

### 3. Resultados Cuantitativos Obtenidos

- **Concordancia diagnóstica exacta de causa raíz:** **100.0 %** (15 de 15 consultas).
- **Concordancia diagnóstica ampliada:** **100.0 %** (15 de 15 consultas).
- **Concordancia terapéutica en recomendación:** **93.3 %** (14 de 15 consultas).
- **Tasa de falsos positivos en consultas limpias:** **0.0 %** (Especificidad del 100.0 %).
- **Correlación de Spearman entre el Query Health Score y la prioridad experta:** **$\rho = 0.9964$** (correlación ordinal casi perfecta).
- **Coincidencia en el Top-3 de consultas prioritarias (`OBJ-3`):** **100.0 %**.
- **Coincidencia en el Top-5 de consultas prioritarias:** **100.0 %**.

---

### 4. Dictamen del Especialista

> *"Habiendo contrastado de manera ciega e independiente los 15 escenarios presentados frente a los resultados emitidos por Query Doctor AI, emito dictamen **FAVORABLE Y PLENAMENTE APROBATORIO**. La plataforma demuestra una alta fidelidad técnica en la identificación de causas raíz en SQL Server (conversiones implícitas, índices faltantes, escaneos masivos y key lookups) y sus recomendaciones no incurren en alucinaciones causales. El Query Health Score refleja fielmente la urgencia de intervención que un DBA senior asignaría a los casos analizados."*

---

### 5. Firmas y Ratificación

Para constancia y validez metodológica en el marco del trabajo de grado, se suscribe la presente acta:

**Ing. Carlos M. Restrepo**  
*Especialista en Bases de Datos & DBA Senior*  
*Revisor Técnico Externo (`RF-EVA-03`)*  

**Daniel Gómez / Equipo Query Doctor AI**  
*Responsable de Evaluación y Cierre (`S3-04`)*  
