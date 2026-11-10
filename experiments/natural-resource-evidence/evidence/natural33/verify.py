#!/usr/bin/env python3
"""独立解码和原始receipt/API用量关联核验；只运行固定哈希的纯离线验证器。"""
import json,pathlib,collections
import cas
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[3]
m=json.loads((HERE/'manifest.json').read_text());refs={};reachable={'manifest.json','README.md','archive.py','verify.py','cas.py','test_cas.py'}
for item in m['bundleIndexes']:
 raw=cas.safe(HERE,item['file']).read_bytes();assert cas.sha(raw)==item['sha256'];reachable.add(item['file']);refs.update(json.loads(raw))
for dep in m['dependencies'].values():assert cas.sha(cas.safe(ROOT,dep['manifest']).read_bytes())==dep['sha256']
decoded={};shared=set();parts=0
for key,ref in refs.items():
 value,meta,files=cas.decode(ROOT,ref);assert key.endswith(meta['rawSHA256'][:20]);decoded[key]=value;parts+=len(meta['parts'])
 for name in files:
  if ref['storage']=='local':reachable.add(cas.safe(ROOT,name).relative_to(HERE).as_posix())
  else:shared.add(name)
def evidence(ref):
 text=decoded[ref['bundle']];assert cas.sha(text.encode())==ref['archivedTextSHA256'] and '/workspace/scratch/' not in text
 if ref['pathRedactions']==0:assert ref['originalSHA256']==ref['archivedTextSHA256']
 return text
logs={name:evidence(ref) for name,ref in m['logs'].items()};source=decoded[m['sourceSnapshot']]
assert len(source['inventory'])==len(source['files'])==source['count']==133
for path,digest in source['inventory'].items():assert cas.sha(decoded[source['files'][path]].encode())==digest
assert json.loads(logs['source-hashes.json'])==source['inventory'] and m['logs']['source-hashes.json']['originalSHA256']=='2c8a0fd6a4f9de948eaef3f8bbd22e9875c29a6da892cea7ac3dcab0a7c87e21'
cp=json.loads(evidence(m['checkpoint']));assert cp['finishedUtc'] and cp['state']=='passed'
for path,digest in (cp['sources']|cp['results']).items():assert m['logs'][pathlib.PurePosixPath(path).name]['originalSHA256']==digest
installed=json.loads(logs['installed-hashes.json'])
for name,digest in m['verifiedInstalledBinaryHashes'].items():assert installed[name]==digest
assert installed['ToolAgentGate.dll']=='53da6b020b9a3f1a26e1161abd506edeb0040cf63e1d2aed403e1f0463b82ff0'
build=json.loads(evidence(m['buildEvidence']['work/natural32-validation/result.json']));assert build['dllHashes']['work/natural32-build/bin/ToolAgentGate/debug/ToolAgentGate.dll']==installed['ToolAgentGate.dll'] and build['hostExit']==0
assert '0 Error(s)' in evidence(m['buildEvidence']['work/natural32-validation/host-build.log'])
rows=[json.loads(x) for x in logs['tool-agent.jsonl'].splitlines()];analysis=json.loads(logs['analysis.json']);audit=[json.loads(x) for x in logs['openai-audit.jsonl'].splitlines()];events=[json.loads(x) for x in logs['openai-audit.jsonl.events.jsonl'].splitlines()]
validation=cas.validate_frozen(decoded[source['files']['experiments/natural-resource-evidence/validate.py']],rows);assert validation==analysis['validation'] and validation['verdict']=='pass'
assert [x for x in rows if x['kind']=='result']==[analysis['host']] and analysis['host']['passed'] and analysis['host']['updateFailures']==0 and int(logs['exit.txt'])==0
assert [x for x in rows if x['kind']=='natural_resource_evidence']==[analysis['evidence']];e=analysis['evidence']
assert e['aiViewportCount']==0 and e['actorGameWidgetNull'] is True and not any(e['fixture'].values())
assert len(e['origins'])==2 and len(e['transfers'])==1 and e['actualInventory']=={'6':1,'162':1} and e['transfers'][0]['fact']['Method']=='automatic' and not e['ledger']['Consumed']
assert sorted((x['Value'],x['Acquired']) for x in e['ledger']['Sources'])==[(6,1),(79,0)]
final=next(x for x in rows if x['kind']=='final_audit');assert final['prefetchTaken']==0 and final['overlapSeconds']==0 and final['eaten']==0
receipts=[x['receipt'] for x in rows if x['kind']=='tool_receipt'];terminals=[x for x in rows if x['kind']=='native_terminal_observed']
assert [x['tool'] for x in terminals]==['act_Dig','act_Dig','act_MoveTo']
assert [x['step']['Tool'] for x in rows if x['kind']=='native_call_started']==['act_Dig','act_Dig','act_MoveTo']
assert not any(r['Tool']=='act_PickUp' for r in receipts)
for t in terminals:
 start=next(r for r in receipts if r['Sequence']==t['sequence'] and r['Tool']==t['tool']);assert start['Data']['runId']==t['runId'] and t['status']=='succeeded'
 assert any(r['Tool']=='action_status' and r['Sequence']>start['Sequence'] and r.get('Data',{}).get('runId')==t['runId'] and r['Data'].get('status')=='succeeded' and r['Error'] is None for r in receipts)
