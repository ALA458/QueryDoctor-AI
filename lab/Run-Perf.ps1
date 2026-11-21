<#
.SYNOPSIS
  S3-07 - Pruebas de rendimiento (RNF-RND-01, RNF-RND-02) sobre el laboratorio. NO corre en la CI.
.DESCRIPTION
  Pasos separados:
    prepare    Query Store de QD_Lab listo para 5 000 consultas, carga de las 5 000 consultas distintas y conteo en
               sys.query_store_query. Si no llega a 5 000, se detiene: no se mide.
    ingest     (parte 2) ingesta completa + incremental: CPU de la sesion de la plataforma y esperas de bloqueo.
    dashboard  (parte 3) latencia p50/p95/p99 del listado y del detalle.
    all        todos, en orden.
  SOLO LABORATORIO:
  - Usa sa del contenedor sqlserver. La contrasena se lee DENTRO del contenedor (/run/secrets) y va por la variable
    SQLCMDPASSWORD, nunca en la linea de comandos de Windows ni en los archivos de salida.
  - El certificado del contenedor es autofirmado, por eso sqlcmd usa -C. No es una excepcion para la plataforma
    (invariante 3).
  - Cambiar opciones de Query Store en QD_Lab es preparacion del laboratorio, no algo que haga la plataforma (CA-HU02.3).
  Las cifras que salen de aqui son de la maquina donde se corre; el informe es docs/rendimiento/S3-07-resultados.md.
.PARAMETER Step
  prepare | ingest | dashboard | all
.PARAMETER SqlContainer
  Nombre del contenedor de SQL Server del laboratorio (por omision el del compose: querydoctor-sqlserver-1).
#>
[CmdletBinding()]
param(
    [ValidateSet('prepare', 'ingest', 'dashboard', 'all')]
    [string]$Step = 'all',
    [string]$SqlContainer = 'querydoctor-sqlserver-1'
)

$ErrorActionPreference = 'Stop'
$perfSqlDir = Join-Path $PSScriptRoot 'sql\perf'
$rawDir = Join-Path $PSScriptRoot 'results\raw\perf'
New-Item -ItemType Directory -Force $rawDir | Out-Null

$RequiredQueries = 5000

function Save-Utf8NoBom([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($false)))
}

function Assert-Container {
    $state = docker inspect -f '{{.State.Health.Status}}' $SqlContainer 2>$null
    if ($LASTEXITCODE -ne 0 -or $state -ne 'healthy') {
        throw "El contenedor $SqlContainer no esta arriba y sano. Levantalo con: docker compose up -d sqlserver"
    }
}

# Ejecuta un archivo .sql como sa dentro del contenedor. El archivo se copia con docker cp (en vez de pasarlo por una
# tuberia) para que PowerShell 5.1 no le agregue un BOM que sqlcmd no entiende.
# -DiscardResults: las filas se tiran dentro del contenedor y solo vuelven los mensajes (RAISERROR/PRINT).
function Invoke-LabSqlFile {
    param([string]$File, [switch]$DiscardResults)
    $target = '/tmp/qd-perf-' + [IO.Path]::GetFileName($File)
    docker cp $File "${SqlContainer}:$target" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker cp fallo para $File" }
    $sqlcmd = '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -I -b -W -i ' + $target
    $redirect = if ($DiscardResults) { ' -r0 2>&1 >/dev/null' } else { ' 2>&1' }
    $cmd = 'export SQLCMDPASSWORD=$(cat /run/secrets/mssql_sa_password); ' + $sqlcmd + $redirect
    $out = docker exec $SqlContainer bash -c $cmd
    $code = $LASTEXITCODE
    docker exec $SqlContainer rm -f $target | Out-Null
    if ($code -ne 0) { throw "sqlcmd fallo con $([IO.Path]::GetFileName($File)) (exit $code):`n$($out -join "`n")" }
    $out
}

if ($Step -in 'prepare', 'all') {
    Assert-Container
    $log = @()

    Write-Host '== S3-07 prepare: Query Store de QD_Lab (captura ALL, 2 048 MB, sin limpieza por tamano)' -ForegroundColor Cyan
    $log += '##### 01-perf-querystore.sql'
    $log += Invoke-LabSqlFile (Join-Path $perfSqlDir '01-perf-querystore.sql')

    Write-Host "== S3-07 prepare: $RequiredQueries consultas distintas (primera pasada)" -ForegroundColor Cyan
    $log += '##### 02-perf-workload.sql (pasada 1)'
    $log += Invoke-LabSqlFile (Join-Path $perfSqlDir '02-perf-workload.sql') -DiscardResults | ForEach-Object { Write-Host $_; $_ }

    Write-Host '== S3-07 prepare: conteo en sys.query_store_query' -ForegroundColor Cyan
    $count = @(Invoke-LabSqlFile (Join-Path $perfSqlDir '03-perf-count.sql'))
    $log += '##### 03-perf-count.sql'
    $log += $count
    $count | ForEach-Object { Write-Host $_ }
    Save-Utf8NoBom (Join-Path $rawDir 'prepare.txt') ($log -join "`n")

    $summary = $count | Where-Object { $_ -match 'PERF_COUNT=(\d+)' } | Select-Object -Last 1
    if (-not $summary) { throw 'No se pudo leer PERF_COUNT de 03-perf-count.sql.' }
    $null = $summary -match 'PERF_COUNT=(\d+) PERF_TEXTS=(\d+) PERF_RECENT=(\d+)'
    $perfCount = [int]$Matches[1]; $perfTexts = [int]$Matches[2]; $perfRecent = [int]$Matches[3]
    if ($perfCount -lt $RequiredQueries -or $perfTexts -lt $RequiredQueries -or $perfRecent -lt $RequiredQueries) {
        throw "Query Store tiene $perfCount consultas QD-PERF ($perfTexts textos distintos, $perfRecent en las ultimas 24 h). Se necesitan ${RequiredQueries}: NO se mide."
    }
    Write-Host "  OK: $perfCount consultas QD-PERF con $perfTexts textos distintos, $perfRecent ejecutadas en las ultimas 24 h." -ForegroundColor Green
    Write-Host "  Salida guardada en lab/results/raw/perf/prepare.txt"
}

if ($Step -in 'ingest', 'all') {
    throw 'El paso ingest se agrega en la parte 2 de S3-07.'
}

if ($Step -in 'dashboard', 'all') {
    throw 'El paso dashboard se agrega en la parte 3 de S3-07.'
}
