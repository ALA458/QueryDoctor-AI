/*
  S1-07 / F5 - Valores por defecto de Query Store en SQL Server 2022.
  Se ejecuta ANTES de configurar nada: crea una BD nueva SIN tocar Query Store
  y registra lo que el motor deja por defecto (estado, intervalo, retencion, captura).
  Solo laboratorio. Ejecutar como administrador del lab (sa).
*/
SET NOCOUNT ON;

IF DB_ID(N'QD_Defaults') IS NOT NULL
BEGIN
    ALTER DATABASE QD_Defaults SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE QD_Defaults;
END;
GO

CREATE DATABASE QD_Defaults;
GO

SELECT @@VERSION AS server_version;

SELECT name, compatibility_level, is_query_store_on
FROM sys.databases
WHERE name = N'QD_Defaults';
GO

USE QD_Defaults;
GO

SELECT actual_state_desc, desired_state_desc, interval_length_minutes,
       stale_query_threshold_days, query_capture_mode_desc,
       size_based_cleanup_mode_desc, max_storage_size_mb,
       flush_interval_seconds
FROM sys.database_query_store_options;
GO

USE master;
GO

DROP DATABASE QD_Defaults;
GO
