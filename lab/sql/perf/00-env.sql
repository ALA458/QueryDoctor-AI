/*
  S3-07 - Entorno del SQL Server de laboratorio para la seccion "Entorno" del informe. Solo lectura, como sa.
  Cada fila empieza con una clave que Run-Perf.ps1 copia a lab/results/raw/perf/environment.txt.
*/
SET NOCOUNT ON;

SELECT CONCAT(N'sqlserver: ', REPLACE(REPLACE(@@VERSION, CHAR(10), N' '), CHAR(13), N' '));
SELECT CONCAT(N'cpu_count: ', cpu_count) FROM sys.dm_os_sys_info;
SELECT CONCAT(N'scheduler_count: ', scheduler_count) FROM sys.dm_os_sys_info;
SELECT CONCAT(N'memoria_mb: ', physical_memory_kb / 1024) FROM sys.dm_os_sys_info;
SELECT CONCAT(N'querystore: ', actual_state_desc, N', captura ', query_capture_mode_desc, N', intervalo ',
              interval_length_minutes, N' min, ', current_storage_size_mb, N'/', max_storage_size_mb, N' MB')
FROM QD_Lab.sys.database_query_store_options;
GO
