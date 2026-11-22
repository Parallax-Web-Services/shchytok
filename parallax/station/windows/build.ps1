#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDirectory = 'D:\Parallax\StationWindows\app',
    [switch]$SmokeTest,
    [switch]$Restart,
    [switch]$Run
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$project = Join-Path $PSScriptRoot 'Parallax.Station.Windows.csproj'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'The .NET 8 Windows desktop SDK is required.' }
$exe = Join-Path $OutputDirectory 'Parallax.Station.exe'
if ($Restart) {
    $session = (Get-Process -Id $PID).SessionId
    foreach ($station in @(Get-Process -Name 'Parallax.Station' -ErrorAction SilentlyContinue)) {
        if ($station.SessionId -eq $session -and $station.Path -ieq $exe) {
            if (-not $station.CloseMainWindow() -or -not $station.WaitForExit(15000)) {
                throw 'Station did not close cleanly. Close its window before rebuilding.'
            }
        }
    }
}
& $dotnet publish $project -c Release --self-contained false -o $OutputDirectory --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Native Station build failed.' }
$exe = Join-Path $OutputDirectory 'Parallax.Station.exe'
if ($SmokeTest) {
    $evidence = Join-Path (Split-Path -Parent $OutputDirectory) 'evidence'
    $process = Start-Process -FilePath $exe -ArgumentList @('--smoke-test', ('"' + $evidence + '"')) -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Native Station smoke check failed; inspect $evidence." }
    $restartCheck = Start-Process -FilePath $exe -ArgumentList @('--verify-context', ('"' + $evidence + '"')) -PassThru -Wait
    if ($restartCheck.ExitCode -ne 0) { throw "Station context did not survive process restart; inspect $evidence." }
    Write-Output "Native Windows checks passed. Evidence: $evidence"
}
if ($Run) { Start-Process -FilePath $exe | Out-Null }
Write-Output "Native Station executable: $exe"
