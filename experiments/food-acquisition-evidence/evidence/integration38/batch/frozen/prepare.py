#!/usr/bin/env python3
"""Stage four deterministic Eat probes + one independent natural model host. No launches."""
import argparse,hashlib,importlib.util,json,re,shutil,shlex
from pathlib import Path
CASES=('wrong_slot_value','over_count','fresh_slot_change','valid_eat')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def load(p):
 s=importlib.util.spec_from_file_location('stage38_'+sha(p)[:12],p);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
def write(p,v):
 with p.open('x') as f:json.dump(v,f,indent=2)
def prepare(root,build,content,expected,label):
 food=load(root/'experiments/food-acquisition-evidence/stage.py');natural=load(root/'experiments/natural-resource-evidence/stage.py');recovery=load(root/'experiments/batch-recovery/recovery.py')
 root,build,content=map(food.safe,(root,build,content))
 if not re.fullmatch('[A-Za-z0-9_-]{1,32}',label) or sha(build/'ToolAgentGate.dll')!=expected:raise ValueError('label_or_host_hash')
 batch=root/'work'/(label+'-batch');names=list(CASES)
 for p in [batch]+[root/'work'/(label+'-'+n) for n in names]:
  if p.exists():raise FileExistsError('fresh_required:'+str(p))
 batch.mkdir();(batch/'frozen').mkdir()
 for p in Path(__file__).parent.iterdir():
  if p.is_file() and p.suffix in ('.py','.md','.json','.log'):shutil.copy2(p,batch/'frozen'/p.name)
 shutil.copy2(root/'experiments/batch-recovery/recovery.py',batch/'frozen/recovery.py')
 shutil.copy2(root/'experiments/direct-move-prefetch-evidence/matrix_runner.py',batch/'frozen/process_helpers.py')
 cases=[]
 for case in names:
  ismodel=case=='natural';dest=natural.stage(root,label+'-'+case,build,content,'natural34-b','model') if ismodel else food.stage(root,label+'-'+case,build,content,'deterministic')
  if ismodel:
   p=dest/'run.sh';s=p.read_text();old='ToolAgentGate.dll natural file natural34-b';assert s.count(old)==1;p.write_text(s.replace(old,'ToolAgentGate.dll natural_food file natural34-b'))
  testdir=dest/'sources/work/integration38-cases';testdir.mkdir(parents=True)
  for p in (batch/'frozen').iterdir():shutil.copy2(p,testdir/p.name)
  meta=json.loads((dest/'stage.json').read_text());meta.update(mode='natural_food file natural34-b' if ismodel else 'food_drop file',controllerKind='official_model_separately_started' if ismodel else 'deterministic_eat_contract38',eatContractCase=None if ismodel else case,automaticApi=False,seedIsPriorComparisonNotBlind=ismodel)
  (dest/'stage.json').write_text(json.dumps(meta,indent=2))
  sh=json.loads((dest/'source-hashes.json').read_text())
  for p in testdir.iterdir():sh[str(p.relative_to(dest/'sources'))]=sha(p)
  (dest/'source-hashes.json').write_text(json.dumps(sh,indent=2,sort_keys=True))
  installed={str(p.relative_to(dest)):sha(p) for p in dest.rglob('*') if p.is_file() and p.name!='installed-hashes.json'}
  (dest/'installed-hashes.json').write_text(json.dumps(installed,indent=2,sort_keys=True))
  command=None if ismodel else ['python3','-B',str(testdir/'driver.py'),str(dest),'--case',case]
  cases.append({'case':case,'directory':str(dest),'model':ismodel,'hostOnlyRun':str(dest/'run.sh'),'driverCommand':command,'validatorCommand':(['python3','-B',str(dest/'sources/experiments/integration-matrix/validate_food.py'),str(dest/'tool-agent.jsonl')] if ismodel else ['python3','-B',str(testdir/'validate.py'),str(dest),'--output',str(dest/'eat-contract-validation.json')]),'expectedHostExit':0 if case in ['valid_eat','natural'] else 1,'requests':8 if ismodel else 4,'wallSeconds':180,'requestSeconds':45,'drainSeconds':7,'runnerWatchdogSeconds':195,'installedHash':sha(dest/'installed-hashes.json'),'sourceHash':sha(dest/'source-hashes.json')})
 manifest={'schema':'integration38-cases-v1','root':str(root),'hostSha256':expected,'environment':'dot-cloud','cases':cases,'automaticStart':False,'maxModelRequests':8,'modelCallsAlreadyMade':0,'naturalStage':'Parent-owned integration38-natural; not prepared or launched here','note':'Negative host goal failure is expected; judge contractVerdict separately. Native natural result not forced.'}
 write(batch/'matrix.json',manifest);(batch/'run-fixtures.sh').write_text('#!/bin/bash\nset -u\nexec python3 -B '+shlex.quote(str(batch/'frozen/runner.py'))+' --manifest '+shlex.quote(str(batch/'matrix.json'))+'\n');write(batch/'bundle-hashes.json',{str(p.relative_to(batch)):sha(p) for p in batch.rglob('*') if p.is_file()})
 for c in cases:
  d=Path(c['directory']);recovery.begin(root,label+'-'+c['case'],'eat-contract38-native','dot-cloud',[str(p.relative_to(root)) for p in [batch/'matrix.json',batch/'bundle-hashes.json',d/'installed-hashes.json',d/'source-hashes.json']],[str((d/'runner-outcome.json').relative_to(root))])
 write(batch/'preparation.json',{'staged':4,'processesStarted':0,'status':'staged_not_run','checkpointsBegun':True})
 return batch/'matrix.json'
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[2]);p.add_argument('--build',type=Path,required=True);p.add_argument('--content',type=Path,required=True);p.add_argument('--expected-host',required=True);p.add_argument('--label',default='integration38');a=p.parse_args();print(prepare(a.root,a.build,a.content,a.expected_host,a.label))
