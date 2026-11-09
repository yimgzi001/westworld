#!/usr/bin/env bash
# Run from a normal authorized desktop session with OpenGL. Never use user worlds.
set -u
repo="$(cd "$(dirname "$0")/../.." && pwd)"
dotnet="${DOTNET:-dotnet}"
if [[ "$dotnet" == */* ]]; then dotnet="$(realpath "$dotnet")"; else dotnet="$(command -v "$dotnet")"; fi
stamp="$(date -u +%Y%m%dT%H%M%S)-$$"
build="$repo/work/search-suite-$stamp-build"
"$dotnet" build "$repo/experiments/scapi-main-integration/IntegrationGate.csproj" -c Release -m:1 -o "$build" || exit 2
for entry in 'a natural actors-cloud-20261109' 'b natural westworld-search-02' 'c natural westworld-search-03' 'd natural westworld-inland-01' 'e natural westworld-inland-02' 'f natural westworld-inland-03' 'wall blocked westworld-blocked-01' 'obstacle obstacle westworld-obstacle-01' 'fixture fixture actors-cloud-20261109' 'terraced terraced actors-cloud-20261109'; do
 read -r name mode seed <<< "$entry"
 target="$repo/work/search-suite-$stamp-$name"
 mkdir "$target" || exit 2
 # Copy only runtime build output, never doc/Mods/old-world files.
 find "$build" -maxdepth 1 -type f \( -name '*.dll' -o -name '*.json' -o -name '*.pdb' -o -name '*.zip' \) -exec cp {} "$target/" \;
 cp -r "$build/runtimes" "$target/" || exit 2
 (cd "$target"; "$dotnet" IntegrationGate.dll "$mode" "$seed" > console.log 2>&1; result=$?; if [[ ( "$mode" == fixture || "$mode" == terraced ) && "$result" == 0 ]]; then "$dotnet" IntegrationGate.dll reload >> console.log 2>&1; printf "reload EXIT=%s\n" "$?" | tee reload-exit.txt; fi; printf '%s EXIT=%s\n' "$name" "$result" | tee exit.txt)
done
# Each individual exit/result is evidence. A natural run may legitimately fail to find food.
