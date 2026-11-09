#!/usr/bin/env python3
"""Read-only evidence collector; --write writes ONLY this archive directory.
No game launch, API, network, credentials, binary assets or world saves.
Requires the publisher's baseline Git tree (path -> blob SHA1, or GitHub tree response).
"""
import argparse,base64,gzip,hashlib,importlib.util,json,pathlib,re
HERE=pathlib.Path(__file__).resolve().parent
ROOT=HERE.parents[3]
BASE=None
RUNS=['food28-deterministic','food28-terra']
ALLOW=('tool-agent.jsonl','console.log','exit.txt','run.sh','deterministic_driver.py','driver.log','driver-exit.txt','api-driver-exit.txt','food-driver-result.json','food-driver-started.json','food-evidence.json','strict-food-validation.json','stage.json','source-hashes.json','installed-hashes.json')
SECRET=re.compile(rb'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)')
TEXT={'.cs','.csproj','.props','.targets','.json','.xml','.md','.py','.sh','.yml','.yaml','.txt','.sln','.slnx','.config','.resx','.js','.ts','.gitignore','.editorconfig'}
def sha(raw):return hashlib.sha256(raw).hexdigest()
def blob(raw):return hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest()
def packed(value):return json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()
def baseline(path):
 data=json.loads(path.read_text())
 if 'tree' in data:return {r['path']:r['sha'] for r in data['tree'] if r.get('type')=='blob'}
 return data

