param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$Run,
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$From,
    [ValidateRange(1,65535)][int]$MockPort = 18761,
    [switch]$SaveForResume,
    [switch]$Resume,
    [switch]$Quota,
    [switch]$QuotaResume
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-GameSmoke.ps1')
$root = (Get-Location).Path
$runtime = Join-Path $root "work/$Run"
$environment = @{ WENMING_FOUNDING_BUDGET_NANO_USD = '' }
$servers = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
if (($Resume -and ($From -or $SaveForResume)) -or ($SaveForResume -and !$From)) {
    throw 'Use -From with -SaveForResume, then -Resume with the same run name.'
}
if ($Quota -and (!$From -or $SaveForResume -or $Resume)) { throw 'Use -Quota with -From.' }
if ($QuotaResume -and ($From -or $SaveForResume -or $Resume -or $Quota)) { throw 'Use -QuotaResume with the same -Run after -Quota.' }
$resultFile = if ($Resume -or $QuotaResume) { 'founding-resume-result.json' } else { 'founding-result.json' }
function Read-FoundingCheckpoint($Saved) {
    $directory = Join-Path $runtime ($Saved.worldDirectory -replace '^app:/', '')
    $project = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $directory 'Project.xml'))
    $save = $project.Project.WestworldFoundingSave
    if (!$save.WestworldObservation) { throw 'The normal world save is missing the AI checkpoint.' }
    $save.WestworldObservation | ConvertFrom-Json
}

