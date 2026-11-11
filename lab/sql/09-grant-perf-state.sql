/*
  S1-07 - F4(c): cambia qd_noperm a VIEW DATABASE PERFORMANCE STATE (permiso mas especifico de 2022)
  y se verifica que alcanza para leer Query Store. Ejecutar como sa; la parte de lectura la hace el script 08.
*/
USE QD_Lab;
GO
GRANT VIEW DATABASE PERFORMANCE STATE TO qd_noperm;
GO
