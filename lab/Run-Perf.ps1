<#
.SYNOPSIS
  S3-07 - Pruebas de rendimiento (RNF-RND-01, RNF-RND-02) sobre el laboratorio. NO corre en la CI.
.DESCRIPTION
  Pasos separados:
    prepare    Query Store de QD_Lab listo para 5 000 consultas, carga de las 5 000 consultas distintas y conteo en
               sys.query_store_query. Si no llega a 5 000, se detiene: no se mide.
    ingest     RNF-RND-01. Un calentamiento descartado + N repeticiones con base limpia en los dos lados (Query Store
               vaciado y recargado; volumen de PostgreSQL nuevo). En cada una: primera ingesta completa y una ingesta
               incremental posterior, con Extended Events (CPU de la sesion de la plataforma, esperas de bloqueo),
               carga de usuario concurrente y muestreo de memoria del Worker.
    dashboard  (parte 3) latencia p50/p95/p99 del listado y del detalle.
    all        todos, en orden.
  SOLO LABORATORIO:
  - Usa sa del contenedor sqlserver. La contrasena se lee DENTRO del contenedor (/run/secrets) y va por la variable
    SQLCMDPASSWORD, nunca en la linea de comandos de Windows ni en los archivos de salida.
  - El certificado del contenedor es autofirmado, por eso sqlcmd usa -C. No es una excepcion para la plataforma
    (invariante 3). La plataforma se levanta con docker-compose.lab.yml (excepcion de TLS solo del laboratorio).
  - Cambiar opciones de Query Store en QD_Lab, crear sesiones XE y el umbral de procesos bloqueados es preparacion del
    laboratorio, no algo que haga la plataforma (CA-HU02.3, invariante 1).
  - ingest BORRA los datos locales de la plataforma (volumen querydoctor_pgdata) y el contenido de Query Store de
    QD_Lab en cada repeticion (decision D4 de S3-07). Al terminar deja QD_Lab con intervalo de 1 minuto; hay que
    volver a correr lab/Run-Lab.ps1 -Step workload antes de las suites LabTests/AcceptanceTests.
  Las cifras que salen de aqui son de la maquina donde se corre; el informe es docs/rendimiento/S3-07-resultados.md.
.PARAMETER Step
  prepare | ingest | dashboard | all
.PARAMETER Repetitions
  Repeticiones medidas de la ingesta (ademas del calentamiento). Por omision 3.
.PARAMETER SkipBuild
  No reconstruir las imagenes api/worker/web ni la consola de medicion.
#>
[CmdletBinding()]
param(
    [ValidateSet('prepare', 'ingest', 'dashboard', 'all')]
    [string]$Step = 'all',
    [ValidateRange(1, 10)]
    [int]$Repetitions = 3,
    [switch]$SkipBuild,
    [string]$BaseUrl = 'http://127.0.0.1:8080'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$perfSqlDir = Join-Path $PSScriptRoot 'sql\perf'
$rawDir = Join-Path $PSScriptRoot 'results\raw\perf'
$secretsDir = Join-Path $root 'secrets'
New-Item -ItemType Directory -Force $rawDir | Out-Null

$RequiredQueries = 5000
$SqlContainer = 'querydoctor-sqlserver-1'
$DbContainer = 'querydoctor-db-1'
$WorkerContainer = 'querydoctor-worker-1'
$PgVolume = 'querydoctor_pgdata'
$ConcurrentWorkers = 2
$Invariant = [Globalization.CultureInfo]::InvariantCulture
$ComposeFiles = @(
    '-f', (Join-Path $root 'docker-compose.yml'),
    '-f', (Join-Path $root 'docker-compose.lab.yml'),
    '-f', (Join-Path $PSScriptRoot 'perf\docker-compose.perf.yml'))
$PerfDll = Join-Path $root 'tests\QueryDoctor.PerfTests\bin\Release\net10.0\QueryDoctor.PerfTests.dll'

# ------------------------------------------------------------------------------------------------ utilidades

function Save-Utf8NoBom([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($false)))
}

function Assert-Container([string]$Name) {
    $state = docker inspect -f '{{.State.Health.Status}}' $Name 2>$null
    if ($LASTEXITCODE -ne 0 -or $state -ne 'healthy') {
        throw "El contenedor $Name no esta arriba y sano."
    }
}

