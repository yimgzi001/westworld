#!/usr/bin/env python3
"""独立解码核验；只重算固定哈希的纯离线validate_log，不运行驱动、宿主或API。"""
import collections,json,pathlib,re
import cas
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[3]
m=json.loads((HERE/'manifest.json').read_text());refs={};reachable={'manifest.json','README.md','ARCHIVE-PLAN.md','archive.py','verify.py','cas.py','test_cas.py'}
for item in m['bundleIndexes']:
 raw=cas.safe(HERE,item['file']).read_bytes();assert cas.sha(raw)==item['sha256'];reachable.add(item['file']);part=json.loads(raw)
 assert not(refs.keys()&part.keys());refs.update(part)
for dependency in m['dependencies'].values():assert cas.sha(cas.safe(ROOT,dependency['manifest']).read_bytes())==dependency['sha256']
decoded={};shared_files=set();parts=0
for key,ref in refs.items():
 value,meta,files=cas.decode(ROOT,ref);assert key.endswith(meta['rawSHA256'][:20]);decoded[key]=value;parts+=len(meta['parts'])
 for relative in files:
  path=cas.safe(ROOT,relative)
  if ref['storage']=='local':reachable.add(path.relative_to(HERE).as_posix())
  else:shared_files.add(relative)
def evidence(ref):
 text=decoded[ref['bundle']];assert cas.sha(text.encode())==ref['archivedTextSHA256'] and '/workspace/scratch/' not in text
 if ref['pathRedactions']==0:assert ref['originalSHA256']==ref['archivedTextSHA256']
 return text
support=decoded[m['supportEvidence']]
finished=json.loads(evidence(support['batchEvidence']['work/natural31-batch/finished.json']))
source_checks=checkpoint_checks=binary_checks=0;source_reference=None;results={}
for name,ref in m['runs'].items():
 run=decoded[ref['bundle']];summary=run['summary'];logs={path:evidence(value) for path,value in run['logs'].items()}
 source=decoded[run['sourceSnapshot']];assert len(source['inventory'])==len(source['files'])==source['count']==133
 if source_reference is None:source_reference=source['inventory']
 assert source_reference==source['inventory']
 for path,digest in source['inventory'].items():
  relative=pathlib.PurePosixPath(path);assert not relative.is_absolute() and '..' not in relative.parts
  assert cas.sha(decoded[source['files'][path]].encode())==digest;source_checks+=1
 assert json.loads(logs['source-hashes.json'])==source['inventory']
 assert run['logs']['source-hashes.json']['originalSHA256']=='1cfe2b413018483ffab19fa12b3c576ed3242766fc2228c8a619873617531e4d'
 assert cas.sha(logs['deterministic_driver.py'].encode())==source['inventory']['experiments/natural-resource-evidence/deterministic_driver.py']
 rows=[json.loads(line) for line in logs['tool-agent.jsonl'].splitlines() if line.strip()]
 analysis=json.loads(logs['analysis.json']);validation=cas.validate_frozen(decoded[source['files']['experiments/natural-resource-evidence/validate.py']],rows)
 assert validation==analysis['validation']==summary['validation'];assert validation['verdict']==ref['verdict']==m['expectedVerdicts'][name]
 assert [row for row in rows if row.get('kind')=='result']==[summary['hostResult']]==[analysis['host']]
 events=[row for row in rows if row.get('kind')=='natural_resource_evidence'];assert len(events)==1 and events[0]==analysis['evidence'];e=events[0]
 assert dict(collections.Counter(row.get('kind') for row in rows))==summary['eventCounts']
 assert summary['actualSeed']==e['seed']==json.loads(logs['stage.json'])['seed']
 assert e['aiViewportCount']==0 and e['actorGameWidgetNull'] is True and all(value is False for value in e['fixture'].values())
 assert summary['hostResult']['updateFailures']==0
 assert int(logs['exit.txt'])==summary['hostExit'] and int(logs['driver-exit.txt'])==summary['driverExit']==0
 driver=json.loads(logs['natural-driver-result.json']);assert driver['kind']=='deterministic_natural_resource_test_not_model' and driver['error'] is None
 batch=next(item for item in finished if item['case']==name)
 assert batch['result']==summary['hostResult'] and batch['hostExit']==summary['hostExit'] and batch['driverExit']==0
 cp=json.loads(evidence(run['checkpoint']));assert cp['finishedUtc']
 for path,digest in(cp['sources']|cp['results']).items():
  assert run['logs'][pathlib.PurePosixPath(path).name]['originalSHA256']==digest;checkpoint_checks+=1
 installed=json.loads(logs['installed-hashes.json'])
 for path,digest in run['verifiedInstalledBinaryHashes'].items():assert installed[path]==digest;binary_checks+=1
 assert installed['ToolAgentGate.dll']=='28ab28c57709e6507ef52440402b7b78249ecf7de46bf7184206aa71b5f3c71f'
 if name in('natural31-a','natural31-b'):
  assert summary['hostExit']==1 and summary['hostResult']['reason']=='model_round_budget_exhausted'
  assert summary['eventCounts'].get('native_call_started',0)==0 and driver['responses']==8
  assert all(not item['steps'] for item in driver['events'])
  assert not e['origins'] and not e['transfers'] and e['ledger']['Reason']=='awaiting_origin'
 else:
  assert summary['hostExit']==0 and summary['hostResult']['passed'] is True
  assert e['seed']=='tool-agent-20261109' and len(e['origins'])==len(e['transfers'])==1
  assert e['origins'][0]['fact']['Value']==2 and e['origins'][0]['fact']['Count']==1
  assert e['transfers'][0]['fact']['InventoryBefore']==0 and e['transfers'][0]['fact']['InventoryAfter']==1
  assert e['actualInventory']['2']==1 and not e['ledger']['Consumed']
 results[name]=validation['verdict']
