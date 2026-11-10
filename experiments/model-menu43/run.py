#!/usr/bin/env python3
"""Stage or explicitly run one reviewed food_drop model fixture. No default/API at import."""
import argparse, hashlib, importlib.util, json, os, pathlib, subprocess, sys, time
P=pathlib.Path
MODEL='gpt-6.1-terra'
REQUESTS=4 # ToolScenarioBudget.For(food_drop), unchanged host baseline.
SECONDS=180
WATCHDOG=195
ROOT=P(__file__).resolve().parents[2]

def safe(p):
 p=P(p).absolute()
 if any(x.is_symlink() for x in (p,*p.parents)):raise ValueError('symlink')
 return p

def sha(p):return hashlib.sha256(safe(p).read_bytes()).hexdigest()
def load(p):
 p=safe(p);spec=importlib.util.spec_from_file_location('menu43_'+sha(p)[:12],p);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

def write(p,obj):
 with safe(p).open('x') as f:json.dump(obj,f,indent=2);f.flush();os.fsync(f.fileno())
def rows(p):
 if not p.exists():return []
 if safe(p).stat().st_size>64*1024*1024:raise ValueError('log_bound')
 return [json.loads(l) for l in p.read_text().splitlines() if l.strip()]

def stage(root,label,build,content):
 root=safe(root);base=load(root/'experiments/food-acquisition-evidence/stage.py')
 base.SOURCE_FOLDERS=(*base.SOURCE_FOLDERS,'experiments/model-menu43')
 d=base.stage(root,label,build,content,'model')
 run=d/'run.sh';s=run.read_text();needle='ToolAgentGate.dll food_drop file'
 if s.count(needle)!=1:raise ValueError('stage_mode_mismatch')
 # Explicit environment assignment must precede executable, not its file argument.
 executable=str(root/'work/dotnet/dotnet')
 if s.count(executable)!=1:raise ValueError('stage_executable_mismatch')
 s=s.replace(executable,'env WESTWORLD_CAPABILITY_MENU=1 '+executable)
 run.write_text(s)
 metadata=json.loads((d/'stage.json').read_text());metadata.update(schema='model-menu43-stage-v1',maximumDriverRequests=REQUESTS,maximumDriverSeconds=SECONDS,hostMaxRequests=REQUESTS,hostWallSeconds=SECONDS,requestSeconds=45,watchdogSeconds=WATCHDOG,model=MODEL,reasoningEffort='none',capabilityMenu=True,artificialFood=True,naturalSurvival=False)
 (d/'stage.json').write_text(json.dumps(metadata,indent=2))
 hashes={p.relative_to(d).as_posix():sha(p) for p in d.rglob('*') if p.is_file() and p.name!='installed-hashes.json'}
 (d/'installed-hashes.json').write_text(json.dumps(hashes,indent=2,sort_keys=True))
 return d

def verify(d):
 d=safe(d);m=json.loads((d/'stage.json').read_text())
 if m.get('schema')!='model-menu43-stage-v1' or m.get('model')!=MODEL or m.get('reasoningEffort')!='none' or m.get('maximumDriverRequests')!=REQUESTS or m.get('hostMaxRequests')!=REQUESTS or m.get('maximumDriverSeconds')!=SECONDS or not m.get('capabilityMenu'):raise ValueError('stage_contract')
 for name,h in json.loads((d/'installed-hashes.json').read_text()).items():
  p=P(name)
  if p.is_absolute() or '..' in p.parts or sha(d/p)!=h:raise ValueError('installed_hash')
 if 'env WESTWORLD_CAPABILITY_MENU=1 ' not in (d/'run.sh').read_text():raise ValueError('menu_switch_missing')
 for n in ['execution-started.json','execution.json','control','doc','Mods','exit.txt','tool-agent.jsonl','api-audit.jsonl','api-audit.jsonl.events.jsonl']:
  if (d/n).exists():raise FileExistsError('no_replay')
 return m

def summarize(d):
 d=safe(d);a=rows(d/'tool-agent.jsonl');api=rows(d/'api-audit.jsonl')
 prompts=[x for x in a if x.get('kind')=='model_prompt']
 discovery=[x for x in a if x.get('kind')=='capability_discovery']
 menu=any('read_capability_schema' in x.get('prompt','') and 'capabilities' in x.get('prompt','') for x in prompts)
 results=[x for x in a if x.get('kind')=='result']
 final=results[-1] if results else None
 published={x.get('requestId') for x in a if x.get('kind')=='controller_request_published'}
 received={x.get('requestId') for x in a if x.get('kind')=='controller_response_received'}
 genuine=any(x.get('outcome')=='received' and x.get('actualModel')==MODEL and x.get('requestId') in published&received and x.get('requestId') is not None for x in api)
 return dict(schema='model-menu43-summary-v1',artificialFood=True,naturalSurvival=False,newMenuObserved=menu,discoveryCount=len(discovery),modelPrompts=len(prompts),hostResult=final,actualModels=sorted({x['actualModel'] for x in api if x.get('actualModel')}),genuineModelResponseObserved=genuine,apiRecords=len(api),usage=[x.get('usage') for x in api],passed=bool(menu and final and final.get('passed') and genuine),note='Host food provenance/effect validation must also be reviewed; missing usage is null, not zero.')

