using Engine;
using Game;
using Game.Westworld;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
internal static class HuntingGate {
 static readonly string Root=AppContext.BaseDirectory;static readonly Stopwatch clock=Stopwatch.StartNew();
 internal static string PreyTemplate="Wildboar";
 static bool created,finished,passed;static HuntingScenario scenario;static SubsystemUpdate updates;
 internal static void Log(object value){string json=JsonSerializer.Serialize(value);Console.WriteLine(json);File.AppendAllText(Path.Combine(Root,"hunting.jsonl"),json+"\n");}
 static int Main(string[] args){
  if(args.Length is <1 or >2||args[0]!="fixture"||!Root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)
    ||Directory.Exists(Path.Combine(Root,"doc"))||Directory.Exists(Path.Combine(Root,"Mods")))return 2;
  if(args.Length==2){if(args[1] is not ("Wildboar" or "Cow_Brown"))return 2;PreyTemplate=args[1];}
  Log(new{kind="start",preyTemplate=PreyTemplate,artificialFixture=true,inventoryInjection=false,models=0,scope=$"two logs -> native wooden club -> artificial {PreyTemplate} -> native meat drop and pickup"});
  Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;
 }
 static void Frame(){try{
  if(finished)return;if(clock.Elapsed.TotalSeconds>240){Finish(false,"wall timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;
   var w=WorldsManager.CreateWorld(new WorldSettings{Name="Hunting artificial arena gate",Seed="hunting-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}
  if(GameManager.Project is null)return;updates??=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);
  if(updates.UpdateFailureCount!=0)throw new InvalidOperationException(updates.LastUpdateFailure);
  var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
  if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(GameManager.Project){Name="ProductionActor",IsExternalActor=true});ScreensManager.SwitchScreen("Game");}
  if(scenario is null&&players.ComponentPlayers.Count==1&&players.PlayersData[0].IsReadyForPlaying){scenario=new(players.ComponentPlayers[0]);updates.AddUpdateable(scenario);}
  if(scenario?.Done==true)Finish(true,"native hunting and loot verified");
 }catch(Exception e){Log(new{kind="failure",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool ok,string reason){if(finished)return;finished=true;passed=ok;Log(new{kind="result",passed,reason,seconds=clock.Elapsed.TotalSeconds,updateFailures=updates?.UpdateFailureCount});try{scenario?.Dispose();}finally{Window.Close();}}
}
internal sealed class HuntingScenario:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-14;public bool Done{get;private set;}
 readonly Dictionary<Pickable,int> ownedDropCounts=new();int generatedMeat,remainingMeat;
 GameEntitySystem.Entity prey;ComponentHealth preyHealth;ComponentLoot loot;Vector3 lastMeasured,huntOrigin;double path, huntStarted, nextHunt, corpseObservedAt;int hits, misses, scans, rawBefore;bool deadLogged,lootLogged;Vector3 lastSeen;
 readonly ComponentPlayer p;readonly AtomicActions actions;readonly ActorTools tools;readonly SubsystemTerrain terrain;
 readonly NativeActorSession senses;readonly ScapiInterestBackend backend;readonly InterestCoordinator interests;
 Point3 logCell;readonly Point3 tableCell;readonly Vector3 fixtureOrigin;int harvested;readonly long initialDug;AtomicActionRun face;int stage;double elapsed;readonly int baseline;readonly Dictionary<int,int> baselineItems;
 public HuntingScenario(ComponentPlayer player){p=player;terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);initialDug=p.PlayerStats.BlocksDug;
  baseline=Total();var inv0=p.ComponentMiner.Inventory;baselineItems=Enumerable.Range(0,inv0.SlotsCount).Where(i=>inv0.GetSlotCount(i)>0).GroupBy(inv0.GetSlotValue).ToDictionary(g=>g.Key,g=>g.Sum(inv0.GetSlotCount));HuntingGate.Log(new{kind="native_spawn_inventory",inventory=Inventory(),baseline});Require(new[]{OakWoodBlock.Index,PlanksBlock.Index,StickBlock.Index,CraftingTableBlock.Index,WoodenClubBlock.Index}.All(v=>Count(v)==0),"spawn has production materials; refusing to consume them");
  senses=new NativeActorSession(p.Project,Guid.NewGuid(),actionsEnabled:false);senses.Bind(Guid.NewGuid(),p);p.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);
  backend=new ScapiInterestBackend(terrain,p.Project.FindSubsystem<SubsystemSpawn>(true));interests=new(senses.Registry,backend,new InterestRadii(16,32));interests.Sync();terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);
  actions=new AtomicActions(p.Entity);tools=new ActorTools(actions,true); // Explicit public recipe guide, not hidden terrain knowledge.
  int x=Terrain.ToCell(p.ComponentBody.Position.X),y=Terrain.ToCell(p.ComponentBody.Position.Y),z=Terrain.ToCell(p.ComponentBody.Position.Z);
  for(int a=x-3;a<=x+3;a++)for(int b=z-3;b<=z+3;b++){terrain.ChangeCell(a,y-1,b,DirtBlock.Index);for(int c=y;c<=y+3;c++)terrain.ChangeCell(a,c,b,0);}
  fixtureOrigin=new(x+.5f,y,z+.5f);p.ComponentBody.Position=fixtureOrigin;p.ComponentBody.Velocity=Vector3.Zero;
  logCell=new(x+2,y,z);tableCell=logCell;terrain.ChangeCell(logCell.X,logCell.Y,logCell.Z,OakWoodBlock.Index);terrain.ChangeCell(x+2,y,z+1,OakWoodBlock.Index);
  HuntingGate.Log(new{kind="artificial_resource_fixture",log=new[]{logCell.X,logCell.Y,logCell.Z},inventory=Inventory(),publicRecipeGuide=true,placedLogs=2,placedStone=0});
 }
 JsonElement Call(string name,object args=null){using var d=JsonDocument.Parse(tools.InvokeJson(name,JsonSerializer.Serialize(args??new{})));Require(d.RootElement.GetProperty("ok").GetBoolean(),name+": "+d.RootElement);return d.RootElement.GetProperty("data").Clone();}
 int Count(int content){var i=p.ComponentMiner.Inventory;return Enumerable.Range(0,i.SlotsCount).Where(s=>Terrain.ExtractContents(i.GetSlotValue(s))==content).Sum(i.GetSlotCount);}
 int Total(){var i=p.ComponentMiner.Inventory;return Enumerable.Range(0,i.SlotsCount).Sum(i.GetSlotCount);}
 object Inventory(){var i=p.ComponentMiner.Inventory;return Enumerable.Range(0,i.SlotsCount).Where(s=>i.GetSlotCount(s)>0).Select(s=>new{slot=s,value=i.GetSlotValue(s),count=i.GetSlotCount(s)}).ToArray();}
 static bool Terminal(JsonElement task)=>task.ValueKind==JsonValueKind.Object&&task.GetProperty("status").GetString() is "succeeded" or "failed" or "cancelled";
 void Craft(int result,int count,bool external=false){var o=Call("observe",new{look_distance=5,object_distance=5,limit=24});var recipes=Call("find_recipes",new{result_value=result,offset=0,limit=32});Require(recipes.GetArrayLength()>0,"no public recipe");
  var chosen=recipes.EnumerateArray().First(r=>r.GetProperty("recipe").GetProperty("resultValue").GetInt32()==result);
  var task=Call("plan_craft",new{workspace=external?o.GetProperty("focus").GetProperty("storageReference").GetString():o.GetProperty("craftingReference").GetString(),recipe=chosen.GetProperty("reference").GetString(),count});HuntingGate.Log(new{kind="craft_started",result,count,recipe=chosen,task,before=Inventory()});}
 public void Update(float dt){elapsed+=dt;if(stage>=20&&stage<=23){path+=Vector2.Distance(lastMeasured.XZ,p.ComponentBody.Position.XZ);lastMeasured=p.ComponentBody.Position;UpdateHunt();return;}interests.Sync();Require(p.GameWidget is null&&p.ComponentHealth.Health>0,"not live viewless actor");Require(elapsed<200,"simulation timeout");
  if(stage==0&&elapsed>.7){face=actions.Start(new FaceTowards(new Vector3(logCell.X+.5f,logCell.Y+.5f,logCell.Z+.5f)));stage=1;}
  else if(stage==1&&face.Result.Status!=AtomicActionStatus.Running){Require(face.Result.Status==AtomicActionStatus.Succeeded,"facing failed");var o=Call("observe",new{look_distance=5,object_distance=5,limit=24});var f=o.GetProperty("focus");var hit=actions.Queries.LookAhead(5);
   Require(hit?.Cell==logCell&&hit.BlockValue.HasValue&&Terrain.ExtractContents(hit.BlockValue.Value)==OakWoodBlock.Index,"real eye ray did not see fixture log");
   Call("submit_plan",new{steps=new object[]{new{tool="act_Dig",arguments=new{target=f.GetProperty("reference").GetString(),timeout=30},minimum_affected=1}}});HuntingGate.Log(new{kind="observed_log",observation=o});stage=2;}
  else if(stage==2){var t=Call("task_status");if(!Terminal(t))return;Require(t.GetProperty("status").GetString()=="succeeded","dig failed");Require(p.PlayerStats.BlocksDug==initialDug+harvested+1,"not one native dig");Require(Terrain.ExtractContents(terrain.Terrain.GetCellValue(logCell.X,logCell.Y,logCell.Z))==0,"fixture not removed");Call("act_MoveTo",new{bearing=0,distance=1,range=.3,timeout=6});stage=3;}
  else if(stage==3){var a=Call("action_status");if(a.ValueKind==JsonValueKind.Object&&a.GetProperty("status").GetString()=="running")return;if(Count(OakWoodBlock.Index)!=harvested+1)return;harvested++;Require(Total()==baseline+harvested,"unexpected collected inventory");HuntingGate.Log(new{kind="native_pickup_verified",harvested,inventory=Inventory()});
   if(harvested==1){logCell=new(logCell.X,logCell.Y,logCell.Z+1);face=actions.Start(new FaceTowards(new Vector3(logCell.X+.5f,logCell.Y+.5f,logCell.Z+.5f)));stage=1;}else{Craft(PlanksBlock.Index,8);stage=4;}}
  else if(stage==4){var t=Call("task_status");if(!Terminal(t))return;Require(t.GetProperty("status").GetString()=="succeeded"&&Count(OakWoodBlock.Index)==0&&Count(PlanksBlock.Index)==8&&Total()==baseline+8,"log-to-planks conservation failed");HuntingGate.Log(new{kind="planks_verified",task=t,inventory=Inventory()});Craft(CraftingTableBlock.Index,1);stage=5;}
  else if(stage==5){var t=Call("task_status");if(!Terminal(t))return;Require(t.GetProperty("status").GetString()=="succeeded"&&Count(PlanksBlock.Index)==4&&Count(CraftingTableBlock.Index)==1&&Total()==baseline+5,"planks-to-table conservation failed");HuntingGate.Log(new{kind="table_item_verified",task=t,inventory=Inventory()});Craft(StickBlock.Index,4);stage=6;}
  else if(stage==6){var t=Call("task_status");if(!Terminal(t))return;Require(t.GetProperty("status").GetString()=="succeeded"&&Count(PlanksBlock.Index)==2&&Count(StickBlock.Index)==4&&Total()==baseline+7,"stick conservation failed");
   face=actions.Start(new MoveTo(fixtureOrigin,.3f,8));stage=60;}
  else if(stage==60&&face.Result.Status!=AtomicActionStatus.Running){Require(face.Result.Status==AtomicActionStatus.Succeeded,"return to previously occupied fixture origin failed");face=actions.Start(new FaceTowards(new Vector3(tableCell.X+.5f,tableCell.Y-.01f,tableCell.Z+.5f)));stage=7;}
  else if(stage==7&&face.Result.Status!=AtomicActionStatus.Running){Require(face.Result.Status==AtomicActionStatus.Succeeded,"support facing failed");var o=Call("observe",new{look_distance=5,object_distance=5,limit=24});var f=o.GetProperty("focus");var hit=actions.Queries.LookAhead(5);
   Require(hit?.Cell==new Point3(tableCell.X,tableCell.Y-1,tableCell.Z),"placement support not actually observed");
   var inv=p.ComponentMiner.Inventory;int slot=Enumerable.Range(0,inv.SlotsCount).First(i=>inv.GetSlotCount(i)>0&&Terrain.ExtractContents(inv.GetSlotValue(i))==CraftingTableBlock.Index);
   Call("act_Place",new{target=f.GetProperty("reference").GetString(),slot,value=inv.GetSlotValue(slot)});stage=8;}
  else if(stage==8){var a=Call("action_status");if(a.GetProperty("status").GetString()=="running")return;Require(a.GetProperty("status").GetString()=="succeeded"&&Count(CraftingTableBlock.Index)==0&&Terrain.ExtractContents(terrain.Terrain.GetCellValue(tableCell.X,tableCell.Y,tableCell.Z))==CraftingTableBlock.Index,"native workbench placement failed");
   face=actions.Start(new FaceTowards(new Vector3(tableCell.X+.5f,tableCell.Y+.5f,tableCell.Z+.5f)));stage=9;}
  else if(stage==9&&face.Result.Status!=AtomicActionStatus.Running){Require(face.Result.Status==AtomicActionStatus.Succeeded,"workbench facing failed");Require(actions.Queries.LookAhead(5)?.Cell==tableCell,"wrong workbench");Craft(WoodenClubBlock.Index,1,true);stage=10;}
  else if(stage==10){var t=Call("task_status");if(!Terminal(t))return;Require(t.GetProperty("status").GetString()=="succeeded"&&Count(PlanksBlock.Index)==0&&Count(StickBlock.Index)==3&&Count(WoodenClubBlock.Index)==1&&Total()==baseline+4,"club conservation failed");Require(baselineItems.All(kv=>Enumerable.Range(0,p.ComponentMiner.Inventory.SlotsCount).Where(i=>p.ComponentMiner.Inventory.GetSlotValue(i)==kv.Key).Sum(p.ComponentMiner.Inventory.GetSlotCount)==kv.Value),"spawn inventory changed");var own=p.Entity.FindComponent<ComponentCraftingTable>(true);var placed=p.Project.FindSubsystem<SubsystemBlockEntities>(true).GetBlockEntity(tableCell)?.Entity.FindComponent<ComponentCraftingTable>(true);Require(placed is not null,"earned workbench entity missing");var ownSlots=Enumerable.Range(0,own.SlotsCount).Select(i=>new{slot=i,value=own.GetSlotValue(i),count=own.GetSlotCount(i)}).ToArray();var placedSlots=Enumerable.Range(0,placed.SlotsCount).Select(i=>new{slot=i,value=placed.GetSlotValue(i),count=placed.GetSlotCount(i)}).ToArray();Require(ownSlots.All(s=>s.count==0)&&placedSlots.All(s=>s.count==0),"crafting workspace residual items violate conservation");HuntingGate.Log(new{kind="all_workspaces_empty",ownSlots,placedSlots});HuntingGate.Log(new{kind="wooden_club_verified",task=t,inventory=Inventory(),health=p.ComponentHealth.Health,views=p.Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count,placedWorkbench=new[]{tableCell.X,tableCell.Y,tableCell.Z}});StartHunt();}

 }

 void StartHunt(){
  // Explicit artificial arena, not natural discovery. The animal retains every native behavior.
  var origin=p.ComponentBody.Position;int x=Terrain.ToCell(origin.X),y=Terrain.ToCell(origin.Y),z=Terrain.ToCell(origin.Z);
  for(int a=x-5;a<=x+5;a++)for(int b=z-5;b<=z+5;b++){terrain.ChangeCell(a,y-1,b,DirtBlock.Index);for(int c=y;c<=y+3;c++)terrain.ChangeCell(a,c,b,(Math.Abs(a-x)==5||Math.Abs(b-z)==5)&&c<y+3?GraniteBlock.Index:0);}
  var inv=p.ComponentMiner.Inventory;int slot=Enumerable.Range(0,inv.SlotsCount).First(i=>inv.GetSlotCount(i)>0&&Terrain.ExtractContents(inv.GetSlotValue(i))==WoodenClubBlock.Index);
  var select=actions.Start(new SelectItem(slot,inv.GetSlotValue(slot)));Require(select.Result.Status==AtomicActionStatus.Succeeded,"select earned club failed");
  rawBefore=Count(RawMeatBlock.Index);Require(rawBefore==0,"raw meat present before hunt");Require(!p.Project.FindSubsystem<SubsystemPickables>(true).Pickables.Any(q=>!q.ToRemove&&q.Count>0&&Terrain.ExtractContents(q.Value)==RawMeatBlock.Index),"fixture contains unrelated raw meat before hunt");
  prey=p.Project.FindSubsystem<SubsystemCreatureSpawn>(true).SpawnCreature(HuntingGate.PreyTemplate,new Vector3(x+2.5f,y,z+.5f),false);Require(prey is not null,"wildboar spawn failed");
  preyHealth=prey.FindComponent<ComponentHealth>(true);loot=prey.FindComponent<ComponentLoot>(true);
  lastMeasured=huntOrigin=origin;huntStarted=elapsed;stage=20;nextHunt=elapsed+.5;
  HuntingGate.Log(new{kind="artificial_arena",halfWidth=5,wallHeight=3,animal=HuntingGate.PreyTemplate,animalId=prey.Id,spawnHealthAudit=preyHealth.Health,weapon="earned WoodenClub",inventory=Inventory(),noInjectedItems=true,nativeAnimalBehavior=true});
 }
 void UpdateHunt(){
  interests.Sync();Require(p.GameWidget is null,"unexpected viewport");if(p.ComponentHealth.Health<=.25f)HuntingGate.Log(new{kind="risk_stop_audit",reason="low_self_health",selfHealth=p.ComponentHealth.Health,preyHealthAudit=preyHealth.Health,hits,misses,path,netHorizontalDisplacement=Vector2.Distance(huntOrigin.XZ,p.ComponentBody.Position.XZ),seconds=elapsed-huntStarted});Require(p.ComponentHealth.Health>.25f,"self health below safe threshold");Require(elapsed-huntStarted<150,"bounded hunting timeout");Require(path<150,"bounded pursuit path exceeded");
  var rawWorld=p.Project.FindSubsystem<SubsystemPickables>(true).Pickables.Where(q=>!q.ToRemove&&q.Count>0&&Terrain.ExtractContents(q.Value)==RawMeatBlock.Index).ToArray();
  Require(rawWorld.All(q=>q.OwnerEntity==prey),"fixture contaminated by non-target raw meat; refusing attribution");
  foreach(var item in rawWorld){if(!ownedDropCounts.ContainsKey(item)){ownedDropCounts.Add(item,item.Count);HuntingGate.Log(new{kind="owned_native_drop_seen",localDropId=ownedDropCounts.Count,count=item.Count,ownerEntityId=prey.Id});}}
  generatedMeat=ownedDropCounts.Values.Sum();remainingMeat=rawWorld.Sum(q=>q.Count);
  Require(Count(RawMeatBlock.Index)-rawBefore<=generatedMeat,"inventory meat lacks observed target-owned drop provenance");
  if(preyHealth.Health<=0&&!deadLogged){deadLogged=true;HuntingGate.Log(new{kind="native_death_audit",preyHealth.DeathTime,preyHealth.CorpseDuration,hits,misses,path,selfHealth=p.ComponentHealth.Health});}
  if(loot.m_lootDropped&&!lootLogged){lootLogged=true;HuntingGate.Log(new{kind="native_loot_audit",dropped=true,gameTime=p.Project.FindSubsystem<SubsystemGameInfo>(true).TotalElapsedGameTime,ownerMeatCount=p.Project.FindSubsystem<SubsystemPickables>(true).Pickables.Where(q=>q.OwnerEntity==prey&&Terrain.ExtractContents(q.Value)==RawMeatBlock.Index).Sum(q=>q.Count)});}
  if(deadLogged&&lootLogged&&generatedMeat>0&&remainingMeat==0&&Count(RawMeatBlock.Index)-rawBefore==generatedMeat){HuntingGate.Log(new{kind="native_meat_pickup_verified",meat=Count(RawMeatBlock.Index)-rawBefore,generatedMeat,remainingMeat,ownedDrops=ownedDropCounts.Count,provenance="all captured drops OwnerEntity==target; no foreign raw meat; full quantity conserved",hits,misses,path,netHorizontalDisplacement=Vector2.Distance(huntOrigin.XZ,p.ComponentBody.Position.XZ),seconds=elapsed-huntStarted,health=p.ComponentHealth.Health,inventory=Inventory(),views=p.Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count});Done=true;return;}
  if(face?.Result.Status==AtomicActionStatus.Running)return;
  if(stage==22){var status=Call("action_status");if(status.ValueKind==JsonValueKind.Object&&status.GetProperty("status").GetString()=="running")return;stage=20;}
  if(elapsed<nextHunt)return;nextHunt=elapsed+.1;
  var visible=actions.Queries.FindVisibleObjects(10,24);var animal=visible.Entities.FirstOrDefault(e=>e.EntityId==prey.Id);
  HuntingGate.Log(new{kind="hunt_observation",entities=visible.Entities.Select(e=>new{e.EntityId,e.Name,e.Distance,position=new[]{e.Position.X,e.Position.Y,e.Position.Z}}),pickables=visible.Pickables.Select(q=>new{q.Value,q.Count,position=new[]{q.Position.X,q.Position.Y,q.Position.Z}}),path});
  // Decision positions are exclusively from this real filtered observation; health/loot audit never supplies coordinates.
  var meat=visible.Pickables.FirstOrDefault(q=>Terrain.ExtractContents(q.Value)==RawMeatBlock.Index);
  if(meat is not null){face=actions.Start(new MoveTo(meat.Position,.4f,2));return;}
  if(stage==23){Require(elapsed-corpseObservedAt<30,"observed corpse produced no accessible meat within 30 seconds");return;}
  if(animal is not null&&preyHealth.Health<=0){lastSeen=animal.Position;corpseObservedAt=elapsed;stage=23;HuntingGate.Log(new{kind="visible_corpse_confirmed",entityId=animal.EntityId,observedPosition=new[]{lastSeen.X,lastSeen.Y,lastSeen.Z},meaning="death flag sampled only while target is presently visible; no unseen target position used",waitBoundSeconds=30});face=actions.Start(new FaceTowards(lastSeen+new Vector3(0,.2f,0),2));return;}
  if(animal is null){if(++scans>24)throw new InvalidOperationException("target lost; bounded scan exhausted");Call("act_FaceTowards",new{bearing=45,pitch=-10,timeout=2});stage=22;return;}
  scans=0;lastSeen=animal.Position;
  if(stage==21&&animal.Distance<=2f){if(p.Project.FindSubsystem<SubsystemTime>(true).GameTime-p.ComponentMiner.m_lastHitTime<=p.ComponentMiner.HitInterval)return;var before=preyHealth.Health;var attack=actions.Start(new MeleeAttack(prey));if(preyHealth.Health<before)hits++;else misses++;HuntingGate.Log(new{kind="native_attack_audit",result=attack.Result,before,after=preyHealth.Health,weapon=p.ComponentMiner.ActiveBlockValue,selfHealth=p.ComponentHealth.Health});stage=20;if(preyHealth.Health<=0){lastSeen=animal.Position;corpseObservedAt=elapsed;stage=23;HuntingGate.Log(new{kind="visible_target_killed_by_native_attack",entityId=animal.EntityId,observedPosition=new[]{lastSeen.X,lastSeen.Y,lastSeen.Z},meaning="same game-thread attack completed against this freshly visible target; subsequent wait uses last observed position only",waitBoundSeconds=30});face=actions.Start(new FaceTowards(lastSeen+new Vector3(0,.2f,0),2));}return;}
  if(animal.Distance>1.8f){face=actions.Start(new MoveTo(lastSeen,.9f,1));return;}
  face=actions.Start(new FaceTowards(lastSeen+new Vector3(0,.6f,0),2));stage=21;
 }

 static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
 public void Dispose(){try{Call("cancel_task");Call("cancel_action");if(face?.Result.Status==AtomicActionStatus.Running)actions.Cancel(face);}finally{var u=p.Project.FindSubsystem<SubsystemUpdate>(true);u.RemoveUpdateable(this);u.RemoveUpdateable(senses);senses.Dispose();interests.Release();backend.Dispose();}}
}
