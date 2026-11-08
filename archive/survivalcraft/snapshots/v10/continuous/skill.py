"""Game-time/displacement completion; wall time only for safety deadline."""
from dataclasses import dataclass
import math
@dataclass
class Skill:
 direction: str
 origin: dict
 start_tick: float
 start_wall: float
 target_distance: float=.2
 max_game_seconds: float=1
 max_wall_seconds: float=18
 last_tick: float|None=None
 last_wall: float|None=None
 def evaluate(self,pos,tick,now,health=1,state_age=0):
  try:
   values=[tick,now,health,state_age,self.start_tick,self.start_wall,self.target_distance,self.max_game_seconds,self.max_wall_seconds]+[v[k] for v in (pos,self.origin) for k in ('x','y','z')]
   if not all(type(v) in (float,int) and math.isfinite(v) for v in values):return 'unsafe'
  except (KeyError,TypeError):return 'unsafe'
  if self.direction not in ('forward','backward','stop') or not 0<health<=1 or state_age<0:return 'unsafe'
  if min(self.target_distance,self.max_game_seconds,self.max_wall_seconds)<=0:return 'unsafe'
  if tick<self.start_tick or now<self.start_wall:return 'unsafe'
  if self.last_tick is not None and tick<self.last_tick:return 'unsafe'
  if self.last_wall is not None and now<self.last_wall:return 'unsafe'
  self.last_tick=tick;self.last_wall=now
  d=math.sqrt(sum((pos[k]-self.origin[k])**2 for k in ('x','y','z')))
  if health<1 or state_age>2 or d>2 or abs(pos['y']-self.origin['y'])>.75:return 'unsafe'
  if d>=self.target_distance:return 'distance_complete'
  if tick-self.start_tick>=self.max_game_seconds:return 'game_budget_complete'
  if now-self.start_wall>=self.max_wall_seconds:return 'timeout'
  return 'running'
class SkillSlot:
 def __init__(self):self.active=None;self.prepared=None
 def prepare(self,direction):
  assert direction in ('forward','backward','stop');self.prepared=direction
 def start_if_idle(self,pos,tick,now):
  if self.active is None and self.prepared is not None:
   self.active=Skill(self.prepared,dict(pos),tick,now);self.prepared=None
  return self.active
 def finish(self):self.active=None
