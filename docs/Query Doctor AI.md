# Query Doctor AI

**Diseño y desarrollo de una plataforma inteligente para el análisis, diagnóstico y recomendación de optimizaciones de consultas SQL basada en evidencia observable**

Documento final del proyecto · Proyecto de Ingeniería I · Periodo académico 2026-02

Universidad Tecnológica de Bolívar · Escuela de Transformación Digital · Programa de Ingeniería de Sistemas y Computación

Cartagena de Indias, 15 de noviembre de 2026

> **Nota editorial.** Los marcadores entre corchetes (como `[POR COMPLETAR]`) señalan exclusivamente datos administrativos pendientes del equipo o mediciones empíricas programadas para la fase de cierre (Sprint 3). Ninguna cifra ni resultado técnico ha sido estimado ni extrapolado fuera de la evidencia verificada en el repositorio.

## Información general

| Campo | Contenido |
| --- | --- |
| Título del proyecto | Query Doctor AI: plataforma inteligente para el análisis, diagnóstico y recomendación de optimizaciones de consultas SQL |
| Director / tutor | Aisner Marrugo |
| Organización o entidad vinculada | Ninguna. Proyecto académico de la Universidad Tecnológica de Bolívar; la organización propietaria de la base de datos monitoreada actúa como beneficiario indirecto genérico |
| Línea o área temática | Bases de datos, ingeniería de software e inteligencia artificial aplicada |
| Fecha de corte | 15 de noviembre de 2026 (corte de estabilización pre-Sprint 3 posterior al Sprint 2) |
| Repositorio y versión | `https://github.com/ALA458/QueryDoctor-AI` · Rama `chore/pre-sprint3-bloqueantes-y-docs` (base `main` `543af12`; etiqueta de entrega final `v1.0` pendiente del cierre en Sprint 3) |

| Nombre completo | Código | Correo | Responsabilidad técnica |
| --- | --- | --- | --- |
| Álvaro Jesús Ayala Alcalá | [POR COMPLETAR] | [POR COMPLETAR] | Ingesta, conectividad y persistencia |
| Joel David González Barros | [POR COMPLETAR] | [POR COMPLETAR] | Motor determinístico, Query Health Score y validador |
| Maykol Stiven Madrid Romero | [POR COMPLETAR] | [POR COMPLETAR] | Componente de IA, base de conocimiento y evaluación |
| Daniel Alejandro Tache Lara | [POR COMPLETAR] | [POR COMPLETAR] | Interfaz web, experiencia de usuario y gobernanza del proyecto |

## 1. Título del proyecto

**Título:** Diseño y desarrollo de una plataforma inteligente para el análisis, diagnóstico y recomendación de optimizaciones de consultas SQL basada en evidencia observable.

**Nombre del producto:** Query Doctor AI.

El título delimita con precisión el propósito (analizar, diagnosticar y recomendar optimizaciones), el objeto de estudio (consultas SQL) y el enfoque metodológico y tecnológico (combinación de análisis determinístico sobre evidencia observable e inteligencia artificial generativa verificable), evitando formulaciones genéricas.

## 2. Resumen ejecutivo

Identificar qué consulta SQL consume una proporción elevada de recursos resulta relativamente directo mediante las vistas de rendimiento del motor; sin embargo, explicar las causas técnicas de dicha degradación exige interpretar planes de ejecución serializados en formato XML, correlacionar estadísticas históricas y comprender el comportamiento del optimizador. En la práctica operativa, ese conocimiento suele concentrarse de manera tácita en los administradores de bases de datos (DBA), lo cual ralentiza la resolución de incidentes y dificulta la autonomía de los equipos de desarrollo.

Query Doctor AI es una plataforma web no invasiva que se conecta exclusivamente en modo de solo lectura a *Query Store* en Microsoft SQL Server (2016 o superior) y Azure SQL Database para extraer texto de consultas, planes de ejecución y estadísticas agregadas. Un motor determinístico de reglas versionado analiza los planes en busca de antipatrones con evidencia explícita y reproducible; un indicador compuesto, denominado *Query Health Score*, prioriza las consultas según su impacto relativo sobre la carga de trabajo; y un servicio de inteligencia artificial generativa traduce los hallazgos en explicaciones estructuradas en español a partir de fragmentos de conocimiento técnico curados y recuperados de forma determinística desde el núcleo del sistema. Toda afirmación generada atraviesa un validador determinístico externo al modelo de lenguaje que la contrasta contra la evidencia del motor y contra relaciones causa-efecto curadas, registrando el resultado en una bitácora de trazabilidad de solo adición.

El principio rector del diseño establece que el motor determinístico constituye la única fuente de verdad técnica y que la inteligencia artificial únicamente explica evidencia previamente demostrada por dicho motor. La plataforma no ejecuta sentencias de escritura ni modifica la base monitoreada: toda intervención correctiva es evaluada y ejecutada manualmente por una persona. La solución se implementa como un monolito modular en .NET 10 (hosts API y Worker), un servicio de IA aislado en Python (FastAPI), una interfaz web estática en HTML5, CSS y JavaScript servida mediante Nginx, y PostgreSQL 16 (`pgvector`) como repositorio local. La validación empírica contempla escenarios sembrados, cargas de referencia TPC-H, contraste con un especialista en bases de datos y arneses reproducibles, reportando todos los resultados como tasas medidas bajo condiciones explícitas de laboratorio.

## 3. Contexto y antecedentes

### 3.1 Contexto del problema

Las bases de datos transaccionales (OLTP) y analíticas (OLAP) ejecutan millones de sentencias SQL a diario. Al crecer el volumen de datos y la concurrencia aparecen degradaciones de rendimiento, y la respuesta operativa inicial es identificar qué consultas monopolizan CPU, memoria y E/S. La instrumentación moderna hace que ese primer paso sea relativamente simple; la dificultad real está en el diagnóstico.

SQL es un lenguaje declarativo: la persona describe qué datos quiere y el optimizador de consultas decide cómo obtenerlos, evaluando alternativas de unión y de acceso a partir de estadísticas de distribución e índices disponibles. Cuando las estadísticas están desactualizadas, los datos están sesgados o falta una estructura de acceso adecuada, el optimizador estima mal y produce planes ineficientes. Entender por qué obliga a leer grafos de operadores serializados como Showplan XML y a cruzarlos con métricas de ejecución, tarea para la que la mayoría de los equipos de desarrollo carece de tiempo o formación. El resultado habitual es prueba y error, escalamiento vertical costoso o código que perpetúa el problema.

### 3.2 Soluciones nativas del motor

SQL Server incorpora Query Store, que persiste texto de consultas, planes y estadísticas agregadas por intervalo de tiempo (Microsoft, s. f.-a), y desde SQL Server 2017 la corrección automática de planes (Automatic Plan Correction), que compara el plan actual con el último plan bueno conocido y expone recomendaciones en la vista `sys.dm_db_tuning_recommendations`. Microsoft refinó posteriormente el modelo estadístico de esa función (Microsoft, 2026). Esto confirma que detectar regresiones con evidencia estadística es un enfoque validado por el propio fabricante, pero su alcance es limitado: cubre regresiones de plan ya detectadas, no explica causas de fondo en lenguaje comprensible ni prioriza el conjunto de consultas problemáticas de una base de datos.

### 3.3 Herramientas comerciales de monitoreo

| Herramienta | Enfoque principal | Limitación frente a Query Doctor AI |
| --- | --- | --- |
| SolarWinds Database Performance Analyzer | Análisis de esperas multimotor con detección de anomalías asistida por IA | Recomendaciones orientadas al cuello de botella general; menor profundidad causal por consulta individual |
| Redgate SQL Monitor | Monitoreo en tiempo real, alertas y asesores de tablas e índices | Seguimiento de planes y recomendaciones reactivos, sin explicación en lenguaje natural trazable a evidencia (SQLYARD, 2025) |
| Datadog Database Monitoring | Correlación de métricas SQL con trazas de aplicación | El diagnóstico SQL es un módulo de una plataforma de observabilidad general, no el núcleo (IDERA, 2025) |

### 3.4 Investigación académica

El antecedente directo más relevante es D-Bot, un sistema de diagnóstico de bases de datos basado en modelos de lenguaje grandes (Zhou et al., 2023b), precedido por la línea «LLM as DBA» del mismo grupo (Zhou et al., 2023a). Como antecedente previo a los LLM, DBSherlock ayuda a explicar la causa raíz de anomalías comparando regiones de comportamiento normal y anómalo (Yoon et al., 2016). La literatura sobre ajuste automático de índices, desde heurísticas hasta aprendizaje por refuerzo, está consolidada (Zhao et al., 2023; Ding et al., 2024). Un estudio de clasificación de la calidad de consultas SQL con árboles de decisión, k-NN, SVM y Naive Bayes respalda el análisis estático de patrones (Predicting SQL query quality using machine learning techniques, 2023), y una revisión sistemática de 183 artículos publicados entre 2020 y 2024 muestra que el uso de LLM en operaciones de infraestructura es un campo activo (A survey of AIOps in the era of large language models, 2025).

Para el componente de IA se adopta la generación aumentada por recuperación (Lewis et al., 2020), la evaluación de fidelidad inspirada en RAGAS (Es et al., 2023) y la definición de alucinación como afirmación no sustentada por el contexto disponible (Ji et al., 2023).

### 3.5 Hallazgo técnico propio: qué evidencia contiene realmente Query Store

Antes de comprometer reglas del motor se analizó, con la documentación oficial del fabricante, qué evidencia ofrece Query Store (SRS, Apéndice A). Las conclusiones condicionan todo el diseño:

1. Los planes almacenados son **planes estimados**, equivalentes a `SET SHOWPLAN_XML ON` (Microsoft, s. f.-a, s. f.-b). No contienen filas reales por operador ni advertencias de desbordamiento a TempDB, que solo existen en un plan real (Microsoft, s. f.-d).
2. Las estadísticas de ejecución están **agregadas por plan e intervalo**, no por operador (Microsoft, s. f.-c).
3. Las señales de compilación (conversión implícita que afecta el plan, índices faltantes, ausencia de estadísticas, uniones sin predicado, lecturas completas y búsquedas por clave) sí están en el plan estimado y son plenamente detectables (Redgate Simple Talk, s. f.).
4. El plan real es una función opcional que el DBA debe habilitar y que exige un permiso adicional (Microsoft, s. f.-e); la plataforma nunca la activa, en cumplimiento de la restricción de solo lectura.

De ahí surge la **clase de evidencia** de cada hallazgo (ESTIMADA, EJECUCION\_AGREGADA o PLAN\_REAL): ni el motor ni la IA pueden presentar un dato estimado como observado en una ejecución real.

Estas hipótesis (`H1`–`H5`) y hallazgos documentales (`F1`–`F8`) se formularon inicialmente a partir de documentación oficial del fabricante. Durante el Sprint 1 (tarea `S1-07`), el equipo ejecutó el protocolo empírico sobre SQL Server 2022 Developer (`16.0.4315.2`, nivel de compatibilidad `160`) mediante siete escenarios sembrados (`ESC01` a `ESC06` y `ESC04B`), 16 planes de referencia y tres corridas consecutivas con reproducibilidad idéntica por hash SHA-256 (`lab/results/S1-07-verificacion.md`). Los resultados verificados se sintetizan en la siguiente tabla:

