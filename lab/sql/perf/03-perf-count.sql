/*
  S3-07 - Verificacion previa a medir: cuantas consultas de la prueba capturo Query Store de verdad.
  Solo lectura de vistas de catalogo de QD_Lab, ejecutada como sa por lab/Run-Perf.ps1.
  El patron esta anclado al inicio (SELECT seguido del comentario marcador QD-PERF:nnnnn), asi que este mismo lote,
  que Query Store tambien captura con QUERY_CAPTURE_MODE = ALL, no se cuenta.
  (Nota: sin comentarios anidados en esta cabecera; sqlcmd no los entiende y deja de reconocer los GO.)
  La ultima fila (PERF_COUNT=..., PERF_TEXTS=..., PERF_RECENT=...) la lee Run-Perf.ps1; con menos de 5 000 no mide.
*/
SET NOCOUNT ON;
USE QD_Lab;
GO

DECLARE @pattern nvarchar(80) = N'SELECT /* QD-PERF:[0-9][0-9][0-9][0-9][0-9] */%';

DECLARE @perfQueries int, @perfTexts int, @perfRecent int, @allQueries int;

SELECT @perfQueries = COUNT(DISTINCT q.query_id),
       @perfTexts = COUNT(DISTINCT t.query_text_id),
       @perfRecent = COUNT(DISTINCT CASE WHEN q.last_execution_time >= DATEADD(HOUR, -24, SYSDATETIMEOFFSET()) THEN q.query_id END)
FROM sys.query_store_query q
JOIN sys.query_store_query_text t ON t.query_text_id = q.query_text_id
WHERE t.query_sql_text LIKE @pattern;

SELECT @allQueries = COUNT(*) FROM sys.query_store_query;

SELECT actual_state_desc, query_capture_mode_desc, max_storage_size_mb, current_storage_size_mb,
       size_based_cleanup_mode_desc
FROM sys.database_query_store_options;

SELECT @allQueries AS consultas_totales_query_store,
       @perfQueries AS consultas_qd_perf,
       @perfTexts AS textos_distintos_qd_perf,
       @perfRecent AS qd_perf_ejecutadas_ultimas_24h;

SELECT CONCAT(N'PERF_COUNT=', @perfQueries, N' PERF_TEXTS=', @perfTexts, N' PERF_RECENT=', @perfRecent,
              N' QS_TOTAL=', @allQueries) AS resumen;
GO
