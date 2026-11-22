param([Parameter(Mandatory)][string]$StateDirectory, [string]$ScreenPath, [switch]$Quit)
$ErrorActionPreference = 'Stop'
$taskState = Get-Content -LiteralPath (Join-Path $StateDirectory 'vm-state.json') -Raw | ConvertFrom-Json
if ($taskState.Kind -notin @('upstream-reference-guest', 'parallax-station-branding-preview') -or $taskState.QmpHost -ne '127.0.0.1') {
    throw 'Expected a local Parallax reference or branding preview VM state.'
}
$taskClient = [Net.Sockets.TcpClient]::new()
try {
    $taskClient.Connect('127.0.0.1', [int]$taskState.QmpPort)
    $taskStream = $taskClient.GetStream()
    $taskStream.ReadTimeout = 15000
    $taskReader = [IO.StreamReader]::new($taskStream)
    $taskWriter = [IO.StreamWriter]::new($taskStream)
    $taskWriter.AutoFlush = $true
    $taskWriter.NewLine = "`n"
    $taskGreeting = $taskReader.ReadLine() | ConvertFrom-Json
    if (-not $taskGreeting.QMP) { throw 'Missing QMP greeting.' }
    function Invoke-TaskQmp {
        param([string]$Command, [hashtable]$CommandArguments = @{})
        $taskMessage = @{execute=$Command; id=$Command}
        if ($CommandArguments.Count) { $taskMessage.arguments = $CommandArguments }
        $taskWriter.WriteLine(($taskMessage | ConvertTo-Json -Depth 6 -Compress))
        while ($null -ne ($taskLine = $taskReader.ReadLine())) {
            $taskReply = $taskLine | ConvertFrom-Json
            if ($taskReply.error) { throw "QMP error: $taskLine" }
            if ($taskReply.id -eq $Command) { return $taskReply.return }
        }
        throw "QMP ended before replying to $Command."
    }
    Invoke-TaskQmp 'qmp_capabilities' | Out-Null
    $taskUuid = Invoke-TaskQmp 'query-uuid'
    if ($taskUuid.UUID -ne $taskState.Uuid) { throw 'VM UUID does not match the recorded instance.' }
    $taskStatus = Invoke-TaskQmp 'query-status'
    $taskName = Invoke-TaskQmp 'query-name'
    if ($Quit) { Invoke-TaskQmp 'quit' | Out-Null }
    if ($ScreenPath) {
        if (-not [IO.Path]::IsPathRooted($ScreenPath) -or $ScreenPath.Contains('::')) {
            throw 'Screenshot requires an absolute native Windows path.'
        }
        Invoke-TaskQmp 'screendump' @{filename=$ScreenPath; format='png'} | Out-Null
    }
    [ordered]@{Name=$taskName.name;Uuid=$taskUuid.UUID;Status=$taskStatus.status;Running=$taskStatus.running;ScreenPath=$ScreenPath} | ConvertTo-Json -Compress
} finally {
    $taskClient.Dispose()
}
