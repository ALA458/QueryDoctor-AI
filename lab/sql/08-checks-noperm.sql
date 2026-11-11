/*
  S1-07 - Caso negativo F4(b): usuario SIN permisos no puede leer Query Store.
  Luego, como sa, se prueba F4(c): VIEW DATABASE PERFORMANCE STATE (SQL Server 2022) en lugar de VIEW DATABASE STATE.
*/
SET NOCOUNT ON;
USE QD_Lab;
GO
PRINT '=== Lectura de sys.query_store_plan con el usuario actual';
BEGIN TRY
    SELECT USER_NAME() AS usuario, COUNT(*) AS plans FROM sys.query_store_plan;
END TRY
BEGIN CATCH
    SELECT USER_NAME() AS usuario, ERROR_NUMBER() AS error_numero, ERROR_MESSAGE() AS error_mensaje;
END CATCH;
GO
