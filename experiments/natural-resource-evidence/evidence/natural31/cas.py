#!/usr/bin/env python3
"""阶段31归档共用工具：内容寻址、旧包复用、解码校验。没有游戏或网络入口。"""
import base64,gzip,hashlib,json,pathlib,re
PART_BYTES=29000
SECRET=re.compile(rb'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|(?:api[_-]?key|access[_-]?token|password)\s*[:=]\s*[\x22\x27][A-Za-z0-9_./+\-=]{20,}[\x22\x27])',re.I)
def sha(raw):return hashlib.sha256(raw).hexdigest()
def packed(value):return json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()
def safe(root,relative):
 p=pathlib.PurePosixPath(relative)
 if p.is_absolute() or '..' in p.parts:raise ValueError('拒绝路径越界：'+relative)
 path=root/relative
 if path.is_symlink() or any(parent.is_symlink() for parent in path.parents):raise ValueError('拒绝符号链接：'+relative)
 return path
def read_text(path):
 raw=path.read_bytes()
 if SECRET.search(raw):raise ValueError('凭据特征命中：'+str(path))
 return raw.decode()
def decode(root,ref):
 path=safe(root,ref['manifest']);raw=path.read_bytes()
 if sha(raw)!=ref['sha256']:raise ValueError('manifest哈希不符：'+ref['manifest'])
 meta=json.loads(raw);parts=[];files=[ref['manifest']]
 for item in meta['parts']:
  relative=(path.parent.relative_to(root)/item['file']).as_posix();data=safe(root,relative).read_bytes()
  if sha(data)!=item['sha256'] or len(data)!=item['bytes'] or len(data)>PART_BYTES:raise ValueError('分片哈希/长度不符：'+relative)
  parts.append(data);files.append(relative)
 zipped=base64.b64decode(b''.join(parts),validate=True)
 if sha(zipped)!=meta['gzipSHA256']:raise ValueError('压缩数据哈希不符')
 payload=gzip.decompress(zipped)
 if sha(payload)!=meta['rawSHA256'] or len(payload)!=meta['rawBytes']:raise ValueError('解码数据哈希不符')
 if SECRET.search(payload):raise ValueError('解码后凭据特征命中')
 return json.loads(payload),meta,files

class Store:
 """先在内存准备，所有运行就绪后才commit；复用旧包不会重新压缩或复制分片。"""
 def __init__(self,root,out,reuse_dirs=()):
  self.root=pathlib.Path(root).resolve();self.out=pathlib.Path(out).resolve()
  self.out.relative_to(self.root)
  self.refs={};self.pending={};self.catalog={};self.dependencies={};self.by_hash={}
  for relative in reuse_dirs:
   directory=safe(self.root,relative);manifest=directory/'manifest.json';original=manifest.read_bytes();index=json.loads(original)
   bundles=index.get('bundles',{})
   if not bundles:
    for part in index.get('bundleIndexes',[]):
     raw=safe(directory,part['file']).read_bytes()
     if sha(raw)!=part['sha256']:raise ValueError('旧包索引哈希不符')
     bundles.update(json.loads(raw))
   for key,ref in bundles.items():
    meta_name=ref.get('manifest',key+'-manifest.json');meta_path=safe(directory,meta_name)
    raw=meta_path.read_bytes()
    if sha(raw)!=ref['sha256']:raise ValueError('旧包manifest哈希不符')
    meta=json.loads(raw)
    self.catalog.setdefault(meta['rawSHA256'],(key,{'manifest':meta_path.relative_to(self.root).as_posix(),'sha256':ref['sha256'],'storage':'shared'},relative,sha(original)))
 def add(self,kind,value):
  raw=packed(value)
  if SECRET.search(raw):raise ValueError('待归档内容存在凭据特征')
  digest=sha(raw)
  if digest in self.by_hash:return self.by_hash[digest]
  if digest in self.catalog:
   key,ref,origin,origin_sha=self.catalog[digest];restored,meta,_=decode(self.root,ref)
   if packed(restored)!=raw:raise ValueError('旧包内容不一致')
   self.refs[key]=ref;self.dependencies[origin]={'manifest':origin+'/manifest.json','sha256':origin_sha}
  else:
   key=kind+'-'+digest[:20];zipped=gzip.compress(raw,mtime=0);encoded=base64.b64encode(zipped);parts=[]
   for offset in range(0,len(encoded),PART_BYTES):
    name=f'{key}.gz.b64.part{offset//PART_BYTES+1:03}';part=encoded[offset:offset+PART_BYTES]
    self.pending[name]=part;parts.append({'file':name,'bytes':len(part),'sha256':sha(part)})
   metadata=packed({'rawSHA256':digest,'rawBytes':len(raw),'gzipSHA256':sha(zipped),'parts':parts})
   name=key+'-manifest.json';self.pending[name]=metadata
   self.refs[key]={'manifest':(self.out.relative_to(self.root)/name).as_posix(),'sha256':sha(metadata),'storage':'local'}
  self.by_hash[digest]=key
  return key
 def commit(self):
  self.out.mkdir(parents=True,exist_ok=True)
  # 本工具不会删除旧输出；若内容不同，调用方须使用新的批次目录。
  for name,raw in self.pending.items():
   p=safe(self.out,name)
   if p.exists() and p.read_bytes()!=raw:raise ValueError('拒绝覆盖不同内容：'+name)
  for name,raw in self.pending.items():safe(self.out,name).write_bytes(raw)
 def counts(self):
  return {'referencedBundles':len(self.refs),'newBundles':sum(ref['storage']=='local' for ref in self.refs.values()),'reusedBundles':sum(ref['storage']=='shared' for ref in self.refs.values()),'newPayloadFiles':len(self.pending)}

VALIDATOR_SHA256='e2f1e19336ece5e14395f02258a656925dc4ed54449933bda7b7cf4d356edd17'
def validate_frozen(source,rows):
 """仅执行已审阅且固定哈希的纯离线验证器，绝不执行宿主/驱动或日志内容。"""
 if sha(source.encode())!=VALIDATOR_SHA256:raise ValueError('离线验证器不是本次冻结版本')
 namespace={'__name__':'frozen_natural31_offline_validator'}
 exec(compile(source,'frozen-natural31-validate.py','exec'),namespace)
 return namespace['validate_log'](rows)
