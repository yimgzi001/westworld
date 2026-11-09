#!/usr/bin/env python3
"""Stage already built host to fresh controlled fixture dirs; no process/API execution."""
from pathlib import Path
import shutil,json,hashlib,sys
r=Path(__file__).resolve().parents[3];label=sys.argv[1];assert label.replace('-','').isalnum();out=r/'experiments/scapi-tool-agent/bin/Release/net10.0';assert (out/'ToolAgentGate.dll').exists()
cases=['early','late','failure','cancel','expired'];assert all(not (r/f'work/{label}-{c}').exists() for c in cases)
for case in cases:
 d=r/f'work/{label}-{case}';shutil.copytree(out,d);shutil.copy2(r/'work/nav-handoff02-late/Content.zip',d/'Content.zip')
 sources=d/'sources';sources.mkdir()
 for n in ['Program.cs','ToolAgentScenario.Prefetch.cs','ToolAgentScenario.HandoffFixture.cs','StructuredFileController.cs','NativeToolOffers.cs','DecisionDispatchWindow.cs']:
  shutil.copy2(r/'experiments/scapi-tool-agent'/n,sources/n)
 (sources/'manifest.json').write_text(json.dumps({p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sources.glob('*.cs')},indent=2))
 (d/'installed-hashes.json').write_text(json.dumps({p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in d.glob('*.dll')},indent=2))
 (d/'run.sh').write_text(f'''#!/bin/bash
set -u
cd "{d}"
test ! -e doc && test ! -e tool-agent.jsonl || exit 2
python3 - <<'CHECK'
import json,pathlib,hashlib
for n,h in json.load(open('installed-hashes.json')).items():
 assert hashlib.sha256(pathlib.Path(n).read_bytes()).hexdigest()==h,n
CHECK
if [ $? -ne 0 ]; then exit 2; fi
python3 "{r}/experiments/scapi-tool-agent/prefetch-gate/driver.py" "{d}" {case} >driver.log 2>&1 &
driver=$!
"{r}/work/dotnet/dotnet" ToolAgentGate.dll handoff file {case} >console.log 2>&1
code=$?
echo "$code" >exit.txt
wait "$driver"
exit "$code"
''')
 print(d/'run.sh')
