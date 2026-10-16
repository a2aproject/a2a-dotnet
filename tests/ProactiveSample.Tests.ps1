param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Get-FreePort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port }
    finally { $listener.Stop() }
}

function Start-SampleProcess([string[]]$Arguments) {
    $info = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $info.WorkingDirectory = $root
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($info)
    return @{
        Process = $process
        Errors = $process.StandardError.ReadToEndAsync()
    }
}

function Wait-CliLine($Cli, [string]$Pattern) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $pending = $Cli.Process.StandardOutput.ReadLineAsync()
    while ([DateTime]::UtcNow -lt $deadline) {
        if (!$pending.Wait(100)) { continue }
        $line = $pending.GetAwaiter().GetResult()
        if ($null -eq $line) {
            throw "CLI exited before '$Pattern': $($Cli.Errors.GetAwaiter().GetResult())"
        }
        Write-Host $line
        if ($line -match $Pattern) { return $line }
        $pending = $Cli.Process.StandardOutput.ReadLineAsync()
    }
    throw "CLI did not print '$Pattern' within 20 seconds."
}

function Invoke-Trigger([string]$TaskId, [string]$Message, [int]$ExpectedStatus) {
    $response = Invoke-WebRequest "$script:agentUrl/proactive/tasks/$TaskId/continue" `
        -Method Post -ContentType 'application/json' `
        -Body (@{ message = $Message } | ConvertTo-Json -Compress) `
        -NoProxy -SkipHttpErrorCheck -TimeoutSec 15
    if ([int]$response.StatusCode -ne $ExpectedStatus) {
        throw "Trigger returned $($response.StatusCode), expected $ExpectedStatus`: $($response.Content)"
    }
    $content = $response.Content
    if ($content -is [byte[]]) { $content = [System.Text.Encoding]::UTF8.GetString($content) }
    return $content | ConvertFrom-Json
}

function Invoke-A2A([string]$Method, [hashtable]$Params) {
    return (Invoke-RestMethod "$script:agentUrl/proactive" -Method Post `
        -Headers @{ 'A2A-Version' = '1.0' } -ContentType 'application/json' -NoProxy -TimeoutSec 15 `
        -Body (@{ jsonrpc = '2.0'; id = 'smoke'; method = $Method; params = $Params } |
            ConvertTo-Json -Depth 20 -Compress))
}

