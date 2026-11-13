# Plan de sprints — Query Doctor AI

> Exportado de `QueryDoctor_Sprints_1.xlsx` (2026-11-04). La hoja de cálculo es la fuente; regenerar este archivo cuando cambie.


## Resumen

- Query Doctor AI · Plan de sprints
- Base: SRS v1.0 + Adenda v1.1 + Documento de arquitectura v0.1. Sprints de 1 semana. Tamaños: S ≈ 0,5 día · M ≈ 1,5 días · L ≈ 3 días (supuestos editables en C14:C16).
- Sprint · Fechas · Meta · Tareas · Hechas · % avance
- Sprint 0 (hecho) · 31-ago – 28-oct · Investigación, SRS, adenda y arquitectura · 13 · 10 · 0.77
- Sprint 1 · 29-oct – 4-nov · Rebanada 1: de SQL Server real a hallazgos en la interfaz · 16 · 6 · 0.38
- Sprint 2 · 5-nov – 11-nov · Rebanadas 2 y 3: score, detalle y explicación trazada · 16 · 1 · 0.06
- Sprint 3 (tentativo) · 12-nov – 15-nov · Evaluación, informe del laboratorio y cierre · 12 · 0 · 0
- Carga estimada por persona (días de trabajo)
- Compara la carga de cada sprint con unos 5 días hábiles por persona. Si supera la capacidad, recorten primero lo Podría y luego lo Debería.
- Tamaño · Días
- S · 0.5
- M · 1.5
- L · 3
- Persona · Sprint 1 (días) · Capacidad S1 · Sprint 2 (días) · Capacidad S2 · Sprint 3 (días) · Capacidad S3
- Álvaro · 6 · 4 · 6 · 5 · 2.5 · 3
- Joel · 6 · 4 · 6.5 · 5 · 2.5 · 3
- Maykol · 5 · 4 · 6.5 · 5 · 3.5 · 3
- Daniel · 5.5 · 4 · 6 · 5 · 3.5 · 3
- Total equipo · 22.5 · 16 · 25 · 20 · 12 · 12
- Exceso sobre capacidad · 0.41 · 0.25 · 0
- Rojo: la persona tiene más trabajo estimado que días hábiles en ese sprint. Capacidades en azul: días hábiles por persona (S1 tiene el festivo del 2-nov; S3 cuenta 12 y 13-nov + 1 día de fin de semana). Edítenlas si trabajan fines de semana.
- Orden de recorte si no alcanza (SRS 5.3)
- 1. Todo lo Podría (ya está fuera del plan: exportar PDF, umbrales desde la UI, comparación antes/después, plan real).
- 2. Lo Debería: REG-05/06/07/08/10, vista gráfica del plan, comparación de planes, historial del score, caché de explicaciones, README probado por externo, corrida con perfil de pago.
- 3. Simplificar Debe sin eliminarlos: base de conocimiento solo de reglas Debe; aceptación solo en SQL Server 2022; series como tabla; demo del Sprint 1 por Swagger.
- 4. Nunca: solo lectura y sus controles, trazabilidad de hallazgos, IA que solo explica lo que el motor demostró, arneses de evaluación e informe del laboratorio.

## Sprint 0 (hecho)

31-ago al 28-oct-2026 · Investigación y especificación. Lo tachado está terminado; lo amarillo es investigación que sigue pendiente y ya tiene tarea asignada.

