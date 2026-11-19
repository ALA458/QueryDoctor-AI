# Cuaderno de Contraste frente a Query Doctor AI (Etapa B) — `S3-04`

> **Instrucción:** Abre este cuaderno **después** de completar `planilla_etapa_a.csv`. Califica la concordancia en `planilla_etapa_b.csv`.

## CASO-01 (`TPCH_Q02`)

- **Query Health Score:** `15.09` / 100 | **Banda:** `CRITICA` | **Ranking de prioridad del sistema:** `#4` de 10

### 1. Hallazgos del motor determinístico (`S3-01` calibrado)
- **`F-4B024D546AD3` · `REG-02` (Índice faltante)** — Severidad: `BAJA` | Evidencia: `ESTIMADA` | Nodos: Nodo #10 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `equality_columns=P_SIZE`, `impact_pct=15.7729`, `include_columns=P_MFGR, P_TYPE`, `inequality_columns=`, `schema=dbo`, `table=PART`, `write_cost_warning=La sugerencia del optimizador no considera todos los índices existentes ni el costo de escritura (INSERT, UPDATE, DELETE) y mantenimiento; debe evaluarse manualmente por un DBA antes de aplicarse.`
- **`F-3C205B0D08E2` · `REG-02` (Índice faltante)** — Severidad: `MEDIA` | Evidencia: `ESTIMADA` | Nodos: Nodo #17 (Clustered Index Scan), Nodo #18 (Clustered Index Seek)
  - Evidencia extraída: `database=QD_TPCH`, `equality_columns=PS_SUPPKEY`, `impact_pct=48.9655`, `include_columns=PS_SUPPLYCOST`, `inequality_columns=`, `schema=dbo`, `table=PARTSUPP`, `write_cost_warning=La sugerencia del optimizador no considera todos los índices existentes ni el costo de escritura (INSERT, UPDATE, DELETE) y mantenimiento; debe evaluarse manualmente por un DBA antes de aplicarse.`
- **`F-A8FD598D053D` · `REG-03` (Lectura completa costosa (contextual))** — Severidad: `MEDIA` | Evidencia: `ESTIMADA` | Nodos: Nodo #10 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `estimated_rows=200.198`, `estimated_rows_read=20000`, `estimated_subtree_cost=0.248986`, `has_predicate=true`, `index=PK_PART`, `logical_op=Clustered Index Scan`, `min_estimated_rows_threshold=10000`, `min_predicate_volume_rows_threshold=1000`, `min_query_consumption_pct_threshold=1`, `physical_op=Clustered Index Scan`, `predicate=[QD_TPCH].[dbo].[PART].[P_SIZE] as [P].[P_SIZE]=(15) AND [QD_TPCH].[dbo].[PART].[P_TYPE] as [P].[P_TYPE] like '%STEEL'`, `rows_read_to_returned_ratio=99.9011`, `rows_read_to_returned_ratio_threshold=10`, `schema=dbo`, `table=PART`, `table_cardinality=20000`, `triggered_by=predicate_ratio_and_volume`, `volume_rows_evaluated=20000`
- **`F-C034FDD5EBAB` · `REG-03` (Lectura completa costosa (contextual))** — Severidad: `MEDIA` | Evidencia: `ESTIMADA` | Nodos: Nodo #17 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `estimated_rows=80000`, `estimated_rows_read=80000`, `estimated_subtree_cost=0.43943`, `has_predicate=false`, `index=PK_PARTSUPP`, `logical_op=Clustered Index Scan`, `min_estimated_rows_threshold=10000`, `min_predicate_volume_rows_threshold=1000`, `min_query_consumption_pct_threshold=1`, `physical_op=Clustered Index Scan`, `rows_read_to_returned_ratio=1`, `rows_read_to_returned_ratio_threshold=10`, `schema=dbo`, `table=PARTSUPP`, `table_cardinality=80000`, `triggered_by=volume_and_consumption`, `volume_rows_evaluated=80000`

### 2. Explicación generada por IA (Estado: `TRAZADA`)
- **Resumen:** Diagnóstico de rendimiento para la consulta TPCH_Q02, identificando escaneos costosos y oportunidades de indexación.

