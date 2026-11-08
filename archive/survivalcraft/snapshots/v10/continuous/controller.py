"""Transport-independent continuous controller. No network, model calls or game I/O.
Host supplies observed state and a command sink. Wall-clock safety TTLs do not
substitute for the game-thread 0.4-game-second command lease.
"""
from dataclasses import dataclass
from concurrent.futures import ThreadPoolExecutor
from threading import BoundedSemaphore
import math

@dataclass(frozen=True)
class State:
    alias: str
    observed_at: float  # local monotonic receive time; never model-supplied
    health: float
    observation: dict

@dataclass(frozen=True)
class Plan:
    action: str
    x: float = 0
    z: float = 0
    horizon: float = 3

class PlannerPool:
    """Bounded global concurrency/backlog; None means retry later, never block tick."""
    def __init__(self, planner, workers=4, capacity=20):
        self.planner=planner; self.executor=ThreadPoolExecutor(max_workers=workers)
        self.capacity=BoundedSemaphore(capacity)
    def submit(self, state):
        if not self.capacity.acquire(blocking=False): return None
        try: f=self.executor.submit(self.planner,state)
        except BaseException: self.capacity.release(); raise
        f.add_done_callback(lambda _:self.capacity.release())
        return f
    def close(self): self.executor.shutdown(wait=True,cancel_futures=True)

class Controller:
    def __init__(self, alias, planner, sink, stale_after=2, max_plan_age=5,
                 refresh=.1, lease=.4, replan_after=1, damage_stop=.05):
        assert 0<refresh<lease<=.5 and stale_after>0 and max_plan_age>0
        self.alias=alias; self.planner=planner; self.sink=sink
        self.stale_after=stale_after; self.max_plan_age=max_plan_age
        self.refresh=refresh; self.lease=lease; self.replan_after=replan_after
        self.damage_stop=damage_stop
        self.plan=None; self.pending=None; self.expiry=0; self.next_plan=0
        self.last_send=-math.inf; self.mode='idle'; self.health=None
        self.generation=0; self.emergency_latched=False; self.events=[]
    def _send(self, action, now, x=0,z=0):
        self.sink({'action':action,'x':x,'y':0,'z':z,'seconds':self.lease})
        self.last_send=now
    def emergency(self, now, why):
        self.generation+=1; self.plan=None; self.expiry=0
        if self.pending: self.pending[0].cancel()
        # Retain non-cancellable pending future to avoid piling up more work.
        self.emergency_latched=True
        if self.mode!='unsafe': self._send('stop',now)
        self.mode='unsafe'; self.events.append((now,why))
    def resume(self):
        # Explicit caller decision after checking fresh safe observation.
        self.emergency_latched=False; self.health=None
    @staticmethod
    def valid(p):
        return (isinstance(p,Plan) and p.action in {'move','stop'} and
          all(type(v) in (int,float) and math.isfinite(v) for v in (p.x,p.z,p.horizon)) and
          abs(p.x)<=1 and abs(p.z)<=1 and 0<p.horizon<=5 and
          (p.action!='stop' or p.x==p.z==0))
    def tick(self,state,now):
        age=now-state.observed_at
        bad=(state.alias!=self.alias or not math.isfinite(age) or age<0 or age>self.stale_after or
             not math.isfinite(state.health) or state.health<=0 or state.health>1)
        damage=self.health is not None and self.health-state.health>=self.damage_stop
        if bad or damage:
            if not self.emergency_latched:self.emergency(now,'stale/invalid/dead/damaged')
            return
        self.health=state.health
        if self.emergency_latched:return
        if self.pending and self.pending[0].done():
            f,gen,requested_at=self.pending; self.pending=None
            try: p=f.result()
            except BaseException: p=None
            if gen==self.generation and now-requested_at<=self.max_plan_age and self.valid(p):
                self.plan=p; self.expiry=min(now+p.horizon,requested_at+self.max_plan_age)
            self.next_plan=now+self.replan_after
        if self.plan is not None and now>=self.expiry:
            self.plan=None; self._send('stop',now); self.mode='idle'
        if self.pending is None and now>=self.next_plan:
            f=self.planner.submit(state)
            if f is not None:self.pending=(f,self.generation,now)
        if self.plan is not None:
            self.mode='executing+planning' if self.pending else 'executing'
            if now-self.last_send>=self.refresh:
                self._send(self.plan.action,now,self.plan.x,self.plan.z)
        elif self.mode not in ('idle','planning'):
            self._send('stop',now); self.mode='planning'
        else:self.mode='planning'
