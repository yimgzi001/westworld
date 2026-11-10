#!/usr/bin/env python3
"""Freeze, explicitly run serially, or summarize mixed integration evidence. No default execution."""
import math,uuid
import argparse,hashlib,importlib.util,json,os,pathlib,re,shutil,subprocess,sys,time
P=pathlib.Path
FIXTURES=('direct_late','direct_failure','direct_cancel')
TAILS=('tail_early','tail_late','tail_failure')
def safe(p):
 p=P(p).absolute()
 if any(x.is_symlink() for x in (p,*p.parents)):raise ValueError('symlink')
 return p
def sha(p):return hashlib.sha256(safe(p).read_bytes()).hexdigest()
def load(path):
 spec=importlib.util.spec_from_file_location('integration_'+sha(path)[:12],path);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
def write(path,obj):
 with safe(path).open('x') as f:f.write(json.dumps(obj,indent=2,allow_nan=False));f.flush();os.fsync(f.fileno())
def read_rows(path):
 if not path.exists():return []
 if safe(path).stat().st_size>64*1024*1024:raise ValueError('log_bound')
 return [json.loads(l) for l in path.read_text().splitlines() if l.strip()]
def verify(directory,hashes):
 for name,h in hashes.items():
  p=P(name)
  if p.is_absolute() or '..' in p.parts or not p.parts:raise ValueError('relative_path')
  if sha(directory/p)!=h:raise ValueError('hash_changed')
def prepare(root,label,build,content,seeds,expected_host):
 root,build,content=map(safe,(root,build,content))
 if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,24}',label):raise ValueError('label')
 if len(seeds)!=3 or len(set(seeds))!=3 or any(not re.fullmatch(r'[A-Za-z0-9_-]{1,64}',s) for s in seeds):raise ValueError('three_distinct_seeds')
 if not re.fullmatch('[0-9a-f]{64}',expected_host) or sha(build/'ToolAgentGate.dll')!=expected_host:raise ValueError('host_hash')
 batch=root/'work'/(label+'-integration');cases=[dict(id='food-'+str(i),mode='natural_food',kind='official_model',seed=s) for i,s in enumerate(seeds)]+[dict(id=c,mode='handoff',kind='deterministic_fixture',fixture=c) for c in FIXTURES]+[dict(id=c,mode='handoff',kind='deterministic_fixture',fixture='direct_early' if c=='tail_early' else 'direct_late' if c=='tail_late' else 'direct_tail_failure',tail=True) for c in TAILS]
 if any((root/'work'/(label+'-'+c['id'])).exists() for c in cases) or batch.exists():raise FileExistsError('fresh_batch_required')
 food=load(root/'experiments/food-acquisition-evidence/stage.py');natural=load(root/'experiments/natural-resource-evidence/stage.py')
 batch.mkdir(parents=True);(batch/'frozen').mkdir()
 for name in ('batch.py','validate_food.py','tail_driver.py','validate_tail.py'):shutil.copy2(root/'experiments/integration-matrix'/name,batch/'frozen'/name)
 binaries=None;source_union={}
 for c in cases:
  name=label+'-'+c['id']
  d=natural.stage(root,name,build,content,c['seed'],'model') if c['kind']=='official_model' else food.stage(root,name,build,content,'deterministic',c['fixture'])
  if c['kind']=='official_model':
   run=d/'run.sh';text=run.read_text();old='ToolAgentGate.dll natural file '+c['seed'];assert text.count(old)==1;run.write_text(text.replace(old,'ToolAgentGate.dll natural_food file '+c['seed']))
   meta=json.loads((d/'stage.json').read_text());meta.update(mode='natural_food file '+c['seed'],foodSuccessRequiresIndependentValidation=True);(d/'stage.json').write_text(json.dumps(meta,indent=2))
  if c.get('tail'):
   target=d/'sources/experiments/integration-matrix';target.mkdir(parents=True,exist_ok=True)
   for filename in ['tail_driver.py','validate_tail.py']:shutil.copy2(root/'experiments/integration-matrix'/filename,target/filename)
  for source,digest in json.loads((d/'source-hashes.json').read_text()).items():
   if source in source_union and source_union[source]!=digest:raise ValueError('source_changed_between_cases')
   source_union[source]=digest
  hashes=json.loads((d/'installed-hashes.json').read_text());hashes['run.sh']=sha(d/'run.sh');hashes['stage.json']=sha(d/'stage.json');(d/'installed-hashes.json').write_text(json.dumps(hashes,indent=2))
  if c.get('tail'):
   for filename in ['tail_driver.py','validate_tail.py']:hashes['sources/experiments/integration-matrix/'+filename]=sha(d/'sources/experiments/integration-matrix'/filename)
   (d/'installed-hashes.json').write_text(json.dumps(hashes,indent=2))
  b={k:v for k,v in hashes.items() if k=='Content.zip' or food.native(P(k).name) or k.endswith(('.deps.json','.runtimeconfig.json'))}
  if binaries is not None and binaries!=b:raise ValueError('build_changed_between_cases')
  binaries=b;c.update(directory=str(d),installedSha256=sha(d/'installed-hashes.json'),sourceSha256=sha(d/'source-hashes.json'),watchdogSeconds=195 if c['kind']=='official_model' else 80)
 verify(root,source_union)
 manifest=dict(schema='integration-matrix-v1',root=str(root),label=label,cases=cases,expectedHostSha256=expected_host,automaticStart=False,maximumModelRequests=24,maximumRequestsPerModelCase=8,modelCaseSeconds=180,environment='dot-cloud',scope='Three genuine model natural-food attempts plus distinct artificial delayed/failure/cancel lifecycle controls; no synthetic result is model evidence.',historicalControl='natural33 verifies natural acquisition only, not edible food success',sourceBuildCorrespondence='Binary hash verified; coordinator must separately verify source/build correspondence before execution')
 write(batch/'matrix.json',manifest);write(batch/'bundle-hashes.json',{str(p.relative_to(batch)):sha(p) for p in batch.rglob('*') if p.is_file()});return batch/'matrix.json'
