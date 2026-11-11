-- S1-07 - Escenarios sembrados.
-- Cada sentencia lleva una etiqueta QD-LAB:ESCxx dentro de su texto para localizarla
-- en sys.query_store_query_text.
USE QD_Lab;
GO

-- ESC01: conversion implicita. CustomerCode es varchar; el parametro es nvarchar.
CREATE OR ALTER PROCEDURE dbo.usp_ESC01 @code nvarchar(20)
AS
BEGIN
    SELECT /* QD-LAB:ESC01 */ OrderId, Amount
    FROM dbo.Orders
    WHERE CustomerCode = @code;
END;
GO

-- ESC02: Key Lookup. Selecciona columnas que el indice no cubre.
CREATE OR ALTER PROCEDURE dbo.usp_ESC02 @id int
AS
BEGIN
    SELECT /* QD-LAB:ESC02 */ OrderId, Amount, CreatedAt
    FROM dbo.Orders
    WHERE CustomerId = @id;
END;
GO

-- ESC03: indice faltante. Region sin indice.
CREATE OR ALTER PROCEDURE dbo.usp_ESC03 @region int
AS
BEGIN
    SELECT /* QD-LAB:ESC03 */ OrderId, Amount
    FROM dbo.Orders
    WHERE Region = @region AND Amount > 900;
END;
GO

-- ESC04: sesgo de cardinalidad / parameter sniffing.
-- Ejecutar primero con un valor raro (1500) y luego con 1 (99% de las filas).
CREATE OR ALTER PROCEDURE dbo.usp_ESC04 @k int
AS
BEGIN
    SELECT /* QD-LAB:ESC04 */ OrderId, Amount, CreatedAt
    FROM dbo.Orders
    WHERE SkewKey = @k;
END;
GO

-- ESC05: sort con spill. Ordena filas anchas con concesion de memoria minima.
CREATE OR ALTER PROCEDURE dbo.usp_ESC05
AS
BEGIN
    SELECT /* QD-LAB:ESC05 */ OrderId, Note, Amount
    FROM dbo.Orders
    ORDER BY Note DESC, Amount
    OPTION (MAX_GRANT_PERCENT = 0.001);
END;
GO

-- ESC06 (F8): plan profundamente anidado. Cadena de 140 joins con FORCE ORDER.
CREATE OR ALTER PROCEDURE dbo.usp_ESC06
AS
BEGIN
    DECLARE @n int = 140, @i int = 2;
    DECLARE @sql nvarchar(max) =
        N'SELECT /* QD-LAB:ESC06 */ t1.Id FROM dbo.Tiny t1';
    WHILE @i <= @n
    BEGIN
        SET @sql += N' JOIN dbo.Tiny t' + CAST(@i AS nvarchar(10))
                  + N' ON t' + CAST(@i AS nvarchar(10)) + N'.Id = t1.Id';
        SET @i += 1;
    END;
    SET @sql += N' OPTION (FORCE ORDER);';
    EXEC sys.sp_executesql @sql;
END;
GO

-- ESC04B: igual que ESC04 pero SIN optimizacion sensible a parametros (PSP exige compat. 160; el hint de nivel 150 la desactiva),
-- para provocar el sniffing clasico: un plan reutilizado con estimacion lejana a la realidad.
CREATE OR ALTER PROCEDURE dbo.usp_ESC04B @k int
AS
BEGIN
    SELECT /* QD-LAB:ESC04B */ OrderId, Amount, CreatedAt
    FROM dbo.Orders
    WHERE SkewKey = @k
    OPTION (USE HINT('QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_150'));
END;
GO

