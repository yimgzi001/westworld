#!/usr/bin/env python3
"""Stage existing build and source fingerprints only; never launches any process."""
import argparse,hashlib,json,pathlib,re,shutil,shlex

def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  while b:=f.read(65536):h.update(b)
 return h.hexdigest()
def safe(path):
 if any(p.is_symlink() for p in (path,*path.parents)):raise ValueError('symlink')
 return path

CASES=("early","route_cancel_before","route_cancel_after","route_wall","route_support")

def native_library_name(name):
 return bool(re.fullmatch(r'[A-Za-z0-9_+.-]+\.so(?:\.[0-9]+)*',name)) or name.lower().endswith(('.dll','.dylib'))

def stage(root,label,delay,repeat,content,case="early"):
 root=safe(pathlib.Path(root).absolute());content=safe(pathlib.Path(content).absolute())
 if case not in CASES:raise ValueError('case_not_allowed')
 if not re.fullmatch('[A-Za-z0-9][A-Za-z0-9_-]{0,60}',label) or delay not in (1,3,8) or repeat not in (1,2,3):raise ValueError('case')
 out=root/'experiments/scapi-tool-agent/bin/Release/net10.0';dest=root/f'work/{label}-delay{delay}-repeat{repeat}'
 if case!='early':dest=root/f'work/{label}-{case}-delay{delay}-repeat{repeat}'
 safe(dest)
 if dest.exists():raise FileExistsError('fresh directory required')
 required=['ToolAgentGate.dll','Survivalcraft.dll','Westworld.Scapi.Adapter.dll','Westworld.Runtime.Core.dll','Westworld.Decisions.Local.dll','ToolAgentGate.runtimeconfig.json','ToolAgentGate.deps.json','runtimes/linux-x64/native/libglfw.so.3','runtimes/linux-x64/native/libnfd.so']
 for n in required:
  if not safe(out/n).is_file():raise FileNotFoundError(n)
 if not content.is_file():raise FileNotFoundError('Content.zip')
 files=[]
 for p in out.rglob('*'):
  safe(p)
  if not p.is_file():continue
  rel=p.relative_to(out)
  if native_library_name(p.name) or p.name=='init.js' or p.name.endswith(('.runtimeconfig.json','.deps.json')):
   files.append((p,rel))
 sources=[]
 for folder in ['runtime/Westworld.Runtime.Core','runtime/Westworld.Scapi.Adapter','experiments/scapi-tool-agent','game/Survivalcraft/Westworld/Actions','game/Survivalcraft/Westworld/Knowledge','game/Survivalcraft/Westworld/Upper']:
  sources.extend(p for p in (root/folder).glob('*.cs') if safe(p).is_file())
 sources.extend([root/'experiments/navigation-continuity/driver.py',root/'experiments/navigation-continuity/stage.py'])
 dest.mkdir(parents=True)
 for p,rel in files:
  target=dest/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,target)
 shutil.copy2(content,dest/'Content.zip')
 for p in sources:
  target=dest/'sources'/p.relative_to(root);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,target)
 shutil.copy2(root/'experiments/navigation-continuity/driver.py',dest/'driver.py')
 hashes={str(p.relative_to(dest)):sha(p) for p in dest.rglob('*') if p.is_file()}
 (dest/'installed-hashes.json').write_text(json.dumps(hashes,sort_keys=True,indent=2))
 (dest/'stage.json').write_text(json.dumps({'schema':'continuity-stage-v1','delaySeconds':delay,'repeat':repeat,'mode':f'handoff file {case}','case':case,'status':'staged_not_run','stagingOnly':True,'sourceBuildCorrespondence':'hashes record files at staging; coordinator must verify these sources built installed DLLs'},indent=2))
 q=shlex.quote
 (dest/'run.sh').write_text(f'''#!/bin/bash
set -u
cd {q(str(dest))}
test ! -e doc && test ! -e tool-agent.jsonl && test ! -e continuity-driver-started.json || exit 2
python3 - <<'CHECK'
import json,pathlib,hashlib
for name,expected in json.load(open('installed-hashes.json')).items():
 p=pathlib.Path(name)
 assert not p.is_symlink() and hashlib.sha256(p.read_bytes()).hexdigest()==expected,name
CHECK
if [ $? -ne 0 ]; then exit 2; fi
python3 driver.py . --delay {delay} >driver.log 2>&1 &
driver=$!
{q(str(root/'work/dotnet/dotnet'))} ToolAgentGate.dll handoff file {case} >console.log 2>&1
code=$?
printf '%s\\n' "$code" >exit.txt
wait "$driver"; driver_code=$?
printf '%s\\n' "$driver_code" >driver-exit.txt
if [ "$code" -ne 0 ]; then exit "$code"; fi
exit "$driver_code"
''')
 return dest
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=pathlib.Path,default=pathlib.Path(__file__).resolve().parents[2]);p.add_argument('--label',required=True);p.add_argument('--delay',type=int,choices=[1,3,8],required=True);p.add_argument('--repeat',type=int,choices=[1,2,3],required=True);p.add_argument('--content',type=pathlib.Path,required=True);p.add_argument('--case',choices=CASES,default='early');a=p.parse_args();print(stage(a.root,a.label,a.delay,a.repeat,a.content,a.case))
