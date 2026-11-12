/*
  S1-07 - Verificaciones como administrador del lab (sa): F3, F6, F8, H4.
  Salida cruda a lab/results/raw/checks-sa.txt
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE QD_Lab;
GO
-- Estado conocido: el protocolo parte con la opcion de plan real DESACTIVADA (por defecto).
ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = OFF;
ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
GO

PRINT '=== F3: columnas de sys.query_store_runtime_stats (buscar spill / por operador)';
SELECT name FROM sys.all_columns WHERE object_id = OBJECT_ID('sys.query_store_runtime_stats')
  AND (name LIKE '%spill%' OR name LIKE '%tempdb%' OR name LIKE '%rowcount%' OR name LIKE '%memory%' OR name LIKE '%dop%' OR name LIKE '%log_bytes%' OR name LIKE '%spill%')
ORDER BY column_id;
GO

PRINT '=== F3: filas de runtime_stats por plan e intervalo (ESC01..ESC06)';
SELECT LEFT(SUBSTRING(t.query_sql_text, CHARINDEX('QD-LAB:', t.query_sql_text), 12), 12) AS escenario,
       rs.plan_id, rs.runtime_stats_interval_id, rs.execution_type_desc,
       rs.count_executions, rs.avg_rowcount, rs.avg_query_max_used_memory, rs.avg_dop, rs.avg_tempdb_space_used
FROM sys.query_store_runtime_stats rs
JOIN sys.query_store_plan p ON p.plan_id = rs.plan_id
JOIN sys.query_store_query q ON q.query_id = p.query_id
JOIN sys.query_store_query_text t ON t.query_text_id = q.query_text_id
WHERE t.query_sql_text LIKE N'%/* QD-LAB:%' AND t.query_sql_text NOT LIKE N'%sys.query_store%' AND t.query_sql_text NOT LIKE N'%sys.dm_exec%'
ORDER BY escenario, rs.plan_id, rs.runtime_stats_interval_id;
GO

PRINT '=== F8: ESC06 como xml vs nvarchar(max)';
SELECT p.plan_id,
       LEN(CAST(p.query_plan AS nvarchar(max)))                 AS len_nvarchar,
       CASE WHEN TRY_CAST(p.query_plan AS xml) IS NULL THEN 'NULL (no convertible a xml)' ELSE 'xml OK' END AS como_xml
FROM sys.query_store_plan p
JOIN sys.query_store_query q ON q.query_id = p.query_id
JOIN sys.query_store_query_text t ON t.query_text_id = q.query_text_id
WHERE t.query_sql_text LIKE N'%/* QD-LAB:ESC06 */%' AND t.query_sql_text NOT LIKE N'%sys.query_store%' AND t.query_sql_text NOT LIKE N'%sys.dm_exec%';
GO

PRINT '=== F8: ESC06 por sys.dm_exec_query_plan (cache)';
SELECT CASE WHEN qp.query_plan IS NULL THEN 'NULL' ELSE 'con plan' END AS dm_exec_query_plan
FROM sys.dm_exec_cached_plans cp
CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
CROSS APPLY sys.dm_exec_query_plan(cp.plan_handle) qp
WHERE st.text LIKE N'%/* QD-LAB:ESC06 */%' AND st.text NOT LIKE N'%dm_exec_cached_plans%';
GO

CREATE TABLE #sink (OrderId int, Amount decimal(12,2));
GO

-- Lista, por plan en cache de usp_ESC01, si dm_exec_query_plan_stats devuelve plan y si trae filas reales.
CREATE OR ALTER PROCEDURE dbo.usp_lab_plan_stats
AS
BEGIN
    SELECT cp.objtype, cp.usecounts,
           CASE WHEN ps.query_plan IS NULL THEN 'NULL' ELSE 'plan' END AS plan_stats,
           CASE WHEN CAST(ps.query_plan AS nvarchar(max)) LIKE N'%ActualRows%' THEN 'si' ELSE 'no' END AS filas_reales
    FROM sys.dm_exec_cached_plans cp
    CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
    OUTER APPLY sys.dm_exec_query_plan_stats(cp.plan_handle) ps
    WHERE st.objectid = OBJECT_ID('dbo.usp_ESC01') AND st.dbid = DB_ID();
END;
GO

PRINT '=== F6: LAST_QUERY_PLAN_STATS (estado inicial)';
SELECT name, value FROM sys.database_scoped_configurations WHERE name = 'LAST_QUERY_PLAN_STATS';
GO

PRINT '=== F6: opcion OFF, cache limpia, se ejecuta ESC01 una vez';
ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
GO
INSERT #sink EXEC dbo.usp_ESC01 @code = N'C000042';
EXEC dbo.usp_lab_plan_stats;
GO

PRINT '=== F6: opcion ON, cache limpia, se ejecuta ESC01 una vez';
ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = ON;
ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
GO
INSERT #sink EXEC dbo.usp_ESC01 @code = N'C000042';
EXEC dbo.usp_lab_plan_stats;
GO

PRINT '=== F6: volatilidad. Se limpia la cache y se vuelve a consultar';
ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
GO
EXEC dbo.usp_lab_plan_stats;
GO
DROP PROCEDURE dbo.usp_lab_plan_stats;
GO
PRINT '=== H4: EstimateRows del plan QS (operador raiz) vs avg_rowcount, por plan';
;WITH x AS (
  SELECT p.plan_id, LEFT(SUBSTRING(t.query_sql_text, CHARINDEX('QD-LAB:', t.query_sql_text), 12), 12) AS escenario,
         TRY_CAST(p.query_plan AS xml) AS px
  FROM sys.query_store_plan p
  JOIN sys.query_store_query q ON q.query_id = p.query_id
  JOIN sys.query_store_query_text t ON t.query_text_id = q.query_text_id
  WHERE t.query_sql_text LIKE N'%/* QD-LAB:ESC0[1-5]%' AND t.query_sql_text NOT LIKE N'%sys.query_store%')
SELECT x.escenario, x.plan_id,
  x.px.value('declare namespace s="http://schemas.microsoft.com/sqlserver/2004/07/showplan"; (//s:StmtSimple/@StatementEstRows)[1]', 'float') AS est_rows_sentencia,
  (SELECT SUM(rs.avg_rowcount * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0)
     FROM sys.query_store_runtime_stats rs WHERE rs.plan_id = x.plan_id) AS avg_rowcount_ponderado
FROM x ORDER BY x.escenario, x.plan_id;
GO

PRINT '=== Restaurar LAST_QUERY_PLAN_STATS = OFF';
ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = OFF;
GO

