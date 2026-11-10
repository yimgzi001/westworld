#!/usr/bin/env python3
"""Freeze one reviewed parallel run. Does not build/start a game or call API.
Lifecycle follows capability-matrix40/stage.py; mode is fixed matrix_parallel/file.
"""
import argparse, hashlib, json, pathlib, re, shutil
ROOT=pathlib.Path(__file__).resolve().parents[2]
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def launcher(root,actors=2):
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
'''.replace('SDK',str(root/'work/dotnet/dotnet')).replace('MODE','matrix_parallel').replace('__ENV_JSON__',json.dumps({'WESTWORLD_CAPABILITY_MENU':'1','WESTWORLD_MATRIX41':'baseline','WESTWORLD_SOCIAL42':'1','WESTWORLD_PARALLEL46_ACTORS':str(actors)}))

def prepare(root,label,assets,actors=2):
 if type(actors) is not int or actors not in (2,4):raise ValueError('reviewed actor count must be 2 or 4')
 root=pathlib.Path(root).resolve();assets=pathlib.Path(assets).resolve()
 if not re.fullmatch(r'[A-Za-z0-9_-]{1,64}',label):raise ValueError('invalid fresh label')
 out=root/'experiments/scapi-tool-agent/bin/Debug/net10.0'
 if not (assets/'Content.zip').is_file() or not (out/'ToolAgentGate.dll').is_file():raise ValueError('existing Content.zip and built Debug host required')
 if any(p.is_symlink() for p in out.rglob('*')):raise ValueError('symlink in build output')
 if any(p.name in ('doc','Mods','tool-agent.jsonl','console.log') for p in out.rglob('*')):raise ValueError('build output contains mutable runtime state')
 d=root/'work'/f'{label}-actors{actors}-matrix_parallel';batch=root/'work'/f'{label}-actors{actors}-parallel-batch.sh'
 if d.exists() or batch.exists():raise ValueError('refusing existing run or batch path')
 sources={}
 for directory in ['experiments/scapi-tool-agent','runtime/Westworld.Capabilities','runtime/Westworld.Decisions.Local','runtime/Westworld.Scapi.Adapter','runtime/Westworld.Runtime.Core','experiments/capability-matrix40','experiments/parallel46']:
  for p in (root/directory).glob('*'):
   if p.is_file() and p.suffix in ('.cs','.csproj','.py','.md'):sources[str(p.relative_to(root))]=sha(p)
 shutil.copytree(out,d);shutil.copy2(assets/'Content.zip',d/'Content.zip')
 shutil.copy2(root/'experiments/parallel46/summarize.py',d/'summarize-parallel.py')
 installed={str(p.relative_to(d)):sha(p) for p in d.rglob('*') if p.is_file()}
 (d/'installed-hashes.json').write_text(json.dumps(installed,indent=2));(d/'source-hashes.json').write_text(json.dumps(sources,indent=2))
 (d/'run.sh').write_text(launcher(root,actors));(d/'run.sh').chmod(0o755)
 # The summarizer itself is frozen and hashed with runtime files.
 batch.write_text(f'''#!/usr/bin/env bash
set +e
bash "{d}/run.sh"
run_code=$?
python3 "{d}/summarize-parallel.py" "{d}/tool-agent.jsonl" > "{d}/summary.json"
summary_code=$?
test "$run_code" -eq 0 && test "$summary_code" -eq 0
''');batch.chmod(0o755)
 return d,batch

def main():
 ap=argparse.ArgumentParser();ap.add_argument('--label',required=True);ap.add_argument('--assets',required=True);ap.add_argument('--actors',nargs='+',type=int,choices=[2,4],default=[2,4]);a=ap.parse_args()
 if len(set(a.actors))!=len(a.actors):ap.error('duplicate actor count')
 batch=ROOT/'work'/f'{a.label}-parallel-all.sh'
 if not re.fullmatch(r'[A-Za-z0-9_-]{1,64}',a.label):ap.error('invalid fresh label')
 if batch.exists() or any((ROOT/'work'/f'{a.label}-actors{n}-matrix_parallel').exists() or (ROOT/'work'/f'{a.label}-actors{n}-parallel-batch.sh').exists() for n in a.actors):ap.error('refusing existing batch paths')
 runs=[]
 try:
  for count in a.actors:
   d,b=prepare(ROOT,a.label,a.assets,count);runs.append((d,b));print(d/'run.sh')
 except ValueError as e:ap.error(str(e))
 text='#!/usr/bin/env bash\nset +e\nfailed=0\n'
 for d,b in runs:text+=f'bash "{b}"\nif [ "$?" -ne 0 ]; then failed=1; fi\n'
 text+='exit "$failed"\n';batch.write_text(text);batch.chmod(0o755);print(batch)
if __name__=='__main__':main()
