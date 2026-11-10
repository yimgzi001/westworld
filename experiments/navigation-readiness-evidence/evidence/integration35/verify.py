#!/usr/bin/env python3
"""明文包哈希与纯离线验收；无网络、无游戏入口。"""
import argparse,ast,collections,hashlib,json,math,pathlib,re,types,uuid
from archive import SECRET,sha
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[3]
ap=argparse.ArgumentParser();ap.add_argument('--check-sources',action='store_true');args=ap.parse_args();m=json.loads((HERE/'manifest.json').read_text())
contents={};allowed={'manifest.json','README.md','archive.py','verify.py'}
for name,meta in m['files'].items():
 p=HERE/name;assert not p.is_symlink();raw=p.read_bytes();assert sha(raw)==meta['archivedSHA256'] and len(raw)==meta['bytes'];text=raw.decode();assert not SECRET.search(text)
 assert str(ROOT) not in text and not re.search(r'/(?:home|Users)/[^/\s]+/|/usr/share/fonts/',text)
 if meta['pathRedactions']==0:assert meta['originalSHA256']==meta['archivedSHA256']
 contents[name]=text;allowed.add(name)
actual={p.relative_to(HERE).as_posix() for p in HERE.rglob('*') if p.is_file() and '__pycache__' not in p.parts};assert actual==allowed
sourcechecks=links=0
for label,run in m['runs'].items():
 inv=json.loads(contents[label+'/source-hashes.json']);assert len(inv)==run['sourceFilesVerified'];sourcechecks+=len(inv)
 build=json.loads(contents['pure/source-before.json']);joined={p:h for p,h in inv.items() if p in build};assert len(joined)==run['buildInventoryLinks'] and all(build[p]==h for p,h in joined.items());links+=len(joined)
 installed=json.loads(contents[label+'/installed-hashes.json'])
 for p,h in run['installedHashesVerified'].items():assert installed[p]==h
 assert installed['ToolAgentGate.dll']==contents['pure/host-sha.txt'].strip()
 for p,name in run['sourceOverrides'].items():assert sha(contents[name].encode())==inv[p]
 if args.check_sources:
  for p,h in inv.items():
   actual_source=contents[run['sourceOverrides'][p]].encode() if p in run['sourceOverrides'] else (ROOT/p).read_bytes();assert sha(actual_source)==h,p
if args.check_sources:assert sha((ROOT/m['pipelineFixture']['repositoryPath']).read_bytes())==m['pipelineFixture']['sha256']
assert int(contents['pure/contracts.exit'])==int(contents['pure/host-build.exit'])==0 and 'Ran 14 tests' in contents['pure/stage-tests.log']
counts={}
for p,text in contents.items():
 if p.startswith('pure/contracts/'):
  last=text.strip().splitlines()[-1];match=re.search(r'(\d+)/(\d+)',last) or re.search(r'(?:TOTAL|FINAL|PASS) (\d+)',last);assert match,(p,last)
  if len(match.groups())==2:assert match[1]==match[2]
  counts[p]=int(match[1])
assert len(counts)==39 and sum(counts.values())==1591
# 只抽取已冻结validator的validate、driver的ready_sample和常量，不导入或执行driver入口。
v=contents['frozen-minimal/readiness-validate.py'];d=contents['frozen-minimal/readiness-driver.py']
assert sha(v.encode())=='2bbacd615ce27b72b516d798f07e7643ecd33db175194b8174006f7f4b39d8cd' and sha(d.encode())=='ca36839cede4482b59ae879df2e7da4140826b960675514dd363aafe3edc5d40'
dtree=ast.parse(d);nodes=[n for n in dtree.body if isinstance(n,ast.FunctionDef) and n.name=='ready_sample' or isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='NAV_ARGS' for t in n.targets)]
dns={'math':math};exec(compile(ast.Module(body=nodes,type_ignores=[]),'frozen-pure-readiness-helpers','exec'),dns)
vnode=next(n for n in ast.parse(v).body if isinstance(n,ast.FunctionDef) and n.name=='validate');vns={'math':math,'uuid':uuid,'driver_module':types.SimpleNamespace(NAV_ARGS=dns['NAV_ARGS'],ready_sample=dns['ready_sample'])};exec(compile(ast.Module(body=[vnode],type_ignores=[]),'frozen-pure-readiness-validator','exec'),vns)
rows=[json.loads(x) for x in contents['readiness/tool-agent.jsonl'].splitlines()];driver=json.loads(contents['readiness/readiness-driver-result.json']);verdict=vns['validate'](rows,driver);assert verdict==json.loads(contents['readiness/readiness-validation.json']) and verdict['verdict']=='pass'
assert all(int(contents['readiness/'+p])==0 for p in ('exit.txt','driver-exit.txt','host-coordinator-exit.txt'))
result=next(r for r in rows if r['kind']=='result');assert result['passed'] and result['updateFailures']==0
food=[json.loads(x) for x in contents['food-0/tool-agent.jsonl'].splitlines()];events=[json.loads(x) for x in contents['food-0/api-audit.jsonl.events.jsonl'].splitlines()];interrupt=json.loads(contents['food-0/interruption.json'])
assert contents['food-0/api-audit.jsonl']=='' and len([e for e in events if e['event']=='request_attempt'])==1 and not any(e['event'] in ('request_result','response_published','ended') for e in events)
assert len([r for r in food if r['kind']=='controller_request_published'])==2 and not any(r['kind'] in ('native_call_started','controller_response_received','result') for r in food)
assert interrupt['requestOutcome']=='unknown' and interrupt['usage'] is None and interrupt['hostStoppedByCoordinator'] and not interrupt['replayed'] and interrupt['nativeExitFileMissing']
assert 'food-0/exit.txt' not in contents
requests={json.loads(text)['requestId']:json.loads(text) for p,text in contents.items() if p.startswith('food-0/control/') and p.endswith('/request.json')};assert len(requests)==2
attempt=next(e for e in events if e['event']=='request_attempt');request=requests[attempt['requestId']]
assert sha(request['prompt'].encode())==attempt['promptSha256'] and sha(json.dumps(request['responseSchema'],ensure_ascii=False,separators=(',',':')).encode())==attempt['schemaSha256']
for k in ('sessionId','actorId','observationId'):assert request[k]==attempt[k]
assert m['archiveComplete']
print(json.dumps({'plaintextFiles':len(actual),'evidenceFiles':len(m['files']),'contractSuites':len(counts),'contractChecks':sum(counts.values()),'stageTests':14,'readinessVerdict':'pass','readinessSeconds':result['seconds'],'food0':'approval_interrupted_api_outcome_unknown_not_model_failure','hostRequests':2,'apiAttempts':1,'apiResponses':0,'usage':None,'sourceHashChecksAtArchive':sourcechecks,'buildSourceHashLinks':links,'repositorySourcesChecked':args.check_sources,'totalBytes':sum((HERE/n).stat().st_size for n in actual),'orphans':[]},ensure_ascii=False))
