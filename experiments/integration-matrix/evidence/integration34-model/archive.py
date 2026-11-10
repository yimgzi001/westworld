#!/usr/bin/env python3
"""阶段34三场模型/基础设施子包；只读取已结束场次，不执行驱动、游戏或API。"""
import argparse,json,pathlib
import cas
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[3];CASES=('food-0','food-1','food-2')
SOURCE='be471c7923419a415d5221d4468dfaae5584efd56c3817b80e669ffad97fad3a';HOST='b862f2a423d5c5daffa95a15d7be22134d4dc9953bc8f11ddc3b8aed0926a7eb'
REUSE=('experiments/integration-matrix/evidence/integration34-native',)+tuple('experiments/natural-resource-evidence/evidence/'+x for x in ('natural33','natural32','natural31'))+tuple('experiments/food-acquisition-evidence/evidence/'+x for x in ('food30','food30-controller-audit'))
ALLOW=('tool-agent.jsonl','console.log','api-audit.jsonl','api-audit.jsonl.events.jsonl','stage.json','source-hashes.json','installed-hashes.json','run.sh','init.js')
OPTIONAL=('execution.json','execution-started.json','exit.txt','external-driver-launch.json','driver-launch.log','host-launch.log')
SUPPORT=('work/integration34-integration/matrix.json','work/integration34-revalidation/model-food1-independent-v1.json','work/integration34-revalidation/model-batch-independent-v2.json','work/integration34-failfast/tests.log','work/integration34-failfast/exit.txt','work/integration34-failfast/source-sha256.txt','work/integration34-failfast/result.json')
def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--write',action='store_true');args=ap.parse_args()
 for name in CASES:
  d=ROOT/'work'/('integration34-'+name)
  if name=='food-0':
   x=json.loads(cas.read_text(d/'execution.json'));assert x['hostExit']==x['driverExit']==-15 and x['drainCertain']
  else:
   assert int(cas.read_text(d/'exit.txt'))==1
   events=[json.loads(x) for x in cas.read_text(d/'api-audit.jsonl.events.jsonl').splitlines()];assert events[-1]['event']=='ended' and events[-1]['status']=='completed'
 store=cas.Store(ROOT,HERE,REUSE)
 def ev(relative):
  p=cas.safe(ROOT,relative);raw=p.read_bytes();clean,n=cas.sanitize(cas.read_text(p),ROOT)
  return {'bundle':store.add('evidence-file',clean),'originalSHA256':cas.sha(raw),'archivedTextSHA256':cas.sha(clean.encode()),'pathRedactions':n}
 m={'schema':'westworld-integration34-model-evidence-v1','archiveComplete':True,'runs':{},'support':{},'supplementSources':{},'bundleIndexes':[],
  'coordinationProvenance':{'source':'协调者核验工具session后提供的记录，非补造原生execution日志','food-1':{'driverSession':60045,'driverExit':0,'hostExit':1},'food-2':{'driverSession':43515,'driverExit':0,'hostExit':1},'mode':'supported_authorized_exec_network_driver_plus_cloud_gui_host','networkSettingsChanged':False,'serviceChanged':False,'food0RequestsReplayed':False},
  'scope':'food0基础设施网络失败/中断，模型质量不评估且费用未知；food1/2真实模型预算耗尽，均未完成食用目标。不能将known tokens当总计费。'}
 for name in CASES:
  d=ROOT/'work'/('integration34-'+name);inv=json.loads(cas.read_text(d/'source-hashes.json'));assert cas.sha((d/'source-hashes.json').read_bytes())==SOURCE and len(inv)==149
  assert {p.relative_to(d/'sources').as_posix():cas.sha(p.read_bytes()) for p in (d/'sources').rglob('*') if p.is_file() and '__pycache__' not in p.parts}==inv
  sources={p:store.add('source-file',cas.read_text(cas.safe(d/'sources',p))) for p in sorted(inv)}
  logs={p:ev('work/integration34-'+name+'/'+p) for p in ALLOW}
  for p in OPTIONAL:
   if (d/p).is_file():logs[p]=ev('work/integration34-'+name+'/'+p)
  for p in sorted((d/'control').glob('[0-9]*/*.json')):
   if p.name in ('request.json','accepted.json','canceled.json') or (p.name.startswith('response-') and p.name.endswith('.received.json')):
    rel=p.relative_to(d).as_posix();logs[rel]=ev('work/integration34-'+name+'/'+rel)
  installed=json.loads(cas.read_text(d/'installed-hashes.json'));hashes={}
  for p in ('ToolAgentGate.dll','Survivalcraft.dll','Westworld.Runtime.Core.dll','Westworld.Scapi.Adapter.dll','Westworld.Decisions.Local.dll'):
   hashes[p]=cas.sha((d/p).read_bytes());assert hashes[p]==installed[p]
  assert hashes['ToolAgentGate.dll']==HOST
  for p in ('run.sh','stage.json'):assert logs[p]['originalSHA256']==installed[p]
  m['runs'][name]=store.add('run-evidence',{'logs':logs,'sourceSnapshot':store.add('source-inventory',{'inventory':inv,'files':sources,'count':149}),'verifiedInstalledBinaryHashes':hashes,'missingOptionalFiles':[p for p in OPTIONAL if p not in logs]})
 for p in SUPPORT:m['support'][p]=ev(p)
 for p,digest in json.loads((HERE/'supplement-hashes.json').read_text()).items():
  text=cas.read_text(cas.safe(ROOT,p));assert cas.sha(text.encode())==digest;m['supplementSources'][p]={'bundle':store.add('source-file',text),'sha256':digest}
 m['supplementScope']='修正的failfast runner及46项离线测试源码是后验版本，不属于本三场冻结149份sources；没有重放旧请求或重跑游戏。'
 m['dependencies']=store.dependencies;files={};items=list(store.refs.items())
 for offset in range(0,len(items),80):
  name=f'bundle-index-{offset//80+1:03}.json';raw=cas.packed(dict(items[offset:offset+80]));files[name]=raw;m['bundleIndexes'].append({'file':name,'sha256':cas.sha(raw)})
 files['manifest.json']=cas.packed(m)+b'\n'
 if args.write:
  store.commit()
  for name,raw in files.items():
   p=cas.safe(HERE,name)
   if p.exists() and p.read_bytes()!=raw:raise ValueError('不覆写不同内容：'+name)
   p.write_bytes(raw)
 print(json.dumps({'runs':3,**store.counts(),'sourceHashChecks':447,'write':args.write},ensure_ascii=False))
if __name__=='__main__':main()
