using Engine;
using Game;
using GameEntitySystem;

/// <summary>
/// Privileged artificial-component fixture and audit ONLY. Never serialize this object
/// or AuditSnapshot into model context. It does not issue any movement, tool selection,
/// attack, wait, pickup, model request, or inventory write. The model uses normal tools.
/// </summary>
public sealed class HuntingFixture : IDisposable {
 readonly ComponentPlayer player;readonly Entity target;readonly ComponentHealth health;readonly ComponentLoot loot;
 readonly SubsystemGameInfo game;readonly SubsystemPickables pickables;readonly Action<object> log;
 readonly Dictionary<Pickable,int> targetDrops=new();readonly int ownerThread=Environment.CurrentManagedThreadId;
 readonly double startTime,maxGameSeconds;readonly Vector3 startPosition;Vector3 previousPosition;
 readonly object[] initialInventory;readonly int initialRawMeat;
 bool disposed,deathRecorded,lootRecorded;double horizontalPath;int actorDamageEvents,otherDamageEvents;
 double actorDamageAmount;double? firstDropTime;bool lastInjuryByActor;
 public bool Complete {get;private set;}
 public bool Failed {get;private set;}
 public string? FailureReason {get;private set;}
 public static HuntingFixture SetupFixture(ComponentPlayer player,Action<object> audit,double maxGameSeconds=300)=>new(player,audit,maxGameSeconds);

