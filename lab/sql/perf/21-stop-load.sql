/*
  S3-07 - Corta la carga concurrente (sesiones abiertas con sqlcmd -H QD-PERF-LOAD). SOLO LABORATORIO, como sa.
  El KILL se arma con los session_id del momento (SQL dinamico del laboratorio, nunca de la plataforma).
*/
SET NOCOUNT ON;

DECLARE @kill nvarchar(max) =
(
    SELECT STRING_AGG(CONCAT(N'KILL ', session_id, N';'), N' ')
    FROM sys.dm_exec_sessions
    WHERE host_name = N'QD-PERF-LOAD' AND session_id <> @@SPID
);

IF @kill IS NOT NULL
    EXEC sys.sp_executesql @kill;

SELECT COUNT(*) AS sesiones_de_carga_restantes
FROM sys.dm_exec_sessions
WHERE host_name = N'QD-PERF-LOAD' AND session_id <> @@SPID;
GO
