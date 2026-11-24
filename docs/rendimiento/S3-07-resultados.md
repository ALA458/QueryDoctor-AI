# S3-07 — Pruebas de rendimiento: ingesta y tablero con 5 000 consultas

**Requisitos:** RNF-RND-01, RNF-RND-02 (y CA-HU04.2) · **Prioridad:** Debe · **Responsable:** Álvaro · **Fecha de las
mediciones:** ingesta 2026-11-22, tablero 2026-11-23 (UTC) · **Código medido:** `src/` idéntico a `main` en `6892788`; herramientas de medición de
la rama `feat/S3-07-rendimiento` (`1555118` para la ingesta, `d213dc1` para el tablero).

> **Alcance (RF-EVA-04, invariante 10).** Estas cifras son **mediciones de una sola máquina de laboratorio** con datos
> sintéticos, en las condiciones descritas abajo. No son una afirmación de rendimiento de la plataforma en producción
> ni en otro equipo. Las metas marcadas con † son **metas de ingeniería provisionales** (SRS, Convenciones): fijadas por
> el equipo, sin respaldo externo, pendientes de ratificar con el tutor (DA-06, abierta; S1-20 sin hacer). Una meta
> "cumplida" aquí solo significa que la medición quedó dentro del valor provisional.

## 1. Resultado en una tabla

| Requisito | Meta (SRS) | Medición (3 repeticiones, sin calentamiento) | Resultado |
|---|---|---|---|
| RNF-RND-01 (a) — CPU de la sesión de la plataforma, ingesta **completa** | ≤ 5 % de la CPU total † | media **0,85 %** (0,80–0,89 %) | **Cumple (meta provisional †)** |
| RNF-RND-01 (a) — ídem, ingesta **incremental** | ≤ 5 % † | media **2,84 %** (1,31–3,82 %) | **Cumple (meta provisional †)** · ver §6.3 |
| RNF-RND-01 (b) — esperas de bloqueo sobre objetos de usuario | 0 ocurrencias † | **0** en las 6 fases | **Cumple (meta provisional †)** |
| RNF-RND-02 / CA-HU04.2 — listado de consultas críticas, **concurrencia 1** | ≤ 2 s p95 † | p95 **543 ms** (n = 1 500, 0 errores) | **Cumple (meta provisional †)** |
| RNF-RND-02 — detalle sin IA, **concurrencia 1** | ≤ 3 s † (p95, supuesto S3) | p95 **44 ms** (n = 1 500, 0 errores) | **Cumple (meta provisional †)** |
| RNF-RND-02 — listado, **concurrencia 5** (supuesto S4) | ≤ 2 s p95 † | p95 **2 189 ms**; **640 de 1 500 (43 %) HTTP 500**; p95 de las correctas 3 459 ms | **No cumple** |
| RNF-RND-02 — detalle sin IA, **concurrencia 5** (supuesto S4) | ≤ 3 s † | p95 **114 ms**; **109 de 1 500 (7 %) HTTP 500** | **No cumple** (por los errores) |

**Lectura corta:** la ingesta de 5 000 consultas carga poco a SQL Server y no bloquea a nadie. El tablero responde holgado
con **un** usuario. Con **cinco** usuarios a la vez falla: la Api devuelve HTTP 500 por un defecto de concurrencia en
`SecretProtector` (§7.1), y además el listado pasa de 2 s porque no pagina (§7.2). Las dos causas quedan como deuda.

## 2. Qué dice el SRS (citas textuales)

- **†:** "El símbolo † indica una meta de ingeniería: un valor objetivo fijado por el equipo, sin respaldo en una fuente
  externa, que no constituye una afirmación factual y que se ratifica con el tutor y se contrasta con datos empíricos
  antes de la entrega final (DA-06)."