| ID | Tarea | Módulo | MoSCoW | Requisitos | Hecho cuando (criterio de aceptación) | Responsable | Tamaño | Estado | Depende de | Si no alcanza |
|---|---|---|---|---|---|---|---|---|---|---|
| S0-01 | Propuesta del proyecto (Avance 1): problema, objetivos OBJ-1…OBJ-6, alcance | Gestión | Debe | OBJ-1…OBJ-6 | Entregada y aprobada para continuar | Equipo | — | Hecho |  |  |
| S0-02 | Análisis documental de Query Store: qué evidencia existe, con qué permisos y límites (hallazgos F1–F8, clases de evidencia) | Investigación | Debe | SRS 2.8, Apéndice A | Documentado en el Apéndice A con fuentes oficiales de Microsoft | Joel | — | Hecho |  |  |
| S0-03 | SRS v1.0 según ISO/IEC/IEEE 29148: 103 requisitos, historias, casos de uso, trazabilidad | Gestión | Debe | Todo el SRS | Versión formal entregada el 27-sep-2026 | Equipo | — | Hecho |  |  |
| S0-04 | Respuesta a la revisión del evaluador: Adenda SRS v1.1 (seguridad obligatoria, TRAZADA vs validada, fórmula del score, matriz de soporte) | Gestión | Debe | Adenda v1.1 | Borrador de adenda con los 7 cambios pedidos | Equipo | — | Hecho |  |  |
| S0-05 | Documento de arquitectura v0.1: C4 (contexto, contenedores, componentes), 11 ADR aceptados, vistas dinámicas, seguridad, despliegue | Arquitectura | Debe | DA-05, RNF-MAN-03 | Publicado; ADR-01…ADR-11 en estado Aceptada | Equipo | — | Hecho |  |  |
| S0-06 | Decisiones abiertas de arquitectura cerradas: DA-03, DA-04, DA-05, DA-09, DA-12, DA-13 | Arquitectura | Debe | DA-03/04/05/09/12/13 | Registradas en la Sección 14.2 del documento de arquitectura | Equipo | — | Hecho |  |  |
| S0-07 | Modelo de datos preliminar v0.2 (UML de clases, 5 esquemas) revisado contra el SRS v1.1 | Arquitectura | Debe | RI-04, SRS 4.4 | Figura 7.1 del documento de arquitectura | Equipo | — | Hecho |  |  |
| S0-08 | Prototipo del adaptador de LLM con perfiles (OpenRouter pago/gratis, Gemini, Ollama) y candado de datos | IA | Debe | RNF-SEG-05, RES-06, ADR-10 | 13 pruebas en verde; falta integrarlo al repositorio (S1-18) | Maykol | — | Hecho |  |  |
| S0-09 | Diagramas: contexto, capas, casos de uso y ER (SRS); contenedores, explicación y componentes (arquitectura) | Arquitectura | Debe | SRS Fig. 2.1–4.1 | Incluidos en SRS y documento de arquitectura | Equipo | — | Hecho |  |  |
| S0-10 | Verificación EMPÍRICA de los hallazgos F1–F8 en un SQL Server real (protocolo del Apéndice A) | Investigación | Debe | DA-07, RF-EVA-04 | → Sprint 1 (S1-07) | Joel | — | Hecho | S1-07 |  |
| S0-11 | Ratificar con el tutor la fórmula del score, las metas † y el especialista revisor | Gestión | Debe | DA-01, DA-06, DA-10 | → Sprint 1 (S1-20) | Daniel | — | Pendiente | S1-20 |  |
| S0-12 | Calibrar umbrales por defecto con datos del laboratorio | Investigación | Debe | DA-02 | → Sprint 3 (necesita el laboratorio y el arnés) | Joel | — | Pendiente | S3-01 |  |
| S0-13 | Estrategia de carga del Join Order Benchmark en SQL Server | Investigación | Debería | DA-11 | → Sprint 3 (S2-14); se recomienda posponer JOB y usar solo TPC-H | Joel | — | Pendiente | S2-14 |  |

## Sprint 1

29-oct al 4-nov-2026 · 4 días hábiles (2-nov festivo) · Meta: de una base SQL Server real a hallazgos visibles (rebanada 1). Demo: registrar el SQL Server del laboratorio, ingerir y ver REG-01 y REG-04 sobre la consulta sembrada.

