#!/usr/bin/env bash
# Pure protocol/profile tests only. No GUI, model, world, or network calls by tests.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-$ROOT/work/dotnet/dotnet}"
LOG_DIR="${LOG_DIR:-$ROOT/work/tool-contract-test-$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$LOG_DIR"
# NuGet restore must already be completed. Do not hide build failures with tail/pipes.
for suite in ToolCallLedger ToolDecision NativeToolOffers ProviderBounds HuntingTools ProductionTools AttackReadiness InferenceAudit; do
  project="$ROOT/tests/Westworld.$suite.Tests/$suite.Tests.csproj"
  DOTNET_CLI_USE_MSBUILD_SERVER=0 "$DOTNET" build "$project" -c Release --no-restore --disable-build-servers -m:1 >"$LOG_DIR/$suite.build.log" 2>&1 || {
    cat "$LOG_DIR/$suite.build.log"; exit 1;
  }
  assembly="$ROOT/tests/Westworld.$suite.Tests/bin/Release/net10.0/$suite.Tests.dll"
  if [[ "$suite" == ProductionTools ]]; then
    assembly="$ROOT/tests/Westworld.$suite.Tests/bin/Release/net10.0/Westworld.$suite.Tests.dll"
  fi
  "$DOTNET" "$assembly" | tee "$LOG_DIR/$suite.test.log"
done
printf 'All eight pure suites passed. Logs: %s\n' "$LOG_DIR"
