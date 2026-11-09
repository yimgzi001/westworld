using System.Runtime.CompilerServices;
namespace Westworld.Scapi.Adapter;

public sealed record NavigationOwnerIdle(bool DirectActionIdle,bool TaskIdle);

/// <summary>Game-thread host arbitration. All host dispatch paths must honor this gate.
/// The status callback MUST inspect actual native direct action and task state, not a cached flag.
/// It cannot intercept a caller that bypasses arbitration and calls AtomicActions directly.</summary>
public sealed class LocalNavigationSkillLease {
 sealed class OwnerState { public Guid Held;public bool Quarantined; }
 sealed class SessionState { public double Last=double.NaN;public bool Faulted; }
 static readonly ConditionalWeakTable<object,OwnerState> owners=new();
 static readonly ConditionalWeakTable<object,SessionState> sessions=new();
 readonly object ownerIdentity,sessionIdentity;readonly OwnerState owner;readonly SessionState session;
 readonly Func<NavigationOwnerIdle> readActualIdle;readonly int thread=Environment.CurrentManagedThreadId;
 public LocalNavigationSkillLease(object existingActions,object observationSession,Func<NavigationOwnerIdle> readActualIdle){
  ArgumentNullException.ThrowIfNull(existingActions);ArgumentNullException.ThrowIfNull(observationSession);ArgumentNullException.ThrowIfNull(readActualIdle);
  ownerIdentity=existingActions;sessionIdentity=observationSession;this.readActualIdle=readActualIdle;
  owner=owners.GetValue(existingActions,_=>new());session=sessions.GetValue(observationSession,_=>new());
 }
 public bool Matches(object actions,object senses)=>ReferenceEquals(ownerIdentity,actions)&&ReferenceEquals(sessionIdentity,senses);
 void Thread(){if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Game thread required");}
 public bool ClockFaulted=>session.Faulted;
 public bool Quarantined=>owner.Quarantined;
 public bool ValidateClock(double now){Thread();if(session.Faulted)return false;
  if(!double.IsFinite(now)||now<0||(!double.IsNaN(session.Last)&&now<session.Last)){session.Faulted=true;return false;}session.Last=now;return true;}
 public bool TryAcquire(Guid request,double now){Thread();if(request==Guid.Empty||!ValidateClock(now)||owner.Quarantined||owner.Held!=Guid.Empty)return false;
  var idle=readActualIdle();if(!idle.DirectActionIdle||!idle.TaskIdle)return false;owner.Held=request;return true;}
 public bool IsHeld(Guid request){Thread();return request!=Guid.Empty&&owner.Held==request&&!owner.Quarantined;}
 public void Release(Guid request,bool physicallySettled){Thread();if(request==Guid.Empty||owner.Held!=request)throw new InvalidOperationException("Not lease owner");
  owner.Held=Guid.Empty;if(!physicallySettled||session.Faulted)owner.Quarantined=true;}
}
