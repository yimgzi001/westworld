#!/bin/bash
set -u
cd <CLOUD_REPO>/work/integration38-over_count || exit 2
test ! -e doc && test ! -e tool-agent.jsonl && test ! -e exit.txt || exit 2
python3 - <<'CHECK'
import json,pathlib,hashlib
for name,expected in json.load(open('installed-hashes.json')).items():
 p=pathlib.Path(name)
 assert not p.is_absolute() and '..' not in p.parts
 assert not any(x.is_symlink() for x in (p,*p.parents))
 assert hashlib.sha256(p.read_bytes()).hexdigest()==expected,name
CHECK
if [ $? -ne 0 ]; then exit 2; fi
<CLOUD_REPO>/work/dotnet/dotnet ToolAgentGate.dll food_drop file >console.log 2>&1
code=$?
printf '%s\n' "$code" >exit.txt
exit "$code"
