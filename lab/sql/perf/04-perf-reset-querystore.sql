/*
  S3-07 - Base limpia del lado de SQL Server para cada repeticion de la medicion de ingesta.
  PREPARACION DEL LABORATORIO, ejecutada como sa por lab/Run-Perf.ps1 -Step ingest (CA-HU02.3: la plataforma nunca
  cambia Query Store).
    - QUERY_STORE CLEAR: cada repeticion parte de un Query Store vacio y la carga 02 lo llena igual. Borra tambien los
      escenarios ESC/ACC: despues de S3-07 hay que volver a correr lab/Run-Lab.ps1 -Step workload (y la carga 12 de
      S3-06) antes de las suites LabTests/AcceptanceTests.
    - INTERVAL_LENGTH_MINUTES = 60 (supuesto S8 de S3-07): con 1 minuto, el sondeo programa una ingesta automatica
      cada minuto ("* * * * *") que se meteria en medio de la medicion. Con 60 queda en el minuto :05 de cada hora,
      que Run-Perf.ps1 evita. 60 es ademas el valor por defecto de SQL Server. 09-perf-restore.sql lo devuelve a 1.
*/
SET NOCOUNT ON;

IF DB_ID(N'QD_Lab') IS NULL
    RAISERROR(N'Falta QD_Lab: corre antes lab/Run-Lab.ps1 -Step build y -Step login.', 16, 1);
GO

ALTER DATABASE QD_Lab SET QUERY_STORE CLEAR;
ALTER DATABASE QD_Lab SET QUERY_STORE
(
    OPERATION_MODE = READ_WRITE,
    QUERY_CAPTURE_MODE = ALL,
    INTERVAL_LENGTH_MINUTES = 60,
    MAX_STORAGE_SIZE_MB = 2048,
    SIZE_BASED_CLEANUP_MODE = OFF
);
GO

SELECT actual_state_desc, query_capture_mode_desc, interval_length_minutes, current_storage_size_mb
FROM QD_Lab.sys.database_query_store_options;
GO
