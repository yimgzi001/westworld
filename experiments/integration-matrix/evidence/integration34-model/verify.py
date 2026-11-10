#!/usr/bin/env python3
"""离线核验冻结来源、API身份/用量、原始动作与独立摘要；绝不运行驱动/游戏/API。"""
import ast,json,math,pathlib,re
import cas
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[3];m=json.loads((HERE/'manifest.json').read_text());refs={};reachable={'manifest.json','README.md','archive.py','verify.py','cas.py','test_cas.py','supplement-hashes.json'}
for part in m['bundleIndexes']:
 raw=cas.safe(HERE,part['file']).read_bytes();assert cas.sha(raw)==part['sha256'];reachable.add(part['file']);p=json.loads(raw);assert not(refs.keys()&p.keys());refs.update(p)
for d in m['dependencies'].values():assert cas.sha(cas.safe(ROOT,d['manifest']).read_bytes())==d['sha256']
decoded={};parts=0;shared=set()
for key,ref in refs.items():
 v,meta,files=cas.decode(ROOT,ref);assert key.endswith(meta['rawSHA256'][:20]);decoded[key]=v;parts+=len(meta['parts'])
 for p in files:
  if ref['storage']=='local':reachable.add(cas.safe(ROOT,p).relative_to(HERE).as_posix())
  else:shared.add(p)
def ev(ref):
 s=decoded[ref['bundle']];assert cas.sha(s.encode())==ref['archivedTextSHA256'] and str(ROOT) not in s and not re.search(r'/(?:home|Users)/[^/\s]+/|/usr/share/fonts/',s)
 if ref['pathRedactions']==0:assert ref['originalSHA256']==ref['archivedTextSHA256']
 return s
support={p:ev(r) for p,r in m['support'].items()};summary=json.loads(support['work/integration34-revalidation/model-batch-independent-v2.json']);matrix=json.loads(support['work/integration34-integration/matrix.json'])
assert m['support']['work/integration34-revalidation/model-batch-independent-v2.json']['originalSHA256']=='9f209b7fbecf2b54bcec576a6124801c86ed3c7cc32f0710e1cc8e50d4ef5079'
assert summary['originalManifestSha256']==m['support']['work/integration34-integration/matrix.json']['originalSHA256']
supp=m['supplementSources'];fixed=json.loads((HERE/'supplement-hashes.json').read_text())
for p,r in supp.items():assert cas.sha(decoded[r['bundle']].encode())==r['sha256']==fixed[p]
for line in support['work/integration34-failfast/source-sha256.txt'].splitlines():
 digest,p=line.split(None,1);assert supp[p.strip()]['sha256']==digest
