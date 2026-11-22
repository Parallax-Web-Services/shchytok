#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$PreviewDirectory = 'D:\Parallax\StationPreview',
    [ValidateSet('gtk', 'none')]
    [string]$DisplayMode = 'gtk',
    [switch]$GuestDesktop
)
$ErrorActionPreference = 'Stop'
$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$state = Join-Path $PreviewDirectory 'session'
$image = Join-Path $PreviewDirectory 'station-preview.bin'
& (Join-Path $tools 'windows_vm.ps1') -Action start -ImagePath $image `
    -StateDirectory $state -ImageKind parallax-station-branding-preview `
    -DisplayMode $DisplayMode -QmpPort 4445 -SshPort 2223
if (-not $?) { throw 'Preview launch failed.' }
$index = 0
foreach ($delay in @(0, 1, 3, 8, 16)) {
    if ($delay) { Start-Sleep -Seconds $delay }
    $screen = Join-Path $state ('boot-{0:D2}.png' -f $index)
    & (Join-Path $tools 'inspect_vm.ps1') -StateDirectory $state -ScreenPath $screen
    $index++
}
if ($GuestDesktop) {
    $sessionScript = Join-Path $tools 'preview_guest_session.py'
    $payload = [Convert]::ToBase64String([IO.File]::ReadAllBytes($sessionScript))
    & (Join-Path $tools 'preview_guest.ps1') -StateDirectory $state `
        -Command "printf %s $payload | base64 -d | python3"
    if (-not $?) { throw 'Guest desktop setup failed; inspect the running VM.' }
    Start-Sleep -Seconds 2
    & (Join-Path $tools 'inspect_vm.ps1') -StateDirectory $state `
        -ScreenPath (Join-Path $state 'desktop.png')
}
Write-Output "Preview is running. Boot screenshots: $state"