| ID | Tarea | Módulo | MoSCoW | Requisitos | Hecho cuando (criterio de aceptación) | Responsable | Tamaño | Estado | Depende de | Si no alcanza |
|---|---|---|---|---|---|---|---|---|---|---|
| S1-01 | Crear repositorio y solución .NET 10: Domain, Application, Infrastructure, Api, Worker; carpeta ai-service; CI con build + pruebas en cada PR | Infraestructura | Debe | RNF-MAN-03, RNF-MAN-04, ADR-01 | Un PR de prueba dispara la CI y la rama main exige revisión + CI en verde | Álvaro | M | Hecho |  | No recortable (núcleo de la demo) |
| S1-02 | Docker Compose: web, api, worker, ai, db (pgvector) y SQL Server 2022 de laboratorio; .env.example; secretos como Docker secrets | Infraestructura | Debe | RNF-FLE-01, RNF-SEG-04 | `docker compose up` levanta todo en una máquina limpia; ningún secreto en archivos versionados | Maykol | M | Hecho | S1-01 | Dejar el contenedor ai vacío hasta el Sprint 2 |
| S1-04 | Migraciones EF Core de los esquemas core y ops (incluye tablas de ASP.NET Core Identity) con las restricciones UK del modelo v0.2 | Persistencia | Debe | RF-ING-05, RF-ING-07 | Migración aplica en limpio; insertar duplicados por clave natural falla | Daniel | M | Hecho | S1-01 | No recortable (núcleo de la demo) |
| S1-05 | SecretProtector: cifrado por campo AES-256-GCM con clave maestra externa | Seguridad | Debe | RNF-SEG-04, RNF-SEG-14 | Pruebas: cifrar/descifrar, clave errónea falla, el texto plano nunca llega a la base ni a los logs | Maykol | M | Hecho | S1-01 | Cifrar solo credenciales ahora; SQL y planes en el Sprint 2 |
| S1-06 | Identity mínimo: DBA inicial desde secreto, login, cambio obligatorio de contraseña y bloqueo por intentos | Seguridad | Debe | RNF-SEG-11, RNF-SEG-13, HU-01 | Toda ruta sin sesión responde 401; 5 intentos fallidos bloquean la cuenta | Daniel | S | Por hacer | S1-04 | Roles y asignación de conexiones ya están en S2-12 |
| S1-07 | Laboratorio: script de SQL Server 2022 con Query Store y escenarios sembrados (conversión implícita, Key Lookup, índice faltante, sesgo de cardinalidad, sort con spill); ejecutar el protocolo del Apéndice A | Investigación | Debe | DA-07, RF-EVA-04, Apéndice A | Tabla con qué aparece y qué no en el plan guardado por Query Store para cada escenario; confirma o corrige F1–F8 | Joel | M | Hecho | S1-02 | Empezar solo con conversión implícita y Key Lookup |
| S1-08 | QueryStoreGateway: solo sentencias constantes de la lista blanca, TLS con validación de certificado, LOCK_TIMEOUT corto; registro en statement_log | Ingesta | Debe | RNF-SEG-01, RNF-SEG-03, RF-TRZ-04, HU-15 | Prueba: statement_log solo contiene SELECT; una conexión sin TLS válido se rechaza | Maykol | M | Hecho | S1-04 | No recortable (núcleo de la demo) |
| S1-09 | Registro de conexión + CapabilityProbe: versión, edición, permisos, estado de Query Store, nivel de soporte y funciones omitidas | Conexiones | Debe | RF-CFG-01…05, RF-CFG-07, RNF-COM-04 | CA-HU01.1–01.3 y CA-HU02.1–02.3 en verde contra el SQL Server de laboratorio | Álvaro | M | Por hacer | S1-08 | Dejar la advertencia de permisos de escritura (RF-CFG-05, Debería) para el Sprint 2 |
| S1-10 | Hangfire: cliente en la API, servidor en el Worker, panel /hangfire solo DBA; trabajo recurrente por conexión según interval_length_minutes + 5 min | Infraestructura | Debe | ADR-04, DA-03 | La pestaña Servers muestra solo el Worker; un trabajo encolado se ejecuta tras reiniciar el Worker | Maykol | S | Por hacer | S1-06 | Solo ingesta manual en el Sprint 1; la recurrente en el Sprint 2 |
| S1-11 | IngestionService incremental: marca de agua, agregación ponderada por ejecuciones, Top-N, upsert idempotente, bloqueo por conexión, planes no analizables | Ingesta | Debe | RF-ING-01, 02, 04, 05, 06, 07, 08, 09; RNF-FIA-01 | CA-HU03.1–03.4: dos ingestas seguidas no duplican; un corte de red retoma sin pérdida | Álvaro | L | Por hacer | S1-09, S1-10 | Dejar la prueba de corte de red (RNF-FIA-01) para el Sprint 2 |
| S1-12 | PlanParser: Showplan XML con XmlReader (sin DTD, con límite de tamaño), árbol tipado de operadores, ignora lo desconocido | Motor | Debe | RF-MOT-01, RNF-COM-02, RF-MOT-16 | Parsea los planes del laboratorio; un XML inválido produce REG-11 sin detener el lote | Joel | M | Por hacer | S1-07 | No recortable (núcleo de la demo) |
| S1-13 | Catálogo de reglas versionado (IRule), analysis_run, migración del esquema analysis, contrato JSON del hallazgo | Motor | Debe | RF-MOT-02, 03, 14, 15, 17 | Mismo plan + misma versión + mismos umbrales = mismos hallazgos (prueba de determinismo) | Joel | M | Por hacer | S1-04 | No recortable (núcleo de la demo) |
| S1-14 | Reglas REG-01 (conversión implícita), REG-02 (índice faltante + advertencia de costo de escritura) y REG-04 (Key Lookup contextual) | Motor | Debe | RF-MOT-04, 06, 07; RNF-MAN-02 | Cada regla con un plan positivo y uno negativo en la suite; CA-HU05.1, 05.2 y CA-HU06.1–06.3 | Joel | M | Por hacer | S1-12, S1-13 | REG-02 pasa al Sprint 2 |
| S1-15 | SPA base React + TypeScript: login, layout, pantalla de conexiones (alta, estado de Query Store, nivel de soporte, última ingesta) | UI | Debe | RF-DASH-09, RF-CFG-07, HU-01, HU-02 | Un DBA registra una conexión y ve su estado sin usar la API a mano | Daniel | M | Por hacer | S1-06, S1-09 | Demo del Sprint 1 por Swagger; la pantalla pasa al Sprint 2 |
| S1-16 | Listado de consultas por porcentaje de consumo con sus hallazgos (todavía sin score) | UI | Debe | RF-DASH-01 (parcial), RF-SCO-04 (parcial) | La consulta sembrada del laboratorio aparece con REG-01 y REG-04 y su evidencia | Daniel | M | Por hacer | S1-11, S1-14 | Demo por Swagger; el listado pasa al Sprint 2 |
| S1-20 | Reunión con el tutor: ratificar fórmula del score, metas †, N del conjunto de evaluación y especialista; pedir aval para el recorte de alcance (SRS 5.3) | Gestión | Debe | DA-01, DA-06, DA-10, SRS 5.3 | Acta con lo decidido; DA-01, DA-06 y DA-10 actualizadas; recortes aprobados | Daniel | S | Por hacer |  | No recortable (núcleo de la demo) |

