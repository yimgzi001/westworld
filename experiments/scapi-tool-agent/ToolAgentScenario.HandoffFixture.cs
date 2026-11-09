using Engine;
using Game;
using Westworld.Scapi.Adapter;
// Explicit deterministic lifecycle fixture only. Never used for craft/cook/natural modes.
internal sealed partial class ToolAgentScenario {
 bool fixtureInjected;int fixtureNavigationStarts;bool fixtureFirstArrived;
 void SetupHandoffFixture(){if(ToolAgentGate.Mode!="handoff")return;var t=p.Project.FindSubsystem<SubsystemTerrain>(true);var b=p.ComponentBody.Position;int x=Terrain.ToCell(b.X),y=Terrain.ToCell(b.Y),z=Terrain.ToCell(b.Z);for(int xx=x-7;xx<=x+7;xx++)for(int zz=z-7;zz<=z+7;zz++){t.ChangeCell(xx,y-1,zz,DirtBlock.Index);for(int yy=y;yy<y+4;yy++)t.ChangeCell(xx,yy,zz,0);}p.ComponentBody.Position=new(x+.5f,y,z+.5f);p.ComponentBody.Velocity=Vector3.Zero;p.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);ToolAgentGate.Log(new{kind="handoff_fixture",scenario=ToolAgentGate.HandoffCase,deterministicFileDriver=ToolAgentGate.HandoffCase!="model",modelDriver=ToolAgentGate.HandoffCase=="model",noInventoryInjection=true});}
 void TickHandoffFixture(){if(ToolAgentGate.Mode!="handoff"||prefetchLease is null||fixtureInjected||MonotonicSeconds-prefetchStarted<.1)return;
  if(ToolAgentGate.HandoffCase=="cancel"){fixtureInjected=true;InvalidatePrefetch("fixture_cancel");Call("host_cancel_navigation",new{request_id=navigation.Snapshot.RequestId});ToolAgentGate.Log(new{kind="fixture_cancel",monotonicSeconds=MonotonicSeconds});}
  else if(ToolAgentGate.HandoffCase=="failure"){fixtureInjected=true;var b=p.ComponentBody.Position;int x=Terrain.ToCell(b.X)+1,y=Terrain.ToCell(b.Y),z=Terrain.ToCell(b.Z);var t=p.Project.FindSubsystem<SubsystemTerrain>(true);for(int zz=z-2;zz<=z+2;zz++){t.ChangeCell(x,y,zz,GraniteBlock.Index);t.ChangeCell(x,y+1,zz,GraniteBlock.Index);}ToolAgentGate.Log(new{kind="fixture_wall",x,y,z,monotonicSeconds=MonotonicSeconds});}
 }
 bool EndHandoffFixture(){if(ToolAgentGate.Mode=="handoff"&&ToolAgentGate.HandoffCase=="model"){
  if(navigation.Busy||inference is not null||waiting is not null||steps.Count>0||actions.IsActionRunning)return false;
  var delta=p.ComponentBody.Position-origin;if(delta.X<3.2f||delta.X>4.8f||Math.Abs(delta.Z)>.5f||p.ComponentBody.Velocity.Length()>=.1f)return false;
  Passed=!navigation.OwnerQuarantined&&p.GameWidget is null&&p.ComponentHealth.Health>=.25f;Done=true;Reason=Passed?"model_observed_endpoint_verified":"model_endpoint_guard_failed";
  ToolAgentGate.Log(new{kind="model_navigation_result",Passed,delta=new[]{delta.X,delta.Y,delta.Z},fixtureNavigationStarts,prefetchTaken,overlapSeconds,overlapPath,actualPrefetchOccurred=prefetchTaken>0&&overlapPath>.01,note="Endpoint criterion, inspect actual tool receipts for chosen segment sizes; no forced model steps."});return true;
 }if(ToolAgentGate.Mode!="handoff"||prefetchLease is null||navigation.Busy||inference is not null||waiting is not null||steps.Count>0)return false;
  bool positive=ToolAgentGate.HandoffCase is "early" or "late";if(positive&&prefetchTaken<1)return false;
  if(!positive&&handoff.Occupied)return false;
  bool retiredLateFile=System.IO.Directory.Exists(System.IO.Path.Combine(ToolAgentGate.Root,"control"))&&System.IO.Directory.EnumerateDirectories(System.IO.Path.Combine(ToolAgentGate.Root,"control"),"*-"+prefetchLease.RequestId.ToString("N")).Any(d=>System.IO.File.Exists(System.IO.Path.Combine(d,"response.json")));
  if(!positive&&!prefetchAccepted&&!retiredLateFile)return true; // Hold only this controlled negative fixture; do not request a new decision before late injection.
  bool safe=!navigation.OwnerQuarantined&&!actions.IsActionRunning&&p.ComponentBody.Velocity.Length()<.1&&p.GameWidget is null;
  bool expected=positive?fixtureNavigationStarts==2&&navigation.Snapshot.Status==BoundedNavigationStatus.Arrived&&prefetchTaken==1&&overlapSeconds>0&&overlapPath>.01:prefetchTaken==0&&fixtureNavigationStarts==1&&(ToolAgentGate.HandoffCase=="expired"?invalidatedReason=="decision_deadline":ToolAgentGate.HandoffCase=="cancel"?navigation.Snapshot.Status==BoundedNavigationStatus.Cancelled:navigation.Snapshot.Status==BoundedNavigationStatus.Failed);
  Passed=safe&&expected;Done=true;Reason=Passed?"controlled_prefetch_lifecycle_verified":"controlled_prefetch_lifecycle_failed";ToolAgentGate.Log(new{kind="handoff_fixture_result",scenario=ToolAgentGate.HandoffCase,Passed,fixtureNavigationStarts,prefetchTaken,fixtureFirstArrived,invalidatedReason,retiredLateFile,prefetchAccepted,overlapSeconds,overlapPath});return true;
 }
}