def manifest(path):
 path=safe(path);batch=path.parent;verify(batch,json.loads((batch/'bundle-hashes.json').read_text()));m=json.loads(path.read_text());root=safe(m['root'])
 if m.get('schema')!='integration-matrix-v1' or m.get('environment')!='dot-cloud' or batch!=root/'work'/(m['label']+'-integration'):raise ValueError('scope')
 if [c['id'] for c in m['cases']]!=['food-0','food-1','food-2',*FIXTURES,*TAILS]:raise ValueError('case_scope')
 for c in m['cases']:
  d=safe(c['directory'])
  if d!=root/'work'/(m['label']+'-'+c['id']):raise ValueError('case_path')
  if sha(d/'installed-hashes.json')!=c['installedSha256'] or sha(d/'source-hashes.json')!=c['sourceSha256']:raise ValueError('stage_manifest_changed')
  verify(d,json.loads((d/'installed-hashes.json').read_text()))
 return m

def measured_overlap(rows,events):
 """Same-machine monotonic, same request/run only. No interpolation across missing samples."""
 results=[]
 for request in [r for r in rows if r.get('kind')=='prefetch_requested']:
  lease=request.get('lease',{});rid=lease.get('RequestId');run=request.get('precedingRunId')
  result=dict(requestId=rid,runId=run,verdict='unknown',seconds=None,path=None)
  starts=[r for r in events if r.get('event')=='request_attempt' and r.get('requestId')==rid]
  ends=[r for r in events if r.get('event')=='request_result' and r.get('requestId')==rid]
  try:
   if len(starts)!=1 or len(ends)!=1 or not rid or not run:raise ValueError()
   if any(r.get('sessionId')!=lease.get('SessionId') or r.get('actorId')!=lease.get('ActorId') for r in starts+ends):raise ValueError()
   start=max(request['monotonicSeconds'],starts[0]['monotonicSeconds']);end=ends[0]['monotonicSeconds']
   for r in rows:
    if r.get('kind')=='prefetch_invalidated' and r.get('requestId')==rid:end=min(end,r['monotonicSeconds'])
   if not (math.isfinite(start) and math.isfinite(end) and start<end):raise ValueError()
   samples=[r for r in rows if r.get('kind')=='predecessor_physical_sample' and start<=r.get('monotonicSeconds',-1)<=end]
   seconds=path=0;segments=0
   for a,b in zip(samples,samples[1:]):
    if not all(x.get('runId')==run and x.get('sessionId')==lease['SessionId'] and x.get('actorId')==lease['ActorId'] and x.get('receiptSequence')==lease['PrecedingReceiptSequence'] and x.get('moving') is True and x.get('status')=='running' and x.get('settled') is False and x.get('cancelRequested') is False for x in [a,b]):continue
    dt=b['monotonicSeconds']-a['monotonicSeconds'];pa,pb=a['position'],b['position']
    if not 0<dt<=.25 or len(pa)!=3 or len(pb)!=3 or not all(math.isfinite(v) for v in pa+pb):continue
    seconds+=dt;path+=math.hypot(pb[0]-pa[0],pb[2]-pa[2]);segments+=1
   result.update(verdict='observed' if seconds>0 and path>.01 else 'unknown',seconds=seconds if segments else None,path=path if segments else None,segments=segments,
     scope='Actual movement while local official request attempt remained outstanding; not proof of server compute, successful handoff, or zero wait.')
  except (KeyError,ValueError,TypeError):pass
  results.append(result)
 return results