- **RNF-RND-01** (Debe, OBJ-1, HU-03, verificación T): "Durante un ciclo de ingesta, la sesión de la plataforma no
  deberá consumir más del 5 % de la CPU total de la instancia de prueba ni provocar esperas de bloqueo sobre objetos de
  usuario (0 ocurrencias) †."
- **RNF-RND-02** (Debe, OBJ-5, HU-04, T): "Con 5 000 consultas ingeridas, el dashboard deberá responder en ≤ 2 s
  (percentil 95) al listado de consultas críticas y en ≤ 3 s al detalle de una consulta sin explicación de IA †."
- **CA-HU04.2:** "Dado 5 000 consultas ingeridas, cuando abro el listado, entonces este responde en ≤ 2 s (percentil 95)."
- **DA-06:** "Ratificar formalmente, con el tutor del proyecto, cada meta numérica marcada como provisional en este
  documento, […] y definir el plan concreto de calibración empírica de cada una."

## 3. Decisiones y supuestos de la medición

El SRS no fija concurrencia, número de solicitudes ni algunos detalles. Se aprobaron en el PUNTO 1 de S3-07:

| Id | Decisión o supuesto |
|---|---|
| D1 | RNF-RND-01 mide **sobrecarga sobre SQL Server** (CPU de la sesión de la plataforma y esperas de bloqueo), no la duración de la ingesta. Duración, filas/s y memoria del Worker se registran como **información sin meta**. |
| D2 | Supuestos S1–S7 (abajo) y S8 (agregado al implementar). |
| D3 | Herramienta propia en .NET (`tests/QueryDoctor.PerfTests`, sin paquetes NuGet) en lugar de k6, que menciona `ARQUITECTURA_v0.1.md` (desviación del "cómo", no del requisito). |
| D4 | **Base limpia** en cada repetición: Query Store de QD_Lab vaciado y recargado, y volumen de PostgreSQL nuevo. |
| S1 | "Listado de consultas críticas" = `GET /queries?connectionId=…&window=24h&sort=score`. El orden por consumo se mide como informativo. |
| S2 | "Detalle sin IA" = `GET /queries/{id}?window=24h`. El árbol del plan se mide como informativo. |
| S3 | El p95 aplica también al detalle (el texto lo pone junto al listado; la arquitectura lo aplica a los dos). |
| S4 | Concurrencia 1 (latencia pura) y 5 (equipo pequeño a la vez), bucle cerrado sin pausa; veredicto por nivel. |
| S5 | 20 solicitudes de calentamiento descartadas + 500 medidas por escenario, concurrencia y repetición; 3 repeticiones. Tope de 300 s por escenario (no se alcanzó en ninguno). Detalle rotando sobre 100 consultas elegidas con semilla 3007. |
| S6 | Latencia medida en el cliente, por Nginx (`127.0.0.1:8080`), desde el envío hasta leer el cuerpo completo. Una respuesta con error cuenta con su tiempo y hace que el escenario no cumpla. |
| S7 | Ingesta con sus valores por defecto salvo `Ingestion__TopN=5000` (el máximo que acepta `IngestionOptions.Validate`). |
| S8 | Durante la medición, QD_Lab usa `INTERVAL_LENGTH_MINUTES = 60` (valor por defecto de SQL Server). Con 1 minuto, el sondeo programa una ingesta automática cada minuto que se metería en la medición; con 60 corre en el minuto :05, que el script evita. Una fase en la que se cuele una ingesta programada se anula (pasó una vez en la corrida de prueba y el detector la marcó). Al terminar se vuelve a 1 minuto. |

## 4. Entorno exacto

Archivo crudo: `lab/results/raw/perf/environment.txt`.

