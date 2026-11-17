# S3-01 — Calibración empírica de umbrales del motor determinístico (`DA-02`, `RF-MOT-14`)

> **Declaración de alcance y provisionalidad (`DA-02`, `DA-06`, `RF-EVA-04`, Invariante 10):** Los valores por defecto calibrados en este documento son **provisionales (`†`)** y reflejan únicamente la evidencia del corpus versionado en [`tests/fixtures/engine-ground-truth.json`](../../tests/fixtures/engine-ground-truth.json) (`44` planes: `18` sembrados de `QD_Lab` + `26` de TPC-H a escala `SF = 0.1` sobre SQL Server 2022 Developer `16.0.4315.2`, sin *Join Order Benchmark* según `SRS §5.3`). No representan una calibración universal para cargas de producción OLTP/OLAP externas. Los umbrales de las reglas aún no implementadas en el catálogo activo (`REG-07`, `REG-08` y `REG-10`) permanecen declarados como **por calibrar / provisionales**.

- **Catálogo de reglas:** `v1.0.0` (`REG-01`–`REG-11`; reglas `Debe` activas evaluadas: `REG-01`–`REG-04`, más tolerancia `REG-11`).
- **Hash de configuración (`RuleThresholds`):**
  - Línea base `S2-14`: `8182349B6FF3DAD35F1B6FCCBD38C6AA8B0AFDDF9E7B91C28EBB061D8502E61E`
  - Calibrado `S3-01`: `641FC0A9B5A4B4768E6F82D09755C3F453BA55DFCABCEC42620E168C88A2EB25`
- **Comandos reproducibles:**
  ```powershell
  # 1) Barrido de sensibilidad sobre --split calibration + verificación en evaluation y all
  dotnet build tools/QueryDoctor.EngineHarness
  powershell -NoProfile -ExecutionPolicy Bypass -File .\lab\Run-CalibrationSweep.ps1

  # 2) Puerta de verificación de metas RNF-FUN-01 (P >= 90 %, R >= 80 %)
  dotnet run --project tools/QueryDoctor.EngineHarness -- --corpus all --check-targets
  ```

---

## 1. Protocolo aplicado (separación `calibration` → `evaluation`)

Conforme a `SRS §6.2` (*Protocolo de validación empírica del motor determinístico*), la calibración se ejecutó en dos etapas estrictamente separadas:

1. **Ajuste exclusivo sobre `--split calibration`** (`22` planes: `9` `seeded`, `13` `tpch`), ejecutando barridos univariados de sensibilidad mediante [`lab/Run-CalibrationSweep.ps1`](../Run-CalibrationSweep.ps1) sobre [`s3-01-calibration-sweep.json`](./raw/s3-01-calibration-sweep.json).
2. **Validación ciega sobre `--split evaluation`** (`22` planes: `9` `seeded`, `13` `tpch`) sin modificar el candidato seleccionado, seguida de la comprobación global (`--split all`) con `--check-targets`.

---

## 2. Diagnóstico de la línea base (`S2-14`) y decisión de diseño

En [`S2-14-arnes-motor.md`](./S2-14-arnes-motor.md), `REG-01`, `REG-02` y `REG-04` ya alcanzaban `100.0 %` de precisión y exhaustividad, mientras que `REG-03` obtenía `89.8 %` de precisión global (`53` TP, `6` FP, `0` FN; `BAJO META` `RNF-FUN-01` `>= 90 %`).

Los `6` falsos positivos ocurrían en `dbo.NATION` (`25` filas totales) al tener predicado con selectividad `1`–`2` filas (razón `12.5`–`25.0 > 10`) bajo la condición `(a)` de [`Reg03CostlyFullScanRule.cs`](../../src/QueryDoctor.Application/Engine/Rules/Catalog/Reg03CostlyFullScanRule.cs):

- `calibration` (`5` FP): `TPCH_Q07` (`#3`, `#12`, razón `12.5`), `TPCH_Q11` (`#8`, `#16`, razón `25.0`) y `TPCH_Q21` (`#13`, razón `25.0`).
- `evaluation` (`1` FP): `TPCH_Q20` (`#2`, razón `25.0`).

En lugar de elevar artificialmente la razón `Reg03RowsReadToReturnedRatio` por encima de `25` (lo cual reduciría la sensibilidad frente a recorridos selectivos con razón `11`–`25` sobre tablas grandes de bajo consumo y rompería casos medianos como `AuditHeap` de `5 000` filas), se incorporó el parámetro configurable **`Reg03MinPredicateVolumeRows`** (`min_predicate_volume_rows`) en la condición `(a)` de `REG-03`, alineado con `RF-MOT-05` (*«Un recorrido completo sin predicado y sobre una tabla de bajo volumen no deberá señalarse»*).

