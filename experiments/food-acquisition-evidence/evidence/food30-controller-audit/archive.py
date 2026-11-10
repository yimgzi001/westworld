#!/usr/bin/env python3
"""阶段30运行后控制器离线修复小包，仅封存指定冻结源码与已完成测试日志。
不启动游戏、测试、网络或API，不更改food30两场运行证据。
"""
import argparse,base64,gzip,hashlib,json,pathlib,re
HERE=pathlib.Path(__file__).resolve().parent
ROOT=HERE.parents[3]
SOURCES={
 'experiments/openai-tool-controller/controller.py':'9bb584494d1776994df5e6c4c753fdb083d2dde5ebc5340cfa203fabf5b31501',
 'experiments/openai-tool-controller/test_controller.py':'d13605f9102a00b14f7e1e724ffbd6346f06468fca6961a22493791ddb0baa5a',
 'experiments/openai-tool-controller/README.md':'5204ca9e6732577b5b9ffcad1774d594a577931dab29a79c2b46382fe50d99d7'}
LOGS=tuple('work/controller-lifecycle30/'+name for name in ('tests.log','exit.txt','source-sha256.txt','review-tests.log'))
SECRET=re.compile(rb'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|(?:api[_-]?key|access[_-]?token|password)\s*[:=]\s*[\x22\x27][A-Za-z0-9_./+\-=]{20,}[\x22\x27])',re.I)
def sha(raw):return hashlib.sha256(raw).hexdigest()
def packed(value):return json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()
def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--write',action='store_true');args=ap.parse_args()
 index={'schema':'westworld-food30-postrun-controller-audit-v1','files':{},'bundles':{},
  'scope':'两场结束后的控制器修复；未在food30-terra或food30-deterministic执行。53项离线测试，独立复审亦为53项。无新增模型/API或原生游戏验证。',
  'fix':'replay先原子持久原有audit再写events.result；sidecar失败仍保留usage。',
  'sanitization':'日志云仓库绝对路径替换为<CLOUD_REPO>；源码原字节保存。凭据特征命中即拒绝。',
  'finalTests':53,'independentReviewTests':53,'modelOrNativeValidation':False}
 for path in (*SOURCES,*LOGS):
  p=ROOT/path;assert not p.is_symlink() and not any(parent.is_symlink() for parent in p.parents)
  original=p.read_bytes();assert not SECRET.search(original),path
  frozen=path in SOURCES
  if frozen:assert sha(original)==SOURCES[path],'冻结源码变更，拒绝自动替换：'+path
  text=original.decode();safe=text if frozen else text.replace(str(ROOT),'<CLOUD_REPO>')
  raw=packed(safe);key=('source-file-' if frozen else 'audit-file-')+sha(raw)[:20]
  if key not in index['bundles']:
   zipped=gzip.compress(raw,mtime=0);encoded=base64.b64encode(zipped);parts=[]
   for offset in range(0,len(encoded),29000):
    name=f'{key}.gz.b64.part{offset//29000+1:03}';part=encoded[offset:offset+29000]
    parts.append({'file':name,'bytes':len(part),'sha256':sha(part)})
    if args.write:(HERE/name).write_bytes(part)
   meta=packed({'rawSHA256':sha(raw),'rawBytes':len(raw),'gzipSHA256':sha(zipped),'parts':parts})
   if args.write:(HERE/(key+'-manifest.json')).write_bytes(meta)
   index['bundles'][key]={'sha256':sha(meta)}
  index['files'][path]={'bundle':key,'originalSHA256':sha(original),'archivedTextSHA256':sha(safe.encode()),'frozenSource':frozen}
 assert 'Ran 53 tests' in (ROOT/LOGS[0]).read_text() and (ROOT/LOGS[1]).read_text().strip()=='0'
 assert 'Ran 53 tests' in (ROOT/LOGS[3]).read_text()
 for line in (ROOT/LOGS[2]).read_text().splitlines():
  digest,path=line.split(None,1);assert SOURCES[path.removeprefix('westworld-cloud/')]==digest
 if args.write:(HERE/'manifest.json').write_bytes(packed(index)+b'\n')
 print(json.dumps({'evidenceFiles':len(index['files']),'bundles':len(index['bundles']),'frozenSources':len(SOURCES),'finalOfflineTests':53,'independentReviewTests':53,'write':args.write}))
if __name__=='__main__':main()
