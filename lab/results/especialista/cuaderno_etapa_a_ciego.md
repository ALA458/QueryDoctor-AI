# Cuaderno de Evaluación Diagnóstica (Etapa A — Ciega) — `S3-04`

> **Importante:** Evalúa cada caso y registra tu criterio en `planilla_etapa_a.csv` **antes** de abrir los archivos de la Etapa B. El orden ha sido aleatorizado y no expone identificadores de reglas ni diagnósticos de Query Doctor AI.

## CASO-01

- **Entorno:** Carga analítica TPC-H
- **Métricas de ejecución:** ejecuciones=`1`, consumo ponderado en ventana=`2.3215` %, filas devueltas promedio=`100`
- **Costo total estimado del subárbol:** `1.70227` | **Nivel optimización:** `FULL` (aborto temprano: `TimeOut`)

### Consulta SQL
```sql
SELECT TOP (100) -- QD-TPCH:Q02:
    S.S_ACCTBAL,
    S.S_NAME,
    N.N_NAME,
    P.P_PARTKEY,
    P.P_MFGR,
    S.S_ADDRESS,
    S.S_PHONE,
    S.S_COMMENT
FROM dbo.PART P
JOIN dbo.PARTSUPP PS ON P.P_PARTKEY = PS.PS_PARTKEY
JOIN dbo.SUPPLIER S ON S.S_SUPPKEY = PS.PS_SUPPKEY
JOIN dbo.NATION N ON S.S_NATIONKEY = N.N_NATIONKEY
JOIN dbo.REGION R ON N.N_REGIONKEY = R.R_REGIONKEY
WHERE P.P_SIZE = 15
  AND P.P_TYPE LIKE '%STEEL'
  AND R.R_NAME = 'EUROPE'
  AND PS.PS_SUPPLYCOST = (
      SELECT MIN(PS2.PS_SUPPLYCOST)
      FROM dbo.PARTSUPP PS2
      JOIN dbo.SUPPLIER S2 ON S2.S_SUPPKEY = PS2.PS_SUPPKEY
      JOIN dbo.NATION N2 ON S2.S_NATIONKEY = N2.N_NATIONKEY
      JOIN dbo.REGION R2 ON N2.N_REGIONKEY = R2.R_REGIONKEY
      WHERE P.P_PARTKEY = PS2.PS_PARTKEY
        AND R2.R_NAME = 'EUROPE'
  )
ORDER BY S.S_ACCTBAL DESC, N.N_NAME ASC, S.S_NAME ASC, P.P_PARTKEY ASC
```

### Resumen estructural del plan Showplan XML

| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |
|---:|---|---|---:|---:|---:|---|
| #0 | `Sort` | `TopN Sort` | 23.6 | - | 1.7023 | `-` |
| #1 | `Nested Loops` | `Inner Join` | 23.6 | - | 1.6907 | `-` |
| #2 | `Nested Loops` | `Inner Join` | 118.2 | - | 1.6684 | `-` |
| #3 | `Nested Loops` | `Inner Join` | 118.2 | - | 1.6461 | `-` |
| #4 | `Nested Loops` | `Inner Join` | 118.2 | - | 1.5832 | `-` |
| #6 | `Stream Aggregate` | `Aggregate` | 118.2 | - | 1.2388 | `-` |
| #8 | `Sort` | `Sort` | 160.2 | - | 1.2387 | `-` |
| #9 | `Hash Match` | `Inner Join` | 160.2 | - | 1.2255 | `-` |
| #10 | `Clustered Index Scan` | `Clustered Index Scan` | 200.2 | 20000.0 | 0.2490 | `dbo.PART [PK_PART]` |
| #11 | `Hash Match` | `Inner Join` | 16000.0 | - | 0.8556 | `-` |
| #12 | `Nested Loops` | `Inner Join` | 200.0 | - | 0.0182 | `-` |
| #13 | `Nested Loops` | `Inner Join` | 5.0 | - | 0.0105 | `-` |
| #14 | `Clustered Index Scan` | `Clustered Index Scan` | 25.0 | 25.0 | 0.0033 | `dbo.NATION [PK_NATION]` |
| #15 | `Clustered Index Seek` | `Clustered Index Seek` | 1.0 | 1.0 | 0.0071 | `dbo.REGION [PK_REGION]` |
| #16 | `Index Seek` | `Index Seek` | 40.0 | 40.0 | 0.0069 | `dbo.SUPPLIER [IX_SUPPLIER_S_NATIONKEY]` |
| #17 | `Clustered Index Scan` | `Clustered Index Scan` | 80000.0 | 80000.0 | 0.4394 | `dbo.PARTSUPP [PK_PARTSUPP]` |
| #18 | `Clustered Index Seek` | `Clustered Index Seek` | 1.0 | 4.0 | 0.3436 | `dbo.PARTSUPP [PK_PARTSUPP]` |
| #19 | `Clustered Index Seek` | `Clustered Index Seek` | 1.0 | 1.0 | 0.0624 | `dbo.SUPPLIER [PK_SUPPLIER]` |
| #20 | `Clustered Index Seek` | `Clustered Index Seek` | 1.0 | 1.0 | 0.0218 | `dbo.NATION [PK_NATION]` |
| #21 | `Clustered Index Seek` | `Clustered Index Seek` | 1.0 | 1.0 | 0.0218 | `dbo.REGION [PK_REGION]` |

