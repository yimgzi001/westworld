"""Conservative planner over caller-normalized, observed loaded surface cells.
No game reads/writes. Unknown labels are not traversable. A route is a proposal,
not proof of collision-free terrain; execution must revalidate local observations.
"""
from dataclasses import dataclass
from collections import deque
import math

@dataclass(frozen=True)
class Cell:
 x:int
 z:int
 y:float # canonical walk/feet height, not arbitrary GetTopHeight
 loaded:bool
 walkable:bool
 kind:str # solid, water, lava, foliage, hazard, unknown
 headroom:float
 observed_at:float

@dataclass(frozen=True)
class Route:
 status:str
 cells:tuple
 expanded:int

class Planner:
 def __init__(self,max_step=1,max_radius=32,max_steps=48,max_nodes=4096,max_age=10):
  self.max_step=max_step;self.max_radius=max_radius;self.max_steps=max_steps;self.max_nodes=max_nodes;self.max_age=max_age
 def safe(self,c,now):
  return (c.loaded and c.walkable and c.kind=='solid' and
    all(type(v) in (int,float) and math.isfinite(v) for v in (c.x,c.z,c.y,c.headroom,c.observed_at,now)) and
    type(c.x)==int and type(c.z)==int and c.headroom>=2 and 0<=now-c.observed_at<=self.max_age)
 def plan(self,cells,start,goal,now):
  if start not in cells or goal not in cells:return Route('unknown_endpoint',(),0)
  if not self.safe(cells[start],now) or not self.safe(cells[goal],now):return Route('unsafe_endpoint',(),0)
  if math.dist(start,goal)>self.max_radius:return Route('out_of_range',(),0)
  q=deque([start]);prev={start:None};depth={start:0};expanded=0
  while q and expanded<self.max_nodes:
   p=q.popleft();expanded+=1
   if p==goal:
    path=[]
    while p is not None:path.append(p);p=prev[p]
    return Route('ok',tuple(reversed(path)),expanded)
   if depth[p]>=self.max_steps:continue
   for dx,dz in ((1,0),(-1,0),(0,1),(0,-1)):
    n=(p[0]+dx,p[1]+dz)
    if n in prev or n not in cells or math.dist(start,n)>self.max_radius:continue
    c=cells[n]
    if (c.x,c.z)!=n or not self.safe(c,now) or abs(c.y-cells[p].y)>self.max_step:continue
    # No diagonal corner cutting and no jump/drop beyond one block.
    prev[n]=p;depth[n]=depth[p]+1;q.append(n)
  return Route('budget_exhausted' if q else 'no_safe_route',(),expanded)
 def exploration(self,cells,start,visited,now):
  # Prefer a reachable, observed land cell near an unknown boundary. Never step
  # into unknown land to discover whether it is safe.
  candidates=[]
  for p,c in cells.items():
   if p not in visited and p!=start and self.safe(c,now) and math.dist(p,start)<=self.max_radius:
    unknown=sum((p[0]+dx,p[1]+dz) not in cells for dx,dz in ((1,0),(-1,0),(0,1),(0,-1)))
    candidates.append((-unknown,math.dist(start,p),p))
  # Bounded candidate search prevents map-size explosion.
  for _,_,p in sorted(candidates)[:64]:
   r=self.plan(cells,start,p,now)
   if r.status=='ok':return r
  return Route('no_safe_exploration_goal',(),0)

class ProgressGuard:
 """Independent bounded execution monitor. Caller sends STOP on any non-running status."""
 def __init__(self,origin,now,max_distance=32,max_seconds=30,stall_seconds=4,min_progress=.08):
  self.origin=origin;self.start=now;self.last_time=now;self.last_improvement=now
  self.max_distance=max_distance;self.max_seconds=max_seconds;self.stall_seconds=stall_seconds;self.min_progress=min_progress
  self.best=math.inf;self.target=None
 def check(self,pos,target,now,health,air,state_age,route_valid=True):
  vals=(*pos,*target,now,health,air,state_age)
  if not all(type(x) in (int,float) and math.isfinite(x) for x in vals):return 'unsafe'
  if now<self.last_time or state_age<0 or state_age>2 or health<1 or air<1 or not route_valid:return 'unsafe'
  self.last_time=now
  if math.dist(pos,self.origin)>self.max_distance:return 'range_stop'
  if now-self.start>=self.max_seconds:return 'deadline_stop'
  if self.target!=target:self.target=target;self.best=math.inf;self.last_improvement=now
  d=math.dist(pos,target)
  if d<=.2:return 'waypoint_reached'
  if self.best-d>=self.min_progress:self.best=d;self.last_improvement=now
  if now-self.last_improvement>=self.stall_seconds:return 'stall_stop'
  return 'running'