def run(d,allow_api=False):
 if not allow_api or not sys.stdin.isatty():raise ValueError('explicit_api_and_tty_required')
 d=safe(d);verify(d)
 driver=d/'sources/experiments/openai-tool-controller/controller.py'
 controller=load(driver)
 if MODEL not in controller.VERIFIED_MODELS or 'none' not in controller.VERIFIED_MODELS[MODEL]:raise ValueError('model_not_verified')
 lifecycle=load(d/'sources/experiments/direct-move-prefetch-evidence/matrix_runner.py')
 auditcheck=load(d/'sources/experiments/integration-matrix/batch.py')
 command=[sys.executable,'-B',str(driver),'file','--control',str(d/'control'),'--audit',str(d/'api-audit.jsonl'),'--model',MODEL,'--reasoning-effort','none','--max-requests',str(REQUESTS),'--total-seconds',str(SECONDS),'--wait-seconds','60']
 write(d/'execution-started.json',dict(model=MODEL,reasoningEffort='none',requests=REQUESTS,seconds=SECONDS,noRetry=True,menuRequired=True))
 processes=[];logs=[];started=time.monotonic();forced=False
 result=dict(error=None,driverExit=None,hostExit=None,processCleanupCertain=True,nativeDrainCertain=False)
 try:
  log=(d/'driver-launch.log').open('x');logs.append(log)
  # Inherited controlling terminal only; the controller itself owns getpass.
  proc=subprocess.Popen(command,cwd=d,stdout=log,stderr=None,start_new_session=True);processes.append(proc)
  print('Official controller requests hidden terminal input; no key is accepted by this wrapper.',flush=True)
  while True:
   if proc.poll() is not None:raise RuntimeError('controller_not_ready')
   if any(x.get('event')=='ready' for x in rows(d/'api-audit.jsonl.events.jsonl')):break
   if time.monotonic()-started>60:raise TimeoutError('ready_timeout')
   time.sleep(.05)
  log=(d/'host-launch.log').open('x');logs.append(log)
  host=subprocess.Popen(['bash',str(d/'run.sh')],cwd=d,stdout=log,stderr=subprocess.STDOUT,start_new_session=True);processes.append(host)
  while any(p.poll() is None for p in processes):
   fatal=auditcheck.fatal_model_audit(d)
   if fatal:result['error']='fatal_model_failure';break
   if proc.poll() not in (None,0):result['error']='controller_failed';break
   if host.poll() is not None and not (d/'exit.txt').exists():raise RuntimeError('host_exit_missing')
   if time.monotonic()-started>WATCHDOG:raise TimeoutError('watchdog')
   time.sleep(.05)
 except KeyboardInterrupt:result['error']='interrupted'
 except Exception as e:result['error']=type(e).__name__ # no key/HTTP body/traceback
 finally:
  forced=any(p.poll() is None for p in processes)
  for p in reversed(processes):
   try:lifecycle.terminate_owned(p)
   except BaseException:result['processCleanupCertain']=False
  for f in logs:f.close()
  if processes:result['driverExit']=processes[0].returncode
  if len(processes)>1:result['hostExit']=processes[1].returncode
  result.update(elapsedSeconds=time.monotonic()-started,forcedTermination=forced)
  # OS cleanup is not native drain. Keep false until raw native evidence is audited.
  write(d/'execution.json',result)
 report=summarize(d);write(d/'menu-summary.json',report)
 return 0 if result['error'] is None and result['hostExit']==0 and result['driverExit']==0 and report['passed'] else 1

def main():
 p=argparse.ArgumentParser(description=__doc__);sub=p.add_subparsers(dest='mode',required=True)
 s=sub.add_parser('stage');s.add_argument('--root',type=P,default=ROOT);s.add_argument('--label',required=True);s.add_argument('--build-output',type=P,required=True);s.add_argument('--content',type=P,required=True)
 r=sub.add_parser('run');r.add_argument('--directory',type=P,required=True);r.add_argument('--allow-model-api',action='store_true')
 q=sub.add_parser('summary');q.add_argument('--directory',type=P,required=True)
 a=p.parse_args()
 if a.mode=='stage':print(stage(a.root,a.label,a.build_output,a.content));return 0
 if a.mode=='summary':print(json.dumps(summarize(a.directory),indent=2));return 0
 return run(a.directory,a.allow_model_api)
if __name__=='__main__':
 try:sys.exit(main())
 except Exception as e:print('model-menu43 error: '+type(e).__name__,file=sys.stderr);sys.exit(2)
