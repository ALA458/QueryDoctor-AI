#!/usr/bin/env bash
# S3-06 (RNF-COM-01, D7): un job de aceptación en verde con pruebas omitidas no es evidencia. dotnet test devuelve 0
# aunque todas las pruebas salgan Skipped (por ejemplo, si QD_LAB_SQL quedara vacía), así que aquí se leen los .trx y
# se exige que se haya ejecutado lo esperado (auditoría M2 de S3-06).
#
#   lab/ci/check-acceptance-results.sh <carpeta-con-trx> <plataforma>
#
# Reglas: AcceptanceTests sin ninguna omitida; LabTests sin omitidas, salvo una en sql2019 (la cuenta con solo
# VIEW DATABASE PERFORMANCE STATE, que existe desde 2022). Ninguna fallida y al menos una ejecutada en cada suite.
set -euo pipefail

dir="${1:?Uso: check-acceptance-results.sh <carpeta> <plataforma>}"
platform="${2:?Falta la plataforma (sql2022, sql2019)}"
status=0

counter() {
    local trx="$1" name="$2"
    grep -o "<Counters [^>]*>" "$trx" | grep -oE "\b$name=\"[0-9]+\"" | grep -oE '[0-9]+'
}

check() {
    local suite="$1" allowed_skips="$2"
    local file="$dir/$suite-$platform.trx"
    if [[ ! -f "$file" ]]; then
        echo "::error::Falta $file: la suite $suite no dejó resultados."
        status=1
        return
    fi

    local total executed failed skipped
    total="$(counter "$file" total)"
    executed="$(counter "$file" executed)"
    failed="$(counter "$file" failed)"
    skipped=$((total - executed))
    echo "$suite ($platform): total=$total ejecutadas=$executed fallidas=$failed omitidas=$skipped (permitidas $allowed_skips)"

    if (( executed == 0 || failed > 0 || skipped > allowed_skips )); then
        echo "::error::$suite en $platform no es evidencia de aceptación: $executed ejecutadas, $failed fallidas, $skipped omitidas."
        status=1
    fi
}

check AcceptanceTests 0
if [[ "$platform" == "sql2019" ]]; then
    check LabTests 1
else
    check LabTests 0
fi

exit "$status"
