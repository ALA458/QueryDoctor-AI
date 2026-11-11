<#
.SYNOPSIS
  S1-07 - Orquesta el laboratorio SQL Server 2022 y el protocolo empirico del Apendice A.
.DESCRIPTION
  Ejecuta los scripts de lab/sql dentro del contenedor `sqlserver` del compose.
  - La contrasena de sa se lee DENTRO del contenedor (/run/secrets), nunca en la linea de comandos.
  - La contrasena de qd_reader se lee de secrets/mssql_reader_password.txt y se pasa por variable de entorno.
  SOLO LABORATORIO: el certificado del contenedor es autofirmado, por eso sqlcmd usa -C.
  Esto NO es una excepcion para la plataforma (invariante 3: TLS con validacion fuera del lab).
.PARAMETER Step
  defaults | build | login | workload | extract | all
#>
[CmdletBinding()]
param(
    [ValidateSet('defaults', 'build', 'login', 'workload', 'extract', 'checks', 'repro', 'all')]
    [string]$Step = 'all'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sqlDir = Join-Path $PSScriptRoot 'sql'
$planDir = Join-Path $root 'tests\fixtures\plans'
$rawDir = Join-Path $PSScriptRoot 'results\raw'
New-Item -ItemType Directory -Force $planDir, $rawDir | Out-Null

function Get-ReaderPassword {
    $p = Join-Path $root 'secrets\mssql_reader_password.txt'
    if (-not (Test-Path $p)) { throw "Falta $p (una linea ASCII, 8+ caracteres con mayusculas, minusculas, numeros y simbolos)." }
    (Get-Content $p -Raw).Trim()
}

# Ejecuta un archivo .sql o un texto como sa dentro del contenedor.
function Invoke-SqlAsSa {
    param([string]$File, [string]$Text, [string]$Prefix = '', [switch]$Raw, [switch]$PlanOnly, [string]$User = 'sa')
    $script = if ($File) { Get-Content $File -Raw } else { $Text }
    $script = $Prefix + $script
    # Sin comillas internas: PowerShell 5 las elimina al pasar argumentos a programas nativos.
    $cmd = '/opt/mssql-tools18/bin/sqlcmd -S localhost -U ' + $User + ' -P ' + $(if ($User -eq 'sa') { '$(cat /run/secrets/mssql_sa_password)' } else { '$READER_PASSWORD' }) + ' -C -I -b ' + $(if ($Raw) { '-y 0' } else { '-W' })
    # PlanOnly: filtra en el contenedor solo las lineas de Showplan XML (ESC05 devuelve 1M de filas).
    if ($PlanOnly) { $cmd += ' | grep -a ^.ShowPlanXML' }
    $script | docker compose -f (Join-Path $root 'docker-compose.yml') exec -T -e "READER_PASSWORD=$(Get-ReaderPassword)" sqlserver bash -c $cmd
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd fallo (exit $LASTEXITCODE)" }
}

function Save-Utf8NoBom([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($false)))
}

$labels = 'ESC01', 'ESC02', 'ESC03', 'ESC04', 'ESC04B', 'ESC05', 'ESC06'

if ($Step -in 'defaults', 'all') {
    Write-Host '== F5: defaults de Query Store (BD nueva sin configurar)' -ForegroundColor Cyan
    Invoke-SqlAsSa -File (Join-Path $sqlDir '00-defaults-probe.sql') | Tee-Object (Join-Path $rawDir 'F5-defaults.txt')
}

if ($Step -in 'build', 'all') {
    Write-Host '== Construyendo QD_Lab' -ForegroundColor Cyan
    foreach ($f in '01-database.sql', '02-schema-data.sql', '03-scenarios.sql') {
        Invoke-SqlAsSa -File (Join-Path $sqlDir $f)
    }
}

if ($Step -in 'login', 'all') {
    Write-Host '== Logins qd_reader (VIEW DATABASE STATE) y qd_noperm' -ForegroundColor Cyan
    Invoke-SqlAsSa -File (Join-Path $sqlDir '04-readonly-login.sql')
}

if ($Step -in 'workload', 'all') {
    Write-Host '== Carga de trabajo (modo Query Store)' -ForegroundColor Cyan
    Invoke-SqlAsSa -File (Join-Path $sqlDir '05-workload.sql') | Out-Null
    Write-Host '== Carga de trabajo con plan REAL (verdad de terreno, SET STATISTICS XML ON)' -ForegroundColor Cyan
    $actual = @(Invoke-SqlAsSa -File (Join-Path $sqlDir '05-workload.sql') -Prefix "SET STATISTICS XML ON;`r`nGO`r`n" -Raw -PlanOnly)
    foreach ($l in $labels) {
        $mine = @($actual | Where-Object { $_ -like "*QD-LAB:$l *" })
        if ($mine.Count -eq 0) { Write-Warning "Sin plan real para $l"; continue }
        # Ultimo plan real emitido para el escenario (en ESC04 es el de la ejecucion con el valor dominante).
        Save-Utf8NoBom (Join-Path $planDir "$l.actual.sqlplan") $mine[-1]
        Write-Host "  $l.actual.sqlplan ($($mine.Count) ejecuciones)"
    }
    Invoke-SqlAsSa -Text 'EXEC QD_Lab.sys.sp_query_store_flush_db;' | Out-Null
}

