# Cuaderno de Evaluación Técnica para Especialista en Bases de Datos (`S3-04`)

> **Instrucciones para el Especialista:** Analice cada una de las siguientes consultas de forma estrictamente independiente a partir del texto SQL, las métricas de Query Store y el plan de ejecución XML referenciado. No consulte diagnósticos previos ni herramientas automatizadas.

Para cada caso, complete en la planilla CSV adjunta (`planilla_especialista.csv`):
1. **Causa raíz principal (`specialist_root_cause_rule`):** `REG-01`, `REG-02`, `REG-03`, `REG-04`, `REG-11` o `NINGUNO`.
2. **Acción correctiva sugerida (`specialist_action_type`):** `ALINEAR_TIPOS`, `CREAR_INDICE`, `MODIFICAR_INDICE_INCLUDE`, `REESCRIBIR_PREDICADO_SARGABLE`, `REFACTORIZAR_CTE_TABLA_TEMP`, `NINGUNA_ACCION`.
3. **Detalle de la recomendación (`specialist_action_detail`):** Breve justificación técnica de la intervención.
4. **Nivel de prioridad (`specialist_priority`):** `CRITICA`, `ALTA`, `MEDIA`, `BAJA`, `LEVE_NINGUNA`.
5. **Rango de urgencia (`specialist_rank`):** Posición del 1 al 15 (1 = la más urgente de intervenir).

---

## Caso `SPE-01`: REG-01: Filtro varchar contra parámetro Unicode nvarchar
- **Categoría:** `seeded_conversion`
- **Referencia de Plan XML:** `plans/ESC01.qs.1.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 300
- **Duración media:** 1250.0 ms
- **Tiempo medio de CPU:** 950.0 ms
- **Lecturas lógicas promedio (I/O):** 48,000 páginas
- **Filas retornadas promedio:** 20.0

### Texto SQL de la Consulta
```sql
SELECT c_custkey, c_name FROM dbo.customer WHERE c_phone = @qd_p0
```

---

## Caso `SPE-02`: REG-01: Join en clave con tipos desalineados int y bigint
- **Categoría:** `seeded_conversion`
- **Referencia de Plan XML:** `plans/ESC01.qs.2.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 78
- **Duración media:** 3400.0 ms
- **Tiempo medio de CPU:** 3100.0 ms
- **Lecturas lógicas promedio (I/O):** 115,000 páginas
- **Filas retornadas promedio:** 750.0

### Texto SQL de la Consulta
```sql
SELECT o.o_orderkey, l.l_linenumber FROM dbo.orders o JOIN dbo.lineitem l ON o.o_orderkey = l.l_orderkey WHERE o.o_orderstatus = @qd_p0
```

---

## Caso `SPE-03`: REG-01: Búsqueda en campo char con literal string variable
- **Categoría:** `seeded_conversion`
- **Referencia de Plan XML:** `plans/q-std-03.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 15
- **Duración media:** 850.0 ms
- **Tiempo medio de CPU:** 620.0 ms
- **Lecturas lógicas promedio (I/O):** 18,500 páginas
- **Filas retornadas promedio:** 5.0

### Texto SQL de la Consulta
```sql
SELECT s_suppkey, s_name FROM dbo.supplier WHERE s_nationkey = @qd_p0
```

---

## Caso `SPE-04`: REG-01: Conversión implícita en clave foránea de detalle
- **Categoría:** `seeded_conversion`
- **Referencia de Plan XML:** `plans/q-std-04.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 110
- **Duración media:** 4200.0 ms
- **Tiempo medio de CPU:** 3950.0 ms
- **Lecturas lógicas promedio (I/O):** 240,000 páginas
- **Filas retornadas promedio:** 1200.0

### Texto SQL de la Consulta
```sql
SELECT l_partkey, l_quantity FROM dbo.lineitem WHERE l_shipinstruct = @qd_p0
```

---

## Caso `SPE-05`: REG-02: Filtro compuesto sobre lineitem sin índice secundario
- **Categoría:** `tpch_missing_index`
- **Referencia de Plan XML:** `plans/ESC04.qs.1.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 45
- **Duración media:** 5200.0 ms
- **Tiempo medio de CPU:** 4800.0 ms
- **Lecturas lógicas promedio (I/O):** 350,000 páginas
- **Filas retornadas promedio:** 32000.0

### Texto SQL de la Consulta
```sql
SELECT l_orderkey, l_extendedprice FROM dbo.lineitem WHERE l_shipdate >= @qd_p0 AND l_shipdate <= @qd_p1
```

---

## Caso `SPE-06`: REG-02: Join entre pedidos y clientes sin índice en clave foránea
- **Categoría:** `tpch_missing_index`
- **Referencia de Plan XML:** `plans/q-std-06.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 60
- **Duración media:** 4100.0 ms
- **Tiempo medio de CPU:** 3850.0 ms
- **Lecturas lógicas promedio (I/O):** 210,000 páginas
- **Filas retornadas promedio:** 14500.0

### Texto SQL de la Consulta
```sql
SELECT o.o_orderkey, c.c_name FROM dbo.orders o JOIN dbo.customer c ON o.o_custkey = c.c_custkey WHERE c.c_mktsegment = @qd_p0
```

---

