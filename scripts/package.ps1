param([string]$Dotnet = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Dotnet) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $Dotnet = $cmd.Source }
    else { $Dotnet = Join-Path $env:LOCALAPPDATA 'MonitorPilotTools\dotnet\dotnet.exe' }
}
$exe = Join-Path $projectRoot 'artifacts\release\MonitorPilot.exe'
$setup = Join-Path $projectRoot 'artifacts\MonitorPilot-Setup.exe'
if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $setup)) { throw 'Run scripts/build.ps1 first.' }
$selfTests = Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\qa\self-tests.json') -Raw | ConvertFrom-Json
$uiTests = Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\qa\live-ui.json') -Raw | ConvertFrom-Json
if (-not $selfTests.Passed -or -not $uiTests.Passed) { throw 'Release tests have not passed.' }
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\MonitorPilot\MonitorPilot.csproj') -Raw)).Project.PropertyGroup.Version
$binaryVersion = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion.Split('+')[0]
if ($binaryVersion -ne $version -or $uiTests.Version -ne ($version + '.0')) { throw 'Build and test versions do not match the source. Rebuild before packaging.' }
if ((Get-FileHash -LiteralPath $exe).Hash -ne (Get-FileHash -LiteralPath $setup).Hash) { throw 'Setup and portable executables differ.' }
$project = Join-Path $projectRoot 'src\MonitorPilot\MonitorPilot.csproj'
$packJson = & $Dotnet msbuild $project -t:ResolveFrameworkReferences -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -getItem:ResolvedRuntimePack --nologo
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve runtime notices.' }
$packs = ($packJson | ConvertFrom-Json).Items.ResolvedRuntimePack
$core = $packs | Where-Object FrameworkName -eq 'Microsoft.NETCore.App'
$desktop = $packs | Where-Object FrameworkName -eq 'Microsoft.WindowsDesktop.App'
if (-not $core -or -not $desktop) { throw 'Runtime pack paths are unavailable.' }
$output = Join-Path $projectRoot 'artifacts\packages'
$staging = Join-Path $projectRoot ('artifacts\package-staging\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$files = @()
foreach ($kind in @('Portable','Setup')) {
    $folder = Join-Path $staging $kind
    $notices = Join-Path $folder 'licenses'
    New-Item -ItemType Directory -Path $notices -Force | Out-Null
    if ($kind -eq 'Portable') { Copy-Item -LiteralPath $exe -Destination (Join-Path $folder 'MonitorPilot.exe') }
    else { Copy-Item -LiteralPath $setup -Destination (Join-Path $folder 'MonitorPilot-Setup.exe') }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $folder 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $folder 'THIRD-PARTY-NOTICES.md')
    Copy-Item -LiteralPath (Join-Path $core.PackageDirectory 'LICENSE.TXT') -Destination (Join-Path $notices 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $core.PackageDirectory 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $notices 'DOTNET-THIRD-PARTY-NOTICES.txt')
    Copy-Item -LiteralPath (Join-Path $desktop.PackageDirectory 'LICENSE') -Destination (Join-Path $notices 'WPF-LICENSE.txt')
    $quickstart = @"
MonitorPilot $version - Windows x64

$kind edition. Extract this complete folder before starting the executable.
Setup: run MonitorPilot-Setup.exe. Portable: run MonitorPilot.exe.
No separate .NET installation or administrator privileges are required.
The interface is currently German. Brightness and color sliders apply live.
Enable DDC/CI in external monitors for hardware controls.
Closing the window keeps the app in the tray; choose Beenden to exit.
Display changes require confirmation; no confirmation triggers rollback.
Profiles and recovery data: %LOCALAPPDATA%\MonitorPilot

Docs and compatibility: https://github.com/devdiyar/MonitorPilot
Issues: https://github.com/devdiyar/MonitorPilot/issues
The executable is currently unsigned. SHA256SUMS.txt lists release hashes.
Source uses MIT. Bundled runtime notices are in licenses/.
"@
    Set-Content -LiteralPath (Join-Path $folder 'START-HERE.txt') -Value $quickstart -Encoding utf8
    $zip = Join-Path $output ("MonitorPilot-$kind-win-x64.zip")
    Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip -CompressionLevel Optimal -Force
    $files += $zip
}
$commit = git -C $projectRoot rev-parse HEAD
$info = [ordered]@{
    Version=$version; SourceCommit=$commit; Runtime='win-x64'; CreatedUtc=[DateTime]::UtcNow.ToString('o')
    ExecutableSHA256=(Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant()
    DotnetRuntime=$core.NuGetPackageVersion; WindowsDesktopRuntime=$desktop.NuGetPackageVersion
    SelfTestsPassed=$selfTests.Total; WpfChecksPassed=$uiTests.Checks.Count
}
$buildInfo = Join-Path $output 'BUILD-INFO.json'
$info | ConvertTo-Json | Set-Content -LiteralPath $buildInfo -Encoding utf8
$files += $buildInfo
$hashLines = @($files | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) })
$hashLines += (Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant() + '  MonitorPilot.exe'
$hashLines += (Get-FileHash -LiteralPath $setup).Hash.ToLowerInvariant() + '  MonitorPilot-Setup.exe'
$hashLines | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Get-ChildItem -LiteralPath $output -File | Select-Object Name,Length