| # | Tipo | Afirmación | Evidencia (`finding_ids`) | Fragmento KB |
|---:|---|---|---|---|
| 1 | `OBSERVACION` | La consulta realiza un Clustered Index Scan en la tabla 'dbo.PART' (Nodo #10) que lee 20000 filas para retornar aproximadamente 200.198 filas estimadas, lo que representa una relación de lectura a retorno de 99.9011. | `F-A8FD598D053D` | `-` |
| 2 | `INTERPRETACION` | El Clustered Index Scan en la tabla 'dbo.PART' es costoso porque, a pesar de tener un predicado ([P].[P_SIZE]=(15) AND [P].[P_TYPE] like '%STEEL'), examina todas las páginas del índice agrupado y luego filtra las filas, lo que incrementa las lecturas lógicas y el consumo de recursos del buffer pool. | `F-A8FD598D053D` | `a1010003-0003-0000-0000-000000000001` |
| 3 | `RECOMENDACION` | Se sugiere evaluar la creación de un índice no agrupado en la tabla 'dbo.PART' sobre las columnas 'P_SIZE' y 'P_TYPE' para convertir el escaneo en una búsqueda más selectiva, o refactorizar el predicado 'P_TYPE LIKE @qd_str_3' si es posible para permitir un uso más eficiente del índice. | `F-A8FD598D053D` | `a1010003-0003-0000-0000-000000000002` |
| 4 | `OBSERVACION` | El optimizador de consultas ha identificado un índice faltante en la tabla 'dbo.PART' con columnas de igualdad 'P_SIZE' y columnas incluidas 'P_MFGR, P_TYPE', con un impacto estimado del 15.7729% en el costo del plan. | `F-4B024D546AD3` | `-` |
| 5 | `INTERPRETACION` | La sugerencia de índice faltante para la tabla 'dbo.PART' indica que el optimizador, durante su fase de optimización basada en costos, ha estimado un beneficio significativo si existiera un índice compuesto que cubriera las columnas 'P_SIZE', 'P_MFGR' y 'P_TYPE'. | `F-4B024D546AD3` | `a1010002-0002-0000-0000-000000000001` |
| 6 | `RECOMENDACION` | Se recomienda evaluar la creación de un índice no agrupado en 'dbo.PART' con 'P_SIZE' como columna clave y 'P_MFGR, P_TYPE' como columnas incluidas. Antes de implementarlo, se debe considerar el costo de escritura y mantenimiento, y si un índice existente podría ser modificado para incluir estas columnas. | `F-4B024D546AD3` | `a1010002-0002-0000-0000-000000000002` |
| 7 | `OBSERVACION` | Se detecta un Clustered Index Scan en la tabla 'dbo.PARTSUPP' (Nodo #17) que lee 80000 filas, con una cardinalidad de tabla de 80000, y sin predicado directo en este operador. | `F-C034FDD5EBAB` | `-` |
| 8 | `INTERPRETACION` | El Clustered Index Scan en 'dbo.PARTSUPP' es costoso porque se realiza una lectura completa de la tabla sin un predicado que restrinja las filas en el operador, lo que implica procesar todas las filas del objeto. | `F-C034FDD5EBAB` | `a1010003-0003-0000-0000-000000000001` |
| 9 | `RECOMENDACION` | Se sugiere revisar la lógica de la subconsulta para determinar si es posible aplicar un predicado más selectivo directamente en el Clustered Index Scan de 'dbo.PARTSUPP' o crear un índice no agrupado que soporte la condición de búsqueda. | `F-C034FDD5EBAB` | `a1010003-0003-0000-0000-000000000002` |
| 10 | `OBSERVACION` | El optimizador ha identificado un índice faltante en la tabla 'dbo.PARTSUPP' con columnas de igualdad 'PS_SUPPKEY' y columnas incluidas 'PS_SUPPLYCOST', con un impacto estimado del 48.9655% en el costo del plan. | `F-3C205B0D08E2` | `-` |
| 11 | `INTERPRETACION` | La sugerencia de índice faltante para la tabla 'dbo.PARTSUPP' indica que el optimizador ha estimado un beneficio sustancial si existiera un índice compuesto que cubriera las columnas 'PS_SUPPKEY' y 'PS_SUPPLYCOST', lo que podría mejorar el rendimiento de las operaciones de búsqueda y agregación en la subconsulta. | `F-3C205B0D08E2` | `a1010002-0002-0000-0000-000000000001` |
| 12 | `RECOMENDACION` | Se recomienda evaluar la creación de un índice no agrupado en 'dbo.PARTSUPP' con 'PS_SUPPKEY' como columna clave y 'PS_SUPPLYCOST' como columna incluida. Es crucial analizar el impacto en las operaciones de escritura y el mantenimiento general de los índices antes de su implementación. | `F-3C205B0D08E2` | `a1010002-0002-0000-0000-000000000002` |

---

## CASO-02 (`TPCH_Q06`)

- **Query Health Score:** `44.26` / 100 | **Banda:** `ATENCION` | **Ranking de prioridad del sistema:** `#8` de 10

### 1. Hallazgos del motor determinístico (`S3-01` calibrado)
- **`F-013F2E81F2F2` · `REG-02` (Índice faltante)** — Severidad: `ALTA` | Evidencia: `ESTIMADA` | Nodos: Nodo #6 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `equality_columns=`, `impact_pct=87.6214`, `include_columns=L_EXTENDEDPRICE`, `inequality_columns=L_QUANTITY, L_DISCOUNT, L_SHIPDATE`, `schema=dbo`, `table=LINEITEM`, `write_cost_warning=La sugerencia del optimizador no considera todos los índices existentes ni el costo de escritura (INSERT, UPDATE, DELETE) y mantenimiento; debe evaluarse manualmente por un DBA antes de aplicarse.`
- **`F-F4DE35F62A98` · `REG-03` (Lectura completa costosa (contextual))** — Severidad: `ALTA` | Evidencia: `ESTIMADA` | Nodos: Nodo #6 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `estimated_rows=39775.9`, `estimated_rows_read=600000`, `estimated_subtree_cost=6.55389`, `has_predicate=true`, `index=PK_LINEITEM`, `logical_op=Clustered Index Scan`, `min_estimated_rows_threshold=10000`, `min_predicate_volume_rows_threshold=1000`, `min_query_consumption_pct_threshold=1`, `physical_op=Clustered Index Scan`, `predicate=[QD_TPCH].[dbo].[LINEITEM].[L_SHIPDATE]>='1994-01-01' AND [QD_TPCH].[dbo].[LINEITEM].[L_SHIPDATE]<'1995-01-01' AND [QD_TPCH].[dbo].[LINEITEM].[L_DISCOUNT]>=(0.05) AND [QD_TPCH].[dbo].[LINEITEM].[L_DISCOUNT]<=(0.07) AND [QD_TPCH].[dbo].[LINEITEM].[L_QUANTITY]<(24.00)`, `rows_read_to_returned_ratio=15.0845`, `rows_read_to_returned_ratio_threshold=10`, `schema=dbo`, `table=LINEITEM`, `table_cardinality=600000`, `triggered_by=predicate_ratio_and_volume`, `volume_rows_evaluated=600000`

### 2. Explicación generada por IA (Estado: `TRAZADA`)
- **Resumen:** Diagnóstico de rendimiento para la consulta TPCH_Q06, identificando un escaneo completo costoso y la ausencia de un índice óptimo.

| # | Tipo | Afirmación | Evidencia (`finding_ids`) | Fragmento KB |
|---:|---|---|---|---|
| 1 | `OBSERVACION` | La consulta TPCH_Q06 realiza un Clustered Index Scan en la tabla dbo.LINEITEM (Nodo #6) que procesa 600,000 filas para retornar solo 1 fila, lo que representa un ratio de lectura a retorno de 15.08 a 1. | `F-F4DE35F62A98` | `-` |
| 2 | `OBSERVACION` | El Clustered Index Scan en el Nodo #6 tiene un predicado aplicado que filtra por las columnas L_SHIPDATE, L_DISCOUNT y L_QUANTITY. | `F-F4DE35F62A98` | `-` |
| 3 | `OBSERVACION` | El optimizador de SQL Server ha identificado la ausencia de un índice que podría mejorar significativamente el rendimiento de la consulta, con un impacto estimado del 87.62% sobre el costo del plan. | `F-013F2E81F2F2` | `-` |
| 4 | `INTERPRETACION` | El alto ratio de filas leídas a filas retornadas (15.08:1) en el Clustered Index Scan indica que el motor de base de datos está examinando una gran cantidad de datos que luego son descartados por el predicado, lo que es ineficiente y consume recursos innecesarios. | `F-F4DE35F62A98` | `a1010003-0003-0000-0000-000000000001` |
| 5 | `INTERPRETACION` | La sugerencia de índice faltante se genera porque el optimizador de costos estima que un índice compuesto sobre las columnas L_QUANTITY, L_DISCOUNT y L_SHIPDATE, incluyendo L_EXTENDEDPRICE, reduciría drásticamente el costo del plan al permitir una búsqueda más selectiva en lugar de un escaneo completo. | `F-013F2E81F2F2` | `a1010002-0002-0000-0000-000000000001` |
| 6 | `RECOMENDACION` | Se sugiere evaluar la creación de un índice no agrupado sobre las columnas L_SHIPDATE, L_DISCOUNT y L_QUANTITY, incluyendo L_EXTENDEDPRICE, para convertir el escaneo costoso en una búsqueda más eficiente. Es crucial analizar el impacto de este nuevo índice en las operaciones de escritura (INSERT, UPDATE, DELETE) antes de su implementación. | `F-013F2E81F2F2, F-F4DE35F62A98` | `a1010002-0002-0000-0000-000000000002` |
| 7 | `RECOMENDACION` | Considere refactorizar la consulta o crear un índice no agrupado selectivo sobre las columnas del predicado para evitar el escaneo completo del índice agrupado, lo que mejoraría la selectividad y reduciría las lecturas lógicas. | `F-F4DE35F62A98` | `a1010003-0003-0000-0000-000000000002` |

---

## CASO-03 (`SEEDED_REG01_SEEK_PLAN`)

- **Query Health Score:** `29.88` / 100 | **Banda:** `CRITICA` | **Ranking de prioridad del sistema:** `#6` de 10

### 1. Hallazgos del motor determinístico (`S3-01` calibrado)
- **`F-05F90BD50CCD` · `REG-01` (Conversión implícita que afecta el plan)** — Severidad: `ALTA` | Evidencia: `ESTIMADA` | Nodos: Nodo #2 (Index Scan)
  - Evidencia extraída: `convert_issue=Seek Plan`, `expression=CONVERT_IMPLICIT(nvarchar(20),[QD_Lab].[dbo].[Orders].[CustomerCode],0)=[@code]`, `index=IX_Orders_CustomerCode`, `table=Orders`

### 2. Explicación generada por IA (Estado: `TRAZADA`)
- **Resumen:** Diagnóstico de rendimiento para la consulta SEEDED_REG01_SEEK_PLAN, identificando una conversión implícita que degrada el plan de ejecución.

| # | Tipo | Afirmación | Evidencia (`finding_ids`) | Fragmento KB |
|---:|---|---|---|---|
| 1 | `OBSERVACION` | La consulta ha sido ejecutada 50 veces y representa el 2% del consumo total de la carga de trabajo, retornando un promedio de 120 filas por ejecución. | `F-05F90BD50CCD` | `-` |
| 2 | `OBSERVACION` | Se ha detectado una conversión implícita en la expresión de predicado 'CONVERT_IMPLICIT(nvarchar(20),[QD_Lab].[dbo].[Orders].[CustomerCode],0)=[@code]' en el Nodo #2 (Index Scan) que involucra la columna CustomerCode de la tabla dbo.Orders y el índice IX_Orders_CustomerCode. | `F-05F90BD50CCD` | `-` |
| 3 | `INTERPRETACION` | La conversión implícita detectada en la columna indexada CustomerCode provoca una pérdida de sargabilidad, lo que fuerza al optimizador de consultas a utilizar un Index Scan en lugar de un Index Seek más eficiente, resultando en una lectura estimada de 500 filas para retornar 120 filas. | `F-05F90BD50CCD` | `a1010001-0001-0000-0000-000000000001` |
| 4 | `RECOMENDACION` | Se sugiere revisar y alinear el tipo de datos del parámetro '@code' con el tipo de datos de la columna 'CustomerCode' en la tabla 'dbo.Orders' para eliminar la conversión implícita. Esto debería permitir que el optimizador de consultas utilice un Index Seek, mejorando significativamente el rendimiento. | `F-05F90BD50CCD` | `a1010001-0001-0000-0000-000000000002` |

---

## CASO-04 (`ESC04B_QS_1`)

- **Query Health Score:** `50` / 100 | **Banda:** `ATENCION` | **Ranking de prioridad del sistema:** `#9` de 10

### 1. Hallazgos del motor determinístico (`S3-01` calibrado)
- *El motor determinístico no emitió hallazgos del catálogo activo (`REG-01`..`REG-04`) para este plan.*

### 2. Explicación generada por IA (Estado: `OMITIDA_SIN_HALLAZGOS`)
- *Explicación omitida por diseño (`RF-IA-01`): no se generan afirmaciones cuando no existen hallazgos determinísticos.*

---

## CASO-05 (`ESC03_QS_1`)

- **Query Health Score:** `13.52` / 100 | **Banda:** `CRITICA` | **Ranking de prioridad del sistema:** `#3` de 10

### 1. Hallazgos del motor determinístico (`S3-01` calibrado)
- **`F-18F40E1A92C0` · `REG-02` (Índice faltante)** — Severidad: `ALTA` | Evidencia: `ESTIMADA` | Nodos: Nodo #1 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_Lab`, `equality_columns=Region`, `impact_pct=99.277`, `include_columns=`, `inequality_columns=Amount`, `schema=dbo`, `table=Orders`, `write_cost_warning=La sugerencia del optimizador no considera todos los índices existentes ni el costo de escritura (INSERT, UPDATE, DELETE) y mantenimiento; debe evaluarse manualmente por un DBA antes de aplicarse.`
- **`F-0D04EF7E0B2D` · `REG-03` (Lectura completa costosa (contextual))** — Severidad: `ALTA` | Evidencia: `ESTIMADA` | Nodos: Nodo #1 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_Lab`, `estimated_rows=16172.1`, `estimated_rows_read=1000000`, `estimated_subtree_cost=23.3339`, `has_predicate=true`, `index=PK_Orders`, `logical_op=Clustered Index Scan`, `min_estimated_rows_threshold=10000`, `min_predicate_volume_rows_threshold=1000`, `min_query_consumption_pct_threshold=1`, `physical_op=Clustered Index Scan`, `predicate=[QD_Lab].[dbo].[Orders].[Region]=[@region] AND [QD_Lab].[dbo].[Orders].[Amount]>(900.00)`, `rows_read_to_returned_ratio=61.8349`, `rows_read_to_returned_ratio_threshold=10`, `schema=dbo`, `table=Orders`, `table_cardinality=1000000`, `triggered_by=predicate_ratio_and_volume`, `volume_rows_evaluated=1000000`

### 2. Explicación generada por IA (Estado: `TRAZADA`)
- **Resumen:** Diagnóstico de rendimiento para la consulta ESC03_QS_1, identificando un escaneo completo costoso y la sugerencia de un índice faltante para optimizar el acceso a datos.

| # | Tipo | Afirmación | Evidencia (`finding_ids`) | Fragmento KB |
|---:|---|---|---|---|
| 1 | `OBSERVACION` | La consulta ESC03_QS_1 realiza un Clustered Index Scan en la tabla dbo.Orders, procesando 1,000,000 filas para retornar un promedio de 5,000 filas por ejecución. | `F-0D04EF7E0B2D` | `-` |
| 2 | `OBSERVACION` | El optimizador de SQL Server ha detectado un índice faltante con un impacto estimado del 99.277% sobre el costo del plan actual, sugiriendo columnas 'Region' para igualdad y 'Amount' para desigualdad. | `F-18F40E1A92C0` | `-` |
| 3 | `INTERPRETACION` | El Clustered Index Scan en la tabla dbo.Orders es costoso porque, a pesar de tener un predicado en la cláusula WHERE, el motor de base de datos debe examinar todas las páginas del índice agrupado para aplicar el filtro, descartando la mayoría de las filas leídas. Esto se evidencia por la lectura de 1,000,000 filas para obtener solo 5,000, lo que resulta en una relación de filas leídas a retornadas de 61.83 a 1. | `F-0D04EF7E0B2D` | `a1010003-0003-0000-0000-000000000001` |
| 4 | `INTERPRETACION` | La ausencia de un índice adecuado que cubra las columnas 'Region' y 'Amount' impide que el optimizador realice una búsqueda más eficiente, forzando un escaneo completo del índice agrupado para satisfacer la consulta. | `F-18F40E1A92C0, F-0D04EF7E0B2D` | `a1010002-0002-0000-0000-000000000001` |
| 5 | `RECOMENDACION` | Se sugiere evaluar la creación de un índice no agrupado sobre las columnas 'Region' y 'Amount' para permitir que el motor de base de datos realice una búsqueda más selectiva y evite el escaneo completo del índice agrupado. Esto podría convertir el filtro residual en una búsqueda puntual. | `F-18F40E1A92C0, F-0D04EF7E0B2D` | `a1010003-0003-0000-0000-000000000002` |
| 6 | `RECOMENDACION` | Antes de implementar un nuevo índice, se recomienda al DBA analizar el impacto en el costo de escritura (INSERT, UPDATE, DELETE) y el mantenimiento general de los índices existentes. Considere si un índice existente podría ser modificado para incluir las columnas necesarias en lugar de crear uno completamente nuevo. | `F-18F40E1A92C0` | `a1010002-0002-0000-0000-000000000002` |

---

## CASO-06 (`ESC01_QS_1`)

- **Query Health Score:** `13.52` / 100 | **Banda:** `CRITICA` | **Ranking de prioridad del sistema:** `#2` de 10

### 1. Hallazgos del motor determinístico (`S3-01` calibrado)
- **`F-30229DA0024A` · `REG-01` (Conversión implícita que afecta el plan)** — Severidad: `ALTA` | Evidencia: `ESTIMADA` | Nodos: Nodo #3 (Index Scan)
  - Evidencia extraída: `column=CustomerCode`, `convert_issue=Seek Plan`, `expression=CONVERT_IMPLICIT(nvarchar(20),[QD_Lab].[dbo].[Orders].[CustomerCode],0)=[@code]`, `index=IX_Orders_CustomerCode`, `table=Orders`, `target_data_type=nvarchar`
- **`F-405CF25401F3` · `REG-03` (Lectura completa costosa (contextual))** — Severidad: `ALTA` | Evidencia: `ESTIMADA` | Nodos: Nodo #3 (Index Scan)
  - Evidencia extraída: `database=QD_Lab`, `estimated_rows=20`, `estimated_rows_read=1000000`, `estimated_subtree_cost=3.03143`, `has_predicate=true`, `index=IX_Orders_CustomerCode`, `logical_op=Index Scan`, `min_estimated_rows_threshold=10000`, `min_predicate_volume_rows_threshold=1000`, `min_query_consumption_pct_threshold=1`, `physical_op=Index Scan`, `predicate=CONVERT_IMPLICIT(nvarchar(20),[QD_Lab].[dbo].[Orders].[CustomerCode],0)=[@code]`, `rows_read_to_returned_ratio=50000`, `rows_read_to_returned_ratio_threshold=10`, `schema=dbo`, `table=Orders`, `table_cardinality=1000000`, `triggered_by=predicate_ratio_and_volume`, `volume_rows_evaluated=1000000`

### 2. Explicación generada por IA (Estado: `TRAZADA`)
- **Resumen:** Diagnóstico de rendimiento para la consulta ESC01_QS_1, identificando una conversión implícita que degrada el rendimiento del índice y provoca lecturas excesivas.

| # | Tipo | Afirmación | Evidencia (`finding_ids`) | Fragmento KB |
|---:|---|---|---|---|
| 1 | `OBSERVACION` | La consulta ESC01_QS_1 ejecuta un Index Scan en la tabla dbo.Orders sobre la columna CustomerCode, utilizando el índice IX_Orders_CustomerCode. | `F-30229DA0024A, F-405CF25401F3` | `-` |
| 2 | `OBSERVACION` | El plan de ejecución muestra una conversión implícita en el predicado de la cláusula WHERE, específicamente CONVERT_IMPLICIT(nvarchar(20),[QD_Lab].[dbo].[Orders].[CustomerCode],0)=[@code]). | `F-30229DA0024A` | `-` |
| 3 | `OBSERVACION` | El Index Scan en el Nodo #3 lee aproximadamente 1,000,000 filas para devolver solo 20 filas en promedio, lo que representa una relación de lectura a retorno de 50,000:1. | `F-405CF25401F3` | `-` |
| 4 | `INTERPRETACION` | La conversión implícita del tipo de datos en la columna CustomerCode impide que el optimizador de consultas utilice el índice IX_Orders_CustomerCode de manera eficiente para una búsqueda (Index Seek), forzando un Index Scan completo. Esto se debe a la precedencia de tipos de datos que aplica CONVERT_IMPLICIT sobre la columna indexada, perdiendo la sargabilidad. | `F-30229DA0024A` | `a1010001-0001-0000-0000-000000000001` |
| 5 | `INTERPRETACION` | El Index Scan resultante, con un predicado residual aplicado después de leer un gran volumen de datos, es costoso. El motor de base de datos examina todas las páginas del índice y luego descarta la mayoría de las filas que no cumplen con el predicado, lo que incrementa significativamente las lecturas lógicas y el consumo de recursos. | `F-405CF25401F3` | `a1010003-0003-0000-0000-000000000001` |
| 6 | `RECOMENDACION` | Se sugiere alinear el tipo de datos del parámetro @code con el tipo de datos de la columna CustomerCode en la tabla dbo.Orders. Por ejemplo, si CustomerCode es VARCHAR, el parámetro debería ser VARCHAR para evitar la conversión implícita y permitir un Index Seek eficiente. | `F-30229DA0024A` | `a1010001-0001-0000-0000-000000000002` |
| 7 | `RECOMENDACION` | Se recomienda revisar la definición del índice IX_Orders_CustomerCode y la columna CustomerCode para asegurar que los tipos de datos sean consistentes y permitan búsquedas puntuales. Si la alineación de tipos de datos no es posible, se podría considerar la creación de un índice no agrupado más selectivo o la refactorización de la expresión en la cláusula WHERE para hacerla sargable. | `F-405CF25401F3` | `a1010003-0003-0000-0000-000000000002` |

---

## CASO-07 (`TPCH_Q18`)

- **Query Health Score:** `26` / 100 | **Banda:** `CRITICA` | **Ranking de prioridad del sistema:** `#5` de 10

### 1. Hallazgos del motor determinístico (`S3-01` calibrado)
- **`F-5D932640BEA7` · `REG-03` (Lectura completa costosa (contextual))** — Severidad: `ALTA` | Evidencia: `ESTIMADA` | Nodos: Nodo #7 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `estimated_rows=150000`, `estimated_rows_read=150000`, `estimated_subtree_cost=1.30162`, `has_predicate=false`, `index=PK_ORDERS`, `logical_op=Clustered Index Scan`, `min_estimated_rows_threshold=10000`, `min_predicate_volume_rows_threshold=1000`, `min_query_consumption_pct_threshold=1`, `physical_op=Clustered Index Scan`, `rows_read_to_returned_ratio=1`, `rows_read_to_returned_ratio_threshold=10`, `schema=dbo`, `table=ORDERS`, `table_cardinality=150000`, `triggered_by=volume_and_consumption`, `volume_rows_evaluated=150000`

### 2. Explicación generada por IA (Estado: `TRAZADA`)
- **Resumen:** Diagnóstico de rendimiento para la consulta TPCH_Q18, identificando un escaneo completo costoso en la tabla ORDERS que impacta el consumo de recursos.

| # | Tipo | Afirmación | Evidencia (`finding_ids`) | Fragmento KB |
|---:|---|---|---|---|
| 1 | `OBSERVACION` | La consulta realiza un escaneo completo del índice agrupado 'PK_ORDERS' en la tabla 'dbo.ORDERS' (Nodo #7). | `F-5D932640BEA7` | `-` |
| 2 | `OBSERVACION` | Este escaneo procesa 150,000 filas estimadas, que es la cardinalidad total de la tabla 'ORDERS', y representa el 8.757% del costo total de la consulta. | `F-5D932640BEA7` | `-` |
| 3 | `INTERPRETACION` | Un 'Clustered Index Scan' sin predicado en el nodo de escaneo, como el observado en la tabla 'ORDERS', implica que el motor de base de datos examina todas las páginas del índice agrupado, lo que puede resultar en un uso ineficiente de recursos al leer más datos de los necesarios. | `F-5D932640BEA7` | `a1010003-0003-0000-0000-000000000001` |
| 4 | `RECOMENDACION` | Se sugiere revisar la consulta y el esquema de índices de la tabla 'ORDERS' para determinar si es posible crear un índice no agrupado selectivo sobre las columnas utilizadas en los predicados o reescribir la consulta para permitir una búsqueda más eficiente, evitando el escaneo completo del índice agrupado. | `F-5D932640BEA7` | `a1010003-0003-0000-0000-000000000002` |

---

## CASO-08 (`SEEDED_REG04_POS_RID_2500`)

- **Query Health Score:** `35` / 100 | **Banda:** `CRITICA` | **Ranking de prioridad del sistema:** `#7` de 10

### 1. Hallazgos del motor determinístico (`S3-01` calibrado)
- **`F-3360FA5458D9` · `REG-04` (Búsqueda por clave excesiva (contextual))** — Severidad: `MEDIA` | Evidencia: `ESTIMADA` | Nodos: Nodo #4 (RID Lookup)
  - Evidencia extraída: `database=QD_Lab`, `estimate_rebinds=2499`, `estimate_rewinds=0`, `estimated_executions=2500`, `estimated_subtree_cost=2.8`, `lookup_kind=RID Lookup`, `min_estimated_executions_threshold=1000`, `min_query_consumption_pct_threshold=1`, `output_columns=Detail`, `schema=dbo`, `table=HeapAudit`

### 2. Explicación generada por IA (Estado: `TRAZADA`)
- **Resumen:** Diagnóstico de rendimiento para la consulta SEEDED_REG04_POS_RID_2500, identificando un problema de RID Lookup excesivo en la tabla HeapAudit.

| # | Tipo | Afirmación | Evidencia (`finding_ids`) | Fragmento KB |
|---:|---|---|---|---|
| 1 | `OBSERVACION` | La consulta ejecuta un operador 'RID Lookup' en el Nodo #4 sobre la tabla 'dbo.HeapAudit'. | `F-3360FA5458D9` | `-` |
| 2 | `OBSERVACION` | El operador 'RID Lookup' tiene un 'estimated_executions' de 2500, lo que indica que se espera que se realice esta operación un número elevado de veces por cada ejecución de la consulta. | `F-3360FA5458D9` | `-` |
| 3 | `OBSERVACION` | La consulta retorna un promedio de 2500 filas por ejecución, lo que coincide con el número estimado de ejecuciones del RID Lookup. | `F-3360FA5458D9` | `-` |
| 4 | `INTERPRETACION` | El alto número de 'RID Lookups' sugiere que el índice utilizado para la búsqueda inicial no cubre todas las columnas requeridas por la consulta, forzando al motor a realizar saltos adicionales a la tabla base (heap) para recuperar la columna 'Detail' por cada fila encontrada. | `F-3360FA5458D9` | `a1010004-0004-0000-0000-000000000001` |
| 5 | `RECOMENDACION` | Se sugiere evaluar la creación o modificación de un índice no agrupado en la tabla 'dbo.HeapAudit' que incluya la columna 'CorrelationKey' como clave y la columna 'Detail' en la cláusula INCLUDE. Esto permitiría que la consulta se resuelva completamente desde el índice, eliminando los costosos 'RID Lookups'. | `F-3360FA5458D9` | `a1010004-0004-0000-0000-000000000002` |

---

## CASO-09 (`TPCH_NEG02`)

- **Query Health Score:** `99.8` / 100 | **Banda:** `SALUDABLE` | **Ranking de prioridad del sistema:** `#10` de 10

### 1. Hallazgos del motor determinístico (`S3-01` calibrado)
- *El motor determinístico no emitió hallazgos del catálogo activo (`REG-01`..`REG-04`) para este plan.*

### 2. Explicación generada por IA (Estado: `OMITIDA_SIN_HALLAZGOS`)
- *Explicación omitida por diseño (`RF-IA-01`): no se generan afirmaciones cuando no existen hallazgos determinísticos.*

---

## CASO-10 (`TPCH_Q20`)

- **Query Health Score:** `4.64` / 100 | **Banda:** `CRITICA` | **Ranking de prioridad del sistema:** `#1` de 10

### 1. Hallazgos del motor determinístico (`S3-01` calibrado)
- **`F-811C7395DA05` · `REG-02` (Índice faltante)** — Severidad: `MEDIA` | Evidencia: `ESTIMADA` | Nodos: Nodo #14 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `equality_columns=`, `impact_pct=73.2469`, `include_columns=L_PARTKEY, L_SUPPKEY, L_QUANTITY`, `inequality_columns=L_SHIPDATE`, `schema=dbo`, `table=LINEITEM`, `write_cost_warning=La sugerencia del optimizador no considera todos los índices existentes ni el costo de escritura (INSERT, UPDATE, DELETE) y mantenimiento; debe evaluarse manualmente por un DBA antes de aplicarse.`
- **`F-D76245076438` · `REG-02` (Índice faltante)** — Severidad: `ALTA` | Evidencia: `ESTIMADA` | Nodos: Nodo #14 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `equality_columns=L_PARTKEY, L_SUPPKEY`, `impact_pct=89.435`, `include_columns=L_QUANTITY`, `inequality_columns=L_SHIPDATE`, `schema=dbo`, `table=LINEITEM`, `write_cost_warning=La sugerencia del optimizador no considera todos los índices existentes ni el costo de escritura (INSERT, UPDATE, DELETE) y mantenimiento; debe evaluarse manualmente por un DBA antes de aplicarse.`
- **`F-C3E77814CAB3` · `REG-03` (Lectura completa costosa (contextual))** — Severidad: `MEDIA` | Evidencia: `ESTIMADA` | Nodos: Nodo #12 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `estimated_rows=4000`, `estimated_rows_read=20000`, `estimated_subtree_cost=0.248986`, `has_predicate=true`, `index=PK_PART`, `logical_op=Clustered Index Scan`, `min_estimated_rows_threshold=10000`, `min_predicate_volume_rows_threshold=1000`, `min_query_consumption_pct_threshold=1`, `physical_op=Clustered Index Scan`, `predicate=[QD_TPCH].[dbo].[PART].[P_NAME] as [P].[P_NAME] like 'part green%'`, `rows_read_to_returned_ratio=5`, `rows_read_to_returned_ratio_threshold=10`, `schema=dbo`, `table=PART`, `table_cardinality=20000`, `triggered_by=volume_and_consumption`, `volume_rows_evaluated=20000`
- **`F-CFB0AAF9F421` · `REG-03` (Lectura completa costosa (contextual))** — Severidad: `MEDIA` | Evidencia: `ESTIMADA` | Nodos: Nodo #13 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `estimated_rows=80000`, `estimated_rows_read=80000`, `estimated_subtree_cost=0.43943`, `has_predicate=false`, `index=PK_PARTSUPP`, `logical_op=Clustered Index Scan`, `min_estimated_rows_threshold=10000`, `min_predicate_volume_rows_threshold=1000`, `min_query_consumption_pct_threshold=1`, `physical_op=Clustered Index Scan`, `rows_read_to_returned_ratio=1`, `rows_read_to_returned_ratio_threshold=10`, `schema=dbo`, `table=PARTSUPP`, `table_cardinality=80000`, `triggered_by=volume_and_consumption`, `volume_rows_evaluated=80000`
- **`F-6528115E7115` · `REG-03` (Lectura completa costosa (contextual))** — Severidad: `ALTA` | Evidencia: `ESTIMADA` | Nodos: Nodo #14 (Clustered Index Scan)
  - Evidencia extraída: `database=QD_TPCH`, `estimated_rows=92471.3`, `estimated_rows_read=600000`, `estimated_subtree_cost=7.10402`, `has_predicate=true`, `index=PK_LINEITEM`, `logical_op=Clustered Index Scan`, `min_estimated_rows_threshold=10000`, `min_predicate_volume_rows_threshold=1000`, `min_query_consumption_pct_threshold=1`, `physical_op=Clustered Index Scan`, `predicate=[QD_TPCH].[dbo].[LINEITEM].[L_SHIPDATE] as [L].[L_SHIPDATE]>='1994-01-01' AND [QD_TPCH].[dbo].[LINEITEM].[L_SHIPDATE] as [L].[L_SHIPDATE]<'1995-01-01'`, `rows_read_to_returned_ratio=6.4885`, `rows_read_to_returned_ratio_threshold=10`, `schema=dbo`, `table=LINEITEM`, `table_cardinality=600000`, `triggered_by=volume_and_consumption`, `volume_rows_evaluated=600000`

### 2. Explicación generada por IA (Estado: `TRAZADA`)
- **Resumen:** Diagnóstico de rendimiento para la consulta TPCH_Q20, identificando escaneos costosos y oportunidades de indexación en las tablas LINEITEM, PART y PARTSUPP.

| # | Tipo | Afirmación | Evidencia (`finding_ids`) | Fragmento KB |
|---:|---|---|---|---|
| 1 | `OBSERVACION` | La consulta realiza un Clustered Index Scan en la tabla dbo.LINEITEM (Nodo #14) que evalúa 600,000 filas, lo que representa la cardinalidad total de la tabla, para retornar un número significativamente menor de filas estimadas (92,471.3). | `F-6528115E7115` | `-` |
| 2 | `INTERPRETACION` | El Clustered Index Scan en dbo.LINEITEM (Nodo #14) es costoso porque, a pesar de tener un predicado de filtro sobre L_SHIPDATE, el motor de base de datos debe leer todas las páginas del índice agrupado para aplicar el filtro, lo que incrementa las lecturas lógicas y el consumo de recursos. | `F-6528115E7115` | `a1010003-0003-0000-0000-000000000001` |
| 3 | `RECOMENDACION` | Se sugiere evaluar la creación de un índice no agrupado en la tabla dbo.LINEITEM que incluya las columnas L_PARTKEY, L_SUPPKEY y L_SHIPDATE, y que cubra L_QUANTITY. Esto permitiría al optimizador realizar una búsqueda más eficiente en lugar de un escaneo completo, mejorando el rendimiento de la subconsulta. | `F-811C7395DA05, F-D76245076438, F-6528115E7115` | `a1010002-0002-0000-0000-000000000002` |
| 4 | `OBSERVACION` | Existe un Clustered Index Scan en la tabla dbo.PART (Nodo #12) que procesa 20,000 filas (la cardinalidad completa de la tabla) para un predicado LIKE en P_NAME, con una relación de filas leídas a retornadas de 5 a 1. | `F-C3E77814CAB3` | `-` |
| 5 | `INTERPRETACION` | El escaneo completo en dbo.PART (Nodo #12) es ineficiente porque el predicado LIKE 'part green%' no puede utilizar un índice de manera óptima para una búsqueda puntual, forzando un escaneo de todo el índice agrupado para encontrar las filas que coinciden. | `F-C3E77814CAB3` | `a1010003-0003-0000-0000-000000000001` |
| 6 | `RECOMENDACION` | Considere crear un índice no agrupado en la columna P_NAME de la tabla dbo.PART para mejorar la eficiencia del predicado LIKE. Alternativamente, si es posible, refactorizar el predicado para que sea sargable podría permitir un uso más efectivo de los índices existentes o nuevos. | `F-C3E77814CAB3` | `a1010003-0003-0000-0000-000000000002` |
| 7 | `OBSERVACION` | La tabla dbo.PARTSUPP (Nodo #13) también experimenta un Clustered Index Scan que lee 80,000 filas, que es la cardinalidad total de la tabla, sin un predicado de filtro directo en este nodo del plan. | `F-CFB0AAF9F421` | `-` |
| 8 | `INTERPRETACION` | El escaneo completo en dbo.PARTSUPP (Nodo #13) contribuye al costo general de la consulta al leer todas las filas de la tabla, lo que indica que no hay un índice adecuado para satisfacer las condiciones de unión o subconsulta de manera más selectiva en este punto del plan. | `F-CFB0AAF9F421` | `a1010003-0003-0000-0000-000000000001` |
| 9 | `RECOMENDACION` | Se recomienda analizar las columnas utilizadas en las uniones y subconsultas que involucran a dbo.PARTSUPP para determinar si un índice no agrupado sobre PS_SUPPKEY o PS_PARTKEY, o una combinación de ambos, podría convertir este escaneo en una búsqueda más eficiente. | `F-CFB0AAF9F421` | `a1010003-0003-0000-0000-000000000002` |

---
