using Engine;
using Game;
using Game.Westworld;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
using Westworld.Decisions.Local;
internal static class ToolAgentGate {
 internal static readonly string Root=AppContext.BaseDirectory;internal static string Mode;static readonly Stopwatch clock=Stopwatch.StartNew();
 static bool created,finished,passed;static ToolAgentScenario scenario;static SubsystemUpdate updates;
 internal static void Log(object value){string s=JsonSerializer.Serialize(value);Console.WriteLine(s);File.AppendAllText(Path.Combine(Root,"tool-agent.jsonl"),s+"\n");}
 static int Main(string[] args){if(args.Length!=1||args[0] is not("inventory" or "craft" or "cook" or "hunt" or "natural")||!Root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)||Directory.Exists(Path.Combine(Root,"doc"))||Directory.Exists(Path.Combine(Root,"Mods")))return 2;
  Mode=args[0];if(Mode=="natural"){Log(new{kind="pending",mode=Mode,reason="natural-acquisition provenance gate not implemented; no success claimed"});return 3;}Log(new{kind="start",buildMarker="compact-host-free04",mode=Mode,artificialFixture=Mode is "craft" or "cook" or "hunt",nativeInitialInventoryRetained=true,contextTokens=8192,maxOutputTokens=512,maxModelRounds=12});Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;
 }
 static void Frame(){try{if(finished)return;if(clock.Elapsed.TotalSeconds>=595){Finish(false,"wall_budget_exhausted");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;var w=WorldsManager.CreateWorld(new WorldSettings{Name="Structured tool agent "+Mode,Seed="tool-agent-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}
  if(GameManager.Project is null)return;updates??=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);if(updates.UpdateFailureCount!=0)throw new InvalidOperationException(updates.LastUpdateFailure);
  var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(GameManager.Project){Name="ToolAgent",IsExternalActor=true});ScreensManager.SwitchScreen("Game");}
  if(scenario is null&&players.ComponentPlayers.Count==1&&players.PlayersData[0].IsReadyForPlaying){scenario=new(players.ComponentPlayers[0]);updates.AddUpdateable(scenario);}
  if(scenario?.Done==true)Finish(scenario.Passed,scenario.Reason);
 }catch(Exception e){Log(new{kind="failure",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool ok,string reason){if(finished)return;finished=true;passed=ok;try{scenario?.Dispose();}catch(Exception e){passed=false;reason="cleanup_failure";try{Log(new{kind="cleanup_failure",error=e.ToString()});}catch{}}finally{try{Log(new{kind="result",passed,reason,seconds=clock.Elapsed.TotalSeconds,updateFailures=updates?.UpdateFailureCount});}finally{Window.Close();}}}

}
internal sealed class ToolAgentScenario:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-14;public bool Done{get;private set;}public bool Passed{get;private set;}public string Reason{get;private set;}="pending";
 readonly HuntingFixture hunt;readonly ComponentPlayer p;readonly NativeToolSession gateway;readonly ActorTools tools;readonly AtomicActions actions;
 readonly NativeActorSession senses;readonly ScapiInterestBackend backend;readonly InterestCoordinator interests;readonly LlamaProcessProvider model;readonly CancellationTokenSource cancellation=new();
 readonly List<JsonElement> recent=[];readonly Queue<ToolDecisionStep> steps=[];readonly Vector3 origin;readonly long initialEaten;readonly Dictionary<int,int> baseline;
 Task<ToolDecisionPlan> inference;ToolDecisionPrompt activePrompt;string waiting;ToolDecisionStep waitingStep;ToolReceipt waitingReceipt;int rounds;double time,nextPoll,nextPlan,path;Vector3 previous;bool disposed;bool mealVerified;float eatBefore;long eatenBefore;int eatValue,eatCountBefore;
 public ToolAgentScenario(ComponentPlayer player){p=player;origin=previous=p.ComponentBody.Position;initialEaten=p.PlayerStats.FoodItemsEaten;
  var inv=p.ComponentMiner.Inventory;baseline=Enumerable.Range(0,inv.SlotsCount).Where(i=>inv.GetSlotCount(i)>0).GroupBy(i=>Terrain.ExtractContents(inv.GetSlotValue(i))).ToDictionary(g=>g.Key,g=>g.Sum(inv.GetSlotCount));
  actions=new(p.Entity);tools=new(actions,true);gateway=new(Guid.NewGuid(),p,tools);
  senses=new NativeActorSession(p.Project,gateway.SessionId,actionsEnabled:false);senses.Bind(gateway.ActorId,p);p.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);var terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);backend=new(terrain,p.Project.FindSubsystem<SubsystemSpawn>(true));interests=new(senses.Registry,backend,new InterestRadii(16,32));interests.Sync();terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);
  string local=Path.GetFullPath(Path.Combine(ToolAgentGate.Root,"..","local-model"));model=new(Path.Combine(local,"llama-b12211","llama-completion"),Path.Combine(local,"Qwen3.5-4B-Q4_K_M.gguf"),_=>false,TimeSpan.FromSeconds(120),contextTokens:8192,maxOutputTokens:512);
  if(ToolAgentGate.Mode is "craft" or "cook"){
   int x=Terrain.ToCell(origin.X),y=Terrain.ToCell(origin.Y),z=Terrain.ToCell(origin.Z);
   for(int a=x-3;a<=x+3;a++)for(int b=z-3;b<=z+3;b++){terrain.ChangeCell(a,y-1,b,DirtBlock.Index);for(int c=y;c<=y+3;c++)terrain.ChangeCell(a,c,b,0);}
   p.ComponentBody.Position=new(x+.5f,y,z+.5f);p.ComponentBody.Velocity=Vector3.Zero;p.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);
   if(ToolAgentGate.Mode=="craft"){terrain.ChangeCell(x+2,y+1,z,OakWoodBlock.Index);terrain.ChangeCell(x+2,y+1,z+1,OakWoodBlock.Index);}
   else{terrain.ChangeCell(x+2,y,z,FurnaceBlock.Index);var drops=p.Project.FindSubsystem<SubsystemPickables>(true);drops.AddPickable(RawMeatBlock.Index,1,p.ComponentBody.Position+new Vector3(.4f,.2f,0),Vector3.Zero,null);drops.AddPickable(PlanksBlock.Index,1,p.ComponentBody.Position+new Vector3(0,.2f,.4f),Vector3.Zero,null);}
   ToolAgentGate.Log(new{kind="artificial_fixture",mode=ToolAgentGate.Mode,resources=ToolAgentGate.Mode=="craft"?"two world oak blocks; no crafted items":"world furnace plus one rawmeat/one plank world drop; not hunting",inventoryInjection=false,publicRecipeGuide=true});
  }
  if(ToolAgentGate.Mode=="hunt")hunt=HuntingFixture.SetupFixture(p,ToolAgentGate.Log,500);
  Call("read_inventory",new{},false);nextPlan=.8;
 }
 ToolReceipt Call(string name,object args,bool remember=true){var r=gateway.Call(name,args);ToolAgentGate.Log(new{kind="tool_receipt",receipt=r,arguments=args});if(remember){string storage=null;if(args is JsonElement e&&e.TryGetProperty("storage",out var st)&&st.ValueKind==JsonValueKind.String)storage=st.GetString();else{var a=JsonSerializer.SerializeToElement(args);if(a.TryGetProperty("storage",out var st2)&&st2.ValueKind==JsonValueKind.String)storage=st2.GetString();}
  recent.Add(JsonSerializer.SerializeToElement(new{tool=name,ok=r.State=="returned",data=r.Data,storage,error=r.Error,state=r.State}));while(recent.Count>8)recent.RemoveAt(0);}return r;}
 (JsonElement Observation,JsonElement Inventory) Snapshot(){var o=Call("observe",new{look_distance=16,object_distance=16,limit=4},false);var i=Call("read_inventory",new{},false);if(o.State!="returned"||i.State!="returned")throw new InvalidOperationException("Cannot obtain actual actor snapshot");return(o.Data,i.Data);}
 string Goal=>ToolAgentGate.Mode switch{"inventory"=>"Eat an actually owned edible item.","craft"=>"Obtain one wooden club through actual available resources and native crafting.","hunt"=>"Obtain meat by interacting with actual visible prey using available native tools; preserve your health.","cook"=>"Produce cooked meat from the available raw meat and eat that cooked meat, not the initial fish.",_=>"Obtain an additional resource from the natural world. Eating the initial fish is not newly acquired food."};
 object Manual=>new{items=new[]{new{id=9,name="oak wood"},new{id=21,name="planks"},new{id=23,name="sticks"},new{id=27,name="crafting table"},new{id=64,name="furnace"},new{id=88,name="raw meat"},new{id=89,name="cooked meat"},new{id=122,name="wooden club"},new{id=162,name="cooked fish"}},note="Static public item labels, not possessions or a plan. Native recipe and storage queries provide actual requirements and slot roles."};
 JsonElement actionState,taskState;
 static object CompactInventory(JsonElement inventory)=>new{slots=inventory.GetProperty("slots").EnumerateArray().Where(s=>s.GetProperty("count").GetInt32()>0).ToArray(),emptySlotIds=inventory.GetProperty("slots").EnumerateArray().Where(s=>s.GetProperty("count").GetInt32()==0).Select(s=>s.GetProperty("slot").GetInt32()).ToArray(),selectedSlot=inventory.GetProperty("selectedSlot")};
 IReadOnlyList<ToolOffer> Menu(IReadOnlyList<ToolOffer> offers){var a=Call("action_status",new{},false);var t=Call("task_status",new{},false);if(a.State!="returned"||t.State!="returned")throw new InvalidOperationException("Cannot read actual activity state");actionState=a.Data;taskState=t.Data;
  bool active=Running(a)||Running(t);return offers.Where(o=>o.Name is not ("observe" or "read_self" or "read_inventory" or "action_status" or "task_status")&&(active||o.Name is not ("cancel_action" or "cancel_task"))).ToArray();}
 ToolDecisionPrompt Prompt(JsonElement o,JsonElement i,IReadOnlyList<ToolOffer> offers)=>new(gateway.SessionId,gateway.ActorId,Guid.NewGuid(),JsonSerializer.SerializeToElement(new{goal=Goal,observation=o,inventory=CompactInventory(i),terminal=new{action=actionState,task=taskState},host="Host supplies fresh observation/self/inventory and action/task status; waits for native terminal outcomes; cancels on uncertainty/shutdown. Native query/cancel capabilities remain available in gateway.",recentResults=recent.Where(r=>!r.TryGetProperty("tool",out var t)||t.GetString() is not ("observe" or "read_self" or "read_inventory" or "action_status" or "task_status")).ToArray(),manual=Manual}),offers,4);
 int Count(int contents){var inv=p.ComponentMiner.Inventory;return Enumerable.Range(0,inv.SlotsCount).Where(i=>Terrain.ExtractContents(inv.GetSlotValue(i))==contents).Sum(inv.GetSlotCount);}
 static string Status(JsonElement data)=>data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("status",out var s)?s.GetString():null;
 bool Running(ToolReceipt r)=>r.State=="returned"&&Status(r.Data) is "running" or "waiting" or "blocked";
 void Fail(string reason){steps.Clear();Done=true;Reason=reason;}
 void ClearPlan(string reason){steps.Clear();recent.Add(JsonSerializer.SerializeToElement(new{tool="host_feedback",ok=false,error=reason}));while(recent.Count>8)recent.RemoveAt(0);nextPlan=time+.25;}
 void Reconcile(){if(gateway.RequiresReconciliation is not long uncertain)return;steps.Clear();var a=Call("action_status",new{},false);var t=Call("task_status",new{},false);var inventory=Call("read_inventory",new{});
  if(Running(a)||Running(t)){Call("cancel_task",new{});Call("cancel_action",new{});return;}
  if(a.State=="returned"&&t.State=="returned"&&inventory.State=="returned"){var o=Call("observe",new{look_distance=16,object_distance=16,limit=4});if(o.State!="returned")return;gateway.Reconcile(uncertain,t.Sequence);waiting=null;waitingStep=null;ToolAgentGate.Log(new{kind="reconciled",uncertain,evidence=t.Sequence});}}

 public void Update(float dt){time+=dt;path+=(p.ComponentBody.Position-previous).XZ.Length();previous=p.ComponentBody.Position;interests.Sync();if(Done)return;hunt?.Update();if(hunt?.Failed==true){Fail("hunt_fixture_"+hunt.FailureReason);return;}
  if(p.ComponentHealth.Health<.25f||p.GameWidget is not null){Fail("actor_health_or_view_guard");return;}
  if(gateway.RequiresReconciliation.HasValue){if(time>=nextPoll){nextPoll=time+.5;Reconcile();}return;}
  if(waiting is not null){if(time<nextPoll)return;nextPoll=time+.25;var r=Call(waiting,new{},false);if(Running(r))return;
   recent.Add(JsonSerializer.SerializeToElement(new{tool=waiting,ok=r.State=="returned"&&Status(r.Data)=="succeeded",data=r.Data,error=r.Error}));while(recent.Count>8)recent.RemoveAt(0);
   bool success=r.State=="returned"&&Status(r.Data)=="succeeded";if(success&&waitingStep.Tool=="act_Eat")CheckEat();waiting=null;waitingStep=null;if(!success)ClearPlan("native_terminal_failure");return;}
  if(CheckGoal()){Passed=Done=true;Reason="actual_inventory_or_native_eating_goal_verified";return;}
  if(inference is not null){if(!inference.IsCompleted)return;try{var plan=inference.GetAwaiter().GetResult();ToolAgentGate.Log(new{kind="model_response",round=rounds,plan,metrics=model.LastMetrics,raw=model.LastResponse});foreach(var step in plan.Steps)steps.Enqueue(step);if(plan.Steps.Count==0)nextPlan=time+1;}
   catch(Exception e){ToolAgentGate.Log(new{kind="model_failure",round=rounds,error=e.ToString(),metrics=model.LastMetrics,raw=model.LastResponse});ClearPlan("model_response_failed");}inference=null;return;}
  if(steps.Count>0){var step=steps.Dequeue();try{var snap=Snapshot();var offers=Menu(NativeToolOffers.Build(gateway.GetTools(),snap.Observation,snap.Inventory,recent));var validation=Prompt(snap.Observation,snap.Inventory,offers);validation.ParseResponse(JsonSerializer.Serialize(new{goal="Revalidate pending model step against current evidence",steps=new[]{new{tool=step.Tool,arguments=step.Arguments}}}));
    if(step.Tool=="act_Eat"){eatValue=step.Arguments.GetProperty("value").GetInt32();eatCountBefore=Count(Terrain.ExtractContents(eatValue));eatBefore=p.ComponentVitalStats.Food;eatenBefore=p.PlayerStats.FoodItemsEaten;}
    var receipt=Call(step.Tool,step.Arguments);if(receipt.State!="returned"){ClearPlan("native_request_failed");return;}
    string status=Status(receipt.Data);if(step.Tool=="plan_craft"||step.Tool.StartsWith("act_",StringComparison.Ordinal)){
     if(status is "running" or "waiting" or "blocked"){waiting=step.Tool=="plan_craft"?"task_status":"action_status";waitingStep=step;waitingReceipt=receipt;nextPoll=time+.1;}
     else if(status=="succeeded"){if(step.Tool=="act_Eat")CheckEat();}
     else ClearPlan("native_immediate_failure");}
   }catch(Exception e){ToolAgentGate.Log(new{kind="step_revalidation_failed",step,error=e.Message});ClearPlan("step_stale_or_invalid");}return;}
  if(time<nextPlan)return;if(rounds>=12){Fail("model_round_budget_exhausted");return;}
  try{var s=Snapshot();var offers=Menu(NativeToolOffers.Build(gateway.GetTools(),s.Observation,s.Inventory,recent));activePrompt=Prompt(s.Observation,s.Inventory,offers);rounds++;ToolAgentGate.Log(new{kind="model_prompt",round=rounds,prompt=activePrompt.Prompt,schema=activePrompt.ResponseSchema,offerCount=offers.Count,promptChars=activePrompt.Prompt.Length});var prompt=activePrompt;inference=Task.Run(()=>model.CompleteStructuredAsync(prompt.Prompt,prompt.ResponseSchema,prompt.ParseResponse,cancellation.Token));}
  catch(Exception e){ToolAgentGate.Log(new{kind="prompt_failure",error=e.ToString()});Fail("prompt_budget_or_schema_failure");}
 }
 void CheckEat(){bool correct=ToolAgentGate.Mode=="inventory"||Terrain.ExtractContents(eatValue)==CookedMeatBlock.Index;if(correct&&Count(Terrain.ExtractContents(eatValue))<eatCountBefore&&p.PlayerStats.FoodItemsEaten>eatenBefore&&p.ComponentVitalStats.Food>eatBefore){mealVerified=true;ToolAgentGate.Log(new{kind="native_eating_verified",value=eatValue,foodBefore=eatBefore,foodAfter=p.ComponentVitalStats.Food,itemsBefore=eatCountBefore,itemsAfter=Count(Terrain.ExtractContents(eatValue))});}}
 bool CheckGoal()=>ToolAgentGate.Mode switch{"inventory" or "cook"=>mealVerified,"craft"=>Count(WoodenClubBlock.Index)>baseline.GetValueOrDefault(WoodenClubBlock.Index),"hunt"=>hunt?.Complete==true,"natural"=>Enumerable.Range(0,p.ComponentMiner.Inventory.SlotsCount).Select(p.ComponentMiner.Inventory.GetSlotValue).Select(Terrain.ExtractContents).Distinct().Any(c=>c!=0&&Count(c)>baseline.GetValueOrDefault(c)),_=>false};
 public void Dispose(){if(disposed)return;disposed=true;var errors=new List<Exception>();void Clean(Action action){try{action();}catch(Exception e){errors.Add(e);}}
  cancellation.Cancel();if(inference is not null&&!inference.IsCompleted){try{inference.Wait(TimeSpan.FromSeconds(3));}catch(AggregateException){}if(!inference.IsCompleted)errors.Add(new InvalidOperationException("Inference did not stop within cleanup deadline"));}
  Clean(()=>Call("cancel_task",new{}));Clean(()=>Call("cancel_action",new{}));
  Clean(()=>{var a=Call("action_status",new{},false);var t=Call("task_status",new{},false);if(a.State!="returned"||t.State!="returned"||Running(a)||Running(t))throw new InvalidOperationException("Native shutdown state unverified or still active");});
  Clean(()=>{var finalInventory=Call("read_inventory",new{},false);if(finalInventory.State!="returned")throw new InvalidOperationException("Final inventory read failed");ToolAgentGate.Log(new{kind="final_audit",inventory=finalInventory.Data,rounds,path,netDisplacement=(p.ComponentBody.Position-origin).XZ.Length(),food=p.ComponentVitalStats.Food,health=p.ComponentHealth.Health,eaten=p.PlayerStats.FoodItemsEaten-initialEaten,modelInFlight=inference is not null&&!inference.IsCompleted,requiresReconciliation=gateway.RequiresReconciliation,huntAudit=hunt?.AuditSnapshot()});});
  Clean(()=>hunt?.Dispose());var u=p.Project.FindSubsystem<SubsystemUpdate>(true);Clean(()=>u.RemoveUpdateable(this));Clean(()=>u.RemoveUpdateable(senses));Clean(()=>senses.Dispose());Clean(()=>interests.Release());Clean(()=>backend.Dispose());if(errors.Count>0)throw new AggregateException(errors);
 }
}
