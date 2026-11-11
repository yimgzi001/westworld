param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$Run,
    [switch]$Live,
    [switch]$ReloadDuringDig,
    [ValidateRange(1,65535)][int]$MockPort = 18761
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-GameSmoke.ps1')
$root = (Get-Location).Path
$runtime = Join-Path $root "work/$Run"
$environment = @{
    WENMING_MODEL = 'gpt-6-luna'
    WENMING_MODEL_PROTOCOL = 'responses'
    WENMING_MODEL_ENDPOINT = "http://127.0.0.1:$MockPort/global/solo-production/responses"
    WENMING_MODEL_API_KEY = 'local-test-key'
    WENMING_FOUNDING_BUDGET_NANO_USD = '500000000'
    WENMING_FOUNDING_INPUT_NANO_USD = '10'
    WENMING_FOUNDING_OUTPUT_NANO_USD = '20'
}
$server = $null
try {
    if ($Live) {
        $key = $env:WENMING_MODEL_API_KEY
        if (!$key -and (Test-Path -LiteralPath '.env.local')) {
            $setting = Get-Content -LiteralPath '.env.local' | Where-Object { $_ -match '^\s*WENMING_MODEL_API_KEY\s*=' } | Select-Object -First 1
            if ($setting) { $key = ($setting -split '=',2)[1].Trim().Trim('"').Trim("'") }
        }
        if (!$key) { throw 'Set WENMING_MODEL_API_KEY locally.' }
        $environment.WENMING_MODEL_ENDPOINT = 'https://api.openai.com/v1/responses'
        $environment.WENMING_MODEL_API_KEY = $key
        $environment.WENMING_FOUNDING_BUDGET_NANO_USD = '30000000'
        $environment.WENMING_FOUNDING_INPUT_NANO_USD = '100'
        $environment.WENMING_FOUNDING_OUTPUT_NANO_USD = '500'
    }
    Invoke-GameSmoke -Runtime $runtime -ResultFile 'solo-production-result.json' -Environment $environment -TimeoutSeconds 250 -WaitForExit -Prepare {
        if (Test-Path -LiteralPath $runtime) { throw 'Use a new verification run name.' }
        New-Item -ItemType Directory -Path $runtime | Out-Null
        Copy-Item 'game/Survivalcraft.Windows/bin/Release/net10.0-windows/win-x64/*' -Destination $runtime -Recurse
        $base = [IO.File]::ReadAllText((Join-Path $root 'game/Survivalcraft/init.js'))
        $fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'solo-production-smoke.js'))
        [IO.File]::WriteAllText((Join-Path $runtime 'init.js'), $base + [Environment]::NewLine + $fixture)
        if ($ReloadDuringDig) { New-Item -ItemType File -Path (Join-Path $runtime 'solo-reload-during-dig') | Out-Null }
        if (!$Live) {
            $mock = Join-Path $PSScriptRoot 'actor-model-mock.py'
            $script:server = Start-Process -FilePath (Get-Command python).Source -ArgumentList ('"{0}" --port {1}' -f $mock, $MockPort) -WorkingDirectory $runtime -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $runtime 'mock.log')
            Start-Sleep -Milliseconds 500
            if ($server.HasExited) { throw 'The fixed-response service could not start.' }
        }
    }
} finally {
    if ($server) {
        if (!$server.HasExited) { $server.Kill() }
        $server.Dispose()
    }
}
