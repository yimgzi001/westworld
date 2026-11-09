param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$Run,
    [ValidateSet('suite','scale','scale-action','live','live-session','live-forage','live-observe','parallel','serial','budget','rate','interrupt','resume','ambiguous','unknown')][string]$Mode = 'suite',
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$ResumeFrom
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
$timeout = switch ($Mode) {
    'live-forage' { 420 }
    'live-observe' { 160 }
    'live-session' { 240 }
    'live' { 160 }
    default { 70 }
}
Invoke-GameSmoke -Runtime $runtime -ResultFile 'global-cognition-result.json' -Environment $environment -TimeoutSeconds $timeout -Prepare {
    if ($Mode -in 'resume','unknown' -and !$ResumeFrom) { throw 'Specify the interrupted runtime directory.' }
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
    $base = [IO.File]::ReadAllText((Join-Path $root 'game/Survivalcraft/init.js'))
    $fixtureName = if ($Mode -in 'scale','scale-action','live','live-session','live-forage','live-observe') { 'global-cognition-scale.js' } else { 'global-cognition-smoke.js' }
    $fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $fixtureName))
    [IO.File]::WriteAllText((Join-Path $runtime 'init.js'),$base+[Environment]::NewLine+$fixture)
    [IO.File]::WriteAllText((Join-Path $runtime 'global-mode'),$Mode)
    if ($Mode -in 'resume','unknown') {
        Copy-Item -LiteralPath (Join-Path $root "work/$ResumeFrom/cognition-run") -Destination $runtime -Recurse
    }
}
