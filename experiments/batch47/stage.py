#!/usr/bin/env python3
"""Freeze one reviewed parallel run. Does not build/start a game or call API.
Lifecycle follows capability-matrix40/stage.py; mode is fixed matrix_parallel/file.
"""
import argparse, hashlib, json, pathlib, re, shutil
ROOT=pathlib.Path(__file__).resolve().parents[2]
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def launcher(root,suite='scale2'):
 mode='matrix_scale47' if suite.startswith('scale') else 'matrix_recovery47'
 env={'WESTWORLD_CAPABILITY_MENU':'1','WESTWORLD_MATRIX41':'baseline','WESTWORLD_SOCIAL42':'1','WESTWORLD_SCALE47_ACTORS':suite.removeprefix('scale') if suite.startswith('scale') else '2'}
 return '\n'.join(['#!/usr/bin/env bash','set -euo pipefail','cd -- "$(dirname -- "$0")"','test ! -e doc && test ! -e Mods && test ! -e tool-agent.jsonl && test ! -e console.log',
 'python3 monitor47.py --sdk '+str(root/'work/dotnet/dotnet')+' --mode '+mode+" --env '"+json.dumps(env)+"'",''])

def prepare(root,label,assets,suite="scale2"):
 if suite not in ('scale2','scale10','scale20','scale50','recovery'):raise ValueError('unreviewed suite')
 root=pathlib.Path(root).resolve();assets=pathlib.Path(assets).resolve()
 if not re.fullmatch(r'[A-Za-z0-9_-]{1,64}',label):raise ValueError('invalid fresh label')
 out=root/'experiments/scapi-tool-agent/bin/Debug/net10.0'
 if not (assets/'Content.zip').is_file() or not (out/'ToolAgentGate.dll').is_file():raise ValueError('existing Content.zip and built Debug host required')
 if any(p.is_symlink() for p in out.rglob('*')):raise ValueError('symlink in build output')
 if any(p.name in ('doc','Mods','tool-agent.jsonl','console.log') for p in out.rglob('*')):raise ValueError('build output contains mutable runtime state')
 d=root/'work'/f'{label}-{suite}';batch=root/'work'/f'{label}-{suite}-batch.sh'
 if d.exists() or batch.exists():raise ValueError('refusing existing run or batch path')
 sources={}
 for directory in ['experiments/scapi-tool-agent','runtime/Westworld.Capabilities','runtime/Westworld.Decisions.Local','runtime/Westworld.Scapi.Adapter','runtime/Westworld.Runtime.Core','experiments/capability-matrix40','experiments/batch47']:
  for p in (root/directory).glob('*'):
   if p.is_file() and p.suffix in ('.cs','.csproj','.py','.md'):sources[str(p.relative_to(root))]=sha(p)
 shutil.copytree(out,d);shutil.copy2(assets/'Content.zip',d/'Content.zip')
 for source,target in [('summarize.py','summarize47.py'),('monitor.py','monitor47.py')]:shutil.copy2(root/'experiments/batch47'/source,d/target)
 installed={str(p.relative_to(d)):sha(p) for p in d.rglob('*') if p.is_file()}
 (d/'installed-hashes.json').write_text(json.dumps(installed,indent=2));(d/'source-hashes.json').write_text(json.dumps(sources,indent=2))
 (d/'run.sh').write_text(launcher(root,suite));(d/'run.sh').chmod(0o755)
 # The summarizer itself is frozen and hashed with runtime files.
 batch.write_text(f'''#!/usr/bin/env bash
set +e
bash "{d}/run.sh"
run_code=$?
python3 "{d}/summarize47.py" "{d}/tool-agent.jsonl" > "{d}/summary.json"
summary_code=$?
test "$run_code" -eq 0 && test "$summary_code" -eq 0
''');batch.chmod(0o755)
 return d,batch

def main():
 ap=argparse.ArgumentParser();ap.add_argument('--label',required=True);ap.add_argument('--assets',required=True);ap.add_argument('--group',choices=['baseline','stress'],default='baseline');ap.add_argument('--suites',nargs='+',choices=['scale2','scale10','scale20','scale50','recovery'],default=None);a=ap.parse_args()
 a.suites=a.suites or (['scale2','scale10'] if a.group=='baseline' else ['scale20','scale50','recovery'])
 if not re.fullmatch(r'[A-Za-z0-9_-]{1,64}',a.label):ap.error('invalid label')
 if len(set(a.suites))!=len(a.suites):ap.error('duplicate suites')
 batch=ROOT/'work'/f'{a.label}-batch47-all.sh'
 if batch.exists():ap.error('existing batch')
 for s in a.suites:
  if (ROOT/'work'/f'{a.label}-{s}').exists() or (ROOT/'work'/f'{a.label}-{s}-batch.sh').exists():ap.error('existing run')
 runs=[prepare(ROOT,a.label,a.assets,s) for s in a.suites]
 text='#!/usr/bin/env bash\nset +e\nfailed=0\n'
 for d,b in runs:
  (d/'execution-group.json').write_text(json.dumps({'concurrentGroup':a.label if a.group=='stress' else None,'execution':a.group,'contentionExpected':a.group=='stress'}))
  text+=f'bash "{b}"\nif [ "$?" -ne 0 ]; then failed=1; fi\n'
 if a.group=='stress':
  text='#!/usr/bin/env bash\nset +e\nfailed=0\npids=()\n'+''.join(f'bash "{b}" &\npids+=($!)\n' for d,b in runs)+'for p in "${pids[@]}"; do wait "$p" || failed=1; done\n'
 text+='exit "$failed"\n';batch.write_text(text);batch.chmod(0o755);print(batch)
if __name__=='__main__':main()
