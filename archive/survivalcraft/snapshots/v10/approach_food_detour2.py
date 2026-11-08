import pathlib,json,time,os,sys,math,subprocess
sys.path.insert(0,str(pathlib.Path('qwen-lab/navigation').resolve()))
from grid_planner import Cell,Planner
def propose(data,pos,target,visited):
 rows={(r["x"],r["z"]):r for r in data["surface"] if (r["x"],r["z"])!=(-133,61)};cells={k:Cell(k[0],k[1],r["y"]+1,True,r["solid"],"solid",r["headroom"],0) for k,r in rows.items()}
 start=(math.floor(pos[0]),math.floor(pos[2]));goal=(-138,70)
 if start==goal:return {"status":"adjacent_to_food_stop_and_inspect","route":[]}
 route=Planner(max_radius=32,max_steps=48).plan(cells,start,goal,0)
 if route.status!="ok":return {"status":route.status,"route":[]}
 return {"status":"candidate_known_detour","full_observed_route_steps":len(route.cells)-1,"route":[{"center":[x+.5,cells[x,z].y,z+.5]} for x,z in route.cells[:9]]}
p=pathlib.Path('survivalcraft-lab');d=p/'ipc-wet';f=next(f for f in d.glob('*.state.json') if time.time()-f.stat().st_mtime<3);q=f.with_name(f.name.replace('.state.json','.command.json'));visited=set();results=[];deadline=time.monotonic()+180
for ix in range(6):
 s=json.loads(f.read_text())['state']
 if time.monotonic()>deadline or s['health']<1 or s['air']<1 or time.time()-f.stat().st_mtime>2:break
 i=time.time_ns();t=q.with_suffix('.tmp');t.write_text(json.dumps(dict(Id=i,Action='scanfood',Seconds=.4)));os.replace(t,q)
 end=time.monotonic()+3
 while time.monotonic()<end:
  m=d/'food-scan.json';raw=json.loads(m.read_text())
  if raw['id']==i:break
  time.sleep(.1)
 else:raise RuntimeError('scan_ack_missing')
 s=json.loads(f.read_text())['state'];a=s['position'];pos=[a['x'],a['y'],a['z']];visited.add((math.floor(a['x']),math.floor(a['z'])))
 r=propose(dict(raw,surface=raw['walkSurface']),pos,[-139,66,70],visited);r['source_file_mtime_unix']=m.stat().st_mtime
 if not r['status'].startswith('candidate'):results.append({'stage':ix,'status':r['status'],'found':raw['found']});break
 route=p/f'wet-detour2-route-{ix}.json';route.write_text(json.dumps(r,indent=2));name=f'wet-detour2-result-{ix}.json'
 subprocess.run(['python',str(p/'execute_observed_route.py'),str(route),name,'ipc-wet'],check=True)
 v=json.loads((p/name).read_text());results.append({'stage':ix,'outcome':v['outcome'],'stop_ack':v['stop_ack'],'final':v['final']})
 for w in v['waypoints']:
  for sample in w['samples']:
   a=sample['position'];visited.add((math.floor(a['x']),math.floor(a['z'])))
 (p/'wet-detour2-approach-summary.json').write_text(json.dumps({'stages':results,'observed_visited_cells':list(visited),'maximum_stages':12,'no_model':True,'starter_bread_excluded':True},indent=2))
 if v['outcome']!='route_completed' or not v['stop_ack']:break
print(json.dumps(results))
