/*
  S1-07 - Base de laboratorio QD_Lab con Query Store configurado para el protocolo.
  CONFIGURACION SOLO DE LABORATORIO (no representa produccion, ver RA-07):
    capture ALL, intervalo de 1 minuto, flush corto. Idempotente.
*/
SET NOCOUNT ON;

IF DB_ID(N'QD_Lab') IS NOT NULL
BEGIN
    ALTER DATABASE QD_Lab SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE QD_Lab;
END;
GO

CREATE DATABASE QD_Lab;
GO

ALTER DATABASE QD_Lab SET COMPATIBILITY_LEVEL = 160;
ALTER DATABASE QD_Lab SET RECOVERY SIMPLE;
ALTER DATABASE QD_Lab SET QUERY_STORE = ON;
ALTER DATABASE QD_Lab SET QUERY_STORE
(
    OPERATION_MODE = READ_WRITE,
    QUERY_CAPTURE_MODE = ALL,
    INTERVAL_LENGTH_MINUTES = 1,
    DATA_FLUSH_INTERVAL_SECONDS = 60,
    MAX_STORAGE_SIZE_MB = 512,
    CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30)
);
GO

SELECT @@VERSION AS server_version;
SELECT actual_state_desc, interval_length_minutes, query_capture_mode_desc,
       flush_interval_seconds
FROM QD_Lab.sys.database_query_store_options;
GO
