/*
  S3-07 - Deja el laboratorio como lo esperan S1-07 y S3-06 al terminar la medicion de ingesta (o si se corta):
  intervalo de Query Store de 1 minuto (01-database.sql), sesion de Extended Events borrada, umbral de procesos
  bloqueados en 0 y "show advanced options" en 0 (sus valores por defecto). Los datos de Query Store no se recuperan: hay que volver a correr la carga
  del laboratorio (lab/Run-Lab.ps1 -Step workload). Ejecutada como sa por lab/Run-Perf.ps1. Idempotente.
*/
SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'qd_perf_s307')
    DROP EVENT SESSION qd_perf_s307 ON SERVER;
GO

EXEC sys.sp_configure N'show advanced options', 1;
RECONFIGURE;
EXEC sys.sp_configure N'blocked process threshold (s)', 0;
RECONFIGURE;
EXEC sys.sp_configure N'show advanced options', 0;
RECONFIGURE;
GO

ALTER DATABASE QD_Lab SET QUERY_STORE (INTERVAL_LENGTH_MINUTES = 1);
GO

SELECT actual_state_desc, interval_length_minutes FROM QD_Lab.sys.database_query_store_options;
GO
