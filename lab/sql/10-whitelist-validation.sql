/*
  S1-07 - Validacion de las 6 sentencias constantes de TelemetryStatementWhitelist (S1-08)
  contra el SQL Server 2022 de laboratorio, usando qd_reader (solo VIEW DATABASE STATE).
*/
SET NOCOUNT ON;
USE QD_Lab;
GO

PRINT '=== 1. DetectEngineVersionSql';
SELECT CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(128)) AS ProductVersion,
       CAST(SERVERPROPERTY('ProductLevel') AS NVARCHAR(128)) AS ProductLevel,
       CAST(SERVERPROPERTY('Edition') AS NVARCHAR(128)) AS Edition,
       CAST(SERVERPROPERTY('EngineEdition') AS INT) AS EngineEdition;
GO

PRINT '=== 2. GetQueryStoreOptionsSql';
SELECT actual_state, actual_state_desc, readonly_reason,
       current_storage_size_mb, max_storage_size_mb, query_capture_mode_desc, size_based_cleanup_mode_desc
FROM sys.database_query_store_options;
GO

PRINT '=== 3. CheckUserPermissionsSql';
SELECT permission_name FROM fn_my_permissions(NULL, 'DATABASE');
GO

PRINT '=== 4. GetTopQueriesByTotalDurationSql (@TopCount=10, @MinExecutionTime hace 1 dia)';
DECLARE @TopCount int = 10;
DECLARE @MinExecutionTime datetimeoffset(7) = DATEADD(DAY, -1, SYSDATETIMEOFFSET());
SELECT TOP (@TopCount) q.query_id, q.query_text_id, LEFT(qt.query_sql_text, 65) AS sql_preview,
       SUM(rs.count_executions) AS total_executions,
       SUM(rs.avg_duration * rs.count_executions) AS total_duration_us,
       SUM(rs.avg_cpu_time * rs.count_executions) AS total_cpu_time_us,
       SUM(rs.avg_logical_io_reads * rs.count_executions) AS total_logical_reads
FROM sys.query_store_query q
JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
JOIN sys.query_store_plan p ON q.query_id = p.query_id
JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
WHERE rs.last_execution_time >= @MinExecutionTime
GROUP BY q.query_id, q.query_text_id, qt.query_sql_text
ORDER BY total_duration_us DESC;
GO

PRINT '=== 5. GetPlanXmlByPlanIdSql (@PlanId = primer plan de ESC01)';
DECLARE @PlanId bigint = (
    SELECT TOP (1) p.plan_id
    FROM sys.query_store_plan p
    JOIN sys.query_store_query q ON q.query_id = p.query_id
    JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id
    WHERE qt.query_sql_text LIKE N'%/* QD-LAB:ESC01 */%' AND qt.query_sql_text NOT LIKE N'%sys.query_store%'
    ORDER BY p.plan_id
);
SELECT p.plan_id, p.query_id, LEN(p.query_plan) AS plan_len_chars
FROM sys.query_store_plan p WHERE p.plan_id = @PlanId;
GO

PRINT '=== 6. GetQueryStoreViewColumnsSql (@ViewName = query_store_runtime_stats)';
DECLARE @ViewName sysname = N'query_store_runtime_stats';
SELECT c.name AS column_name FROM sys.columns c
JOIN sys.objects o ON c.object_id = o.object_id
WHERE o.name = @ViewName AND SCHEMA_NAME(o.schema_id) = 'sys'
ORDER BY c.column_id;
GO

PRINT '=== 6b. Comparacion: sys.all_columns vs sys.columns para vistas de sistema';
DECLARE @ViewName2 sysname = N'query_store_runtime_stats';
SELECT
    (SELECT COUNT(*) FROM sys.columns c JOIN sys.objects o ON c.object_id = o.object_id WHERE o.name = @ViewName2 AND SCHEMA_NAME(o.schema_id) = 'sys') AS filas_con_sys_columns,
    (SELECT COUNT(*) FROM sys.all_columns c JOIN sys.all_objects o ON c.object_id = o.object_id WHERE o.name = @ViewName2 AND SCHEMA_NAME(o.schema_id) = 'sys') AS filas_con_sys_all_columns;
GO
