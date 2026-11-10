using Engine;
using Game;
using GameEntitySystem;
using System.Text.Json;

// Privileged artificial target/item preparation and read-only effect audit only.
// Every attack uses the public current-reference menu and normal native executor.
// No health edits, forced hit, frozen animal AI or rewritten combat probabilities.
internal sealed partial class ToolAgentScenario {
 Entity combat41Target;
 ComponentHealth combat41Health;
 string combat41PublicName,combat41Phase;
 int combat41ClubSlot=-1,combat41SpearSlot=-1,combat41DamageEvents,combat41ForeignDamageEvents,combat41Projectiles,combat41SpearBefore;
 float combat41HealthBefore;
 double combat41AttributedAmount;
 SubsystemProjectiles combat41ProjectileSubsystem;
 readonly List<Projectile> combat41OwnedProjectiles=[];

 void Combat41Injured(Injury injury){
  if(combat41Target is null)return;
  bool byActor=injury.Attacker?.Entity==p.Entity;
  if(byActor){combat41DamageEvents++;combat41AttributedAmount+=Math.Max(0,injury.Amount);}else combat41ForeignDamageEvents++;
  ToolAgentGate.Log(new{kind="combat41_native_injury",phase=combat41Phase,artificial=true,byActor,amount=injury.Amount,healthBefore=combat41Health.Health,note="Native Injury callback audit only; actual post-health checked separately."});
 }
 void Combat41ProjectileAdded(Projectile projectile){
  if(projectile.OwnerEntity!=p.Entity||Terrain.ExtractContents(projectile.Value)!=WoodenSpearBlock.Index)return;
  combat41Projectiles++;combat41OwnedProjectiles.Add(projectile);
  ToolAgentGate.Log(new{kind="combat41_native_projectile",phase=combat41Phase,artificial=true,ownerIsActor=true,value=projectile.Value,position=new[]{projectile.Position.X,projectile.Position.Y,projectile.Position.Z},speed=projectile.Velocity.Length()});
 }
 void Combat41RemoveTarget(){
  if(combat41Health!=null)combat41Health.Injured-=Combat41Injured;
  if(combat41Target?.IsAddedToProject==true)p.Project.RemoveEntity(combat41Target,true);
  combat41Target=null;combat41Health=null;
 }
 int Combat41Grant(int value){
  var inv=p.ComponentMiner.Inventory;
  int slot=Enumerable.Range(0,Math.Min(inv.VisibleSlotsCount,inv.SlotsCount)).Last(s=>inv.GetSlotCount(s)==0);
  inv.AddSlotItems(slot,value,1);
  Matrix40Check(inv.GetSlotValue(slot)==value&&inv.GetSlotCount(slot)==1,"combat41_explicit_fixture_item_granted");
  return slot;
 }
 void Combat41Spawn(string phase,float distance){
  if(actions.IsActionRunning)throw new InvalidOperationException("combat41_fixture_requires_idle_owner");
  Combat41RemoveTarget();combat41Phase=phase;combat41DamageEvents=0;combat41ForeignDamageEvents=0;combat41AttributedAmount=0;
  // Position changes are fixture preparation, not movement capability evidence.
  p.ComponentBody.Position=new Vector3(matrix40X+.5f,matrix40Y,matrix40Z+.5f);p.ComponentBody.Velocity=Vector3.Zero;
  // Restore a declared fixture pose between independent targets, not wider vision.
  // Earlier chase/turn actions must not leave the new +X target behind the actor.
  p.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);
  p.ComponentLocomotion.LookAngles=Vector2.Zero;p.ComponentLocomotion.LookOrder=Vector2.Zero;
  Vector3 position=p.ComponentBody.Position+new Vector3(distance,0,0);
  combat41Target=p.Project.FindSubsystem<SubsystemCreatureSpawn>(true).SpawnCreature("Cow_Brown",position,false)??throw new InvalidOperationException("combat41_native_cow_spawn_failed");
  combat41Health=combat41Target.FindComponent<ComponentHealth>(true);
  combat41PublicName=combat41Target.FindComponent<ComponentCreature>(true).DisplayName;
  combat41HealthBefore=combat41Health.Health;
  if(combat41HealthBefore<=0)throw new InvalidOperationException("combat41_spawn_not_alive");
  combat41Health.Injured+=Combat41Injured;
  ToolAgentGate.Log(new{kind="combat41_fixture",phase,artificial=true,creatureTemplate="Cow_Brown",nativeBehaviorUnchanged=true,healthEdited=false,forceHit=false,positionEdited=true,rotationEdited=true,lookAnglesEdited=true,fixtureFacing="+X horizontal",spawnPosition=new[]{position.X,position.Y,position.Z},inventoryInjected=true,healthBaseline=combat41HealthBefore,modelApi=false});
 }
 string Combat41VisibleTarget(){
  // Selection uses only public name/reference/distance. Hidden entity identity/health
  // is audit-only and cannot manufacture an observation reference.
  var o=Snapshot().Observation;
  var targets=o.GetProperty("entities").EnumerateArray().Where(e=>e.TryGetProperty("name",out var name)&&name.GetString()==combat41PublicName).ToArray();
  if(targets.Length!=1)throw new Matrix40ObservationPending("combat41_unique_public_cow_target_required");
  return targets[0].GetProperty("reference").GetString();
 }
 // A later declared strike can legitimately lose reach to the moving native animal.
 // It remains a FAILED attempt; only allow the group to reach its separate effect
 // audit after an earlier strike already produced observed, attributable damage.
 bool Combat41AcceptObservedFailure(JsonElement terminal){
  if(Matrix41Suite!="combat"||combat41Phase!="melee_three_declared_attempts"||matrix40Case!="act_MeleeAttack"||matrix40Receipt?.Tool!="act_MeleeAttack"||
   terminal.ValueKind!=JsonValueKind.Object||!terminal.TryGetProperty("status",out var status)||status.GetString()!="failed"||
   !terminal.TryGetProperty("failure",out var failure)||failure.GetString()!="outOfReach"||
   combat41Health is null||combat41DamageEvents==0||combat41ForeignDamageEvents!=0||combat41Health.Health>=combat41HealthBefore)return false;
  ToolAgentGate.Log(new{kind="combat41_declared_attempt_failed",phase=combat41Phase,nativeStatus="failed",actualFailure="outOfReach",notSuccess=true,acceptedAsSuccess=false,groupMayContinue=true,reason="Earlier native damage exists; final group effect check is still required.",healthBefore=combat41HealthBefore,healthNow=combat41Health.Health,actorDamageEvents=combat41DamageEvents});
  return true;
 }
 void Combat41VerifyDamage(string prefix){
  Matrix40Check(combat41Target?.IsAddedToProject==true&&combat41Health!=null,prefix+"_actual_target_retained_for_audit");
  Matrix40Check(combat41DamageEvents>0&&combat41AttributedAmount>0,prefix+"_native_injury_attributed_to_actor");
  Matrix40Check(combat41ForeignDamageEvents==0,prefix+"_no_foreign_damage_contamination");
  Matrix40Check(combat41Health.Health<combat41HealthBefore,prefix+"_actual_health_decreased");
  ToolAgentGate.Log(new{kind="combat41_effect_verified",phase=combat41Phase,artificial=true,healthBefore=combat41HealthBefore,healthAfter=combat41Health.Health,actorDamageEvents=combat41DamageEvents,foreignDamageEvents=combat41ForeignDamageEvents,actorDamageReported=combat41AttributedAmount,projectiles=combat41Projectiles,modelApi=false,note="Actual native effects, not terminal-only success or natural survival evidence."});
 }
 void Matrix41EnqueueCombat(){
  Matrix40Enqueue("act_SetPosture",()=>new{posture="standing"});
  Matrix40Enqueue("act_FaceTowards",()=>new{bearing=0,pitch=0,timeout=4});
  Matrix40Enqueue("act_Wait",()=>new{duration=.1},()=>{
   combat41ClubSlot=Combat41Grant(WoodenClubBlock.Index);combat41SpearSlot=Combat41Grant(WoodenSpearBlock.Index);
   combat41ProjectileSubsystem=p.Project.FindSubsystem<SubsystemProjectiles>(true);combat41ProjectileSubsystem.ProjectileAdded+=Combat41ProjectileAdded;
   Combat41Spawn("melee_three_declared_attempts",1.45f);
   ToolAgentGate.Log(new{kind="combat41_items_fixture",artificial=true,clubSlot=combat41ClubSlot,clubValue=WoodenClubBlock.Index,spearSlot=combat41SpearSlot,spearValue=WoodenSpearBlock.Index,countEach=1,note="Injected component-control equipment, not gathered or crafted."});
  });
  Matrix40Enqueue("act_SelectItem",()=>new{slot=combat41ClubSlot,value=WoodenClubBlock.Index});
  Matrix40Enqueue("act_FaceTowards",()=>new{destination=Combat41VisibleTarget(),timeout=4});
  // Native hit probability remains intact. Three predeclared attempts, never replay
  // an uncertain call. A miss is not damage; verify the actual final health delta.
  for(int attempt=0;attempt<3;attempt++){
   Matrix40Enqueue("act_Wait",()=>new{duration=.8});
   Matrix40Enqueue("act_MoveTo",()=>new{destination=Combat41VisibleTarget(),range=1,timeout=4});
   Matrix40Enqueue("act_FaceTowards",()=>new{destination=Combat41VisibleTarget(),timeout=4});
   Matrix40Enqueue("act_MeleeAttack",()=>new{target=Combat41VisibleTarget()});
  }
  Matrix40Enqueue("act_Wait",()=>new{duration=.1},()=>{Combat41VerifyDamage("melee");Combat41Spawn("wooden_spear",2.5f);});
  Matrix40Enqueue("act_Wait",()=>new{duration=.1});
  Matrix40Enqueue("act_SelectItem",()=>new{slot=combat41SpearSlot,value=WoodenSpearBlock.Index});
  Matrix40Enqueue("act_FaceTowards",()=>new{destination=Combat41VisibleTarget(),timeout=4});
  Matrix40Enqueue("act_UseAimedItem",()=>{
   combat41Projectiles=0;combat41OwnedProjectiles.Clear();combat41HealthBefore=combat41Health.Health;combat41DamageEvents=0;combat41ForeignDamageEvents=0;combat41AttributedAmount=0;
   combat41SpearBefore=p.ComponentMiner.Inventory.GetSlotCount(combat41SpearSlot);
   return new{slot=combat41SpearSlot,value=WoodenSpearBlock.Index,target=Combat41VisibleTarget(),duration=.15};
  },()=>{
   Matrix40Check(combat41Projectiles==1&&combat41OwnedProjectiles.Count==1,"aimed_native_owned_projectile_created");
   Matrix40Check(p.ComponentMiner.Inventory.GetSlotCount(combat41SpearSlot)==combat41SpearBefore-1,"aimed_actual_spear_inventory_consumed");
  });
  Matrix40Enqueue("act_Wait",()=>new{duration=.8},()=>Combat41VerifyDamage("aimed_hit"));
 }
 void DisposeCapabilityCombat41(){
  if(combat41ProjectileSubsystem!=null){combat41ProjectileSubsystem.ProjectileAdded-=Combat41ProjectileAdded;combat41ProjectileSubsystem=null;}
  if(actions.IsActionRunning){if(combat41Health!=null)combat41Health.Injured-=Combat41Injured;ToolAgentGate.Log(new{kind="combat41_cleanup_deferred",reason="native_owner_active",worldMustBeDisposed=true});return;}
  Combat41RemoveTarget();
  // Do not turn/remove live projectiles or restore inventory while physics is
  // active. The isolated world is disposable; no natural save is ever reused.
 }
}
