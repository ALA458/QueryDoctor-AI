/*
  S3-07 (RNF-RND-01) - Carga de usuario concurrente durante la ingesta medida, para que "0 esperas de bloqueo sobre
  objetos de usuario" signifique algo: sin otra carga, el 0 se cumpliria solo.
  SOLO LABORATORIO, ejecutada como sa por lab/Run-Perf.ps1 en segundo plano (con -H QD-PERF-LOAD, para poder
  cortarla con 21-stop-load.sql). PLANTILLA: Run-Perf.ps1 reemplaza __SECONDS__ (tope de seguridad).
  Usa los procedimientos ESC01-04, ESC04B y ESC06 de lab/sql/03-scenarios.sql sobre dbo.Orders y dbo.Tiny.
  ESC05 queda fuera: devuelve 1 000 000 de filas y provoca un spill a proposito; solo agregaria ruido de E/S.
  Pausa de 100 ms entre ejecuciones: carga de fondo ligera, no una prueba de estres.
*/
SET NOCOUNT ON;
USE QD_Lab;
GO

DECLARE @fin datetime2 = DATEADD(SECOND, __SECONDS__, SYSUTCDATETIME());
DECLARE @pick int, @c nvarchar(20), @i int, @r int, @k int, @n int = 0;

WHILE SYSUTCDATETIME() < @fin
BEGIN
    SET @pick = ABS(CHECKSUM(NEWID())) % 6;
    IF @pick = 0
    BEGIN
        SET @c = N'C0' + RIGHT(N'00000' + CAST(ABS(CHECKSUM(NEWID())) % 50000 AS nvarchar(5)), 5);
        EXEC dbo.usp_ESC01 @code = @c;
    END
    ELSE IF @pick = 1
    BEGIN
        SET @i = ABS(CHECKSUM(NEWID())) % 50000;
        EXEC dbo.usp_ESC02 @id = @i;
    END
    ELSE IF @pick = 2
    BEGIN
        SET @r = ABS(CHECKSUM(NEWID())) % 20;
        EXEC dbo.usp_ESC03 @region = @r;
    END
    ELSE IF @pick = 3
    BEGIN
        SET @k = CASE WHEN ABS(CHECKSUM(NEWID())) % 10 = 0 THEN 1500 ELSE 1 END;
        EXEC dbo.usp_ESC04 @k = @k;
    END
    ELSE IF @pick = 4
    BEGIN
        SET @k = CASE WHEN ABS(CHECKSUM(NEWID())) % 10 = 0 THEN 1500 ELSE 1 END;
        EXEC dbo.usp_ESC04B @k = @k;
    END
    ELSE
        EXEC dbo.usp_ESC06;

    SET @n += 1;
    WAITFOR DELAY '00:00:00.100';
END;

RAISERROR(N'  carga concurrente: %d ejecuciones', 0, 1, @n) WITH NOWAIT;
GO