# Ejecuta un archivo .sql como sa dentro del contenedor. El archivo se copia con docker cp (en vez de pasarlo por una
# tuberia) para que PowerShell 5.1 no le agregue un BOM que sqlcmd no entiende.
# -DiscardResults: las filas se tiran dentro del contenedor y solo vuelven los mensajes (RAISERROR/PRINT).
# Cada linea se muestra en pantalla en cuanto llega (el avance de la carga) y tambien se devuelve para el registro.
function Invoke-LabSqlFile {
    param([string]$File, [switch]$DiscardResults, [switch]$Quiet)
    $target = '/tmp/qd-perf-' + [IO.Path]::GetFileName($File)
    docker cp $File "${SqlContainer}:$target" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker cp fallo para $File" }
    $sqlcmd = '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -I -b -W -i ' + $target
    # -r1 manda a stderr TODOS los mensajes (con -r0 solo los de severidad >= 11 y se perdia el avance).
    $redirect = if ($DiscardResults) { ' -r1 2>&1 >/dev/null' } else { ' 2>&1' }
    $cmd = 'export SQLCMDPASSWORD=$(cat /run/secrets/mssql_sa_password); ' + $sqlcmd + $redirect
    $out = @(docker exec $SqlContainer bash -c $cmd | ForEach-Object { if (-not $Quiet) { Write-Host $_ }; $_ })
    $code = $LASTEXITCODE
    # docker cp deja el archivo como root y el contenedor corre como mssql: se borra como root.
    docker exec -u root $SqlContainer rm -f $target | Out-Null
    if ($code -ne 0) { throw "sqlcmd fallo con $([IO.Path]::GetFileName($File)) (exit $code):`n$($out -join "`n")" }
    $out
}

# Plantilla .template.sql: reemplaza __CLAVE__ por su valor y la ejecuta (archivo temporal fuera del repo).
function Invoke-LabSqlTemplate {
    param([string]$Template, [hashtable]$Values, [switch]$Quiet)
    $text = Get-Content (Join-Path $perfSqlDir $Template) -Raw
    foreach ($k in $Values.Keys) { $text = $text.Replace("__${k}__", [string]$Values[$k]) }
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ($Template -replace '\.template\.sql$', '.sql')
    Save-Utf8NoBom $tmp $text
    try { Invoke-LabSqlFile $tmp -Quiet:$Quiet } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

# Consulta de solo lectura a PostgreSQL de la plataforma (salida sin alinear, campos separados por |).
# Solo comillas simples en el SQL: PowerShell 5.1 rompe las dobles al pasarlas a programas nativos.
function Invoke-Psql([string]$Sql) {
    $out = docker exec $DbContainer psql -U querydoctor -d querydoctor -At -F '|' -v ON_ERROR_STOP=1 -c $Sql
    if ($LASTEXITCODE -ne 0) { throw "psql fallo (exit $LASTEXITCODE)" }
    $out
}

function Invoke-Compose {
    & docker compose @ComposeFiles @args
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($args -join ' ') fallo (exit $LASTEXITCODE)" }
}

# La salida de la consola va a la pantalla, no al flujo de objetos (si no, se mezclaria con los resultados).
function Invoke-PerfTool {
    & dotnet $PerfDll @args | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "QueryDoctor.PerfTests $($args[0]) fallo (exit $LASTEXITCODE)" }
}

function ConvertTo-Utc([string]$Value) {
    [DateTimeOffset]::Parse($Value, $Invariant, [Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()
}

function Format-XeUtc([DateTimeOffset]$Value) { $Value.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffffff', $Invariant) }

function Format-Num([double]$Value, [string]$Format = '0.###') { $Value.ToString($Format, $Invariant) }

# Export-Csv de PowerShell 5.1 usa la cultura del equipo (coma decimal en es-CO): los numeros se pasan a texto con punto.
function Export-InvariantCsv($Rows, [string]$Path) {
    $Rows | ForEach-Object {
        $o = [ordered]@{}
        foreach ($p in $_.PSObject.Properties) {
            $o[$p.Name] = if ($p.Value -is [double] -or $p.Value -is [decimal]) { $p.Value.ToString($Invariant) } else { $p.Value }
        }
        [pscustomobject]$o
    } | Export-Csv $Path -NoTypeInformation -Encoding UTF8
}

function Assert-PerfCount([string]$LogFile) {
    $count = @(Invoke-LabSqlFile (Join-Path $perfSqlDir '03-perf-count.sql') -Quiet)
    if ($LogFile) { Save-Utf8NoBom $LogFile ($count -join "`n") }
    $summary = $count | Where-Object { $_ -match 'PERF_COUNT=(\d+)' } | Select-Object -Last 1
    if (-not $summary) { throw 'No se pudo leer PERF_COUNT de 03-perf-count.sql.' }
    $null = $summary -match 'PERF_COUNT=(\d+) PERF_TEXTS=(\d+) PERF_RECENT=(\d+) QS_TOTAL=(\d+)'
    $r = [pscustomobject]@{ Count = [int]$Matches[1]; Texts = [int]$Matches[2]; Recent = [int]$Matches[3]; Total = [int]$Matches[4]; Lines = $count }
    if ($r.Count -lt $RequiredQueries -or $r.Texts -lt $RequiredQueries -or $r.Recent -lt $RequiredQueries) {
        throw "Query Store tiene $($r.Count) consultas QD-PERF ($($r.Texts) textos distintos, $($r.Recent) en las ultimas 24 h). Se necesitan ${RequiredQueries}: NO se mide."
    }
    Write-Host "  OK: $($r.Count) consultas QD-PERF, $($r.Texts) textos distintos, $($r.Recent) en las ultimas 24 h ($($r.Total) en Query Store)." -ForegroundColor Green
    $r
}

function Assert-Secrets {
    $needed = 'postgres_password.txt', 'mssql_sa_password.txt', 'mssql_reader_password.txt', 'initial_dba_password.txt',
        'master_encryption_key.txt', 'perf_dba_password.txt'
    $missing = @($needed | Where-Object { -not (Test-Path (Join-Path $secretsDir $_)) })
    if ($missing.Count -gt 0) {
        throw ("Faltan secretos en ${secretsDir}: $($missing -join ', '). perf_dba_password.txt es la contrasena que la " +
            'medicion le pone al DBA en el primer ingreso (12+ caracteres con mayusculas, minusculas, numeros y simbolos).')
    }

    # Misma politica que Identity en la Api (AuthServiceCollectionExtensions): se revisa antes de borrar nada, para no
    # perder una base y una espera por una contrasena que la Api va a rechazar. Nunca se imprime la contrasena.
    $perf = [IO.File]::ReadAllText((Join-Path $secretsDir 'perf_dba_password.txt')).Trim()
    $initial = [IO.File]::ReadAllText((Join-Path $secretsDir 'initial_dba_password.txt')).Trim()
    $faltan = @()
    if ($perf.Length -lt 12) { $faltan += '12 o mas caracteres' }
    if ($perf -cnotmatch '[a-z]') { $faltan += 'una minuscula' }
    if ($perf -cnotmatch '[A-Z]') { $faltan += 'una mayuscula' }
    if ($perf -notmatch '[0-9]') { $faltan += 'un digito' }
    if ($perf -notmatch '[^a-zA-Z0-9]') { $faltan += 'un simbolo (por ejemplo ! # % * - _)' }
    if ($perf -ceq $initial) { $faltan += 'ser distinta de initial_dba_password.txt' }
    $perf = $null; $initial = $null
    if ($faltan.Count -gt 0) {
        throw "perf_dba_password.txt no cumple la politica de contrasenas de la Api. Le falta: $($faltan -join ', ')."
    }
}

# Entorno de la medicion (sin secretos): para la seccion "Entorno" del informe.
function Save-Environment {
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $os = Get-CimInstance Win32_OperatingSystem
    $lines = @(
        "fecha_utc: $([DateTime]::UtcNow.ToString('o'))",
        "cpu: $($cpu.Name) ($($cpu.NumberOfCores) nucleos, $($cpu.NumberOfLogicalProcessors) logicos)",
        "ram_host_gb: $(Format-Num ($os.TotalVisibleMemorySize / 1MB) '0.0')",
        "windows: $($os.Caption) $($os.Version)",
        "docker: $(docker version --format '{{.Server.Version}}')",
        "docker_ncpu: $(docker info --format '{{.NCPU}}')",
        "docker_mem_gb: $(Format-Num ([double](docker info --format '{{.MemTotal}}') / 1GB) '0.0')",
        "dotnet_sdk: $(dotnet --version)",
        "worker_runtime: $((docker exec $WorkerContainer dotnet --list-runtimes) -join ' ; ')",
        "postgres: $(Invoke-Psql 'SELECT version();')",
        "git_commit: $(git -C $root rev-parse --short HEAD)"
    )
    $sql = @(Invoke-LabSqlFile (Join-Path $perfSqlDir '00-env.sql') -Quiet)
    $lines += $sql | Where-Object { $_ -match '^(sqlserver|cpu_count|scheduler_count|memoria_mb|querystore)' }
    Save-Utf8NoBom (Join-Path $rawDir 'environment.txt') ($lines -join "`n")
    Write-Host '  Entorno guardado en lab/results/raw/perf/environment.txt'
}

# ------------------------------------------------------------------------------------------------ piezas de la medicion

# Volumen de PostgreSQL nuevo: la Api migra y siembra el DBA al arrancar (D4: base limpia).
function Reset-PlatformDb {
    Write-Host '  Base de PostgreSQL nueva (volumen querydoctor_pgdata)'
    Invoke-Compose stop api worker web | Out-Null
    Invoke-Compose rm -s -f db | Out-Null
    # Si el volumen no existe, docker escribe en stderr; con ErrorActionPreference = Stop eso cortaria el script.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { docker volume rm $PgVolume 2>&1 | Out-Null } finally { $ErrorActionPreference = $previous }
    $exists = docker volume ls -q --filter "name=^${PgVolume}$"
    if ($exists) { throw "No se pudo borrar el volumen $PgVolume (sigue en uso?)." }
    Invoke-Compose up -d --wait db api worker web | Out-Null
}

# La ingesta programada de QD_Lab corre en el minuto :05 de cada hora (intervalo de 60 min, supuesto S8). Antes de
# cada fase medida se espera a estar lejos de ese minuto; si aun asi se cuela, la repeticion se anula.
function Wait-SafeWindow {
    while ($true) {
        $m = [DateTime]::UtcNow.Minute
        if ($m -ge 8 -and $m -le 45) { return }
        Write-Host "  Esperando a salir de la ventana de la ingesta programada (minuto :05; ahora :$('{0:D2}' -f $m))..."
        Start-Sleep -Seconds 30
    }
}

function Start-ConcurrentLoad {
    $rendered = Join-Path ([IO.Path]::GetTempPath()) 'qd-perf-load.sql'
    $text = (Get-Content (Join-Path $perfSqlDir '20-concurrent-load.template.sql') -Raw).Replace('__SECONDS__', '3600')
    Save-Utf8NoBom $rendered $text
    docker cp $rendered "${SqlContainer}:/tmp/qd-perf-load.sql" | Out-Null
    Remove-Item $rendered -ErrorAction SilentlyContinue
    $procs = @()
    for ($i = 1; $i -le $ConcurrentWorkers; $i++) {
        # Comillas simples de PowerShell: el $(cat ...) lo expande bash dentro del contenedor, no PowerShell.
        $arg = 'exec ' + $SqlContainer + ' bash -c "export SQLCMDPASSWORD=$(cat /run/secrets/mssql_sa_password); ' +
            '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -I -H QD-PERF-LOAD -i /tmp/qd-perf-load.sql > /dev/null 2>&1"'
        $procs += Start-Process -FilePath docker -ArgumentList $arg -NoNewWindow -PassThru
    }
    Start-Sleep -Seconds 3
    $procs
}

function Stop-ConcurrentLoad($Procs) {
    Invoke-LabSqlFile (Join-Path $perfSqlDir '21-stop-load.sql') -Quiet | Out-Null
    foreach ($p in $Procs) {
        $exited = $p.WaitForExit(15000)
        if (-not $exited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
    docker exec -u root $SqlContainer rm -f /tmp/qd-perf-load.sql | Out-Null
}

# docker stats no da marcas de tiempo: un trabajo en segundo plano lo llama en bucle y antepone la hora UTC.
function Start-StatsSampler([string]$File) {
    $stop = "$File.stop"
    Remove-Item $File, $stop -ErrorAction SilentlyContinue
    $job = Start-Job -ArgumentList $File, $stop, $WorkerContainer, $SqlContainer -ScriptBlock {
        param($f, $s, $worker, $sql)
        while (-not (Test-Path $s)) {
            $ts = [DateTime]::UtcNow.ToString('o')
            $lines = docker stats --no-stream --format '{{json .}}' $worker $sql
            foreach ($l in $lines) { [IO.File]::AppendAllText($f, "$ts|$l`n") }
        }
    }
    [pscustomobject]@{ Job = $job; Stop = $stop; File = $File }
}

function Stop-StatsSampler($Sampler) {
    New-Item -ItemType File -Force $Sampler.Stop | Out-Null
    Wait-Job $Sampler.Job -Timeout 30 | Out-Null
    Remove-Job $Sampler.Job -Force
    Remove-Item $Sampler.Stop -ErrorAction SilentlyContinue
}

function ConvertTo-MiB([string]$Text) {
    if ($Text -notmatch '([\d.]+)\s*([KMGT]?i?B)') { return 0.0 }
    $n = [double]::Parse($Matches[1], $Invariant)
    switch ($Matches[2]) { 'B' { $n / 1MB } 'KiB' { $n / 1KB } 'kB' { $n / 1KB } 'MiB' { $n } 'MB' { $n } 'GiB' { $n * 1KB } 'GB' { $n * 1KB } default { $n } }
}

# Memoria del Worker y CPU del contenedor de SQL Server dentro de la ventana [From, To].
function Measure-Stats([string]$File, [DateTimeOffset]$From, [DateTimeOffset]$To) {
    $worker = @(); $sqlCpu = @()
    if (Test-Path $File) {
        foreach ($line in Get-Content $File) {
            $parts = $line.Split('|', 2)
            if ($parts.Count -lt 2) { continue }
            $ts = ConvertTo-Utc $parts[0]
            if ($ts -lt $From -or $ts -gt $To) { continue }
            $s = $parts[1] | ConvertFrom-Json
            if ($s.Name -eq $WorkerContainer) { $worker += ConvertTo-MiB ($s.MemUsage.Split('/')[0]) }
            if ($s.Name -eq $SqlContainer) { $sqlCpu += [double]::Parse($s.CPUPerc.TrimEnd('%'), $Invariant) }
        }
    }
    $wm = $worker | Measure-Object -Average -Maximum
    $sc = $sqlCpu | Measure-Object -Average -Maximum
    [pscustomobject]@{
        Samples = $worker.Count
        WorkerMemPeakMiB = if ($worker.Count) { $wm.Maximum } else { $null }
        WorkerMemMeanMiB = if ($worker.Count) { $wm.Average } else { $null }
        SqlCpuMeanPct = if ($sqlCpu.Count) { $sc.Average } else { $null }
        SqlCpuPeakPct = if ($sqlCpu.Count) { $sc.Maximum } else { $null }
    }
}

# Trabajo Hangfire de la ingesta manual: espera a que termine (la ejecucion queda Succeeded al confirmar el upsert, pero
# el analisis del motor y el score corren despues en el mismo trabajo). Solo lee el esquema hangfire.
function Wait-HangfireJob([Guid]$ConnectionId, [int]$TimeoutSeconds = 3600) {
    $sql = "SELECT j.id, j.statename, " +
        "(SELECT min(s.createdat)::text FROM hangfire.state s WHERE s.jobid = j.id AND s.name = 'Processing'), " +
        "(SELECT max(s.createdat)::text FROM hangfire.state s WHERE s.jobid = j.id AND s.name IN ('Succeeded','Failed','Deleted')) " +
        "FROM hangfire.job j WHERE j.invocationdata::text LIKE '%RunManualAsync%' AND j.arguments::text LIKE '%$ConnectionId%' " +
        "ORDER BY j.id DESC LIMIT 1;"
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        $row = @(Invoke-Psql $sql) | Select-Object -First 1
        if ($row) {
            $f = $row.Split('|')
            if ($f[1] -in 'Succeeded', 'Failed', 'Deleted' -and $f[3]) {
                return [pscustomobject]@{ JobId = $f[0]; State = $f[1]; ProcessingUtc = (ConvertTo-Utc $f[2]); EndUtc = (ConvertTo-Utc $f[3]) }
            }
        }
        if ([DateTime]::UtcNow -gt $deadline) { throw "El trabajo Hangfire de la ingesta no termino en $TimeoutSeconds s." }
        Start-Sleep -Milliseconds 500
    }
}

function Get-PlatformCounts([Guid]$ConnectionId) {
    $c = "'$ConnectionId'"
    $sql = "SELECT (SELECT count(*) FROM core.query WHERE connection_id = $c), " +
        "(SELECT count(*) FROM core.plan p JOIN core.query q ON q.id = p.query_id WHERE q.connection_id = $c), " +
        "(SELECT count(*) FROM core.runtime_stat WHERE connection_id = $c), " +
        "(SELECT count(*) FROM analysis.analysis_run r JOIN core.query q ON q.id = r.query_id WHERE q.connection_id = $c), " +
        "(SELECT count(*) FROM analysis.finding f JOIN analysis.analysis_run r ON r.id = f.analysis_run_id JOIN core.query q ON q.id = r.query_id WHERE q.connection_id = $c), " +
        "(SELECT count(*) FROM analysis.health_score WHERE connection_id = $c);"
    $f = (@(Invoke-Psql $sql) | Select-Object -First 1).Split('|')
    [pscustomobject]@{ Queries = [int]$f[0]; Plans = [int]$f[1]; RuntimeStats = [int]$f[2]; AnalysisRuns = [int]$f[3]; Findings = [int]$f[4]; HealthScores = [int]$f[5] }
}

function Get-XeValue([string]$Summary, [string]$Key) {
    if ($Summary -match "(^|\s)$Key=(\S+)") { return $Matches[2] }
    $null
}

# Una fase medida (full | incremental): XE + carga concurrente + muestreo -> ingesta manual -> fin del trabajo.
function Measure-IngestPhase([string]$Label, [string]$Phase, [Guid]$ConnectionId) {
    Wait-SafeWindow
    Write-Host "  [$Label/$Phase] Extended Events, carga concurrente y muestreo de memoria" -ForegroundColor Cyan
    docker exec -u root $SqlContainer bash -c 'rm -f /var/opt/mssql/log/qd_perf_s307*.xel' | Out-Null
    Invoke-LabSqlFile (Join-Path $perfSqlDir '10-xe-start.sql') -Quiet | Out-Null
    $load = Start-ConcurrentLoad
    $statsFile = Join-Path $rawDir "ingest-$Label-$Phase-stats.txt"
    $sampler = Start-StatsSampler $statsFile
    $runFile = Join-Path $rawDir "ingest-$Label-$Phase-run.json"
    try {
        Write-Host "  [$Label/$Phase] Ingesta manual"
        Invoke-PerfTool ingest --secrets $secretsDir --connection-id $ConnectionId --out $runFile --base-url $BaseUrl --timeout-seconds 3600
        $job = Wait-HangfireJob $ConnectionId
    }
    finally {
        Stop-StatsSampler $sampler
        Stop-ConcurrentLoad $load
    }

    $result = Get-Content $runFile -Raw | ConvertFrom-Json
    $run = $result.run
    $started = ConvertTo-Utc $run.startedAt
    $finished = ConvertTo-Utc $run.finishedAt
    $runSeconds = ($finished - $started).TotalSeconds

    $xe = @(Invoke-LabSqlTemplate -Template '11-xe-read.template.sql' -Values @{ FROM_UTC = (Format-XeUtc $started); TO_UTC = (Format-XeUtc $finished) } -Quiet)
    Save-Utf8NoBom (Join-Path $rawDir "ingest-$Label-$Phase-xe.txt") ($xe -join "`n")
    $summary = $xe | Where-Object { $_ -match '^XE_SUMMARY ' } | Select-Object -Last 1
    if (-not $summary) { throw 'No se pudo leer XE_SUMMARY de 11-xe-read.template.sql.' }

    $counts = Get-PlatformCounts $ConnectionId
    $stats = Measure-Stats $statsFile $job.ProcessingUtc $job.EndUtc
    $cpuUs = [double](Get-XeValue $summary 'cpu_us')
    $cpuCount = [int](Get-XeValue $summary 'cpu_count')
    $cpuPct = if ($runSeconds -gt 0) { 100.0 * $cpuUs / ($runSeconds * 1e6 * $cpuCount) } else { $null }
    $scheduled = @($result.allRuns | Where-Object { $_.trigger -eq 'Scheduled' -and $_.status -eq 'Succeeded' })

    [pscustomobject]@{
        repeticion = $Label
        fase = $Phase
        valida = ($scheduled.Count -eq 0 -and $run.status -eq 'Succeeded')
        estado = $run.status
        causa = $run.cause
        inicio_utc = $started.ToString('o')
        fin_utc = $finished.ToString('o')
        duracion_ejecucion_s = [math]::Round($runSeconds, 3)
        duracion_trabajo_s = [math]::Round(($job.EndUtc - $job.ProcessingUtc).TotalSeconds, 3)
        observado_desde_post_s = [math]::Round([double]$result.observedSeconds, 3)
        filas_upsert = $run.rowsUpserted
        filas_por_s = if ($runSeconds -gt 0) { [math]::Round([double]$run.rowsUpserted / $runSeconds, 1) } else { $null }
        pg_consultas = $counts.Queries
        pg_planes = $counts.Plans
        pg_runtime_stats = $counts.RuntimeStats
        pg_analisis = $counts.AnalysisRuns
        pg_hallazgos = $counts.Findings
        pg_scores = $counts.HealthScores
        xe_sentencias = [long](Get-XeValue $summary 'statements')
        xe_cpu_us = [long]$cpuUs
        xe_fuera_de_ventana = [long](Get-XeValue $summary 'outside_window')
        cpu_count = $cpuCount
        cpu_plataforma_pct = if ($null -ne $cpuPct) { [math]::Round($cpuPct, 4) } else { $null }
        esperas_bloqueo_plataforma = [long](Get-XeValue $summary 'platform_lock_waits')
        esperas_bloqueo_otras = [long](Get-XeValue $summary 'other_lock_waits')
        esperas_bloqueo_otras_ms = [long](Get-XeValue $summary 'other_lock_wait_ms')
        informes_bloqueo = [long](Get-XeValue $summary 'blocked_reports')
        bloqueos_por_plataforma = [long](Get-XeValue $summary 'blocked_by_platform')
        xe_eventos_perdidos = [long](Get-XeValue $summary 'dropped')
        muestras_docker = $stats.Samples
        worker_mem_pico_mib = if ($null -ne $stats.WorkerMemPeakMiB) { [math]::Round($stats.WorkerMemPeakMiB, 1) } else { $null }
        worker_mem_media_mib = if ($null -ne $stats.WorkerMemMeanMiB) { [math]::Round($stats.WorkerMemMeanMiB, 1) } else { $null }
        sqlserver_cpu_media_pct = if ($null -ne $stats.SqlCpuMeanPct) { [math]::Round($stats.SqlCpuMeanPct, 2) } else { $null }
        ingestas_programadas_coladas = $scheduled.Count
    }
}

function Invoke-IngestRepetition([string]$Label) {
    Write-Host "== S3-07 ingest: repeticion $Label" -ForegroundColor Cyan
    # Antes de registrar la conexion no hay ingesta programada: vaciar y cargar Query Store no necesita esperar.
    Write-Host '  Query Store vaciado (intervalo de 60 min) y primera pasada de la carga'
    Invoke-LabSqlFile (Join-Path $perfSqlDir '04-perf-reset-querystore.sql') -Quiet | Out-Null
    Invoke-LabSqlFile (Join-Path $perfSqlDir '02-perf-workload.sql') -DiscardResults | Out-Null
    $null = Assert-PerfCount (Join-Path $rawDir "ingest-$Label-count.txt")

    # Desde el sondeo existe la ingesta programada del minuto :05: se arranca lejos de ese minuto.
    Wait-SafeWindow
    Reset-PlatformDb
    $setupFile = Join-Path $rawDir "ingest-$Label-setup.json"
    Invoke-PerfTool setup --secrets $secretsDir --out $setupFile --base-url $BaseUrl
    $connectionId = [Guid]((Get-Content $setupFile -Raw | ConvertFrom-Json).connectionId)

    $full = Measure-IngestPhase $Label 'full' $connectionId
    if ($full.pg_consultas -ne $RequiredQueries) {
        Write-Warning "La primera ingesta dejo $($full.pg_consultas) consultas en PostgreSQL (se esperaban $RequiredQueries)."
    }

    Write-Host '  Segunda pasada de la carga (mismas 5 000 consultas) para la ingesta incremental'
    Invoke-LabSqlFile (Join-Path $perfSqlDir '02-perf-workload.sql') -DiscardResults | Out-Null
    $incr = Measure-IngestPhase $Label 'incremental' $connectionId
    @($full, $incr)
}

# ------------------------------------------------------------------------------------------------ pasos

if ($Step -in 'prepare', 'all') {
    Assert-Container $SqlContainer
    $log = @()

    Write-Host '== S3-07 prepare: Query Store de QD_Lab (captura ALL, 2 048 MB, sin limpieza por tamano)' -ForegroundColor Cyan
    $log += '##### 01-perf-querystore.sql'
    $log += Invoke-LabSqlFile (Join-Path $perfSqlDir '01-perf-querystore.sql')

    Write-Host "== S3-07 prepare: $RequiredQueries consultas distintas (primera pasada)" -ForegroundColor Cyan
    $log += '##### 02-perf-workload.sql (pasada 1)'
    $log += Invoke-LabSqlFile (Join-Path $perfSqlDir '02-perf-workload.sql') -DiscardResults

    Write-Host '== S3-07 prepare: conteo en sys.query_store_query' -ForegroundColor Cyan
    $count = Assert-PerfCount $null
    $log += '##### 03-perf-count.sql'
    $log += $count.Lines
    Save-Utf8NoBom (Join-Path $rawDir 'prepare.txt') ($log -join "`n")
    Write-Host '  Salida guardada en lab/results/raw/perf/prepare.txt'
}

if ($Step -in 'ingest', 'all') {
    Assert-Container $SqlContainer
    Assert-Secrets
    if (-not $SkipBuild) {
        Write-Host '== S3-07 ingest: compilando la consola de medicion e imagenes api/worker/web' -ForegroundColor Cyan
        & dotnet build (Join-Path $root 'tests\QueryDoctor.PerfTests') -c Release -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'No compilo tests/QueryDoctor.PerfTests.' }
        Invoke-Compose build api worker web
    }
    if (-not (Test-Path $PerfDll)) { throw "Falta ${PerfDll}: corre sin -SkipBuild." }

    $rows = @()
    try {
        $labels = @('calentamiento') + (1..$Repetitions | ForEach-Object { "r$_" })
        foreach ($label in $labels) {
            $rows += Invoke-IngestRepetition $label
            if ($label -eq 'calentamiento') { Save-Environment }
            Export-InvariantCsv $rows (Join-Path $rawDir 'ingest-summary.csv')
        }
    }
    finally {
        Write-Host '== S3-07 ingest: dejando QD_Lab con intervalo de 1 min y sin sesion XE' -ForegroundColor Cyan
        Invoke-LabSqlFile (Join-Path $perfSqlDir '09-perf-restore.sql') -Quiet | Out-Null
    }

    Write-Host '== S3-07 ingest: resumen (el calentamiento se descarta en el informe)' -ForegroundColor Cyan
    $rows | Format-Table repeticion, fase, valida, duracion_ejecucion_s, duracion_trabajo_s, filas_upsert, pg_consultas,
        cpu_plataforma_pct, esperas_bloqueo_otras, bloqueos_por_plataforma, worker_mem_pico_mib -AutoSize
    $invalid = @($rows | Where-Object { -not $_.valida })
    if ($invalid.Count -gt 0) { Write-Warning "$($invalid.Count) fase(s) no validas (ingesta programada colada o estado distinto de Succeeded): repetir." }
    Write-Host '  Datos crudos en lab/results/raw/perf/ (ingest-*.json, -xe.txt, -stats.txt, ingest-summary.csv)'
}

if ($Step -in 'dashboard', 'all') {
    throw 'El paso dashboard se agrega en la parte 3 de S3-07.'
}