**Sugerencias XML `<MissingIndexes>` del optimizador SQL Server:**
- `Impacto 49.0% en dbo.PARTSUPP: EQ(PS_SUPPKEY) INCLUDE(PS_SUPPLYCOST)`
- `Impacto 15.8% en dbo.PART: EQ(P_SIZE) INCLUDE(P_MFGR, P_TYPE)`

---

## CASO-02

- **Entorno:** Carga analítica TPC-H
- **Métricas de ejecución:** ejecuciones=`1`, consumo ponderado en ventana=`1.3603` %, filas devueltas promedio=`1`
- **Costo total estimado del subárbol:** `6.77438` | **Nivel optimización:** `FULL` (aborto temprano: `ninguno`)

### Consulta SQL
```sql
SELECT -- QD-TPCH:Q06:
    SUM(L_EXTENDEDPRICE * L_DISCOUNT) AS REVENUE
FROM dbo.LINEITEM
WHERE L_SHIPDATE >= DATEFROMPARTS(1994, 1, 1)
  AND L_SHIPDATE < DATEADD(YEAR, 1, DATEFROMPARTS(1994, 1, 1))
  AND L_DISCOUNT BETWEEN 0.05 AND 0.07
  AND L_QUANTITY < 24
```

### Resumen estructural del plan Showplan XML

| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |
|---:|---|---|---:|---:|---:|---|
| #1 | `Compute Scalar` | `Compute Scalar` | 1.0 | - | 6.7744 | `-` |
| #2 | `Stream Aggregate` | `Aggregate` | 1.0 | - | 6.7744 | `-` |
| #3 | `Parallelism` | `Gather Streams` | 6.0 | - | 6.7744 | `-` |
| #4 | `Stream Aggregate` | `Aggregate` | 6.0 | - | 6.7459 | `-` |
| #5 | `Compute Scalar` | `Compute Scalar` | 39775.9 | - | 6.5639 | `-` |
| #6 | `Clustered Index Scan` | `Clustered Index Scan` | 39775.9 | 600000.0 | 6.5539 | `dbo.LINEITEM [PK_LINEITEM]` |

**Sugerencias XML `<MissingIndexes>` del optimizador SQL Server:**
- `Impacto 87.6% en dbo.LINEITEM: INEQ(L_QUANTITY, L_DISCOUNT, L_SHIPDATE) INCLUDE(L_EXTENDEDPRICE)`

---

## CASO-03

- **Entorno:** Escenario de laboratorio SQL Server
- **Métricas de ejecución:** ejecuciones=`50`, consumo ponderado en ventana=`2` %, filas devueltas promedio=`120`
- **Costo total estimado del subárbol:** `0.45` | **Nivel optimización:** `N/A` (aborto temprano: `ninguno`)

### Consulta SQL
```sql
SELECT OrderId FROM dbo.Orders WHERE CustomerCode = @code
```

### Resumen estructural del plan Showplan XML

| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |
|---:|---|---|---:|---:|---:|---|
| #2 | `Index Scan` | `Index Scan` | 120.0 | 500.0 | 0.0500 | `dbo.Orders [IX_Orders_CustomerCode]` |

