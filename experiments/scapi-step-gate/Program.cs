using Engine;using Game;using Game.Westworld;using Westworld.Runtime.Core;using Westworld.Scapi.Adapter;using System.Diagnostics;using System.Text.Json;
static class Gate {
 static readonly string Root=AppContext.BaseDirectory;static readonly Stopwatch Wall=Stopwatch.StartNew();static string mode;static bool created,done,passed;static Runner runner;
 public static void Log(object o){var s=JsonSerializer.Serialize(o);Console.WriteLine(s);File.AppendAllText(Path.Combine(Root,"step.jsonl"),s+"\n");}
 [STAThread]
 static int Main(string[] args){if(args.Length!=1||args[0] is not("survival-goal-crop" or "survival-goal-pending-hunger" or "survival-goal-pending-cold" or "survival-goal-contract" or "survival-goal-target" or "survival-goal-urgent-food" or "survival-goal-explore" or "survival-goal-hearing" or "survival-goal-fallback" or "survival-goal-night" or "survival-goal-warming" or "up" or "down" or "continuous" or "navigate" or "navigate-turn" or "navigate-drop" or "navigate-edge" or "navigate-inset" or "navigate-vegetation" or "navigate-cold" or "navigate-natural-stall" or "navigate-natural-cotton" or "navigate-natural-step-loop" or "predator-pumpkin-corner" or "predator-length-boundary" or "forage" or "forage-far" or "forage-target" or "forage-observed-path" or "forage-pumpkin" or "food-safety" or "food-origin" or "food-budget" or "production-kit" or "production-kitchen" or "production-hunt" or "production-hunt-ranged" or "production-hunt-approach" or "production-hunt-tracking" or "production-project" or "production-gear-while-prey-hidden" or "production-resume-gear" or "threat-range" or "threat-rhino-range" or "predator-memory" or "predator-cliff" or "predator-edge-return" or "predator-edge-tip" or "predator-clearance" or "predator-shelter-clearance" or "predator-coast-turn" or "predator-shelter-route-detour" or "shelter" or "shelter-retry" or "shelter-wet" or "shelter-night-hungry" or "shelter-site-choice" or "shelter-predator" or "shelter-middle-predator" or "shelter-lower-predator" or "shelter-late-predator" or "shelter-passive-cow" or "shelter-fast-reseal" or "shelter-fast-reseal-changed-anchor" or "shelter-far-wolf-outlet" or "shelter-far-wolf-no-outlet" or "shelter-backpack-earth" or "shelter-full-storage" or "shelter-illness" or "shelter-camp" or "shelter-shallow" or "coast" or "coast-boat")||!Root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)||Directory.Exists(Path.Combine(Root,"doc")))return 2;mode=args[0];Window.StartHidden=true;Log(new{kind="start",mode,fixture=true,models=0});Window.Created+=()=>Window.Frame+=Frame;try{IsolatedGameSettings.PrepareWindows(Root);Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;}
 static void Frame(){if(done)return;try{
  if(Wall.Elapsed.TotalSeconds>(mode.StartsWith("survival-goal-",StringComparison.Ordinal) ? 100 : mode == "production-project" ? 150 : mode is "shelter-wet" or "shelter-night-hungry" or "shelter-camp" or "production-hunt" or "production-hunt-ranged" or "production-hunt-approach" or "production-hunt-tracking"?90:60)){Finish(false,"timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;bool natural=mode is "navigate-natural-stall" or "navigate-natural-cotton" or "navigate-natural-step-loop" or "predator-coast-turn" or "predator-shelter-route-detour" or "predator-pumpkin-corner" or "predator-length-boundary";var settings=new WorldSettings{Name="Observed step fixture",Seed=natural?"natural34-b":"actors-cloud-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion};if(!natural&&mode is not("up" or "down"))settings.TerrainGenerationMode=TerrainGenerationMode.FlatContinent;if(mode.StartsWith("survival-goal-",StringComparison.Ordinal)||mode is "shelter" or "shelter-wet" or "shelter-night-hungry" or "shelter-site-choice" or "shelter-predator" or "shelter-camp" or "production-hunt" or "production-hunt-ranged" or "production-hunt-approach" or "production-hunt-tracking" or "production-project" or "production-gear-while-prey-hidden" or "production-resume-gear" or "navigate-cold" or "predator-edge-return" or "predator-edge-tip" or "predator-clearance" or "predator-shelter-clearance" or "predator-coast-turn" or "predator-shelter-route-detour" or "forage-far" or "forage-target" or "forage-observed-path" or "forage-pumpkin" or "predator-memory" or "navigate-natural-cotton" or "navigate-natural-step-loop" or "predator-pumpkin-corner" or "predator-length-boundary" or "shelter-middle-predator" or "shelter-lower-predator" or "shelter-late-predator" or "shelter-passive-cow" or "shelter-fast-reseal" or "shelter-fast-reseal-changed-anchor" or "shelter-far-wolf-outlet" or "shelter-far-wolf-no-outlet" or "shelter-backpack-earth" or "shelter-full-storage")settings.EnvironmentBehaviorMode=EnvironmentBehaviorMode.Static;var w=WorldsManager.CreateWorld(settings);ScreensManager.SwitchScreen("GameLoading",w,null);}
  if(GameManager.Project is null)return;var u=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);if(u.UpdateFailureCount!=0)throw new Exception(u.LastUpdateFailure);var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
  if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(GameManager.Project){Name="StepActor",IsExternalActor=true});ScreensManager.SwitchScreen("Game");}
  if(runner==null&&players.ComponentPlayers.Count==1&&players.PlayersData[0].IsReadyForPlaying){runner=new Runner(players.ComponentPlayers[0],mode);u.AddUpdateable(runner);}
  if(runner?.Done==true)Finish(runner.Passed,"native step finished");
 }catch(Exception e){Log(new{kind="error",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool ok,string reason){done=true;passed=ok;Log(new{kind="result",passed,reason,seconds=Wall.Elapsed.TotalSeconds,updateFailures=GameManager.Project?.FindSubsystem<SubsystemUpdate>(true).UpdateFailureCount});runner?.Dispose();Window.Close();}
}
sealed partial class Runner:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-14;public bool Done,Passed;readonly ComponentPlayer p;readonly bool up;readonly NativeActorSession senses;readonly LocalObservationMemory memory;readonly AtomicActions actions;readonly Guid actor=Guid.NewGuid();readonly ScapiInterestBackend backend;readonly InterestCoordinator interest;readonly SubsystemTerrain terrain;
 double elapsed;int stage,scan;AtomicActionRun run;ObservedStepRoute chosen;long started;Vector3 target;HashSet<(int,int,int)> proofAir;
 readonly bool continuous;
 PlaySurvivalTravel? travel;
 bool jumped;
 readonly string mode;
 readonly InterestRadii mapRange;
 public Runner(ComponentPlayer p,string mode){this.p=p;this.mode=mode;up=mode!="down";continuous=mode=="continuous";mapRange=mode=="survival-goal-explore"?new(80,96):new(16,mode is "forage" or "forage-far" or "forage-target" or "forage-observed-path"?48:32);terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);senses=new(p.Project,Guid.NewGuid(),false,3);senses.Bind(actor,p);p.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);memory=new(senses.SessionId,actor);actions=new(p.Entity);backend=new(terrain,p.Project.FindSubsystem<SubsystemSpawn>(true));interest=new(senses.Registry,backend,mapRange);interest.Sync();terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);}
 public void Update(float dt){if(Done)return;elapsed+=dt;interest.Sync();try{
  if(mode.StartsWith("survival-goal-",StringComparison.Ordinal)){RunSurvivalGoal();return;}
  if(mode is "coast" or "coast-boat"){RunCoast();return;}
  if(mode is "navigate" or "navigate-turn" or "navigate-drop" or "navigate-edge" or "navigate-inset" or "navigate-vegetation" or "navigate-cold"){RunRollingNavigation();return;}
  if(mode=="forage-pumpkin"){RunPumpkinHarvest();return;}
  if(mode=="food-safety"){RunFoodSafety();return;}
  if(mode=="food-origin"){RunFoodOrigin();return;}
  if(mode=="food-budget"){RunFoodBudget();return;}
  if(mode is "production-kit" or "production-kitchen" or "production-hunt" or "production-hunt-ranged" or "production-hunt-approach" or "production-hunt-tracking" or "production-project" or "production-gear-while-prey-hidden" or "production-resume-gear"){RunProduction();return;}
  if(mode is "threat-range" or "threat-rhino-range" or "predator-memory"){RunThreatRange();return;}
  if(mode is "predator-cliff" or "predator-edge-return" or "predator-edge-tip" or "predator-clearance" or "predator-shelter-clearance"){RunPredatorCliff();return;}
  if(mode is "predator-coast-turn" or "predator-shelter-route-detour"){RunPredatorCoastTurn();return;}
  if(mode=="shelter-shallow"){RunShallowShelter();return;}
  if(mode=="shelter-site-choice"){RunShelterSiteChoice();return;}
  if(mode is "navigate-natural-stall" or "navigate-natural-cotton" or "navigate-natural-step-loop" or "predator-pumpkin-corner" or "predator-length-boundary"){RunNaturalStall();return;}
  if(mode is "forage" or "forage-far" or "forage-target" or "forage-observed-path" or "shelter" or "shelter-retry" or "shelter-wet" or "shelter-night-hungry" or "shelter-predator" or "shelter-middle-predator" or "shelter-lower-predator" or "shelter-late-predator" or "shelter-passive-cow" or "shelter-fast-reseal" or "shelter-fast-reseal-changed-anchor" or "shelter-far-wolf-outlet" or "shelter-far-wolf-no-outlet" or "shelter-backpack-earth" or "shelter-full-storage" or "shelter-illness" or "shelter-camp"){RunSurvivalSkill();return;}
  if(stage==0&&elapsed>3){var pos=p.ComponentBody.Position;int x=Terrain.ToCell(pos.X),y=Terrain.ToCell(pos.Y),z=Terrain.ToCell(pos.Z);int dy=up?1:-1;
   for(int a=x-4;a<=x+4;a++)for(int b=z-4;b<=z+4;b++)for(int c=y-3;c<=y+5;c++)terrain.ChangeCell(a,c,b,(Math.Abs(a-x)==4||Math.Abs(b-z)==4)&&c<=y+3?3:c<=(a>=x+1?y+dy-1:y-1)?3:0);
   p.ComponentBody.Position=new(x+(up?.5f:.74f),y,z+.5f);p.ComponentBody.Velocity=Vector3.Zero;
   if(continuous)p.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);
   target=new(x+1.5f,y+dy,z+.5f);Gate.Log(new{kind="fixture",up,origin=p.ComponentBody.Position.ToString(),target=target.ToString(),teleportOnlyForSetup=true,perimeterWaterBarrier=true});stage=1;elapsed=0;
  }else if(continuous&&stage==1&&elapsed>.7){RunContinuous();}
  else if(stage==1&&elapsed>.7){var o=senses.Observe(senses.SessionId,actor);var window=memory.Observe(o);var routes=ObservedStepRoutes.Project(window);chosen=routes.FirstOrDefault(r=>Math.Abs(r.Destination.X-target.X)<.01&&Math.Abs(r.Destination.Z-target.Z)<.01&&Math.Abs(r.Destination.Y-target.Y)<.01);
   Gate.Log(new{kind="observe",o.Time,o.Position,known=o.Cells.Count(c=>c.Visibility=="known"),routes=routes.Count,standing=p.ComponentBody.StandingOnValue,velocity=p.ComponentBody.Velocity.ToString(),scan});
   if(chosen!=null){proofAir=chosen.Evidence.Where(c=>(c.Value&1023)==0).Select(c=>(c.X,c.Y,c.Z)).ToHashSet();started=Stopwatch.GetTimestamp();run=actions.Start(new StepTo(target));Gate.Log(new{kind="step_started",chosen,status=run.Result.Status.ToString(),jumpSpeed=p.ComponentLocomotion.JumpSpeed});stage=3;}
   else if(scan++<12){float pitch=scan%3==0?-30:scan%3==2?45:-55;float bearing=scan%3==0?-3:3;run=actions.Start(new FaceTowards(p.ComponentCreatureModel.EyePosition+Vector3.UnitX*bearing,4,pitch));stage=2;}
   else{Done=true;Passed=false;Gate.Log(new{kind="refused",reason="required geometry not visible",window.Cells});}
  }else if(stage==2&&run.Result.Status!=AtomicActionStatus.Running)stage=1;
  else if(stage==3){var b=p.ComponentBody.BoundingBox;var guard=ObservedStepGuard.Check(chosen,new(b.Min.X,b.Min.Y,b.Min.Z),new(b.Max.X,b.Max.Y,b.Max.Z),p.ComponentBody.IsSmoothRiseEnabled,p.ComponentBody.MaxSmoothRiseHeight);bool inside=guard.Inside,knownSupportContactUsed=guard.KnownSupportContactUsed;
   bool expired=chosen.MaximumAge+Stopwatch.GetElapsedTime(started).TotalSeconds>3;if(!inside||expired)actions.Cancel(run);
   Gate.Log(new{kind="step_tick",position=p.ComponentBody.Position.ToString(),velocity=p.ComponentBody.Velocity.ToString(),standing=p.ComponentBody.StandingOnValue,jump=p.ComponentLocomotion.LastJumpOrder,nativeSmoothRise=p.ComponentBody.MaxSmoothRiseHeight,knownSupportContactUsed,inside,expired,status=run.Result.Status.ToString(),failure=run.Result.Failure.ToString()});
   if(run.Result.Status!=AtomicActionStatus.Running){Done=true;Passed=inside&&!expired&&run.Result.Status==AtomicActionStatus.Succeeded&&p.ComponentBody.StandingOnValue.HasValue&&Math.Abs(p.ComponentBody.Position.Y-target.Y)<.1f&&p.ComponentHealth.Health>0;}
  }
 }catch(Exception e){Done=true;Gate.Log(new{kind="scenario_error",error=e.ToString()});}}
 void RunContinuous(){
  double now=p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
  travel??=new PlaySurvivalTravel(p,actions,20,2.4f,"survey",false,now,
   ()=> (JsonSerializer.SerializeToElement(new{}),Array.Empty<PlaySurvivalFood.Food>()),
   ()=>memory.Observe(senses.Observe(senses.SessionId,actor,allDirections:true)),Gate.Log);
  jumped|=p.ComponentLocomotion.LastJumpOrder>0;
  travel.Update(now);
  if(travel.Status is not {} status)return;
  var end=p.ComponentBody.Position;
  Passed=status=="succeeded"&&jumped&&end.X>target.X+.35f&&Math.Abs(end.Y-target.Y)<.1f
    &&p.ComponentBody.StandingOnValue.HasValue&&p.ComponentHealth.Health>0f;
  Done=true;
  Gate.Log(new{kind="continuous_step_result",Passed,status,travel.Reason,travel.Legs,jumped,end=end.ToString()});
 }
 public void Dispose(){DisposeSurvivalGoal();travel?.Stop();earthShelter?.Stop();production?.Stop();if(mode is "production-project" or "production-gear-while-prey-hidden" or "production-resume-gear")p.Project.FindSubsystem<SubsystemPickables>(true).PickableAdded-=MoveFirstWoodDrop;if(run?.Result.Status==AtomicActionStatus.Running)actions.Cancel(run);p.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(this);interest.Release();backend.Dispose();p.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(senses);senses.Dispose();}
}
