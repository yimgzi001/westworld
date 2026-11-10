#!/usr/bin/env python3
"""阶段32完成后归档：只读结束用例和白名单证据；--write仅写本包，不执行游戏/API。"""
import argparse,collections,json,pathlib,re
import cas
HERE=pathlib.Path(__file__).resolve().parent
ROOT=HERE.parents[3]
RUNS=('natural32-a','natural32-b','natural32-c')
SOURCE_HASH='2c8a0fd6a4f9de948eaef3f8bbd22e9875c29a6da892cea7ac3dcab0a7c87e21'
HOST_HASH='53da6b020b9a3f1a26e1161abd506edeb0040cf63e1d2aed403e1f0463b82ff0'
REUSE=('experiments/natural-resource-evidence/evidence/natural31','experiments/food-acquisition-evidence/evidence/food30','experiments/food-acquisition-evidence/evidence/food30-controller-audit')
ALLOW=('tool-agent.jsonl','console.log','exit.txt','driver-exit.txt','driver.log','run.sh','init.js',
       'stage.json','source-hashes.json','installed-hashes.json','analysis.json','deterministic_driver.py',
       'natural-driver-started.json','natural-driver-result.json')
FINAL_FILES=tuple(p.as_posix() for p in sorted(pathlib.Path('work/natural32-validation').rglob('*')) if p.is_file() and p.suffix in ('.log','.txt','.json','.exit'))
BATCH_FILES=tuple('work/natural32-batch/'+name for name in ('run.py','progress.json','finished.json'))
TEXT={'.cs','.csproj','.props','.targets','.json','.xml','.md','.py','.sh','.yml','.yaml','.txt','.sln','.slnx','.config','.resx','.js','.ts'}
SUPPLEMENT_SOURCES={'tests/Westworld.UnifiedObservation.Tests/Westworld.UnifiedObservation.Tests.csproj': 'f2a9296382c427ad60578295e75f33cc88a7e3005258197d2ae4201fc2708119', 'tests/Westworld.UnifiedObservation.Tests/Program.cs': '580fe58658e8e4629e08e94abfb1960905532e7e24fe09c259fcd6bfcc3b937c', 'tests/Westworld.NativeToolOffers.Tests/NativeStub.cs': 'bf80ce6be66f7f2876739e9aebeae4924ac94553388e4b32a1cffbebaaa3656c', 'tests/Westworld.NativeToolOffers.Tests/NativeToolOffers.Tests.csproj': '195b1efc3d779fbcc5638a4b0983256aa556560d092aefbfeb2028a917adc17b', 'tests/Westworld.NativeToolOffers.Tests/Program.cs': '542aa6d0da2a1a4198f9ebc89a99a0a1cb22df72d35fdfea338ba390b0494acf', 'tests/Westworld.ToolResultProjection.Tests/ToolResultProjection.Tests.csproj': 'a7532d85de3a924caa39c021d217823907732d9c6878790fa921943b80bb771b', 'tests/Westworld.ToolResultProjection.Tests/Program.cs': '02c770a982cf26b5f22b5a71c4c29ce0c23e1359020c79f54a7d33cfc28223dc'}
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--write',action='store_true');args=parser.parse_args()
 # 完成闸门在读取任何场景日志前执行，不以存在一份日志当成结束。
 checkpoints={}
 for name in RUNS:
  cp=json.loads(cas.read_text(ROOT/'.batch-checkpoints'/(name+'.json')))
  if not cp.get('finishedUtc') or cp.get('state') not in ('passed','failed'):raise ValueError('场景未结束：'+name)
  if not (ROOT/'work'/name/'exit.txt').is_file():raise ValueError('缺少host退出文件：'+name)
  checkpoints[name]=cp
 finished=json.loads(cas.read_text(ROOT/'work/natural32-batch/finished.json'))
 assert {row['case'] for row in finished}==set(RUNS) and len(finished)==3
 store=cas.Store(ROOT,HERE,REUSE)
 index={'schema':'westworld-natural32-evidence-v1','runs':{},'scope':'三种子确定性自然资源采集：两场采集失败（a零动作、b三次原生转向成功），一场获得1份value=2土块；非模型、非食用或长期生存。',
  'sourceBasis':'三场各自133份冻结源码完全相同，禁止用工作树冒充运行版本。',
  'auditBoundaries':['仅清除原方块光照位15360，保留contents/data；该修复有纯测试，成功场原始值8本身不含光照位。','同角色同格活跃Dig窗口绑定不是严格多角色矿工因果ID。'],
  'sanitization':'日志/配置的仓库、用户家目录和系统字体路径分别替换为<CLOUD_REPO>/<USER_HOME>/<SYSTEM_FONTS>，原始与脱敏哈希分开；冻结源码原字节保存。',
  'excluded':['世界与私密运行存档','游戏二进制和资产','依赖','密钥及环境变量','模型原始账本'],
  'reuseContract':'本包列出的旧包manifest与分片依赖属于复现输入；须保留完整仓库依赖，单独复制本目录不完整。'}
 def evidence(relative,kind='evidence-file'):
  path=cas.safe(ROOT,relative);raw=path.read_bytes();text=cas.read_text(path);safe,redactions=cas.sanitize(text,ROOT)
  return {'bundle':store.add(kind,safe),'originalSHA256':cas.sha(raw),'archivedTextSHA256':cas.sha(safe.encode()),'pathRedactions':redactions}
 reference=None;rows_by_run={}
 for name in RUNS:
  d=ROOT/'work'/name;inventory_raw=(d/'source-hashes.json').read_bytes();assert cas.sha(inventory_raw)==SOURCE_HASH
  inventory=json.loads(inventory_raw);actual={p.relative_to(d/'sources').as_posix():cas.sha(p.read_bytes()) for p in (d/'sources').rglob('*') if p.is_file()}
  assert actual==inventory and len(inventory)==133
  if reference is None:reference=inventory
  assert inventory==reference
  source_refs={}
  for path,digest in sorted(inventory.items()):
   relative=pathlib.PurePosixPath(path);assert not relative.is_absolute() and '..' not in relative.parts and relative.suffix in TEXT
   source=cas.read_text(cas.safe(d/'sources',path));assert cas.sha(source.encode())==digest
   source_refs[path]=store.add('source-file',source)
  logs={path:evidence('work/'+name+'/'+path) for path in ALLOW}
  rows=[json.loads(line) for line in cas.read_text(d/'tool-agent.jsonl').splitlines() if line.strip()];rows_by_run[name]=rows
  analysis=json.loads(cas.read_text(d/'analysis.json'));result=[row for row in rows if row.get('kind')=='result'];ev=[row for row in rows if row.get('kind')=='natural_resource_evidence']
  assert len(result)==len(ev)==1 and analysis['host']==result[0] and analysis['evidence']==ev[0]
  validator=cas.read_text(d/'sources/experiments/natural-resource-evidence/validate.py')
  independent=cas.validate_frozen(validator,rows);assert independent==analysis['validation']
  batch=next(row for row in finished if row['case']==name)
  assert batch['result']==result[0] and batch['hostExit']==int(cas.read_text(d/'exit.txt')) and batch['driverExit']==int(cas.read_text(d/'driver-exit.txt'))
  driver=json.loads(cas.read_text(d/'natural-driver-result.json'));stage=json.loads(cas.read_text(d/'stage.json'))
  assert driver['kind']=='deterministic_natural_resource_test_not_model' and driver['error'] is None
  assert stage['seed']==ev[0]['seed'] and stage['controllerKind']=='deterministic'
  assert cas.sha((d/'deterministic_driver.py').read_bytes())==inventory['experiments/natural-resource-evidence/deterministic_driver.py']
  for path,digest in (checkpoints[name]['sources']|checkpoints[name]['results']).items():assert digest and cas.sha(cas.safe(ROOT,path).read_bytes())==digest
  installed=json.loads(cas.read_text(d/'installed-hashes.json'));binaries={}
  for filename in ('ToolAgentGate.dll','Survivalcraft.dll','Westworld.Runtime.Core.dll','Westworld.Scapi.Adapter.dll','Westworld.Decisions.Local.dll'):
   digest=cas.sha((d/filename).read_bytes());assert digest==installed[filename];binaries[filename]=digest
  assert binaries['ToolAgentGate.dll']==HOST_HASH
  summary={'actualSeed':ev[0]['seed'],'hostResult':result[0],'hostExit':batch['hostExit'],'driverExit':batch['driverExit'],
   'validation':independent,'eventCounts':dict(collections.Counter(row.get('kind') for row in rows)),
   'originCount':len(ev[0]['origins']),'transferCount':len(ev[0]['transfers']),'ledgerReason':ev[0]['ledger']['Reason'],
   'actualInventory':ev[0]['actualInventory'],'aiViewportCount':ev[0]['aiViewportCount'],'actorGameWidgetNull':ev[0]['actorGameWidgetNull']}
  entry={'summary':summary,'logs':logs,'sourceSnapshot':store.add('source-inventory',{'inventory':inventory,'files':source_refs,'count':133}),
   'checkpoint':evidence('.batch-checkpoints/'+name+'.json'),'verifiedInstalledBinaryHashes':binaries,
   'buildCorrespondence':'最终安装宿主53da6b…；natural32-validation最终构建和133份冻结源码分别保留。body32前置失败不混入最终通过。'}
  index['runs'][name]=entry
 support={'finalEvidence':{path:evidence(path,'build-file') for path in FINAL_FILES},
  'batchEvidence':{path:evidence(path,'batch-file') for path in BATCH_FILES},
  'preliminaryEvidence':{},'supplementalTestSources':{},
  'testScopes':{'contracts':'最终28套；其中组件23、菜单135、投影42，不重复累计','python':'最终61项纯测试'},
  'supplementScope':'三套body相关C#测试源码于归档时另行固定；不冒充运行冻结清单。'}
 for p in sorted((ROOT/'work').glob('body32-*.log')):
  relative=p.relative_to(ROOT).as_posix();support['preliminaryEvidence'][relative]=evidence(relative,'build-file')
 for path,digest in SUPPLEMENT_SOURCES.items():
  source=cas.read_text(cas.safe(ROOT,path));assert cas.sha(source.encode())==digest
  support['supplementalTestSources'][path]={'bundle':store.add('source-file',source),'sha256':digest}
 counts={}
 for p in sorted((ROOT/'work/natural32-validation/contracts').glob('*.test.log')):
  last=cas.read_text(p).strip().splitlines()[-1];m=re.search(r'(\d+)/(\d+)',last) or re.search(r'TOTAL (\d+)',last) or re.search(r'PASS (\d+) endpoint',last)
  assert m
  if len(m.groups())==2:assert m[1]==m[2]
  counts[p.name]=int(m[1])
 assert len(counts)==28 and sum(counts.values())==1110
 assert 'Ran 61 tests' in cas.read_text(ROOT/'work/natural32-validation/python-tests.log')
 final=json.loads(cas.read_text(ROOT/'work/natural32-validation/result.json'))
 assert final['hostExit']==final['contractsExit']==final['pythonExit']==0 and final['changedSources']==[]
 assert final['dllHashes']['work/natural32-build/bin/ToolAgentGate/debug/ToolAgentGate.dll']==HOST_HASH
 before=json.loads(cas.read_text(ROOT/'work/natural32-validation/source-before.json'))
 support['finalContractCounts']=counts
 index['supportEvidence']=store.add('support-index',support)
 # 主清单只引用内容包，避免为三个相同源码清单重复打包。
 for name,entry in list(index['runs'].items()):index['runs'][name]={'bundle':store.add('run-evidence',entry),'verdict':entry['summary']['validation']['verdict']}
 index['dependencies']=store.dependencies;index['archiveComplete']=True;index['expectedVerdicts']={'natural32-a':'fail','natural32-b':'fail','natural32-c':'pass'}
 payload_files={};items=list(store.refs.items());index['bundleIndexes']=[]
 for offset in range(0,len(items),80):
  filename=f'bundle-index-{offset//80+1:03}.json';raw=cas.packed(dict(items[offset:offset+80]));payload_files[filename]=raw
  index['bundleIndexes'].append({'file':filename,'sha256':cas.sha(raw)})
 payload_files['manifest.json']=cas.packed(index)+b'\n'
 if args.write:
  store.commit()
  for name,raw in payload_files.items():
   path=cas.safe(HERE,name)
   if path.exists() and path.read_bytes()!=raw:raise ValueError('归档已存在不同清单，禁止覆盖：'+name)
   path.write_bytes(raw)
 print(json.dumps({'runs':3,**store.counts(),'verdicts':index['expectedVerdicts'],'finalEvidence':len(support['finalEvidence']),
  'preliminaryEvidence':len(support['preliminaryEvidence']),'sourceHashChecks':399,'write':args.write},ensure_ascii=False))
if __name__=='__main__':main()