def summarize_case(c,batch,validator_directory=None):
 validators=safe(validator_directory) if validator_directory is not None else batch/'frozen'
 d=safe(c['directory']);rows=read_rows(d/'tool-agent.jsonl');api=read_rows(d/'api-audit.jsonl');events=read_rows(d/'api-audit.jsonl.events.jsonl')
 evidence=[r for r in rows if r.get('kind')=='natural_resource_evidence'];report={'id':c['id'],'kind':c['kind'],'nativeLogPresent':bool(rows),'independentValidation':{'verdict':'unknown','failures':['missing_evidence']}}
 if rows and c['kind']=='official_model':report['independentValidation']=load(validators/'validate_food.py').validate_log(rows)
 if rows and c['kind']=='deterministic_fixture' and (d/'direct-driver-result.json').exists():report['independentValidation']=load(validators/'validate_tail.py' if c.get('tail') else d/'sources/experiments/direct-move-prefetch-evidence/validate.py').validate(rows,json.loads((d/'direct-driver-result.json').read_text()),c['id'] if c.get('tail') else c['fixture'])
 # The legacy per-request file is the sole token source. Never sum lifecycle duplicates.
 records=[r for r in api if isinstance(r.get('usage'),dict)]
 report['usage']={'knownInputTokens':sum(r['usage'].get('input_tokens',0) or 0 for r in records),'knownOutputTokens':sum(r['usage'].get('output_tokens',0) or 0 for r in records),'knownTotalTokens':sum(r['usage'].get('total_tokens',0) or 0 for r in records),'unknownUsageRecords':sum(r.get('usage') is None for r in api),'requestAttempts':sum(r.get('event')=='request_attempt' for r in events),'actualModels':sorted({r['actualModel'] for r in api if isinstance(r.get('actualModel'),str)}),'noAuditMeansUnknown':not bool(api),'cost':None}
 host_requests={r.get('requestId') for r in rows if r.get('kind')=='controller_request_published'}
 host_received={r.get('requestId') for r in rows if r.get('kind')=='controller_response_received'}
 report['genuineModelResponseObserved']=c['kind']=='official_model' and any(r.get('outcome')=='received' and r.get('actualModel') and r.get('requestId') in host_requests&host_received for r in api)
 token_values=[r.get('usage',{}).get('total_tokens') if isinstance(r.get('usage'),dict) else None for r in api]
 report['usage']['totalTokens']=sum(token_values) if token_values and all(type(v) is int and v>=0 for v in token_values) and len(api)>=report['usage']['requestAttempts'] and len({r.get('requestId') for r in api})==len(api) else None
 report['hostFoodClaim']=evidence[0].get('foodGoalVerified') if len(evidence)==1 else None
 report['foodEvidence']={k:evidence[0].get(k) for k in ['baselineInventory','actualInventory','ledger','meals','foodEvents','unknownReason']} if len(evidence)==1 else None
 report['requestToActionDiagnostics']=[{k:r.get(k) for k in ['candidateRequestId','monotonicSeconds','requestToNativeCallSeconds','replyObservedToNativeCallSeconds']} for r in rows if r.get('kind')=='native_call_started']
 report['prefetchDiagnostics']=[{k:r.get(k) for k in ['kind','requestId','precedingRunId','precedingKind','overlapSeconds','overlapPath','monotonicSeconds']} for r in rows if r.get('kind') in ['prefetch_requested','prefetch_response','prefetch_handoff_taken','prefetch_invalidated']]
 report['officialRequestMotionOverlap']=measured_overlap(rows,events) if c['kind']=='official_model' else []
 report['asyncCoverage']='independent_fixture_validator' if c['kind']=='deterministic_fixture' else 'see_identity_bound_request_motion_intervals_handoff_not_asserted'
 report['artifactHashes']={name:sha(d/name) for name in ['tool-agent.jsonl','api-audit.jsonl','api-audit.jsonl.events.jsonl','exit.txt','execution.json','direct-driver-result.json'] if (d/name).is_file()}
 return report

