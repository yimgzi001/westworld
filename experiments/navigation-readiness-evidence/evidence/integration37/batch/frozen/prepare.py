#!/usr/bin/env python3
"""Freeze three existing file-controlled native cases. No process launches."""
import argparse, hashlib, importlib.util, json, re, shutil, shlex
from pathlib import Path

CASES={'arrive':('early','continuity'),'cancel':('route_cancel_after','continuity'),'readiness':('direct_readiness','readiness')}
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def load(path):
 s=importlib.util.spec_from_file_location('prep_'+sha(path)[:12],path);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
def write(path,obj):
 with path.open('x') as f:json.dump(obj,f,indent=2)
def prepare(root,build,content,expected,label='integration37'):
 stage=load(root/'experiments/food-acquisition-evidence/stage.py'); recovery=load(root/'experiments/batch-recovery/recovery.py')
 root,build,content=map(stage.safe,(root,build,content))
 if not re.fullmatch(r'[A-Za-z0-9_-]{1,32}',label) or not re.fullmatch('[a-f0-9]{64}',expected):raise ValueError('label_or_hash')
 if sha(build/'ToolAgentGate.dll')!=expected:raise ValueError('frozen_host_changed')
 batch=root/'work'/(label+'-native-batch')
 names=[label+'-'+case for case in CASES]
 for p in [batch]+[root/'work'/n for n in names]+[root/'.batch-checkpoints'/(n+'.json') for n in names]:
  stage.safe(p)
  if p.exists():raise FileExistsError('fresh_only:'+str(p))
 batch.mkdir();(batch/'frozen').mkdir()
 for f in ['prepare.py','runner.py','validate.py','test_validate.py','README.md']:shutil.copy2(Path(__file__).parent/f,batch/'frozen'/f)
 shutil.copy2(root/'experiments/batch-recovery/recovery.py',batch/'frozen/recovery.py')
 shutil.copy2(root/'experiments/direct-move-prefetch-evidence/matrix_runner.py',batch/'frozen/process_helpers.py')
 cases=[]; baseline=None
 for case,(mode,driver) in CASES.items():
  dest=stage.stage(root,label+'-'+case,build,content,'deterministic','direct_readiness' if case=='readiness' else None)
  script=dest/'run.sh'; old='ToolAgentGate.dll food_drop file'; new='ToolAgentGate.dll handoff file '+mode
  if case!='readiness':
   text=script.read_text();assert text.count(old)==1;script.write_text(text.replace(old,new))
  meta=json.loads((dest/'stage.json').read_text());meta.update(mode='handoff file '+mode,controllerKind='deterministic_no_model_audit37',maximumDriverRequests=4 if driver=='readiness' else 2,maximumDriverSeconds=65)
  (dest/'stage.json').write_text(json.dumps(meta,indent=2))
  installed=json.loads((dest/'installed-hashes.json').read_text())
  for f in ['run.sh','stage.json']:installed[f]=sha(dest/f)
  (dest/'installed-hashes.json').write_text(json.dumps(installed,indent=2,sort_keys=True))
  sources=json.loads((dest/'source-hashes.json').read_text())
  if baseline is not None and sources!=baseline:raise ValueError('source_changed_between_cases')
  baseline=sources
  cases.append(dict(case=case,mode=mode,driver=driver,directory=str(dest),checkpoint=label+'-'+case,installedSha256=sha(dest/'installed-hashes.json'),sourceSha256=sha(dest/'source-hashes.json')))
 manifest={'schema':'audit37-native-matrix-v1','root':str(root),'label':label,'environment':'dot-cloud','cases':cases,'hostSha256':expected,'nativeWatchdogSeconds':80,'caseMaximumSeconds':90,'maximumDriverSeconds':65,'apiCalls':0,'automaticStart':False,'sourceBuildCorrespondence':'Coordinator verifies source/build correspondence; captured source/binary hashes alone do not prove compilation.'}
 write(batch/'matrix.json',manifest)
 (batch/'run-matrix.sh').write_text('#!/bin/bash\nset -u\nexec python3 -B '+shlex.quote(str(batch/'frozen/runner.py'))+' --manifest '+shlex.quote(str(batch/'matrix.json'))+'\n')
 write(batch/'bundle-hashes.json',{str(p.relative_to(batch)):sha(p) for p in batch.rglob('*') if p.is_file()})
 for c in cases:
  dest=Path(c['directory']);sources=[batch/'bundle-hashes.json',batch/'matrix.json',dest/'installed-hashes.json',dest/'source-hashes.json']
  recovery.begin(root,c['checkpoint'],'navigation-audit37-native','dot-cloud',[str(p.relative_to(root)) for p in sources],[str((dest/'runner-outcome.json').relative_to(root))])
 write(batch/'preparation.json',{'status':'staged_not_run','processesStarted':0,'caseCount':3,'checkpointsBegun':True})
 return batch/'run-matrix.sh'
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[2]);p.add_argument('--build',type=Path,required=True);p.add_argument('--content',type=Path,required=True);p.add_argument('--expected-host',required=True);p.add_argument('--label',default='integration37');a=p.parse_args();print(prepare(a.root,a.build,a.content,a.expected_host,a.label))
