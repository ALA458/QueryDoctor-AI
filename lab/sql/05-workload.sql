/*
  S1-07 - Carga de trabajo: ejecuta cada escenario para que Query Store lo capture.
  Se usa en dos modos (ver Run-Lab.ps1):
    - normal      : solo ejecuta (lo que vera la plataforma en Query Store)
    - actual plan : el script se antepone con SET STATISTICS XML ON (verdad de terreno)
*/
USE QD_Lab;
GO
SET NOCOUNT ON;

-- ESC01
EXEC dbo.usp_ESC01 @code = N'C012345';
EXEC dbo.usp_ESC01 @code = N'C000007';

-- ESC02 (valor selectivo => seek + key lookup)
EXEC dbo.usp_ESC02 @id = 1234;
EXEC dbo.usp_ESC02 @id = 4321;

-- ESC03
EXEC dbo.usp_ESC03 @region = 5;

-- ESC04: primero valor raro (compila plan seek+lookup), luego el valor dominante (reutiliza el plan)
EXEC sys.sp_recompile N'dbo.usp_ESC04';
EXEC dbo.usp_ESC04 @k = 1500;
EXEC dbo.usp_ESC04 @k = 1;

-- ESC05
EXEC dbo.usp_ESC05;

-- ESC06
EXEC dbo.usp_ESC06;
GO

-- ESC04B: valor raro primero (plan seek + lookup), luego el dominante (mismo plan reutilizado)
EXEC sys.sp_recompile N'dbo.usp_ESC04B';
EXEC dbo.usp_ESC04B @k = 1500;
EXEC dbo.usp_ESC04B @k = 1;
GO