FATAL_MODEL_ERRORS={'network_error','redirect_rejected','destination_changed','response_bound','worker_failed',
 'worker_protocol','worker_output_bound','worker_input_bound','response_shape','response_not_completed',
 'model_refusal','decision_bound','decision_shape','goal_bound','steps_bound','step_shape',
 'duplicate_json_field','nonfinite_json','credential_echo_rejected','invalid_or_transport_response',
 'prior_request_requires_reconciliation','unverified_model_or_effort'}
def fatal_model_audit(directory):
 """Read only complete durable audit lines; a partial last write is not protocol failure."""
 path=directory/'api-audit.jsonl'
 if not path.exists():return None
 if safe(path).stat().st_size>64*1024*1024:return {'code':'audit_bound','requestId':None}
 raw=path.read_bytes();lines=raw.split(b'\n')[:-1]
 for line in lines:
  if not line.strip():continue
  try:record=json.loads(line)
  except (ValueError,UnicodeError):return {'code':'audit_protocol_invalid','requestId':None}
  if not isinstance(record,dict):return {'code':'audit_protocol_invalid','requestId':None}
  error=record.get('error')
  fatal=error in FATAL_MODEL_ERRORS if isinstance(error,str) else False
  fatal=fatal or (isinstance(error,str) and error in {'http_status_400','http_status_401','http_status_403','http_status_404','http_status_405','http_status_413','http_status_415','http_status_422'})
  if record.get('outcome')=='failed' and not isinstance(error,str):return {'code':'audit_protocol_invalid','requestId':None}
  if record.get('outcome')=='failed' and error not in FATAL_MODEL_ERRORS and error not in ('timeout','host_canceled') and not fatal:return {'code':'controller_unclassified_failure','requestId':None}
  if fatal:
   rid=record.get('requestId')
   try:rid=str(uuid.UUID(rid)) if isinstance(rid,str) else None
   except ValueError:rid=None
   return {'code':error,'requestId':rid}
 return None

