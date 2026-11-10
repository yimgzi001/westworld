#!/usr/bin/env python3
"""Freeze one reviewed cooperative run. Does not build/start a game or call API.
Lifecycle follows capability-matrix40/stage.py; mode is fixed matrix_coop/file.
"""
import argparse, hashlib, json, pathlib, re, shutil
ROOT=pathlib.Path(__file__).resolve().parents[2]
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def launcher(root):
 return '''#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
test ! -e doc && test ! -e Mods && test ! -e tool-agent.jsonl && test ! -e console.log
python3 - <<'VERIFY'
import json,hashlib,pathlib
for name,want in json.load(open('installed-hashes.json')).items():
 p=pathlib.Path(name)
 assert p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest()==want,name
VERIFY
set +e
python3 - <<'RUN'
import json,os,pathlib,resource,subprocess,time
started=time.monotonic()
env=dict(os.environ,**__ENV_JSON__)
with open('console.log','w') as output:
 result=subprocess.run(['timeout','--signal=TERM','--kill-after=5s','140','SDK','ToolAgentGate.dll','MODE','file'],env=env,stdout=output,stderr=subprocess.STDOUT)
usage=resource.getrusage(resource.RUSAGE_CHILDREN)
pathlib.Path('resources.json').write_text(json.dumps({'wallSeconds':time.monotonic()-started,'userCpuSeconds':usage.ru_utime,'systemCpuSeconds':usage.ru_stime,'peakRssKiB':usage.ru_maxrss,'scope':'game subprocess tree including timeout; no model API','exitCode':result.returncode},indent=2))
raise SystemExit(result.returncode)
RUN
code=$?
printf '%s\\n' "$code" >exit.txt
exit "$code"
'''.replace('SDK',str(root/'work/dotnet/dotnet')).replace('MODE','matrix_coop').replace('__ENV_JSON__',json.dumps({'WESTWORLD_CAPABILITY_MENU':'1','WESTWORLD_MATRIX41':'baseline','WESTWORLD_SOCIAL42':'1'}))

def prepare(root,label,assets):
 root=pathlib.Path(root).resolve();assets=pathlib.Path(assets).resolve()
 if not re.fullmatch(r'[A-Za-z0-9_-]{1,64}',label):raise ValueError('invalid fresh label')
 out=root/'experiments/scapi-tool-agent/bin/Debug/net10.0'
 if not (assets/'Content.zip').is_file() or not (out/'ToolAgentGate.dll').is_file():raise ValueError('existing Content.zip and built Debug host required')
 if any(p.is_symlink() for p in out.rglob('*')):raise ValueError('symlink in build output')
 if any(p.name in ('doc','Mods','tool-agent.jsonl','console.log') for p in out.rglob('*')):raise ValueError('build output contains mutable runtime state')
 d=root/'work'/f'{label}-matrix_coop';batch=root/'work'/f'{label}-coop-batch.sh'
 if d.exists() or batch.exists():raise ValueError('refusing existing run or batch path')
 sources={}
 for directory in ['experiments/scapi-tool-agent','runtime/Westworld.Capabilities','runtime/Westworld.Decisions.Local','runtime/Westworld.Scapi.Adapter','runtime/Westworld.Runtime.Core','experiments/capability-matrix40','experiments/cooperation45']:
  for p in (root/directory).glob('*'):
   if p.is_file() and p.suffix in ('.cs','.csproj','.py','.md'):sources[str(p.relative_to(root))]=sha(p)
 shutil.copytree(out,d);shutil.copy2(assets/'Content.zip',d/'Content.zip')
 shutil.copy2(root/'experiments/cooperation45/summarize.py',d/'summarize-coop.py')
 installed={str(p.relative_to(d)):sha(p) for p in d.rglob('*') if p.is_file()}
 (d/'installed-hashes.json').write_text(json.dumps(installed,indent=2));(d/'source-hashes.json').write_text(json.dumps(sources,indent=2))
 (d/'run.sh').write_text(launcher(root));(d/'run.sh').chmod(0o755)
 # The summarizer itself is frozen and hashed with runtime files.
 batch.write_text(f'''#!/usr/bin/env bash
set +e
bash "{d}/run.sh"
run_code=$?
python3 "{d}/summarize-coop.py" "{d}/tool-agent.jsonl" > "{d}/summary.json"
summary_code=$?
test "$run_code" -eq 0 && test "$summary_code" -eq 0
''');batch.chmod(0o755)
 return d,batch

def main():
 ap=argparse.ArgumentParser();ap.add_argument('--label',required=True);ap.add_argument('--assets',required=True);a=ap.parse_args()
 try:d,b=prepare(ROOT,a.label,a.assets)
 except ValueError as e:ap.error(str(e))
 print(d/'run.sh');print(b)
if __name__=='__main__':main()
