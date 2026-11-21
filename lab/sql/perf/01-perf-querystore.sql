/*
  S3-07 (RNF-RND-01, RNF-RND-02) - Query Store de QD_Lab preparado para 5 000 consultas.
  PREPARACION DEL LABORATORIO, ejecutada como sa por lab/Run-Perf.ps1 (CA-HU02.3: la plataforma nunca cambia la
  configuracion de Query Store; esto lo hace el laboratorio antes de medir).
    - QUERY_CAPTURE_MODE = ALL: con AUTO, SQL Server descarta consultas baratas o de una sola ejecucion y no
      llegariamos a 5 000 (hallazgo F5 de S1-07).
    - MAX_STORAGE_SIZE_MB = 2048 y SIZE_BASED_CLEANUP_MODE = OFF: que la limpieza por tamano no borre consultas
      a mitad de la prueba. Si Query Store se llenara pasaria a READ_ONLY; por eso se verifica el estado al final.
  No borra lo que ya hay (los escenarios ESC/ACC siguen sirviendo a S1-07 y S3-06). Idempotente.
*/
SET NOCOUNT ON;

IF DB_ID(N'QD_Lab') IS NULL
    RAISERROR(N'Falta QD_Lab: corre antes lab/Run-Lab.ps1 -Step build y -Step login.', 16, 1);
GO

ALTER DATABASE QD_Lab SET QUERY_STORE = ON;
ALTER DATABASE QD_Lab SET QUERY_STORE
(
    OPERATION_MODE = READ_WRITE,
    QUERY_CAPTURE_MODE = ALL,
    MAX_STORAGE_SIZE_MB = 2048,
    SIZE_BASED_CLEANUP_MODE = OFF
);
GO

SELECT actual_state_desc, desired_state_desc, query_capture_mode_desc, max_storage_size_mb,
       current_storage_size_mb, size_based_cleanup_mode_desc, interval_length_minutes, flush_interval_seconds
FROM QD_Lab.sys.database_query_store_options;
GO
