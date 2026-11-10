#!/usr/bin/env bash
set -u
cd "$(dirname "$0")"
python3 -B sources/experiments/navigation-readiness-evidence/driver.py "$PWD" > driver-console.log 2>&1 & driver=$!
trap 'kill "$driver" 2>/dev/null || true' EXIT
bash run.sh
host_rc=$?
printf '%s\n' "$host_rc" > host-coordinator-exit.txt
wait "$driver"; driver_rc=$?
printf '%s\n' "$driver_rc" > driver-exit.txt
trap - EXIT
python3 -B sources/experiments/navigation-readiness-evidence/validate.py "$PWD" --output "$PWD/readiness-validation.json"
echo "N35_READINESS_HOST=$host_rc DRIVER=$driver_rc VALIDATOR=$?"
