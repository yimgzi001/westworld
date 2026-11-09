using Engine;using Game;using Game.Westworld;using Westworld.Runtime.Core;using Westworld.Scapi.Adapter;using System.Diagnostics;using System.Text.Json;
static class Gate {
 static readonly string Root=AppContext.BaseDirectory;static readonly Stopwatch Wall=Stopwatch.StartNew();static string mode;static bool created,done,passed;static Runner runner;
 public static void Log(object o){var s=JsonSerializer.Serialize(o);Console.WriteLine(s);File.AppendAllText(Path.Combine(Root,"step.jsonl"),s+"\n");}
 static int Main(string[] args){if(args.Length!=1||args[0] is not("up" or "down")||!Root.Contains("/work/")||Directory.Exists(Path.Combine(Root,"doc")))return 2;mode=args[0];Log(new{kind="start",mode,fixture=true,models=0});Window.Created+=()=>Window.Frame+=Frame;try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;}
 static void Frame(){if(done)return;try{
  if(Wall.Elapsed.TotalSeconds>60){Finish(false,"timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;var w=WorldsManager.CreateWorld(new WorldSettings{Name="Observed step fixture",Seed="actors-cloud-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}
  if(GameManager.Project is null)return;var u=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);if(u.UpdateFailureCount!=0)throw new Exception(u.LastUpdateFailure);var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
  if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(GameManager.Project){Name="StepActor",IsExternalActor=true});ScreensManager.SwitchScreen("Game");}
  if(runner==null&&players.ComponentPlayers.Count==1&&players.PlayersData[0].IsReadyForPlaying){runner=new Runner(players.ComponentPlayers[0],mode=="up");u.AddUpdateable(runner);}
  if(runner?.Done==true)Finish(runner.Passed,"native step finished");
 }catch(Exception e){Log(new{kind="error",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool ok,string reason){done=true;passed=ok;Log(new{kind="result",passed,reason,seconds=Wall.Elapsed.TotalSeconds,updateFailures=GameManager.Project?.FindSubsystem<SubsystemUpdate>(true).UpdateFailureCount});runner?.Dispose();Window.Close();}
}
sealed class Runner:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-14;public bool Done,Passed;readonly ComponentPlayer p;readonly bool up;readonly NativeActorSession senses;readonly LocalObservationMemory memory;readonly AtomicActions actions;readonly Guid actor=Guid.NewGuid();readonly ScapiInterestBackend backend;readonly InterestCoordinator interest;readonly SubsystemTerrain terrain;
 double elapsed;int stage,scan;AtomicActionRun run;ObservedStepRoute chosen;long started;Vector3 target;HashSet<(int,int,int)> proofAir;
 public Runner(ComponentPlayer p,bool up){this.p=p;this.up=up;terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);senses=new(p.Project,Guid.NewGuid(),false,3);senses.Bind(actor,p);p.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);memory=new(senses.SessionId,actor);actions=new(p.Entity);backend=new(terrain,p.Project.FindSubsystem<SubsystemSpawn>(true));interest=new(senses.Registry,backend,new(16,32));interest.Sync();terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);}
 public void Update(float dt){if(Done)return;elapsed+=dt;interest.Sync();try{
  if(stage==0&&elapsed>3){var pos=p.ComponentBody.Position;int x=Terrain.ToCell(pos.X),y=Terrain.ToCell(pos.Y),z=Terrain.ToCell(pos.Z);int dy=up?1:-1;
   for(int a=x-4;a<=x+4;a++)for(int b=z-4;b<=z+4;b++)for(int c=y-3;c<=y+5;c++)terrain.ChangeCell(a,c,b,(Math.Abs(a-x)==4||Math.Abs(b-z)==4)&&c<=y+3?3:c<=(a>=x+1?y+dy-1:y-1)?3:0);
   p.ComponentBody.Position=new(x+(up?.5f:.74f),y,z+.5f);p.ComponentBody.Velocity=Vector3.Zero;target=new(x+1.5f,y+dy,z+.5f);Gate.Log(new{kind="fixture",up,origin=p.ComponentBody.Position.ToString(),target=target.ToString(),teleportOnlyForSetup=true,perimeterWaterBarrier=true});stage=1;elapsed=0;
  }else if(stage==1&&elapsed>.7){var o=senses.Observe(senses.SessionId,actor);var window=memory.Observe(o);var routes=ObservedStepRoutes.Project(window);chosen=routes.FirstOrDefault(r=>Math.Abs(r.Destination.X-target.X)<.01&&Math.Abs(r.Destination.Z-target.Z)<.01&&Math.Abs(r.Destination.Y-target.Y)<.01);
   Gate.Log(new{kind="observe",o.Time,o.Position,known=o.Cells.Count(c=>c.Visibility=="known"),routes=routes.Count,standing=p.ComponentBody.StandingOnValue,velocity=p.ComponentBody.Velocity.ToString(),scan});
   if(chosen!=null){proofAir=chosen.Evidence.Where(c=>(c.Value&1023)==0).Select(c=>(c.X,c.Y,c.Z)).ToHashSet();started=Stopwatch.GetTimestamp();run=actions.Start(new StepTo(target));Gate.Log(new{kind="step_started",chosen,status=run.Result.Status.ToString(),jumpSpeed=p.ComponentLocomotion.JumpSpeed});stage=3;}
   else if(scan++<12){float pitch=scan%3==0?-30:scan%3==2?45:-55;float bearing=scan%3==0?-3:3;run=actions.Start(new FaceTowards(p.ComponentCreatureModel.EyePosition+Vector3.UnitX*bearing,4,pitch));stage=2;}
   else{Done=true;Passed=false;Gate.Log(new{kind="refused",reason="required geometry not visible",window.Cells});}
  }else if(stage==2&&run.Result.Status!=AtomicActionStatus.Running)stage=1;
  else if(stage==3){var b=p.ComponentBody.BoundingBox;bool inside=true,knownSupportContactUsed=false;for(int x=(int)Math.Floor(b.Min.X+1e-5);x<=(int)Math.Floor(b.Max.X-1e-5)&&inside;x++)for(int z=(int)Math.Floor(b.Min.Z+1e-5);z<=(int)Math.Floor(b.Max.Z-1e-5)&&inside;z++)for(int y=(int)Math.Floor(b.Min.Y+1e-5);y<=(int)Math.Floor(b.Max.Y-1e-5);y++)if(!proofAir.Contains((x,y,z))){
    bool nativeRiseContact=up&&p.ComponentBody.IsSmoothRiseEnabled&&y==Terrain.ToCell(target.Y)-1&&x==Terrain.ToCell(target.X)&&z==Terrain.ToCell(target.Z)&&b.Min.Y>=target.Y-Math.Min(p.ComponentBody.MaxSmoothRiseHeight,.5f)&&chosen.Evidence.Any(c=>c.X==x&&c.Y==y&&c.Z==z&&(c.Value&1023)==3);
    bool nativeEdgeContact=!up&&y==(int)Math.Round(chosen.Origin.Y)-1&&b.Min.Y>=chosen.Origin.Y-.02&&chosen.Evidence.Any(c=>c.X==x&&c.Y==y&&c.Z==z&&(c.Value&1023)==3);
    if(!nativeRiseContact&&!nativeEdgeContact){inside=false;break;}else knownSupportContactUsed=true;
   }
   bool expired=chosen.MaximumAge+Stopwatch.GetElapsedTime(started).TotalSeconds>3;if(!inside||expired)actions.Cancel(run);
   Gate.Log(new{kind="step_tick",position=p.ComponentBody.Position.ToString(),velocity=p.ComponentBody.Velocity.ToString(),standing=p.ComponentBody.StandingOnValue,jump=p.ComponentLocomotion.LastJumpOrder,nativeSmoothRise=p.ComponentBody.MaxSmoothRiseHeight,knownSupportContactUsed,inside,expired,status=run.Result.Status.ToString(),failure=run.Result.Failure.ToString()});
   if(run.Result.Status!=AtomicActionStatus.Running){Done=true;Passed=inside&&!expired&&run.Result.Status==AtomicActionStatus.Succeeded&&p.ComponentBody.StandingOnValue.HasValue&&Math.Abs(p.ComponentBody.Position.Y-target.Y)<.1f&&p.ComponentHealth.Health>0;}
  }
 }catch(Exception e){Done=true;Gate.Log(new{kind="scenario_error",error=e.ToString()});}}
 public void Dispose(){if(run?.Result.Status==AtomicActionStatus.Running)actions.Cancel(run);p.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(this);interest.Release();backend.Dispose();p.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(senses);senses.Dispose();}
}
