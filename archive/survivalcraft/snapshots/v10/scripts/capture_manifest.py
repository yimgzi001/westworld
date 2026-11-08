import pathlib,json,time,hashlib,sys
p=pathlib.Path(__file__).resolve().parent;out={}
for f in (p/'ipc-bot').glob('*.state.json'):
 if time.time()-f.stat().st_mtime>3:continue
 j=json.loads(f.read_text());s=j['state'];out[j['playerAlias']]={'identity_hash':hashlib.sha256(f.name.encode()).hexdigest(),'position':s['position'],'health':s['health'],'inventory_hash':hashlib.sha256(json.dumps(s['slots'],sort_keys=True).encode()).hexdigest()}
(p/sys.argv[1]).write_text(json.dumps(out,indent=2));print('captured',len(out),'roles')