| Elemento | Valor |
|---|---|
| Equipo | Portátil, 12th Gen Intel Core i5-12450HX (8 núcleos, 12 lógicos), 15,7 GB de RAM, Windows 11 Home 10.0.26200 |
| Docker | Docker Desktop, motor 28.5.1, backend WSL 2; **12 CPU y 7,6 GB** asignados a la VM de Docker (compartidos por todos los contenedores) |
| SQL Server | 2022 Developer RTM-CU27-GDR (16.0.4305.5) en Linux (Ubuntu 22.04.5), contenedor `sqlserver` del compose; `cpu_count` = 12, 6 243 MB visibles |
| PostgreSQL | 16.15 (imagen `pgvector/pgvector:pg16`) |
| .NET | SDK 10.0.200 en el host; runtime 10.0.15 en los contenedores api/worker |
| Plataforma | `docker-compose.yml` + `docker-compose.lab.yml` + `lab/perf/docker-compose.perf.yml`; `APP_ENV` Development (laboratorio); servicio de IA apagado (no participa) |
| Límites por contenedor | Ninguno (el compose no fija `cpus` ni `mem_limit`) |

## 5. Datos y procedimiento reproducible

### 5.1 Datos (solo laboratorio, sintéticos)

- **5 000 consultas distintas** en QD_Lab (`lab/sql/perf/02-perf-workload.sql`): cada una empieza con
  `SELECT /* QD-PERF:nnnnn */`, busca un rango propio de `dbo.Orders` (1 000 000 de filas sintéticas de
  `lab/sql/02-schema-data.sql`) y usa una de 10 formas con GROUP BY o TOP, para que SQL Server no aplique la
  parametrización simple y junte textos.
- Query Store preparado por el laboratorio (nunca por la plataforma, CA-HU02.3): `QUERY_CAPTURE_MODE = ALL`,
  `MAX_STORAGE_SIZE_MB = 2048`, `SIZE_BASED_CLEANUP_MODE = OFF`.
- **Verificación antes de medir** (`03-perf-count.sql`), en cada repetición: **5 000** consultas QD-PERF en
  `sys.query_store_query`, **5 000** textos distintos y **5 000** ejecutadas en las últimas 24 h (5 003–5 004 en total en
  Query Store). Si alguno no llega a 5 000, el script se detiene sin medir.
- Después de cada primera ingesta, PostgreSQL tenía **exactamente 5 000** consultas, 5 000 planes y 5 000 filas de
  estadísticas por repetición.

### 5.2 Cómo se repite

Requisitos: contenedor `sqlserver` arriba con QD_Lab construida (`lab/Run-Lab.ps1 -Step build` y `-Step login`) y la
carpeta `secrets/` completa, incluido `perf_dba_password.txt` (contraseña que la medición le pone al DBA en el primer
ingreso; el script revisa la política antes de borrar nada).

```powershell
powershell -ExecutionPolicy Bypass -File .\lab\Run-Perf.ps1 -Step prepare     # 5 000 consultas + conteo
powershell -ExecutionPolicy Bypass -File .\lab\Run-Perf.ps1 -Step ingest      # RNF-RND-01 (calentamiento + 3)
powershell -ExecutionPolicy Bypass -File .\lab\Run-Perf.ps1 -Step dashboard   # RNF-RND-02 (3 repeticiones)
```

⚠️ `ingest` y `dashboard` **borran** el volumen local de PostgreSQL (`querydoctor_pgdata`) y el contenido de Query
Store de QD_Lab. Después hay que volver a correr `lab/Run-Lab.ps1 -Step workload` antes de LabTests/AcceptanceTests.

### 5.3 Ingesta (RNF-RND-01)

Por repetición (un calentamiento descartado + r1, r2, r3):

1. Query Store vaciado (`04-perf-reset-querystore.sql`, intervalo de 60 min), primera pasada de las 5 000 consultas y conteo.
2. Espera fuera del minuto :05 (S8), volumen de PostgreSQL nuevo; la Api migra y siembra el DBA.
3. Alta de QD_Lab por la Api pública (`qd_reader`, TLS obligatorio) y espera del sondeo hasta `Active`.
4. **Fase completa:** se arranca la sesión de Extended Events (`10-xe-start.sql`), **2 sesiones de carga de usuario
   concurrente** (`20-concurrent-load.template.sql`: ESC01–ESC04, ESC04B y ESC06 en bucle con 100 ms de pausa) y el
   muestreo de `docker stats`; se pide la ingesta manual (`POST /connections/{id}/ingestions`) y se espera a que el
   trabajo Hangfire termine (incluye el análisis del motor y el score).
