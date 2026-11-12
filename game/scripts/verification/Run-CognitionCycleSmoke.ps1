param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$Run,
    [switch]$Live,
    [switch]$Reload,
    [switch]$Scheduler,
    [switch]$Adaptive,
    [switch]$Guard,
    [switch]$Perturb,
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$From,
    [ValidateRange(1,30)][int]$Minutes = 8,
    [ValidateRange(1,5)][int]$Population = 3,
    [ValidateRange(1,65535)][int]$PanelPort = 18762
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-GameSmoke.ps1')
$root = (Get-Location).Path
$runtime = Join-Path $root "work/$Run"
$environment = @{ WENMING_PANEL_PORT = [string]$PanelPort }
$server = $null
$Scheduler = $Scheduler -or $Guard
if ($Scheduler -and $Live) { throw 'Use a fixed scheduler check or a live check, separately.' }
if ($Adaptive -and ($Scheduler -or $Live)) { throw 'Use mechanics or live validation separately.' }
if ($Perturb -and !$Live) { throw 'Use -Perturb with an independent live run.' }
if ($Scheduler) {
    $environment.WENMING_MODEL_API_KEY = 'local-test-placeholder'
    $environment.WENMING_MODEL_ENDPOINT = 'http://127.0.0.1:18763/responses'
    $environment.WENMING_MODEL_PROTOCOL = 'responses'
    $environment.WENMING_FOUNDING_BUDGET_NANO_USD = '1000000000'
    $environment.WENMING_FOUNDING_INPUT_NANO_USD = '100'
    $environment.WENMING_FOUNDING_OUTPUT_NANO_USD = '500'
}
if ($Live) {
    $key = $env:WENMING_MODEL_API_KEY
    if (!$key -and (Test-Path -LiteralPath '.env.local')) {
        $setting = Get-Content -LiteralPath '.env.local' | Where-Object { $_ -match '^\s*WENMING_MODEL_API_KEY\s*=' } | Select-Object -First 1
        if ($setting) { $key = ($setting -split '=',2)[1].Trim().Trim('"').Trim("'") }
    }
    if (!$key) { throw 'Set WENMING_MODEL_API_KEY locally.' }
    $environment.WENMING_MODEL_API_KEY = $key
    $environment.WENMING_MODEL = 'gpt-6-luna'
    $environment.WENMING_MODEL_PROTOCOL = 'responses'
    $environment.WENMING_MODEL_ENDPOINT = 'https://api.openai.com/v1/responses'
    $environment.WENMING_FOUNDING_BUDGET_NANO_USD = '3000000000'
    $environment.WENMING_FOUNDING_INPUT_NANO_USD = '100'
    $environment.WENMING_FOUNDING_OUTPUT_NANO_USD = '500'
}
$fixtureName = if ($Guard) { 'cognition-guard-resume.js' } elseif ($Live) { 'cognition-cycle-live.js' } elseif ($Adaptive) { 'adaptive-control-mechanics.js' } elseif ($Scheduler) { 'cognition-cycle-scheduler.js' } else { 'cognition-cycle-mechanics.js' }
$firstResult = if ($Guard) { 'cognition-guard-checkpoint1.json' } elseif ($Live -and $Reload) { 'cognition-cycle-checkpoint.json' } else { 'cognition-cycle-result.json' }
try {
Invoke-GameSmoke -Runtime $runtime -ResultFile $firstResult -Environment $environment -TimeoutSeconds ($Minutes * 60 + 160) -WaitForExit -Prepare {
    if (Test-Path -LiteralPath $runtime) { throw 'Use a new verification run name.' }
    New-Item -ItemType Directory -Path $runtime | Out-Null
    $binarySource = if ($From) { Join-Path $root "work/$From" } else { Join-Path $root 'game/Survivalcraft.Windows/bin/Release/net10.0-windows/win-x64' }
    Get-ChildItem -LiteralPath $binarySource | Where-Object {
        $_.Name -notin @('doc','Bugs') -and $_.Name -notlike 'cognition-cycle-*' -and $_.Extension -ne '.log'
    } | Copy-Item -Destination $runtime -Recurse
    $base = [IO.File]::ReadAllText((Join-Path $root 'game/Survivalcraft/init.js'))
    $fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $fixtureName))
    [IO.File]::WriteAllText((Join-Path $runtime 'init.js'), $base + [Environment]::NewLine + $fixture)
    @{ population=$Population; seconds=$Minutes*60; reload=[bool]$Reload; perturb=[bool]$Perturb } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime 'cognition-cycle-config.json') -Encoding utf8
    @{ commit=(git rev-parse HEAD); assemblySha256=(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $runtime 'Survivalcraft.dll')).Hash;
        binaryFrom=$From; background=$true; mute=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime 'cognition-cycle-build.json') -Encoding utf8
    if ($Scheduler) {
        $mock = Join-Path $PSScriptRoot 'cognition-cycle-mock.py'
        $log = Join-Path $runtime 'cognition-cycle-mock.jsonl'
        $script:server = Start-Process -FilePath (Get-Command python).Source -ArgumentList ('"{0}" --port 18763 --log "{1}" {2}' -f $mock,$log, $(if ($Guard) { '--guard' } else { '' })) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $runtime 'mock.log')
    }
}
if ($Guard) {
    Invoke-GameSmoke -Runtime $runtime -ResultFile 'cognition-guard-checkpoint2.json' -Environment $environment -TimeoutSeconds 100 -WaitForExit -Prepare {}
    Invoke-GameSmoke -Runtime $runtime -ResultFile 'cognition-guard-checkpoint3.json' -Environment $environment -TimeoutSeconds 100 -WaitForExit -Prepare {}
    Invoke-GameSmoke -Runtime $runtime -ResultFile 'cognition-cycle-result.json' -Environment $environment -TimeoutSeconds 100 -WaitForExit -Prepare {}
} elseif ($Live -and $Reload) {
    Invoke-GameSmoke -Runtime $runtime -ResultFile 'cognition-cycle-result.json' -Environment $environment -TimeoutSeconds ($Minutes * 60 + 160) -WaitForExit -Prepare {}
}
} catch {
    $failureFile = Join-Path $runtime 'cognition-cycle-result.json'
    if (Test-Path -LiteralPath $failureFile) {
        $failure = Get-Content -LiteralPath $failureFile -Raw | ConvertFrom-Json
        if ($failure.result -eq 'failed') { throw "Cognition run failed: $($failure.error). Evidence: $failureFile" }
    }
    throw
} finally {
    if ($server) { if (!$server.HasExited) { $server.Kill() }; $server.Dispose() }
}