| ID | Hallazgo / hipótesis verificada | Resultado empírico | Evidencia principal en laboratorio |
| --- | --- | --- | --- |
| **F1** (`H1`) | El plan almacenado en Query Store es estimado (`SHOWPLAN_XML`) | **Confirmado** | `0` nodos `<RunTimeInformation>` y `0` atributos `ActualRows` en los 9 planes `*.qs.*.sqlplan`, frente a presencia completa en los `*.actual.sqlplan` |
| **F2** (`H1`) | El plan estimado omite advertencias reales de desbordamiento (`SpillToTempDb`, `SortSpillDetails`) | **Confirmado** | `ESC05.actual.sqlplan` registra `SpillToTempDb=2` y `SortSpillDetails=2`, mientras `ESC05.qs.1.sqlplan` registra `0` ocurrencias |
| **F3** (`H3`) | `sys.query_store_runtime_stats` agrega métricas por `(plan_id, execution_type, interval_id)` sin filas por operador ni contadores de *spill* | **Confirmado** (precisión de columna) | Inspección de las 74 columnas reales (`checks.txt`): la memoria se denomina `avg_query_max_used_memory` (no `avg_max_used_memory`) y se observan múltiples filas por intervalo |
| **F4** | Leer Query Store requiere `VIEW DATABASE STATE` (o `VIEW DATABASE PERFORMANCE STATE` en 2022) y cero permisos de escritura | **Confirmado** | Cuenta `qd_reader` lee todas las vistas y falla al intentar `INSERT` (error `229`); `qd_noperm` falla con error `262` hasta recibir `VIEW DATABASE PERFORMANCE STATE` |
| **F5** | Valores por defecto en SQL Server 2022 para bases nuevas | **Confirmado** | Base temporal `QD_Defaults`: Query Store activo (`READ_WRITE`), intervalo `60` min, retención `30` días, modo `AUTO`, tamaño máximo `1000` MB |
| **F6** (`H5`) | `sys.dm_exec_query_plan_stats` es opcional (`LAST_QUERY_PLAN_STATS`), volátil y requiere permiso a nivel de servidor | **Confirmado** | Con opción en `0` omite `ActualRows`; tras `CLEAR PROCEDURE_CACHE` desaparece la fila; `qd_reader` recibe error `300` (`VIEW SERVER PERFORMANCE STATE` denegado) |
| **F7** (`H2`) | Las señales de compilación (`PlanAffectingConvert`, `MissingIndexes`, `Lookup="1"`, `EstimateRows`) permanecen en Query Store | **Confirmado** | Verificado en `ESC01.qs.1`, `ESC02.qs.1`, `ESC03.qs.1` y `ESC04B.qs.1` |
| **F8** | El tipo `xml` de SQL Server falla al superar 128 niveles; debe leerse como `nvarchar(max)` | **Confirmado** | En `ESC06` (140 niveles), `TRY_CAST(query_plan AS xml)` devuelve `NULL`, mientras `CAST(... AS nvarchar(max))` entrega los `304 890` caracteres íntegros |
| **H4** | La discrepancia de cardinalidad sin plan real solo se observa a nivel de sentencia | **Confirmado** | En `ESC04B` (`plan_id = 25`), `StatementEstRows = 1.0` frente a `avg_rowcount = 495 000.5` en `runtime_stats` |

Adicionalmente, el escenario `ESC04` evidenció que bajo nivel de compatibilidad `160` (*Parameter Sensitive Plan Optimization*) SQL Server genera planes *Dispatcher* (`ESC04.qs.1.sqlplan`, 1 264 bytes) sin nodos `<RelOp>`, hallazgo incorporado directamente al diseño tolerante de `ShowplanXmlParser` (`S1-12`). Asimismo, la validación inicial de la lista blanca (`S1-08`) detectó que las vistas del catálogo `sys.*` no figuran en `sys.columns`/`sys.objects`, lo cual motivó su corrección a `sys.all_columns` y `sys.all_objects` en `TelemetryStatementWhitelist.cs`.

### 3.6 Balance y vacío identificado

El problema no ha pasado desapercibido: los motores nativos, las herramientas comerciales y la investigación reciente incorporan en mayor o menor medida detección de anomalías, alertas y recomendaciones. Sin embargo, ninguna de las soluciones revisadas integra de forma verificable evidencia determinística del motor con explicaciones trazables y obligatoriamente ancladas a dato observable. Las nativas detectan regresiones sin explicar causas; las comerciales rara vez exhiben la evidencia que sustenta sus recomendaciones; y los sistemas académicos basados en LLM no siempre obligan a que cada explicación apunte a un dato del motor. **Ese espacio entre la detección automatizada y la explicación verificable es el aporte del proyecto.** No se pretende una técnica inédita sino una integración deliberada y medible.

## 4. Planteamiento del problema

### 4.1 Descripción del problema

**¿Qué ocurre?** Diagnosticar la causa de una consulta SQL lenta o costosa exige interpretar manualmente planes de ejecución y estadísticas, y ese conocimiento rara vez está documentado de forma reproducible.

**¿Dónde ocurre?** En equipos que operan bases de datos SQL Server y Azure SQL Database, tanto locales como en la nube, donde el consumo de cómputo se traduce en costo directo.

**¿Quiénes están afectados?** Los DBA, que concentran la carga de diagnóstico; los desarrolladores backend, que dependen del DBA para entender sus propias consultas; y las organizaciones, que sufren incidentes de rendimiento y sobreaprovisionamiento.

**Evidencia.** La existencia y pertinencia del problema se sustentan en fuentes técnicas del fabricante (Microsoft, 2026), oferta comercial especializada (SQLYARD, 2025; IDERA, 2025) y literatura académica reciente (Zhou et al., 2023b; Yoon et al., 2016). Como limitación metodológica declarada, no se levantó una línea base primaria mediante entrevistas o mediciones cronometradas en una organización productiva concreta; dicha brecha se mitiga mediante el contraste frente a un especialista externo (`RF-EVA-03`) y la prueba controlada de usabilidad (`RNF-INT-01`).

**Pertinencia para Ingeniería de Sistemas.** El problema se resuelve integrando bases de datos, arquitectura de software, ingeniería de requisitos, seguridad, diseño de interfaces e IA aplicada, competencias propias del perfil de egreso.

### 4.2 Análisis de causas y efectos

| Causa | Efecto |
| --- | --- |
| Falta de correlación automática entre métricas, plan de ejecución y estructura de índices | Diagnóstico lento y dependiente de un especialista senior |
| Ausencia de historial estructurado de análisis por consulta | Imposibilidad de detectar regresiones sin herramientas externas |
| Conocimiento tácito no documentado sobre patrones de planes problemáticos | Curva de aprendizaje alta para desarrolladores sin experiencia en tuning |
| Herramientas existentes que generan alertas con explicaciones limitadas | Baja confianza y adopción parcial de las recomendaciones automáticas |
| Recomendaciones de IA sin verificación contra evidencia | Riesgo de afirmaciones sin sustento y de sesgo de automatización (Parasuraman y Manzey, 2010) |

### 4.3 Pregunta problema

¿De qué manera el diseño y desarrollo de una plataforma inteligente que combine el análisis determinístico de telemetría (métricas y planes de ejecución) con inteligencia artificial generativa puede asistir a desarrolladores y DBA en el diagnóstico temprano, la explicación comprensible y la recomendación fundamentada de optimizaciones para consultas SQL problemáticas?

## 5. Justificación

### 5.1 Relevancia del problema

El rendimiento de las bases de datos incide en la experiencia del usuario final y en el costo operativo, especialmente en la nube con facturación por consumo. Automatizar parte del diagnóstico reduce el tiempo medio de resolución (MTTR) de incidentes de rendimiento y la dependencia de un único especialista. Además, el proyecto aborda un problema contemporáneo de la IA aplicada: cómo obtener explicaciones útiles de un modelo generativo sin perder verificabilidad.

### 5.2 Beneficiarios

- **Directos:** DBA y desarrolladores backend responsables de escribir y mantener consultas.
- **Indirectos:** equipos de plataforma e infraestructura que responden por acuerdos de nivel de servicio (SLA) y la organización dueña de la base de datos.
- **Académicos:** docentes y evaluadores, que pueden reproducir la evaluación mediante los arneses incluidos.

### 5.3 Impacto esperado

Las mejoras esperadas se expresan como metas verificables, no como afirmaciones generales: tiempo de identificación de la consulta prioritaria y su causa (RNF-INT-01), precisión y exhaustividad por regla (RNF-FUN-01), cero explicaciones no validadas presentadas como verificadas (RNF-FUN-02), cero literales sensibles enviados al proveedor de IA (RNF-SEG-05) y sobrecarga acotada sobre la base monitoreada (RNF-RND-01). El detalle y el método de medición están en la sección 15.

### 5.4 Pertinencia para Ingeniería de Sistemas

Se aplican: ingeniería de requisitos según ISO/IEC/IEEE 29148 (2018) y clasificación de calidad según ISO/IEC 25010 (2023); arquitectura documentada con el modelo C4 y registros de decisión (ADR); seguridad según OWASP Top 10 for LLM Applications 2025 (OWASP Foundation, 2025a) y ASVS 5.0 (OWASP Foundation, 2025b); protección de datos conforme a la Ley 1581 de 2012; ingeniería de datos (ingesta incremental idempotente) y evaluación experimental de un componente de IA.

## 6. Objetivos

### 6.1 Objetivo general

Diseñar y desarrollar una plataforma que integre métricas de ejecución, análisis de planes y explicaciones generadas mediante inteligencia artificial para apoyar el diagnóstico y la priorización de consultas SQL con bajo rendimiento en SQL Server y Azure SQL Database.

### 6.2 Objetivos específicos

| ID | Objetivo específico | Resultado verificable |
| --- | --- | --- |
| OBJ-1 | Implementar la ingesta segura y de solo lectura de métricas y planes de ejecución desde Query Store y persistirlos en un modelo de datos local propio | Ingesta idempotente y reanudable; registro de sentencias emitidas que contiene únicamente SELECT sobre una lista blanca |
| OBJ-2 | Implementar un motor determinístico de reglas técnicas que analice los planes capturados y produzca hallazgos reproducibles y trazables | Catálogo versionado de reglas; mismo plan, versión y umbrales producen los mismos hallazgos |
| OBJ-3 | Desarrollar un Query Health Score que priorice las consultas según su impacto real sobre el consumo de recursos | Score de 0 a 100 con desglose y resultado idéntico ante los mismos datos y pesos |
| OBJ-4 | Integrar un componente de IA generativa cuyas explicaciones sean trazables a evidencia observable y verificables automáticamente | Validador automático y registro de trazabilidad de solo adición |
| OBJ-5 | Construir un tablero funcional que presente consultas críticas, tendencias históricas y recomendaciones respaldadas por evidencia | Tablero con listado priorizado, detalle, visor de planes y explicación con enlaces a evidencia |
| OBJ-6 | Validar empíricamente la precisión y la utilidad de la plataforma con escenarios controlados, benchmarks estándar y contraste con un especialista | Arneses reproducibles e informe del laboratorio |

Los cinco primeros objetivos provienen de la propuesta (Avance 1); el OBJ-6 se incorporó en el SRS para dar sustento medible a las afirmaciones de eficacia, en coherencia con la exigencia del tutor de que el componente de IA «debe poder evaluarse».

## 7. Alcance del proyecto

### 7.1 Incluye

El alcance de la Versión 1.0 (MVP) se delimita en el SRS (sección 1.2) junto con el criterio de ajuste de alcance (sección 5.3) y comprende:

- Registro y administración de conexiones cifradas y de solo lectura a SQL Server (2016 o superior) y Azure SQL Database, con detección automática de versión, permisos, estado de Query Store y matriz de soporte de tres niveles (`Probado`, `Compatible sin pruebas`, `No soportado`).
- Ingesta manual o programada de texto de consultas, planes y estadísticas desde Query Store, con persistencia en un repositorio local propio, de forma incremental, idempotente y reanudable.
- Análisis estático determinístico del Showplan XML mediante un catálogo versionado para las reglas de prioridad *Debe* (`REG-01`, `REG-02`, `REG-03`, `REG-04` y `REG-11`), diferenciando explícitamente la clase de evidencia de cada hallazgo.
- Cálculo determinístico del *Query Health Score* con desglose de componentes e historial por consulta.
- Explicaciones y recomendaciones generadas por IA sobre fragmentos de conocimiento curados y recuperados de forma determinística por regla desde el núcleo .NET, validadas automáticamente y registradas en una traza encadenada de solo adición.
- Tablero de consultas críticas, series temporales por plan, visor jerárquico de planes con nodos resaltados y explicación trazable con enlaces a evidencia.
- Autenticación obligatoria, control de acceso por roles (`DBA` y `Desarrollador`) filtrado en la capa de datos y cifrado por campo en reposo.
- Arneses de evaluación reproducibles del motor y del componente de IA, e informe versionado del laboratorio.

### 7.2 No incluye

