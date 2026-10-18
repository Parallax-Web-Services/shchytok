#requires -Version 5.1
<#
.SYNOPSIS
Inspect native Windows QEMU or start an upstream reference guest with a writable overlay.
.DESCRIPTION
Uses Windows Hypervisor Platform (WHPX). The caller supplies a verified raw image.
No downloads, installations, execution-policy changes, or base-image writes occur.
Copy this trusted repository helper to a local Windows path before running it.
#>
[CmdletBinding()]
param(
    [ValidateSet('doctor', 'start')]
    [string]$Action = 'doctor',
    [string]$ImagePath,
    [string]$StateDirectory,
    [string]$QemuPath,
    [ValidateSet('gtk', 'none')]
    [string]$DisplayMode = 'gtk',
    [ValidateNotNullOrEmpty()]
    [string]$CpuModel = 'qemu64,+ssse3,+sse4.1,+sse4.2,+aes,+pclmulqdq,+popcnt,+cx16,+lahf_lm,-xsave,-xsaveopt,-xsavec,-xsaves,-avx,-avx2',
    [ValidateRange(1024, 65535)]
    [int]$QmpPort = 4444,
    [ValidateRange(1024, 65535)]
    [int]$SshPort = 2222
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-Qemu {
    $candidates = @()
    if ($QemuPath) {
        if (Test-Path -LiteralPath $QemuPath -PathType Container) {
            $candidates += Join-Path $QemuPath 'qemu-system-x86_64.exe'
        } else {
            $candidates += $QemuPath
        }
    } else {
        $found = Get-Command 'qemu-system-x86_64.exe' -ErrorAction SilentlyContinue
        if ($found) { $candidates += $found.Source }
        $candidates += 'C:\msys64\ucrt64\bin\qemu-system-x86_64.exe'
        if ($env:ProgramFiles) {
            $candidates += Join-Path $env:ProgramFiles 'qemu\qemu-system-x86_64.exe'
        }
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).ProviderPath
        }
    }
    throw 'Native Windows QEMU was not found. Pass -QemuPath to its executable or directory.'
}

function Invoke-NativeText {
    param([string]$Executable, [string[]]$Arguments)
    $output = & $Executable @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed (exit $LASTEXITCODE): $($output.Trim())"
    }
    return $output.Trim()
}

