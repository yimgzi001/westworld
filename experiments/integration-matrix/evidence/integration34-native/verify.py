#!/usr/bin/env python3
"""解码全部引用并重新执行固定哈希的纯离线validator；不运行宿主/驱动/API。"""
import json,pathlib,re
import cas
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[3];m=json.loads((HERE/'manifest.json').read_text());refs={}
reachable={'manifest.json','README.md','archive.py','verify.py','cas.py','test_cas.py','supplement-hashes.json'}
for part in m['bundleIndexes']:
 raw=cas.safe(HERE,part['file']).read_bytes();assert cas.sha(raw)==part['sha256'];reachable.add(part['file']);new=json.loads(raw);assert not(refs.keys()&new.keys());refs.update(new)
for d in m['dependencies'].values():assert cas.sha(cas.safe(ROOT,d['manifest']).read_bytes())==d['sha256']
decoded={};shared=set();parts=0
for key,ref in refs.items():
 value,meta,files=cas.decode(ROOT,ref);assert key.endswith(meta['rawSHA256'][:20]);decoded[key]=value;parts+=len(meta['parts'])
 for p in files:
  if ref['storage']=='local':reachable.add(cas.safe(ROOT,p).relative_to(HERE).as_posix())
  else:shared.add(p)
def ev(ref):
 s=decoded[ref['bundle']];assert cas.sha(s.encode())==ref['archivedTextSHA256']
 assert str(ROOT) not in s and not re.search(r'/(?:home|Users)/[^/\s]+/|/usr/share/fonts/',s)
 if ref['pathRedactions']==0:assert ref['originalSHA256']==ref['archivedTextSHA256']
 return s
support={group:{p:ev(r) for p,r in decoded[key].items()} for group,key in m['support'].items()}
def get(path):return next(files[path] for files in support.values() if path in files)
initial=json.loads(get('work/integration34-integration/run-deterministic-result.json'));matrix=json.loads(get('work/integration34-integration/matrix.json'));post=json.loads(get('work/integration34-revalidation/revalidation-v2.json'))
assert post['originalManifestSha256']==decoded[m['support']['integration34-integration']]['work/integration34-integration/matrix.json']['originalSHA256']
assert len(initial['cases'])==6 and initial['automaticReplay'] is False
for p,digest in json.loads(get('work/integration34-integration/bundle-hashes.json')).items():
 assert decoded[m['support']['integration34-integration']]['work/integration34-integration/'+p]['originalSHA256']==digest
supplement=m['supplementSources'];supphash=json.loads((HERE/'supplement-hashes.json').read_text())
for path,ref in supplement.items():assert cas.sha(decoded[ref['bundle']].encode())==ref['sha256']==supphash[path]
v2meta=json.loads(get('work/integration34-revalidation/validator-and-test-hashes-v2-final.json'))
for name,digest in v2meta['hashes'].items():assert supplement['experiments/integration-matrix/'+name]['sha256']==digest
assert v2meta['testCount']==38 and v2meta['testsExit']==0 and v2meta['oldUnknownRetained'] and not v2meta['gamesRerun'] and not v2meta['oldFrozenChanged']
assert 'Ran 38 tests' in get('work/integration34-revalidation/tests-v2-final.log') and 'Ran 34 tests' in get('work/integration34-pure/tests.log')
def validator(source,expected):
 assert cas.sha(source.encode())==expected
 ns={'__name__':'frozen_offline_verifier'};exec(compile(source,'fixed-offline-verifier','exec'),ns);return ns['validate']
