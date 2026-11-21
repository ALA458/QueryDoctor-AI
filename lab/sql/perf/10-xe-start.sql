/*
  S3-07 (RNF-RND-01) - Sesion de Extended Events que mide la sobrecarga de la plataforma sobre SQL Server.
  SOLO LABORATORIO, ejecutada como sa por lab/Run-Perf.ps1 antes de cada ingesta medida. No forma parte de la
  plataforma: la plataforma nunca crea sesiones XE ni cambia la configuracion del servidor (invariante 1).
    - rpc_completed y sql_batch_completed de la aplicacion "QueryDoctor-AI Telemetry Gateway" (el ApplicationName que
      fija QueryStoreGateway): cpu_time de cada sentencia de la plataforma.
    - wait_completed de esperas de bloqueo (LCK_M_*) de CUALQUIER sesion, con su aplicacion: si la plataforma hiciera
      esperar a la carga de usuario, aparece aqui.
    - blocked_process_report con umbral de 1 s: quien bloquea a quien, con la aplicacion del que bloquea.
  El filtro de wait_completed se arma con las claves de sys.dm_xe_map_values (SQL dinamico del laboratorio).
*/
SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'qd_perf_s307')
    DROP EVENT SESSION qd_perf_s307 ON SERVER;
GO

EXEC sys.sp_configure N'show advanced options', 1;
RECONFIGURE;
EXEC sys.sp_configure N'blocked process threshold (s)', 1;
RECONFIGURE;
GO

DECLARE @lockKeys nvarchar(max) =
(
    SELECT STRING_AGG(N'[wait_type] = (' + CAST(map_key AS nvarchar(10)) + N')', N' OR ')
    FROM sys.dm_xe_map_values
    WHERE name = N'wait_types' AND map_value LIKE N'LCK[_]M[_]%'
);

DECLARE @app nvarchar(100) = N'QueryDoctor-AI Telemetry Gateway';
DECLARE @ddl nvarchar(max) = N'
CREATE EVENT SESSION qd_perf_s307 ON SERVER
ADD EVENT sqlserver.rpc_completed (
    ACTION (sqlserver.client_app_name, sqlserver.session_id)
    WHERE sqlserver.client_app_name = N''' + @app + N'''),
ADD EVENT sqlserver.sql_batch_completed (
    ACTION (sqlserver.client_app_name, sqlserver.session_id)
    WHERE sqlserver.client_app_name = N''' + @app + N'''),
ADD EVENT sqlos.wait_completed (
    ACTION (sqlserver.client_app_name, sqlserver.session_id, sqlserver.database_name)
    WHERE (' + @lockKeys + N')),
ADD EVENT sqlserver.blocked_process_report
ADD TARGET package0.event_file (SET filename = N''/var/opt/mssql/log/qd_perf_s307.xel'', max_file_size = 256, max_rollover_files = 4)
WITH (MAX_DISPATCH_LATENCY = 1 SECONDS, EVENT_RETENTION_MODE = ALLOW_SINGLE_EVENT_LOSS, STARTUP_STATE = OFF);';

EXEC sys.sp_executesql @ddl;
ALTER EVENT SESSION qd_perf_s307 ON SERVER STATE = START;
GO

SELECT s.name, CAST(SYSUTCDATETIME() AS datetime2(3)) AS iniciada_utc
FROM sys.dm_xe_sessions s WHERE s.name = N'qd_perf_s307';
GO