5. Segunda pasada de las mismas 5 000 consultas y **fase incremental** igual que la 4.

Cálculo: **CPU % = Σ `cpu_time` de las sentencias de la aplicación `QueryDoctor-AI Telemetry Gateway` ÷ (duración de la
ejecución × `cpu_count`)**, con la ventana de la ejecución que registra la plataforma (`ingestion_run.started_at` →
`finished_at`). Bloqueos: eventos `wait_completed` con `LCK_M_*` de cualquier sesión y `blocked_process_report` (umbral
de 1 s, configurado solo durante la medición y devuelto a 0 al terminar). El ciclo XE se validó antes con un bloqueo
provocado sobre `dbo.Tiny` (transacción revertida): capturó la espera `LCK_M_S` de 3 765 ms y los informes de bloqueo.

### 5.4 Tablero (RNF-RND-02)

1. Base limpia como en 5.3 y **una** ingesta completa → 5 000 consultas, 5 000 planes y 5 000 scores en PostgreSQL.
2. **Worker detenido** durante la medición: sin ingestas programadas ni trabajos de fondo compitiendo con la Api (la
   Api no depende del Worker para leer; invariante 2).
3. Comando `dashboard` de `tests/QueryDoctor.PerfTests`, login como DBA y, por repetición, a concurrencia 1: listado
   por score, detalle, listado por consumo y árbol del plan; a concurrencia 5: listado por score y detalle. Cada
   escenario: 20 solicitudes de calentamiento (descartadas) y 500 medidas.
4. Percentiles por **rango más cercano** (el valor en la posición ⌈p/100·n⌉ de la serie ordenada; sin interpolar);
   desviación estándar muestral. Cubierto por pruebas unitarias (`tests/QueryDoctor.UnitTests/Perf/`).
5. Muestreo de CPU y memoria de los contenedores `api` y `db` cada ~2 s.

## 6. Resultados de la ingesta (RNF-RND-01)

Ventana: 2026-11-22 22:28–22:37 UTC. Las 8 fases fueron válidas: ninguna ingesta programada colada, 0 eventos XE
perdidos, 0 informes de bloqueo. Archivo crudo: `lab/results/raw/perf/ingest-summary.csv` (+ un `-run.json`,
`-xe.txt` y `-stats.txt` por fase).

### 6.1 Datos crudos por repetición

| Rep. | Fase | Ejecución (s) | Trabajo (s) | Filas | Filas/s | Consultas en PG | Sentencias | CPU plataforma (µs) | **CPU %** | Esperas LCK de otros | Bloqueos por la plataforma | Pico memoria Worker (MiB) | CPU media contenedor SQL (% de 1 núcleo) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| calentamiento¹ | completa | 29,142 | 39,558 | 20 000 | 686,3 | 5 000 | 30 005 | 2 517 000 | 0,7198 | 0 | 0 | 609,4 | 460,3 |
| calentamiento¹ | incremental | 0,371 | 3,393 | 5 022 | 13 547,1 | 5 009 | 60 | 93 000 | 2,0906 | 0 | 0 | 862,7 | 419,0 |
| r1 | completa | 35,061 | 45,175 | 20 000 | 570,4 | 5 000 | 30 005 | 3 731 000 | 0,8868 | 0 | 0 | 640,6 | 477,5 |
| r1 | incremental | 0,314 | 3,527 | 5 031 | 16 047,6 | 5 012 | 78 | 128 000 | 3,4024 | 0 | 0 | 874,8 | 452,1 |
| r2 | completa | 33,089 | 42,929 | 20 000 | 604,4 | 5 000 | 30 005 | 3 175 000 | 0,7996 | 0 | 0 | 658,3 | 472,5 |
| r2 | incremental | 0,218 | 3,086 | 5 010 | 22 986,4 | 5 005 | 36 | 100 000 | 3,8234 | 0 | 0 | 843,2 | 456,7 |
| r3 | completa | 34,303 | 42,881 | 20 000 | 583,0 | 5 000 | 30 005 | 3 540 000 | 0,8600 | 0 | 0 | 634,1 | 482,8 |
| r3 | incremental | 0,746 | 5,425 | 5 022 | 6 734,8 | 5 009 | 60 | 117 000 | 1,3075 | 0 | 0 | 822,3 | 381,4 |

