param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$Run,
    [ValidateSet('scale','scale-action','observe-ahead','observe-ledger-interrupt','observe-ledger-recover','observe-ledger-write-fault',
        'observe-world-interrupt','observe-world-resume','live','live-session','live-forage','live-observe')][string]$Mode = 'observe-ahead',
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$ResumeFrom,
    [ValidateRange(15,120)][int]$ObservationFrameRate,
    [ValidateRange(1,65535)][int]$PanelPort,
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$ObservationBaselineRun
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-GameSmoke.ps1')
$root = (Get-Location).Path
$runtime = Join-Path $root "work/$Run"
$environment = @{
    WENMING_MODEL_API_KEY = 'local-test-key'
    WENMING_MODEL = 'gpt-6-luna'
    WENMING_MODEL_ENDPOINT = 'http://127.0.0.1:18759/global'
}
if ($PanelPort) { $environment.WENMING_PANEL_PORT = [string]$PanelPort }
$timeout = switch ($Mode) {
    'live-forage' { 420 }
    'live-observe' { 160 }
    'live-session' { 240 }
    'live' { 160 }
    default { 70 }
}
Invoke-GameSmoke -Runtime $runtime -ResultFile 'global-cognition-result.json' -Environment $environment -TimeoutSeconds $timeout -Prepare {
    if ($Mode -in 'observe-ledger-recover','observe-world-resume' -and !$ResumeFrom) { throw 'Specify the interrupted runtime directory.' }
    if (($ObservationFrameRate -or $ObservationBaselineRun) -and $Mode -notin 'observe-ahead','live-observe') {
        throw 'Frame-rate control and baseline builds apply only to observation scenarios.'
    }
    if ($ObservationBaselineRun -eq $Run) { throw 'Use a different run name for the comparison.' }
    if ($ObservationFrameRate) { $environment.WENMING_OBSERVATION_FRAME_RATE = [string]$ObservationFrameRate }
    if ($Mode -in 'live','live-session','live-forage','live-observe') {
        if (Test-Path -LiteralPath $runtime) { throw 'Use a new run name for a paid test.' }
        $environment.WENMING_MODEL_ENDPOINT = 'https://api.openai.com/v1/responses'
        $key = $env:WENMING_MODEL_API_KEY
        $localConfig = Join-Path $root '.env.local'
        if (!$key -and (Test-Path -LiteralPath $localConfig)) {
            $keyLine = Get-Content -LiteralPath $localConfig |
                Where-Object { $_ -match '^\s*WENMING_MODEL_API_KEY\s*=' } | Select-Object -First 1
            if ($keyLine) { $key = ($keyLine -split '=',2)[1].Trim().Trim('"').Trim("'") }
        }
        if (!$key) { throw 'Set WENMING_MODEL_API_KEY in the process environment or .env.local.' }
        $probeRequest = @{
            Uri = 'https://api.openai.com/v1/models/gpt-6-luna'
            Headers = @{ Authorization = "Bearer $key" }
            TimeoutSec = 15
            SkipHttpErrorCheck = $true
        }
        $probe = Invoke-WebRequest @probeRequest
        if ($probe.StatusCode -ne 200) { throw "OpenAI model access check returned HTTP $($probe.StatusCode)." }
        $environment.WENMING_MODEL_API_KEY = $key
    }
    New-Item -ItemType Directory -Path $runtime | Out-Null
    Copy-Item 'game/Survivalcraft.Windows/bin/Release/net10.0-windows/win-x64/*' -Destination $runtime -Recurse
    if ($ObservationBaselineRun) {
        Copy-Item -LiteralPath (Join-Path $root "work/$ObservationBaselineRun/Survivalcraft.dll") -Destination $runtime -Force
    }
    $base = [IO.File]::ReadAllText((Join-Path $root 'game/Survivalcraft/init.js'))
    $fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'global-cognition-scale.js'))
    [IO.File]::WriteAllText((Join-Path $runtime 'init.js'),$base+[Environment]::NewLine+$fixture)
    [IO.File]::WriteAllText((Join-Path $runtime 'global-mode'),$Mode)
    if ($Mode -in 'observe-ledger-recover','observe-world-resume') {
        Copy-Item -LiteralPath (Join-Path $root "work/$ResumeFrom/cognition-run") -Destination $runtime -Recurse
    }
    if ($Mode -eq 'observe-world-resume') {
        Copy-Item -LiteralPath (Join-Path $root "work/$ResumeFrom/global-cognition-result.json") -Destination (Join-Path $runtime 'interrupted-result.json')
    }
    if ($Mode -eq 'observe-ledger-write-fault') {
        New-Item -ItemType Directory -Path (Join-Path $runtime 'cognition-run/ledger.json.tmp') -Force | Out-Null
    }
}
