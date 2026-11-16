/*
  S3-06 - Escenarios de ACEPTACION (RNF-COM-01). SOLO LABORATORIO. Se ejecuta como sa DESPUES de 05-workload.sql.
  Idempotente: crea o reemplaza los procedimientos y los ejecuta.

  Llevan la marca QD-ACC (no QD-LAB) para que Run-Lab.ps1 -Step extract no los tome como fixtures de S1-07
  ni cambien los hashes de reproducibilidad de lab/results/S1-07-verificacion.md.

  Por que existen (hallazgos de S3-06 al correr la plataforma de punta a punta):
  - ACC01 (CA-HU05.2): ESC02 estima ~20 lookups y su consumo es ~0,03 %, por debajo de los umbrales por defecto de
    REG-04 (1000 ejecuciones estimadas y 1 % de consumo, RuleThresholds). Aqui un rango de CustomerId estima ~2000
    lookups y se repite para superar el 1 % de la ventana. Los umbrales NO se tocan (DA-02, S3-01).
  - ACC02 (CA-HU07.2): en compatibilidad 160 las variantes de PSP de ESC04 son query_id distintos, asi que ninguna
    consulta del laboratorio tiene dos planes. Aqui la misma consulta compila dos planes: primero con un valor raro
    (seek + lookup) y, tras sp_recompile, con el valor dominante (scan). El hint de nivel 150 desactiva PSP.
  Ninguno devuelve filas al cliente (agregados), para no inflar la salida de sqlcmd.
*/
USE QD_Lab;
GO
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ACC01 @from int, @to int
AS
BEGIN
    SELECT /* QD-ACC:ACC01 */ SUM(Amount) AS total, MAX(CreatedAt) AS ultimo
    FROM dbo.Orders WITH (INDEX(IX_Orders_CustomerId))
    WHERE CustomerId BETWEEN @from AND @to;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ACC02 @k int
AS
BEGIN
    SELECT /* QD-ACC:ACC02 */ SUM(Amount) AS total, MAX(CreatedAt) AS ultimo
    FROM dbo.Orders
    WHERE SkewKey = @k
    OPTION (USE HINT('QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_150'));
END;
GO

-- ACC01: 100 clientes x 20 pedidos = ~2000 lookups estimados por ejecucion.
EXEC sys.sp_recompile N'dbo.usp_ACC01';
DECLARE @i int = 0;
WHILE @i < 200
BEGIN
    EXEC dbo.usp_ACC01 @from = 1000, @to = 1099;
    SET @i += 1;
END;
GO

-- ACC02: plan 1 (valor raro: seek + lookup) y plan 2 (valor dominante: scan) para la MISMA consulta.
EXEC sys.sp_recompile N'dbo.usp_ACC02';
EXEC dbo.usp_ACC02 @k = 1500;
EXEC dbo.usp_ACC02 @k = 1500;
EXEC sys.sp_recompile N'dbo.usp_ACC02';
EXEC dbo.usp_ACC02 @k = 1;
EXEC dbo.usp_ACC02 @k = 1;
GO

EXEC sys.sp_query_store_flush_db;
GO
