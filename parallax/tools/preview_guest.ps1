#requires -Version 5.1
<# Run a command only in the locally launched, UUID-checked preview guest.
   Uses ChromiumOS's public test-image SSH key, never a personal SSH identity. #>
[CmdletBinding()]
param(
    [string]$StateDirectory = 'D:\Parallax\StationPreview\session',
    [string]$TestKeySource = '\\wsl.localhost\Ubuntu\mnt\wsl\projects\workspace\parallax\shchytok\src\third_party\chromite\ssh_keys\testing_rsa',
    [string]$Command = 'cat /etc/parallax-station-preview.json; uname -r'
)
$ErrorActionPreference = 'Stop'
$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$state = Get-Content -LiteralPath (Join-Path $StateDirectory 'vm-state.json') -Raw | ConvertFrom-Json
if ($state.Kind -ne 'parallax-station-branding-preview' -or $state.SshHost -ne '127.0.0.1') {
    throw 'Expected the isolated local branding preview guest.'
}
$current = & (Join-Path $tools 'inspect_vm.ps1') -StateDirectory $StateDirectory | ConvertFrom-Json
if (-not $current.Running) { throw 'The UUID-checked preview is not running.' }
$listener = Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort ([int]$state.SshPort) -State Listen
if (-not ($listener | Where-Object { $_.OwningProcess -eq $state.ProcessId })) {
    throw 'The SSH forwarding listener does not belong to the recorded preview VM.'
}
$sshStatePath = Join-Path $StateDirectory 'ssh-session.json'
if (Test-Path -LiteralPath $sshStatePath) {
    $sshState = Get-Content -LiteralPath $sshStatePath -Raw | ConvertFrom-Json
} else {
    $privateDirectory = Join-Path $env:TEMP ('parallax-station-preview-' + $state.Uuid)
    New-Item -ItemType Directory -Path $privateDirectory -ErrorAction Stop | Out-Null
    $key = Join-Path $privateDirectory 'testing_rsa'
    Copy-Item -LiteralPath $TestKeySource -Destination $key -ErrorAction Stop
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = Get-Acl -LiteralPath $key
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($identity)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, 'Read', 'Allow'))
    Set-Acl -LiteralPath $key -AclObject $acl
    $sshState = @{KeyPath=$key; KnownHostsPath=(Join-Path $privateDirectory 'known_hosts')}
    $sshState | ConvertTo-Json | Set-Content -LiteralPath $sshStatePath -Encoding UTF8
}
$ssh = Join-Path $env:WINDIR 'System32\OpenSSH\ssh.exe'
# The new host is the freshly created local guest, checked above through its
# QMP UUID and forwarding-process ownership. Subsequent key changes are rejected.
$arguments = @('-i', $sshState.KeyPath, '-p', [string]$state.SshPort,
    '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15',
    '-o', 'StrictHostKeyChecking=accept-new', '-o', "UserKnownHostsFile=$($sshState.KnownHostsPath)",
    'root@127.0.0.1', $Command)
& $ssh @arguments
if ($LASTEXITCODE -ne 0) { throw "Preview guest command failed with exit $LASTEXITCODE." }