for group in('finalEvidence','batchEvidence','preliminaryEvidence'):
 for ref in support[group].values():evidence(ref)
for path,ref in support['supplementalTestSources'].items():assert cas.sha(decoded[ref['bundle']].encode())==ref['sha256']
assert evidence(support['finalEvidence']['work/natural31-ledger-final-test.log']).strip()=='PASS 47'
assert 'Ran 46 tests' in evidence(support['finalEvidence']['work/natural31-python-tests.log'])
assert '0 Error(s)' in evidence(support['finalEvidence']['work/natural31-build-final.log'])
counts=support['preliminaryContractCounts'];assert len(counts)==28 and sum(counts.values())==1094
for filename,count in counts.items():
 last=evidence(support['preliminaryEvidence']['work/natural31-validation/contracts/'+filename]).strip().splitlines()[-1]
 match=re.search(r'(\d+)/(\d+)',last) or re.search(r'TOTAL (\d+)',last) or re.search(r'PASS (\d+) endpoint',last)
 assert match and int(match[1])==count
initial=json.loads(evidence(support['preliminaryEvidence']['work/natural31-validation/result.json']))
assert initial['ledger']['tests']==23 and initial['origin']['tests']==21
assert initial['dllHashes']['work/natural31-build/bin/ToolAgentGate/debug/ToolAgentGate.dll']!='28ab28c57709e6507ef52440402b7b78249ecf7de46bf7184206aa71b5f3c71f'
used=set()
def visit(value):
 if isinstance(value,dict):
  for item in value.values():visit(item)
 elif isinstance(value,list):
  for item in value:visit(item)
 elif isinstance(value,str) and value in decoded and value not in used:used.add(value);visit(decoded[value])
visit(m);assert used==decoded.keys()
orphans=[p.name for p in HERE.iterdir() if p.is_file() and p.name not in reachable];assert not orphans
assert m['archiveComplete'] is True
print(json.dumps({'runs':3,'verdicts':results,'referencedBundles':len(refs),'newBundles':sum(ref['storage']=='local' for ref in refs.values()),'reusedBundles':sum(ref['storage']=='shared' for ref in refs.values()),'referencedParts':parts,'localFiles':len(reachable),'sharedDependencyFiles':len(shared_files),'sourceHashChecks':source_checks,'checkpointHashChecks':checkpoint_checks,'installedHashLinks':binary_checks,'finalLedgerTests':47,'finalPythonTests':46,'preliminaryContractSuites':28,'preliminaryContractAssertions':1094,'orphanFiles':orphans,'localBytes':sum((HERE/name).stat().st_size for name in reachable),'largestLocalFileBytes':max((HERE/name).stat().st_size for name in reachable)},ensure_ascii=False))
