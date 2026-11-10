using Engine;
using Game;
using GameEntitySystem;
using System.Text.Json;

// Artificial saddled horse, native creature behavior and mounting rules intact.
// Current public entity references select targets; entity handles are audit only.
internal sealed partial class ToolAgentScenario {
 Entity mount42Entity;
 ComponentMount mount42Native;
 string mount42Name;
 Vector3 mount42SteerFrom,mount42ActorFrom;
 void Mount42Spawn(){
  if(actions.IsActionRunning||p.ComponentBody.ParentBody!=null)throw new InvalidOperationException("mount42_fixture_requires_unmounted_idle_actor");
  if(mount42Entity!=null)throw new InvalidOperationException("mount42_fixture_already_created");
  var position=p.ComponentBody.Position+new Vector3(1.65f,0,0);
  mount42Entity=p.Project.FindSubsystem<SubsystemCreatureSpawn>(true).SpawnCreature("Horse_Bay_Saddled",position,false)??throw new InvalidOperationException("mount42_native_horse_spawn_failed");
  mount42Native=mount42Entity.FindComponent<ComponentMount>(true);
  mount42Name=mount42Entity.FindComponent<ComponentCreature>(true).DisplayName;
  // Initial heading is declared fixture preparation, not evidence of steering.
  mount42Native.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);
  ToolAgentGate.Log(new{kind="mount42_fixture",artificial=true,template="Horse_Bay_Saddled",spawnPosition=new[]{position.X,position.Y,position.Z},rotationEdited=true,fixtureHeading="+X",nativeBehaviorUnchanged=true,inventoryInjected=false,modelApi=false});
  Mount42Audit("before_mount");
 }
 string Mount42PublicTarget(){
  var entities=Snapshot().Observation.GetProperty("entities").EnumerateArray().Where(e=>e.GetProperty("name").GetString()==mount42Name).ToArray();
  if(entities.Length!=1)throw new Matrix40ObservationPending("mount42_unique_visible_horse_required");
  return entities[0].GetProperty("reference").GetString();
 }
 void Mount42Audit(string phase)=>ToolAgentGate.Log(new{kind="mount42_state",phase,self=Matrix40Read("read_self",new{}),horsePosition=mount42Native==null?null:new[]{mount42Native.ComponentBody.Position.X,mount42Native.ComponentBody.Position.Y,mount42Native.ComponentBody.Position.Z},actualParentIsHorse=mount42Native!=null&&p.ComponentBody.ParentBody==mount42Native.ComponentBody,actualRiderIsActor=mount42Native?.Rider?.Entity==p.Entity,nativeAnimating=p.Entity.FindComponent<ComponentRider>(true).m_isAnimating,auditOnly=true,modelApi=false});
 void Mount42VerifyMounted(){
  Mount42Audit("after_mount");
  Matrix40Check(mount42Entity.IsAddedToProject&&p.ComponentBody.ParentBody==mount42Native.ComponentBody,"mount42_actual_parent_is_fixture_horse");
  Matrix40Check(mount42Native.Rider?.Entity==p.Entity&&!p.Entity.FindComponent<ComponentRider>(true).m_isAnimating,"mount42_actual_rider_and_animation_complete");
  Matrix40Check(Matrix40Read("read_self",new{}).GetProperty("isMounted").GetBoolean(),"mount42_public_self_reports_mounted");
 }
 object Mount42SteerArgs(){
  Mount42VerifyMounted();mount42SteerFrom=mount42Native.ComponentBody.Position;mount42ActorFrom=p.ComponentBody.Position;
  return new{turn=0,throttle=.33,duration=1.5};
 }
 void Mount42VerifySteered(){
  Mount42Audit("after_steer");Mount42VerifyMounted();
  float distance=(mount42Native.ComponentBody.Position-mount42SteerFrom).XZ.Length();
  float actorDistance=(p.ComponentBody.Position-mount42ActorFrom).XZ.Length();
  Matrix40Check(distance>.05f&&actorDistance>.05f,"mount42_native_steering_real_horizontal_displacement");
  ToolAgentGate.Log(new{kind="mount42_steer_effect",horseDisplacement=distance,riderDisplacement=actorDistance,minimumRequired=.05,teleportDuringAction=false,modelApi=false});
 }
 void Mount42VerifyDismounted(){
  Mount42Audit("after_dismount");
  Matrix40Check(p.ComponentBody.ParentBody==null&&mount42Native.Rider==null,"mount42_actual_parent_and_rider_released");
  Matrix40Check(!p.Entity.FindComponent<ComponentRider>(true).m_isAnimating&&!Matrix40Read("read_self",new{}).GetProperty("isMounted").GetBoolean(),"mount42_public_unmounted_animation_complete");
 }
 void Matrix42EnqueueMount(){
  Matrix40Enqueue("act_SetPosture",()=>new{posture="standing"});
  Matrix40Enqueue("act_FaceTowards",()=>new{bearing=0,pitch=0,timeout=4},Mount42Spawn);
  Matrix40Enqueue("act_Mount",()=>new{target=Mount42PublicTarget()},Mount42VerifyMounted);
  Matrix40Enqueue("act_Steer",Mount42SteerArgs,Mount42VerifySteered);
  Matrix40Enqueue("act_Dismount",()=>new{},Mount42VerifyDismounted);
 }
 void DisposeMount42(){
  if(mount42Entity==null)return;
  if(actions.IsActionRunning||mount42Native?.Rider!=null||p.ComponentBody.ParentBody==mount42Native?.ComponentBody){
   ToolAgentGate.Log(new{kind="mount42_cleanup_deferred",reason="fixture_still_ridden_or_action_running",requiresDisposableWorldUnload=true});return;
  }
  if(mount42Entity.IsAddedToProject)p.Project.RemoveEntity(mount42Entity,true);
  ToolAgentGate.Log(new{kind="mount42_cleanup",fixtureRemoved=true,modelApi=false});mount42Entity=null;mount42Native=null;
 }
}
