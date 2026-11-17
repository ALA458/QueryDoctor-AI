<#
.SYNOPSIS
    Barrido reproducible de calibración de umbrales sobre el split `calibration`
    y verificación ciega sobre `evaluation` y `all` (S3-01, DA-02, RF-MOT-14).
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$harnessExe = Join-Path $repoRoot "tools\QueryDoctor.EngineHarness\bin\$Configuration\net10.0\QueryDoctor.EngineHarness.exe"
if (-not (Test-Path $harnessExe)) {
    throw "No se encontró el ejecutable del arnés en '$harnessExe'. Compila primero con: dotnet build tools/QueryDoctor.EngineHarness --configuration $Configuration"
}

$rawDir = Join-Path $PSScriptRoot 'results\raw'
New-Item -ItemType Directory -Force $rawDir | Out-Null

function Save-Utf8NoBom {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Format-PctOrNa {
    param($Obj, [string]$PropName)
    $prop = $Obj.PSObject.Properties[$PropName]
    if ($null -eq $prop -or $null -eq $prop.Value) { return 'N/A' }
    return ('{0:P1}' -f [double]$prop.Value)
}

function Invoke-HarnessPoint {
    param(
        [string]$Parameter,
        [double]$Value,
        [hashtable]$Thresholds,
        [string]$Split = 'calibration'
    )

    $tmpFile = Join-Path ([IO.Path]::GetTempPath()) ("qd-sweep-{0}.json" -f [Guid]::NewGuid().ToString('N'))
    try {
        Save-Utf8NoBom -Path $tmpFile -Text ($Thresholds | ConvertTo-Json)
        $jsonText = & $harnessExe --split $Split --thresholds $tmpFile --format json
        if ($LASTEXITCODE -notin 0, 1) {
            throw "Fallo al ejecutar arnés (exit $LASTEXITCODE) para $Parameter=$Value"
        }
        $report = ($jsonText -join "`n") | ConvertFrom-Json
        return [ordered]@{
            parameter = $Parameter
            value = $Value
            split = $Split
            thresholds_hash = $report.thresholds_hash
            reg03 = $report.rules | Where-Object { $_.rule_id -eq 'REG-03' } | Select-Object -First 1
            reg04 = $report.rules | Where-Object { $_.rule_id -eq 'REG-04' } | Select-Object -First 1
            all_must_targets_met = $report.all_must_targets_met
        }
    }
    finally {
        Remove-Item -Path $tmpFile -ErrorAction SilentlyContinue
    }
}

$baseProfile = @{
    Reg03MinEstimatedRows = 10000.0
    Reg03MinPredicateVolumeRows = 1000.0
    Reg03RowsReadToReturnedRatio = 10.0
    MinQueryConsumptionPct = 1.0
    Reg04MinEstimatedExecutions = 1000.0
}

$sweepResults = @()

foreach ($v in @(0.0, 100.0, 500.0, 1000.0, 5000.0, 10000.0)) {
    $p = $baseProfile.Clone()
    $p.Reg03MinPredicateVolumeRows = $v
    $sweepResults += Invoke-HarnessPoint -Parameter 'Reg03MinPredicateVolumeRows' -Value $v -Thresholds $p
}

foreach ($v in @(5000.0, 10000.0, 15000.0, 20000.0)) {
    $p = $baseProfile.Clone()
    $p.Reg03MinEstimatedRows = $v
    $sweepResults += Invoke-HarnessPoint -Parameter 'Reg03MinEstimatedRows' -Value $v -Thresholds $p
}

foreach ($v in @(5.0, 10.0, 15.0, 20.0, 30.0)) {
    $p = $baseProfile.Clone()
    $p.Reg03RowsReadToReturnedRatio = $v
    $sweepResults += Invoke-HarnessPoint -Parameter 'Reg03RowsReadToReturnedRatio' -Value $v -Thresholds $p
}

foreach ($v in @(0.5, 1.0, 1.5, 2.5)) {
    $p = $baseProfile.Clone()
    $p.MinQueryConsumptionPct = $v
    $sweepResults += Invoke-HarnessPoint -Parameter 'MinQueryConsumptionPct' -Value $v -Thresholds $p
}

foreach ($v in @(10.0, 20.0, 100.0, 500.0, 1000.0, 1500.0, 2000.0)) {
    $p = $baseProfile.Clone()
    $p.Reg04MinEstimatedExecutions = $v
    $sweepResults += Invoke-HarnessPoint -Parameter 'Reg04MinEstimatedExecutions' -Value $v -Thresholds $p
}

$sweepPath = Join-Path $rawDir 's3-01-calibration-sweep.json'
Save-Utf8NoBom -Path $sweepPath -Text ($sweepResults | ConvertTo-Json -Depth 6)

Write-Host '=== Barrido sobre --split calibration ===' -ForegroundColor Cyan
foreach ($row in $sweepResults) {
    $r3 = $row.reg03
    $r4 = $row.reg04
    $p3 = Format-PctOrNa -Obj $r3 -PropName 'precision'
    $c3 = Format-PctOrNa -Obj $r3 -PropName 'recall'
    $p4 = Format-PctOrNa -Obj $r4 -PropName 'precision'
    $c4 = Format-PctOrNa -Obj $r4 -PropName 'recall'
    Write-Host ("{0,-28} = {1,-7} | REG-03 TP={2,2} FP={3,2} FN={4,2} P={5,6} R={6,6} | REG-04 TP={7} FP={8} FN={9} P={10,6} R={11,6}" -f `
        $row.parameter, $row.value, $r3.true_positives, $r3.false_positives, $r3.false_negatives, $p3, $c3, `
        $r4.true_positives, $r4.false_positives, $r4.false_negatives, $p4, $c4)
}

foreach ($sp in @('calibration', 'evaluation', 'all')) {
    $outJson = Join-Path $rawDir "s3-01-$sp.json"
    $jsonOut = & $harnessExe --split $sp --check-targets --format json
    if ($LASTEXITCODE -ne 0) {
        throw "El split '$sp' no cumplió metas RNF-FUN-01 (exit $LASTEXITCODE)."
    }
    Save-Utf8NoBom -Path $outJson -Text ($jsonOut -join "`n")
    Write-Host "Split '$sp' validado con --check-targets OK -> $outJson" -ForegroundColor Green
}