 public HuntingFixture(ComponentPlayer player,Action<object> audit,double maxGameSeconds=300){
  this.player=player??throw new ArgumentNullException(nameof(player));log=audit??throw new ArgumentNullException(nameof(audit));
  if(!double.IsFinite(maxGameSeconds)||maxGameSeconds<10||maxGameSeconds>900)throw new ArgumentOutOfRangeException(nameof(maxGameSeconds));
  this.maxGameSeconds=maxGameSeconds;
  if(!player.PlayerData.IsExternalActor||player.GameWidget is not null||!player.Entity.IsAddedToProject||player.ComponentHealth.Health<=0)throw new ArgumentException("A live external zero-viewport actor is required");
  game=player.Project.FindSubsystem<SubsystemGameInfo>(true);pickables=player.Project.FindSubsystem<SubsystemPickables>(true);
  initialInventory=Inventory();initialRawMeat=Count(RawMeatBlock.Index);
  if(initialRawMeat!=0||pickables.Pickables.Any(IsLiveRawMeat))throw new InvalidOperationException("Hunting control requires no pre-existing raw meat; never clear inventory/world to force this");
  if(Count(WoodenClubBlock.Index)!=0)throw new InvalidOperationException("Hunting control already owns a club; refusing ambiguous weapon acquisition");
  var terrain=player.Project.FindSubsystem<SubsystemTerrain>(true);
  var position=player.ComponentBody.Position;int x=Terrain.ToCell(position.X),y=Terrain.ToCell(position.Y),z=Terrain.ToCell(position.Z);
  // Explicit test-world modifications, not generated resources presented as natural.
  // Arena permits native animal movement/retaliation, but bounds escape in this control.
  for(int a=x-6;a<=x+6;a++)for(int b=z-6;b<=z+6;b++){
   terrain.ChangeCell(a,y-1,b,DirtBlock.Index);
   for(int c=y;c<=y+3;c++)terrain.ChangeCell(a,c,b,(Math.Abs(a-x)==6||Math.Abs(b-z)==6)&&c<y+3?GraniteBlock.Index:0);
  }
  startPosition=new Vector3(x+.5f,y,z+.5f);player.ComponentBody.Position=startPosition;player.ComponentBody.Velocity=Vector3.Zero;
  previousPosition=startPosition;startTime=game.TotalElapsedGameTime;
  Vector2 direction=player.ComponentCreatureModel.EyeRotation.GetForwardVector().XZ;
  direction=direction.LengthSquared()>.01f?Vector2.Normalize(direction):new Vector2(1,0);
  var forward=new Vector3(direction.X,0,direction.Y);
  Vector3 weaponPosition=startPosition+forward*1.5f+new Vector3(0,.65f,0);
  Vector3 preyPosition=startPosition+forward*3;
  target=player.Project.FindSubsystem<SubsystemCreatureSpawn>(true).SpawnCreature("Cow_Brown",preyPosition,false)
    ??throw new InvalidOperationException("Native Cow_Brown spawn failed");
  health=target.FindComponent<ComponentHealth>(true);loot=target.FindComponent<ComponentLoot>(true);
  health.Injured+=OnInjured;pickables.PickableAdded+=OnPickableAdded;
  // Component positive control deliberately skips crafting. This is a WORLD drop,
  // not an inventory grant. Normal native pickup and model SelectItem are still needed.
  pickables.AddPickable(WoodenClubBlock.Index,1,weaponPosition,Vector3.Zero,null,null);
  log(new{kind="hunting_fixture_setup",artificialFixture=true,componentControlSkipsCrafting=true,worldWeaponDrop=true,inventoryInjection=false,
   preyTemplate="Cow_Brown",preyEntityId=target.Id,preyPosition=new[]{preyPosition.X,preyPosition.Y,preyPosition.Z},
   weaponPosition=new[]{weaponPosition.X,weaponPosition.Y,weaponPosition.Z},arenaHalfWidth=6,wallHeight=3,
   nativeAnimalBehaviorUnchanged=true,initialInventory,initialRawMeat,startTime,maxGameSeconds});
 }
 static bool IsLiveRawMeat(Pickable item)=>!item.ToRemove&&item.Count>0&&Terrain.ExtractContents(item.Value)==RawMeatBlock.Index;
 int Count(int content){var i=player.ComponentMiner.Inventory;return Enumerable.Range(0,i.SlotsCount).Where(s=>Terrain.ExtractContents(i.GetSlotValue(s))==content).Sum(i.GetSlotCount);}
 object[] Inventory(){var i=player.ComponentMiner.Inventory;return Enumerable.Range(0,i.SlotsCount).Where(s=>i.GetSlotCount(s)>0).Select(s=>(object)new{slot=s,value=i.GetSlotValue(s),count=i.GetSlotCount(s)}).ToArray();}
 void Owner(){if(disposed)throw new ObjectDisposedException(nameof(HuntingFixture));if(Environment.CurrentManagedThreadId!=ownerThread)throw new InvalidOperationException("Game thread required");}
 void OnInjured(Injury injury){
  // Audit callback only, invoked by native Injury.Process; never modifies the injury.
  if(disposed)return;lastInjuryByActor=injury.Attacker?.Entity==player.Entity;
  double effective=Math.Min(Math.Max(0,injury.Amount),Math.Max(0,health.Health));
  if(lastInjuryByActor){actorDamageEvents++;actorDamageAmount+=effective;}else otherDamageEvents++;
  log(new{kind="hunting_native_injury_audit",targetEntityId=target.Id,byBoundActor=lastInjuryByActor,amount=injury.Amount,
   appliedUpperBound=effective,healthBefore=health.Health,gameTime=game.TotalElapsedGameTime});
 }
 void OnPickableAdded(Pickable item){
  if(disposed||Terrain.ExtractContents(item.Value)!=RawMeatBlock.Index||item.Count<=0)return;
  if(item.OwnerEntity!=target){Fail("foreign_raw_meat_contaminates_control");return;}
  if(targetDrops.TryAdd(item,item.Count)){
   firstDropTime??=game.TotalElapsedGameTime;
   log(new{kind="hunting_native_owned_drop_audit",dropIdentity=targetDrops.Count,ownerEntityId=target.Id,count=item.Count,
    gameTime=game.TotalElapsedGameTime,deathTime=health.DeathTime,corpseDuration=health.CorpseDuration});
  }
 }
 void Fail(string reason){if(Failed||Complete)return;Failed=true;FailureReason=reason;log(new{kind="hunting_fixture_failed",reason,audit=AuditSnapshot()});}
 public void Update(){
  Owner();if(Failed||Complete)return;
  var position=player.ComponentBody.Position;horizontalPath+=Vector2.Distance(previousPosition.XZ,position.XZ);previousPosition=position;
  if(player.GameWidget is not null){Fail("actor_gained_viewport");return;}
  if(player.ComponentHealth.Health<=0){Fail("actor_died");return;}
  if(game.TotalElapsedGameTime-startTime>maxGameSeconds){Fail("fixture_deadline");return;}
  if(pickables.Pickables.Any(q=>IsLiveRawMeat(q)&&q.OwnerEntity!=target)){Fail("foreign_raw_meat_contaminates_control");return;}
  if(health.DeathTime.HasValue&&!deathRecorded){deathRecorded=true;log(new{kind="hunting_native_death_audit",health.DeathTime,health.CorpseDuration,
   actorDamageEvents,otherDamageEvents,lastInjuryByActor,actorDamageAmount,gameTime=game.TotalElapsedGameTime});}
  if(loot.m_lootDropped&&!lootRecorded){lootRecorded=true;log(new{kind="hunting_native_loot_audit",gameTime=game.TotalElapsedGameTime,
   health.DeathTime,health.CorpseDuration,firstDropTime,totalOwnedMeat=targetDrops.Values.Sum()});}
  int gained=Count(RawMeatBlock.Index)-initialRawMeat,generated=targetDrops.Values.Sum();
  if(gained>generated){Fail("inventory_meat_has_no_target_owned_provenance");return;}
  int remaining=pickables.Pickables.Where(IsLiveRawMeat).Sum(q=>q.Count);
  if(deathRecorded&&lootRecorded&&generated>0&&remaining==0&&gained==generated){
   if(actorDamageEvents==0||!lastInjuryByActor||otherDamageEvents>0){Fail("death_not_exclusively_attributed_to_bound_actor");return;}
   if(!firstDropTime.HasValue||!health.DeathTime.HasValue||firstDropTime.Value+1e-5<health.DeathTime.Value+health.CorpseDuration){Fail("loot_preceded_native_corpse_delay");return;}
   Complete=true;log(new{kind="hunting_fixture_outcome_verified",audit=AuditSnapshot(),meaning="host audit only; artificial world-drop weapon and enclosed Cow_Brown; no model decisions supplied by fixture"});
  }
 }
 /// <summary>HOST LOG ONLY. Contains hidden diagnostic health and ownership data, never model perception.</summary>
 public object AuditSnapshot()=>new{complete=Complete,failed=Failed,failure=FailureReason,
  seconds=game.TotalElapsedGameTime-startTime,health=player.ComponentHealth.Health,targetHealth=health.Health,
  actorDamageEvents,otherDamageEvents,actorDamageAmount,deathTime=health.DeathTime,corpseDuration=health.CorpseDuration,
  nativeLootDropped=loot.m_lootDropped,firstDropTime,ownedDropObjects=targetDrops.Count,totalOwnedMeat=targetDrops.Values.Sum(),
  remainingTargetMeat=pickables.Pickables.Where(q=>IsLiveRawMeat(q)&&q.OwnerEntity==target).Sum(q=>q.Count),
  actualMeatGain=Count(RawMeatBlock.Index)-initialRawMeat,inventory=Inventory(),
  horizontalPath,netHorizontalDisplacement=Vector2.Distance(startPosition.XZ,player.ComponentBody.Position.XZ),
  views=player.Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count};
 public void Dispose(){if(disposed)return;Owner();health.Injured-=OnInjured;pickables.PickableAdded-=OnPickableAdded;disposed=true;}
}