if ($Resume) {
    $saved = Get-Content -Raw -LiteralPath (Join-Path $runtime 'founding-result.json') | ConvertFrom-Json
    if ($saved.result -ne 'passed' -or !$saved.worldDirectory) { throw 'Save the world before resuming.' }
    $before = Read-FoundingCheckpoint $saved
}
if ($QuotaResume) {
    $saved = Get-Content -Raw -LiteralPath (Join-Path $runtime 'founding-result.json') | ConvertFrom-Json
    if ($saved.result -ne 'passed' -or !$saved.worldDirectory) { throw 'Complete the quota pause scenario before reopening.' }
}
try {
    $result = Invoke-GameSmoke -Runtime $runtime -ResultFile $resultFile -Environment $environment -TimeoutSeconds 180 -WaitForExit:($SaveForResume -or $Resume -or $Quota -or $QuotaResume) -Prepare {
        if ($Resume -or $QuotaResume) {
            New-Item -ItemType File -Path (Join-Path $runtime 'founding-resume') -Force | Out-Null
        } else {
            if (Test-Path -LiteralPath $runtime) { throw 'Use a new verification run name.' }
            New-Item -ItemType Directory -Path $runtime | Out-Null
            Copy-Item 'game/Survivalcraft.Windows/bin/Release/net10.0-windows/win-x64/*' -Destination $runtime -Recurse
            $base = [IO.File]::ReadAllText((Join-Path $root 'game/Survivalcraft/init.js'))
            $fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'founding-world-smoke.js'))
            [IO.File]::WriteAllText((Join-Path $runtime 'init.js'), $base + [Environment]::NewLine + $fixture)
            if ($From) {
                $source = Join-Path $root "work/$From/founding-result.json"
                $initial = Get-Content -Raw -LiteralPath $source | ConvertFrom-Json
                if ($initial.result -ne 'passed') { throw 'Generate and verify the initial archive first.' }
                Copy-Item -LiteralPath $source -Destination (Join-Path $runtime 'founding-baseline.json')
                Copy-Item -LiteralPath $initial.archive -Destination (Join-Path $runtime 'initial.scworld')
            }
            if ($SaveForResume) { New-Item -ItemType File -Path (Join-Path $runtime 'founding-save') | Out-Null }
            if ($Quota) { New-Item -ItemType File -Path (Join-Path $runtime 'founding-quota') | Out-Null }
        }
        if ($From -or $Resume -or $QuotaResume) {
            $mode = if ($SaveForResume -or $Resume) { 'founding-resume' } else { 'founding' }
            if ($Quota -or $QuotaResume) { $mode = 'founding-quota' }
            $environment.WENMING_MODEL_ENDPOINT = "http://127.0.0.1:$MockPort/global/$mode/responses"
            $environment.WENMING_MODEL = 'gpt-6-luna'
            $environment.WENMING_MODEL_PROTOCOL = 'responses'
            $environment.WENMING_MODEL_API_KEY = 'local-test-key'
            $environment.WENMING_FOUNDING_BUDGET_NANO_USD = '500000000'
            $environment.WENMING_FOUNDING_INPUT_NANO_USD = '10'
            $environment.WENMING_FOUNDING_OUTPUT_NANO_USD = '20'
            if ($Quota -or $QuotaResume) {
                $environment.WENMING_FOUNDING_BUDGET_NANO_USD = '300000'
                $environment.WENMING_FOUNDING_STORAGE_LIMIT_BYTES = if ($QuotaResume) { '' } else { '60000000' }
            }
            $mock = Join-Path $PSScriptRoot 'actor-model-mock.py'
            $servers.Add((Start-Process -FilePath (Get-Command python).Source -ArgumentList ('"{0}" --port {1}' -f $mock, $MockPort) `
                -WorkingDirectory $runtime -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $runtime 'mock.log')))
            Start-Sleep -Milliseconds 500
            if ($servers[0].HasExited) { throw 'The fixed-response service could not start. See mock.log.' }
        }
    }
    $report = $result | ConvertFrom-Json
    if ($SaveForResume) {
        $checkpoint = Read-FoundingCheckpoint $report
        $running = @($checkpoint.actors | Where-Object { $_.task.view.status -eq 'running' -and $_.task.view.completedSteps -eq 1 })
        $speaking = @($running | Where-Object { $_.task.continuation.'$type' -eq 'speech' -and $_.task.continuation.delivered -gt 0 })
        $turning = @($running | Where-Object { $_.task.continuation.'$type' -eq 'motion' })
        if ($checkpoint.time -ne $report.time -or $running.Count -ne 50 -or $speaking.Count -ne 1 -or $turning.Count -ne 1) {
            throw 'The normal save must contain the exact clock, 50 in-progress plans, the speech prefix, and the resolved turn target.'
        }
        $report.checks += 'the normal save contains 50 original task IDs with one settled step, remaining action time and the delivered speech prefix'
    }
    if ($Resume) {
        $after = Read-FoundingCheckpoint $saved
        $prior = @{}
        foreach ($actor in $before.actors) { $prior[$actor.id] = $actor.task.view.taskId }
        $continued = @($after.actors | Where-Object {
            $_.cognition.lastTask.taskId -eq $prior[$_.id] -and $_.cognition.lastTask.status -eq 'succeeded' -and $_.cognition.lastTask.completedSteps -eq 3
        })
        $speech = ($before.actors | Where-Object { $_.task.continuation.'$type' -eq 'speech' }).task.continuation
        $heard = @($after.actors.cognition.inbox | Where-Object {
            $_.value.speechId -eq $speech.id -and $_.value.speechComplete -and $_.value.text -ceq $speech.text
        })
        $remaining = ($before.actors.task.continuation.seconds | Measure-Object -Maximum).Maximum
        if ($continued.Count -ne 50 -or $heard.Count -eq 0 -or $after.time - $before.time -gt $remaining + .75) {
            throw 'Expected all original plans, the complete speech without replay, and only the remaining wait.'
        }
        $turning = $before.actors | Where-Object { $_.task.continuation.'$type' -eq 'motion' }
        $target = $turning.task.continuation
        $turned = $report.actors | Where-Object id -eq $turning.id
        $dx = $target.x - $turned.position[0]
        $dz = $target.z - $turned.position[2]
        $facing = ($dx * $turned.forward[0] + $dz * $turned.forward[2]) / [Math]::Sqrt($dx * $dx + $dz * $dz)
        if ($facing -lt .998) { throw 'The resumed turn did not face its original resolved target.' }
        $report.checks += 'all 50 original tasks completed, with only the remaining wait, speech and turn to the original target'
    }
    if ($SaveForResume -or $Resume) {
        $result = $report | ConvertTo-Json -Depth 12
        [IO.File]::WriteAllText((Join-Path $runtime $resultFile), $result)
    }
    $result
} finally {
    foreach ($server in $servers) {
        if (!$server.HasExited) { $server.Kill() }
        $server.Dispose()
    }
}
