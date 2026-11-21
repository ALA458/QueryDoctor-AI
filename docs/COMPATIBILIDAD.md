# Compatibilidad por plataforma — Query Doctor AI

> Derivado del SRS v1.1 definitivo (Cuadro 4.16, RNF-COM-01, DA-12) con el resultado de la tarea **S3-06**. Si este
> documento y el SRS difieren, manda el SRS: la celda de SQL Server 2019 está pendiente de llevarse al SRS (ver la
> nota para Maykol al final). El código (`SupportMatrix`) debe coincidir **exactamente** con la tabla de niveles de
> abajo; `SupportMatrixDocumentTests` lo comprueba en cada `dotnet test`.
>
> **Alcance (invariante 10, RF-EVA-04):** este documento dice en qué plataformas se ejecutaron las pruebas de
> aceptación y con qué resultado. No afirma eficacia, precisión ni exhaustividad del motor ni de la IA; para eso, el
> informe del laboratorio (S3-05).

## Niveles (SRS, definición del Cuadro 4.16)

- **Probado:** con pruebas de aceptación automáticas ejecutadas en laboratorio.
- **Compatible sin pruebas:** verificado documentalmente, sin pruebas de aceptación ejecutadas.
- **No soportado:** la conexión se rechaza.

RNF-COM-01 (Debe): la plataforma debe funcionar y tener pruebas de aceptación ejecutadas contra las plataformas
Probado; para las demás se declara el nivel en la documentación (este archivo).

## Matriz de niveles

Cada fila trae un `ProductVersion` y un `SERVERPROPERTY('EngineEdition')` de muestra: la prueba evalúa
`SupportMatrix.Assess(plataforma declarada, versión, edición)` y exige el nivel de la columna **Nivel**.

<!-- matriz-soporte:inicio -->
| Plataforma | Plataforma declarada | ProductVersion de muestra | EngineEdition | Nivel |
|---|---|---|---|---|
| SQL Server anterior a 2016 | SqlServer | 12.0.6024.0 | 3 | No soportado |
| SQL Server 2016 (13.x) | SqlServer | 13.0.7000.253 | 3 | Compatible sin pruebas |
| SQL Server 2017 (14.x) | SqlServer | 14.0.3456.2 | 3 | Compatible sin pruebas |
| SQL Server 2019 (15.x) | SqlServer | 15.0.4510.2 | 3 | Probado |
| SQL Server 2022 (16.x) | SqlServer | 16.0.4325.4 | 3 | Probado |
| SQL Server 2025 o superior (17.x+) | SqlServer | 17.0.1000.7 | 3 | Compatible sin pruebas |
| Azure SQL Database | AzureSqlDatabase | 12.0.2000.8 | 5 | Compatible sin pruebas |
| Azure SQL Managed Instance | SqlServer | 12.0.2000.8 | 8 | No soportado |
<!-- matriz-soporte:fin -->

Funciones por plataforma (sin cambios respecto al Cuadro 4.16):

| Plataforma | Query Store | TempDB y bytes de log (RF-ING-03) | Plan real opcional (RF-ING-12) | Permiso mínimo |
|---|---|---|---|---|
| SQL Server 2016 | Sí | No (se omite con aviso, RNF-COM-04) | No | `VIEW DATABASE STATE` |
| SQL Server 2017 | Sí | Sí | No | `VIEW DATABASE STATE` |
| SQL Server 2019 | Sí | Sí | Sí | `VIEW DATABASE STATE` |
| SQL Server 2022 | Sí | Sí | Sí | `VIEW DATABASE PERFORMANCE STATE` o `VIEW DATABASE STATE` |
| SQL Server 2025+ | Sí | Sí | Sí | `VIEW DATABASE PERFORMANCE STATE` o `VIEW DATABASE STATE` |
| Azure SQL Database | Sí | Sí | Sí | `VIEW DATABASE PERFORMANCE STATE` o `VIEW DATABASE STATE` |

## Evidencia de las plataformas Probado

Job `acceptance` de `.github/workflows/ci.yml`, una entrada de la matriz por imagen, en cada PR a `main`. Prepara el
laboratorio desde cero con `lab/ci/prepare-acceptance-lab.sh` (scripts de `lab/sql`), corre `QueryDoctor.LabTests` y
`QueryDoctor.AcceptanceTests` contra el motor real (sin Skipped por falta de laboratorio) y publica un `.trx` por
plataforma, con el criterio de aceptación de cada prueba en el trait `Criterio`.

