param([string]$Dotnet = "dotnet", [switch]$BuildGame)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $projects = @(
        "archive/survivalcraft/snapshots/native-checkpoint/recovery-vision-core/Tests.csproj",
        "archive/survivalcraft/snapshots/cross-tick-wip/recovery-vision-fixture-next/Tests.csproj",
        "experiments/scapi-fixture-sequencing/Tests.csproj"
    )
    foreach ($project in $projects) {
        & $Dotnet run --project $project --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "Regression failed: $project ($LASTEXITCODE)" }
    }
    if ($BuildGame) {
        & $Dotnet build game/Survivalcraft.Windows/Survivalcraft.Windows.csproj --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "Windows build failed ($LASTEXITCODE)" }
    }
} finally { Pop-Location }
