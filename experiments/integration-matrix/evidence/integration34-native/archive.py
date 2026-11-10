#!/usr/bin/env python3
"""阶段34六场原生子包；复用既有CAS，仅读结束场次，不运行游戏/API。"""
import argparse,json,pathlib,re
import cas
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[3]
CASES=('direct_late','direct_failure','direct_cancel','tail_early','tail_late','tail_failure')
SOURCE='439d9655af8310513d03cf3ab576eefbea03cf397153a5bfbbf8d013dc6af2b0';HOST='b862f2a423d5c5daffa95a15d7be22134d4dc9953bc8f11ddc3b8aed0926a7eb'
REUSE=tuple('experiments/natural-resource-evidence/evidence/'+n for n in ('natural33','natural32','natural31'))+tuple('experiments/food-acquisition-evidence/evidence/'+n for n in ('food30','food30-controller-audit'))
ALLOW=('tool-agent.jsonl','console.log','execution.json','execution-started.json','exit.txt','direct-driver-result.json','direct-driver-started.json','driver-launch.log','host-launch.log','stage.json','source-hashes.json','installed-hashes.json','run.sh','init.js','deterministic_driver.py')
def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--write',action='store_true');args=ap.parse_args()
 initial=json.loads(cas.read_text(ROOT/'work/integration34-integration/run-deterministic-result.json'));assert {r['case'] for r in initial['cases']}==set(CASES)
 for name in CASES:
  d=ROOT/'work'/('integration34-'+name);x=json.loads(cas.read_text(d/'execution.json'))
  assert x['case']==name and x['hostExit']==x['driverExit']==0 and x['drainCertain'] is True and x['error'] is None and int(cas.read_text(d/'exit.txt'))==0
 store=cas.Store(ROOT,HERE,REUSE)
 def evidence(relative):
  p=cas.safe(ROOT,relative);raw=p.read_bytes();text=cas.read_text(p);clean,n=cas.sanitize(text,ROOT)
  return {'bundle':store.add('evidence-file',clean),'originalSHA256':cas.sha(raw),'archivedTextSHA256':cas.sha(clean.encode()),'pathRedactions':n}
 m={'schema':'westworld-integration34-native-evidence-v1','archiveComplete':True,'scope':'仅六场人工非模型生命周期原生控制；三模型场次尚未纳入，不声称自然食物成功或P3/P4原生接线。','runs':{},'support':{},'supplementSources':{},'bundleIndexes':[]}
 for name in CASES:
  d=ROOT/'work'/('integration34-'+name);inv=json.loads(cas.read_text(d/'source-hashes.json'));assert cas.sha((d/'source-hashes.json').read_bytes())==SOURCE
  assert {p.relative_to(d/'sources').as_posix():cas.sha(p.read_bytes()) for p in (d/'sources').rglob('*') if p.is_file() and '__pycache__' not in p.parts}==inv and len(inv)==140
  source={p:store.add('source-file',cas.read_text(cas.safe(d/'sources',p))) for p in sorted(inv)}
  installed=json.loads(cas.read_text(d/'installed-hashes.json'));hashes={}
  for p in ('ToolAgentGate.dll','Survivalcraft.dll','Westworld.Runtime.Core.dll','Westworld.Scapi.Adapter.dll','Westworld.Decisions.Local.dll'):
   hashes[p]=cas.sha((d/p).read_bytes());assert hashes[p]==installed[p]
  assert hashes['ToolAgentGate.dll']==HOST
  entry={'logs':{p:evidence('work/integration34-'+name+'/'+p) for p in ALLOW},'sourceSnapshot':store.add('source-inventory',{'inventory':inv,'files':source,'count':140}),'verifiedInstalledBinaryHashes':hashes}
  m['runs'][name]=store.add('run-evidence',entry)
 for group in ('integration34-integration','integration34-unified','integration34-pure','integration34-revalidation'):
  files={}
  for p in sorted((ROOT/'work'/group).rglob('*')):
   if p.is_file() and p.suffix in ('.json','.py','.txt','.log','.exit') and '__pycache__' not in p.parts:files[p.relative_to(ROOT).as_posix()]=evidence(p.relative_to(ROOT).as_posix())
  m['support'][group]=store.add('support-index',files)
 for p,digest in json.loads((HERE/'supplement-hashes.json').read_text()).items():
  text=cas.read_text(cas.safe(ROOT,p));assert cas.sha(text.encode())==digest
  m['supplementSources'][p]={'bundle':store.add('source-file',text),'sha256':digest}
 m['supplementScope']='补充源码另按固定SHA保存，非140份stage来源；旧P5与v2单独保留。未执行这些源码或重跑原生。'
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
 print(json.dumps({'runs':6,**store.counts(),'sourceHashChecks':840,'write':args.write},ensure_ascii=False))
if __name__=='__main__':main()
