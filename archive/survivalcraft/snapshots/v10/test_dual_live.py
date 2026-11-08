import json,time,pathlib,sys,math
sys.path.insert(0,str(pathlib.Path(__file__).parent/'Bridge'))
from controller import issue
p=pathlib.Path(__file__).parent; h=p/'ipc';c=p/'ipc-client'
def read(f):return json.loads(f.read_text())
files=list(h.glob('*.state.json'));hf=max(files,key=lambda f:read(f)['lastCommand']);cid=next(f.name.removesuffix('.state.json') for f in files if f!=hf);hid=hf.name.removesuffix('.state.json');cf=c/(cid+'.state.json')
bh,bc=read(hf),read(cf)
assert bh['state']['health']>0 and bc['state']['health']>0
ch=issue(h,hid,'move',x=1,seconds=.5);cc=issue(c,cid,'move',z=-1,seconds=.5)
deadline=time.time()+90
while time.time()<deadline:
 ah,ac=read(hf),read(cf)
 if ah['lastCommand']==ch['Id'] and ac['lastCommand']==cc['Id'] and ah['tickSeconds']>bh['tickSeconds']+1.5 and ac['tickSeconds']>bc['tickSeconds']+1.5:break
 time.sleep(.25)
issue(h,hid,'stop');issue(c,cid,'stop')
r={}
for k,b,a in [('host',bh,ah),('client',bc,ac)]:
 r[k]={'before':b['state']['position'],'after':a['state']['position'],'health':a['state']['health'],'tick_delta':a['tickSeconds']-b['tickSeconds'],'ack':a['lastCommand']==({'host':ch,'client':cc}[k]['Id'])}
r['server_observes_client']=read(h/(cid+'.state.json'))['state']['position'];r['client_observes_host']=read(next(f for f in c.glob('*.state.json') if f!=cf))['state']['position']
(p/'dual-live-v2.json').write_text(json.dumps(r,indent=2));print(json.dumps(r))