¹ Descartado. *Ejecución* = lectura de Query Store + cifrado + upsert (marcas del Worker en `ingestion_run`); *Trabajo*
= todo el trabajo Hangfire, con el análisis del motor de los planes y el recálculo del score. *Filas* = textos,
consultas, planes y estadísticas confirmados. Las esperas de bloqueo **de la propia plataforma** también fueron 0 en
todas las fases.

### 6.2 Resumen estadístico (r1–r3, n = 3)

| Medición | Completa: media (mín–máx; DE) | Incremental: media (mín–máx; DE) |
|---|---|---|
| **CPU de la plataforma (% de la CPU total)** | **0,849 %** (0,800–0,887; 0,045) | **2,844 %** (1,308–3,823; 1,348) |
| CPU de SQL Server usada por la plataforma | 3,482 s (3,175–3,731; 0,283) | 0,115 s (0,100–0,128; 0,014) |
| Esperas de bloqueo causadas / informes de bloqueo | 0 / 0 | 0 / 0 |
| Duración de la ejecución | 34,15 s (33,09–35,06; 0,99) | 0,43 s (0,22–0,75; 0,28) |
| Duración del trabajo (con análisis y score) | 43,66 s (42,88–45,18; 1,31) | 4,01 s (3,09–5,43; 1,24) |
| Filas confirmadas por segundo | 586 (570–604; 17) | 15 256 (6 735–22 986; 8 155) |
| Pico de memoria del contenedor Worker | 644 MiB (634–658; 13) | 847 MiB (822–875; 26) |

### 6.3 Cómo leer estos números

- **Dónde se va la CPU de la ingesta completa.** En r1, el 90 % del `cpu_time` (3,35 de 3,73 s) es la lectura de los
  5 000 planes uno por uno (`SELECT … query_plan FROM sys.query_store_plan WHERE plan_id = @PlanId`); la lectura de los
  textos usa 0,10 s y la sentencia Top-N, 0,24 s. Son 30 005 sentencias: 5 000 planes, 5 000 textos, 10 002 `SET
  LOCK_TIMEOUT` de sesión, 10 001 `sp_reset_connection` del pool y 2 de estado/Top-N.
- **La incremental es corta y su porcentaje es sensible.** Dura menos de 1 s y SQL Server informa `cpu_time` en
  saltos de 1 ms, así que unas decenas de milisegundos mueven el porcentaje (de 1,3 % a 3,8 %). Por eso se reporta
  también la CPU absoluta (~0,1 s). En la corrida de prueba, una fase incremental anulada dio 5,18 % con solo 27 ms de
  CPU en 43 ms. La medición no se ajustó: si la meta se ratifica así, una ingesta muy corta podría superar el 5 % con
  una carga absoluta mínima (ver deuda D-4).
- **Por qué la incremental trae algo más de 5 000 consultas.** Relee el intervalo de 60 min, que sigue abierto (la
  marca de agua solo avanza con intervalos cerrados), y Query Store también captura las consultas de la carga
  concurrente y de los scripts; el Top-N elige 5 000 de ese conjunto. Textos y planes ya conocidos no se vuelven a
  pedir: solo se leen 12 planes y 12 textos nuevos.
