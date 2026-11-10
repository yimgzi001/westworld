using Engine;
using Game;
using System.Text.Json;

// Isolated artificial component controls. Terrain/starting needs are declared;
// actual climbing height and sleeping transitions are produced only by native tools.
internal sealed partial class ToolAgentScenario {
 Point3 body42Ladder;
 float body42ClimbStart,body42ClimbGoal;
 bool body42SleepFixture;
 float body42InitialSleep,body42InitialWetness;
 void Body42ResetPose(){
  if(actions.IsActionRunning)throw new InvalidOperationException("body42_fixture_requires_idle_owner");
  p.ComponentBody.Position=new Vector3(matrix40X+.5f,matrix40Y,matrix40Z+.5f);p.ComponentBody.Velocity=Vector3.Zero;
  p.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);
  p.ComponentLocomotion.LookAngles=Vector2.Zero;p.ComponentLocomotion.LookOrder=Vector2.Zero;
 }
 void Body42PrepareLadder(){
  Body42ResetPose();body42Ladder=new(matrix40X+1,matrix40Y,matrix40Z);
  int value=Terrain.MakeBlockValue(WoodenLadderBlock.Index,0,LadderBlock.SetFace(0,3));
  // Face 3 normal is -X; backing is ladderCell - normal, i.e. +X.
  // Native rung continuity/attachment rules remain in force.
  for(int y=matrix40Y;y<=matrix40Y+3;y++)Matrix40Set(matrix40X+2,y,matrix40Z,GraniteBlock.Index);
  for(int y=matrix40Y;y<=matrix40Y+3;y++)Matrix40Set(matrix40X+1,y,matrix40Z,value);
  body42ClimbStart=p.ComponentBody.Position.Y;body42ClimbGoal=matrix40Y+2;
  ToolAgentGate.Log(new{kind="body42_climb_fixture",artificial=true,worldEdited=true,positionEdited=true,rotationEdited=true,ladderFace=3,rungs=4,ladderCell=new[]{body42Ladder.X,body42Ladder.Y,body42Ladder.Z},startY=body42ClimbStart,goalY=body42ClimbGoal,ladderStateInjected=false,flightEnabled=false,modelApi=false});
 }
 string Body42LadderReference()=>Matrix40Block(body42Ladder.X,body42Ladder.Y+1,body42Ladder.Z);
 void Body42VerifyClimb(){
  var own=Matrix40Read("read_self",new{});float actual=own.GetProperty("position").GetProperty("y").GetSingle();
  Matrix40Check(actual-body42ClimbStart>1.5f,"climb42_actual_public_height_increased");
  Matrix40Check(MathF.Abs(actual-body42ClimbGoal)<=.12f,"climb42_actual_height_matches_goal");
  Matrix40Check(MathF.Abs(p.ComponentBody.Position.Y-actual)<.001f,"climb42_public_and_native_height_match");
  Matrix40Check(p.ComponentLocomotion.LadderValue.HasValue,"climb42_native_ladder_engaged");
  ToolAgentGate.Log(new{kind="body42_climb_effect",artificial=true,startY=body42ClimbStart,goalY=body42ClimbGoal,actualY=actual,nativeLadderEngaged=p.ComponentLocomotion.LadderValue.HasValue,heightForcedAfterDispatch=false,modelApi=false});
 }
 void Body42PrepareSleep(){
  Body42ResetPose();body42InitialSleep=p.ComponentVitalStats.Sleep;body42InitialWetness=p.ComponentVitalStats.Wetness;body42SleepFixture=true;
  for(int x=matrix40X-2;x<=matrix40X+2;x++)for(int z=matrix40Z-2;z<=matrix40Z+2;z++){
   Matrix40Set(x,matrix40Y-1,z,GrassBlock.Index);Matrix40Set(x,matrix40Y+3,z,PlanksBlock.Index);
  }
  // Satisfy tired/dry preconditions only. Never call ComponentSleep.Sleep directly,
  // modify its timers/conditions, or fabricate its public sleeping state.
  p.ComponentVitalStats.Sleep=.5f;p.ComponentVitalStats.Wetness=0;
  ToolAgentGate.Log(new{kind="body42_sleep_fixture",artificial=true,worldEdited=true,positionEdited=true,needsEdited=true,originalSleep=body42InitialSleep,originalWetness=body42InitialWetness,injectedSleep=.5f,injectedWetness=0,ground="grass",roof="5x5 planks at +3",sleepStateEdited=false,conditionsRemoved=false,modelApi=false});
 }
 void Body42VerifySleepState(bool expected,string phase){
  bool actual=Matrix40Read("read_self",new{}).GetProperty("isSleeping").GetBoolean();
  Matrix40Check(actual==expected,"sleep42_public_"+phase);
  Matrix40Check(p.ComponentSleep.IsSleeping==expected,"sleep42_native_"+phase);
  ToolAgentGate.Log(new{kind="body42_sleep_effect",phase,artificial=true,expectedSleeping=expected,publicSleeping=actual,nativeSleeping=p.ComponentSleep.IsSleeping,sleepNeed=p.ComponentVitalStats.Sleep,modelApi=false});
 }
 void Matrix42EnqueueBody(){
  Matrix40Enqueue("act_SetPosture",()=>new{posture="standing"});
  if(Matrix41Suite=="climb"){
   Matrix40Enqueue("act_Wait",()=>new{duration=.1},Body42PrepareLadder);
   Matrix40Enqueue("act_Wait",()=>new{duration=.3});
   Matrix40Enqueue("act_FaceTowards",()=>new{bearing=0,pitch=0,timeout=4});
   Matrix40Enqueue("act_ClimbTo",()=>new{target=Body42LadderReference(),height=body42ClimbGoal,timeout=12},Body42VerifyClimb);
  }else if(Matrix41Suite=="sleep"){
   Matrix40Enqueue("act_Wait",()=>new{duration=.1},Body42PrepareSleep);
   Matrix40Enqueue("act_Wait",()=>new{duration=.5},()=>{
    Body42VerifySleepState(false,"initial_awake");
    bool can=p.ComponentSleep.CanSleep(out var reason);ToolAgentGate.Log(new{kind="body42_sleep_precondition",canSleep=can,reason,standing=p.ComponentBody.StandingOnValue.HasValue,immersion=p.ComponentBody.ImmersionDepth,modelApi=false});
    Matrix40Check(can,"sleep42_original_conditions_satisfied");
   });
   Matrix40Enqueue("act_Sleep",()=>new{},()=>Body42VerifySleepState(true,"native_sleep_started"));
   Matrix40Enqueue("act_WakeUp",()=>new{},()=>Body42VerifySleepState(false,"native_wake_completed"));
  }else throw new InvalidOperationException("body42_requires_isolated_climb_or_sleep_suite");
 }
 void DisposeCapabilityBody42(){
  if(!body42SleepFixture)return;
  // No forced wake/needs restoration while a native action may own the actor.
  // This disposable fixture world is never reused as a natural save.
  bool restored=!actions.IsActionRunning&&!p.ComponentSleep.IsSleeping;
  if(restored){p.ComponentVitalStats.Sleep=body42InitialSleep;p.ComponentVitalStats.Wetness=body42InitialWetness;}
  ToolAgentGate.Log(new{kind="body42_fixture_disposal",sleepState=p.ComponentSleep.IsSleeping,originalSleep=body42InitialSleep,originalWetness=body42InitialWetness,restoredNeeds=restored,worldMustBeDisposed=true});
  body42SleepFixture=false;
 }
}
