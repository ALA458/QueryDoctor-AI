/*
  S1-07 - Verificaciones como qd_reader (solo VIEW DATABASE STATE): F4(a), F6 (permiso extra), F7.
*/
SET NOCOUNT ON;
USE QD_Lab;
GO
PRINT '=== usuario actual y permisos efectivos en la BD';
SELECT USER_NAME() AS usuario;
SELECT permission_name FROM fn_my_permissions(NULL, 'DATABASE') ORDER BY permission_name;
GO

PRINT '=== F4(a): lectura de las vistas de Query Store con VIEW DATABASE STATE';
SELECT (SELECT COUNT(*) FROM sys.query_store_query)         AS queries,
       (SELECT COUNT(*) FROM sys.query_store_plan)          AS plans,
       (SELECT COUNT(*) FROM sys.query_store_runtime_stats) AS runtime_stats,
       (SELECT COUNT(*) FROM sys.query_store_query_text)    AS texts;
SELECT actual_state_desc, query_capture_mode_desc, interval_length_minutes FROM sys.database_query_store_options;
GO

PRINT '=== F4(d): una escritura debe fallar';
BEGIN TRY
    INSERT dbo.Tiny (Id, V) VALUES (999, 999);
    PRINT 'ERROR: la escritura NO fallo';
END TRY
BEGIN CATCH
    SELECT ERROR_NUMBER() AS error_numero, ERROR_MESSAGE() AS error_mensaje;
END CATCH;
GO

PRINT '=== F4(a): lectura de planes como nvarchar(max) (sentencia de la whitelist GetPlanXmlByPlanId)';
SELECT TOP (3) p.plan_id, p.query_id, LEN(p.query_plan) AS len FROM sys.query_store_plan p ORDER BY p.plan_id;
GO

PRINT '=== F6: dm_exec_query_plan_stats requiere permiso de servidor (se espera error)';
BEGIN TRY
    SELECT TOP (1) ps.query_plan
    FROM sys.dm_exec_cached_plans cp CROSS APPLY sys.dm_exec_query_plan_stats(cp.plan_handle) ps;
    PRINT 'ERROR: se pudo leer sin permiso de servidor';
END TRY
BEGIN CATCH
    SELECT ERROR_NUMBER() AS error_numero, ERROR_MESSAGE() AS error_mensaje;
END CATCH;
GO