- **Cualquier modificación de la base monitoreada** (DDL, DML o cambios de configuración, incluido forzar planes). Toda acción correctiva la decide y ejecuta manualmente un DBA (`RES-01`, `ADR-07`).
- Motores distintos de SQL Server y Azure SQL Database (PostgreSQL, MySQL, Oracle), multitenant a gran escala e integración con CI/CD, previstos para Proyecto de Ingeniería II.
- Monitoreo en tiempo real y alertas automáticas: se analiza telemetría histórica capturada periódicamente.
- Una garantía absoluta de ausencia de errores en las explicaciones de IA. En su lugar, ninguna explicación se presenta como verificada si no superó el validador, y la tasa de afirmaciones sustentadas se reporta como métrica medida.
- Seudonimización de nombres de tablas y columnas hacia el proveedor de IA (riesgo residual aceptado, `DA-13`).
- Funcionalidades de prioridad *Debería* y *Podría* excluidas conforme al orden de recorte del SRS (§5.3): reglas `REG-05` a `REG-10`, exportación a PDF, edición de umbrales desde la interfaz, comparación visual antes/después, comparación gráfica de planes y captura del plan real.

### 7.3 Producto o resultado esperado

Una Release 1.0 desplegable mediante `docker compose up`, integrada por cinco contenedores de plataforma (`web`, `api`, `worker`, `ai`, `db`) más un sexto contenedor de laboratorio (`sqlserver` Developer 2022 en la red aislada `targets`), capaz de conectarse a una instancia soportada, priorizar consultas críticas, presentar hallazgos con su evidencia y emitir explicaciones trazables. La entrega incluye código fuente, pruebas automatizadas, SRS v1.1 definitivo, documento de arquitectura, arneses de evaluación e informe del laboratorio.

## 8. Usuarios, clientes y partes interesadas

| Actor | Rol | Necesidad o interés | Nivel de impacto |
| --- | --- | --- | --- |
| DBA | Usuario final principal | Diagnóstico rápido, confiable y trazable; control total sobre cualquier cambio | Alto |
| Desarrollador backend | Usuario secundario | Entender por qué su consulta es lenta sin depender permanentemente del DBA | Alto |
| Docente / tutor (Aisner Marrugo) | Evaluador académico | Rigor metodológico, trazabilidad IA → evidencia y reproducibilidad de resultados | Alto |
| Organización dueña de la base de datos | Beneficiario indirecto | Menor costo de cómputo y menos incidentes; garantía de no intrusión | Medio |
| Especialista en bases de datos | Revisor externo | Contrastar hallazgos y explicaciones con criterio experto (RF-EVA-03) | Medio |

La validación de los resultados corresponde al tutor, apoyado en el contraste con un especialista cuya disponibilidad efectiva y número de consultas (N) se ratifican en la reunión S1-20 (DA-10).

## 9. Requerimientos preliminares

Los requisitos se especificaron conforme a ISO/IEC/IEEE 29148 (2018), con sintaxis controlada tipo EARS (Mavin et al., 2009), priorización MoSCoW (Clegg y Barker, 1994) y clasificación de los no funcionales según ISO/IEC 25010 (2023). El SRS v1.1 definitivo integra la adenda y consolida **114 requisitos** (76 funcionales y 38 no funcionales), de los cuales 74 tienen prioridad *Debe*, equivalentes al **64,9 %** del total. A continuación se presentan los más representativos; el listado completo, las historias de usuario (HU-01 a HU-21), los casos de uso (CU-01 a CU-07) y las matrices de trazabilidad están en el SRS (`docs/SRS.tex`).

### 9.1 Requerimientos funcionales

| ID | Requerimiento | Prioridad |
| --- | --- | --- |
| RF-CFG-01 a 04 | Registrar conexiones, probar conectividad (15 s por defecto), rechazar versiones anteriores a SQL Server 2016 y mostrar el estado de Query Store sin modificarlo | Debe |
| RF-ING-04, 05 | Agregar estadísticas por plan, tipo de ejecución e intervalo, ponderadas por ejecuciones, y procesar solo intervalos nuevos sin generar duplicados | Debe |
| RF-ING-09 | Registrar como no analizable el plan nulo, truncado o inválido sin detener la ingesta | Debe |
| RF-MOT-02, 03 | Mismo plan, catálogo y umbrales producen los mismos hallazgos; cada hallazgo declara regla, versión, nodos, evidencia y clase de evidencia | Debe |
| RF-MOT-04 a 07 | Detectar conversión implícita, lecturas completas costosas, Key/RID Lookup excesivos y decodificar índices faltantes (REG-01 a 04) | Debe |
| RF-SCO-01 a 04 | Query Health Score de 0 a 100, determinístico, con desglose, ordenable por score o porcentaje de consumo | Debe |
| RF-IA-01 a 03 | Explicar solo si hay hallazgos; afirmaciones con referencias; validador de identificadores y valores numéricos antes de mostrar | Debe |
| RF-IA-05, RF-DASH-06 | Toda recomendación es una sugerencia con aviso persistente de revisión humana, sin acciones ejecutables | Debe |
| RF-IA-06 | Si el proveedor de IA falla, mostrar los hallazgos determinísticos y avisar la indisponibilidad | Debe |
| RF-TRZ-01, 02 | Registrar evidencia, versiones, modelo, prompt, hash de contexto y resultado de validación en un registro de solo adición | Debe |
| RF-DASH-01 a 05 | Listado priorizado, detalle, series temporales, visor jerárquico de planes y enlaces de cada afirmación a su evidencia | Debe |
| RF-EVA-01, 02 | Arnés reproducible del motor (precisión y exhaustividad por regla) y de la IA (afirmaciones sustentadas, sin sustento y concordancia numérica) | Debe |
| RF-MOT-08 a 10, 13 | Reglas REG-05 a REG-08: estadísticas ausentes, unión sin predicado, discrepancia de cardinalidad y regresión entre planes | Debería |
| RF-DASH-07, 08 | Vista gráfica del plan y comparación de dos planes | Debería |
| RF-DASH-10 a 12 | Exportar reporte, comparar antes y después de un cambio, editar umbrales desde la interfaz | Podría |

### 9.2 Requerimientos no funcionales

Las metas marcadas con † son provisionales y se calibrarán con datos empíricos (DA-06).

| ID | Requerimiento | Criterio preliminar |
| --- | --- | --- |
| RNF-SEG-01 | Solo SELECT sobre una lista blanca de vistas de Query Store y catálogo | 0 sentencias DDL, DML o de configuración en el registro de sentencias |
| RNF-SEG-03, 04 | TLS 1.2 o superior con validación de certificado; credenciales y claves cifradas en reposo | Sin secretos en texto plano en repositorio, configuración ni registros |
| RNF-SEG-05 | Sustituir literales por marcadores antes de enviar contexto al proveedor de IA | 0 ocurrencias de literales sembrados en las solicitudes registradas |
| RNF-SEG-06, 07 | Datos no confiables delimitados; salida del modelo validada contra esquema y saneada | 10 o más casos de inyección sin alterar el formato ni evadir el validador |
| RNF-SEG-11 a 14 | Autenticación obligatoria, roles, denegación por defecto y cifrado por campo (adenda v1.1) | Pruebas de autorización por endpoint y rol |
| RNF-FUN-01 | Precisión y exhaustividad de las reglas *Debe* | Precisión ≥ 90 % y exhaustividad ≥ 80 % † |
| RNF-FUN-02 | Ninguna explicación no validada se muestra como verificada | 0 excepciones |
| RNF-FUN-03 | Explicaciones que superan el validador al primer intento | ≥ 90 % y 100 % de coincidencia numérica † |
| RNF-RND-01 | Sobrecarga sobre la base monitoreada durante la ingesta | ≤ 5 % de CPU y 0 esperas de bloqueo sobre objetos de usuario † |
| RNF-RND-02, 03, 04 | Latencia del tablero, de la explicación y del análisis de un plan | Listado ≤ 2 s (p95); explicación ≤ 30 s (p95); plan de 1 MB ≤ 500 ms (p95) † |
| RNF-FIA-01, 02 | Reanudar ingesta sin pérdida ni duplicación; límite de tiempo y reintentos hacia la IA | Reintento exponencial; 60 s y 2 reintentos por defecto |
| RNF-COM-01, 02, 04 | Compatibilidad con matriz de soporte de tres niveles; tolerancia a elementos desconocidos del Showplan | Aceptación en SQL Server 2022 y Azure SQL (2019 según ADR) |
| RNF-INT-01 a 03 | Usabilidad medida con usuarios y accesibilidad | 80 % de 5 o más desarrolladores identifica la consulta prioritaria y su causa en ≤ 3 min †; SUS ≥ 68 † (Brooke, 1996); WCAG 2.2 AA (World Wide Web Consortium, 2023) |
| RNF-MAN-01, 02 | Reglas independientes, un plan positivo y uno negativo por regla | Cobertura del motor ≥ 80 % † |
| RNF-FLE-01 | Instalación por una persona ajena al equipo siguiendo el README | ≤ 60 minutos † |

## 10. Propuesta de solución

Query Doctor AI es una plataforma web de diagnóstico de rendimiento que se integra con dos sistemas externos: las bases de datos monitoreadas, de las que solo lee información ya recolectada por Query Store, y un proveedor de IA generativa, al que envía únicamente contexto mínimo y enmascarado. Es utilizada por DBA y desarrolladores a través de un navegador.

**Flujo de valor.** La telemetría se ingiere desde Query Store y se persiste localmente; el motor determinístico analiza cada plan y produce hallazgos; en paralelo, esos hallazgos alimentan el cálculo del Query Health Score y la generación de explicaciones; las explicaciones pasan obligatoriamente por un validador automático y quedan registradas en la traza.

**Componentes principales.**

1. **Capa de ingesta y persistencia:** conector Query Store de solo lectura (único componente con acceso a las bases objetivo) y repositorio local.
2. **Capa de análisis:** analizador de Showplan XML, motor de reglas versionado (clases de evidencia A, B, C y D) y calculadora del Query Health Score, que toma en cuenta consumo relativo de CPU, duración y lecturas, frecuencia de ejecución y severidad de hallazgos (RF-SCO-02).
3. **Capa de explicación y presentación:** enmascarado léxico de literales con el analizador oficial de T-SQL (`ScriptDom`), recuperación determinística de fragmentos curados por `rule_id` (`RF-IA-04`), servicio de IA generativa con esquema JSON estricto, validador externo en .NET con tres criterios (existencia de identificadores, coincidencia numérica y relación causal curada), traza encadenada por hash y tablero web.

**Información utilizada.** Vistas `sys.query_store_query_text`, `query`, `plan`, `runtime_stats` y `runtime_stats_interval` (Microsoft, s. f.-b, s. f.-c).

**Diferencias frente a soluciones existentes.**

- Cada hallazgo declara su clase de evidencia y los nodos del plan que lo originan; no existen alertas opacas.
- La IA no diagnostica: explica lo que el motor demostró, y el componente que genera no certifica su propia salida (el validador vive fuera del servicio de IA).
- Las explicaciones distinguen entre TRAZADA (superaron el validador) y NO\_VERIFICADA, y no se afirma exactitud absoluta.
- La eficacia se mide con arneses reproducibles, incluida una condición de control (el mismo modelo sin hallazgos del motor) que cuantifica el aporte del enfoque híbrido (Zhou et al., 2023b).
- Restricción de diseño verificable: cuenta con únicamente `VIEW DATABASE STATE` y ninguna ruta de código hacia escritura.

## 11. Alternativas de solución

La arquitectura se seleccionó comparando tres alternativas (Documento de Arquitectura, ADR-01, ADR-08 y sección 13).

