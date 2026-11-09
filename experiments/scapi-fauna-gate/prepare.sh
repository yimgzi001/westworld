#!/usr/bin/env bash
# Prepare only. Does not launch GUI. Usage: bash prepare.sh <existing-runtime> <new-work-directory>
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
assets=$(realpath "$1")
dest=$(realpath -m "$2")
[[ "$dest" == "$repo/work/"* && ! -e "$dest" ]] || { echo 'Require a fresh directory inside cloud repo/work'; exit 2; }
bin="$repo/experiments/scapi-fauna-gate/bin/Release/net10.0"
[[ -f "$bin/FaunaGate.dll" && -f "$assets/Content.zip" ]] || exit 2
mkdir -p "$dest"
cp -a "$bin/." "$dest/"
for name in Content.zip init.js libglfw.so.3 libnfd.so runtimes; do
  if [[ -e "$assets/$name" ]]; then cp -a "$assets/$name" "$dest/"; fi
done
printf 'Prepared %s\nRun with existing GUI environment: dotnet %s/FaunaGate.dll centers (or empty)\n' "$dest" "$dest"
