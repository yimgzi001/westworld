"""Snapshot-only <=8-step progress toward a known resource, never into it.
No blind greedy motion: BFS over observed safe surface, then choose a reachable
endpoint with actual geometric progress. Report blocked rather than assume unknown.
"""
import json,math,argparse,pathlib
from collections import deque
from grid_planner import Cell,Planner

def propose(data,position,target,visited=()):
 start=(math.floor(position[0]),math.floor(position[2]));goal=(int(target[0]),int(target[2]));visited=set(visited)
 base={'snapshot_only':True,'target_block':target,'start_actual':position}
 rows={(r['x'],r['z']):r for r in data['surface'] if all(type(r.get(k))==int for k in ('x','y','z'))}
 cells={p:Cell(p[0],p[1],r['y']+1,True,r.get('solid') is True,'solid' if r.get('type') in ('SandBlock','GrassBlock') else 'unknown',r.get('headroom',0),0) for p,r in rows.items()}
 P=Planner(max_steps=8,max_radius=8)
 if start not in cells or not P.safe(cells[start],0):return dict(base,status='unsafe_or_unknown_start',route=[])
 if abs(position[1]-cells[start].y)>.6:return dict(base,status='start_height_mismatch',route=[])
 def adjacent(p):return abs(p[0]-goal[0])+abs(p[1]-goal[1])==1 and abs(cells[p].y-target[1])<=1
 if adjacent(start):return dict(base,status='adjacent_to_food_stop_and_inspect',route=[])
 q=deque([start]);prev={start:None};depth={start:0};vertical={start:0}
 while q:
  p=q.popleft()
  if depth[p]>=8 or adjacent(p):continue
  for dx,dz in ((1,0),(-1,0),(0,1),(0,-1)):
   n=(p[0]+dx,p[1]+dz)
   if n==goal or n in prev or n not in cells:continue
   if not P.safe(cells[n],0) or abs(cells[n].y-cells[p].y)>1:continue
   prev[n]=p;depth[n]=depth[p]+1;vertical[n]=vertical[p]+abs(cells[n].y-cells[p].y);q.append(n)
 startdist=math.dist(start,goal);choices=[]
 for p in prev:
  if p==start:continue
  progress=startdist-math.dist(p,goal)
  if not adjacent(p) and (progress<.5 or p in visited):continue
  path=[];v=p
  while v is not None:path.append(v);v=prev[v]
  path.reverse()
  # Prefer a safe adjacent endpoint, otherwise best target-distance improvement;
  # small vertical/retrace penalty avoids useless oscillation.
  score=(1000 if adjacent(p) else 0)+progress-.25*vertical[p]-.2*sum(v in visited for v in path[1:])
  choices.append((score,progress,-len(path),p,path))
 if not choices:return dict(base,status='no_safe_progress_within_8_steps',route=[])
 _,progress,_,end,path=max(choices)
 return dict(base,status='candidate_adjacent' if adjacent(end) else 'candidate_progress',distance_before=startdist,distance_after=math.dist(end,goal),progress=progress,route=[{'cell':list(p),'center':[p[0]+.5,cells[p].y,p[1]+.5],'type':rows[p]['type'],'headroom':cells[p].headroom} for p in path])
if __name__=='__main__':
 ap=argparse.ArgumentParser();ap.add_argument('map');ap.add_argument('--position',type=float,nargs=3,required=True);ap.add_argument('--target',type=int,nargs=3,required=True);ap.add_argument('--visited');ap.add_argument('--output',required=True);a=ap.parse_args()
 src=pathlib.Path(a.map);seen=json.loads(pathlib.Path(a.visited).read_text()) if a.visited else [];seen=seen.get('cells',[]) if isinstance(seen,dict) else seen
 out=propose(json.loads(src.read_text()),a.position,a.target,{tuple(v) for v in seen});out['source_file_mtime_unix']=src.stat().st_mtime;pathlib.Path(a.output).write_text(json.dumps(out,indent=2));print(json.dumps(out))