| Criterio | A. Monolito modular .NET (2 hosts) + servicio de IA en Python + PostgreSQL | B. Microservicios por módulo | C. Arquitectura preliminar de cuatro capas (Collector Agent, lakehouse, forzar plan) |
| --- | --- | --- | --- |
| Costo | Bajo: un host, Docker Compose, sin broker ni almacén de objetos | Medio o alto: más contenedores, orquestación y observabilidad | Alto: agente por servidor, almacenamiento de objetos y motor analítico adicionales |
| Complejidad | Moderada: límites de módulo con proyectos y dependencias unidireccionales | Alta: contratos entre servicios y operación distribuida | Alta: tres tecnologías de datos sin requisito que las exija |
| Tiempo de desarrollo | Compatible con 4 personas y aproximadamente 11 semanas | Excede el calendario (RES-04, RES-05) | Excede el calendario |
| Escalabilidad | Suficiente para una organización; particionado mensual si fuera necesario | Alta, pero innecesaria en el MVP | Alta (multitenant), prevista para Proyecto II |
| Mantenibilidad | Alta: reglas como unidades independientes, pruebas de arquitectura automatizadas | Media: sobrecarga de integración | Baja para un equipo de cuatro personas |
| Seguridad | Alta: solo el Worker llega a las bases objetivo y solo el servicio de IA sale a Internet | Alta si se segmenta bien, con mayor superficie | Menor: instalar un agente junto a la base y permitir forzar plan viola RES-01 |
| Viabilidad técnica | Alta | Media | Baja para el MVP |

**Alternativa seleccionada: A.** Es la estructura más pequeña que cumple los requisitos *Debe* con el equipo y el calendario disponibles, mantiene la restricción de solo lectura como invariante estructural y conserva las ideas válidas de C (marca de agua incremental, motor dual, flujo de banderas JSON) para fases posteriores.

Principales decisiones asociadas (ADR, todas aceptadas por el equipo; las propuestas iniciales ADR-05, 06 y 11 se ratifican según el plan de sprints):

| ADR | Decisión | Motivo principal |
| --- | --- | --- |
| ADR-02 | IA en un servicio Python separado | Ecosistema de RAG y evaluación; aísla la única salida a Internet |
| ADR-03 | PostgreSQL 16 con pgvector como repositorio local | Sin límites de licencia o recursos, búsqueda vectorial madura, motor distinto del monitoreado |
| ADR-04 | Hangfire sobre el mismo PostgreSQL | Reintentos, trabajos recurrentes y panel sin contenedor adicional |
| ADR-05 | Enmascarado con ScriptDom en el núcleo .NET | Analizador léxico oficial; enmascarar antes de la frontera evita que la IA vea literales |
| ADR-06 | Validador en el núcleo .NET | El componente que genera no debe certificar su propia salida |
| ADR-07 | Sin forzar plan ni escritura en producción | Viola RES-01 y RNF-SEG-01 |
| ADR-09 | Identity con usuarios locales; OIDC fuera del MVP | Cumple RNF-SEG-13 sin depender de un proveedor de identidad |
| ADR-10 | Adaptador LLM por perfiles (`openrouter_free`, `openrouter_paid`, `gemini_free`, `ollama_local`, `mock`) con candado anti-producción | Permite desarrollar y validar con datos de laboratorio sin presupuesto inicial, impidiendo por código arrancar perfiles gratuitos con `APP_ENV=prod` |
| ADR-11 | Cifrado por campo AES-256-GCM | Protege también respaldos y es portable entre motores |

## 12. Arquitectura del sistema

La arquitectura implementada sigue el documento `docs/arquitectura/ARQUITECTURA_v0.1.md`, modelada bajo el enfoque C4 (Brown, s. f.), organizada por partes interesadas y vistas conforme a ISO/IEC/IEEE 42010 (2022) y documentada mediante registros de decisión arquitectónica (Nygard, 2011).

**\[INSERTAR FIGURA 12.1: Diagrama de contexto (C4 nivel 1), Figura 2.1 del SRS.\]**

**\[INSERTAR FIGURA 12.2: Diagrama de contenedores (C4 nivel 2), documento de arquitectura, sección 4 (`docs/arquitectura/media/image1.png`).\]**

**\[INSERTAR FIGURA 12.3: Flujo de explicación con enmascarado y validación, documento de arquitectura, sección 6.4 (`docs/arquitectura/media/image2.png`).\]**

### 12.1 Contenedores

| Contenedor | Tecnología implementada | Responsabilidad | Restricción estructural ("no hace") |
| --- | --- | --- | --- |
| Web | HTML5, CSS y JavaScript nativo servidos por Nginx | Autenticación, listado y alta de conexiones, listado priorizado de consultas, detalle con series SVG, visor jerárquico de planes y vista de explicación solo texto | No renderiza HTML del LLM; no gestiona ni conserva secretos |
| API | ASP.NET Core sobre .NET 10 (LTS) | Autenticación (Identity), RBAC por políticas (`DbaOnly`, `ConnectionReader`), filtro global por conexión en EF Core, endpoints REST, panel `/hangfire` y encolado de trabajos | No abre conexiones a bases objetivo ni llama al servicio de IA o al LLM |
| Worker | .NET 10, `Host` + servidores Hangfire (`QueryDoctor.Worker`, `Probe`, `Explain`) | Sondeo de capacidades, ingesta incremental, parser, motor de reglas, *Query Health Score*, enmascarado léxico, armado de contexto, cliente HTTP al servicio de IA y validador | No expone puertos de red |
| Servicio de IA | Python 3.12, FastAPI, Pydantic | Plantillas de prompt versionadas (`v1.1`) con delimitación anti-inyección, adaptador LLM multi-perfil (`openrouter_*`, `gemini_free`, `ollama_local`, `mock`), validación de esquema JSON y candado `APP_ENV=prod` | No ve literales ni credenciales; no consulta bases objetivo; no decide si una explicación queda `TRAZADA` |
| Base de datos | PostgreSQL 16 con `pgvector` | Repositorio local (5 esquemas), colas Hangfire, traza/auditoría encadenada de solo adición y base de conocimiento curada | Sin acceso directo desde clientes externos |
| Laboratorio (dev/lab) | SQL Server 2022 Developer (`sqlserver`) | Instancia objetivo controlada con `QD_Lab` (`1 000 000` de filas y escenarios `ESC01`–`ESC06`) para verificación y pruebas de laboratorio | No forma parte del despliegue en producción |

Las dependencias internas de la solución .NET son estrictamente unidireccionales (`Domain ← Application ← Infrastructure ← Api / Worker`) y están verificadas de forma continua mediante 21 pruebas arquitectónicas en `QueryDoctor.ArchitectureTests` (`NetArchTest`, incluyendo verificación IL contra `IgnoreQueryFilters`). El motor de reglas y el validador operan como bibliotecas puras sin E/S.

### 12.2 Segmentación de red (invariante)

| Servicio | Redes Compose | Salida permitida |
| --- | --- | --- |
| `web` | `frontend` | Ninguna (puerto publicado en Compose actual: `127.0.0.1:8080`; publicación productiva sobre `443` TLS pendiente de configuración de certificados) |
| `api` | `frontend`, `backend` | Ninguna |
| `worker` | `backend`, `targets` | Solo bases objetivo SQL Server / Azure SQL |
| `ai` | `backend`, `egress-ai` | Solo el dominio del perfil de LLM activo |
| `db` | `backend` | Ninguna |
| `sqlserver` (lab) | `targets` | Ninguna |

El control de salida por dominio del servicio de IA no es nativo en Docker Compose; en producción debe reforzarse mediante firewall del host o proxy de salida y se declara como control parcial (`RA-05`).

### 12.3 Modelo de datos

El repositorio en PostgreSQL se organiza en **cinco esquemas**, uno por frontera de permisos, todos ya **implementados y versionados en 11 migraciones de EF Core** (desde `Inicial` hasta `AuditoriaUsuarioRelacionado`):

- **`core`:** `connection`, `capability_profile`, `query_text`, `query`, `plan`, `runtime_stat` e `ingestion_run` (con clave natural idempotente en `runtime_stat`, índices sobre `query_hash` y `query_plan_hash`, cifrado por campo en credenciales, SQL y XML, y almacenamiento de `query_plan` como `text` para evitar el límite de 128 niveles de `xml`).
- **`analysis`:** `rule_catalog`, `threshold_set`, `analysis_run`, `finding` y `health_score`.
- **`ai`:** `explanation`, `claim`, `claim_reference`, `kb_fragment` (soporte `vector`) y `kb_causal_relation` (sembradas por migración con fragmentos curados para `REG-01`–`REG-04` y `REG-11`).
- **`audit`:** `trace_entry` y `audit_event`, sin claves foráneas hacia `core` para sobrevivir a la purga de retención, protegidas por un *trigger* de base de datos que rechaza `UPDATE`, `DELETE` y `TRUNCATE`, y encadenadas mediante hash SHA-256 serializado bajo `pg_advisory_xact_lock`. *(Nota técnica: `statement_log` opera actualmente mediante escritor en memoria `InMemoryStatementLogWriter`, y la creación de un rol de aplicación sin privilegios de dueño sobre `audit.*` queda registrada como deuda técnica `T4`).*
- **`ops`:** tablas de ASP.NET Core Identity, `user_connection` (asignaciones DBA → Desarrollador) y tablas propias de Hangfire.

**\[INSERTAR FIGURA 12.4: Diagrama entidad-relación completo del modelo de datos, `docs/arquitectura/MODELO_DATOS_v0.2.md`.\]**

## 13. Tecnologías adoptadas

| Componente | Tecnología implementada | Justificación |
| --- | --- | --- |
| Frontend | HTML5, CSS y JavaScript nativo servidos por Nginx | Eliminó complejidad de *build*; renderiza toda salida externa con `textContent` bajo CSP estricta (`script-src 'self'`, `RNF-SEG-07`) |
| Backend | ASP.NET Core sobre .NET 10 (LTS), EF Core, Identity, Hangfire, Serilog | Ecosistema oficial para `Microsoft.Data.SqlClient` y `ScriptDom`; trabajos en segundo plano con reintentos; autenticación con denegación por defecto |
| Base de datos | PostgreSQL 16 con `pgvector` | Ver `ADR-03`: licencia libre, soporte vectorial para evolución futura, sin límites de tamaño/cómputo |
| Infraestructura | Docker Compose con cuatro redes (`frontend`, `backend`, `targets`, `egress-ai`), Docker secrets y GitHub Actions | Despliegue reproducible (`RNF-FLE-01`); segmentación de red como control estructural de seguridad |
| IA / analítica | Python 3.12, FastAPI, Pydantic, adaptador LLM multi-perfil (OpenRouter/Gemma, Google Gemini 2.5 Flash `gemini_free` con `responseSchema`, Ollama local) | Validación estricta de esquema JSON en salida; cambio de proveedor mediante variable de entorno sin tocar el núcleo .NET |
| Integraciones SQL | Query Store (TDS con TLS 1.2+), `ScriptDom` (`Microsoft.SqlServer.TransactSql.ScriptDom`, `TSql170Parser`) | Fuente nativa de telemetría de solo lectura; analizador léxico oficial que cubre hasta SQL Server 2022 y Azure SQL, sin expresiones regulares |
| Control de versiones y calidad | Git y GitHub (flujo por ramas y PR con *squash*), `dotnet format`, Roslyn analyzers (`TreatWarningsAsErrors`), `NetArchTest` | Trazabilidad del desarrollo conforme a `WORKFLOW.md` y `GUIA_GIT.md` |
| Pruebas automatizadas | xUnit, `NetArchTest`, `Testcontainers.PostgreSql`, `Microsoft.AspNetCore.Mvc.Testing`, `node:test`, `pytest` | Cobertura unitaria, arquitectónica, de integración contra PostgreSQL real, de inyección en Python y de laboratorio contra SQL Server (`LabTests`) |
| Corpus de evaluación | Escenarios sembrados (`QD_Lab`: `ESC01`–`ESC06`), planes `.sqlplan` versionados y carga de referencia TPC-H | Planes realistas y resultados esperados reproducibles (JOB pospuesto según `S0-13` / `SRS §5.3`) |

La selección del proveedor definitivo de pago y su versión congelada para operación productiva permanece documentada en `DA-04`; el MVP se valida con datos de laboratorio bajo los perfiles gratuitos configurables (`openrouter_free` y `gemini_free`).

## 14. Metodología

### 14.1 Enfoque de desarrollo

