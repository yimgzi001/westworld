namespace RecoveryFixtureNext;
public enum Phase { Ready, AwaitTargetAck, WaitTarget, AwaitWallAck, WaitWall, AwaitRestoreAck, WaitRestore, Complete, NeedsReconcile }
public enum WriteKind { PlaceTarget, PlaceWall, RestoreOriginalContents }
public readonly record struct Snapshot(
 double WallSeconds, double GameTick, bool Safe, bool PathChunksValid,
 bool TargetContentDataMatches, bool WallContentDataMatches, bool Visible,
 bool OriginalContentDataMatches, bool OriginalRawMatches, bool OriginalLightMatches);
public sealed record WriteRequest(long Sequence,WriteKind Kind);
public sealed record Outcome(bool AssertionsPassed,bool ContentDataRestored,bool ChunksValid,
 bool RawMatches,bool LightMatches,string Reason);

/// Pure one-layout state machine. It neither reads terrain nor writes files.
/// Adapter must journal each request BEFORE ChangeCell, then acknowledge once.
/// Fresh readback is required after every write. No write is automatically retried.
public sealed class FixtureSequencer
{
 public Phase Phase {get;private set;}=Phase.Ready;
 public Outcome? Result {get;private set;}
 public bool ExpectedVisible {get;}
 readonly bool testWall; readonly double timeout;
 double lastWall=double.NegativeInfinity,lastTick=double.NegativeInfinity,stageSince,stableTick=double.NegativeInfinity;
 int stable; long sequence; WriteRequest? pending;
 bool assertions=true; string reason="running";
 public FixtureSequencer(bool expectedVisible,bool testWall,double stageTimeoutSeconds=15){
  if(testWall&&!expectedVisible)throw new ArgumentException("Wall test requires visible baseline");
  if(!double.IsFinite(stageTimeoutSeconds)||stageTimeoutSeconds<=0||stageTimeoutSeconds>30)throw new ArgumentOutOfRangeException(nameof(stageTimeoutSeconds));
  ExpectedVisible=expectedVisible;this.testWall=testWall;timeout=stageTimeoutSeconds;
 }
 WriteRequest Emit(WriteKind kind,Phase next,double now){
  Phase=next;stageSince=now;stable=0;stableTick=double.NegativeInfinity;
  pending=new(++sequence,kind);return pending;
 }
 WriteRequest Cleanup(string why,double now){
  assertions=false;reason=why;
  return Emit(WriteKind.RestoreOriginalContents,Phase.AwaitRestoreAck,now);
 }
 public void AcknowledgeWrite(long id,bool confirmed,double wallSeconds){
  if(pending is null||pending.Sequence!=id)throw new InvalidOperationException("No matching pending write");
  if(!confirmed||!double.IsFinite(wallSeconds)||wallSeconds<lastWall){
   Phase=Phase.NeedsReconcile;reason="write_uncertain_or_invalid_clock";pending=null;return;
  }
  lastWall=wallSeconds;stageSince=wallSeconds;
  Phase=pending.Kind switch {WriteKind.PlaceTarget=>Phase.WaitTarget,WriteKind.PlaceWall=>Phase.WaitWall,_=>Phase.WaitRestore};
  pending=null;stable=0;stableTick=double.NegativeInfinity;
 }
 public WriteRequest? Step(Snapshot s){
  if(Phase is Phase.Complete or Phase.NeedsReconcile)return null;
  if(!double.IsFinite(s.WallSeconds)||!double.IsFinite(s.GameTick)||s.WallSeconds<lastWall||s.GameTick<lastTick){
   Phase=Phase.NeedsReconcile;reason="clock_invalid_reconcile_required";return null;
  }
  lastWall=s.WallSeconds;lastTick=s.GameTick;
  if(pending is not null){
   if(s.WallSeconds-stageSince>timeout){Phase=Phase.NeedsReconcile;reason="write_ack_timeout";}
   return null;
  }
  if(Phase==Phase.Ready){
   if(!s.Safe||!s.PathChunksValid||!s.OriginalContentDataMatches){
    Phase=Phase.Complete;Result=new(false,s.OriginalContentDataMatches,s.PathChunksValid,s.OriginalRawMatches,s.OriginalLightMatches,"preflight_rejected");return null;
   }
   return Emit(WriteKind.PlaceTarget,Phase.AwaitTargetAck,s.WallSeconds);
  }
  if(Phase==Phase.WaitRestore){
   if(!s.OriginalContentDataMatches||!s.PathChunksValid){stable=0;stableTick=double.NegativeInfinity;}
   else CountFresh(s.GameTick);
   if(stable>=2){
    Phase=Phase.Complete;Result=new(assertions,true,true,s.OriginalRawMatches,s.OriginalLightMatches,reason=="running"?"completed":reason);return null;
   }
   if(s.WallSeconds-stageSince>timeout){
    Phase=Phase.Complete;Result=new(false,s.OriginalContentDataMatches,s.PathChunksValid,s.OriginalRawMatches,s.OriginalLightMatches,"restore_timeout");return null;
   }
   return null;
  }
  if(!s.Safe)return Cleanup("safety_changed",s.WallSeconds);
  if(s.WallSeconds-stageSince>timeout)return Cleanup("validation_timeout",s.WallSeconds);
  if(!s.TargetContentDataMatches||(Phase==Phase.WaitWall&&!s.WallContentDataMatches))
   return Cleanup("fixture_content_changed",s.WallSeconds);
  if(!s.PathChunksValid){stable=0;stableTick=double.NegativeInfinity;return null;}
  CountFresh(s.GameTick);
  if(stable<2)return null;
  bool expected=Phase==Phase.WaitTarget?ExpectedVisible:false;
  if(s.Visible!=expected)return Cleanup("visibility_assertion_failed",s.WallSeconds);
  if(Phase==Phase.WaitTarget&&testWall)return Emit(WriteKind.PlaceWall,Phase.AwaitWallAck,s.WallSeconds);
  return Emit(WriteKind.RestoreOriginalContents,Phase.AwaitRestoreAck,s.WallSeconds);
 }
 void CountFresh(double tick){if(tick>stableTick){stable++;stableTick=tick;}}
 public string StatusReason=>reason;
}