## Caso `SPE-07`: REG-02: Filtro de rango en proveedores sin índice cubierto
- **Categoría:** `tpch_missing_index`
- **Referencia de Plan XML:** `plans/q-std-07.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 25
- **Duración media:** 1950.0 ms
- **Tiempo medio de CPU:** 1600.0 ms
- **Lecturas lógicas promedio (I/O):** 98,000 páginas
- **Filas retornadas promedio:** 1800.0

### Texto SQL de la Consulta
```sql
SELECT s_suppkey, s_acctbal FROM dbo.supplier WHERE s_acctbal > @qd_p0
```

---

## Caso `SPE-08`: REG-03: Table scan sobre tabla grande con predicado residual
- **Categoría:** `seeded_full_scan`
- **Referencia de Plan XML:** `plans/ESC05.qs.1.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 80
- **Duración media:** 3800.0 ms
- **Tiempo medio de CPU:** 3450.0 ms
- **Lecturas lógicas promedio (I/O):** 280,000 páginas
- **Filas retornadas promedio:** 450.0

### Texto SQL de la Consulta
```sql
SELECT p_partkey, p_name FROM dbo.part WHERE p_retailprice > @qd_p0
```

---

## Caso `SPE-09`: REG-03: Clustered index scan en lineitem con filtro residual
- **Categoría:** `tpch_full_scan`
- **Referencia de Plan XML:** `plans/q-std-10.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 50
- **Duración media:** 2900.0 ms
- **Tiempo medio de CPU:** 2600.0 ms
- **Lecturas lógicas promedio (I/O):** 195,000 páginas
- **Filas retornadas promedio:** 210.0

### Texto SQL de la Consulta
```sql
SELECT l_orderkey, l_suppkey FROM dbo.lineitem WHERE l_tax > @qd_p0
```

---

## Caso `SPE-10`: REG-03: Escaneo completo en customer con predicado no sargable
- **Categoría:** `tpch_full_scan`
- **Referencia de Plan XML:** `plans/q-std-12.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 30
- **Duración media:** 1600.0 ms
- **Tiempo medio de CPU:** 1350.0 ms
- **Lecturas lógicas promedio (I/O):** 82,000 páginas
- **Filas retornadas promedio:** 80.0

### Texto SQL de la Consulta
```sql
SELECT c_custkey, c_address FROM dbo.customer WHERE c_comment LIKE @qd_p0
```

---

## Caso `SPE-11`: REG-04: Búsqueda en índice no agrupado con Key Lookup masivo
- **Categoría:** `seeded_key_lookup`
- **Referencia de Plan XML:** `plans/ESC03.qs.1.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 150
- **Duración media:** 2400.0 ms
- **Tiempo medio de CPU:** 2100.0 ms
- **Lecturas lógicas promedio (I/O):** 165,000 páginas
- **Filas retornadas promedio:** 2500.0

### Texto SQL de la Consulta
```sql
SELECT o_orderkey, o_orderdate, o_shippriority FROM dbo.orders WHERE o_custkey = @qd_p0
```

---

## Caso `SPE-12`: REG-04: Key Lookup excesivo en detalle de clientes
- **Categoría:** `tpch_key_lookup`
- **Referencia de Plan XML:** `plans/q-std-14.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 90
- **Duración media:** 1800.0 ms
- **Tiempo medio de CPU:** 1550.0 ms
- **Lecturas lógicas promedio (I/O):** 125,000 páginas
- **Filas retornadas promedio:** 1900.0

### Texto SQL de la Consulta
```sql
SELECT c_custkey, c_name, c_address FROM dbo.customer WHERE c_nationkey = @qd_p0
```

---

## Caso `SPE-13`: REG-04: Key Lookup en partsupp por columnas no cubiertas
- **Categoría:** `tpch_key_lookup`
- **Referencia de Plan XML:** `plans/q-std-15.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 40
- **Duración media:** 1100.0 ms
- **Tiempo medio de CPU:** 880.0 ms
- **Lecturas lógicas promedio (I/O):** 72,000 páginas
- **Filas retornadas promedio:** 650.0

### Texto SQL de la Consulta
```sql
SELECT ps_partkey, ps_suppkey, ps_supplycost FROM dbo.partsupp WHERE ps_availqty < @qd_p0
```

---

## Caso `SPE-14`: REG-11: Showplan XML truncado por límite de tamaño de buffer
- **Categoría:** `seeded_truncation`
- **Referencia de Plan XML:** `plans/ESC06.qs.1.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 10
- **Duración media:** 3500.0 ms
- **Tiempo medio de CPU:** 3100.0 ms
- **Lecturas lógicas promedio (I/O):** 95,000 páginas
- **Filas retornadas promedio:** 100.0

### Texto SQL de la Consulta
```sql
WITH SubCTE AS (SELECT o_orderkey FROM dbo.orders WHERE o_totalprice > @qd_p0) SELECT * FROM SubCTE
```

---

## Caso `SPE-15`: Control Negativo: Búsqueda puntual por Primary Key sin defectos
- **Categoría:** `negative_clean`
- **Referencia de Plan XML:** `plans/ESC02.qs.1.sqlplan`

### Contexto de Ejecución en Query Store
- **Número de ejecuciones registradas:** 500
- **Duración media:** 2.5 ms
- **Tiempo medio de CPU:** 1.8 ms
- **Lecturas lógicas promedio (I/O):** 3 páginas
- **Filas retornadas promedio:** 1.0

### Texto SQL de la Consulta
```sql
SELECT c_custkey, c_name FROM dbo.customer WHERE c_custkey = @qd_p0
```

---
