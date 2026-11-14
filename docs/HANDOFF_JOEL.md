# Estado actual — Joel (Motor, Score, Validador IA y Calidad)

## Última tarea completada: S2-03 (14-nov-2026)

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