Se adoptó un enfoque ágil que combina la planificación por iteraciones de Scrum (Schwaber y Sutherland, 2020) con un tablero Kanban en GitHub Projects (Backlog, En progreso, En revisión, Terminado). Los sprints son de una semana para ajustarse al calendario académico, con revisión de sprint frente a los criterios de aceptación y retrospectiva. Cada tarea del plan (S0-xx a S3-xx) declara módulo, prioridad MoSCoW, requisitos, criterio «hecho cuando», responsable, tamaño (S, M o L), dependencias y la regla de recorte si el tiempo no alcanza.

### 14.2 Etapas del proyecto

| Etapa | Periodo | Actividades principales | Resultado |
| --- | --- | --- | --- |
| Inicio y organización | 31 ago. – 6 sep. | Conformación del equipo, roles, repositorio y plan inicial | Repositorio Git estructurado |
| Comprensión del problema | 7 – 14 sep. | Planteamiento, justificación, objetivos, alcance preliminar y antecedentes | Avance 1 |
| Estado del arte y requisitos | 15 – 28 sep. | Análisis documental de Query Store, SRS según ISO/IEC/IEEE 29148, historias de usuario y casos de uso | SRS v1.0 (Avance 2) |
| Diseño y experiencia de usuario | 29 sep. – 12 oct. | Prototipo navegable, modelo de datos inicial y decisiones tecnológicas; respuesta a la revisión del evaluador | Avance 3 y adenda v1.1 |
| Arquitectura y primera versión funcional | 13 – 26 oct. | Documento de arquitectura (C4, ADR), modelo de datos y plan de sprints | Avance 4 |
| Desarrollo e integración | 27 oct. – 2 nov. | Sprint 1: de SQL Server real a hallazgos en la interfaz | Avance 5 |
| Terminación, pruebas y validación | 3 – 9 nov. | Sprint 2: score, detalle y explicación trazada; pruebas | Avance 6 |
| Correcciones y feria | 10 – 12 nov. | Ajustes, pendón, presentación y demostración | Pendón y presentación |
| Cierre | 13 – 15 nov. | Evaluación final, informe del laboratorio y documento completo | Entrega final |

El plan de sprints se ejecuta en cuatro iteraciones: Sprint 0 (31 ago – 28 oct, investigación, SRS, adenda y arquitectura), Sprint 1 (29 oct – 4 nov, rebanada 1), Sprint 2 (5 – 11 nov, rebanadas 2 y 3) y Sprint 3 (12 – 15 nov, evaluación y cierre).

### 14.3 Pipeline de desarrollo y control de calidad

Cada cambio sigue el flujo definido en `WORKFLOW.md`: planificar una tarea con sus criterios de aceptación, construir (primero la prueba que expresa el criterio), verificar localmente (compilación con advertencias como error, pruebas, formato), auditar (análisis estático con CodeQL, analizadores, escaneo de secretos y de dependencias vulnerables, y revisión por ejes por una sesión distinta de la que escribió el código), abrir un pull request con qué, por qué y evidencia, y fusionar con CI en verde y aprobación humana. La rama `main` está protegida; el único método de fusión es *squash* (`GUIA_GIT.md`). El estado vigente se mantiene en `docs/HANDOFF.md` y cada cierre de sprint se marca con una etiqueta de Git.

Se empleó un asistente de IA de código como herramienta de apoyo, con invariantes escritos en el repositorio, y el equipo conservó la revisión humana obligatoria de cada cambio (véase la declaración de uso de IA).

### 14.4 Estrategia de validación

La estrategia por niveles (unitarias, integración, sistema, aceptación, seguridad, rendimiento y usabilidad) y los protocolos empíricos del motor y de la IA se describen en el SRS (capítulo 6) y se resumen en la sección 15.

### 14.5 Criterio de ajuste de alcance

Si el esfuerzo estimado excede la capacidad, el recorte sigue un orden fijo (SRS 5.3): primero lo *Podría*; luego lo *Debería* no crítico (comparación de planes, vista gráfica, historial del score, caché, README probado por un externo, corrida con perfil de pago); después la simplificación de requisitos *Debe* sin eliminarlos (base de conocimiento solo de reglas *Debe*, aceptación solo en SQL Server 2022, series como tabla); y **nunca** la restricción de solo lectura, la trazabilidad, el principio de que la IA solo explica evidencia del motor ni los arneses de evaluación.

## 15. Validación de la solución

### 15.1 Protocolo de validación del motor determinístico (OBJ-6)

1. **Escenarios sembrados:** planes generados deliberadamente para contener cada antipatrón del catálogo, con resultado esperado conocido por construcción; miden la exhaustividad. El laboratorio dispone de siete escenarios (ESC01 a ESC06 y ESC04B) y 16 planes de referencia.
2. **Cargas de referencia:** consultas TPC-H (Transaction Processing Performance Council, s. f.) y, si el calendario lo permite, Join Order Benchmark (Leis et al., 2015), ejecutadas con Query Store activo. Por riesgo de alcance, el plan recomienda usar solo TPC-H (S0-13).
3. **Anotación experta:** un especialista anota los hallazgos esperados sobre una muestra; mide la precisión.
4. **Métricas:** precisión = VP / (VP + FP) y exhaustividad = VP / (VP + FN) por regla (RNF-FUN-01).
5. **Reproducibilidad:** todo el protocolo se automatiza en un arnés ejecutable por comando (RF-EVA-01).

### 15.2 Protocolo de evaluación del componente de IA

1. **Conjunto de evaluación:** al menos treinta consultas con hallazgos confirmados, incluidos casos adversariales (comentarios con instrucciones engañosas y literales sembrados).
2. **Métricas automáticas (RF-EVA-02):** porcentaje de afirmaciones con evidencia válida, porcentaje sin sustento y concordancia de valores numéricos, inspiradas en las métricas de fidelidad de RAGAS (Es et al., 2023).
3. **Auditoría manual:** al menos treinta explicaciones revisadas por dos revisores independientes; se reporta el coeficiente kappa de Cohen (Cohen, 1960) y la tasa de falsos pasantes del validador (RNF-FUN-03).
4. **Condición de control:** el mismo modelo recibe solo el texto SQL, sin hallazgos ni contexto; la diferencia en la tasa de afirmaciones sin sustento frente a la condición completa es la evidencia del aporte del enfoque híbrido (Zhou et al., 2023b).
5. **Lenguaje de las conclusiones:** los resultados se reportan como tasas medidas sobre un conjunto explícito, nunca como ausencia absoluta de errores.
6. **Perfil del proveedor:** el informe declara con qué perfil de LLM se obtuvo cada número, porque otro endpoint puede servir el modelo con otra cuantización (Arquitectura, sección 8.3).

### 15.3 Matriz de indicadores

Las metas † son provisionales y se calibran con el laboratorio (DA-06). La **situación inicial** corresponde a la ausencia de una herramienta equivalente: no existe línea base cuantitativa propia y, por honestidad metodológica, no se declara una.

| Objetivo / resultado | Indicador | Situación inicial | Meta | Método de medición |
| --- | --- | --- | --- | --- |
| OBJ-1 Ingesta segura | Sentencias distintas de SELECT en el registro | N/A (nueva plataforma) | 0 | Revisión de `statement_log` (CA-HU15.2) y prueba con cuenta de solo `VIEW DATABASE STATE` |
| OBJ-1 Bajo impacto | CPU de la instancia y esperas de bloqueo durante la ingesta | Sin medición previa | ≤ 5 % y 0 † | Extended Events en instancia de laboratorio |
| OBJ-1 Fiabilidad | Duplicados o pérdida tras corte de red | N/A | 0 | Prueba que corta la conexión a mitad de la ingesta |
| OBJ-2 Precisión | Precisión por regla *Debe* | Sin medición previa | ≥ 90 % † | Arnés del motor con anotación experta |
| OBJ-2 Exhaustividad | Exhaustividad por regla *Debe* | Sin medición previa | ≥ 80 % † | Arnés del motor con escenarios sembrados |
| OBJ-2 Determinismo | Hallazgos distintos ante misma entrada | N/A | 0 | Prueba de determinismo |
| OBJ-3 Priorización | Reproducibilidad del score y coherencia con el especialista | N/A | Score idéntico; coincidencia con el especialista en las consultas prioritarias (umbral de acuerdo por ratificar en `S1-20`) | Pruebas unitarias y contraste experto (RF-EVA-03) |
| OBJ-4 Verificabilidad | Explicaciones no validadas presentadas como verificadas | N/A | 0 | Inspección y pruebas del validador |
| OBJ-4 Fidelidad | Explicaciones que superan el validador al primer intento; concordancia numérica | Condición de control (sin contexto) | ≥ 90 % y 100 % † | Arnés de IA y auditoría manual |
| OBJ-4 Privacidad | Literales sembrados en solicitudes al proveedor | N/A | 0 | Suite de literales sembrados |
| OBJ-4 Robustez | Casos de inyección que alteran el formato o evaden el validador | N/A | 0 de 10 o más | Suite de inyección |
| OBJ-5 Rendimiento del tablero | Latencia p95 del listado y del detalle con 5 000 consultas | N/A | ≤ 2 s y ≤ 3 s † | Pruebas de carga con datos sintéticos (`S3-07`) |
| OBJ-5 Usabilidad | Participantes que identifican la consulta prioritaria y su causa; SUS | N/A | ≥ 80 % en ≤ 3 min; SUS ≥ 68 † | Prueba con 5 o más desarrolladores (Nielsen, 1993; Brooke, 1996) |
| OBJ-6 Reproducibilidad | Instalación por una persona ajena al equipo | N/A | ≤ 60 min † | Prueba con evaluador externo |

### 15.4 Resultados obtenidos

> **[POR COMPLETAR al cierre del Sprint 3.]** En esta subsección se consolidarán los valores medidos de: (i) precisión y exhaustividad por regla sobre escenarios sembrados y carga TPC-H (`S2-14`, `S3-01`); (ii) métricas automáticas del servicio de IA frente a la condición de control sin contexto (`S3-02`); (iii) auditoría manual por dos revisores independientes con coeficiente kappa de Cohen y tasa de falsos pasantes (`S3-03`); (iv) contraste con especialista externo (`S3-04`); (v) latencia y carga con 5 000 consultas (`S3-07`); y (vi) prueba de usabilidad (`RNF-INT-01`). Cada cifra deberá declarar el perfil de LLM utilizado y remitir al informe versionado del laboratorio (`RF-EVA-04`).

| Indicador | Meta | Resultado medido | Cumple |
| --- | --- | --- | --- |
| Precisión de REG-01 a REG-04 | ≥ 90 % † | \[POR COMPLETAR\] | \[POR COMPLETAR\] |
| Exhaustividad de REG-01 a REG-04 | ≥ 80 % † | \[POR COMPLETAR\] | \[POR COMPLETAR\] |
| Afirmaciones con evidencia válida (condición completa) | ≥ 90 % † | \[POR COMPLETAR\] | \[POR COMPLETAR\] |
| Afirmaciones sin sustento (completa frente a control) | Diferencia a reportar | \[POR COMPLETAR\] | \[POR COMPLETAR\] |
| Literales en solicitudes al proveedor | 0 | `0` en suite automatizada (`S2-04`/`S2-05`); \[POR COMPLETAR en conjunto `S3-02`\] | Parcial (`S2-04`/`S2-05`: Sí) |
| Latencia p95 del listado (5 000 consultas) | ≤ 2 s † | \[POR COMPLETAR\] | \[POR COMPLETAR\] |
| SUS | ≥ 68 † | \[POR COMPLETAR\] | \[POR COMPLETAR\] |

## 16. Consideraciones de seguridad, privacidad y ética

### 16.1 Modelo de seguridad

La seguridad se diseñó por fronteras de confianza, cada una con un control técnico y una prueba que lo verifica, y se mapeó contra OWASP Top 10:2025 y OWASP Top 10 for LLM Applications 2025 (OWASP Foundation, 2025a) en `docs/OWASP_TOP10.md`.