Corrida de referencia: [CI 44061839147](https://github.com/ALA458/QueryDoctor-AI/actions/runs/44061839147) (PR #72,
commit `ed52d25`, 2026-11-17), la primera con 2019 ya clasificado como `Tested` por la plataforma. La anterior
([44051765396](https://github.com/ALA458/QueryDoctor-AI/actions/runs/44051765396), `4e26946`) dio el mismo resultado
con 2019 todavía `CompatibleUntested`.

| Plataforma | Imagen | Motor | AcceptanceTests | LabTests |
|---|---|---|---|---|
| SQL Server 2022 | `mcr.microsoft.com/mssql/server:2022-latest` | 16.0.4325.4 Developer | 27/27 | 14/14 |
| SQL Server 2019 | `mcr.microsoft.com/mssql/server:2019-latest` | 15.0.4510.2 Developer | 27/27 | 13/13 + 1 no aplica |

La que no aplica en 2019 es la cuenta con solo `VIEW DATABASE PERFORMANCE STATE`: ese permiso existe desde 2022.

Qué ejercita la suite de aceptación, de punta a punta (Api real → Hangfire → Worker real → SQL Server real → Api):
registro y sondeo de conexiones, ingesta manual con análisis y score, hallazgos REG-01 a REG-04, listado, detalle,
series y plan, explicaciones con un doble del servicio de IA (TRAZADA, caché, NO_VERIFICADA, IA no disponible,
enmascarado), rol Desarrollador con filtro por conexión, traza y auditoría, y los casos negativos (credenciales,
permisos, Query Store apagado). El detalle criterio por criterio está en el PR de S3-06.

**Límites conocidos:**
- La IA es un doble: las métricas del modelo real son del arnés S3-02, no de esta suite.
- El laboratorio de 2019 usa nivel de compatibilidad 150 (sin PSP); el de 2022, 160.
- Ediciones probadas: Developer en Linux. Standard/Enterprise y Windows comparten el motor, pero no se ejecutaron.
- **Azure SQL Database queda Compatible sin pruebas:** no hubo acceso a una instancia (decisión D4 de S3-06).
- SQL Server 2016, 2017 y 2025+ no tienen aceptación ejecutada.

## Cómo repetirlo en local

```bash
# Clave de sa aleatoria y solo en variables de entorno: nunca escrita en la línea de comandos ni en el historial.
export SA_PASSWORD="Qd!9a$(openssl rand -hex 16)" MSSQL_SA_PASSWORD="$SA_PASSWORD"
export READER_PASSWORD="$(tr -d '\r\n' < secrets/mssql_reader_password.txt)"
docker run -d --name qd-sql2019 -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD -p 127.0.0.1:14339:1433 mcr.microsoft.com/mssql/server:2019-latest
bash lab/ci/prepare-acceptance-lab.sh qd-sql2019
QD_LAB_SQL="127.0.0.1,14339" QD_LAB_PLATFORM=sql2019 dotnet test tests/QueryDoctor.AcceptanceTests
```

En Windows, usar `127.0.0.1` y no `localhost` (resuelve primero a IPv6 y el compose publica 1433 solo en IPv4). En
Git Bash, anteponer `MSYS_NO_PATHCONV=1` al script.

## Nota para Maykol (S1-19, dueño del SRS v1.1)

S3-06 ejecutó la aceptación en SQL Server 2019. Para que el SRS siga mandando y coincida con el código, cambian:

| Dónde (SRS v1.1) | Hoy dice | Debe decir |
|---|---|---|
| Cuadro 4.16, fila SQL Server 2019 (15.x), columna Nivel | Compatible sin pruebas | **Probado** |
| Cuadro 4.16, nota (d), última frase | «SQL Server 2019, 2025+ y Azure SQL Database permanecen en Compatible sin pruebas…» | «SQL Server 2025+ y Azure SQL Database permanecen en Compatible sin pruebas…; SQL Server 2019 pasó a Probado con las pruebas de aceptación de S3-06» |
| Tabla 8.1, DA-12 | «únicamente SQL Server 2022 figura como Probado» | «SQL Server 2019 y 2022 figuran como Probado» |

No cambian: RNF-COM-01, las columnas de funciones y permisos, Azure SQL Database (sigue Compatible sin pruebas) ni
SQL Server 2016, 2017 y 2025+.
