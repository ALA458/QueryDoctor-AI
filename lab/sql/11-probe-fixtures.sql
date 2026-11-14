/*
  S1-09 / P8 - Datos de laboratorio para las pruebas del sondeo de capacidades (tests/QueryDoctor.LabTests).
  SOLO LABORATORIO. Se ejecuta como sa DESPUES de 01 y 04 (01 recrea QD_Lab y borra sus usuarios). Idempotente.

  1. QD_Lab_NoQS: base con Query Store APAGADO (en 2022 una base nueva lo trae encendido, por eso se apaga explicito),
     con qd_reader y VIEW DATABASE STATE, para CA-HU02.3 (el sondeo informa OFF sin intentar activarlo).
  2. qd_perf: login con SOLO VIEW DATABASE PERFORMANCE STATE en QD_Lab, para probar que en 2022+ la matriz
     acepta ese permiso como alternativa a VIEW DATABASE STATE (Cuadro 4.16).

  Ningun permiso de escritura. La contrasena de qd_perf viene de READER_PASSWORD (nunca en el repo), como en 04.
*/
SET NOCOUNT ON;
GO

USE master;
GO
IF DB_ID(N'QD_Lab_NoQS') IS NOT NULL
BEGIN
    ALTER DATABASE QD_Lab_NoQS SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE QD_Lab_NoQS;
END;
GO
CREATE DATABASE QD_Lab_NoQS;
GO
ALTER DATABASE QD_Lab_NoQS SET RECOVERY SIMPLE;
ALTER DATABASE QD_Lab_NoQS SET QUERY_STORE = OFF;
GO
USE QD_Lab_NoQS;
GO
CREATE USER qd_reader FOR LOGIN qd_reader;
GRANT VIEW DATABASE STATE TO qd_reader;
GO

USE master;
GO
IF SUSER_ID(N'qd_perf') IS NOT NULL DROP LOGIN qd_perf;
GO
CREATE LOGIN qd_perf WITH PASSWORD = N'$(READER_PASSWORD)', CHECK_POLICY = OFF;
GO
USE QD_Lab;
GO
IF USER_ID(N'qd_perf') IS NOT NULL DROP USER qd_perf;
CREATE USER qd_perf FOR LOGIN qd_perf;
GRANT VIEW DATABASE PERFORMANCE STATE TO qd_perf;
GO

-- Comprobacion visible al final de la corrida.
SELECT name, is_query_store_on FROM sys.databases WHERE name IN (N'QD_Lab', N'QD_Lab_NoQS');
SELECT pr.name, pe.permission_name
FROM QD_Lab.sys.database_permissions AS pe
JOIN QD_Lab.sys.database_principals AS pr ON pr.principal_id = pe.grantee_principal_id
WHERE pr.name IN (N'qd_reader', N'qd_noperm', N'qd_perf')
ORDER BY pr.name, pe.permission_name;
GO
