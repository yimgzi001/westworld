#!/usr/bin/env python3
"""只读校验阶段30后验控制器小包，不执行解码源码或调用网络/API。"""
import base64,gzip,hashlib,json,pathlib,re
HERE=pathlib.Path(__file__).resolve().parent
sha=lambda raw:hashlib.sha256(raw).hexdigest()
secret=re.compile(rb'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|(?:api[_-]?key|access[_-]?token|password)\s*[:=]\s*[\x22\x27][A-Za-z0-9_./+\-=]{20,}[\x22\x27])',re.I)
def read(name):
 path=pathlib.PurePosixPath(name);assert not path.is_absolute() and '..' not in path.parts
 return (HERE/name).read_bytes()
m=json.loads(read('manifest.json'));decoded={};reachable={'manifest.json','README.md','archive.py','verify.py'};parts_checked=0
for key,ref in m['bundles'].items():
 name=key+'-manifest.json';raw=read(name);assert sha(raw)==ref['sha256'];reachable.add(name);meta=json.loads(raw);parts=[]
 for item in meta['parts']:
  raw=read(item['file']);assert sha(raw)==item['sha256'] and len(raw)==item['bytes']<=29000
  parts.append(raw);reachable.add(item['file']);parts_checked+=1
 zipped=base64.b64decode(b''.join(parts),validate=True);assert sha(zipped)==meta['gzipSHA256']
 raw=gzip.decompress(zipped);assert sha(raw)==meta['rawSHA256'] and len(raw)==meta['rawBytes'] and key.endswith(sha(raw)[:20])
 assert not secret.search(raw),key
 decoded[key]=json.loads(raw)
texts={};used=set();source_count=0
for path,ref in m['files'].items():
 text=decoded[ref['bundle']];texts[path]=text;used.add(ref['bundle'])
 assert sha(text.encode())==ref['archivedTextSHA256']
 if ref['frozenSource']:
  assert ref['archivedTextSHA256']==ref['originalSHA256'];source_count+=1
 else:assert '/workspace/scratch/' not in text
assert used==decoded.keys() and source_count==3
prefix='work/controller-lifecycle30/'
assert 'Ran 53 tests' in texts[prefix+'tests.log'] and texts[prefix+'tests.log'].rstrip().endswith('OK')
assert texts[prefix+'exit.txt'].strip()=='0'
assert 'Ran 53 tests' in texts[prefix+'review-tests.log'] and texts[prefix+'review-tests.log'].rstrip().endswith('OK')
for line in texts[prefix+'source-sha256.txt'].splitlines():
 digest,path=line.split(None,1);assert sha(texts[path.removeprefix('westworld-cloud/')].encode())==digest
assert m['finalTests']==53 and m['independentReviewTests']==53 and m['modelOrNativeValidation'] is False
orphans=[p.name for p in HERE.iterdir() if p.is_file() and p.name not in reachable];assert not orphans
assert all(not p.is_symlink() for p in HERE.iterdir())
print(json.dumps({'evidenceFiles':len(texts),'bundles':len(decoded),'parts':parts_checked,'frozenSourceChecks':source_count,'finalOfflineTests':53,'independentReviewTests':53,'modelOrNativeValidation':False,'files':len(reachable),'bytes':sum((HERE/name).stat().st_size for name in reachable),'largestFileBytes':max((HERE/name).stat().st_size for name in reachable),'orphanFiles':orphans}))