| Frontera | Amenaza | Control | Verificación |
| --- | --- | --- | --- |
| F1. Plataforma → base objetivo | Escritura accidental o maliciosa en producción | Un único componente accede a la base; sentencias constantes de lista blanca, sin SQL dinámico; cuenta de privilegio mínimo creada por el DBA; TLS 1.2 o superior con validación de certificado (IETF, 2008, 2018) | Registro de sentencias solo SELECT; prueba con cuenta que solo tiene `VIEW DATABASE STATE` |
| F2. Núcleo → servicio de IA → LLM | Fuga de literales o datos personales (LLM02) | Enmascarado léxico con ScriptDom, sin expresiones regulares ni opción para desactivarlo; si el lexer falla, solo se envía el contexto de hallazgos | Suite de literales sembrados: 0 ocurrencias |
| F3. LLM → usuario | Inyección de instrucciones (LLM01), desinformación (LLM09), salida insegura (LLM05) | Datos no confiables delimitados, esquema JSON obligatorio, validador fuera del servicio de IA, render como texto y CSP estricta | Suite de 10 o más casos de inyección; tasa de falsos pasantes |
| F4. Usuario → plataforma | Acceso no autorizado | Autenticación obligatoria, RBAC con denegación por defecto, filtro por conexión en la capa de datos, bloqueo por intentos fallidos | Pruebas de autorización por endpoint y rol |

Los secretos se montan como Docker secrets; las credenciales, el texto SQL, los planes y las afirmaciones de explicación se cifran por campo con AES-256-GCM; ningún registro operativo almacena texto SQL (únicamente identificadores, estados, conteos y métricas de enmascarado; `RNF-SEG-09`); y los argumentos de los trabajos de Hangfire contienen solo identificadores, dado que el panel administrativo los persiste sin cifrar. El endpoint `/auth/login` cuenta además con límite de tasa (`AddRateLimiter` con política fija configurable y respuesta HTTP `429`), permaneciendo documentadas como endurecimientos previos a producción la restricción de hosts contra SSRF en la red `targets`, un rol de PostgreSQL sin privilegios de propietario sobre `audit.*` (`T4`) y la terminación TLS en el puerto `443` de Nginx (`docs/OWASP_TOP10.md`).

### 16.2 Protección de datos personales

El texto de una consulta SQL puede contener datos personales de forma incidental (por ejemplo, un número de documento en un predicado). Conforme a la Ley Estatutaria 1581 de 2012 (Congreso de la República de Colombia, 2012), el sistema aplica el principio de minimización: sustituye literales por marcadores tipados antes de que cualquier contexto salga del perímetro de confianza, ejecuta purga automática programada y borrado bajo demanda por el DBA (`RNF-SEG-10` / `S2-13`, mediante el trabajo `retention` y `DELETE /queries/{id}/evidence` o `/connections/{id}/evidence`, reflejado como evidencia expirada en la API), y mantiene en `audit.*` una traza libre de texto SQL que solo registra identificadores y hashes, conciliando así la privacidad de los datos con la verificabilidad histórica.

### 16.3 Uso responsable de la IA

- **Transparencia:** cada afirmación enlaza a los hallazgos y nodos del plan que la sustentan, diferenciando explícitamente entre `TRAZADA` y `NO_VERIFICADA`.
- **Supervisión humana:** toda recomendación se presenta como una hipótesis técnica a evaluar por el DBA, acompañada de un aviso persistente en la interfaz y sin sentencias listas para ejecución directa, mitigando el sesgo de automatización (Parasuraman y Manzey, 2010).
- **Veracidad:** la causalidad solo se admite a partir de relaciones curadas en la base de conocimiento; las afirmaciones no cubiertas por evidencia curada quedan catalogadas como `NO_VERIFICADA`.
- **Candado de datos:** un perfil de proveedor que puede retener o reutilizar datos (`openrouter_free`, `gemini_free`) solo inicia en entornos `dev` o `lab` mediante habilitación explícita; con `APP_ENV=prod`, `security_lock.py` impide el arranque del servicio de IA.
- **Riesgo residual declarado:** los nombres de tablas y columnas viajan al LLM sin seudonimizar (`DA-13`), riesgo aceptado explícitamente porque en laboratorio los esquemas empleados son sintéticos y públicos (`QD_Lab` y TPC-H).
- **Propiedad intelectual y licencias:** se emplean componentes con licencia abierta o gratuita; el SQL Server de laboratorio utiliza la edición Developer, restringida exclusivamente a desarrollo y pruebas (`ADR-03`).

## 17. Viabilidad del proyecto

### 17.1 Viabilidad técnica

Alta para el MVP. El equipo cubre la totalidad de la pila tecnológica (ingesta, motor determinístico, servicio de IA e interfaz web) y la fuente de datos se encuentra documentada y verificada empíricamente sobre SQL Server 2022. Los riesgos técnicos principales —curva de aprendizaje de Showplan XML y variabilidad de proveedores LLM externos— fueron mitigados mediante fixtures reales del laboratorio, enmascarado léxico con `ScriptDom`, adaptador multi-perfil, validación externa y degradación controlada.

### 17.2 Viabilidad económica

El desarrollo y la validación básica no requieren inversión en licencias: se emplean tecnologías abiertas o gratuitas (PostgreSQL, .NET 10, Python, Nginx/JavaScript nativo), SQL Server Developer en contenedor para el laboratorio, GitHub y los perfiles gratuitos de OpenRouter y Google Gemini (`gemini_free`) exclusivamente sobre datos sintéticos de laboratorio. El único costo variable eventual corresponde al uso de un perfil de pago del LLM sin retención de datos en un entorno productivo o en la corrida opcional `S3-10` (prioridad *Debería*). \[POR COMPLETAR: cálculo estimado del costo por explicación y mensual para el perfil de pago según tarifa vigente del modelo seleccionado.\] Cabe señalar que SQL Server 2016 finalizó su soporte extendido el 14 de julio de 2026, lo cual respalda mantenerlo en el nivel «Compatible sin pruebas» (`DA-12`).

### 17.3 Viabilidad operativa

La plataforma se despliega mediante Docker Compose, opera sobre la base monitoreada con una cuenta de privilegio mínimo documentada (`lab/sql/04-readonly-login.sql`), y no requiere agentes instalados en el servidor objetivo ni cambios en las aplicaciones cliente. La facilidad de adopción por parte de usuarios reales será evaluada mediante la prueba de usabilidad (`RNF-INT-01`, `RNF-INT-02`) y el contraste experto (`RF-EVA-03`).

### 17.4 Viabilidad temporal

El calendario de Proyecto de Ingeniería I (31 de agosto al 15 de noviembre de 2026) cubre la construcción y validación del MVP, difiriendo las capacidades empresariales hacia Proyecto de Ingeniería II. **Constituye el principal factor de riesgo:** tras integrar la Adenda v1.1, los requisitos de prioridad *Debe* ascienden al **64,9 %** del total, por encima del umbral heurístico del 60 % sugerido por DSDM (Clegg y Barker, 1994), y el plan de sprints estimó cargas superiores a la capacidad semanal en los Sprints 1 y 2. Dicha presión de cronograma se gestionó aplicando estrictamente el criterio de ajuste de alcance de la sección 14.5, priorizando el núcleo no recortable.

## 18. Riesgos

La probabilidad y el impacto son una valoración cualitativa del equipo (Alta, Media o Baja) que debe ratificarse con el tutor; los riesgos RIE provienen del SRS y los RA del documento de arquitectura.

| ID | Riesgo | Probabilidad | Impacto | Estrategia de mitigación |
| --- | --- | --- | --- | --- |
| RIE-02 | Sobrealcance respecto al calendario | Alta | Alto | MoSCoW, orden de recorte (SRS 5.3), reunión S1-20 para aval de recortes, seguimiento de carga por persona |
| RIE-01 | La evidencia por defecto de Query Store es insuficiente para algunas reglas | Media | Medio | Clases de evidencia; reglas *Debe* solo con evidencia estimada; verificación empírica en laboratorio |
| RIE-03 | Disponibilidad, costo o cuota del proveedor de IA | Alta | Medio | Degradación a hallazgos determinísticos (RF-IA-06), límite de tiempo y reintentos, límites de consumo, adaptador por perfiles |
| RIE-04 / LLM01 | Inyección de instrucciones en el texto SQL | Media | Alto | Delimitación de datos no confiables, esquema obligatorio, validador, saneamiento de salida y suite de 10 casos |
| RIE-05 / LLM02 | Fuga de datos personales al proveedor | Baja | Alto | Enmascarado léxico obligatorio, candado de entorno, retención configurable |
| RIE-06 | Sesgo de automatización | Media | Medio | Enlaces a evidencia, aviso persistente, recomendaciones como opciones |
| RIE-07 | Falta de datos o de especialistas para la validación | Media | Medio | Contraste experto como *Debería*; escenarios sembrados y TPC-H; N a ratificar (DA-10) |
| RIE-08 | Metas † mal calibradas | Media | Medio | Marca explícita y calibración con el laboratorio (S3-01); declarar metas como provisionales con la evidencia disponible |
| RIE-09 | Falsos positivos que erosionen la confianza | Media | Medio | Umbrales por regla, severidades, precisión medida por regla |
| RIE-10 | Curva de aprendizaje en Showplan XML | Media | Medio | Análisis documental, fixtures reales, catálogo versionado, revisión por pares |
| RA-01 | La curaduría de relaciones causales toma más tiempo | Alta | Alto | Curar primero REG-01 a REG-04; lo demás queda NO\_VERIFICADA |
| RA-02 | El validador numérico rechaza explicaciones correctas por formato | Media | Medio | El modelo cita valores por identificador; el núcleo formatea; tolerancia documentada |
| RA-03 | ScriptDom no tokeniza SQL truncado o inválido | Media | Medio | Enviar solo el contexto de hallazgos, nunca regex de respaldo |
| RA-05 | Restricción de salida por dominio no nativa en Compose | Alta | Bajo | Firewall del host o proxy; declarado control parcial |
| RA-07 | Query Store en modo AUTO omite consultas poco frecuentes | Media | Bajo | Mostrar el modo de captura y declararlo en el informe |

## 19. Plan de trabajo

El plan integra el calendario del curso con el plan de sprints. El detalle por tarea (57 tareas con responsable, tamaño, dependencia y regla de recorte) está en `docs/sprints/SPRINTS.md`.

| Actividad | Entregable | Responsable | Inicio | Fin |
| --- | --- | --- | --- | --- |
| Identificación y validación del problema | Propuesta (Avance 1) | Equipo | 31 ago. | 14 sep. |
| Levantamiento de requerimientos | SRS v1.0 y Adenda v1.1 definitiva (114 requisitos, HU-01 a HU-21, CU-01 a CU-07) | Equipo | 15 sep. | 14 nov. |
| Investigación y estado del arte | Antecedentes y análisis de Query Store (Apéndice A) | Joel | 7 sep. | 28 sep. |
| Diseño de la solución y adenda | Adenda SRS v1.1 y prototipo navegable | Equipo y Daniel | 29 sep. | 26 oct. |
| Arquitectura | Documento de arquitectura v0.1 (C4, 11 ADR) y modelo de datos v0.2 | Equipo | 13 oct. | 28 oct. |
| Sprint 1: laboratorio, ingesta y motor base | Compose, cifrado, gateway, laboratorio, parser, REG-01/02/04, SPA base | Según tarea S1-xx | 29 oct. | 4 nov. |
| Sprint 2: score, explicación trazada y RBAC | Score, validador, enmascarado, traza, base de conocimiento, tablero | Según tarea S2-xx | 5 nov. | 11 nov. |
| Sprint 3: evaluación y cierre | Arneses, informe del laboratorio, pruebas de aceptación y rendimiento | Según tarea S3-xx | 12 nov. | 15 nov. |
| Documentación | Documento final, README, registro de decisiones | Equipo | Continuo | 15 nov. |
| Pendón y presentación | Pendón impreso, diapositivas, demostración | Daniel y equipo | 10 nov. | 12 nov. |

**\[INSERTAR FIGURA 19.1: Tablero Kanban y diagrama de Gantt.\]**

## 20. Entregables de Proyecto de Ingeniería I

La siguiente tabla relaciona los entregables exigidos por la plantilla y por la guía del tutor con la evidencia concreta del proyecto.

