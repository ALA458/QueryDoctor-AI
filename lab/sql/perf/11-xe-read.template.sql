/*
  S3-07 (RNF-RND-01) - Detiene la sesion qd_perf_s307 y resume lo que capturo en la ventana del ciclo de ingesta.
  PLANTILLA: lab/Run-Perf.ps1 reemplaza __FROM_UTC__ y __TO_UTC__ (inicio y fin de la ejecucion de ingesta segun la
  plataforma, en UTC) antes de ejecutarla como sa. Solo lee el archivo .xel y vistas del sistema.
  La ultima fila (XE_SUMMARY ...) la lee Run-Perf.ps1. cpu_time y duration de rpc/batch vienen en microsegundos; la
  duracion de wait_completed, en milisegundos.
*/
SET NOCOUNT ON;

DECLARE @from datetime2(7) = '__FROM_UTC__';
DECLARE @to datetime2(7) = '__TO_UTC__';
DECLARE @app nvarchar(100) = N'QueryDoctor-AI Telemetry Gateway';

DECLARE @dropped bigint =
    (SELECT ISNULL(SUM(dropped_event_count + dropped_buffer_count), 0) FROM sys.dm_xe_sessions WHERE name = N'qd_perf_s307');

IF EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE name = N'qd_perf_s307')
    ALTER EVENT SESSION qd_perf_s307 ON SERVER STATE = STOP;

IF OBJECT_ID(N'tempdb..#ev') IS NOT NULL DROP TABLE #ev;

SELECT
    x.e.value(N'(event/@name)[1]', N'nvarchar(60)') AS event_name,
    x.e.value(N'(event/@timestamp)[1]', N'datetime2(7)') AS ts_utc,
    x.e.value(N'(event/data[@name="cpu_time"]/value)[1]', N'bigint') AS cpu_time_us,
    x.e.value(N'(event/data[@name="duration"]/value)[1]', N'bigint') AS duration_raw,
    x.e.value(N'(event/data[@name="logical_reads"]/value)[1]', N'bigint') AS logical_reads,
    x.e.value(N'(event/data[@name="wait_type"]/text)[1]', N'nvarchar(60)') AS wait_type,
    x.e.value(N'(event/action[@name="client_app_name"]/value)[1]', N'nvarchar(128)') AS client_app,
    x.e.value(N'(event/action[@name="session_id"]/value)[1]', N'int') AS session_id,
    x.e.value(N'(event/action[@name="database_name"]/value)[1]', N'nvarchar(128)') AS database_name,
    LEFT(COALESCE(x.e.value(N'(event/data[@name="statement"]/value)[1]', N'nvarchar(max)'),
                  x.e.value(N'(event/data[@name="batch_text"]/value)[1]', N'nvarchar(max)')), 70) AS statement_head,
    x.e.value(N'(event/data[@name="blocked_process"]/value/blocked-process-report/blocking-process/process/@clientapp)[1]',
              N'nvarchar(128)') AS blocking_app,
    x.e.value(N'(event/data[@name="blocked_process"]/value/blocked-process-report/blocked-process/process/@clientapp)[1]',
              N'nvarchar(128)') AS blocked_app
INTO #ev
FROM (
    SELECT CAST(event_data AS xml) AS e
    FROM sys.fn_xe_file_target_read_file(N'/var/opt/mssql/log/qd_perf_s307*.xel', NULL, NULL, NULL)
) AS x;

DECLARE @cpuCount int = (SELECT cpu_count FROM sys.dm_os_sys_info);

-- Sentencias de la plataforma dentro de la ventana, agrupadas por el inicio del texto (son las constantes de la lista
-- blanca y el SET de la sesion, nunca SQL de usuario).
SELECT event_name, statement_head, COUNT(*) AS sentencias, SUM(cpu_time_us) AS cpu_us, SUM(duration_raw) AS duracion_us,
       SUM(logical_reads) AS lecturas_logicas
FROM #ev
WHERE event_name IN (N'rpc_completed', N'sql_batch_completed') AND client_app = @app AND ts_utc BETWEEN @from AND @to
GROUP BY event_name, statement_head
ORDER BY cpu_us DESC;

-- Todas las esperas de bloqueo y los informes de bloqueo de la ventana (se espera que no haya ninguno).
-- duracion: en wait_completed, milisegundos; en blocked_process_report, microsegundos. SQL Server emite un
-- blocked_process_report por cada segundo que dura el mismo bloqueo: el conteo de informes no es el de bloqueos.
SELECT event_name, ts_utc, wait_type, duration_raw AS duracion, client_app, session_id, database_name, blocking_app, blocked_app
FROM #ev
WHERE event_name IN (N'wait_completed', N'blocked_process_report') AND ts_utc BETWEEN @from AND @to
ORDER BY ts_utc;

DECLARE @stmts bigint, @cpu bigint, @dur bigint, @first datetime2(7), @last datetime2(7), @outside bigint;
SELECT @stmts = COUNT(*), @cpu = ISNULL(SUM(cpu_time_us), 0), @dur = ISNULL(SUM(duration_raw), 0),
       @first = MIN(ts_utc), @last = MAX(ts_utc)
FROM #ev
WHERE event_name IN (N'rpc_completed', N'sql_batch_completed') AND client_app = @app AND ts_utc BETWEEN @from AND @to;

SELECT @outside = COUNT(*)
FROM #ev
WHERE event_name IN (N'rpc_completed', N'sql_batch_completed') AND client_app = @app AND ts_utc NOT BETWEEN @from AND @to;

DECLARE @platformLockWaits bigint, @otherLockWaits bigint, @otherLockWaitMs bigint, @blockedTotal bigint, @blockedByPlatform bigint;
SELECT @platformLockWaits = COUNT(*) FROM #ev
WHERE event_name = N'wait_completed' AND client_app = @app AND ts_utc BETWEEN @from AND @to;
SELECT @otherLockWaits = COUNT(*), @otherLockWaitMs = ISNULL(SUM(duration_raw), 0) FROM #ev
WHERE event_name = N'wait_completed' AND ISNULL(client_app, N'') <> @app AND ts_utc BETWEEN @from AND @to;
SELECT @blockedTotal = COUNT(*), @blockedByPlatform = ISNULL(SUM(CASE WHEN blocking_app = @app THEN 1 ELSE 0 END), 0) FROM #ev
WHERE event_name = N'blocked_process_report' AND ts_utc BETWEEN @from AND @to;

SELECT CONCAT(
    N'XE_SUMMARY statements=', @stmts,
    N' cpu_us=', @cpu,
    N' duration_us=', @dur,
    N' cpu_count=', @cpuCount,
    N' first_utc=', ISNULL(CONVERT(nvarchar(30), @first, 126), N'-'),
    N' last_utc=', ISNULL(CONVERT(nvarchar(30), @last, 126), N'-'),
    N' outside_window=', @outside,
    N' platform_lock_waits=', @platformLockWaits,
    N' other_lock_waits=', @otherLockWaits,
    N' other_lock_wait_ms=', @otherLockWaitMs,
    N' blocked_reports=', @blockedTotal,
    N' blocked_by_platform=', @blockedByPlatform,
    N' dropped=', @dropped) AS resumen;

DROP EVENT SESSION qd_perf_s307 ON SERVER;
GO