def main():
 ap=argparse.ArgumentParser();ap.add_argument('--baseline-tree',type=pathlib.Path,required=True);ap.add_argument('--baseline-commit',required=True);ap.add_argument('--write',action='store_true');a=ap.parse_args()
 global BASE
 BASE=a.baseline_commit
 if not re.fullmatch(r'61c83eff[0-9a-f]{32}',BASE):raise ValueError('exact authorized baseline commit required')
 tree=baseline(a.baseline_tree);index={'schema':'westworld-food-evidence-v1','baselineCommit':BASE,'baselineTreeInputSHA256':sha(a.baseline_tree.read_bytes()),'runs':{},'bundles':{},'pending':[]}
 baseInventory=None
 def bundle(kind,value):
  raw=packed(value)
  if SECRET.search(raw):raise ValueError('credential-like content in '+kind+'; must explicitly sanitize/review before archive')
  h=sha(raw);key=kind+'-'+h[:20]
  if key not in index['bundles']:
   z=gzip.compress(raw,mtime=0);encoded=base64.b64encode(z);parts=[]
   for offset in range(0,len(encoded),29000):
    name=f'{key}.gz.b64.part{offset//29000+1:03}';part=encoded[offset:offset+29000]
    parts.append({'file':name,'bytes':len(part),'sha256':sha(part)})
    if a.write:(HERE/name).write_bytes(part)
   assert gzip.decompress(base64.b64decode(encoded))==raw
   metadata=packed({'rawSHA256':h,'rawBytes':len(raw),'gzipSHA256':sha(z),'parts':parts})
   metadataName=key+'-manifest.json'
   if a.write:(HERE/metadataName).write_bytes(metadata)
   index['bundles'][key]={'manifest':metadataName,'sha256':sha(metadata)}
  return key
 for name in RUNS:
  d=ROOT/'work'/name;cp=ROOT/'.batch-checkpoints'/(name.removeprefix('tool-')+'.json')
  if not (d/'exit.txt').exists() or not cp.exists():index['pending'].append(name);continue
  checkpoint=json.loads(cp.read_text())
  if not checkpoint.get('finishedUtc'):index['pending'].append(name+':checkpoint_unfinished');continue
  files={n:(d/n).read_bytes().decode('utf-8') for n in ALLOW if (d/n).is_file()}
  for relative,expected in (checkpoint.get('results',{})|checkpoint.get('sources',{})).items():
   p=ROOT/relative
   if not p.is_file() or sha(p.read_bytes())!=expected:raise ValueError('checkpoint hash mismatch '+relative)
  sourceRoot=d/'sources';inventory={};delta={};unchanged={}
  for p in sorted(sourceRoot.rglob('*')):
   if not p.is_file():continue
   relative=p.relative_to(sourceRoot).as_posix();raw=p.read_bytes();inventory[relative]=sha(raw)
   if tree.get(relative)==blob(raw):unchanged[relative]=tree[relative];continue
   if p.suffix not in TEXT and p.name not in TEXT:raise ValueError('non-text/source delta rejected '+relative)
   delta[relative]=raw.decode('utf-8')
  if not inventory:raise ValueError('missing staged sources '+name)
  if 'source-hashes.json' in files and json.loads(files['source-hashes.json'])!=inventory:raise ValueError('source inventory mismatch '+name)
  if baseInventory is None:baseInventory=inventory.copy()
  source={'baselineCommit':BASE,'inventoryBase':bundle('source-inventory',baseInventory),
   'inventoryChanges':{p:h for p,h in inventory.items() if baseInventory.get(p)!=h},
   'inventoryRemoved':[p for p in baseInventory if p not in inventory],
   'changedOrNewFiles':{p:bundle('source-file',text) for p,text in delta.items()},
   'restore':'All inventory paths absent from changedOrNewFiles are exact Git baseline files; verify final inventory SHA256. No deletion inferred outside staged inventory.'}

  rows=[json.loads(l) for l in files.get('snapshot.jsonl',files.get('tool-agent.jsonl','')).splitlines() if l.strip()]
  result=[r for r in rows if r.get('kind')=='result']
  entry={'exit':int(files['exit.txt'].strip()),'logs':{p:bundle('evidence-file',text) for p,text in files.items()},'sourceDelta':bundle('source-delta',source),'sourceCount':len(inventory),'changedSourceCount':len(delta),'checkpoint':checkpoint,'result':result[-1] if result else None}
  if 'food-evidence.json' in files:
   spec=importlib.util.spec_from_file_location('foodvalidator',ROOT/'experiments/food-acquisition-evidence/validate.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
   entry['strictFoodValidation']=module.validate(json.loads(files['food-evidence.json']))
  else:entry['strictFoodValidation']={'verdict':'partial','missing':['food-evidence.json']}
  audit=d/'api-audit.jsonl'
  if audit.is_file():
   # Only completed run reached here. Never inspect credential sources or retain raw API bodies.
   raw=audit.read_bytes()
   if SECRET.search(raw):raise ValueError('credential-like API audit; stop archive')
   allowed={'sessionId','actorId','observationId','requestId','driverSourceSha256','maxOutputTokens','requestedModel','actualModel','promptSha256','schemaSha256','schemaStrict','effort','outcome','error','wallSeconds','delivery','usage'}
   usageKeys={'input_tokens','output_tokens','total_tokens','input_tokens_details','output_tokens_details','cached_tokens','reasoning_tokens'}
   def safeUsage(v):
    if isinstance(v,dict):return {k:safeUsage(x) for k,x in v.items() if k in usageKeys}
    return v if type(v)==int and v>=0 else None
   records=[]
   for line in raw.splitlines():
    if not line.strip():continue
    record=json.loads(line);safeRecord={k:v for k,v in record.items() if k in allowed and (v is None or type(v) in (str,bool,int,float))}
    if 'usage' in record:safeRecord['usage']=safeUsage(record['usage'])
    records.append(safeRecord)
   entry['safeApiAudit']={'records':bundle('api-safe-fields',records),'originalSHA256':sha(raw),'omitted':'rawDecision, decision, prompts, bodies and every unlisted field'}
  runBytes=json.dumps(entry,ensure_ascii=False,indent=2).encode()+b'\n'
  if a.write:(HERE/(name+'-manifest.json')).write_bytes(runBytes)
  index['runs'][name]={'manifest':name+'-manifest.json','sha256':sha(runBytes),'exit':entry['exit'],'changedSourceCount':entry['changedSourceCount']}
 evidenceGroups={}
 for folder in ['food28-build','food28-contract-tests','food28-contract-retry','outcome28-feedback','food28-python-tests','food28-stage-tests','native-acquisition28-tests']:
  d=ROOT/'work'/folder
  if not d.is_dir():continue
  files={p.name:p.read_bytes().decode('utf-8') for p in sorted(d.iterdir()) if p.is_file() and p.suffix in ('.log','.json','.txt')}
  evidenceGroups[folder]={name:bundle('test-build-file',text) for name,text in files.items()}
 index['testBuildEvidence']=evidenceGroups
 index['postRunValidatorSources']={p:{'content':bundle('source-file',(ROOT/p).read_bytes().decode('utf-8')),'sha256':sha((ROOT/p).read_bytes())} for p in ('experiments/food-acquisition-evidence/validate.py','experiments/food-acquisition-evidence/test_validate.py')}
 index['testScope']='food28 aggregate 26 suites/972 checks passed after missing-assets restore retry; original failed logs retained. Feedback initial fixture duplicate-snapshot failure and final24 retained. Delta28, source-ledger29, native-audit13, Python41 and staging10 have separate scope; native explicit acquisition path is not asserted.'
 index['archiveComplete']=not index['pending']
 if a.write:(HERE/'manifest.json').write_bytes(json.dumps(index,ensure_ascii=False,separators=(',',':')).encode()+b'\n')
 print(json.dumps({'runs':len(index['runs']),'pending':index['pending'],'sourceDeltas':{k:v['changedSourceCount'] for k,v in index['runs'].items()},'write':a.write}))
if __name__=='__main__':main()
