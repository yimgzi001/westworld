#!/usr/bin/env bash
# Build+stage only. Launch run.sh from the normal dot desktop terminal.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
label="${1:?fresh label required}"
assets="${2:?existing runtime assets directory required}"
[[ "$label" =~ ^[A-Za-z0-9_-]+$ ]] || exit 2
sdk="$root/work/dotnet/dotnet"
test -f "$assets/Content.zip"
test -f "$root/work/local-model/Qwen3.5-4B-Q4_K_M.gguf"
test -x "$sdk"
# Serial build avoids restricted MSBuild worker IPC; no restore/network needed.
"$sdk" build "$root/experiments/scapi-tool-agent/ToolAgentGate.csproj" -c Release --no-restore -m:1
python3 - "$root" "$label" "$assets" <<'PY'
import pathlib,sys,shutil,hashlib,json
r=pathlib.Path(sys.argv[1]);label=sys.argv[2];assets=pathlib.Path(sys.argv[3])
out=r/'experiments/scapi-tool-agent/bin/Release/net10.0'
modes=['inventory','craft','cook','hunt']
dest=[r/'work'/f'{label}-{m}' for m in modes]
if any(d.exists() for d in dest):raise SystemExit('Refusing existing run directory')
files=list(out.glob('*.dll'))
for directory in ['experiments/scapi-tool-agent','runtime/Westworld.Decisions.Local','runtime/Westworld.Scapi.Adapter','runtime/Westworld.Runtime.Core']:
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
test ! -e doc && test ! -e Mods && test ! -e tool-agent.jsonl
python3 - <<'CHECK'
import json,hashlib,pathlib
for n,h in json.load(open("installed-hashes.json")).items():
 assert hashlib.sha256(pathlib.Path(n).read_bytes()).hexdigest()==h,n
CHECK
set +e
"{r}/work/dotnet/dotnet" ToolAgentGate.dll {mode} "${{1:-llama}}" >console.log 2>&1
code=$?
printf '%s\\n' "$code" >exit.txt
exit "$code"
'''
 (d/'run.sh').write_text(script)
print('\n'.join(str(d/'run.sh') for d in dest))
PY