def run_case(c,m,allow_model=False,model=None,effort=None):
 d=safe(c['directory']);official=c['kind']=='official_model'
 if official and (not allow_model or not model or not effort or not sys.stdin.isatty()):raise ValueError('explicit_model_effort_api_permission_and_terminal_required')
 if any((d/n).exists() for n in ['execution-started.json','execution.json','control','doc','Mods','exit.txt','tool-agent.jsonl','api-audit.jsonl']):raise FileExistsError('no_replay')
 # Fixed reviewed controller paths; never execute a command from a model or manifest.
 driver_path=d/'sources/experiments/openai-tool-controller/controller.py' if official else d/'sources/experiments/direct-move-prefetch-evidence/driver.py'
 if official:
  controller=load(driver_path)
  if model not in controller.VERIFIED_MODELS or effort not in controller.VERIFIED_MODELS[model]:raise ValueError('unverified_model_effort')
  command=[sys.executable,'-B',str(driver_path),'file','--control',str(d/'control'),'--audit',str(d/'api-audit.jsonl'),'--model',model,'--reasoning-effort',effort,'--max-requests','8','--total-seconds','180','--wait-seconds','60']
 else:
  if c.get('tail'):driver_path=d/'sources/experiments/integration-matrix/tail_driver.py'
  command=[sys.executable,'-B',str(driver_path),str(d),'--case',c['id'] if c.get('tail') else c['fixture']]
 helper=load(d/'sources/experiments/direct-move-prefetch-evidence/matrix_runner.py')
 write(d/'execution-started.json',{'case':c['id'],'monotonicSeconds':time.monotonic(),'automaticRetry':False})
 processes=[];logs=[];result={'case':c['id'],'hostExit':None,'driverExit':None,'drainCertain':True,'error':None,'interrupted':False,'stopRemainingModels':False};started=time.monotonic()
 try:
  log=(d/'driver-launch.log').open('x');logs.append(log)
  if official:print('Enter the official controller key using its hidden terminal prompt; never paste into this script or arguments.',flush=True)
  driver=subprocess.Popen(command,cwd=d,stdout=log,stderr=None if official else subprocess.STDOUT,start_new_session=True);processes.append(driver)
  if official:
   # Do not launch GUI until the controller's persisted ready event exists.
   while True:
    if driver.poll() is not None:raise RuntimeError('controller_exited_before_ready')
    try:ready=any(r.get('event')=='ready' for r in read_rows(d/'api-audit.jsonl.events.jsonl'))
    except json.JSONDecodeError:ready=False
    if ready:break
    if time.monotonic()-started>60:raise TimeoutError('controller_ready_timeout')
    time.sleep(.05)
  log=(d/'host-launch.log').open('x');logs.append(log)
  host=subprocess.Popen(['bash',str(d/'run.sh')],cwd=d,stdout=log,stderr=subprocess.STDOUT,start_new_session=True);processes.append(host)
  while any(p.poll() is None for p in processes):
   fatal=fatal_model_audit(d) if official else None
   if fatal is not None:
    result.update(error='fatal_model_failure',fatalModelFailure=fatal,stopRemainingModels=True);break
   if official and driver.poll() not in (None,0):
    result.update(error='controller_exited_nonzero',stopRemainingModels=True);break
   if time.monotonic()-started>=c['watchdogSeconds']:raise TimeoutError('case_watchdog')
   if host.poll() is not None and not (d/'exit.txt').exists():raise RuntimeError('host_exit_file_missing')
   time.sleep(.05)
  if official and result['error'] is None:
   fatal=fatal_model_audit(d)
   if fatal is not None:result.update(error='fatal_model_failure',fatalModelFailure=fatal,stopRemainingModels=True)
 except KeyboardInterrupt:result.update(error='KeyboardInterrupt',interrupted=True,stopRemainingModels=official)
 except Exception as e:result.update(error=type(e).__name__,stopRemainingModels=official)  # never exception text/key/http details
 finally:
  for p in reversed(processes):
   try:helper.terminate_owned(p)
   except KeyboardInterrupt:result.update(drainCertain=False,interrupted=True,error='KeyboardInterrupt',stopRemainingModels=official)
   except Exception:result['drainCertain']=False
  if processes:result['driverExit']=processes[0].returncode
  if len(processes)>1:result['hostExit']=processes[1].returncode
  for f in logs:f.close()
  result['elapsedSeconds']=time.monotonic()-started;write(d/'execution.json',result)
 return result

