/*
  S1-07 / F4 - Login de minimo privilegio para la plataforma (RNF-SEG-02, CA-HU15.1).
  Solo CONNECT + VIEW DATABASE STATE en QD_Lab. Sin ningun permiso de escritura.
  La contrasena viene de la variable de entorno READER_PASSWORD (nunca en el repo).
  Opcional (F4c, solo SQL Server 2022): VIEW DATABASE PERFORMANCE STATE en lugar del anterior.
*/
USE master;
GO
IF SUSER_ID(N'qd_reader') IS NOT NULL DROP LOGIN qd_reader;
GO
CREATE LOGIN qd_reader WITH PASSWORD = N'$(READER_PASSWORD)', CHECK_POLICY = OFF;
GO
USE QD_Lab;
GO
IF USER_ID(N'qd_reader') IS NOT NULL DROP USER qd_reader;
CREATE USER qd_reader FOR LOGIN qd_reader;
GRANT VIEW DATABASE STATE TO qd_reader;
GO

-- Usuario sin permisos, para el caso negativo de F4(b).
USE master;
GO
IF SUSER_ID(N'qd_noperm') IS NOT NULL DROP LOGIN qd_noperm;
GO
CREATE LOGIN qd_noperm WITH PASSWORD = N'$(READER_PASSWORD)', CHECK_POLICY = OFF;
GO
USE QD_Lab;
GO
IF USER_ID(N'qd_noperm') IS NOT NULL DROP USER qd_noperm;
CREATE USER qd_noperm FOR LOGIN qd_noperm;
GO
