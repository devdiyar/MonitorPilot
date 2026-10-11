param([switch]$Diagnose, [switch]$Hardware, [switch]$LiveHardware, [switch]$Focus, [switch]$Timeout)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'artifacts\release\MonitorPilot.exe'
$qa = Join-Path $projectRoot 'artifacts\qa'
New-Item -ItemType Directory -Path $qa -Force | Out-Null
$checks = @(@('--self-test','self-tests.json'),@('--test-live-ui','live-ui.json'))
if ($Diagnose) { $checks += ,@('--diagnose','diagnostics.json') }
if ($Hardware) { $checks += ,@('--test-ddc','hardware-roundtrip.json') }
if ($LiveHardware) { $checks += ,@('--test-live-ddc','live-hardware.json') }
if ($Focus) { $checks += ,@('--test-focus','focus-roundtrip.json') }
if ($Timeout) { $checks += ,@('--test-timeout','timeout-recovery.json') }
foreach ($check in $checks) {
    $resultPath = Join-Path $qa $check[1]
    $process = Start-Process -FilePath $exe -ArgumentList @($check[0],('"' + $resultPath + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Prüfung fehlgeschlagen: $($check[0]). Ergebnis: $resultPath" }
    Write-Output "OK: $($check[0]) -> $resultPath"
}
