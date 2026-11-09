#!/usr/bin/env python3
"""Stage a given existing build. Never build, launch a game/driver, or call an API."""
import argparse,hashlib,json,pathlib,re,shlex,shutil
REQUIRED=('ToolAgentGate.dll','Survivalcraft.dll','Westworld.Scapi.Adapter.dll','Westworld.Runtime.Core.dll','Westworld.Decisions.Local.dll','ToolAgentGate.runtimeconfig.json','ToolAgentGate.deps.json','runtimes/linux-x64/native/libglfw.so.3','runtimes/linux-x64/native/libnfd.so')
SOURCE_FOLDERS=('runtime','game/Survivalcraft/Westworld','experiments/scapi-tool-agent','experiments/food-acquisition-evidence','experiments/openai-tool-controller')
SOURCE_FILES=('game/Survivalcraft/Component/ComponentPickableGathererPlayer.cs',)
def safe(path):
 p=pathlib.Path(path).absolute()
 if any(x.is_symlink() for x in (p,*p.parents)):raise ValueError('symlink')
 return p

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def native(name):return bool(re.fullmatch(r'[A-Za-z0-9_+.-]+\.so(?:\.[0-9]+)*',name)) or name.endswith(('.dll','.dylib'))
def stage(root,label,build_output,content,controller='deterministic'):
 root=safe(root);out=safe(build_output);content=safe(content)
 if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,60}',label) or controller not in ('deterministic','model'):raise ValueError('stage arguments')
 dest=safe(root/'work'/label)
 if dest.exists():raise FileExistsError('fresh destination required')
 for name in REQUIRED:
  if not safe(out/name).is_file():raise FileNotFoundError(name)
 if not content.is_file():raise FileNotFoundError('Content.zip')
 files=[]
 for p in out.rglob('*'):
  safe(p)
  if p.is_file() and (native(p.name) or p.name=='init.js' or p.name.endswith(('.runtimeconfig.json','.deps.json'))):files.append(p)
 sources=[]
 for folder in SOURCE_FOLDERS:
  for p in (root/folder).rglob('*'):
   if any(part in ('bin','obj','__pycache__','evidence') for part in p.relative_to(root/folder).parts):continue
   if p.is_file() and p.suffix in ('.cs','.csproj','.props','.targets','.py'):sources.append(safe(p))
 for relative in SOURCE_FILES:
  p=safe(root/relative)
  if not p.is_file():raise FileNotFoundError(relative)
  sources.append(p)
 driver=safe(root/'experiments/food-acquisition-evidence/deterministic_driver.py')
 if not driver.is_file():raise FileNotFoundError('deterministic driver')
 dest.mkdir(parents=True)
 for p in files:
  target=dest/p.relative_to(out);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,target)
 shutil.copy2(content,dest/'Content.zip');shutil.copy2(driver,dest/'deterministic_driver.py')
 sourceHashes={}
 for p in sources:
  relative=p.relative_to(root);target=dest/'sources'/relative;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,target);sourceHashes[relative.as_posix()]=sha(p)
 (dest/'source-hashes.json').write_text(json.dumps(sourceHashes,sort_keys=True,indent=2))
 metadata={'schema':'food-acquisition-stage-v1','mode':'food_drop file','controllerKind':controller,'status':'staged_not_run','stagingOnly':True,'automaticDriver':False,'automaticApi':False,'sourceBuildCorrespondence':'Hashes capture given binaries and current source separately; coordinator must verify source/build correspondence. Staging does not prove it.','buildOutput':str(out),'sourceCount':len(sourceHashes),'maximumDriverRequests':4,'maximumDriverSeconds':180}
 (dest/'stage.json').write_text(json.dumps(metadata,indent=2))
 q=shlex.quote
 (dest/'run.sh').write_text(f'''#!/bin/bash
set -u
cd {q(str(dest))} || exit 2
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
{q(str(root/'work/dotnet/dotnet'))} ToolAgentGate.dll food_drop file >console.log 2>&1
code=$?
printf '%s\\n' "$code" >exit.txt
exit "$code"
''')
 hashes={p.relative_to(dest).as_posix():sha(p) for p in dest.rglob('*') if p.is_file()}
 (dest/'installed-hashes.json').write_text(json.dumps(hashes,sort_keys=True,indent=2))
 return dest
if __name__=='__main__':
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--root',type=pathlib.Path,default=pathlib.Path(__file__).resolve().parents[2]);ap.add_argument('--label',required=True);ap.add_argument('--build-output',type=pathlib.Path,required=True);ap.add_argument('--content',type=pathlib.Path,required=True);ap.add_argument('--controller',choices=['deterministic','model'],default='deterministic');a=ap.parse_args();print(stage(a.root,a.label,a.build_output,a.content,a.controller))