**Advertencias XML presentes en el plan:**
- `PlanAffectingConvert`

---

## CASO-04

- **Entorno:** Escenario de laboratorio SQL Server
- **Métricas de ejecución:** ejecuciones=`2`, consumo ponderado en ventana=`4` %, filas devueltas promedio=`495000.5`
- **Costo total estimado del subárbol:** `0.00657038` | **Nivel optimización:** `FULL` (aborto temprano: `GoodEnoughPlanFound`)

### Consulta SQL
```sql
SELECT /* QD-LAB:ESC04B */ OrderId, Amount, CreatedAt
    FROM dbo.Orders
    WHERE SkewKey = @k
    OPTION (USE HINT('QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_150'))
```

### Resumen estructural del plan Showplan XML

| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |
|---:|---|---|---:|---:|---:|---|
| #0 | `Nested Loops` | `Inner Join` | 1.0 | - | 0.0066 | `-` |
| #1 | `Index Seek` | `Index Seek` | 1.0 | 1.0 | 0.0033 | `dbo.Orders [IX_Orders_SkewKey]` |
| #3 | `Clustered Index Seek` | `Clustered Index Seek` | 1.0 | - | 0.0033 | `dbo.Orders [PK_Orders]` |

---

## CASO-05

- **Entorno:** Escenario de laboratorio SQL Server
- **Métricas de ejecución:** ejecuciones=`40`, consumo ponderado en ventana=`5` %, filas devueltas promedio=`5000`
- **Costo total estimado del subárbol:** `23.5171` | **Nivel optimización:** `FULL` (aborto temprano: `ninguno`)

### Consulta SQL
```sql
SELECT /* QD-LAB:ESC03 */ OrderId, Amount
    FROM dbo.Orders
    WHERE Region = @region AND Amount > 900
```

### Resumen estructural del plan Showplan XML

| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |
|---:|---|---|---:|---:|---:|---|
| #0 | `Parallelism` | `Gather Streams` | 16172.1 | - | 23.5171 | `-` |
| #1 | `Clustered Index Scan` | `Clustered Index Scan` | 16172.1 | 1000000.0 | 23.3339 | `dbo.Orders [PK_Orders]` |

**Sugerencias XML `<MissingIndexes>` del optimizador SQL Server:**
- `Impacto 99.3% en dbo.Orders: EQ(Region) INEQ(Amount)`

---

## CASO-06

- **Entorno:** Escenario de laboratorio SQL Server
- **Métricas de ejecución:** ejecuciones=`300`, consumo ponderado en ventana=`5` %, filas devueltas promedio=`20`
- **Costo total estimado del subárbol:** `3.67404` | **Nivel optimización:** `FULL` (aborto temprano: `ninguno`)

### Consulta SQL
```sql
SELECT /* QD-LAB:ESC01 */ OrderId, Amount
    FROM dbo.Orders
    WHERE CustomerCode = @code
```

### Resumen estructural del plan Showplan XML

| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |
|---:|---|---|---:|---:|---:|---|
| #1 | `Nested Loops` | `Inner Join` | 20.0 | - | 3.6740 | `-` |
| #3 | `Index Scan` | `Index Scan` | 20.0 | 1000000.0 | 3.0314 | `dbo.Orders [IX_Orders_CustomerCode]` |
| #5 | `Clustered Index Seek` | `Clustered Index Seek` | 1.0 | - | 0.0625 | `dbo.Orders [PK_Orders]` |

**Advertencias XML presentes en el plan:**
- `PlanAffectingConvert`

---

## CASO-07

- **Entorno:** Carga analítica TPC-H
- **Métricas de ejecución:** ejecuciones=`1`, consumo ponderado en ventana=`8.757` %, filas devueltas promedio=`100`
- **Costo total estimado del subárbol:** `11.021` | **Nivel optimización:** `FULL` (aborto temprano: `TimeOut`)

