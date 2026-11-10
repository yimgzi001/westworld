using Engine;
using Game;
using Game.Westworld;
using System.Text.Json;
using Westworld.Runtime.Core;

// Explicit artificial lifecycle tests, never a survival policy or model instruction.
internal sealed partial class ToolAgentScenario {
 bool IsDirectFixture=>ToolAgentGate.Mode=="handoff"&&ToolAgentGate.HandoffCase?.StartsWith("direct_",StringComparison.Ordinal)==true;
 bool IsDirectModelFixture=>IsDirectFixture&&ToolAgentGate.HandoffCase=="direct_model";
 AtomicActionRun directDismount;int directPreparation;double directPreparedAt;Pickable directFixtureDrop;
 readonly Dictionary<Point3,int> directOriginalCells=[];
 void SetupDirectHandoffFixture(){directPreparedAt=MonotonicSeconds;}
 bool PrepareDirectHandoffFixture(){
  if(!IsDirectFixture||directPreparation==3)return true;
  if(MonotonicSeconds-directPreparedAt>30){Fail("direct_fixture_preparation_timeout");return false;}
  if(directPreparation==0){if(p.Entity.FindComponent<ComponentRider>(true).m_isAnimating)return false;directDismount=actions.Start(new Dismount());directPreparation=1;return false;}
  if(directPreparation==1){if(actions.IsActionRunning)return false;
   ToolAgentGate.Log(new{kind="direct_fixture_dismount",status=directDismount.Result.Status.ToString(),parented=p.ComponentBody.ParentBody is not null});
   if(directDismount.Result.Status!=AtomicActionStatus.Succeeded||p.ComponentBody.ParentBody is not null){Fail("direct_fixture_dismount_failed");return false;}
   var t=p.Project.FindSubsystem<SubsystemTerrain>(true);var b=p.ComponentBody.Position;int x=Terrain.ToCell(b.X),y=Terrain.ToCell(b.Y),z=Terrain.ToCell(b.Z);
   for(int xx=x-7;xx<=x+7;xx++)for(int zz=z-7;zz<=z+7;zz++){DirectFixtureCell(t,xx,y-1,zz,DirtBlock.Index);for(int yy=y;yy<y+4;yy++)DirectFixtureCell(t,xx,yy,zz,0);}
   p.ComponentBody.Position=new(x+.5f,y,z+.5f);p.ComponentBody.Velocity=Vector3.Zero;p.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);
   origin=previous=p.ComponentBody.Position;path=0;
   if(ToolAgentGate.HandoffCase=="direct_stale_reference")directFixtureDrop=p.Project.FindSubsystem<SubsystemPickables>(true).AddPickable(CookedMeatBlock.Index,1,origin+new Vector3(3.6f,.2f,0),Vector3.Zero,null);
   directPreparation=2;ToolAgentGate.Log(new{kind="direct_fixture_prepared",scenario=ToolAgentGate.HandoffCase,position=NativeLogCoordinates.Position(origin),artificial=true,model=IsDirectModelFixture,inventoryInjection=false,dropFixture=directFixtureDrop is not null});return false;
  }
  var terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);int bx=Terrain.ToCell(origin.X),bz=Terrain.ToCell(origin.Z);
  for(int x=bx-5;x<=bx+5;x++)for(int z=bz-5;z<=bz+5;z++)if(terrain.Terrain.GetChunkAtCell(x,z)?.State!=TerrainChunkState.Valid)return false;
  directPreparation=3;ToolAgentGate.Log(new{kind="direct_fixture_ready",scenario=ToolAgentGate.HandoffCase,monotonicSeconds=MonotonicSeconds});return true;
 }
 void DirectFixtureCell(SubsystemTerrain t,int x,int y,int z,int value){var cell=new Point3(x,y,z);directOriginalCells.TryAdd(cell,t.Terrain.GetCellValue(x,y,z));t.ChangeCell(x,y,z,value);}
 void TickDirectHandoffFixture(){
  if(prefetchLease is null||fixtureInjected||MonotonicSeconds-prefetchStarted<.4)return;
  string name=ToolAgentGate.HandoffCase;if(name is not("direct_cancel" or "direct_failure" or "direct_replace"))return;
  fixtureInjected=true;var before=Call("action_status",new{},false);
  if(name=="direct_failure"){
   var b=p.ComponentBody.Position;int x=Terrain.ToCell(b.X)+1,y=Terrain.ToCell(b.Y),z=Terrain.ToCell(b.Z);var t=p.Project.FindSubsystem<SubsystemTerrain>(true);
   for(int zz=z-7;zz<=z+7;zz++)for(int yy=y;yy<=y+2;yy++)DirectFixtureCell(t,x,yy,zz,GraniteBlock.Index);
   ToolAgentGate.Log(new{kind="direct_fixture_injection",scenario=name,type="wall",x,y,z,before=before.Data,monotonicSeconds=MonotonicSeconds});
  }else{
   var canceled=Call("cancel_action",new{},false);ToolReceipt? replacement=null;
   if(name=="direct_replace")replacement=Call("act_Wait",new{duration=.2},false);
   ToolAgentGate.Log(new{kind="direct_fixture_injection",scenario=name,type=name=="direct_replace"?"cancel_then_replace":"cancel",before=before.Data,canceled=canceled.Data,replacement=replacement?.Data,monotonicSeconds=MonotonicSeconds});
  }
 }
 bool EndDirectHandoffFixture(){
  if(IsDirectModelFixture){
   var delta=p.ComponentBody.Position-origin;
   bool endpoint=DirectModelEndpoint.Reached(delta.X,delta.Y,delta.Z,p.ComponentBody.Velocity.Length(),p.ComponentHealth.Health,
    directPreparation==3,waiting is not null||actions.IsActionRunning||navigation.Busy,navigation.OwnerQuarantined,p.GameWidget is not null);
   if(!endpoint)return false;
   // Goal completion does not require a particular tool, queue shape, response or prefetch.
   // RequestStop retires any pending candidate/model request and drains native execution.
   Passed=true;
   ToolAgentGate.Log(new{kind="direct_model_endpoint_result",endpointReached=true,delta=NativeLogCoordinates.Position(delta),prefetchTaken,overlapSeconds,overlapPath,
    precedingKind,actualPrefetchRequiresIndependentValidation=true,artificial=true,naturalSurvival=false,
    note="Endpoint success is independent of model tool choice and async coverage. Independent audit must classify NativeMoveTo/host navigation and actual in-flight motion."});
   RequestStop("model_endpoint_verified");return true;
  }
  if(directPreparation!=3||prefetchLease is null||inference is not null||waiting is not null||steps.Count>0||actions.IsActionRunning)return false;
  bool positive=ToolAgentGate.HandoffCase is "direct_early" or "direct_late";
  if(positive&&prefetchTaken<1)return false;if(!positive&&handoff.Occupied)return false;
  bool retired=Directory.Exists(Path.Combine(ToolAgentGate.Root,"control"))&&Directory.EnumerateDirectories(Path.Combine(ToolAgentGate.Root,"control"),"*-"+prefetchLease.RequestId.ToString("N")).Any(d=>File.Exists(Path.Combine(d,"response.json")));
  if(!positive&&!prefetchAccepted&&!retired)return true; // Wait for controlled late response; never open a new model request.
  var status=Call("action_status",new{},false);bool safe=!navigation.Busy&&!navigation.OwnerQuarantined&&p.ComponentBody.Velocity.Length()<.1f&&p.GameWidget is null;
  if(!safe)return true;
  bool terminal=status.State=="returned"&&Status(status.Data) is "succeeded" or "cancelled" or "failed";
  bool expected=positive?prefetchTaken==1&&overlapSeconds>0&&overlapPath>.01&&Status(status.Data)=="succeeded":prefetchTaken==0&&terminal&&invalidatedReason is not null;
  Passed=safe&&expected;
  ToolAgentGate.Log(new{kind="direct_fixture_result",scenario=ToolAgentGate.HandoffCase,Passed,safe,expected,fixtureInjected,retiredLateFile=retired,prefetchTaken,prefetchAccepted,invalidatedReason,overlapSeconds,overlapPath,actualFinal=status.Data,path,netDisplacement=(p.ComponentBody.Position-origin).XZ.Length(),note="Artificial lifecycle fixture; independent validator must verify the specific failure/cancel/replacement mechanism and dispatch identities."});
  RequestStop(Passed?"direct_prefetch_fixture_verified":"direct_prefetch_fixture_failed");return true;
 }
 void FinishDirectHandoffFixture(){if(!IsDirectFixture)return;var t=p.Project.FindSubsystem<SubsystemTerrain>(true);foreach(var c in directOriginalCells)t.ChangeCell(c.Key.X,c.Key.Y,c.Key.Z,c.Value);if(directFixtureDrop is not null&&!directFixtureDrop.ToRemove)directFixtureDrop.ToRemove=true;}
}
