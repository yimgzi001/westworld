#!/usr/bin/env python3
"""Prepare two new disposable runs; never builds, launches a game, or calls a model."""
import argparse, hashlib, json, pathlib, re, shutil
ROOT=pathlib.Path(__file__).resolve().parents[2]
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def runtime_environment(suite,social_menu):
 return {"WESTWORLD_CAPABILITY_MENU":"1","WESTWORLD_MATRIX41":suite,"WESTWORLD_SOCIAL42":"1" if social_menu else "0"}
def main():
 ap=argparse.ArgumentParser();ap.add_argument('--social-menu',action='store_true',help='Validate social actions through current per-actor dynamic menus');ap.add_argument('--suite',choices=['baseline','equipment','combat','furniture','items','mount','climb','sleep','interact'],default='baseline',help='One isolated reviewed matrix family per fresh world');ap.add_argument('--label',required=True);ap.add_argument('--assets',required=True);ap.add_argument('--modes',nargs='+',choices=['matrix_single','matrix_social'],default=['matrix_single','matrix_social']);args=ap.parse_args()
 if args.social_menu and args.modes!=['matrix_social']:ap.error('social-menu requires --modes matrix_social')
 if len(set(args.modes))!=len(args.modes):ap.error('duplicate modes')
 if args.suite!='baseline' and args.modes!=['matrix_single']:ap.error('family suites require --modes matrix_single')
 if not re.fullmatch(r'[A-Za-z0-9_-]{1,64}',args.label):ap.error('invalid fresh label')
 assets=pathlib.Path(args.assets).resolve();out=ROOT/'experiments/scapi-tool-agent/bin/Debug/net10.0'
 if not (assets/'Content.zip').is_file() or not (out/'ToolAgentGate.dll').is_file():ap.error('existing Content.zip and built Debug host required')
 if any((out/n).exists() for n in ['doc','Mods','tool-agent.jsonl','console.log']):ap.error('build output contains mutable runtime state')
 destinations=[ROOT/'work'/f'{args.label}-{mode}' for mode in args.modes]
 batch=ROOT/'work'/f'{args.label}-matrix-batch.sh'
 if batch.exists() or any(p.exists() for p in destinations):ap.error('refusing existing run or batch path')
 sources={}
 for directory in ['experiments/scapi-tool-agent','runtime/Westworld.Capabilities','runtime/Westworld.Decisions.Local','runtime/Westworld.Scapi.Adapter','runtime/Westworld.Runtime.Core','experiments/capability-matrix40']:
  for p in (ROOT/directory).glob('*'):
   if p.is_file() and p.suffix in ('.cs','.csproj','.py','.md'):sources[str(p.relative_to(ROOT))]=sha(p)
 for d,mode in zip(destinations,args.modes):
  shutil.copytree(out,d);shutil.copy2(assets/'Content.zip',d/'Content.zip')
  installed={str(p.relative_to(d)):sha(p) for p in d.rglob('*') if p.is_file()}
  (d/'installed-hashes.json').write_text(json.dumps(installed,indent=2))
  (d/'source-hashes.json').write_text(json.dumps(sources,indent=2))
  script='''#!/usr/bin/env bash
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
'''.replace('SDK',str(ROOT/'work/dotnet/dotnet')).replace('MODE',mode).replace('__ENV_JSON__',json.dumps(runtime_environment(args.suite,args.social_menu)))
  (d/'run.sh').write_text(script);(d/'run.sh').chmod(0o755)
  print(d/'run.sh')
 batch_text='#!/usr/bin/env bash\nset +e\nfailed=0\n'
 for d,mode in zip(destinations,args.modes):
  required=" --require-social-menu" if args.social_menu else ""
  batch_text+=f'bash "{d}/run.sh"\ncode=$?\nif [ "$code" -ne 0 ]; then failed=1; fi\npython3 "{ROOT}/experiments/capability-matrix40/summarize.py" "{d}/tool-agent.jsonl" --mode {mode}{required} > "{d}/summary.json"\nif [ "$?" -ne 0 ]; then failed=1; fi\n'
 batch_text+='exit "$failed"\n';batch.write_text(batch_text);batch.chmod(0o755);print(batch)
if __name__=='__main__':main()
