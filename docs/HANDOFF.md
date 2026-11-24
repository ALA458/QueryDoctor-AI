# HANDOFF.md — Estado actual

> **Se sobrescribe, no se acumula.** Máximo ~50 líneas. Lo escribe quien cierra una
> sesión (humano o agente). La historia vive en los PR y en git.

**Actualizado:** 2026-11-24 · **Por:** Álvaro (con agente) · **Rama:** `feat/S3-07-rendimiento` (desde `main` en `6892788`)

## Estado
- ✅ En `main`: S1-01 a S1-19 (salvo S1-20), S2-01 a S2-14, S3-01 a S3-03.
- 🔄 **S3-07** (Álvaro, PR por abrir): pruebas de rendimiento con 5 000 consultas (RNF-RND-01, RNF-RND-02, CA-HU04.2).
  Informe: `docs/rendimiento/S3-07-resultados.md`. Todas las metas son provisionales † (DA-06 abierta, S1-20 sin hacer).
  - RNF-RND-01: CPU de la plataforma 0,85 % (completa) y 2,84 % (incremental) de la CPU total; 0 esperas de bloqueo.
  - RNF-RND-02, concurrencia 1: listado p95 543 ms, detalle p95 44 ms → cumple.
  - RNF-RND-02, concurrencia 5: **no cumple**. 43 % de HTTP 500 en el listado y 7 % en el detalle; el listado, aun
    sin errores, tiene p95 de 3,5 s.
- 🔄 S3-06 (aceptación) sigue en su rama: toca `lab/README.md`, `QueryDoctor.slnx`, `HANDOFF.md` y `SPRINTS.md`, igual
  que S3-07. Quien mergee segundo resuelve conflictos (en `lab/README.md` S3-07 solo agrega filas a la tabla).

## Cómo probarla
- Medir: `lab/Run-Perf.ps1 -Step prepare | ingest | dashboard | all`. **No corre en la CI.** Necesita `secrets/` completo
  más `secrets/perf_dba_password.txt` (12+ caracteres con mayúsculas, minúsculas, dígitos y símbolos).
- ⚠️ `ingest` y `dashboard` borran el volumen local de PostgreSQL y vacían Query Store de QD_Lab. Después:
  `lab/Run-Lab.ps1 -Step workload` antes de LabTests/AcceptanceTests. El DBA queda con la contraseña de la medición.
- Pruebas unitarias de la herramienta: `dotnet test tests/QueryDoctor.UnitTests --filter "FullyQualifiedName~Perf"`.
- Datos crudos: `lab/results/raw/perf/` (revisados: sin secretos).

## Lo que necesita saber cada uno
**Maykol**
- **D-1:** `SecretProtector` es singleton y comparte una instancia de `AesGcm` entre hilos. Con solicitudes simultáneas,
  `Decrypt` falla (`AuthenticationTagMismatchException` → HTTP 500). Riesgo: dos `Encrypt` a la vez en el Worker
  podrían guardar un cifrado que no se pueda descifrar (RNF-SEG-14). Hay un arreglo listo **sin commit** en la rama
  `claude/kind-pare-dfa82e` (un `AesGcm` por operación, mismo formato de sobre, prueba de concurrencia). Revísalo tú.
**Daniel**
- **D-2:** `GET /queries` no pagina. Cada respuesta pesa ~2,1 MB y descifra y enmascara las 5 000 consultas. Con 5
  usuarios, la Api llega a 7 núcleos y 2,3 GB. La arquitectura prevé paginación en servidor y un extracto enmascarado
  precalculado. Sigue abierto, además, que `/queries` sea solo DBA (RNF-SEG-12).
**Todos**
- `tests/QueryDoctor.PerfTests` es una consola, no un proyecto xUnit: la CI la compila pero no la ejecuta.
- Durante la medición, QD_Lab usa intervalos de 60 min, porque con 1 min el sondeo programa una ingesta automática
  cada minuto. Al terminar vuelve a 1 min.

## Deuda y límites conocidos
- D-1 y D-2: repetir `Run-Perf.ps1 -Step dashboard` cuando entren los arreglos.
- D-3: el índice para la búsqueda de lo vencido (S2-13) sigue sin medir. D-4: la meta de CPU es frágil para ingestas de
  menos de 1 s (decide el tutor en DA-06).
- Medido solo en SQL Server 2022, en una máquina con Docker Desktop; la base del tablero no tiene hallazgos (L-3).
- Heredado: web sin `evidenceExpired` (Daniel); sin alta de Desarrolladores (D6) ni RLS; DA-06, DA-13 y DA-14 abiertas.

## Siguiente paso
`/audit` de S3-07 en una sesión aparte y PR `feat/S3-07-rendimiento` → `main`. Coordinar D-1 con Maykol y repetir la
medición del tablero después de su arreglo. S1-20 (tutor) debe ratificar las metas † antes de la evaluación final.
