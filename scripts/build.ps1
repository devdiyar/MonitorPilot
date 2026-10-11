param([string]$Dotnet = '', [switch]$Install)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Dotnet) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $Dotnet = $cmd.Source }
    else { $Dotnet = Join-Path $env:LOCALAPPDATA 'MonitorPilotTools\dotnet\dotnet.exe' }
}
if (-not (Test-Path -LiteralPath $Dotnet)) { throw 'Bitte .NET 10 SDK installieren oder -Dotnet angeben.' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $projectRoot 'src\MonitorPilot\MonitorPilot.csproj'
$output = Join-Path $projectRoot 'artifacts\release'
# Keeping managed assemblies uncompressed avoids startup decompression and private
# memory copies. The standalone executable is larger; see the measured comparison.
& $Dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=none -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
$exe = Join-Path $output 'MonitorPilot.exe'
$setup = Join-Path $projectRoot 'artifacts\MonitorPilot-Setup.exe'
Copy-Item -LiteralPath $exe -Destination $setup -Force
$testResult = Join-Path $projectRoot 'artifacts\qa\self-tests.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $testResult) -Force | Out-Null
$test = Start-Process -FilePath $exe -ArgumentList @('--self-test',('"' + $testResult + '"')) -WindowStyle Hidden -Wait -PassThru
if ($test.ExitCode -ne 0) { throw "Selbsttests fehlgeschlagen: $testResult" }
$uiResult = Join-Path $projectRoot 'artifacts\qa\live-ui.json'
$uiTest = Start-Process -FilePath $exe -ArgumentList @('--test-live-ui',('"' + $uiResult + '"')) -WindowStyle Hidden -Wait -PassThru
if ($uiTest.ExitCode -ne 0) { throw "Live-Oberfläche fehlgeschlagen: $uiResult" }
if ($Install) {
    $installProcess = Start-Process -FilePath $setup -ArgumentList '--silent' -WindowStyle Hidden -Wait -PassThru
    if ($installProcess.ExitCode -ne 0) { throw 'Installation fehlgeschlagen. Lokales activity.log prüfen.' }
}
Get-FileHash -LiteralPath $exe,$setup -Algorithm SHA256
