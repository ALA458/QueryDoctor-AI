# Estado actual — Joel (Motor, Score, Validador IA y Calidad)

## Última tarea completada: S1-19 (14-nov-2026)

- **Rama:** `docs/S1-19-srs-v1-1-definitivo` (creada desde `main`)
- **Alcance completado (`S1-19`, Adenda v1.1 §9 y §12):**
  1. **`docs/SRS.tex` actualizado a v1.1 definitivo (`14 de Noviembre de 2026`):**
     - Portada e historial actualizados a `1.1 (definitivo)` con `2026-11-14`.
     - **Cuadro 4.16 (`tab:matriz-soporte`) sin celdas `"A verificar"` ni `"A definir"`:**
       - `SQL Server 2016` y `2017`: `Compatible sin pruebas` (`DA-12` cerrada; nota fin soporte extendido 14-jul-2026).
       - `SQL Server 2022`: único **`Probado`** conforme a `RNF-COM-01`; `SQL Server 2019`, `2025 o superior` y `Azure SQL Database` pasan a `Compatible sin pruebas` hasta completar `S3-06`.
       - `Azure SQL Database`: verificado `Plan real opcional = Sí` (`LAST_QUERY_PLAN_STATS = ON`) y `Permiso mínimo = VIEW DATABASE PERFORMANCE STATE o VIEW DATABASE STATE`.
       - `Azure SQL Managed Instance` (`RNF-COM-03`, `Podría`): pasa a **`No soportado`** en el MVP (regla de recorte S1-19; `avg_tempdb_space_used` devuelve `0` en no-Hyperscale).
     - **Referencias cruzadas y limpieza:** unificados todos los enlaces `Tabla` → `Cuadro`, agregados rótulos `Cuadro 4.1` y `Cuadro 4.12`, enlaces clicables `\ref` para `Sección~\ref{sec:rf-sco}` (4.2.4), `Sección~\ref{sec:decisiones}` (8.1) y `Apéndice~\ref{ap:spike}`, corregida convención `[#]` vs `[R#]`, eliminadas notas de borrador obsoletas, cerrada `DA-12` y marcado aviso de regenerar `casos_uso.pdf` en Overleaf (`CU-05` "trazada" según `RF-IA-10`).
  2. **Código y pruebas sincronizados:**
     - `SupportMatrix.cs`: `SqlServer` 2022 (`16.x`) = `Tested`; `2016/2017/2019/2025+` y `AzureSqlDatabase` = `CompatibleUntested`; `AzureSqlManagedInstance` = `NotSupported` (`UnsupportedVersion`).
     - `SupportMatrixTests.cs` y `ProbeConnectionTests.cs` actualizados y pasando al 100%.

## Siguientes pasos asignados a Joel (Sprint 2)

- **S2-02:** PR abierto/pendiente en `feat/S2-02-health-score-calculator` (`HealthScoreCalculator`, `RF-SCO-01..06`).
- **S2-03:** `ExplanationValidator` con los criterios `(a)` identificadores, `(b)` valores numéricos y `(c)` relación causal curada (`TRAZADA` / `NO_VERIFICADA`).