### Consulta SQL
```sql
SELECT TOP (100) -- QD-TPCH:Q18:
    C.C_NAME,
    C.C_CUSTKEY,
    O.O_ORDERKEY,
    O.O_ORDERDATE,
    O.O_TOTALPRICE,
    SUM(L.L_QUANTITY) AS TOTAL_QTY
FROM dbo.CUSTOMER C
JOIN dbo.ORDERS O ON C.C_CUSTKEY = O.O_CUSTKEY
JOIN dbo.LINEITEM L ON O.O_ORDERKEY = L.L_ORDERKEY
WHERE O.O_ORDERKEY IN (
    SELECT L2.L_ORDERKEY
    FROM dbo.LINEITEM L2
    GROUP BY L2.L_ORDERKEY
    HAVING SUM(L2.L_QUANTITY) > 150
)
GROUP BY C.C_NAME, C.C_CUSTKEY, O.O_ORDERKEY, O.O_ORDERDATE, O.O_TOTALPRICE
ORDER BY O.O_TOTALPRICE DESC, O.O_ORDERDATE ASC
```

### Resumen estructural del plan Showplan XML

| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |
|---:|---|---|---:|---:|---:|---|
| #0 | `Top` | `Top` | 100.0 | - | 11.0210 | `-` |
| #2 | `Nested Loops` | `Inner Join` | 100.0 | - | 11.0210 | `-` |
| #5 | `Nested Loops` | `Inner Join` | 100.0 | - | 10.7573 | `-` |
| #6 | `Sort` | `Sort` | 552.7 | - | 7.2247 | `-` |
| #7 | `Clustered Index Scan` | `Clustered Index Scan` | 150000.0 | 150000.0 | 1.3016 | `dbo.ORDERS [PK_ORDERS]` |
| #8 | `Stream Aggregate` | `Aggregate` | 1.0 | - | 3.8316 | `-` |
| #9 | `Nested Loops` | `Inner Join` | 4.0 | - | 3.8298 | `-` |
| #10 | `Filter` | `Filter` | 1.0 | - | 1.9109 | `-` |
| #11 | `Stream Aggregate` | `Aggregate` | 1.0 | - | 1.9106 | `-` |
| #12 | `Clustered Index Seek` | `Clustered Index Seek` | 4.0 | 4.0 | 1.9089 | `dbo.LINEITEM [PK_LINEITEM]` |
| #13 | `Clustered Index Seek` | `Clustered Index Seek` | 4.0 | 4.0 | 1.9089 | `dbo.LINEITEM [PK_LINEITEM]` |
| #14 | `Clustered Index Seek` | `Clustered Index Seek` | 1.0 | 1.0 | 0.2632 | `dbo.CUSTOMER [PK_CUSTOMER]` |

---

## CASO-08

- **Entorno:** Escenario de laboratorio SQL Server
- **Métricas de ejecución:** ejecuciones=`60`, consumo ponderado en ventana=`4.2` %, filas devueltas promedio=`2500`
- **Costo total estimado del subárbol:** `2.9` | **Nivel optimización:** `N/A` (aborto temprano: `ninguno`)

### Consulta SQL
```sql
SELECT AuditId, Detail FROM dbo.HeapAudit WHERE CorrelationKey = @k
```

### Resumen estructural del plan Showplan XML

| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |
|---:|---|---|---:|---:|---:|---|
| #0 | `Nested Loops` | `Inner Join` | 2500.0 | - | 2.9000 | `-` |
| #1 | `Index Seek` | `Index Seek` | 2500.0 | 2500.0 | 0.0340 | `dbo.HeapAudit [IX_HeapAudit_CorrelationKey]` |
| #4 | `RID Lookup` | `RID Lookup` | 1.0 | - | 2.8000 | `dbo.HeapAudit` |

---

## CASO-09

- **Entorno:** Carga analítica TPC-H
- **Métricas de ejecución:** ejecuciones=`1`, consumo ponderado en ventana=`0.0084` %, filas devueltas promedio=`25`
- **Costo total estimado del subárbol:** `0.0033095` | **Nivel optimización:** `TRIVIAL` (aborto temprano: `ninguno`)

### Consulta SQL
```sql
SELECT -- QD-TPCH:NEG02:
    N_NATIONKEY, N_NAME, N_REGIONKEY FROM dbo.NATION
```

### Resumen estructural del plan Showplan XML

| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |
|---:|---|---|---:|---:|---:|---|
| #0 | `Clustered Index Scan` | `Clustered Index Scan` | 25.0 | 25.0 | 0.0033 | `dbo.NATION [PK_NATION]` |