## Sprint 2

5 al 11-nov-2026 · 5 días hábiles · Meta: priorización y explicación trazada de punta a punta (rebanadas 2 y 3). Demo: la consulta sembrada aparece como Crítica, con su plan resaltado y una explicación TRAZADA que enlaza a su evidencia.

| ID | Tarea | Módulo | MoSCoW | Requisitos | Hecho cuando (criterio de aceptación) | Responsable | Tamaño | Estado | Depende de | Si no alcanza |
|---|---|---|---|---|---|---|---|---|---|---|
| S1-03 | Pruebas de arquitectura (NetArchTest): dominio sin EF Core, módulos sin acceso a internos ajenos, solo QueryStoreGateway usa SqlClient | Calidad | Debe | RNF-MAN-01, RNF-SEG-01, Arq. 13.1 | Las 3 pruebas pasan y cada una falla al romper su regla a propósito | Joel | S | Hecho | S1-01 | Solo la regla de SqlClient (evidencia de HU-15) |
| S1-17 | Curar la base de conocimiento: fragmentos y relaciones causa-efecto para REG-01 a REG-04 | IA | Debe | RF-IA-04, RF-IA-03 (c) | kb_fragment y kb_causal_relation cargados por migración, con fuente citada en cada fragmento | Maykol | M | Por hacer | S1-04 | No recortable (núcleo de la demo) |
| S1-18 | Servicio de IA: FastAPI con el adaptador de LLM integrado, prompt v1 y endpoint /explain con el esquema de afirmaciones | IA | Debe | RF-IA-02 (v1.1), ADR-02, ADR-10 | Con openrouter_free y un contexto de ejemplo devuelve JSON válido; el candado bloquea APP_ENV=prod | Maykol | M | Por hacer | S1-02 | No recortable (núcleo de la demo) |
| S1-19 | Integrar la adenda en el SRS v1.1 definitivo y verificar las celdas "A verificar" de la matriz de soporte | Gestión | Debe | Adenda v1.1 §9, §12 | SRS v1.1 con fecha, matriz sin celdas sin verificar y referencias cruzadas actualizadas | Maykol | M | Por hacer |  | Marcar como "No soportado" lo que no se alcance a verificar |
| S2-01 | REG-03 (recorrido completo contextual) con umbrales por defecto de la adenda | Motor | Debe | RF-MOT-05 (v1.1) | Un scan sin predicado sobre tabla pequeña NO se señala; uno con predicado y razón > 10 sí | Joel | M | Por hacer | S1-14 | No recortable (núcleo de la demo) |
| S2-02 | HealthScoreCalculator con la fórmula de la adenda; recálculo tras cada ingesta e historial | Score | Debe | RF-SCO-01, 02, 03, 05, 06 | Pruebas con el ejemplo calculado a mano; mismos datos = mismo score; parámetros guardados | Joel | M | Por hacer | S1-11, S1-14 | Historial (RF-SCO-05, Debería) al Sprint 3 |
| S2-03 | ExplanationValidator: criterios (a) ids, (b) números, (c) relación curada; estados TRAZADA / NO_VERIFICADA; etiqueta de indicio | IA (.NET) | Debe | RF-IA-03, 10, 12; RNF-FUN-02 | Suite con explicaciones correctas, con número alterado y con interpretación sin fragmento curado | Joel | L | Por hacer | S1-17 | No recortable (núcleo de la demo) |
| S2-04 | LiteralMasker con ScriptDom: SQL, comentarios, valores del plan y evidencia; sin opción para desactivarlo | Seguridad | Debe | RNF-SEG-05 (v1.1), CA-HU21.1 | Suite de literales sembrados: 0 ocurrencias en las solicitudes registradas | Álvaro | M | Por hacer | S1-12 | No recortable (núcleo de la demo) |
| S2-05 | ExplanationContextBuilder + hash canónico + caché; trabajo explain en Hangfire con degradación si la IA falla | IA (.NET) | Debe | RF-IA-01, 06, 07; RNF-FIA-02 | CA-HU09.2, 09.3, CA-HU12.1–12.2: sin hallazgos no se genera; con IA caída se ven los hallazgos y un aviso | Álvaro | M | Por hacer | S2-04, S1-18 | Caché (RF-IA-07, Debería) al Sprint 3 |
| S2-06 | Traza de solo adición encadenada por hash (trace_entry) y auditoría (audit_event) con inserción serializada | Trazabilidad | Debe | RF-TRZ-01, 02, 03, 05 (v1.1) | CA-HU14.1–14.3; la verificación de la cadena detecta una fila alterada a mano | Álvaro | M | Por hacer | S1-04 | Encadenado por hash al Sprint 3; solo adición sí ahora |
| S2-07 | Prompt con delimitación de datos no confiables + suite de 10 casos de inyección | IA | Debe | RNF-SEG-06, CA-HU21.2 | Ningún caso altera el formato ni evade el validador | Maykol | M | Por hacer | S1-18, S2-03 | No recortable (núcleo de la demo) |
| S2-08 | Ampliar la base de conocimiento a REG-03 y a las reglas que entren después | IA | Debe | RF-IA-04 | Cada regla implementada tiene al menos un fragmento curado | Maykol | S | Por hacer | S1-17 | Base solo con reglas Debe (SRS 5.3, punto 3) |
| S2-09 | Detalle de consulta: SQL, series de duración/CPU/lecturas por plan, hallazgos con evidencia, score con banda y desglose | UI | Debe | RF-DASH-02, 03; RF-SCO-06; HU-07, HU-08 | CA-HU07.1–07.2 y CA-HU08.1 demostrados con la consulta sembrada | Daniel | L | Por hacer | S2-02 | Series como tabla en vez de gráfico |
| S2-10 | Visor jerárquico del plan con nodos de hallazgos resaltados | UI | Debe | RF-DASH-04, HU-13 | CA-HU13.1–13.2 | Daniel | M | Por hacer | S1-12 | Árbol de texto indentado, sin vista gráfica (RF-DASH-07 es Debería) |
| S2-11 | Vista de explicación: afirmaciones con enlaces a evidencia, estado TRAZADA/NO_VERIFICADA, avisos persistentes, render solo texto | UI | Debe | RF-DASH-05, 06; RF-IA-10; RNF-SEG-07 | CA-HU10.1–10.3, CA-HU11.1–11.3 | Daniel | M | Por hacer | S2-03, S2-05 | No recortable (núcleo de la demo) |
| S2-12 | RBAC: rol Desarrollador y asignación de conexiones, filtrado en la capa de datos | Seguridad | Debe | RNF-SEG-12 | Un desarrollador no ve conexiones no asignadas ni por API directa | Álvaro | M | Por hacer | S1-06 | No recortable (núcleo de la demo) |

