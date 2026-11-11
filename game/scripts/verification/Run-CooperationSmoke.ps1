param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]*$')][string]$Run,
    [switch]$Gather,
    [switch]$Mechanics,
    [switch]$ReloadDuringCooperation,
    [ValidateRange(1,65535)][int]$PanelPort = 18762
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-GameSmoke.ps1')
$root = (Get-Location).Path
$runtime = Join-Path $root "work/$Run"
$key = $env:WENMING_MODEL_API_KEY
if (!$key -and (Test-Path -LiteralPath '.env.local')) {
    $setting = Get-Content -LiteralPath '.env.local' | Where-Object { $_ -match '^\s*WENMING_MODEL_API_KEY\s*=' } | Select-Object -First 1
    if ($setting) { $key = ($setting -split '=',2)[1].Trim().Trim('"').Trim("'") }
}
if (!$key -and !$Mechanics) { throw 'Set WENMING_MODEL_API_KEY locally.' }
$environment = @{
    WENMING_MODEL = 'gpt-6-luna'
    WENMING_MODEL_API_KEY = $key
    WENMING_PANEL_PORT = [string]$PanelPort
}
$timeout = if ($Gather) { 380 } else { 270 }
$firstResult = if ($ReloadDuringCooperation) { 'cooperation-checkpoint.json' } else { 'cooperation-result.json' }
Invoke-GameSmoke -Runtime $runtime -ResultFile $firstResult -Environment $environment -TimeoutSeconds $timeout -WaitForExit -Prepare {
    if (Test-Path -LiteralPath $runtime) { throw 'Use a new verification run name.' }
    New-Item -ItemType Directory -Path $runtime | Out-Null
    Copy-Item 'game/Survivalcraft.Windows/bin/Release/net10.0-windows/win-x64/*' -Destination $runtime -Recurse
    $base = [IO.File]::ReadAllText((Join-Path $root 'game/Survivalcraft/init.js'))
    $fixtureName = if ($Mechanics) { 'cooperation-mechanics.js' } else { 'cooperation-smoke.js' }
    $fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $fixtureName))
    [IO.File]::WriteAllText((Join-Path $runtime 'init.js'), $base + [Environment]::NewLine + $fixture)
    if ($Gather) { New-Item -ItemType File -Path (Join-Path $runtime 'cooperation-gather') | Out-Null }
    if ($ReloadDuringCooperation) { New-Item -ItemType File -Path (Join-Path $runtime 'cooperation-reload') | Out-Null }
}
if ($ReloadDuringCooperation) {
    Invoke-GameSmoke -Runtime $runtime -ResultFile 'cooperation-result.json' -Environment $environment -TimeoutSeconds $timeout -WaitForExit -Prepare {}
}
