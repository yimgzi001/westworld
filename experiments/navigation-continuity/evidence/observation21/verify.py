#!/usr/bin/env python3
"""Offline decode/hash/safe-text verification. Does not execute restored code or logs."""
import base64,gzip,hashlib,json,pathlib,re
P=pathlib.Path(__file__).resolve().parent
sha=lambda b:hashlib.sha256(b).hexdigest()
m=json.loads((P/'manifest.json').read_text());decoded={};reachable={'manifest.json','README.md','archive.py','verify.py'}
secret=re.compile(rb'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)')
for key,ref in m['bundles'].items():
 raw=(P/ref['manifest']).read_bytes();assert sha(raw)==ref['sha256'];reachable.add(ref['manifest']);b=json.loads(raw);parts=[]
 for item in b['parts']:
  part=(P/item['file']).read_bytes();assert len(part)==item['bytes'] and sha(part)==item['sha256'];parts.append(part);reachable.add(item['file'])
 z=base64.b64decode(b''.join(parts),validate=True);assert sha(z)==b['gzipSHA256'];raw=gzip.decompress(z)
 assert len(raw)==b['rawBytes'] and sha(raw)==b['rawSHA256'];assert not secret.search(raw),key
 decoded[key]=json.loads(raw)
for name,ref in m['runs'].items():
 raw=(P/ref['manifest']).read_bytes();assert sha(raw)==ref['sha256'];reachable.add(ref['manifest']);r=json.loads(raw);source=decoded[r['sourceDelta']]
 inventory=decoded[source['inventoryBase']].copy();inventory.update(source['inventoryChanges'])
 for path in source['inventoryRemoved']:del inventory[path]
 assert len(inventory)==r['sourceCount']
 for path,key in source['changedOrNewFiles'].items():assert sha(decoded[key].encode())==inventory[path]
 logs={path:decoded[key] for path,key in r['logs'].items()}
 if 'source-hashes.json' in logs:assert json.loads(logs['source-hashes.json'])==inventory
 assert int(logs['exit.txt'].strip())==r['exit']
 assert all(not pathlib.PurePosixPath(path).is_absolute() and '..' not in pathlib.PurePosixPath(path).parts for path in inventory)
assert m['archiveComplete'] and not m['pending']
print(json.dumps({'runs':len(m['runs']),'bundles':len(decoded),'sourceFileVersions':sum(k.startswith('source-file-') for k in decoded),'complete':True,'reachableBytes':sum((P/f).stat().st_size for f in reachable),'largestFileBytes':max((P/f).stat().st_size for f in reachable),'orphanFiles':[p.name for p in P.iterdir() if p.is_file() and p.name not in reachable]}))