- **La memoria de la incremental no es "más memoria por ingesta".** Es la memoria total del contenedor, y el Worker no
  se reinicia entre la completa y la incremental: arrastra lo que dejó la primera. Además la incremental tiene solo 1–2
  muestras (`docker stats` da una cada ~2 s).
- **El 0 de bloqueos se midió con carga.** Durante cada fase corrían 2 sesiones de usuario; el contenedor de SQL Server
  promedió ~4,6 núcleos de 12 (casi todo de ESC03, que recorre `dbo.Orders` sin índice a propósito). Esa carga solo lee,
  y la plataforma solo lee vistas de Query Store: un 0 era lo esperado, y la sesión XE habría capturado lo contrario
  (validado en 5.3).

## 7. Resultados del tablero (RNF-RND-02)

Ventana: 2026-11-23 00:23–00:51 UTC. 9 000 solicitudes medidas. Base: 5 000 consultas, 5 000 planes, 5 000 scores y
**0 hallazgos** (ver §8, limitación L-3). Archivos crudos: `dashboard-samples.csv` (una fila por solicitud),
`dashboard-summary.csv`, `dashboard-report.json` y `dashboard-containers.csv`.

### 7.1 Datos crudos por repetición

| Rep. | Conc. | Escenario | Meta † | n | Errores | p50 (ms) | p95 (ms) | p99 (ms) | Máx (ms) | Sol./s | Veredicto |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 1 | listado (score) | 2 000 | 500 | 0 | 353,7 | 562,7 | 719,6 | 988,6 | 2,61 | cumple |
| 1 | 1 | detalle | 3 000 | 500 | 0 | 26,6 | 36,2 | 45,8 | 101,0 | 35,41 | cumple |
| 1 | 1 | listado (consumo) | — | 500 | 0 | 377,0 | 567,0 | 741,7 | 952,1 | 2,50 | informativo |
| 1 | 1 | árbol del plan | — | 500 | 0 | 8,4 | 15,9 | 26,8 | 46,5 | 105,48 | informativo |
| 1 | 5 | listado (score) | 2 000 | 500 | 224 | 872,3 | 1 639,2 | 2 887,8 | 3 706,6 | 5,53 | no cumple |
| 1 | 5 | detalle | 3 000 | 500 | 40 | 74,9 | 138,1 | 185,7 | 301,1 | 61,17 | no cumple |
| 2 | 1 | listado (score) | 2 000 | 500 | 0 | 387,2 | 555,3 | 748,5 | 1 426,6 | 2,45 | cumple |
| 2 | 1 | detalle | 3 000 | 500 | 0 | 30,9 | 46,7 | 68,2 | 83,8 | 30,04 | cumple |
| 2 | 1 | listado (consumo) | — | 500 | 0 | 361,8 | 436,4 | 548,6 | 695,3 | 2,69 | informativo |
| 2 | 1 | árbol del plan | — | 500 | 0 | 7,3 | 10,5 | 14,8 | 21,0 | 128,67 | informativo |
| 2 | 5 | listado (score) | 2 000 | 500 | 216 | 795,8 | 1 084,7 | 1 307,6 | 1 410,8 | 6,92 | no cumple |
| 2 | 5 | detalle | 3 000 | 500 | 27 | 48,7 | 68,8 | 75,6 | 82,0 | 100,74 | no cumple |
| 3 | 1 | listado (score) | 2 000 | 500 | 0 | 373,9 | 521,5 | 692,6 | 825,0 | 2,54 | cumple |
| 3 | 1 | detalle | 3 000 | 500 | 0 | 30,8 | 46,7 | 63,2 | 98,5 | 29,95 | cumple |
| 3 | 1 | listado (consumo) | — | 500 | 0 | 376,5 | 485,5 | 574,5 | 814,2 | 2,58 | informativo |
| 3 | 1 | árbol del plan | — | 500 | 0 | 7,6 | 12,0 | 14,9 | 21,7 | 121,13 | informativo |
| 3 | 5 | listado (score) | 2 000 | 500 | 200 | 928,2 | 5 472,2 | 10 326,1 | 17 031,1 | 3,19 | no cumple |
| 3 | 5 | detalle | 3 000 | 500 | 42 | 61,7 | 85,8 | 96,2 | 103,7 | 80,70 | no cumple |