old_tail=validator(get('work/integration34-integration/frozen/validate_tail.py'),'078ea65d676800269297c82e82588529589967db89b440816b6e14a6e28cbd02')
new_tail=validator(decoded[supplement['experiments/integration-matrix/validate_tail.py']['bundle']],'db45e3dfd7a307cabf220f0c423b9810a630abc827d387bfb393ec0c7dbafd5d')
sourcechecks=hashlinks=binarylinks=0;results={};rawrows={};rawdriver={};runentries={};reference=None
for name,key in m['runs'].items():
 run=decoded[key];runentries[name]=run;logs={p:ev(ref) for p,ref in run['logs'].items()};source=decoded[run['sourceSnapshot']]
 assert len(source['inventory'])==len(source['files'])==source['count']==140
 if reference is None:reference=source['inventory']
 assert reference==source['inventory'] and json.loads(logs['source-hashes.json'])==reference
 assert run['logs']['source-hashes.json']['originalSHA256']=='439d9655af8310513d03cf3ab576eefbea03cf397153a5bfbbf8d013dc6af2b0'
 for p,digest in source['inventory'].items():assert cas.sha(decoded[source['files'][p]].encode())==digest;sourcechecks+=1
 cfg=next(c for c in matrix['cases'] if c['id']==name)
 assert run['logs']['source-hashes.json']['originalSHA256']==cfg['sourceSha256'] and run['logs']['installed-hashes.json']['originalSHA256']==cfg['installedSha256'];hashlinks+=2
 installed=json.loads(logs['installed-hashes.json'])
 for p,digest in run['verifiedInstalledBinaryHashes'].items():assert installed[p]==digest;binarylinks+=1
 assert installed['ToolAgentGate.dll']==matrix['expectedHostSha256']=='b862f2a423d5c5daffa95a15d7be22134d4dc9953bc8f11ddc3b8aed0926a7eb'
 execution=json.loads(logs['execution.json']);old=next(c for c in initial['cases'] if c['case']==name)
 assert {k:v for k,v in old.items() if k!='independentVerdict'}==execution and execution['drainCertain'] and execution['hostExit']==execution['driverExit']==int(logs['exit.txt'])==0
 rows=[json.loads(x) for x in logs['tool-agent.jsonl'].splitlines()];driver=json.loads(logs['direct-driver-result.json']);rawrows[name]=rows;rawdriver[name]=driver
 result=[x for x in rows if x.get('kind')=='result'];assert len(result)==1 and result[0]['passed'] and result[0]['updateFailures']==0
 current=next(c for c in post['cases'] if c['id']==name)
 for p,digest in current['artifactHashes'].items():assert run['logs'][p]['originalSHA256']==digest;hashlinks+=1
 if name.startswith('tail_'):
  old_result=old_tail(rows,driver,name);new_result=new_tail(rows,driver,name);assert old_result['verdict']==old['independentVerdict']=='unknown'
 else:
  direct=validator(decoded[source['files']['experiments/direct-move-prefetch-evidence/validate.py']],'eb6b463e7b403e926cb0ca51391b0b689e48e286b907d3507c44228a7050a9f0')
  new_result=direct(rows,driver,name);assert old['independentVerdict']=='pass'
 assert new_result==current['independentValidation'] and new_result['verdict']=='pass';results[name]={'original':old['independentVerdict'],'revalidated':'pass','hostSeconds':result[0]['seconds']}
# 真实回归夹具只允许从对应原行删字段，不能加入虚构事实。
def subset(a,b):
 if isinstance(a,dict):return isinstance(b,dict) and all(k in b and subset(v,b[k]) for k,v in a.items())
 if isinstance(a,list):return isinstance(b,list) and len(a)==len(b) and all(subset(x,y) for x,y in zip(a,b))
 return a==b
fixture_rows=0
for name in ('tail_early','tail_late','tail_failure'):
 path='experiments/integration-matrix/fixtures/tail_actual34_'+name.removeprefix('tail_')+'.json';fixture=json.loads(decoded[supplement[path]['bundle']])
 assert fixture['sourceLogSha256']==runentries[name]['logs']['tool-agent.jsonl']['originalSHA256'] and fixture['sourceDriverSha256']==runentries[name]['logs']['direct-driver-result.json']['originalSHA256']
 for row in fixture['rows']:
  original=rawrows[name][row['originalLogLine']-1];assert subset({k:v for k,v in row.items() if k!='originalLogLine'},original);fixture_rows+=1
build=json.loads(get('work/integration34-unified/manifest.json'));assert build['buildExit']==build['contractsBaseRunExit']==0 and build['sourceFiles']==1784 and not build['sourceChangedDuringBuild'] and build['contractSuites']==36
for p,digest in build['binaries'].items():assert digest==next(iter(runentries.values()))['verifiedInstalledBinaryHashes'][p]
counts={}
for p,text in support['integration34-unified'].items():
 if p.endswith('.test.log'):
  last=text.strip().splitlines()[-1];a=re.search(r'(\d+)/(\d+)',last) or re.search(r'TOTAL (\d+)',last) or re.search(r'PASS (\d+)',last);assert a
  if len(a.groups())==2:assert a[1]==a[2]
  counts[p]=int(a[1])
assert len(counts)==36
used=set()
def visit(x):
 if isinstance(x,dict):
  for v in x.values():visit(v)
 elif isinstance(x,list):
  for v in x:visit(v)
 elif isinstance(x,str) and x in decoded and x not in used:used.add(x);visit(decoded[x])
visit(m);assert used==decoded.keys();orphans=[p.name for p in HERE.iterdir() if p.is_file() and p.name not in reachable];assert not orphans and m['archiveComplete']
print(json.dumps({'runs':results,'referencedBundles':len(refs),'newBundles':sum(r['storage']=='local' for r in refs.values()),'reusedBundles':sum(r['storage']=='shared' for r in refs.values()),'referencedParts':parts,'sourceHashChecks':sourcechecks,'matrixAndRevalidationHashLinks':hashlinks,'installedHashLinks':binarylinks,'realFixtureRowsChecked':fixture_rows,'contractSuites':len(counts),'contractChecks':sum(counts.values()),'p5OriginalTests':34,'p5FinalTests':38,'localFiles':len(reachable),'localBytes':sum((HERE/n).stat().st_size for n in reachable),'largestLocalFileBytes':max((HERE/n).stat().st_size for n in reachable),'orphanFiles':orphans},ensure_ascii=False))