assert int(support['work/integration34-failfast/exit.txt'])==0 and 'Ran 46 tests' in support['work/integration34-failfast/tests.log']
fix=json.loads(support['work/integration34-failfast/result.json']);assert fix['tests']==46 and fix['networkCalls']==fix['gameRuns']==0 and not fix['oldFrozenChanged'] and not fix['oldRequestsReplayed']
for name,digest in summary['validatorHashes'].items():assert supp['experiments/integration-matrix/'+name]['sha256']==digest
food_source=decoded[supp['experiments/integration-matrix/validate_food.py']['bundle']];assert cas.sha(food_source.encode())=='fa0db52bfa1de0951b62d46cdc217740e2c8af51426ce8b0a3b7b378d127d358'
ns={'__name__':'fixed_offline_food_validator'};exec(compile(food_source,'fixed-food-validator','exec'),ns)
batch_source=decoded[supp['experiments/integration-matrix/batch.py']['bundle']];assert cas.sha(batch_source.encode())=='e5d1015d8584306f626639885296c81bdfd9474c96d35edee02b9d76e4e760c3'
function=next(x for x in ast.parse(batch_source).body if isinstance(x,ast.FunctionDef) and x.name=='measured_overlap');ms={'math':math};exec(compile(ast.Module(body=[function],type_ignores=[]),'fixed-pure-motion-metric','exec'),ms)
sourcechecks=hashlinks=binarylinks=attempts=api_received=host_received=known=unknown=0;results={};source_reference=None
for name,key in m['runs'].items():
 run=decoded[key];logs={p:ev(r) for p,r in run['logs'].items()};source=decoded[run['sourceSnapshot']];assert source['count']==len(source['inventory'])==len(source['files'])==149
 if source_reference is None:source_reference=source['inventory']
 assert source_reference==source['inventory']==json.loads(logs['source-hashes.json'])
 for p,digest in source['inventory'].items():assert cas.sha(decoded[source['files'][p]].encode())==digest;sourcechecks+=1
 cfg=next(c for c in matrix['cases'] if c['id']==name)
 assert run['logs']['source-hashes.json']['originalSHA256']==cfg['sourceSha256']=='be471c7923419a415d5221d4468dfaae5584efd56c3817b80e669ffad97fad3a'
 assert run['logs']['installed-hashes.json']['originalSHA256']==cfg['installedSha256'];hashlinks+=2
 installed=json.loads(logs['installed-hashes.json'])
 for p,digest in run['verifiedInstalledBinaryHashes'].items():assert installed[p]==digest;binarylinks+=1
 assert installed['ToolAgentGate.dll']==matrix['expectedHostSha256']=='b862f2a423d5c5daffa95a15d7be22134d4dc9953bc8f11ddc3b8aed0926a7eb'
 for p in ('run.sh','stage.json'):assert run['logs'][p]['originalSHA256']==installed[p];hashlinks+=1
 rows=[json.loads(x) for x in logs['tool-agent.jsonl'].splitlines()];audit=[json.loads(x) for x in logs['api-audit.jsonl'].splitlines()];events=[json.loads(x) for x in logs['api-audit.jsonl.events.jsonl'].splitlines()]
 report=summary[name];validation=ns['validate_log'](rows);assert validation==report['foodValidation']
 for p,digest in report['artifactHashes'].items():assert run['logs'][p]['originalSHA256']==digest;hashlinks+=1
 requests={json.loads(text)['requestId']:json.loads(text) for p,text in logs.items() if p.endswith('/request.json')};assert len(requests)==len(audit)==len({a['requestId'] for a in audit})
 attempts_here=[e for e in events if e['event']=='request_attempt'];assert len(attempts_here)==len(audit)
 responses={r['requestId']:r for r in rows if r.get('kind')=='controller_response_received'}
 received_here=sum(a['outcome']=='received' for a in audit);known_here=sum((a.get('usage') or {}).get('total_tokens',0) for a in audit);unknown_here=sum(a.get('usage') is None for a in audit)
 for a in audit:
  assert a['requestedModel']=='gpt-6.1-terra' and a['effort']=='none' and a['driverSourceSha256']==source['inventory']['experiments/openai-tool-controller/controller.py']
  req=requests[a['requestId']]
  for k in ('sessionId','actorId','observationId'):assert a[k]==req[k]
  assert cas.sha(req['prompt'].encode())==a['promptSha256'] and cas.sha(json.dumps(req['responseSchema'],ensure_ascii=False,separators=(',',':')).encode())==a['schemaSha256']
  attempt=next(e for e in attempts_here if e['requestId']==a['requestId']);result=next(e for e in events if e['event']=='request_result' and e['requestId']==a['requestId'])
  assert attempt['promptSha256']==a['promptSha256'] and attempt['schemaSha256']==a['schemaSha256'] and result.get('usage')==a.get('usage')
  for k in ('sessionId','actorId','observationId'):assert attempt[k]==result[k]==a[k]
  if a.get('usage') is not None:
   assert a['actualModel']=='gpt-6.1-terra' and a['usage']['output_tokens_details']['reasoning_tokens']==0
   assert a['usage']['input_tokens']+a['usage']['output_tokens']==a['usage']['total_tokens']
  if a['requestId'] in responses:
   assert a['outcome']=='received';h=responses[a['requestId']]['decision'];assert h['Goal']==a['decision']['goal'] and [{'tool':x['Tool'],'arguments':x['Arguments']} for x in h['Steps']]==a['decision']['steps']
   for k in ('sessionId','actorId','observationId'):assert h[k[0].upper()+k[1:]]==a[k]
 if name=='food-0':
  assert len(audit)==2 and received_here==len(responses)==0 and unknown_here==2 and all(a.get('error')=='network_error' for a in audit)
  execution=json.loads(logs['execution.json']);assert execution['hostExit']==execution['driverExit']==-15 and execution['drainCertain'] and execution['error'] is None
  assert 'exit.txt' not in logs and not any(r.get('kind') in ('native_call_started','result','natural_resource_evidence') for r in rows)
  assert report['totalTokens'] is None and report['classification']=='network_failure_interrupted_incomplete_audit'
 else:
  assert len(audit)==8 and int(logs['exit.txt'])==1 and events[-1]['event']=='ended' and events[-1]['status']=='completed'
  result=next(r for r in rows if r['kind']=='result');assert result==report['hostResult'] and result['reason']=='model_round_budget_exhausted' and result['updateFailures']==0
  evidence=next(r for r in rows if r['kind']=='natural_resource_evidence');assert not any(evidence['fixture'].values()) and evidence['aiViewportCount']==0 and evidence['actorGameWidgetNull'] and not evidence['foodEvents'] and not evidence['meals']
  assert not any(r.get('kind')=='native_call_started' and r.get('step',{}).get('Tool')=='act_Eat' for r in rows)
  assert evidence['shutdown']['idle'] and evidence['shutdown']['lateDispatches']==0
  assert report['usage']['knownTotalTokens']==known_here and report['usage']['unknownUsageRecords']==unknown_here
  overlap=ms['measured_overlap'](rows,events);assert len(overlap)==len(report['motionOverlap'])
  for a,b in zip(overlap,report['motionOverlap']):assert all(b.get(k)==v for k,v in a.items())
  if name=='food-1':
   assert known_here==89744 and unknown_here==0 and received_here==len(responses)==8 and evidence['actualInventory']=={'162':1,'226':1}
  else:
   assert known_here==59248 and unknown_here==2 and received_here==6 and len(responses)==5 and report['usage']['totalTokens'] is None
   assert all(a.get('error')=='host_canceled' for a in audit if a.get('usage') is None) and not evidence['origins'] and not evidence['transfers']
 attempts+=len(audit);api_received+=received_here;host_received+=len(responses);known+=known_here;unknown+=unknown_here
 results[name]={'classification':report['classification'],'validatorVerdict':validation['verdict'],'attempts':len(audit),'apiReceived':received_here,'hostReceived':len(responses),'knownTokens':known_here,'unknownUsageRequests':unknown_here,'totalTokens':None if unknown_here else known_here}