Push-Location $root
$server = $null
$cli = $null
$storeDirectory = $null
try {
    dotnet build samples\AgentServer\AgentServer.csproj --no-restore -c $Configuration --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'AgentServer build failed.' }
    dotnet build samples\A2ACli\A2ACli.csproj --no-restore -c $Configuration -f net10.0 --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'A2ACli build failed.' }

    $script:agentUrl = "http://127.0.0.1:$(Get-FreePort)"
    $receiverUrl = "http://127.0.0.1:$(Get-FreePort)"
    $storeDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "a2a-proactive-smoke-$([Guid]::NewGuid().ToString('N'))"
    $server = Start-SampleProcess @(
        "samples\AgentServer\bin\$Configuration\net10.0\AgentServer.dll",
        '--agent', 'proactive', '--urls', $agentUrl,
        '--push-callback', "$receiverUrl/notify",
        '--store', 'file', '--data-dir', $storeDirectory
    )
    $server.Output = $server.Process.StandardOutput.ReadToEndAsync()
    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($server.Process.HasExited) {
            throw "AgentServer exited: $($server.Output.GetAwaiter().GetResult()) $($server.Errors.GetAwaiter().GetResult())"
        }
        try {
            $null = Invoke-RestMethod "$agentUrl/health" -NoProxy -TimeoutSec 1
            $ready = $true
            break
        }
        catch [System.Net.Http.HttpRequestException], [System.OperationCanceledException] {
            Start-Sleep -Milliseconds 100
        }
    }
    if (!$ready) { throw 'AgentServer did not become ready.' }

    $cli = Start-SampleProcess @(
        "samples\A2ACli\bin\$Configuration\net10.0\A2A.Cli.dll",
        '--agent', $agentUrl, '--use-push-notifications', '--return-immediately',
        '--push-notification-receiver', $receiverUrl
    )
    $cli.Process.StandardInput.WriteLine('Start a task for external updates')
    $cli.Process.StandardInput.WriteLine('')
    $cli.Process.StandardInput.Flush()
    $task = Wait-CliLine $cli '^\{"id":' | ConvertFrom-Json
    if ($task.status.state -ne 'TASK_STATE_WORKING') { throw 'Initial task must remain Working.' }

    $message = '  External job finished: exact text with "quotes" and a backslash \.  '
    $result = Invoke-Trigger $task.id $message 200
    if ($result.taskId -ne $task.id -or $result.contextId -ne $task.contextId -or $result.persistedEventCount -ne 1 -or
        $result.acceptedPushNotificationCount -ne 1) {
        throw 'Trigger did not report one persisted and accepted event for the original task.'
    }
    $line = Wait-CliLine $cli 'Push notification => \{"message":'
    $notification = ($line -replace '^.*Push notification => ', '') | ConvertFrom-Json
    if ($notification.message.taskId -ne $task.id -or
        $notification.message.contextId -ne $task.contextId -or
        $notification.message.role -ne 'ROLE_AGENT' -or
        $notification.message.parts[0].text -cne $message) {
        throw 'A2ACli did not receive the exact external message for the original task/context.'
    }

    $null = Invoke-Trigger $task.id 'Another external update' 200
    $line = Wait-CliLine $cli 'Push notification => \{"message":'
    $secondNotification = ($line -replace '^.*Push notification => ', '') | ConvertFrom-Json
    if ($secondNotification.message.taskId -ne $task.id -or
        $secondNotification.message.contextId -ne $task.contextId -or
        $secondNotification.message.parts[0].text -cne 'Another external update') {
        throw 'The repeated trigger must deliver its new text on the same task/context.'
    }
    $persisted = Invoke-A2A 'GetTask' @{ id = $task.id }
    if ($persisted.error -or $persisted.result.status.state -ne 'TASK_STATE_WORKING' -or
        $persisted.result.history.Count -ne 3 -or
        $persisted.result.history[0].role -ne 'ROLE_USER' -or
        $persisted.result.history[0].parts[0].text -cne 'Start a task for external updates' -or
        $persisted.result.history[1].parts[0].text -cne $message -or
        $persisted.result.history[2].parts[0].text -cne 'Another external update') {
        throw 'Task history must contain the original input and both proactive messages, without synthetic input.'
    }

    # Seed extra stored state while the task is idle to exercise full-snapshot preservation.
    $snapshot = $persisted.result
    $snapshot | Add-Member -NotePropertyName artifacts -NotePropertyValue @(
        @{ artifactId = 'preserved-artifact'; parts = @(@{ text = 'Existing artifact content' }) }
    ) -Force
    $snapshot | Add-Member -NotePropertyName metadata -NotePropertyValue @{ source = 'preserved-metadata' } -Force
    $snapshot | ConvertTo-Json -Depth 20 -Compress |
        Set-Content -LiteralPath (Join-Path $storeDirectory "tasks\$($task.id).json") -Encoding utf8
    $followUp = Invoke-A2A 'SendMessage' @{
        message = @{
            messageId = 'inbound-follow-up'; taskId = $task.id; contextId = $task.contextId
            role = 'ROLE_USER'; parts = @(@{ text = 'Keep the previous updates' })
        }
        configuration = @{ returnImmediately = $true }
    }
    if ($followUp.error -or $followUp.result.task.id -ne $task.id) {
        throw "Could not follow up on the existing task: $($followUp | ConvertTo-Json -Depth 20 -Compress)"
    }
    $null = Invoke-Trigger $task.id 'After inbound follow-up' 200
    $null = Wait-CliLine $cli 'Push notification => \{"message":.*"text":"After inbound follow-up"'
    $afterFollowUp = (Invoke-A2A 'GetTask' @{ id = $task.id }).result
    if ($afterFollowUp.contextId -ne $task.contextId -or $afterFollowUp.status.state -ne 'TASK_STATE_WORKING' -or
        $afterFollowUp.history.Count -ne 5 -or
        $afterFollowUp.history[0].parts[0].text -cne 'Start a task for external updates' -or
        $afterFollowUp.history[1].parts[0].text -cne $message -or
        $afterFollowUp.history[2].parts[0].text -cne 'Another external update' -or
        $afterFollowUp.history[3].messageId -ne 'inbound-follow-up' -or
        $afterFollowUp.history[4].parts[0].text -cne 'After inbound follow-up' -or
        $afterFollowUp.artifacts.Count -ne 1 -or
        $afterFollowUp.artifacts[0].artifactId -ne 'preserved-artifact' -or
        $afterFollowUp.artifacts[0].parts[0].text -cne 'Existing artifact content' -or
        $afterFollowUp.metadata.source -cne 'preserved-metadata') {
        throw 'An inbound follow-up must preserve all history, artifacts, metadata, and task/context identity.'
    }

    $null = Invoke-Trigger $task.id ' ' 400
    $missing = Invoke-Trigger 'missing' 'Update' 404
    if ($missing.failureKind -ne 'TaskNotFound') {
        throw "Missing task must have a distinct error: $($missing | ConvertTo-Json -Depth 10 -Compress)"
    }
    $pushless = Invoke-A2A 'SendMessage' @{
        message = @{ messageId = 'without-push'; role = 'ROLE_USER'; parts = @(@{ text = 'No push' }) }
        configuration = @{ returnImmediately = $true }
    }
    if ($pushless.error) { throw "Could not create the pushless task: $($pushless.error.message)" }
    $unregistered = Invoke-Trigger $pushless.result.task.id 'Update' 409
    if ($unregistered.failureKind -ne 'MissingPushRegistration') { throw 'Missing registration must be reported.' }
    foreach ($unapprovedCallback in @("$receiverUrl/other", "$receiverUrl/notify?unexpected=1",
        $receiverUrl.Replace('127.0.0.1', '127.0.0.2') + '/notify')) {
        $rejected = Invoke-A2A 'SendMessage' @{
            message = @{ messageId = 'unapproved-callback'; role = 'ROLE_USER'; parts = @(@{ text = 'Reject this callback' }) }
            configuration = @{
                returnImmediately = $true
                taskPushNotificationConfig = @{ url = $unapprovedCallback }
            }
        }
        if ($rejected.error.code -ne -32602) {
            throw "An unapproved callback must be rejected: $($rejected | ConvertTo-Json -Depth 10 -Compress)"
        }
    }
    $canceled = Invoke-A2A 'CancelTask' @{ id = $task.id }
    if ($canceled.error) { throw "Could not cancel the task: $($canceled.error.message)" }
    $terminal = Invoke-Trigger $task.id 'Too late' 409
    if ($terminal.failureKind -ne 'NonContinuableTask') { throw 'Terminal task must be rejected.' }

    $cli.Process.StandardInput.WriteLine(':q')
    $cli.Process.StandardInput.Flush()
    if (!$cli.Process.WaitForExit(10000) -or $cli.Process.ExitCode -ne 0) {
        throw 'A2ACli did not exit cleanly.'
    }
    Write-Host 'Proactive sample passed: real A2ACli push receipt, repeated triggers, inbound follow-up state, and HTTP errors.'
}
finally {
    foreach ($child in @($cli, $server)) {
        if ($null -ne $child) {
            if (!$child.Process.HasExited) {
                Stop-Process -Id $child.Process.Id
                $child.Process.WaitForExit()
            }
            $child.Process.Dispose()
        }
    }
    if ($null -ne $storeDirectory -and (Test-Path -LiteralPath $storeDirectory)) {
        Remove-Item -LiteralPath $storeDirectory -Recurse
    }
    Pop-Location
}
