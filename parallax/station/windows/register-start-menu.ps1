#requires -Version 5.1
[CmdletBinding()]
param([string]$Executable = 'D:\Parallax\StationWindows\app\Parallax.Station.exe')
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($Executable)
if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw 'Build Parallax Station before registering its launcher.' }
$programs = [Environment]::GetFolderPath('Programs')
$folder = Join-Path $programs 'Parallax'
[IO.Directory]::CreateDirectory($folder) | Out-Null
$path = Join-Path $folder 'Parallax Station.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($path)
$shortcut.TargetPath = $target
$shortcut.Arguments = ''
$shortcut.WorkingDirectory = Split-Path -Parent $target
$shortcut.IconLocation = $target + ',0'
$shortcut.Description = 'Parallax Station - business operations workspace'
$shortcut.Save()
$check = $shell.CreateShortcut($path)
if ($check.TargetPath -ine $target -or -not (Test-Path -LiteralPath $path)) { throw 'The Station launcher could not be verified.' }
Write-Output ('Start menu launcher: ' + $path)
Write-Output ('Target: ' + $check.TargetPath)