assert (attempts,api_received,host_received,known,unknown)==(18,14,13,148992,4)
assert summary['combinedUsage']['knownTokens']==known and summary['combinedUsage']['unknownUsageRequests']==unknown and summary['combinedUsage']['totalTokens'] is None and summary['combinedUsage']['cost'] is None
used=set()
def visit(x):
 if isinstance(x,dict):
  for v in x.values():visit(v)
 elif isinstance(x,list):
  for v in x:visit(v)
 elif isinstance(x,str) and x in decoded and x not in used:used.add(x);visit(decoded[x])
visit(m);assert used==decoded.keys();orphans=[p.name for p in HERE.iterdir() if p.is_file() and p.name not in reachable];assert not orphans and m['archiveComplete']
print(json.dumps({'runs':results,'sourceHashChecks':sourcechecks,'matrixAndArtifactHashLinks':hashlinks,'installedHashLinks':binarylinks,'apiAttempts':attempts,'apiReceived':api_received,'hostReceived':host_received,'knownTokens':known,'unknownUsageRequests':unknown,'totalTokens':None,'cost':None,'failfastOfflineTests':46,'referencedBundles':len(refs),'newBundles':sum(r['storage']=='local' for r in refs.values()),'reusedBundles':sum(r['storage']=='shared' for r in refs.values()),'referencedParts':parts,'localFiles':len(reachable),'localBytes':sum((HERE/n).stat().st_size for n in reachable),'largestLocalFileBytes':max((HERE/n).stat().st_size for n in reachable),'orphanFiles':orphans},ensure_ascii=False))
