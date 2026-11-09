using System.Diagnostics;
using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
static class Gate {
 static readonly string root=AppContext.BaseDirectory;static readonly Stopwatch clock=Stopwatch.StartNew();static bool created,finished,passed;static Scenario scenario;public static string Mode;
 public static void Log(object o){string s=JsonSerializer.Serialize(o);Console.WriteLine(s);File.AppendAllText(Path.Combine(root,"map-memory.jsonl"),s+"\n");}
 static int Main(string[] args){if(args.Length!=0||!root.Contains("/work/")||Directory.Exists(Path.Combine(root,"doc"))||Directory.Exists(Path.Combine(root,"Mods")))return 2;Mode="map";Log(new{kind="start",mode=Mode,artificialFlatFixture=true,deterministicDecisionNotModel=true});Window.Created+=()=>Window.Frame+=Frame;try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;}
 static void Frame(){try{if(finished)return;if(clock.Elapsed.TotalSeconds>60){Finish(false,"wall_budget");return;}if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;var w=WorldsManager.CreateWorld(new WorldSettings{Name="Map memory "+Mode,Seed="map-memory-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}var project=GameManager.Project;if(project is null)return;var update=project.FindSubsystem<SubsystemUpdate>(true);if(update.UpdateFailureCount>0)throw new InvalidOperationException(update.LastUpdateFailure);var players=project.FindSubsystem<SubsystemPlayers>(true);if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(project){Name="NavigationActor",IsExternalActor=true});ScreensManager.SwitchScreen("Game");}if(scenario is null&&players.ComponentPlayers.Count==1&&players.PlayersData[0].IsReadyForPlaying){scenario=new(players.ComponentPlayers[0]);update.AddUpdateable(scenario);}if(scenario?.Done==true)Finish(scenario.Passed,scenario.Reason);}catch(Exception e){Log(new{kind="failure",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool ok,string reason){if(finished)return;finished=true;passed=ok;bool cleanupVerified=false;try{scenario?.Dispose();cleanupVerified=true;}catch(Exception e){passed=false;reason="cleanup_failed";Log(new{kind="cleanup_error",error=e.ToString()});}finally{try{Log(new{kind="result",passed,reason,seconds=clock.Elapsed.TotalSeconds,cleanupVerified,updateFailureCount=GameManager.Project?.FindSubsystem<SubsystemUpdate>(false)?.UpdateFailureCount});}finally{Window.Close();}}}
}
sealed class Scenario:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-14;public bool Done{get;private set;}public bool Passed{get;private set;}public string Reason{get;private set;}="pending";
 readonly ComponentPlayer p;readonly AtomicActions actions;readonly ActorTools tools;readonly NativeToolSession gateway;readonly NativeActorSession senses;readonly ScapiInterestBackend backend;readonly InterestCoordinator interest;readonly SubsystemTerrain terrain;readonly Stopwatch clock=Stopwatch.StartNew();
 Vector3 origin;Point3 landmark;int stage=-2,checks;AtomicActionRun aim;string oldLive,rememberedLandmark,rememberedOrigin;double startTime;
 void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);checks++;Gate.Log(new{kind="assertion",name,passed=true});}
 ToolReceipt Call(string name,object args){var r=gateway.Call(name,args);Gate.Log(new{kind="receipt",name,args,receipt=r});return r;}
 void ReconcileRejected(){if(gateway.RequiresReconciliation is long pending){var status=Call("action_status",new{});Check(Status(status) is not("running" or "waiting"),"rejected_write_has_no_running_action");var inventory=Call("read_inventory",new{});Check(inventory.State=="returned","reconciliation_inventory_read");gateway.Reconcile(pending,inventory.Sequence);}}
 JsonElement Observe()=>Call("observe",new{look_distance=16,object_distance=16,limit=4}).Data;
 string Status(ToolReceipt r)=>r.Data.ValueKind==JsonValueKind.Object&&r.Data.TryGetProperty("status",out var s)?s.GetString():null;
 bool Finished(){var r=Call("action_status",new{});string s=Status(r);if(s is "running" or "waiting")return false;Check(s=="succeeded","native_action_succeeded");return true;}
 public Scenario(ComponentPlayer player){
  p=player;actions=new(p.Entity);tools=new(actions,true);gateway=new(Guid.NewGuid(),p,tools);
  senses=new NativeActorSession(p.Project,gateway.SessionId,actionsEnabled:false);senses.Bind(gateway.ActorId,p);p.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);
  terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);backend=new(terrain,p.Project.FindSubsystem<SubsystemSpawn>(true));interest=new(senses.Registry,backend,new InterestRadii(16,32));interest.Sync();terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);
  var b=p.ComponentBody.Position;int x=Terrain.ToCell(b.X),y=Terrain.ToCell(b.Y),z=Terrain.ToCell(b.Z);
  for(int xx=x-5;xx<=x+5;xx++)for(int zz=z-5;zz<=z+5;zz++){terrain.ChangeCell(xx,y-1,zz,DirtBlock.Index);for(int yy=y;yy<y+4;yy++)terrain.ChangeCell(xx,yy,zz,0);}
  p.ComponentBody.Position=origin=new(x+.5f,y,z+.5f);p.ComponentBody.Velocity=Vector3.Zero;p.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);
  landmark=new(x+2,y+1,z);terrain.ChangeCell(landmark.X,landmark.Y,landmark.Z,OakWoodBlock.Index);
  Gate.Log(new{kind="fixture",artificialWorldEdit=true,landmark=new[]{landmark.X,landmark.Y,landmark.Z},origin=new[]{origin.X,origin.Y,origin.Z},note="Isolated sightline fixture; no model/API, no inventory injection. Later replacement is an explicit fixture mutation."});
 }
 public void Update(float dt){
  if(Done)return;if(clock.Elapsed.TotalSeconds>35)throw new InvalidOperationException("scenario_wall_budget");interest.Sync();
  if(stage==-2){if(clock.Elapsed.TotalSeconds<2||p.ComponentBody.Velocity.Length()>.1)return;aim=actions.Start(new FaceTowards(new Vector3(landmark.X+.5f,landmark.Y+.5f,landmark.Z+.5f),3));Gate.Log(new{kind="initial_fixture_aim",result=aim.Result});stage=-1;return;}
  if(stage==-1){if(actions.IsActionRunning)return;Gate.Log(new{kind="initial_fixture_aim_finished",result=aim.Result});Check(aim.Result.Status==AtomicActionStatus.Succeeded,"initial_aim_succeeded");stage=0;}
  if(stage==0){var o=Observe();Check(o.TryGetProperty("focus",out var f)&&f.GetProperty("blockValue").GetInt32()==OakWoodBlock.Index,"real_ray_observes_fixture_oak");oldLive=f.GetProperty("reference").GetString();startTime=o.GetProperty("gameTime").GetDouble();
   var legacy=Call("read_map",new{});Check(legacy.State=="returned"&&legacy.Data.ValueKind==JsonValueKind.Array,"noarg_legacy_array");
   var entries=legacy.Data.EnumerateArray().ToArray();Check(entries.Length>=2,"standing_and_landmark_recorded");
   rememberedLandmark=entries.Single(e=>e.TryGetProperty("name",out var name)&&name.ValueKind==JsonValueKind.String&&name.GetString()==f.GetProperty("name").GetString()).GetProperty("reference").GetString();
   rememberedOrigin=entries.First(e=>!e.TryGetProperty("name",out var name)||name.ValueKind==JsonValueKind.Null).GetProperty("reference").GetString();
   int offset=0,read=0;do{var page=Call("read_map",new{offset,limit=1});Check(page.State=="returned","native_page_returns");var d=page.Data;read+=d.GetProperty("entries").GetArrayLength();Check(d.GetProperty("offset").GetInt32()==offset&&d.GetProperty("total").GetInt32()==entries.Length,"page_metadata_truth");
    if(!d.GetProperty("hasMore").GetBoolean()){Check(!d.TryGetProperty("nextOffset",out var n)||n.ValueKind==JsonValueKind.Null,"last_page_no_cursor");break;}offset=d.GetProperty("nextOffset").GetInt32();
   }while(read<128);Check(read==entries.Length,"all_pages_same_capture_complete");
   Check(Call("read_map",new{offset=129,limit=1}).State!="returned","offset_out_of_bounds_rejected");
   Call("act_FaceTowards",new{bearing=180,pitch=-15,timeout=3});stage=2;return;
  }

  if(stage==2){if(!Finished())return;var o=Observe();Check(!o.TryGetProperty("focus",out var f)||f.ValueKind==JsonValueKind.Null||f.GetProperty("reference").GetString()!=oldLive,"original_live_reference_no_longer_observed");
   var legacy=Call("read_map",new{});var old=legacy.Data.EnumerateArray().Single(e=>e.GetProperty("reference").GetString()==rememberedLandmark);Check(old.GetProperty("lastSeenGameTime").GetDouble()==startTime,"historical_time_not_refreshed_by_turning");
   foreach(var tool in new[]{"act_Dig","act_MeleeAttack","act_PickUp","act_FaceTowards"}){
    object args=tool=="act_FaceTowards"?new{destination=rememberedLandmark,timeout=1}:tool=="act_PickUp"?new{target=rememberedLandmark,count=1}:tool=="act_MeleeAttack"?new{target=rememberedLandmark}:(object)new{target=rememberedLandmark,timeout=1};
    Check(Call(tool,args).State!="returned","memory_ref_not_"+tool);ReconcileRejected();
   }
   terrain.ChangeCell(landmark.X,landmark.Y,landmark.Z,GraniteBlock.Index);Gate.Log(new{kind="fixture_landmark_replaced",landmark=new[]{landmark.X,landmark.Y,landmark.Z},oldValue=OakWoodBlock.Index,newValue=GraniteBlock.Index});
   Check(Call("act_Dig",new{target=oldLive,timeout=1}).State!="returned","old_live_oak_reference_cannot_modify_changed_block");ReconcileRejected();
   Check(terrain.Terrain.GetCellContents(landmark.X,landmark.Y,landmark.Z)==GraniteBlock.Index,"changed_resource_not_dug");
   Call("act_MoveTo",new{destination=rememberedOrigin,range=.4,timeout=4});stage=3;return;
  }
  if(stage==3){if(!Finished())return;Check((p.ComponentBody.Position-origin).XZ.Length()<.7,"remembered_standing_position_native_move_resolved");Check(p.GameWidget is null,"no_ai_viewport");Passed=true;Done=true;Reason="native_memory_navigation_and_permissions_verified";Gate.Log(new{kind="summary",checks,Passed,modelUsed=false,resourceClaim=false,displacement=(p.ComponentBody.Position-origin).XZ.Length(),navigationClaim="standing-point resolution only, not long-distance return",position=new[]{p.ComponentBody.Position.X,p.ComponentBody.Position.Y,p.ComponentBody.Position.Z}});}
 }
 public void Dispose(){Call("cancel_action",new{});p.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(this);p.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(senses);senses.Dispose();interest.Release();backend.Dispose();}
}
