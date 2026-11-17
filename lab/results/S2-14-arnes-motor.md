# S2-14 — Línea base del arnés del motor determinístico (`RF-EVA-01`, `CA-HU20.1`)

> **Declaración de alcance (`RF-EVA-04`, Invariante 10):** Los valores reportados corresponden únicamente al conjunto de evaluación versionado del repositorio ([`tests/fixtures/engine-ground-truth.json`](../../tests/fixtures/engine-ground-truth.json)) bajo condiciones explícitas de laboratorio (`RuleThresholds.Default` v`1.0.0`, SQL Server 2022 Developer `16.0.4315.2`, `QUERY_CAPTURE_MODE = ALL`, TPC-H `SF = 0.1` sin *Join Order Benchmark* según `SRS §5.3`). La calibración de umbrales sobre particiones separadas (`calibration` → `evaluation`) corresponde a la tarea `S3-01`.

- **Comando reproducible (`CA-HU20.1`):**
  ```powershell
  dotnet run --project tools/QueryDoctor.EngineHarness
  ```
- **Entorno de captura TPC-H:** contenedor `sqlserver` (`mcr.microsoft.com/mssql/server:2022-latest`), base `QD_TPCH` (compatibilidad `160`), ejecutada mediante `.\lab\Run-Lab.ps1 -Step tpch`.

---

# Informe de evaluación del motor determinístico (`RF-EVA-01`, `CA-HU20.1`)

- **Catálogo de reglas:** `v1.0.0`
- **Hash de umbrales (`RuleThresholds`):** `8182349B6FF3DAD35F1B6FCCBD38C6AA8B0AFDDF9E7B91C28EBB061D8502E61E`
- **Corpus / Split:** `all` / `all` (`44` planes: `18` sembrados, `26` TPC-H)
- **Metas de referencia (`RNF-FUN-01`, P >= 90 %, R >= 80 %):** REVISAR reglas bajo meta

## Métricas globales por regla (`REG-01`–`REG-04`)

| Regla | Descripción | Positivos | TP | FP | FN | Precisión | Exhaustividad | F1 | Meta `RNF-FUN-01` |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| `REG-01` | Conversión implícita que afecta el plan | 3 | 3 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % | OK |
| `REG-02` | Índice faltante | 29 | 29 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % | OK |
| `REG-03` | Lectura completa costosa (contextual) | 53 | 53 | 6 | 0 | 89.8 % | 100.0 % | 94.6 % | BAJO META |
| `REG-04` | Búsqueda por clave excesiva (contextual) | 2 | 2 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % | OK |

### Desglose en escenarios sembrados (`seeded`)

| Regla | Descripción | Positivos | TP | FP | FN | Precisión | Exhaustividad | F1 | Meta `RNF-FUN-01` |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| `REG-01` | Conversión implícita que afecta el plan | 3 | 3 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % | OK |
| `REG-02` | Índice faltante | 3 | 3 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % | OK |
| `REG-03` | Lectura completa costosa (contextual) | 5 | 5 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % | OK |
| `REG-04` | Búsqueda por clave excesiva (contextual) | 2 | 2 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % | OK |

### Desglose en carga TPC-H (`tpch`)

| Regla | Descripción | Positivos | TP | FP | FN | Precisión | Exhaustividad | F1 | Meta `RNF-FUN-01` |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| `REG-01` | Conversión implícita que afecta el plan | 0 | 0 | 0 | 0 | N/A | N/A | N/A | Sin muestras |
| `REG-02` | Índice faltante | 26 | 26 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % | OK |
| `REG-03` | Lectura completa costosa (contextual) | 48 | 48 | 6 | 0 | 88.9 % | 100.0 % | 94.1 % | BAJO META |
| `REG-04` | Búsqueda por clave excesiva (contextual) | 0 | 0 | 0 | 0 | N/A | N/A | N/A | Sin muestras |

## Tolerancia a planes no analizables (`REG-11` / `RF-MOT-16`)

- No analizables esperados: `1` | Clasificados con `REG-11`: `1` | Fallos inesperados en planes válidos: `0`

## Discrepancias (`FP` / `FN`)

| Tipo | Caso | Corpus / Split | Regla | Nodos | Detalle |
|---|---|---|---|---|---|
| `FP` | `TPCH_Q07` | `tpch/calibration` | `REG-03` | 3 | Hallazgo emitido sin anotación esperada (severidad MEDIA). |
| `FP` | `TPCH_Q07` | `tpch/calibration` | `REG-03` | 12 | Hallazgo emitido sin anotación esperada (severidad MEDIA). |
| `FP` | `TPCH_Q11` | `tpch/calibration` | `REG-03` | 8 | Hallazgo emitido sin anotación esperada (severidad MEDIA). |
| `FP` | `TPCH_Q11` | `tpch/calibration` | `REG-03` | 16 | Hallazgo emitido sin anotación esperada (severidad MEDIA). |
| `FP` | `TPCH_Q20` | `tpch/evaluation` | `REG-03` | 2 | Hallazgo emitido sin anotación esperada (severidad MEDIA). |
| `FP` | `TPCH_Q21` | `tpch/calibration` | `REG-03` | 13 | Hallazgo emitido sin anotación esperada (severidad MEDIA). |

---

## Hallazgo accionable para `S3-01` (calibración)

Los 6 falsos positivos observados en `REG-03` ocurren porque la condición `(a)` de `Reg03CostlyFullScanRule` (`hasPredicate && rowsRead / estimatedRows > 10`) no exige un volumen mínimo de tabla: señala recorridos sobre la tabla catálogo muy pequeña `NATION` (`25` filas en `Q07`, `Q11`, `Q20` y `Q21`). En **`S3-01`** se calibrará el piso mínimo / umbral sobre `--split calibration` y se medirá su efecto sobre `--split evaluation`.