| N.º | Entregable exigido | Evidencia en el proyecto | Estado |
| --- | --- | --- | --- |
| 1 | Documento de formulación del proyecto | Avance 1 y este documento | Entregado |
| 2 | Problema identificado y sustentado con evidencia | Secciones 3 y 4; Apéndice A del SRS | Entregado; evidencia primaria pendiente (sección 4.1) |
| 3 | Objetivos y alcance aprobados | Secciones 6 y 7; SRS cap. 1 y 2 | Entregado |
| 4 | Estado del arte y antecedentes | Sección 3 | Entregado |
| 5 | Requerimientos formales | SRS v1.0 y Adenda v1.1 definitiva (114 requisitos en `docs/SRS.tex`) | Entregado |
| 6 | Análisis de alternativas | Sección 11; ADR-01 a ADR-11 aceptados | Entregado |
| 7 | Arquitectura de software | Documento de arquitectura v0.1 (`docs/arquitectura/ARQUITECTURA_v0.1.md`); modelo de datos v0.2 | Entregado (requiere sincronizar ajustes de S1/S2 en `ARQUITECTURA_v0.1.md`) |
| 8 | Selección tecnológica justificada | Sección 13 | Entregado; versión/proveedor de pago definitivo abierto en `DA-04` |
| 9 | Estrategia de validación | Sección 15; SRS cap. 6 | Entregado |
| 10 | Análisis de viabilidad | Sección 17 | Entregado; cálculo de tarifa del perfil de pago pendiente |
| 11 | Matriz de riesgos | Sección 18 | Entregado |
| 12 | Plan de trabajo para Proyecto de Ingeniería II | Sección 22.3 | Entregado |
| 13 | Prototipo y software funcional | Prototipo navegable (Avance 3); plataforma funcional en `src/`, `ai-service/`, `web/` y `lab/` (corte pre-Sprint 3 sobre base `543af12`) | Entregado (cierre operativo, UI/UX y evaluación en Sprint 3) |

### 20.1 Estado de implementación (corte pre-Sprint 3: 15 de noviembre de 2026)

Al término del Sprint 2 y su fase de estabilización previa al Sprint 3 se encuentra implementado el flujo funcional completo de extremo a punta (registro de conexión → ingesta periódica desde Query Store → análisis determinístico → cálculo de score → enmascarado léxico → explicación con IA → validación determinística → traza encadenada → retención configurable → visualización segura por roles), respaldado por **778 pruebas unitarias**, **21 pruebas de arquitectura** (`NetArchTest`), **114 pruebas de integración** contra PostgreSQL real (`Testcontainers.PostgreSql`), **68 pruebas en Python** (`ai-service` `pytest`) y **3 pruebas de vista frontend** (`node:test`) en verde, además de verificación de formato limpia (`dotnet format`) y validación de composición (`docker compose config`).

| Sprint / Módulo | Alcance y componentes | Estado al 15-nov | Evidencia y observaciones técnicas |
| --- | --- | --- | --- |
| **Sprint 1 — Base, Seguridad y Datos** (`S1-01`–`S1-06`, `S1-08`) | Solución .NET 10, CI (`.NET`, `web` y `ai-service`), `Docker Compose` segmentado, 5 esquemas en 11 migraciones EF Core, `SecretProtector` AES-256-GCM, Identity + bloqueo + cambio obligatorio, `QueryStoreGateway` (`sys.all_columns`) y `NetArchTest` | **Implementado** | Integrado en la línea base con los tres flujos de prueba en `.github/workflows/ci.yml`. Pendiente separar un rol de PostgreSQL restringido para `audit.*` (`T4`) y mitigación SSRF en `targets`. |
| **Sprint 1 — Laboratorio y Conectividad** (`S1-07`, `S1-09`–`S1-11`) | `QD_Lab` (SQL Server 2022), verificación `F1`–`F8`, 16 fixtures `.sqlplan`, `CapabilityProbe`, ingesta incremental por marca de agua con bloqueo consultivo en PostgreSQL e ingesta periódica automática en Hangfire (`HangfireRecurringIngestionScheduler`) | **Implementado** | Verificado con `RecurringIngestionTests` (12 casos). Pendiente ejecutar la prueba real de corte de red frente a SQL Server (`RNF-FIA-01`). |
| **Sprint 1 y 2 — Motor y Score** (`S1-12`–`S1-14`, `S2-01`, `S2-02`) | `ShowplanXmlParser` tolerante a *Dispatcher* y anidamiento >128, catálogo `1.0.0` (`REG-01`, `REG-02`, `REG-03`, `REG-04`, `REG-11`), `HealthScoreCalculator` e historial | **Implementado** | Cobertura unitaria completa sobre planes reales del laboratorio; `REG-05`–`REG-10` excluidas conforme a `SRS §5.3`. |
| **Sprint 2 — IA, Enmascarado, Validador y KB** (`S1-17`–`S1-18`, `S2-03`–`S2-05`, `S2-07`–`S2-08`, `#64`) | `LiteralMasker` (`TSql170Parser`, incluyendo atributos XML de Showplan), caché SHA-256, servicio FastAPI multi-perfil (`openrouter_*`, `gemini_free` con esquema JSON estricto, `ollama_local`, `mock`), candado `APP_ENV=prod`, delimitación anti-inyección (`v1.1`), `ExplanationValidator` determinístico y KB curada para reglas *Debe* | **Implementado** | Verificado en pruebas unitarias, 68 casos `pytest` en `ai-service/tests/` (incluyendo inyección) y petición en vivo sobre `gemini_free` (`gemini-2.5-flash`). |
| **Sprint 2 — Trazabilidad, RBAC y Retención** (`S2-06`, `S2-12`, `S2-13`) | `audit.trace_entry` y `audit.audit_event` encadenadas con SHA-256 y *trigger* anti-alteración; rol `Desarrollador` con filtro global `ConnectionAccess` en `/connections`, `/queries` y `/plans`; alta `POST /users` (`D6`); *rate limit* `429` en `/auth/login` (`A07`); y purga automática/manual de retención (trabajo `retention`, `DELETE …/evidence`) | **Implementado** | Suite `QueryDoctor.IntegrationTests` compilando sin advertencias y pasando **114/114** contra PostgreSQL efímero. |
| **Sprint 1 y 2 — Interfaz Web** (`S1-15`–`S1-16`, `S2-09`–`S2-11`) | Páginas `/login`, `/cambiar-contrasena`, Conexiones (`/`), Consultas (`/consultas`) y Detalle (`/consulta`) con series por plan, árbol jerárquico resaltado y explicación solo texto con enlaces a evidencia | **Implementado** (con rediseño UI/UX) | Programado rediseño visual + asistente de primera conexión (*wizard*) previo a la prueba SUS, junto con controles web para ingesta manual, asignaciones, alta de usuarios, `/trace`, `/audit` y TLS `:443`. |
| **Cierre y Evaluación — Sprint 3** (`S1-20`, `S2-14`, `S3-01`–`S3-10`) | Ratificación tutor (`S1-20`), arneses reproducibles (`S2-14`, `S3-02`), calibración (`S3-01`), auditoría manual con kappa (`S3-03`), contraste experto (`S3-04`), informe final (`S3-05`), aceptación multiplataforma (`S3-06`) y rendimiento 5 000 consultas (`S3-07`) | **Pendiente** | Programado para la fase de cierre (Sprint 3). |

## 21. Resultados esperados

| Resultado esperado | Indicador de cumplimiento | Evidencia |
| --- | --- | --- |
| Plataforma desplegable con un solo comando | `docker compose up` funcional en entorno limpio; README con instalación ≤ 60 min † | Prueba con una persona ajena al equipo |
| Garantía de solo lectura | 0 sentencias no SELECT; todas las funciones con `VIEW DATABASE STATE` | Registro de sentencias y pruebas de arquitectura |
| Motor determinístico validado | Precisión ≥ 90 % y exhaustividad ≥ 80 % por regla *Debe* † | Arnés del motor e informe del laboratorio |
| Priorización por Query Health Score | Score reproducible con desglose; listado ≤ 2 s (p95) con 5 000 consultas † | Pruebas unitarias y de carga |
| Explicaciones trazables y verificadas | 0 explicaciones no validadas como verificadas; tasa de afirmaciones sustentadas medida frente a la condición de control | Arnés de IA, auditoría manual con kappa |
| Protección de datos hacia el proveedor | 0 literales sembrados; 0 de 10 o más inyecciones exitosas | Suites de seguridad |
| Tablero funcional | Historias HU-04, 05, 07, 08, 10, 11, 13 demostradas con la consulta sembrada | Demostración en vivo y criterios de aceptación |
| Documentación reproducible | SRS, adenda, arquitectura, modelo de datos, OWASP, README y registro de decisiones actualizados | Repositorio y etiquetas por sprint |

## 22. Impacto esperado y conclusiones

### 22.1 Impacto esperado

- **Tecnológico:** un patrón de integración entre motor determinístico e IA generativa en el que el modelo no puede certificar su propia salida, con metodología de evaluación reproducible (afirmaciones sustentadas, concordancia numérica, falsos pasantes).
- **Operativo:** reducción esperada del tiempo de identificación de la causa de una consulta problemática y menor dependencia de un especialista senior. El efecto se mide con la prueba de usabilidad (≤ 3 minutos, †), no se asume.
- **Económico:** potencial de reducir el sobreaprovisionamiento derivado de consultas ineficientes no detectadas. No se cuantifica en este MVP porque la plataforma no aplica cambios; su medición corresponde a trabajo futuro.
- **Académico:** un conjunto de artefactos verificables (arneses, escenarios sembrados, fixtures de planes reales) reutilizable por futuros equipos.
- **Ético y social:** democratización del conocimiento de ajuste de consultas hacia perfiles de desarrollo, con transparencia sobre qué está verificado y qué no.

### 22.2 Conclusiones

> **[POR COMPLETAR al cierre del Sprint 3 con base en la sección 15.4.]** La redacción definitiva de este apartado consolidará una conclusión evaluada por cada objetivo específico:

1. **OBJ-1 (ingesta segura):** síntesis de los resultados medidos en control de solo lectura (`statement_log` y permisos mínimos), idempotencia ante reintentos y sobrecarga en la instancia monitoreada.
2. **OBJ-2 (motor determinístico):** precisión y exhaustividad medidas por regla (`REG-01`–`REG-04`) sobre escenarios sembrados y carga TPC-H, junto con el análisis de falsos positivos/negativos.
3. **OBJ-3 (*Query Health Score*):** reproducibilidad determinística y grado de concordancia respecto al ordenamiento de prioridad emitido por el especialista.
4. **OBJ-4 (IA trazable):** tasa de afirmaciones sustentadas frente a la condición de control sin contexto, concordancia entre revisores (kappa de Cohen) y tasa de falsos pasantes, declarando explícitamente el perfil y modelo de LLM empleado.
5. **OBJ-5 (tablero de control):** cobertura de historias de usuario demostradas de extremo a punta, latencias medidas bajo volumen sintético y resultados de usabilidad (`SUS`).
6. **OBJ-6 (validación empírica):** alcance efectivo de las pruebas realizadas, rigor del método reproducible y límites experimentales observados.

Desde el punto de vista arquitectónico e ingenieril, la implementación completada hasta el Sprint 2 confirma la viabilidad técnica de desacoplar el diagnóstico determinístico de la explicación generativa mediante un contrato estricto de afirmaciones, enmascarado léxico obligatorio fuera del perímetro de IA y validación determinística independiente. Los resultados cuantitativos finales deberán interpretarse dentro de sus límites declarados: conjunto de evaluación acotado, datos sintéticos de laboratorio, ejecución con perfiles gratuitos de LLM en entorno no productivo, ausencia de seudonimización de nombres de objetos (`DA-13`) y ausencia de línea base organizacional previa.

### 22.3 Trabajo futuro (Proyecto de Ingeniería II)

Escalamiento hacia un producto de grado empresarial, con las condiciones de retoma definidas en el documento de arquitectura (sección 13):

- Seudonimización reversible de nombres de objetos (alias T1, T1.C3) y comparación de su efecto en la calidad de las explicaciones con el arnés de IA.
- Delegación de la autenticación en OIDC y soporte multitenant.
- Soporte multimotor (PostgreSQL y MySQL) mediante la interfaz `ITelemetrySource`.
- Integración con pipelines de CI/CD y optimización continua de costos en la nube.
- Captura opcional del plan real cuando el DBA habilite `LAST_QUERY_PLAN_STATS` (RF-ING-12).
- Módulo de acción asistida (por ejemplo, forzar plan) solo con una nueva versión del SRS que lo autorice, desactivado por defecto, con credencial propia, doble confirmación humana, auditoría y reversión.
- Migración a un repositorio de análisis dedicado si el volumen supera lo que PostgreSQL maneja bien.

