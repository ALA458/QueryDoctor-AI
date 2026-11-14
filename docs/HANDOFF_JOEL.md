# Estado actual — Joel (Motor, Score, Validador IA y Calidad)

## Última tarea completada: S2-02 (14-nov-2026)

- **Rama:** `feat/S2-02-health-score-calculator` (creada desde `main`)
- **Alcance completado (`S2-02`):**
  1. **Dominio y parámetros (`QueryDoctor.Domain.Analysis`):**
     - `HealthScoreParameters` configurable desde `"HealthScore"` en `appsettings.json` (`w_CPU=0.4`, `w_DUR=0.3`, `w_LECT=0.3`, `tau=0.05`, `alpha=0.5`, pesos de severidad/evidencia y bandas `0-39` Crítica, `40-69` Atención, `70-100` Saludable), con `Validate()`, `ToCanonicalJson()`, `ComputeConfigurationHash()` y textos normativos (`ScaleDirection`, `UsageDisclaimer`).
     - Entidad `HealthScore` (`E-07`) y enum `HealthScoreBand` (`CRITICA`, `ATENCION`, `SALUDABLE`).
  2. **Calculadora pura (`QueryDoctor.Application.Scoring`):**
     - `HealthScoreCalculator` (`IHealthScoreCalculator`): función pura, determinista y sin E/S que implementa las Ecuaciones 4.1–4.5 del SRS v1.1 (`C_r`, `s_r`, `I(q)`, `S(q)`, `P(q)`, `H(q)`), desglose `ContributingFindingsJson` y ordenamiento por score o por consumo (`RF-SCO-04`).
  3. **Persistencia, migración y recálculo tras ingesta:**
     - `HealthScoreConfiguration` (`analysis.health_score`), `AppDbContext.HealthScores` y migración EF Core `20261114185857_PuntajeSaludConsulta`.
     - `RecalculateConnectionScores`, `EfScoreRecalculationStore` y `RecalculateScoresJob` (`JobQueues.Analyze`), disparados automáticamente tras cada ingesta exitosa en `IngestConnection.ExecuteAsync`, conservando la serie histórica en `analysis.health_score` (`RF-SCO-05`).
  4. **Pruebas y verificación:**
     - `HealthScoreCalculatorTests.cs`, `RecalculateConnectionScoresTests.cs` y `AnalysisModelTests.cs`: ejemplo calculado a mano, determinismo, bandas, ordenamiento, historial y DI (`11/11` ArchitectureTests, `507/507` UnitTests, build `-warnaserror` y `dotnet format` OK).

## Siguientes pasos asignados a Joel (Sprint 2)

- **S2-03:** `ExplanationValidator` con los criterios `(a)` identificadores, `(b)` valores numéricos y `(c)` relación causal curada (`TRAZADA` / `NO_VERIFICADA`).
- **S2-09 / S2-10:** Endpoints de consultas, detalle, hallazgos, `HealthScore` con historial y solicitud/validación de explicaciones IA (`Api`).
