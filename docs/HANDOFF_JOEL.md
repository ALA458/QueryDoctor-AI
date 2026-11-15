# Estado actual — Joel (Motor, Score, Validador IA y Calidad)

## Última tarea completada: S2-03 (14-nov-2026)
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
## Última tarea completada: S2-02 (14-nov-2026)

- **Rama:** `feat/S2-03-explanation-validator` (creada desde `main`)
- **Alcance completado (`S2-03`):**
  1. **Validador determinista puro (`QueryDoctor.Application.Validation`):**
     - `ExplanationValidator` (`IExplanationValidator`) y `ExplanationValidationOptions` (`RelativeTolerance = 0.005`, equivalencia `%`, redondeo a medio paso `RA-02`, validación numérica de `summary`, relación curada para todas las reglas citadas).
     - Deserialización estricta de `ExplainResponse` JSON (`query_id`, `summary`, `claims`, `metadata`) y validación de los 3 criterios de `RF-IA-03`: `(a)` existencia de `finding_ids` y `kb_fragment_id` como `Guid` estricto (obligatorio en `INTERPRETACION`/`RECOMENDACION`), `(b)` concordancia numérica con `NumericEvidenceMatcher` (excluye `@qd_*`, `REG-xx`, GUIDs, `F-*`, tipos SQL, `EngineVersion` y `NodeId`s permitidos), `(c)` terna curada `(RuleId, FragmentId, CausalRelationType)` de `S1-17`.
     - Estado binario `TRAZADA` / `NO_VERIFICADA` (`RNF-FUN-02`), aviso persistente `RF-IA-10`, etiqueta determinista `indicio` en `INTERPRETACION` con evidencia `ESTIMADA` o `EJECUCION_AGREGADA` (`RF-IA-12`).
  2. **Puente para `S2-05` y registro DI:**
     - `ExplanationValidationContextBuilder` (`IExplanationValidationContextBuilder`) construye el contexto desde `MaskedFinding` (`S2-04`) e `IKnowledgeBaseRepository` (`S1-17`) sin violar pureza de `Application.Validation`.
     - `AddInfrastructure` registra `ExplanationValidationOptions`, `IExplanationValidator` e `IExplanationValidationContextBuilder`.
  3. **Pruebas y verificación:**
     - `NumericEvidenceMatcherTests.cs` y `ExplanationValidatorTests.cs`: casos `TRAZADA`, números alterados en claim/summary, fuga entre hallazgos, sin fragmento curado, formato no-Guid, relación/regla cruzada, `indicio` y puente `LiteralMasker -> Repository -> Validator`.

## Nota de coordinación para S2-05 (Álvaro)

- En `ai-service` (`prompt_v1.py` y `providers/mock.py`), los ejemplos aún muestran `kb_fragment_id` tipo `"KB-REGxx-xx"` y `MaskedExplanationRequest` envía `kb_fragments = []`. En `S2-05`, al poblar `kb_fragments` desde PostgreSQL (`S1-17`), pasar y devolver los `Guid` reales de `KbFragment.Id` para que el validador los acepte en formato `Guid` estricto.

## Siguientes pasos asignados a Joel (Sprint 2)

- **S2-09 / S2-10:** Endpoints de consultas, detalle, hallazgos, `HealthScore` con historial y solicitud/validación de explicaciones IA (`Api`).