## 23. Referencias bibliográficas

Agile Business Consortium. (s. f.). *DSDM agile project framework: MoSCoW prioritisation*.

Brooke, J. (1996). SUS: A «quick and dirty» usability scale. En P. W. Jordan, B. Thomas, B. A. Weerdmeester y I. L. McClelland (Eds.), *Usability evaluation in industry* (pp. 189–194). Taylor & Francis.

Brown, S. (s. f.). *The C4 model for visualising software architecture*. https://c4model.com

Clegg, D. y Barker, R. (1994). *Case method fast-track: A RAD approach*. Addison-Wesley.

Cockburn, A. (2001). *Writing effective use cases*. Addison-Wesley.

Cohen, J. (1960). A coefficient of agreement for nominal scales. *Educational and Psychological Measurement, 20*(1), 37–46.

Congreso de la República de Colombia. (2012). *Ley Estatutaria 1581 de 2012, por la cual se dictan disposiciones generales para la protección de datos personales*.

Cohn, M. (2004). *User stories applied: For agile software development*. Addison-Wesley.

Ding, B., Chaudhuri, S. y Narasayya, V. (2024). ML-powered index tuning: An overview of recent progress and open challenges. *ACM SIGMOD Record*. https://dl.acm.org/doi/10.1145/3641832.3641836

Es, S., James, J., Espinosa-Anke, L. y Schockaert, S. (2023). *RAGAS: Automated evaluation of retrieval augmented generation* (arXiv:2309.15217). arXiv.

IDERA. (2025, 22 de diciembre). *Top 5 SQL Server performance monitoring tools*. https://www.idera.com/blogs/top-performance-monitoring-solutions/

Dierks, T. y Rescorla, E. (2008). *RFC 5246: The Transport Layer Security (TLS) Protocol Version 1.2*. Internet Engineering Task Force; Rescorla, E. (2018). *RFC 8446: The Transport Layer Security (TLS) Protocol Version 1.3*. Internet Engineering Task Force. https://www.rfc-editor.org/rfc/rfc8446

International Organization for Standardization, International Electrotechnical Commission y Institute of Electrical and Electronics Engineers. (2018). *ISO/IEC/IEEE 29148:2018. Systems and software engineering – Life cycle processes – Requirements engineering*.

International Organization for Standardization y International Electrotechnical Commission. (2023). *ISO/IEC 25010:2023. Systems and software engineering – Systems and software Quality Requirements and Evaluation (SQuaRE) – Product quality model*.

International Organization for Standardization, International Electrotechnical Commission y Institute of Electrical and Electronics Engineers. (2022). *ISO/IEC/IEEE 42010:2022. Software, systems and enterprise – Architecture description*.

Ji, Z., Lee, N., Frieske, R., Yu, T., Su, D., Xu, Y., Ishii, E., Bang, Y., Madotto, A. y Fung, P. (2023). Survey of hallucination in natural language generation. *ACM Computing Surveys, 55*(12), 1–38.

Leis, V., Gubichev, A., Mirchev, A., Boncz, P., Kemper, A. y Neumann, T. (2015). How good are query optimizers, really? *Proceedings of the VLDB Endowment, 9*(3), 204–215.

Lewis, P., Perez, E., Piktus, A., Petroni, F., Karpukhin, V., Goyal, N., Küttler, H., Lewis, M., Yih, W., Rocktäschel, T., Riedel, S. y Kiela, D. (2020). Retrieval-augmented generation for knowledge-intensive NLP tasks. *Advances in Neural Information Processing Systems, 33*.

Mavin, A., Wilkinson, P., Harwood, A. y Novak, M. (2009). Easy approach to requirements syntax (EARS). *Proceedings of the 17th IEEE International Requirements Engineering Conference*.

Microsoft. (s. f.-a). *How Query Store collects data*. Microsoft Learn. https://learn.microsoft.com/en-us/sql/relational-databases/performance/how-query-store-collects-data

Microsoft. (s. f.-b). *sys.query\_store\_plan (Transact-SQL)*. Microsoft Learn. https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-plan-transact-sql

Microsoft. (s. f.-c). *sys.query\_store\_runtime\_stats (Transact-SQL)*. Microsoft Learn. https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-runtime-stats-transact-sql

Microsoft. (s. f.-d). *Display the estimated execution plan; Display an actual execution plan*. Microsoft Learn. https://learn.microsoft.com/sql/relational-databases/performance/display-the-estimated-execution-plan

Microsoft. (s. f.-e). *sys.dm\_exec\_query\_plan\_stats (Transact-SQL)*. Microsoft Learn. https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-views/sys-dm-exec-query-plan-stats-transact-sql

Microsoft. (s. f.-f). *Manage the Query Store*. Microsoft Learn. https://learn.microsoft.com/sql/relational-databases/performance/manage-the-query-store

Microsoft. (2022, 18 de agosto). *Query Store is enabled by default in SQL Server 2022*. SQL Server Blog. https://www.microsoft.com/en-us/sql-server/blog/2022/08/18/query-store-is-enabled-by-default-in-sql-server-2022/

Microsoft. (2026, 6 de mayo). *Query plan regressions got you down? Here's how Automatic Plan Correction can turn it around*. Microsoft Community Hub. https://techcommunity.microsoft.com/blog/sqlserver/query-plan-regressions-got-you-down-heres-how-automatic-plan-correction-can-turn/4515152

Microsoft SQL Server Team. (s. f.). *SQL Server 2016 – Added information on TempDB spill events – Showplan*. https://blogs.msdn.microsoft.com/sql\_server\_team/sql-server-2016-added-information-on-tempdb-spill-events-showplan

Nielsen, J. (1993). *Usability engineering*. Morgan Kaufmann.

North, D. (2006). Introducing BDD. *Better Software*.

Nygard, M. (2011, 15 de noviembre). *Documenting architecture decisions*. Cognitect Blog. https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions

OWASP Foundation. (2025a). *OWASP Top 10 for LLM Applications 2025*. https://genai.owasp.org/llm-top-10/

OWASP Foundation. (2025b). *OWASP Application Security Verification Standard (ASVS) 5.0.0*. https://github.com/OWASP/ASVS

Parasuraman, R. y Manzey, D. H. (2010). Complacency and bias in human use of automation: An attentional integration. *Human Factors, 52*(3), 381–410.

Radi, M. (2023). Predicting SQL query quality using machine learning techniques. *Journal of Theoretical and Applied Information Technology*. https://www.researchgate.net/publication/370897768\_PREDICTING\_SQL\_QUERY\_QUALITY\_USING\_MACHINE\_LEARNING\_TECHNIQUES

Redgate Simple Talk. (s. f.). *Checking the plan cache warnings for a SQL Server database*. https://www.red-gate.com/simple-talk/databases/sql-server/t-sql-programming-sql-server/checking-the-plan-cache-warnings-for-a-sql-server-database/

Schwaber, K. y Sutherland, J. (2020). *The Scrum guide*. https://scrumguides.org

SQLYARD. (2025, 29 de agosto). *SolarWinds Database Performance Analyzer vs. Redgate SQL Monitor*. https://sqlyard.com/2025/08/29/solarwinds-database-performance-analyzer-vs-redgate-sql-monitor/

Transaction Processing Performance Council. (s. f.). *TPC benchmark H (decision support) standard specification*. https://www.tpc.org/tpch/

World Wide Web Consortium. (2023). *Web Content Accessibility Guidelines (WCAG) 2.2*. https://www.w3.org/TR/WCAG22/

Yoon, D. Y., Niu, N. y Mozafari, B. (2016). DBSherlock: A performance diagnostic tool for transactional databases. En *Proceedings of the 2016 International Conference on Management of Data (SIGMOD '16)* (pp. 1599–1614). ACM. https://doi.org/10.1145/2882903.2915218

Zhang, L., Jia, T., Jia, M., Wu, Y., Liu, A., Yang, Y., Wu, Z., Hu, X., Yu, P. S. y Li, Y. (2025). A survey of AIOps in the era of large language models. *ACM Computing Surveys*. https://dl.acm.org/doi/10.1145/3746635

Zhao, X., Zhou, X. y Li, G. (2023). Automatic database index tuning: A survey. *IEEE Transactions on Knowledge and Data Engineering*.

Zhou, X., Li, G. y Liu, Z. (2023a). *LLM as DBA* (arXiv:2308.05481). arXiv. https://arxiv.org/abs/2308.05481

Zhou, X., Li, G., Sun, Z., Liu, Z., Chen, W., Wu, J., Liu, J., Feng, R. y Zeng, G. (2023b). *D-Bot: Database diagnosis system using large language models* (arXiv:2312.01454). arXiv. https://arxiv.org/abs/2312.01454

## 24. Anexos

| Anexo | Contenido | Ubicación en el repositorio |
| --- | --- | --- |
| A | Especificación de Requisitos de Software (SRS) v1.1 definitivo: 114 requisitos, historias (`HU-01`–`HU-21`), casos de uso (`CU-01`–`CU-07`), trazabilidad, apéndice de sustento de Query Store y contrato de hallazgos | `docs/SRS.tex` y `docs/SRS_Query_Doctor_AI (2).pdf` |
| B | Documento de Arquitectura de Software v0.1: vistas C4, ADR-01 a ADR-11, seguridad, despliegue y riesgos | `docs/arquitectura/ARQUITECTURA_v0.1.md` |
| C | Modelo de datos v0.2 (esquemas `core`, `analysis`, `ai`, `audit` y `ops`) | `docs/arquitectura/MODELO_DATOS_v0.2.md` |
| D | Plan de sprints (57 tareas) y resumen operativo de arquitectura | `docs/sprints/SPRINTS.md`, `docs/ARCHITECTURE_PLAN.md` |
| E | Mapeo OWASP Top 10:2025 y OWASP Top 10 for LLM Applications 2025 | `docs/OWASP_TOP10.md` |
| F | Flujo de trabajo, guía de Git y estado de relevo | `docs/WORKFLOW.md`, `docs/GUIA_GIT.md`, `docs/HANDOFF.md` |
| G | Informe del laboratorio: verificación empírica de `F1`–`F8` y `H1`–`H5` en SQL Server 2022, escenarios sembrados y 16 fixtures `.sqlplan` | `lab/results/S1-07-verificacion.md`, `tests/fixtures/plans/` |
| H | Informe final de evaluación del motor y de la IA (`RF-EVA-04`), tabla de revisión manual y acta del especialista | \[POR COMPLETAR en Sprint 3\] |
| I | Interfaz web implementada y diagramas fuente de arquitectura | `web/nginx/`, `docs/arquitectura/media/image1.png`, `docs/arquitectura/media/image2.png` |
| J | Propuesta inicial (Avance 1) | Entregable institucional presentado el 14 de septiembre de 2026 |

## Declaración sobre uso de inteligencia artificial

De acuerdo con las directrices académicas de la asignatura, el equipo declara haber utilizado asistentes basados en inteligencia artificial generativa como apoyo para la estructuración editorial de documentos, el análisis de código y la generación asistida de borradores y casos de prueba, bajo las reglas explícitas consignadas en `CLAUDE.md` y el flujo de verificación de `docs/WORKFLOW.md` (planificación, pruebas de criterios de aceptación, verificación local, auditoría por ejes en sesión independiente y revisión humana obligatoria previa a cada *pull request*). Ningún fragmento generado automáticamente fue aceptado sin inspección técnica ni presentado como evidencia empírica: todos los resultados técnicos y métricas reportadas en este documento provienen exclusivamente de compilaciones, suites de pruebas y ejecuciones reproducibles registradas en el repositorio. Cada integrante del equipo asume plena responsabilidad por la exactitud de la solución y está en capacidad de sustentar individualmente cualquier componente o capítulo.

**Coherencia del proyecto:** Problema → Evidencia → Objetivos → Solución → Metodología → Validación → Resultados esperados.
