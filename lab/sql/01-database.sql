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

-- S3-06: el nivel de compatibilidad maximo de cada version (160 en SQL Server 2022, 150 en 2019), para que el mismo
-- laboratorio sirva a la matriz de aceptacion (RNF-COM-01). En 2022 sigue siendo 160: los fixtures de S1-07 no cambian.
-- Es un script del laboratorio ejecutado como sa, no una sentencia de la plataforma (invariante 1 no aplica).
DECLARE @compat nvarchar(3) =
    CASE WHEN CAST(SERVERPROPERTY('ProductMajorVersion') AS int) >= 16 THEN N'160' ELSE N'150' END;
EXEC (N'ALTER DATABASE QD_Lab SET COMPATIBILITY_LEVEL = ' + @compat + N';');
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
