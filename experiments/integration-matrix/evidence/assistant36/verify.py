#!/usr/bin/env python3
"""阶段36纯离线独立复核：哈希、公开请求绑定、原生收据、来源/自动拾取/进食。"""
import argparse,ast,json,math,pathlib,re,uuid
from archive import HERE,ROOT,SECRET,sha,gitblob,encode

def verify(check_sources=False):
 m=json.loads((HERE/'manifest.json').read_text());text={};allowed={'archive.py','verify.py','README.md','manifest.json','independent-review.json'}
 for p,v in m['files'].items():
  f=HERE/p;assert not f.is_symlink();b=f.read_bytes();assert sha(b)==v['archivedSHA256'] and len(b)==v['bytes'];t=b.decode('utf-8');assert not SECRET.search(t)
  assert str(ROOT) not in t and not re.search(r'/(?:home|Users)/[^/\s]+/|/usr/share/fonts/',t)
  if not v['pathRedactions']:assert v['originalSHA256']==v['archivedSHA256']
  text[p]=t;allowed.add(p)
 actual={p.relative_to(HERE).as_posix() for p in HERE.rglob('*') if p.is_file() and '__pycache__' not in p.parts};assert not actual-allowed and allowed-actual <= {'independent-review.json'}
 inv=json.loads(text['run/source-hashes.json']);assert len(inv)==m['sourceReference']['count']==159
 for p,h in inv.items():
  assert m['sourceReference']['files'][p]['sha256']==h
  if check_sources:
   b=(ROOT/p).read_bytes();assert sha(b)==h and gitblob(b)==m['sourceReference']['files'][p]['gitBlob'],p
 installed=json.loads(text['run/installed-hashes.json']);assert all(installed[p]==h for p,h in m['installedHashesVerified'].items())
 prior=m['priorBuildReference'];b=(ROOT/prior['repositoryPath']).read_bytes();assert sha(b)==prior['sha256'];assert json.loads(b)['runs']['readiness']['installedHashesVerified']==m['installedHashesVerified']
 src=text['frozen-minimal/validate_food.py'];assert sha(src.encode())==inv['experiments/integration-matrix/validate_food.py']=='fa0db52bfa1de0951b62d46cdc217740e2c8af51426ce8b0a3b7b378d127d358'
 # 仅执行冻结验收器的三个纯函数，不执行CLI入口或导入驱动。
 nodes=[n for n in ast.parse(src).body if isinstance(n,ast.FunctionDef) and n.name in ('number','guid','validate_log')];ns={'math':math,'uuid':uuid};exec(compile(ast.Module(body=nodes,type_ignores=[]),'frozen-pure-food-validator','exec'),ns)
 rows=[json.loads(l) for l in text['run/tool-agent.jsonl'].splitlines() if l.strip()];verdict=ns['validate_log'](rows);assert verdict==json.loads(text['run/food-validation.json']) and verdict['verdict']=='pass'
 assert int(text['run/exit.txt'])==0
 requests=[json.loads(t) for p,t in sorted(text.items()) if p.endswith('/request.json')];responses=[json.loads(t) for p,t in sorted(text.items()) if p.endswith('/response-0001.received.json')];accepted=[json.loads(t) for p,t in sorted(text.items()) if p.endswith('/accepted.json')];assert len(requests)==len(responses)==len(accepted)==3
 received=[r for r in rows if r['kind']=='controller_response_received'];published=[r for r in rows if r['kind']=='controller_request_published'];assert len(received)==len(published)==3
 for q,a,ack,pub,rec in zip(requests,responses,accepted,published,received):
  assert all(q[k]==a[k]==pub[k] for k in ('sessionId','actorId','observationId','requestId'));assert ack['requestId']==q['requestId']==rec['requestId'];assert ack['responseFile']=='response-0001.received.json'
  assert [{'Tool':s['tool'],'Arguments':s['arguments']} for s in a['decision']['steps']]==rec['decision']['Steps'] and a['decision']['goal']==rec['decision']['Goal']
  assert q['context']['facts']['goalExecution']['progress']['observationId']==q['observationId']
  for step in a['decision']['steps']:assert step['tool'] in {o['Name'] for o in q['offers']}
 steps=[[s['tool'] for s in a['decision']['steps']] for a in responses];assert steps==[['act_Dismount','act_MoveTo','act_Dig'],[],['act_Eat']]
 target=responses[0]['decision']['steps'][1]['arguments']['destination'];assert target==responses[0]['decision']['steps'][2]['arguments']['target']
 block=next(b for b in requests[0]['context']['facts']['observation']['visibleBlocks'] if b['reference']==target);assert block['name']=='海胆'
 facts2=requests[1]['context']['facts'];assert facts2['goalExecution']['budget']['speculative'] and not any(s['value']==226 for s in facts2['inventory']['slots'])
 facts3=requests[2]['context']['facts'];item=next(i for i in facts3['goalExecution']['progress']['items'] if i['value']==226);assert item['initialCount']==0 and item['currentCount']==item['acquiredCount']==1 and item['newSourceVerified']=='verified'
 assert any(s['slot']==0 and s['value']==226 and s['count']==1 for s in facts3['inventory']['slots'])
 calls=[r for r in rows if r['kind']=='native_call_started'];assert [r['step']['Tool'] for r in calls]==['act_Dismount','act_MoveTo','act_Dig','act_Eat'];assert calls[2]['decisionId']==requests[0]['requestId'] and calls[3]['decisionId']==requests[2]['requestId']
 handoff=next(r for r in rows if r['kind']=='prefetch_handoff_taken');tail=next(r for r in rows if r['kind']=='continuation_tail_settled');assert handoff['requestId']==requests[1]['requestId'] and tail['monotonicSeconds']<handoff['monotonicSeconds']
 e=next(r for r in rows if r['kind']=='natural_resource_evidence');assert len(e['origins'])==len(e['transfers'])==len(e['meals'])==1;assert e['transfers'][0]['fact']['Method']=='automatic'
 origin=e['origins'][0]['fact'];assert origin['Value']==226 and origin['Count']==1 and all(origin[k]==block['cell'][v] for k,v in [('CellX','x'),('CellY','y'),('CellZ','z')])
 meal=e['meals'][0]['Result'];end=next(r for r in rows if r['kind']=='result');final=next(r for r in rows if r['kind']=='final_audit');assert final['health']==1 and end['updateFailures']==0
 stage=json.loads(text['run/stage.json']);assert stage['modelApiCalls']==0 and stage['controller']=='assistant_manual_file_decisions'
 return {'schema':'assistant36-independent-review-v1','verdict':'pass','rawRows':len(rows),'requests':3,'responses':3,'nativeActionSequence':[r['step']['Tool'] for r in calls],'targetFromCurrentPublicView':block,'secondDecisionEmpty':True,'thirdSnapshotNewSourceVerified':True,'hostExit':0,'seconds':end['seconds'],'health':final['health'],'path':final['path'],'netDisplacement':final['netDisplacement'],'foodAtMeal':[meal['FoodBefore'],meal['FoodAfter']],'finalFoodAfterMetabolism':final['food'],'mealStats':[meal['StatsBefore'],meal['StatsAfter']],'automaticTransfers':1,'prefetchTaken':final['prefetchTaken'],'hostReportedMotionOverlapSeconds':final['overlapSeconds'],'responseWaitSeconds':[r['waitSeconds'] for r in received],'externalApiCalls':0,'externalApiCallsBasis':'协调记录及stage声明；非独立网络流量证明','assistantDecisionTokens':None,'sourceFiles':159,'binaryHashLinks':5,'independentFoodValidation':verdict,'limitations':m['limits']}

def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--check-sources',action='store_true');ap.add_argument('--write-review',action='store_true');a=ap.parse_args();review=verify(a.check_sources);p=HERE/'independent-review.json';b=encode(review)
 if a.write_review:
  assert not p.exists() or p.read_bytes()==b;p.write_bytes(b)
 else:assert p.read_bytes()==b
 files=[p for p in HERE.rglob('*') if p.is_file() and '__pycache__' not in p.parts]
 print(json.dumps({'verdict':'pass','files':len(files),'bytes':sum(p.stat().st_size for p in files),'sourceFiles':159,'checkRepositorySources':a.check_sources,'rawRows':review['rawRows'],'requests':3,'foodValidation':'pass','seconds':review['seconds'],'manifestSHA256':sha((HERE/'manifest.json').read_bytes()),'orphans':[]},ensure_ascii=False))
if __name__=='__main__':main()
