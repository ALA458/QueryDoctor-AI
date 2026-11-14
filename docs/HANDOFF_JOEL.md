# Estado actual — Joel (Motor, Score, Validador IA y Calidad)

## Última tarea completada: S2-01 (14-nov-2026)

- **Rama:** `feat/S2-01-reg03-scan-contextual` (creada desde `main`)
- **Alcance completado (`S2-01`):**
  1. **`Reg03CostlyFullScanRule` (`RF-MOT-05 v1.1`, `CA-HU05.4`, Cuadro 4.5, Cuadro 4.11):**
     - Implementada en `src/QueryDoctor.Application/Engine/Rules/Catalog/Reg03CostlyFullScanRule.cs` y autodescubierta por `VersionedRuleCatalog` y `AddInfrastructure` (`RNF-MAN-01`).
     - Evalúa operadores `Table Scan`, `Clustered Index Scan` e `Index Scan` (excluyendo `IsLookup`) bajo las dos condiciones contextuales de `RF-MOT-05`:
       - **Condición (a):** el operador tiene predicado (`PredicateScalarString`) y la razón `(EstimatedRowsRead ?? TableCardinality) / Math.Max(1.0, EstimateRows) > Reg03RowsReadToReturnedRatio` (`10.0` por defecto).
       - **Condición (b):** el volumen real del recorrido (`Math.Max(EstimatedRowsRead ?? TableCardinality ?? 0.0, EstimateRows)`) alcanza `>= Reg03MinEstimatedRows` (`10 000` por defecto) y la consulta alcanza `>= MinQueryConsumptionPct` (`1.0 %` por defecto).
     - Graduación determinista de severidad sobre las filas leídas efectivas (`volumeRows`): `FindingSeverity.Alta` cuando `volumeRows >= 10 * Reg03MinEstimatedRows` (`100 000` por defecto) y `FindingSeverity.Media` por debajo.
  2. **Documentación de umbrales (`RuleThresholds.cs`):**
     - Añadida guía de ajuste por perfil de carga en `Reg03MinEstimatedRows` (`1 000 – 5 000` para OLTP de alta concurrencia; `50 000 – 100 000` para OLAP / Data Warehouse).
  3. **Suite de pruebas (`RNF-MAN-02`):**
     - `tests/QueryDoctor.UnitTests/Engine/Rules/Reg03ContextualFullScanTests.cs` (13 pruebas positivas, negativas, de frontera y de umbrales personalizados sobre `ESC01`, `ESC02`, `ESC03`, `ESC04`, `ESC04B`, `ESC05`, `ESC06` y planes sintéticos).
     - `MustRulesEvaluationTests.cs` actualizado para verificar el autodescubrimiento de las 4 reglas `Debe` (`REG-01`, `REG-02`, `REG-03`, `REG-04`).

## Siguientes pasos asignados a Joel (Sprint 2)

- **S2-02:** `HealthScoreCalculator` con la fórmula de la adenda (`RF-SCO-01..06`), migración de `analysis.health_score`, recálculo tras cada ingesta e historial.
- **S2-03:** `ExplanationValidator` con los criterios `(a)` identificadores, `(b)` valores numéricos y `(c)` relación causal curada (`TRAZADA` / `NO_VERIFICADA`).
