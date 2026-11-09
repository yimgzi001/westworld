#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
label="${1:?fresh label}"; assets="${2:?assets directory}"
[[ "$label" =~ ^[a-zA-Z0-9_-]+$ ]]; test -f "$assets/Content.zip"
"$root/work/dotnet/dotnet" build "$root/experiments/scapi-navigation-ledger-gate/NavigationLedgerGate.csproj" -c Release --no-restore -m:1
python3 - "$root" "$label" "$assets" <<'PY'
from pathlib import Path
import sys,shutil,json,hashlib
r=Path(sys.argv[1]); label=sys.argv[2];assets=Path(sys.argv[3]);out=r/'experiments/scapi-navigation-ledger-gate/bin/Release/net10.0'
for mode in ['flat','cancel','obstacle']:
 if (r/'work'/f'{label}-{mode}').exists():raise SystemExit('Fresh directories required')
for mode in ['flat','cancel','obstacle']:
 d=r/'work'/f'{label}-{mode}';shutil.copytree(out,d);shutil.copy2(assets/'Content.zip',d/'Content.zip')
 hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in d.glob('*.dll')};(d/'installed-hashes.json').write_text(json.dumps(hashes,indent=2))
 (d/'run.sh').write_text(f'''#!/usr/bin/env bash
set -euo pipefail
cd "{d}"
test ! -e doc && test ! -e Mods && test ! -e navigation-ledger.jsonl
python3 - <<'CHECK'
import pathlib,json,hashlib
for name,digest in json.load(open('installed-hashes.json')).items():
 assert hashlib.sha256(pathlib.Path(name).read_bytes()).hexdigest()==digest,name
CHECK
set +e
"{r}/work/dotnet/dotnet" NavigationLedgerGate.dll {mode} >console.log 2>&1
code=$?
printf '%s\\n' "$code" >exit.txt
exit "$code"
''');print(d/'run.sh')
PY
