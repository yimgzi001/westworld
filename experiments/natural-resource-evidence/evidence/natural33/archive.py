#!/usr/bin/env python3
"""阶段33独立归档：仅读已结束用例和获准脱敏文本；不执行游戏、驱动或API。"""
import argparse,json,pathlib
import cas
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[3];CASE='natural33-terra';D=ROOT/'work'/CASE
SOURCE='2c8a0fd6a4f9de948eaef3f8bbd22e9875c29a6da892cea7ac3dcab0a7c87e21'
HOST='53da6b020b9a3f1a26e1161abd506edeb0040cf63e1d2aed403e1f0463b82ff0'
REUSE=('experiments/natural-resource-evidence/evidence/natural32','experiments/natural-resource-evidence/evidence/natural31','experiments/food-acquisition-evidence/evidence/food30','experiments/food-acquisition-evidence/evidence/food30-controller-audit')
ALLOW=('tool-agent.jsonl','console.log','exit.txt','run.sh','init.js','stage.json','source-hashes.json','installed-hashes.json','analysis.json','openai-audit.jsonl','openai-audit.jsonl.events.jsonl')
def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--write',action='store_true');args=ap.parse_args()
 cp=json.loads(cas.read_text(ROOT/'.batch-checkpoints'/f'{CASE}.json'));assert cp['finishedUtc'] and cp['state']=='passed' and int(cas.read_text(D/'exit.txt'))==0
 store=cas.Store(ROOT,HERE,REUSE)
 def evidence(relative):
  p=cas.safe(ROOT,relative);raw=p.read_bytes();text=cas.read_text(p);safe,redactions=cas.sanitize(text,ROOT)
  return {'bundle':store.add('evidence-file',safe),'originalSHA256':cas.sha(raw),'archivedTextSHA256':cas.sha(safe.encode()),'pathRedactions':redactions}
 inventory=json.loads(cas.read_text(D/'source-hashes.json'));assert cas.sha((D/'source-hashes.json').read_bytes())==SOURCE
 actual={p.relative_to(D/'sources').as_posix():cas.sha(p.read_bytes()) for p in (D/'sources').rglob('*') if p.is_file()};assert actual==inventory and len(actual)==133
 sources={}
 for path,digest in sorted(inventory.items()):
  source=cas.read_text(cas.safe(D/'sources',path));assert cas.sha(source.encode())==digest;sources[path]=store.add('source-file',source)
 logs={name:evidence('work/'+CASE+'/'+name) for name in ALLOW}
 for directory in sorted((D/'control').glob('[0-9]*')):
  for name in ('request.json','accepted.json','response-0001.received.json'):
   relative=(directory/name).relative_to(D).as_posix();logs[relative]=evidence('work/'+CASE+'/'+relative)
 assert len(logs)==20
 for path,digest in (cp['sources']|cp['results']).items():assert cas.sha(cas.safe(ROOT,path).read_bytes())==digest
 installed=json.loads(cas.read_text(D/'installed-hashes.json'));binaries={}
 for name in ('ToolAgentGate.dll','Survivalcraft.dll','Westworld.Runtime.Core.dll','Westworld.Scapi.Adapter.dll','Westworld.Decisions.Local.dll'):
  binaries[name]=cas.sha((D/name).read_bytes());assert binaries[name]==installed[name]
 assert binaries['ToolAgentGate.dll']==HOST
 rows=[json.loads(x) for x in cas.read_text(D/'tool-agent.jsonl').splitlines()];analysis=json.loads(cas.read_text(D/'analysis.json'))
 assert cas.validate_frozen(cas.read_text(D/'sources/experiments/natural-resource-evidence/validate.py'),rows)==analysis['validation'] and analysis['validation']['verdict']=='pass'
 m={'schema':'westworld-natural33-evidence-v1','archiveComplete':True,'case':CASE,'logs':logs,
  'sourceSnapshot':store.add('source-inventory',{'inventory':inventory,'files':sources,'count':133}),
  'checkpoint':evidence('.batch-checkpoints/'+CASE+'.json'),'verifiedInstalledBinaryHashes':binaries,
  'buildEvidence':{path:evidence(path) for path in ('work/natural32-validation/result.json','work/natural32-validation/host-build.log')},
  'scope':'独立fresh模型自然资源沙砾采集成功；非食物、非长期生存、非无停顿。两次Dig和MoveTo真实执行；PickUp仅计划，实际automatic transfer。',
  'sanitization':'仓库、用户家目录和系统字体路径分别替换为<CLOUD_REPO>/<USER_HOME>/<SYSTEM_FONTS>，原始与脱敏哈希分别保留；冻结源码原字节。',
  'excluded':['密钥与环境变量','世界及私密存档','游戏二进制与资产','依赖'],
  'missingEvidence':['独立driver退出码文件不存在；使用events ended=completed，不伪造退出码'],
  'dependencies':store.dependencies,'bundleIndexes':[]}
 files={};items=list(store.refs.items())
 for offset in range(0,len(items),80):
  name=f'bundle-index-{offset//80+1:03}.json';raw=cas.packed(dict(items[offset:offset+80]));files[name]=raw;m['bundleIndexes'].append({'file':name,'sha256':cas.sha(raw)})
 files['manifest.json']=cas.packed(m)+b'\n'
 if args.write:
  store.commit()
  for name,raw in files.items():
   p=cas.safe(HERE,name)
   if p.exists() and p.read_bytes()!=raw:raise ValueError('禁止覆盖已存在不同清单：'+name)
   p.write_bytes(raw)
 print(json.dumps({'case':CASE,**store.counts(),'sourceHashChecks':133,'checkpointHashChecks':6,'logs':len(logs),'write':args.write},ensure_ascii=False))
if __name__=='__main__':main()