function Quote-WindowsArgument {
    param([string]$Value)
    # Start-Process joins ArgumentList; quote for Windows CommandLineToArgvW/CRT.
    if ($Value -notmatch '[\s"]') { return $Value }
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Assert-LoopbackPortAvailable {
    param([int]$Port)
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try { $listener.Start() }
    catch { throw "Loopback port $Port is occupied. Choose a different port; no process was stopped." }
    finally { $listener.Stop() }
}

try {
    $qemu = Resolve-Qemu
    $qemuImg = Join-Path (Split-Path -Parent $qemu) 'qemu-img.exe'
    if (-not (Test-Path -LiteralPath $qemuImg -PathType Leaf)) {
        throw "qemu-img.exe is missing beside $qemu"
    }
    $version = Invoke-NativeText $qemu @('--version')
    $accelerators = Invoke-NativeText $qemu @('-accel', 'help')
    if ($accelerators -notmatch '(?m)^\s*whpx\s*$') {
        throw 'This QEMU build does not list WHPX. Use a native Windows build with WHPX support.'
    }
    if ($Action -eq 'doctor') {
        Write-Output $version
        Write-Output (Invoke-NativeText $qemuImg @('--version'))
        Write-Output "QEMU: $qemu"
        Write-Output 'WHPX is listed by this build. This check does not start a VM or prove runtime acceleration.'
        return
    }

    if (-not $ImagePath -or -not $StateDirectory) {
        throw 'start requires -ImagePath (verified raw reference image) and an explicit -StateDirectory.'
    }
    $baseImage = (Resolve-Path -LiteralPath $ImagePath -ErrorAction Stop).ProviderPath
    if (-not (Test-Path -LiteralPath $baseImage -PathType Leaf)) {
        throw 'ImagePath must identify a regular raw image file.'
    }
    if (-not [System.IO.Path]::IsPathRooted($StateDirectory) -or $StateDirectory -match '::') {
        throw 'StateDirectory must be an absolute native Windows path or UNC path.'
    }
    New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null
    $state = (Resolve-Path -LiteralPath $StateDirectory).ProviderPath
    $overlay = Join-Path $state 'reference-overlay.qcow2'
    $stateFile = Join-Path $state 'vm-state.json'
    if ($baseImage -ceq $overlay) { throw 'The base image and overlay must have different paths.' }

    if (Test-Path -LiteralPath $stateFile -PathType Leaf) {
        $previous = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        if (Get-Process -Id $previous.ProcessId -ErrorAction SilentlyContinue) {
            throw 'The recorded PID is still active. Review the saved VM state before reusing this directory.'
        }
    }
    $baseInfo = Invoke-NativeText $qemuImg @('info', '--output=json', $baseImage) | ConvertFrom-Json
    if ($baseInfo.format -ne 'raw') { throw 'The reference base image must be raw, not qcow2 or another container.' }
    if (Test-Path -LiteralPath $overlay) {
        $overlayInfo = Invoke-NativeText $qemuImg @('info', '--output=json', $overlay) | ConvertFrom-Json
        if ($overlayInfo.format -ne 'qcow2' -or $overlayInfo.'backing-filename-format' -ne 'raw' -or
            $overlayInfo.'full-backing-filename' -cne $baseImage) {
            throw 'Existing overlay has an unexpected format or backing image. Use a different state directory.'
        }
    } else {
        Invoke-NativeText $qemuImg @('create', '-f', 'qcow2', '-F', 'raw', '-b', $baseImage, $overlay) | Write-Output
    }
    if ($QmpPort -eq $SshPort) { throw 'QMP and SSH forwarding require different loopback ports.' }
    Assert-LoopbackPortAvailable $QmpPort
    Assert-LoopbackPortAvailable $SshPort

    $vmUuid = [guid]::NewGuid().ToString()
    $stdoutLog = Join-Path $state 'qemu-stdout.log'
    $stderrLog = Join-Path $state 'qemu-stderr.log'
    $drivePath = $overlay.Replace(',', ',,')
    $qemuArguments = @(
        '-name', 'Parallax Station (upstream reference guest)',
        '-uuid', $vmUuid,
        '-machine', 'q35', '-accel', 'whpx', '-cpu', $CpuModel,
        '-smp', '4', '-m', '8192',
        '-device', 'virtio-scsi-pci,id=scsi0',
        '-drive', "file=$drivePath,format=qcow2,if=none,id=osdisk",
        '-device', 'scsi-hd,drive=osdisk,bus=scsi0.0',
        '-netdev', "user,id=net0,hostfwd=tcp:127.0.0.1:$SshPort-:22",
        '-device', 'virtio-net-pci,netdev=net0',
        '-device', 'virtio-rng-pci',
        '-device', 'virtio-vga', '-display', $DisplayMode,
        '-qmp', "tcp:127.0.0.1:$QmpPort,server=on,wait=off"
    )
    $argumentLine = ($qemuArguments | ForEach-Object { Quote-WindowsArgument $_ }) -join ' '
    $windowStyle = if ($DisplayMode -eq 'none') { 'Hidden' } else { 'Normal' }
    $vmProcess = Start-Process -FilePath $qemu -ArgumentList $argumentLine -WorkingDirectory $state `
        -WindowStyle $windowStyle -PassThru -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog
    [ordered]@{
        Kind = 'upstream-reference-guest'; ProcessId = $vmProcess.Id; Uuid = $vmUuid
        QemuPath = $qemu; ImagePath = $baseImage; OverlayPath = $overlay; CpuModel = $CpuModel; DisplayMode = $DisplayMode
        QmpHost = '127.0.0.1'; QmpPort = $QmpPort; SshHost = '127.0.0.1'; SshPort = $SshPort
        StartedUtc = [DateTime]::UtcNow.ToString('o')
        StdoutLog = $stdoutLog; StderrLog = $stderrLog
    } | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
    Start-Sleep -Seconds 2
    $vmProcess.Refresh()
    if ($vmProcess.HasExited) {
        $diagnostic = Get-Content -LiteralPath $stderrLog -Raw -ErrorAction SilentlyContinue
        throw "QEMU exited during startup ($($vmProcess.ExitCode)). $diagnostic"
    }
    Write-Output "Started upstream reference guest, PID $($vmProcess.Id)."
    Write-Output "Overlay: $overlay"
    Write-Output "CPU model: $CpuModel"
    Write-Output "Display mode: $DisplayMode"
    Write-Output "QMP: 127.0.0.1:$QmpPort; SSH forwarding: 127.0.0.1:$SshPort (requires guest SSH service)."
    Write-Output "State and startup diagnostics: $state"
    Write-Output 'This is an upstream reference VM, not a branded or boot-validated Parallax Station image.'
} catch {
    Write-Error $_
    exit 1
}