def run_group(m,path,selection,allow_model=False,model=None,effort=None,case_runner=None):
 case_runner=case_runner or run_case;path=safe(path);batch=path.parent
 chosen=[c for c in m['cases'] if selection in ['all',c['id']] or selection=='deterministic' and c['kind']=='deterministic_fixture' or selection=='models' and c['kind']=='official_model']
 report_path=batch/('run-'+selection+'-result.json');event_path=batch/('run-'+selection+'-events.jsonl')
 if report_path.exists() or event_path.exists():raise FileExistsError('fresh_group_required')
 outcomes=[];stop_reason=None;interrupted=False;active=None
 with event_path.open('x') as audit:
  def event(value):audit.write(json.dumps(value,allow_nan=False)+'\n');audit.flush();os.fsync(audit.fileno())
  event({'event':'group_started','selectedCases':[c['id'] for c in chosen],'automaticReplay':False})
  try:
   for c in chosen:
    active=c['id'];event({'event':'case_starting','case':active})
    result=case_runner(c,m,allow_model,model,effort)
    outcomes.append(result)  # Preserve completed ownership/drain evidence even if summary is interrupted.
    event({'event':'case_finished','result':result})
    try:result['independentVerdict']=summarize_case(c,path.parent)['independentValidation']['verdict']
    except Exception as error:result['independentVerdict']='unknown';result['validationErrorType']=type(error).__name__
    print(json.dumps(result),flush=True)
    if result.get('interrupted'):interrupted=True;stop_reason='interrupted';break
    if not result.get('drainCertain'):stop_reason='owned_process_drain_uncertain';break
    if result.get('stopRemainingModels'):stop_reason='fatal_model_failure';break
  except KeyboardInterrupt:
   interrupted=True;stop_reason='interrupted'
   if active and not any(r['case']==active for r in outcomes):outcomes.append({'case':active,'error':'KeyboardInterrupt','interrupted':True,'drainCertain':None,'outcomeUncertain':True})
  except Exception as error:stop_reason=type(error).__name__
  finally:
   report={'cases':outcomes,'automaticReplay':False,'interrupted':interrupted,'stopReason':stop_reason,
    'unstartedCases':[c['id'] for c in chosen if not any(r['case']==c['id'] for r in outcomes)]}
   write(report_path,report)
   event({'event':'group_ended','interrupted':interrupted,'stopReason':stop_reason})
 return report

def main():
 p=argparse.ArgumentParser(description=__doc__);sub=p.add_subparsers(dest='action',required=True)
 a=sub.add_parser('prepare');a.add_argument('--root',type=P,required=True);a.add_argument('--label',required=True);a.add_argument('--build',type=P,required=True);a.add_argument('--content',type=P,required=True);a.add_argument('--seed',action='append',required=True);a.add_argument('--expected-host-sha256',required=True)
 for name in ['run','summarize']:
  a=sub.add_parser(name);a.add_argument('--manifest',type=P,required=True)
  if name=='run':a.add_argument('--case',choices=['all','deterministic','models','food-0','food-1','food-2',*FIXTURES,*TAILS],required=True);a.add_argument('--allow-model-api',action='store_true');a.add_argument('--model');a.add_argument('--reasoning-effort')
  else:a.add_argument('--output',type=P,required=True);a.add_argument('--validator-directory',type=P)
 args=p.parse_args()
 if args.action=='prepare':print(prepare(args.root,args.label,args.build,args.content,args.seed,args.expected_host_sha256));return
 m=manifest(args.manifest)
 if args.action=='run':
  report=run_group(m,args.manifest,args.case,args.allow_model_api,args.model,args.reasoning_effort)
  outcomes=report['cases']
  raise SystemExit(0 if outcomes and not report['stopReason'] and not report['unstartedCases'] and all(r.get('hostExit')==0 and r.get('driverExit')==0 and r.get('error') is None and r.get('drainCertain') and r.get('independentVerdict')=='pass' for r in outcomes) else 1)
 else:write(args.output,{'schema':'integration-summary-v1','cases':[summarize_case(c,args.manifest.parent,args.validator_directory) for c in m['cases']],'posthocValidatorHashes':{name:sha(safe(args.validator_directory)/name) for name in ['validate_food.py','validate_tail.py']} if args.validator_directory else None,'originalManifestSha256':sha(args.manifest),'note':'Do not aggregate fixture success into genuine model or natural-food success; diagnostics are not independent async proof.'})
if __name__=='__main__':main()