## Sprint 3 (tentativo)

12 al 15-nov-2026 · 2 días hábiles + fin de semana · Meta: evaluación y cierre. Tentativo: se ajusta el 11-nov según lo que quede del Sprint 2.

| ID | Tarea | Módulo | MoSCoW | Requisitos | Hecho cuando (criterio de aceptación) | Responsable | Tamaño | Estado | Depende de | Si no alcanza |
|---|---|---|---|---|---|---|---|---|---|---|
| S2-13 | Retención: borrado automático de texto SQL y planes al vencer el plazo y borrado a pedido del DBA | Seguridad | Debe | RNF-SEG-10 (v1.1) | Los datos vencidos desaparecen; la interfaz muestra "evidencia expirada" y la traza sigue verificable | Álvaro | S | Por hacer | S2-06 | Solo borrado a pedido del DBA |
| S2-14 | Arnés del motor (CLI): planes sembrados + TPC-H con hallazgos esperados anotados; precisión y exhaustividad por regla | Evaluación | Debe | RF-EVA-01, DA-11, HU-20 | CA-HU20.1: un comando reproduce la tabla de métricas por regla | Maykol | M | Por hacer | S1-07, S1-14 | Sin JOB; solo planes sembrados + TPC-H (SRS 5.3, punto 3) |
| S3-01 | Calibrar umbrales con los resultados del laboratorio y el arnés | Evaluación | Debe | DA-02, RF-MOT-14 | Umbrales por defecto actualizados y justificados en el informe | Joel | S | Por hacer | S2-14 | Declarar los umbrales como provisionales con la evidencia que haya |
| S3-02 | Arnés de IA: afirmaciones sustentadas, concordancia numérica, distribución por tipo; condición de control sin contexto | Evaluación | Debe | RF-EVA-02 (v1.1), SRS 6.3 | CA-HU20.2: un comando reproduce las métricas | Maykol | M | Por hacer | S2-03, S2-07 | No recortable (SRS 5.3, punto 4) |
| S3-03 | Auditoría manual de ≥30 explicaciones por dos revisores: kappa de Cohen y tasa de falsos pasantes | Evaluación | Debe | RNF-FUN-03 (v1.1), SRS 6.3 | Tabla de revisión y cálculo de kappa en el informe | Joel | M | Por hacer | S3-02 | No recortable (SRS 5.3, punto 4) |
| S3-04 | Contraste con el especialista sobre N consultas | Evaluación | Debe | RF-EVA-03 (v1.1) | Acta del especialista y comparación con hallazgos y explicaciones | Daniel | M | Por hacer | S1-20 | Reducir N a lo ratificado con el tutor |
| S3-05 | Informe del laboratorio como anexo versionado (protocolo, versiones, datos, resultados por regla) | Evaluación | Debe | RF-EVA-04 | Anexo publicado; ninguna afirmación de eficacia sin remitir a él | Daniel | M | Por hacer | S3-01, S3-03 | No recortable (RF-EVA-04) |
| S3-06 | Pruebas de aceptación contra las plataformas Probado | Calidad | Debe | RNF-COM-01 (v1.1) | Suite de aceptación en verde | Álvaro | M | Por hacer | S2-12 | Solo SQL Server 2022 (+ Azure SQL si hay acceso); ajustar la matriz (SRS 5.3, punto 3) |
| S3-07 | Pruebas de rendimiento: carga de la ingesta y latencia del tablero con 5 000 consultas | Calidad | Debe | RNF-RND-01, RNF-RND-02 | Mediciones registradas frente a las metas † | Álvaro | S | Por hacer | S2-09 |  |
| S3-08 | README de instalación probado por alguien ajeno al equipo | Calidad | Debería | RNF-FLE-01, RNF-MAN-03 | Instalación en ≤ 60 min siguiendo solo el README | Joel | S | Por hacer |  | Recortar primero (Debería) |
| S3-09 | Ensayo de la demo y documento resumen (estado, avances, respuesta a la retroalimentación) | Gestión | Debe | — | Demo cronometrada sin pasos manuales ocultos | Daniel | S | Por hacer |  |  |
| S3-10 | Corrida final del arnés de IA con el perfil de pago (si hay presupuesto) | Evaluación | Debería | ADR-10, Arq. 8.3 | Métricas reportadas con el perfil documentado del producto | Maykol | S | Por hacer | S3-02 | Recortar primero (Debería) |
