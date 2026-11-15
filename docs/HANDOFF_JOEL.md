# Estado actual — Joel (Motor, Score, Validador IA y Calidad)

## Última tarea completada: S2-07 (14-nov-2026)

- **Rama:** `feat/S2-07-prompt-delimitacion-inyeccion` (creada desde `main`)
- **Alcance completado (`S2-07`):**
  1. **Delimitación anti-escape y rotulación (`ai-service/src/prompts/prompt_v1.py`, `config.py`):**
     - `PROMPT_VERSION = "v1.1"`, ejemplo de `kb_fragment_id` actualizado a `<uuid-del-fragmento-curado>`, y rotulación explícita de los 4 vectores de `RNF-SEG-06` (SQL, comentarios, nombres de objeto y valores del plan en `<<<UNTRUSTED_SQL_TEXT>>>`, `<<<UNTRUSTED_FINDING_DATA>>>`, `<<<UNTRUSTED_OBJECT_AND_PLAN_VALUES>>>` y `<<<UNTRUSTED_METRICS_DATA>>>`).
     - Neutralización recursiva (`sanitize_untrusted_text`) de colisiones `<<<`, `>>>`, encabezados `[BASE DE CONOCIMIENTO CURADA...]` y tokens de control de chat; lista blanca estricta para `user_role` (`DBA`/`Desarrollador`) y saneamiento de `query_id`.
  2. **Rechazo estricto en `BaseLLMProvider` (`ai-service/src/providers/base.py`):**
     - Elimina la coerción silenciosa de `claim_type`, `finding_ids` y `kb_fragment_id` y exige JSON puro (sin prosa/HTML fuera del objeto JSON ni fuera de cercado ` ```json ``` `).
  3. **Suite de inyección dual (`>= 10` casos, `RNF-SEG-06`, `CA-HU21.2`):**
     - Python (`ai-service/tests/test_prompt_injection_suite.py`): 12 vectores adversariales verificando encapsulación sin ruptura de bloques y rechazo HTTP `422` ante salidas secuestradas (`68/68` `pytest` OK).
     - .NET (`PromptInjectionValidationTests.cs`): validación conjunta con `LiteralMasker` y `ExplanationValidator` (`0` evasiones).

## Hito previo integrado en `main`: S2-03 (`f731ff3`)

- `ExplanationValidator`, `NumericEvidenceMatcher`, `ExplanationValidationContextBuilder` y registro en DI.

## Siguientes pasos asignados (Sprint 2)

- **S2-05 (Álvaro):** `ExplanationContextBuilder` + hash canónico + caché + job `explain` usando `Guid` curados de `S1-17` e `IExplanationValidator` (`S2-03`).
- **S2-09 / S2-10 / S2-11:** Vistas de detalle de consulta, plan jerárquico y explicaciones en UI.
