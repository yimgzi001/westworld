param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$Run,
    [string]$Protocol = 'responses',
    [string]$Endpoint = '',
    [switch]$Live,
    [switch]$RateLimit,
    [switch]$InvalidDecision
)
$ErrorActionPreference = 'Stop'
if ($Live -and ($Endpoint -or $Protocol -ne 'responses' -or $RateLimit -or $InvalidDecision)) {
    throw 'Live smoke uses only the official Responses endpoint.'
}
if (!$Live -and !$Endpoint) { throw 'Specify a local mock endpoint.' }
. (Join-Path $PSScriptRoot 'Invoke-GameSmoke.ps1')
$root = (Get-Location).Path
$runtime = Join-Path $root "work/$Run"
$environment = @{
    WENMING_MODEL = 'gpt-6-luna'
    WENMING_MODEL_PROTOCOL = $Protocol
    WENMING_MODEL_ENDPOINT = $Endpoint
}
Invoke-GameSmoke -Runtime $runtime -ResultFile 'actor-model-result.json' -Environment $environment -Prepare {
    New-Item -ItemType Directory -Path $runtime | Out-Null
    Copy-Item -Path 'game/Survivalcraft.Windows/bin/Release/net10.0-windows/win-x64/*' -Destination $runtime -Recurse
    $base = [IO.File]::ReadAllText((Join-Path $root 'game/Survivalcraft/init.js'))
    $fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'actor-model-smoke.js'))
    [IO.File]::WriteAllText((Join-Path $runtime 'init.js'),$base+[Environment]::NewLine+$fixture)
    if ($RateLimit) { New-Item -ItemType File -Path (Join-Path $runtime 'expect-rate-limit') | Out-Null }
    if ($InvalidDecision) { New-Item -ItemType File -Path (Join-Path $runtime 'expect-invalid-decision') | Out-Null }
    $environment.WENMING_MODEL_API_KEY = if ($Live) {
        $secure = Read-Host -Prompt 'OpenAI API key' -AsSecureString
        try { [System.Net.NetworkCredential]::new('', $secure).Password } finally { $secure.Dispose() }
    } else { 'local-test-key' }
}