---

## 3. Barrido de sensibilidad sobre `--split calibration`

Datos crudos reproducidos en [`lab/results/raw/s3-01-calibration-sweep.json`](./raw/s3-01-calibration-sweep.json):

| Parámetro barrido | Valor | `REG-03` TP | `REG-03` FP | `REG-03` FN | `REG-03` P | `REG-03` R | `REG-04` P / R | Lectura empírica |
|---|---:|---:|---:|---:|---:|---:|---|---|
| `Reg03MinPredicateVolumeRows` | `0` | 28 | 5 | 0 | 84.8 % | 100.0 % | 100.0 % / 100.0 % | Sin piso: `NATION` (25 filas) genera 5 FP (`BAJO META`) |
| `Reg03MinPredicateVolumeRows` | `100` | 28 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % / 100.0 % | Elimina los 5 FP de `NATION` |
| `Reg03MinPredicateVolumeRows` | `500` | 28 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % / 100.0 % | Meseta estable sin pérdida de TP |
| **`Reg03MinPredicateVolumeRows`** | **`1 000`** | **28** | **0** | **0** | **100.0 %** | **100.0 %** | **100.0 % / 100.0 %** | **Elegido:** margen `40x` sobre `NATION` (25) y `< 5 000` (`AuditHeap`) |
| `Reg03MinPredicateVolumeRows` | `5 000` | 28 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % / 100.0 % | Mantiene TP actuales (`>= 15k`), pero arriesga tablas `1k`–`4.9k` |
| `Reg03MinPredicateVolumeRows` | `10 000` | 28 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % / 100.0 % | Anularía scans selectivos con consumo `< 1 %` en tablas `1k`–`9.9k` |
| `Reg03MinEstimatedRows` | `5 000` | 28 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % / 100.0 % | Estable en el corpus actual |
| **`Reg03MinEstimatedRows`** | **`10 000`** | **28** | **0** | **0** | **100.0 %** | **100.0 %** | **100.0 % / 100.0 %** | **Ratificado:** margen previo al menor TP sin razón (`15 000` filas) |
| `Reg03MinEstimatedRows` | `15 000` | 28 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % / 100.0 % | En el borde exacto de `CUSTOMER` (`15 000`) |
| `Reg03MinEstimatedRows` | `20 000` | 25 | 0 | 3 | 100.0 % | 89.3 % | 100.0 % / 100.0 % | Degrada exhaustividad: pierde 3 scans de `CUSTOMER` (`Q05`, `Q07`, `Q13`) |
| `Reg03RowsReadToReturnedRatio` | `5` | 28 | 2 | 0 | 93.3 % | 100.0 % | 100.0 % / 100.0 % | Introduce 2 FP sobre `SUPPLIER` (`1 000` filas, razón `10.0`) |
| **`Reg03RowsReadToReturnedRatio`** | **`10`** | **28** | **0** | **0** | **100.0 %** | **100.0 %** | **100.0 % / 100.0 %** | **Ratificado:** filtra razón `10.0` (`SUPPLIER`) conservando sensibilidad |
| `Reg03RowsReadToReturnedRatio` | `15`–`30` | 28 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % / 100.0 % | Protegido por condición `(b)` en TPC-H porque `cons >= 1.36 %` |
| **`MinQueryConsumptionPct`** | **`0.5`–`2.5`** | **28** | **0** | **0** | **100.0 %** | **100.0 %** | **100.0 % / 100.0 %** | **Ratificado `1.0 %`:** separa negativo `0.4 %` del menor positivo `1.36 %` |
| `Reg04MinEstimatedExecutions` | `10`–`20` | 28 | 0 | 0 | 100.0 % | 100.0 % | 33.3 % / 100.0 % | 2 FP (`ESC02_QS_1` y caso de bajas ejecuciones) |
| `Reg04MinEstimatedExecutions` | `100`–`1 500` | 28 | 0 | 0 | 100.0 % | 100.0 % | 100.0 % / 100.0 % | Banda estable observada entre `20` (negativo) y `1 500` (positivo) |
| **`Reg04MinEstimatedExecutions`** | **`1 000`** | **28** | **0** | **0** | **100.0 %** | **100.0 %** | **100.0 % / 100.0 %** | **Ratificado (provisional):** banda `(20, 1 500)` sin muestras intermedias |
| `Reg04MinEstimatedExecutions` | `2 000` | 28 | 0 | 0 | 100.0 % | 100.0 % | N/A / 0.0 % | Pierde `SEEDED_REG04_POS_1500` (`1 500` ejecuciones) |