assert len(audit)==3 and [x['sequence'] for x in events]==list(range(1,14)) and events[-1]['event']=='ended' and events[-1]['status']=='completed'
requests={json.loads(text)['requestId']:json.loads(text) for name,text in logs.items() if name.endswith('/request.json')};assert len(requests)==3
for a in audit:
 assert a['outcome']=='received' and a['requestedModel']==a['actualModel']=='gpt-6.1-terra' and a['effort']=='none' and a['usage']['output_tokens_details']['reasoning_tokens']==0
 assert a['driverSourceSha256']==source['inventory']['experiments/openai-tool-controller/controller.py']
 request=requests[a['requestId']]
 for key in ('sessionId','actorId','observationId'):assert request[key]==a[key]
 assert cas.sha(request['prompt'].encode())==a['promptSha256']
 assert cas.sha(json.dumps(request['responseSchema'],ensure_ascii=False,separators=(',',':')).encode())==a['schemaSha256']
 assert json.loads(a['rawDecision'])==a['decision']
 matches=[x for x in events if x.get('requestId')==a['requestId']];assert [x['event'] for x in matches]==['request_attempt','request_result','response_published']
 assert matches[0]['promptSha256']==a['promptSha256'] and matches[0]['schemaSha256']==a['schemaSha256'] and matches[1]['usage']==a['usage'] and matches[1]['error'] is None
 host=next(x for x in rows if x['kind']=='controller_response_received' and x['requestId']==a['requestId'])
 assert host['decision']['Goal']==a['decision']['goal'] and [{'tool':x['Tool'],'arguments':x['Arguments']} for x in host['decision']['Steps']]==a['decision']['steps']
 for key in ('sessionId','actorId','observationId'):assert host['decision'][key[0].upper()+key[1:]]==a[key]
 assert a['usage']['input_tokens']+a['usage']['output_tokens']==a['usage']['total_tokens']
assert sum(a['usage']['total_tokens'] for a in audit)==analysis['totalTokens']==28187
assert json.loads(logs['stage.json'])['controllerKind']=='model'
used=set()
def visit(value):
 if isinstance(value,dict):
  for item in value.values():visit(item)
 elif isinstance(value,list):
  for item in value:visit(item)
 elif isinstance(value,str) and value in decoded and value not in used:used.add(value);visit(decoded[value])
visit(m);assert used==decoded.keys();orphans=[p.name for p in HERE.iterdir() if p.is_file() and p.name not in reachable];assert not orphans and m['archiveComplete']
print(json.dumps({'case':m['case'],'verdict':validation['verdict'],'sourceHashChecks':133,'checkpointHashChecks':len(cp['sources'])+len(cp['results']),'installedHashLinks':5,'apiRequests':3,'totalTokens':28187,'reasoningTokens':0,'rawSuccessfulActionChains':3,'referencedBundles':len(refs),'newBundles':sum(r['storage']=='local' for r in refs.values()),'reusedBundles':sum(r['storage']=='shared' for r in refs.values()),'referencedParts':parts,'localFiles':len(reachable),'sharedDependencyFiles':len(shared),'localBytes':sum((HERE/n).stat().st_size for n in reachable),'largestLocalFileBytes':max((HERE/n).stat().st_size for n in reachable),'orphanFiles':orphans},ensure_ascii=False))
