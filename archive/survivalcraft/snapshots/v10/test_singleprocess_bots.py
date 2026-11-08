import json,time,pathlib,sys
sys.path.insert(0,str(pathlib.Path(__file__).parent/'Bridge'))
from controller import issue
p=pathlib.Path(__file__).parent;d=p/'ipc-bot'
def read(f):return json.loads(f.read_text())
fs=sorted(d.glob('*.state.json'));assert len(fs)==2
before=[read(f) for f in fs]
commands=[issue(d,f.name.removesuffix('.state.json'),'move',x=1 if i==0 else -1,seconds=.5) for i,f in enumerate(fs)]
deadline=time.time()+100
while time.time()<deadline:
 after=[read(f) for f in fs]
 if all(a['lastCommand']==c['Id'] and a['tickSeconds']>b['tickSeconds']+1.5 for a,b,c in zip(after,before,commands)):break
 time.sleep(.25)
for f in fs:issue(d,f.name.removesuffix('.state.json'),'stop')
r=[{'player_alias':f'bot{i+1}','before':b['state']['position'],'after':a['state']['position'],'health':a['state']['health'],'ack':a['lastCommand']==c['Id'],'tick_delta':a['tickSeconds']-b['tickSeconds']} for i,(a,b,c) in enumerate(zip(after,before,commands))]
(p/'singleprocess-bots-actions.json').write_text(json.dumps(r,indent=2));print(json.dumps(r))