if ($Step -in 'extract', 'all') {
    Write-Host '== Extrayendo planes guardados en Query Store (nvarchar(max), F8)' -ForegroundColor Cyan
    Remove-Item (Join-Path $planDir '*.qs.*.sqlplan') -ErrorAction SilentlyContinue
    foreach ($l in $labels) {
        $idq = "SET NOCOUNT ON; SELECT p.plan_id FROM QD_Lab.sys.query_store_plan p JOIN QD_Lab.sys.query_store_query q ON q.query_id = p.query_id JOIN QD_Lab.sys.query_store_query_text t ON t.query_text_id = q.query_text_id WHERE t.query_sql_text LIKE N'%/* QD-LAB:$l */%' AND t.query_sql_text NOT LIKE N'%sys.query_store%' AND t.query_sql_text NOT LIKE N'%sys.dm_exec%' ORDER BY p.plan_id;"
        # Salida normal con -W: encabezado + guiones + ids
        $ids = @(Invoke-SqlAsSa -Text $idq | Select-Object -Skip 2 | Where-Object { $_ -match '^\d+$' })
        $n = 0
        foreach ($id in $ids) {
            $n++
            $q = "SET NOCOUNT ON; SELECT CAST(query_plan AS nvarchar(max)) FROM QD_Lab.sys.query_store_plan WHERE plan_id = $id;"
            # Con -y 0 sqlcmd no imprime encabezado.
            $out = Invoke-SqlAsSa -Text $q -Raw
            Save-Utf8NoBom (Join-Path $planDir "$l.qs.$n.sqlplan") (($out -join "`n").Trim())
        }
        Write-Host "  $l : $n plan(es) en Query Store"
    }
}
if ($Step -in 'checks', 'all') {
    Write-Host '== Verificaciones F3/F6/F8/H4 (sa), F4 (qd_reader, qd_noperm)' -ForegroundColor Cyan
    $r = @()
    $r += '##### sa: 06-checks-sa.sql'
    $r += Invoke-SqlAsSa -File (Join-Path $sqlDir '06-checks-sa.sql')
    $r += '##### qd_reader: 07-checks-reader.sql'
    $r += Invoke-SqlAsSa -File (Join-Path $sqlDir '07-checks-reader.sql') -User 'qd_reader'
    $r += '##### qd_noperm (sin permisos): 08-checks-noperm.sql'
    $r += Invoke-SqlAsSa -File (Join-Path $sqlDir '08-checks-noperm.sql') -User 'qd_noperm'
    $r += '##### F4(c): GRANT VIEW DATABASE PERFORMANCE STATE a qd_noperm y repetir lectura'
    $r += Invoke-SqlAsSa -File (Join-Path $sqlDir '09-grant-perf-state.sql')
    $r += Invoke-SqlAsSa -File (Join-Path $sqlDir '08-checks-noperm.sql') -User 'qd_noperm'
    $r += '##### S1-08 Whitelist (6 sentencias con qd_reader): 10-whitelist-validation.sql'
    $r += Invoke-SqlAsSa -File (Join-Path $sqlDir '10-whitelist-validation.sql') -User 'qd_reader'
    Save-Utf8NoBom (Join-Path $rawDir 'checks.txt') ($r -join "`n")
    $r
}

if ($Step -in 'repro') {
    Write-Host '== Verificando reproducibilidad en 3 corridas consecutivas' -ForegroundColor Cyan
    $reproLines = @()
    for ($i = 1; $i -le 3; $i++) {
        Invoke-SqlAsSa -File (Join-Path $sqlDir '05-workload.sql') | Out-Null
        Invoke-SqlAsSa -Text 'EXEC QD_Lab.sys.sp_query_store_flush_db;' | Out-Null
        & $PSCommandPath -Step extract | Out-Null
        $items = Get-ChildItem $planDir -Filter '*.qs.*.sqlplan' | Sort-Object Name | ForEach-Object {
            $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 12)
            "{0}={1}({2})" -f $_.Name, $h, $_.Length
        }
        $line = "Run $i : " + ($items -join ' | ')
        Write-Host "  $line"
        $reproLines += $line
    }
    Save-Utf8NoBom (Join-Path $rawDir 'reproducibility-3runs.txt') ($reproLines -join "`n")
}

