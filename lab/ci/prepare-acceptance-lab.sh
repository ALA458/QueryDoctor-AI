#!/usr/bin/env bash
# S3-06 (RNF-COM-01): prepara el laboratorio de aceptación desde cero dentro de un contenedor de SQL Server.
# Lo usa el job "acceptance" de .github/workflows/ci.yml y sirve igual en local (p. ej. un SQL Server 2019 aparte).
#
#   SA_PASSWORD=... READER_PASSWORD=... lab/ci/prepare-acceptance-lab.sh <contenedor>
#
# - Las contraseñas llegan por variable de entorno y entran al contenedor con "docker exec -e": nunca en la línea de
#   comandos de sqlcmd del host, nunca en el repo (RNF-SEG-04). En CI se generan al azar en cada corrida.
# - SOLO LABORATORIO: el certificado del contenedor es autofirmado, por eso sqlcmd usa -C (invariante 3: no es una
#   excepción para la plataforma).
# - Orden: 01 (base) → 04 (cuentas) → 02 y 03 (datos y escenarios) → 11 (QD_Lab_NoQS, qd_perf) → carga 05 + 12 dos
#   veces con más de un minuto entre ellas, para que haya al menos dos intervalos de Query Store (CA-HU07.1).
set -euo pipefail

container="${1:?Uso: prepare-acceptance-lab.sh <contenedor>}"
: "${SA_PASSWORD:?Falta SA_PASSWORD}"
: "${READER_PASSWORD:?Falta READER_PASSWORD}"
sql_dir="$(cd "$(dirname "$0")/../sql" && pwd)"

# Las imágenes recientes traen mssql-tools18; algunas de 2019 solo mssql-tools.
sqlcmd="$(docker exec "$container" bash -c 'ls /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd 2>/dev/null | head -1')"
if [[ -z "$sqlcmd" ]]; then
    echo "No se encontró sqlcmd en $container" >&2
    exit 1
fi

run() {
    local file="$1"
    echo "== $(basename "$file")"
    # Algunos scripts (03) se guardaron con BOM UTF-8; Run-Lab.ps1 lo quita al leerlos con Get-Content, aquí se quita
    # antes de pasarlos por stdin (sqlcmd lo trataría como texto y fallaría con "Incorrect syntax").
    sed '1s/^\xEF\xBB\xBF//' "$file" \
        | docker exec -i -e SQLCMDPASSWORD="$SA_PASSWORD" -e READER_PASSWORD="$READER_PASSWORD" "$container" \
            "$sqlcmd" -S localhost -U sa -C -I -b -W > /dev/null
}

echo "== Esperando a SQL Server en $container"
for _ in $(seq 1 60); do
    if docker exec -e SQLCMDPASSWORD="$SA_PASSWORD" "$container" "$sqlcmd" -S localhost -U sa -C -b -Q "SELECT 1" > /dev/null 2>&1; then
        break
    fi
    sleep 2
done
docker exec -e SQLCMDPASSWORD="$SA_PASSWORD" "$container" "$sqlcmd" -S localhost -U sa -C -b -h -1 -Q "SET NOCOUNT ON; SELECT @@VERSION"

for f in 01-database.sql 04-readonly-login.sql 02-schema-data.sql 03-scenarios.sql 11-probe-fixtures.sql; do
    run "$sql_dir/$f"
done

run "$sql_dir/05-workload.sql"
run "$sql_dir/12-acceptance-scenarios.sql"
echo "== Esperando 65 s para un segundo intervalo de Query Store (INTERVAL_LENGTH_MINUTES = 1)"
sleep 65
run "$sql_dir/05-workload.sql"
run "$sql_dir/12-acceptance-scenarios.sql"
echo "== Laboratorio listo"