---

## CASO-10

- **Entorno:** Carga analítica TPC-H
- **Métricas de ejecución:** ejecuciones=`1`, consumo ponderado en ventana=`9.5636` %, filas devueltas promedio=`0`
- **Costo total estimado del subárbol:** `9.61438` | **Nivel optimización:** `FULL` (aborto temprano: `TimeOut`)

### Consulta SQL
```sql
SELECT -- QD-TPCH:Q20:
    S.S_NAME,
    S.S_ADDRESS
FROM dbo.SUPPLIER S
JOIN dbo.NATION N ON S.S_NATIONKEY = N.N_NATIONKEY
WHERE S.S_SUPPKEY IN (
    SELECT PS.PS_SUPPKEY
    FROM dbo.PARTSUPP PS
    WHERE PS.PS_PARTKEY IN (
        SELECT P.P_PARTKEY
        FROM dbo.PART P
        WHERE P.P_NAME LIKE 'part green%'
    )
      AND PS.PS_AVAILQTY > (
          SELECT 0.5 * SUM(L.L_QUANTITY)
          FROM dbo.LINEITEM L
          WHERE L.L_PARTKEY = PS.PS_PARTKEY
            AND L.L_SUPPKEY = PS.PS_SUPPKEY
            AND L.L_SHIPDATE >= DATEFROMPARTS(1994, 1, 1)
            AND L.L_SHIPDATE < DATEADD(YEAR, 1, DATEFROMPARTS(1994, 1, 1))
      )
)
  AND N.N_NAME = 'CANADA'
ORDER BY S.S_NAME ASC
```

### Resumen estructural del plan Showplan XML

| Nodo | Operador físico | Operador lógico | Filas est. | Lecturas est. | Costo subárbol | Objeto |
|---:|---|---|---:|---:|---:|---|
| #0 | `Sort` | `Sort` | 36.8 | - | 9.6144 | `-` |
| #1 | `Hash Match` | `Inner Join` | 36.8 | - | 9.6027 | `-` |
| #2 | `Clustered Index Scan` | `Clustered Index Scan` | 1.0 | 25.0 | 0.0033 | `dbo.NATION [PK_NATION]` |
| #4 | `Merge Join` | `Inner Join` | 921.0 | - | 9.5774 | `-` |
| #5 | `Stream Aggregate` | `Aggregate` | 921.0 | - | 9.5536 | `-` |
| #6 | `Filter` | `Filter` | 2497.3 | - | 9.5519 | `-` |
| #7 | `Stream Aggregate` | `Aggregate` | 2474.3 | - | 9.5503 | `-` |
| #8 | `Sort` | `Sort` | 2501.8 | - | 9.5475 | `-` |
| #9 | `Hash Match` | `Inner Join` | 2501.8 | - | 9.4921 | `-` |
| #11 | `Merge Join` | `Inner Join` | 16000.0 | - | 0.8896 | `-` |
| #12 | `Clustered Index Scan` | `Clustered Index Scan` | 4000.0 | 20000.0 | 0.2490 | `dbo.PART [PK_PART]` |
| #13 | `Clustered Index Scan` | `Clustered Index Scan` | 80000.0 | 80000.0 | 0.4394 | `dbo.PARTSUPP [PK_PARTSUPP]` |
| #14 | `Clustered Index Scan` | `Clustered Index Scan` | 92471.3 | 600000.0 | 7.1040 | `dbo.LINEITEM [PK_LINEITEM]` |
| #15 | `Clustered Index Scan` | `Clustered Index Scan` | 1000.0 | 1000.0 | 0.0140 | `dbo.SUPPLIER [PK_SUPPLIER]` |

**Sugerencias XML `<MissingIndexes>` del optimizador SQL Server:**
- `Impacto 73.2% en dbo.LINEITEM: INEQ(L_SHIPDATE) INCLUDE(L_PARTKEY, L_SUPPKEY, L_QUANTITY)`
- `Impacto 89.4% en dbo.LINEITEM: EQ(L_PARTKEY, L_SUPPKEY) INEQ(L_SHIPDATE) INCLUDE(L_QUANTITY)`

---
