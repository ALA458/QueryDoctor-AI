/*
  S1-07 - Esquema y datos SINTETICOS (sin datos personales, semilla fija por formula).
  Orders: 1.000.000 filas.
    CustomerCode varchar(20)  -> ESC01 (conversion implicita contra nvarchar)
    CustomerId   int          -> ESC02 (indice sin INCLUDE => Key Lookup)
    Region       int          -> ESC03 (sin indice => indice faltante)
    SkewKey      int          -> ESC04 (99% = 1, resto distinto)
    Note         char(200)    -> ESC05 (sort ancho para provocar spill)
  Tiny: tabla minima para ESC06 (anidamiento profundo).
*/
USE QD_Lab;
GO
SET NOCOUNT ON;

CREATE TABLE dbo.Orders
(
    OrderId      int           NOT NULL IDENTITY(1,1) CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED,
    CustomerCode varchar(20)   NOT NULL,
    CustomerId   int           NOT NULL,
    Region       int           NOT NULL,
    SkewKey      int           NOT NULL,
    Amount       decimal(12,2) NOT NULL,
    CreatedAt    datetime2(0)  NOT NULL,
    Note         char(200)     NOT NULL
);
GO

;WITH n AS
(
    SELECT TOP (1000000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c
)
INSERT dbo.Orders (CustomerCode, CustomerId, Region, SkewKey, Amount, CreatedAt, Note)
SELECT
    CONCAT('C', RIGHT('000000' + CAST(i % 50000 AS varchar(10)), 6)),
    i % 50000,
    i % 20,
    CASE WHEN i % 100 = 0 THEN 1000 + (i / 100) ELSE 1 END,
    CAST((i * 37) % 100000 AS decimal(12,2)) / 100,
    DATEADD(MINUTE, -(i % 525600), SYSDATETIME()),
    REPLICATE(CHAR(65 + (i % 26)), 200)
FROM n;
GO

-- ESC01: indice por CustomerCode (varchar). La consulta usara parametro nvarchar.
CREATE INDEX IX_Orders_CustomerCode ON dbo.Orders (CustomerCode);
-- ESC02: indice por CustomerId SIN columnas incluidas => Key Lookup.
CREATE INDEX IX_Orders_CustomerId ON dbo.Orders (CustomerId);
-- ESC04: indice por SkewKey (distribucion muy sesgada).
CREATE INDEX IX_Orders_SkewKey ON dbo.Orders (SkewKey);
-- ESC03: Region NO tiene indice a proposito.
UPDATE STATISTICS dbo.Orders WITH FULLSCAN;
GO

CREATE TABLE dbo.Tiny (Id int NOT NULL CONSTRAINT PK_Tiny PRIMARY KEY, V int NOT NULL);
INSERT dbo.Tiny (Id, V) VALUES (1, 1), (2, 2), (3, 3);
GO

SELECT COUNT(*) AS filas_orders FROM dbo.Orders;
GO
