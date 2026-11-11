# Laboratorio SQL Server 2022 — Query Doctor AI (S1-07)

Este directorio contiene los scripts T-SQL, el orquestador PowerShell y las salidas del protocolo de verificación empírica del **Apéndice A del SRS** (hallazgos F1–F8 e hipótesis H1–H5) sobre el contenedor `sqlserver` (SQL Server 2022 Developer) definido en `docker-compose.yml`.

> **Declaración de alcance (RF-EVA-04, Invariante 10):** La configuración del laboratorio (`QUERY_CAPTURE_MODE = ALL`, `INTERVAL_LENGTH_MINUTES = 1`, `MAX_GRANT_PERCENT = 0.001` en `ESC05`) está diseñada para reproducibilidad determinista de los escenarios de prueba y **no representa un entorno de producción** (donde el valor por defecto es `AUTO` y `60` minutos; ver hallazgo F5 y riesgo RA-07). Ningún resultado de esta carpeta afirma eficacia, precisión ni exhaustividad del motor o del servicio de IA; para el informe completo del protocolo véase [S1-07-verificacion.md](./results/S1-07-verificacion.md).

---

## 1. Requisitos previos y secretos locales

El contenedor `sqlserver` lee la contraseña de `sa` desde un Docker secret y el laboratorio crea una cuenta de solo lectura (`qd_reader`) cuya contraseña también se lee desde `secrets/` (directorio ignorado por Git):

1. Asegúrate de tener creados los tres archivos en `secrets/` (una sola línea ASCII, sin salto de línea final, mínimo 8 caracteres con mayúsculas, minúsculas, números y símbolos):
   - `secrets/postgres_password.txt`
   - `secrets/mssql_sa_password.txt`
   - `secrets/mssql_reader_password.txt`
2. Levanta únicamente el contenedor de laboratorio:
   ```powershell
   docker compose up -d sqlserver
   ```

> **Excepción de TLS exclusiva del laboratorio (Invariante 3, RNF-SEG-03):** El contenedor `mcr.microsoft.com/mssql/server:2022-latest` genera un certificado autofirmado al arrancar. Por ello, las invocaciones de `sqlcmd` dentro de `Run-Lab.ps1` utilizan el modificador `-C` (*trust server certificate*) **únicamente para la orquestación local del contenedor de laboratorio**. El código de producción (`QueryStoreGateway`) mantiene `Encrypt=Strict|Mandatory` y `TrustServerCertificate=false` por defecto.

---

## 2. Estructura

| Ruta | Propósito |
|---|---|
| `sql/00-defaults-probe.sql` | **F5:** Crea una base temporal `QD_Defaults` sin configurar Query Store, registra los valores por defecto de SQL Server 2022 y la elimina. |
| `sql/01-database.sql` | Crea `QD_Lab` (compatibilidad 160) y activa Query Store con captura `ALL` e intervalo de 1 minuto. |
| `sql/02-schema-data.sql` | Crea `dbo.Orders` (1 000 000 de filas sintéticas deterministas, sin datos personales) con índices específicos para cada escenario y `dbo.Tiny` para anidamiento profundo. |
| `sql/03-scenarios.sql` | Define los procedimientos `dbo.usp_ESC01` a `dbo.usp_ESC06` (incluyendo `usp_ESC04B`), cada uno con el marcador `/* QD-LAB:ESCxx */`. |
| `sql/04-readonly-login.sql` | **RNF-SEG-02 / F4:** Script documentado de cuenta de mínimo privilegio `qd_reader` (`CONNECT` + `VIEW DATABASE STATE`, sin permisos de escritura) y cuenta sin permisos `qd_noperm` para la prueba negativa. |
| `sql/05-workload.sql` | Ejecuta todos los escenarios sembrados. |
| `sql/06-checks-sa.sql` | Verificaciones de **F3, F6, F8 y H4** ejecutadas como administrador del laboratorio (`sa`). |
| `sql/07-checks-reader.sql` | Verificaciones de **F4(a), F4(d) y F6 (permiso de servidor)** ejecutadas como `qd_reader`. |
| `sql/08-checks-noperm.sql` y `sql/09-grant-perf-state.sql` | Verificación de **F4(b)** (rechazo sin permisos) y **F4(c)** (`VIEW DATABASE PERFORMANCE STATE` en SQL Server 2022). |
| `sql/10-whitelist-validation.sql` | Ejecuta las 6 sentencias de `TelemetryStatementWhitelist` (S1-08) contra `QD_Lab` usando `qd_reader`. |
| `Run-Lab.ps1` | Orquestador por pasos (`defaults`, `build`, `login`, `workload`, `extract`, `checks`, `repro`, `all`). |
| `results/S1-07-verificacion.md` | Informe versionado con la tabla de hallazgos por escenario y el veredicto F1–F8. |
| `results/raw/` | Salidas crudas (`F5-defaults.txt`, `checks.txt`, `reproducibility-3runs.txt`). |

Los planes Showplan XML extraídos se guardan en `tests/fixtures/plans/` en pares:
- `ESCxx.qs.<n>.sqlplan`: plan estimado tal como lo almacena Query Store (`sys.query_store_plan.query_plan` leído como `nvarchar(max)`).
- `ESCxx.actual.sqlplan`: plan real capturado en el mismo momento con `SET STATISTICS XML ON`.

---

## 3. Ejecución paso a paso o completa

Para ejecutar todo el protocolo desde cero:

```powershell
powershell -ExecutionPolicy Bypass -File .\lab\Run-Lab.ps1 -Step all
powershell -ExecutionPolicy Bypass -File .\lab\Run-Lab.ps1 -Step repro
```

O por etapas individuales:

```powershell
powershell -ExecutionPolicy Bypass -File .\lab\Run-Lab.ps1 -Step defaults   # F5
powershell -ExecutionPolicy Bypass -File .\lab\Run-Lab.ps1 -Step build      # BD + 1M filas + procedimientos
powershell -ExecutionPolicy Bypass -File .\lab\Run-Lab.ps1 -Step login      # qd_reader y qd_noperm
powershell -ExecutionPolicy Bypass -File .\lab\Run-Lab.ps1 -Step workload   # carga + planes reales
powershell -ExecutionPolicy Bypass -File .\lab\Run-Lab.ps1 -Step extract    # planes de Query Store
powershell -ExecutionPolicy Bypass -File .\lab\Run-Lab.ps1 -Step checks     # F3, F4, F6, F8, H4 y whitelist S1-08
```

Para limpiar por completo el entorno de laboratorio (incluyendo volúmenes):

```powershell
docker compose down -v
```
