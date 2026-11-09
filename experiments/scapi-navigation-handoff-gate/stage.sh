#!/usr/bin/env bash
# Build+stage only. Launch run.sh from the normal dot desktop terminal.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
label="${1:?fresh label required}"
assets="${2:?existing runtime assets directory required}"
[[ "$label" =~ ^[A-Za-z0-9_-]+$ ]] || exit 2
sdk="$root/work/dotnet/dotnet"
test -f "$assets/Content.zip"
test -x "$sdk"
# Serial build avoids restricted MSBuild worker IPC; no restore/network needed.
"$sdk" build "$root/experiments/scapi-navigation-handoff-gate/NavigationHandoffGate.csproj" -c Release --no-restore -m:1
python3 - "$root" "$label" "$assets" <<'PY'
import pathlib,sys,shutil,hashlib,json
r=pathlib.Path(sys.argv[1]);label=sys.argv[2];assets=pathlib.Path(sys.argv[3])
out=r/'experiments/scapi-navigation-handoff-gate/bin/Release/net10.0'
modes=['overlap','cancel','obstacle','late']
dest=[r/'work'/f'{label}-{m}' for m in modes]
if any(d.exists() for d in dest):raise SystemExit('Refusing existing run directory')
files=list(out.glob('*.dll'))+[r/'game/Survivalcraft/Westworld/Actions/AtomicActions.cs']
for directory in ['experiments/scapi-navigation-handoff-gate','runtime/Westworld.Scapi.Adapter','runtime/Westworld.Runtime.Core']:
 for pattern in ['*.cs','*.csproj']:files+=list((r/directory).glob(pattern))
manifest={str(p.relative_to(r)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
for mode,d in zip(modes,dest):
 shutil.copytree(out,d)
 shutil.copy2(assets/'Content.zip',d/'Content.zip')
 (d/'build-manifest.json').write_text(json.dumps(manifest,indent=2))
 # Record exact installed DLL hashes and verify before launch.
 hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in d.glob('*.dll')}
 (d/'installed-hashes.json').write_text(json.dumps(hashes,indent=2))
 script=f'''#!/usr/bin/env bash
set -euo pipefail
cd "{d}"
test ! -e doc && test ! -e Mods && test ! -e handoff.jsonl
python3 - <<'CHECK'
import json,hashlib,pathlib
for n,h in json.load(open("installed-hashes.json")).items():
 assert hashlib.sha256(pathlib.Path(n).read_bytes()).hexdigest()==h,n
CHECK
set +e
"{r}/work/dotnet/dotnet" NavigationHandoffGate.dll {mode} >console.log 2>&1
code=$?
printf '%s\\n' "$code" >exit.txt
exit "$code"
'''
 (d/'run.sh').write_text(script)
print('\n'.join(str(d/'run.sh') for d in dest))
PY