Todos los errores fueron **HTTP 500** de la Api; ningún 504 de Nginx ni corte del cliente. Ningún escenario llegó al
tope de 300 s.

### 7.2 Resumen de las 3 repeticiones juntas (n = 1 500 por fila)

| Conc. | Escenario | p50 (ms) | **p95 (ms)** | p99 (ms) | Máx (ms) | Errores | p95 solo respuestas 200 (n) |
|---|---|---|---|---|---|---|---|
| 1 | listado (score) | 373,3 | **543,0** | 707,9 | 1 426,6 | 0 | 543,0 (1 500) |
| 1 | detalle | 29,3 | **44,3** | 65,2 | 101,0 | 0 | 44,3 (1 500) |
| 1 | listado (consumo), informativo | 369,5 | 506,0 | 624,5 | 952,1 | 0 | 506,0 (1 500) |
| 1 | árbol del plan, informativo | 7,7 | 13,0 | 21,7 | 46,5 | 0 | 13,0 (1 500) |
| 5 | listado (score) | 852,8 | **2 188,9** | 8 355,9 | 17 031,1 | **640 (42,7 %)** | **3 458,6** (860) |
| 5 | detalle | 60,2 | **114,1** | 147,1 | 301,1 | **109 (7,3 %)** | 117,0 (1 391) |

- **El p95 "solo de las correctas" del listado a concurrencia 5 (3 459 ms) es peor que el de todas (2 189 ms).** Los
  500 fallan rápido (media 531 ms en el listado, 21 ms en el detalle) y bajan el percentil. Es decir: **aun sin los
  errores, el listado a concurrencia 5 no cumple los 2 s.**
- Cada respuesta del listado pesa **~2,1 MB** (mediana 2 139 530 bytes); la del detalle, ~2,5 KB.
- Recursos durante la medición (791 muestras): Api con CPU media de **3,2 núcleos** (pico 7,0) y memoria media
  862 MiB, **pico 2,3 GB**; PostgreSQL con 0,65 núcleos de media y pico de 145 MiB.

## 8. Hallazgos, causas probables y deuda

Ninguna meta ni medición se cambió para que pase. Lo que no cumple se reporta tal cual.

**D-1 · HTTP 500 con solicitudes concurrentes (causa del "no cumple" a concurrencia 5).**
El log de la Api muestra `CryptographicException: Fallo al descifrar el campo` / `AuthenticationTagMismatchException`
desde `EfQuerySourceReader.ListAsync`. `SecretProtector` (S1-05) se registra como singleton y usa **una sola**
instancia de `AesGcm` para todas las solicitudes (`SecretProtector.cs`, campo `_aesGcm`;
`DependencyInjection.cs`, `AddSingleton<IFieldEncryptor, SecretProtector>()`). .NET no garantiza que los métodos de
una instancia de `AesGcm` sean seguros entre hilos; en secuencia, los mismos datos se descifran bien. **Riesgo
adicional:** dos `Encrypt` simultáneos en el Worker podrían guardar un cifrado que no se pueda descifrar (RNF-SEG-14).
**Propuesta:** un `AesGcm` por operación (o un `lock`), sin cambiar el formato del sobre, con prueba de concurrencia.
Hay un arreglo preparado, sin commit, en una rama aparte (`claude/kind-pare-dfa82e`), pendiente de coordinar con el
dueño de S1-05 (Maykol); no forma parte de S3-07.
**Después de ese arreglo hay que repetir la medición a concurrencia 5** (`Run-Perf.ps1 -Step dashboard`).