---

## 4. Comparativa antes (`S2-14`) vs. después (`S3-01`) por partición

### Resumen por partición y global (después de calibrar)

| Partición | Planes | `REG-01` (TP/FP/FN · P / R) | `REG-02` (TP/FP/FN · P / R) | `REG-03` (TP/FP/FN · P / R) | `REG-04` (TP/FP/FN · P / R) | `REG-11` |
|---|---:|---|---|---|---|---|
| `calibration` | 22 (`9` seeded, `13` tpch) | `2/0/0` · 100.0 % / 100.0 % | `14/0/0` · 100.0 % / 100.0 % | `28/0/0` · 100.0 % / 100.0 % | `1/0/0` · 100.0 % / 100.0 % | `0/0` |
| **`evaluation` (ciega)** | 22 (`9` seeded, `13` tpch) | `1/0/0` · 100.0 % / 100.0 % | `15/0/0` · 100.0 % / 100.0 % | `25/0/0` · 100.0 % / 100.0 % | `1/0/0` · 100.0 % / 100.0 % | `1/1` (`0` inesp.) |
| **`all` (`S3-01`)** | **44** (`18` seeded, `26` tpch) | **`3/0/0` · 100.0 % / 100.0 %** | **`29/0/0` · 100.0 % / 100.0 %** | **`53/0/0` · 100.0 % / 100.0 %** | **`2/0/0` · 100.0 % / 100.0 %** | **`1/1`** (`0` inesp.) |
| *Referencia `all` (`S2-14`)* | *44* | *`3/0/0` · 100.0 % / 100.0 %* | *`29/0/0` · 100.0 % / 100.0 %* | *`53/6/0` · 89.8 % / 100.0 %* | *`2/0/0` · 100.0 % / 100.0 %* | *`1/1`* |

En la partición ciega **`evaluation`**, `Reg03MinPredicateVolumeRows = 1 000` generaliza eliminando el falso positivo restante (`TPCH_Q20` `#2`, `NATION` de `25` filas) sin introducir falsos negativos (`diffs: []`, `all_must_targets_met: true` en `calibration`, `evaluation` y `all`).

---

## 5. Estado final de los parámetros de [`RuleThresholds`](../../src/QueryDoctor.Domain/Analysis/RuleThresholds.cs)

| Propiedad (`RuleThresholds`) | Regla | Valor `S3-01` | Criterio y estado (`DA-02`) |
|---|---|---:|---|
| `Reg03MinEstimatedRows` | `REG-03` | `10 000` | Ratificado como **provisional (`†`)**: `20 000` pierde 3 TP (`CUSTOMER` 15k) en `calibration`. |
| `Reg03MinPredicateVolumeRows` | `REG-03` | `1 000` | **Nuevo valor calibrado (`†`)**: elimina 6/6 FP sobre `NATION` (`25` filas) y preserva `AuditHeap` (`5 000` filas). |
| `Reg03RowsReadToReturnedRatio` | `REG-03` | `10` | Ratificado como **provisional (`†`)**: en `5` aparecen 2 FP sobre `SUPPLIER` (`1 000` filas). |
| `MinQueryConsumptionPct` | `REG-03`, `REG-04` | `1.0` % | Ratificado como **provisional (`†`)**: separa `0.4 %` negativo del menor positivo por volumen (`1.36 %`). |
| `Reg04MinEstimatedExecutions` | `REG-04` | `1 000` | Ratificado como **provisional (`†`)**: banda empírica observada `(20, 1 500)`; sin muestras intermedias en el corpus actual. |
| `Reg07CardinalityRatio` | `REG-07` | `10` | **Por calibrar** (regla `Debería` aún no implementada en `Catalog/`). |
| `Reg08RegressionIncreasePct` | `REG-08` | `50` % | **Por calibrar / provisional** (regla `Debería` aún no implementada; referencia documental de 10 s de CPU en SRS Cuadro 4.5). |
| `Reg08MinExecutionsPerPlan` | `REG-08` | `100` | **Por calibrar** (regla `Debería` aún no implementada en `Catalog/`). |
| `Reg10DurationMaxMinRatio` | `REG-10` | `5` | **Por calibrar** (regla `Podría` aún no implementada en `Catalog/`). |
