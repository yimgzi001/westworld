#!/usr/bin/env python3
"""Explicit serial GUI entry point, only three frozen no-model stages; never import-runs."""
import argparse, hashlib, importlib.util, json, os, subprocess, sys, time
from pathlib import Path

def load(path):
 s=importlib.util.spec_from_file_location('r_'+hashlib.sha256(str(path).encode()).hexdigest()[:12],path);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,v):
 with p.open('x') as f:json.dump(v,f,indent=2);f.flush();os.fsync(f.fileno())
def run_case(c,m,batch,helpers,recovery,validator):
 root=Path(m['root']);d=Path(c['directory']);start=time.monotonic();processes=[];handles=[];uncertain=False
 out={'case':c['case'],'hostExit':None,'driverExit':None,'watchdog':False,'error':None,'missingArtifacts':[]}
 if any((d/f).exists() for f in ['doc','tool-agent.jsonl','exit.txt','runner-started.json','runner-outcome.json']):raise ValueError('used_case_no_replay')
 write(d/'runner-started.json',{'case':c['case'],'monotonicSeconds':start})
 try:
  if sha(d/'installed-hashes.json')!=c['installedSha256'] or sha(d/'source-hashes.json')!=c['sourceSha256']:raise ValueError('stage_manifest_changed')
  helpers.verify_hashes(d,json.loads((d/'installed-hashes.json').read_text()))
  cp=recovery.load(root/'.batch-checkpoints'/(c['checkpoint']+'.json'))
  if cp['state']!='running' or cp['environment']!='dot-cloud':raise ValueError('checkpoint_not_ready')
  f=(d/'driver.log').open('x');g=(d/'launcher.log').open('x');handles=[f,g]
  driver=d/'sources/experiments'/('navigation-readiness-evidence/driver.py' if c['driver']=='readiness' else 'navigation-continuity/driver.py')
  command=[sys.executable,'-B',str(driver),str(d)]+([] if c['driver']=='readiness' else ['--delay','1'])
  dp=subprocess.Popen(command,cwd=d,stdout=f,stderr=subprocess.STDOUT,start_new_session=True);processes.append(dp)
  hp=subprocess.Popen(['bash',str(d/'run.sh')],cwd=d,stdout=g,stderr=subprocess.STDOUT,start_new_session=True);processes.append(hp)
  while hp.poll() is None or dp.poll() is None:
   if time.monotonic()-start>=m['nativeWatchdogSeconds']:out['watchdog']=True;break
   if hp.poll() is not None and not (d/'exit.txt').exists():out['error']='host_wrapper_exited_without_native_exit';break
   time.sleep(.05)
 except Exception as exc:out['error']=type(exc).__name__+':'+str(exc)
 finally:
  for process in processes:
   if process.poll() is None:
    try:helpers.terminate_owned(process)
    except Exception as exc:uncertain=True;out['drainError']=str(exc)
  if processes:out['driverExit']=processes[0].returncode
  if len(processes)>1:out['hostExit']=processes[1].returncode
  for handle in handles:handle.close()
 try:v=validator.evaluate(d,c['case'])
 except Exception as exc:v={'case':c['case'],'verdict':'unknown','error':type(exc).__name__+':'+str(exc)}
 write(d/'native-validation.json',v)
 artifacts=['tool-agent.jsonl','console.log','driver.log','exit.txt','native-validation.json',('readiness-driver-result.json' if c['driver']=='readiness' else 'continuity-driver-result.json')]
 out['missingArtifacts']=[f for f in artifacts if not (d/f).is_file()]
 hashes={f:sha(d/f) for f in artifacts if (d/f).is_file()}
 for p in (d/'control').glob('*/*.json'):hashes[str(p.relative_to(d))]=sha(p)
 write(d/'evidence-hashes.json',hashes)
 out.update(validationVerdict=v['verdict'],elapsedSeconds=time.monotonic()-start,drainUncertain=uncertain)
 out['passed']=out['hostExit']==out['driverExit']==0 and not out['error'] and not out['watchdog'] and not uncertain and not out['missingArtifacts'] and v['verdict']=='pass'
 write(d/'runner-outcome.json',out)
 try:recovery.finish(root,c['checkpoint'],'passed' if out['passed'] else 'failed')
 except Exception as exc:write(d/'checkpoint-finish-error.json',{'error':str(exc)})
 return out

def main(path):
 batch=path.parent;helpers=load(batch/'frozen/process_helpers.py');recovery=load(batch/'frozen/recovery.py');validator=load(batch/'frozen/validate.py')
 helpers.verify_hashes(batch,json.loads((batch/'bundle-hashes.json').read_text()))
 m=json.loads(path.read_text());root=helpers.safe(m['root'])
 if m['schema']!='audit37-native-matrix-v1' or m['environment']!='dot-cloud' or [c['case'] for c in m['cases']]!=['arrive','cancel','readiness'] or m['apiCalls']!=0:raise ValueError('matrix_scope')
 if (m['nativeWatchdogSeconds'],m['caseMaximumSeconds'],m['maximumDriverSeconds'])!=(80,90,65):raise ValueError('budget')
 for c in m['cases']:
  if helpers.safe(c['directory'])!=root/'work'/(m['label']+'-'+c['case']):raise ValueError('case_path')
 if (batch/'matrix-started.json').exists():raise ValueError('no_matrix_replay')
 write(batch/'matrix-started.json',{'monotonicSeconds':time.monotonic()});results=[]
 for c in m['cases']:
  result=run_case(c,m,batch,helpers,recovery,validator);results.append(result)
  if result['drainUncertain']:break
 write(batch/'matrix-result.json',{'results':results,'remainingCasesNotRun':len(m['cases'])-len(results),'allPassed':len(results)==3 and all(r['passed'] for r in results)})
 print(json.dumps(results));return 0 if len(results)==3 and all(r['passed'] for r in results) else 1
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--manifest',type=Path,required=True);a=p.parse_args();raise SystemExit(main(a.manifest))
