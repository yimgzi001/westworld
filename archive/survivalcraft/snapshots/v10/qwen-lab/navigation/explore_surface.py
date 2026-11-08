"""Bounded, deterministic snapshot-only exploration proposal; no game commands.
Visited must be supplied from observed execution, not inferred from planned routes.
"""
import json,math,argparse,pathlib
from collections import deque
from grid_planner import Cell,Planner

def propose(data,position,visited,max_steps=8):
 rows={(r['x'],r['z']):r for r in data['surface'] if type(r.get('x'))==int and type(r.get('z'))==int and type(r.get('y'))==int}
 cells={p:Cell(p[0],p[1],r['y']+1,True,r.get('solid') is True,'solid' if r.get('type') in ('SandBlock','GrassBlock') else 'unknown',r.get('headroom',0),0) for p,r in rows.items()}
 start=(math.floor(position[0]),math.floor(position[2]));P=Planner(max_steps=max_steps,max_radius=max_steps)
 base={'snapshot_only':True,'start_actual':position,'visited_count':len(visited)}
 if data.get('found'):return dict(base,status='food_observed_stop_exploration',route=[])
 if start not in cells or not P.safe(cells[start],0):return dict(base,status='unsafe_or_unknown_start',route=[])
 if abs(position[1]-cells[start].y)>.6:return dict(base,status='start_height_mismatch',route=[])
 # BFS once: bounded reachable graph, no repeated searches through unknown land.
 q=deque([start]);prev={start:None};depth={start:0};vertical={start:0}
 while q:
  p=q.popleft()
  if depth[p]>=max_steps:continue
  for dx,dz in ((1,0),(-1,0),(0,1),(0,-1)):
   n=(p[0]+dx,p[1]+dz)
   if n in prev or n not in cells:continue
   c=cells[n]
   if not P.safe(c,0) or abs(c.y-cells[p].y)>1:continue
   prev[n]=p;depth[n]=depth[p]+1;vertical[n]=vertical[p]+abs(c.y-cells[p].y);q.append(n)
 waters=[p for p,r in rows.items() if r.get('type')=='WaterBlock']
 def water_distance(p):return min((math.dist(p,w) for w in waters),default=8)
 start_water=water_distance(start);candidates=[]
 for p in prev:
  if p==start or p in visited or rows[p]['type']!='GrassBlock' or depth[p]<3:continue
  path=[];v=p
  while v is not None:path.append(v);v=prev[v]
  path.reverse();novel=sum(n not in visited for n in path[1:])
  density=sum(rows.get((p[0]+dx,p[1]+dz),{}).get('type')=='GrassBlock' for dx in range(-2,3) for dz in range(-2,3))
  water=water_distance(p)
  if water<start_water-1:continue # Avoid heading substantially back toward known water.
  # Relative observed-water clearance is a proxy, not a universal inland compass.
  score=2*novel+.3*density+min(water,12)-.8*vertical[p]
  candidates.append((score,water,novel,-vertical[p],p,path,density))
 if not candidates:return dict(base,status='no_unvisited_safe_grass_goal',route=[],reachable_cells=len(prev))
 best=max(candidates);_,water,novel,_,goal,path,density=best
 return dict(base,status='candidate',goal=list(goal),rationale={'new_path_cells':novel,'nearby_grass_25':density,'observed_water_distance':water,'start_observed_water_distance':start_water,'vertical_change_total':vertical[goal]},route=[{'cell':list(p),'center':[p[0]+.5,cells[p].y,p[1]+.5],'type':rows[p]['type'],'headroom':cells[p].headroom} for p in path])
if __name__=='__main__':
 ap=argparse.ArgumentParser();ap.add_argument('map');ap.add_argument('--position',nargs=3,type=float,required=True);ap.add_argument('--visited',required=True);ap.add_argument('--output',required=True);a=ap.parse_args()
 src=pathlib.Path(a.map);raw=json.loads(src.read_text());seen=json.loads(pathlib.Path(a.visited).read_text());seen=seen.get('cells',[]) if isinstance(seen,dict) else seen
 out=propose(raw,a.position,{tuple(p) for p in seen});out['source_file_mtime_unix']=src.stat().st_mtime;pathlib.Path(a.output).write_text(json.dumps(out,indent=2));print(json.dumps(out))