**D-2 · El listado no pagina ni evita descifrar (causa de la latencia a concurrencia 5).**
`GET /queries` devuelve las 5 000 consultas en cada solicitud (~2,1 MB): `EfQuerySourceReader.ListAsync` carga todas
las consultas, planes, estadísticas y scores de la conexión, descifra los 5 000 textos y `GetQueries` los enmascara con
ScriptDom antes de responder. `ARQUITECTURA_v0.1.md` (táctica de RNF-RND-02) prevé "paginación en servidor" y que "el
listado no descifra texto SQL: muestra un extracto enmascarado guardado aparte"; hoy no es así. Con 5 usuarios, la Api
llega a 7 núcleos y 2,3 GB. **Propuesta (dueño de S2-09, Daniel):** paginación en servidor y extracto enmascarado
precalculado en la ingesta. Volver a medir después.

**D-3 · Sin índice para la búsqueda de lo vencido (S2-13).** Lo anotaba `HANDOFF.md` para medir aquí. No se midió: la
purga de retención no forma parte de RNF-RND-01/02. Durante el tablero el Worker estaba detenido, así que la purga no
pudo correr; durante la ingesta no se verificó si el trabajo diario `retention` se ejecutó (con base nueva no había
evidencia vencida que purgar). Queda pendiente como medición propia.

**D-4 · La meta de CPU por ingesta es frágil para ingestas muy cortas.** Con ventanas de menos de 1 s, el porcentaje
depende de pocos milisegundos (§6.3). **Propuesta para DA-06 (decide el tutor, no esta tarea):** acompañar la meta con
un piso absoluto o medirla sobre una ventana mínima. Hasta entonces se reporta el porcentaje tal cual.

### Limitaciones de la medición

- **L-1.** Una sola máquina con Docker Desktop: SQL Server, PostgreSQL, Api, Worker, Nginx y el cliente comparten las
  mismas 12 CPU y 7,6 GB. En otro equipo, o con la base objetivo en otro servidor, las cifras cambian.
- **L-2.** Solo SQL Server 2022 de laboratorio. No se midió SQL Server 2019 ni Azure SQL Database.
- **L-3.** La base del tablero tiene 0 hallazgos (las consultas QD-PERF son sanas). Con hallazgos, el listado también
  enmascara su evidencia: la latencia real podría ser mayor. No medido.
- **L-4.** La CPU de RNF-RND-01 es el `cpu_time` de las sentencias en SQL Server: no incluye el inicio de sesión TLS de
  cada conexión ni la CPU del propio Worker.
- **L-5.** Concurrencia 5 sin pausa entre solicitudes es una carga más dura que 5 personas usando la SPA; es el
  supuesto S4. El SRS no fija la concurrencia.
- **L-6.** La memoria se muestrea cada ~2 s con `docker stats`; los picos cortos pueden no verse.
- **L-7.** n = 3 repeticiones: el resumen describe estas corridas; no permite intervalos de confianza útiles.

## 9. Archivos

| Archivo | Qué es |
|---|---|
| `lab/Run-Perf.ps1` | Orquestador: `prepare`, `ingest`, `dashboard`, `all` |
| `lab/sql/perf/*.sql` | Preparación de Query Store, carga de 5 000 consultas, conteo, sesión XE, carga concurrente, restauración |
| `lab/perf/docker-compose.perf.yml` | `Ingestion__TopN=5000` para el Worker |
| `tests/QueryDoctor.PerfTests/` | Consola de medición (`setup`, `ingest`, `dashboard`); no corre en la CI |
| `tests/QueryDoctor.UnitTests/Perf/` | Pruebas de argumentos, cookies, percentiles, veredicto y muestra con semilla |
| `lab/results/raw/perf/` | Datos crudos de esta corrida (sin secretos: revisado contra cada archivo de `secrets/`) |
