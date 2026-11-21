/*
  S3-07 (RNF-RND-01, RNF-RND-02) - Carga de 5 000 consultas DISTINTAS sobre datos sinteticos de QD_Lab.
  SOLO LABORATORIO, ejecutada como sa por lab/Run-Perf.ps1. El SQL dinamico es del script de laboratorio, no de la
  plataforma (la plataforma solo lee Query Store con la lista blanca; invariante 1).

  Cada consulta tiene un texto unico: empieza con SELECT y el comentario marcador QD-PERF:nnnnn, y lleva un rango de
  OrderId propio. Las 10 formas usan GROUP BY o TOP para que SQL Server NO aplique la parametrizacion simple (que
  juntaria textos distintos en una sola consulta de Query Store). Todas son busquedas por rango sobre la clave
  primaria: baratas, para que la carga dure poco.

  Se corre dos veces: la primera llena Query Store para la primera ingesta; la segunda (mas de 1 minuto despues,
  INTERVAL_LENGTH_MINUTES = 1) agrega intervalos nuevos para la ingesta incremental. Mismos textos = mismas consultas.
  El marcador se arma por partes para que el texto de este lote no cuente como consulta de la prueba.
*/
SET NOCOUNT ON;
USE QD_Lab;
GO

DECLARE @total int = 5000;
DECLARE @i int = 1;
DECLARE @lo int, @hi int, @shape int, @body nvarchar(400), @sql nvarchar(600), @tag nchar(5);
DECLARE @marker nvarchar(20) = N'/* QD-' + N'PERF:';
DECLARE @t0 datetime2 = SYSUTCDATETIME();

WHILE @i <= @total
BEGIN
    SET @lo = ((@i * 197) % 990000) + 1;
    SET @hi = @lo + 50 + (@i % 200);
    SET @shape = @i % 10;
    SET @tag = RIGHT(N'0000' + CAST(@i AS nvarchar(5)), 5);
    DECLARE @range nvarchar(60) = N'o.OrderId BETWEEN ' + CAST(@lo AS nvarchar(10)) + N' AND ' + CAST(@hi AS nvarchar(10));

    SET @body = CASE @shape
        WHEN 0 THEN N'o.Region, COUNT(*) AS n FROM dbo.Orders o WHERE ' + @range + N' GROUP BY o.Region'
        WHEN 1 THEN N'o.CustomerId, SUM(o.Amount) AS total FROM dbo.Orders o WHERE ' + @range + N' GROUP BY o.CustomerId'
        WHEN 2 THEN N'TOP (10) o.OrderId, o.Amount FROM dbo.Orders o WHERE ' + @range + N' ORDER BY o.Amount DESC'
        WHEN 3 THEN N'o.SkewKey, AVG(o.Amount) AS media FROM dbo.Orders o WHERE ' + @range + N' GROUP BY o.SkewKey'
        WHEN 4 THEN N't.V, COUNT(*) AS n FROM dbo.Orders o JOIN dbo.Tiny t ON t.Id = (o.Region % 3) + 1 WHERE ' + @range + N' GROUP BY t.V'
        WHEN 5 THEN N'CAST(o.CreatedAt AS date) AS dia, COUNT(*) AS n FROM dbo.Orders o WHERE ' + @range + N' GROUP BY CAST(o.CreatedAt AS date)'
        WHEN 6 THEN N'TOP (5) o.CustomerCode, o.CreatedAt FROM dbo.Orders o WHERE ' + @range + N' ORDER BY o.CreatedAt DESC'
        WHEN 7 THEN N'o.Region, MAX(o.Amount) AS maximo FROM dbo.Orders o WHERE ' + @range + N' GROUP BY o.Region HAVING COUNT(*) > 1'
        WHEN 8 THEN N'TOP (20) o.OrderId, o.Region FROM dbo.Orders o WHERE ' + @range + N' ORDER BY o.OrderId DESC'
        ELSE N'o.Region, o.SkewKey, MIN(o.Amount) AS minimo FROM dbo.Orders o WHERE ' + @range + N' GROUP BY o.Region, o.SkewKey'
    END;

    SET @sql = N'SELECT ' + @marker + @tag + N' */ ' + @body + N';';
    EXEC sys.sp_executesql @sql;

    IF @i % 500 = 0
        RAISERROR(N'  %d / %d consultas ejecutadas', 0, 1, @i, @total) WITH NOWAIT;

    SET @i += 1;
END;

DECLARE @secs int = DATEDIFF(SECOND, @t0, SYSUTCDATETIME());
RAISERROR(N'  Carga terminada en %d s', 0, 1, @secs) WITH NOWAIT;
GO

EXEC QD_Lab.sys.sp_query_store_flush_db;
GO
