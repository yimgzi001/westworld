using Engine;
using Game;
using System.Text.Json;
using Westworld.Scapi.Adapter;
// Explicit deterministic lifecycle fixture only. Never used for craft/cook/natural modes.
internal sealed partial class ToolAgentScenario {
 bool fixtureInjected;int fixtureNavigationStarts;bool fixtureFirstArrived;
 void SetupHandoffFixture(){if(ToolAgentGate.Mode!="handoff")return;var t=p.Project.FindSubsystem<SubsystemTerrain>(true);var b=p.ComponentBody.Position;int x=Terrain.ToCell(b.X),y=Terrain.ToCell(b.Y),z=Terrain.ToCell(b.Z);for(int xx=x-7;xx<=x+7;xx++)for(int zz=z-7;zz<=z+7;zz++){t.ChangeCell(xx,y-1,zz,DirtBlock.Index);for(int yy=y;yy<y+4;yy++)t.ChangeCell(xx,yy,zz,0);}p.ComponentBody.Position=new(x+.5f,y,z+.5f);p.ComponentBody.Velocity=Vector3.Zero;p.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);ToolAgentGate.Log(new{kind="handoff_fixture",scenario=ToolAgentGate.HandoffCase,deterministicFileDriver=ToolAgentGate.HandoffCase!="model",modelDriver=ToolAgentGate.HandoffCase=="model",noInventoryInjection=true});}
 void TickHandoffFixture(){if(IsRouteFixture){TickRouteFixture();return;}if(ToolAgentGate.Mode!="handoff"||prefetchLease is null||fixtureInjected||MonotonicSeconds-prefetchStarted<.1)return;
  if(ToolAgentGate.HandoffCase=="cancel"){fixtureInjected=true;InvalidatePrefetch("fixture_cancel");Call("host_cancel_navigation",new{request_id=navigation.Snapshot.RequestId});ToolAgentGate.Log(new{kind="fixture_cancel",monotonicSeconds=MonotonicSeconds});}
  else if(ToolAgentGate.HandoffCase=="failure"){fixtureInjected=true;var b=p.ComponentBody.Position;int x=Terrain.ToCell(b.X)+1,y=Terrain.ToCell(b.Y),z=Terrain.ToCell(b.Z);var t=p.Project.FindSubsystem<SubsystemTerrain>(true);for(int zz=z-2;zz<=z+2;zz++){t.ChangeCell(x,y,zz,GraniteBlock.Index);t.ChangeCell(x,y+1,zz,GraniteBlock.Index);}ToolAgentGate.Log(new{kind="fixture_wall",x,y,z,monotonicSeconds=MonotonicSeconds});}
 }
 bool EndHandoffFixture(){if(IsRouteFixture)return EndRouteFixture();if(ToolAgentGate.Mode=="handoff"&&ToolAgentGate.HandoffCase=="model"){
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
 bool IsRouteFixture=>ToolAgentGate.Mode=="handoff"&&ToolAgentGate.HandoffCase?.StartsWith("route_",StringComparison.Ordinal)==true;
 bool routeExercised,routeInjected,routeLost;Guid fixtureRouteId,fixtureLocalId,fixtureOverallId;Vector3 fixtureRouteOrigin;
 double routeStartedAt,routeInjectedAt;bool routeAirborne;float routeLowestY;string routeInjectionFailure;
 void AuditNavigation(object value){
  ToolAgentGate.Log(value);if(!IsRouteFixture)return;
  var e=JsonSerializer.SerializeToElement(value);
  if(e.GetProperty("kind").GetString()!="observed_flat_route_started"||routeExercised)return;
  if(e.GetProperty("nativeStatus").GetString()!="Running"||!actions.IsActionRunning||navigationSkill.Status!=LocalNavigationStatus.Moving)return;
  fixtureLocalId=e.GetProperty("RequestId").GetGuid();fixtureRouteId=e.GetProperty("routeId").GetGuid();fixtureOverallId=navigation.Snapshot.RequestId;
  if(fixtureLocalId!=navigationSkill.RequestId)return;
  routeExercised=true;fixtureRouteOrigin=p.ComponentBody.Position;routeLowestY=fixtureRouteOrigin.Y;routeStartedAt=MonotonicSeconds;
  ToolAgentGate.Log(new{kind="route_fixture_bound",scenario=ToolAgentGate.HandoffCase,fixtureRouteId,fixtureLocalId,fixtureOverallId,monotonicSeconds=routeStartedAt,gameTime=p.Project.FindSubsystem<SubsystemTime>(true).GameTime,position=new[]{fixtureRouteOrigin.X,fixtureRouteOrigin.Y,fixtureRouteOrigin.Z}});
  // Trusted callback runs immediately after native Start returned Running, before its
  // first scheduled Advance. Exact overall ID stop still goes through the gateway.
  if(ToolAgentGate.HandoffCase=="route_cancel_before")InjectRouteCancel("before_first_advance");
 }
 void InjectRouteCancel(string phase){
  routeInjected=true;routeInjectedAt=MonotonicSeconds;InvalidatePrefetch("route_fixture_cancel");steps.Clear();
  var before=p.ComponentBody.Position;var receipt=Call("host_cancel_navigation",new{request_id=fixtureOverallId});
  ToolAgentGate.Log(new{kind="route_fixture_cancel",phase,fixtureRouteId,fixtureLocalId,fixtureOverallId,receiptState=receipt.State,monotonicSeconds=routeInjectedAt,gameTime=p.Project.FindSubsystem<SubsystemTime>(true).GameTime,immediatePositionDelta=(p.ComponentBody.Position-before).Length(),speed=p.ComponentBody.Velocity.Length(),localStatus=navigationSkill.Status.ToString(),nativeStillRunning=actions.IsActionRunning});
  if(receipt.State!="returned")routeInjectionFailure="cancel_rejected";
 }
 void TickRouteFixture(){
  if(!routeExercised)return;
  routeAirborne|=!p.ComponentBody.StandingOnValue.HasValue;routeLowestY=Math.Min(routeLowestY,p.ComponentBody.Position.Y);
  if(navigation.Busy&&navigationSkill.RequestId!=fixtureLocalId){routeLost=true;return;}
  if(routeInjected||routeInjectionFailure is not null)return;
  if(navigationSkill.Status!=LocalNavigationStatus.Moving||!actions.IsActionRunning)return;
  if((p.ComponentBody.Position-fixtureRouteOrigin).XZ.Length()<=.1f)return;
  if(ToolAgentGate.HandoffCase=="route_cancel_after"){InjectRouteCancel("after_actual_displacement");return;}
  var b=p.ComponentBody;int x=Terrain.ToCell(b.Position.X)+1,y=Terrain.ToCell(b.Position.Y),z=Terrain.ToCell(b.Position.Z);
  // This isolated fixture's authorized route is +X. Never place a wall inside the
  // actual body. Remove only future support, never teleport/drop the actor itself.
  if(x<b.BoundingBox.Max.X+1e-5f){routeInjectionFailure="no_nonoverlapping_injection_cell";return;}
  var terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);routeInjected=true;routeInjectedAt=MonotonicSeconds;
  if(ToolAgentGate.HandoffCase=="route_wall") {terrain.ChangeCell(x,y,z,GraniteBlock.Index);terrain.ChangeCell(x,y+1,z,GraniteBlock.Index);}
  else if(ToolAgentGate.HandoffCase=="route_support")terrain.ChangeCell(x,y-1,z,0);
  InvalidatePrefetch("route_fixture_world_changed");steps.Clear();
  ToolAgentGate.Log(new{kind="route_fixture_world_changed",scenario=ToolAgentGate.HandoffCase,fixtureRouteId,fixtureLocalId,fixtureOverallId,cell=new[]{x,ToolAgentGate.HandoffCase=="route_support"?y-1:y,z},monotonicSeconds=routeInjectedAt,gameTime=p.Project.FindSubsystem<SubsystemTime>(true).GameTime,bodyMin=new[]{b.BoundingBox.Min.X,b.BoundingBox.Min.Y,b.BoundingBox.Min.Z},bodyMax=new[]{b.BoundingBox.Max.X,b.BoundingBox.Max.Y,b.BoundingBox.Max.Z}});
 }
 bool EndRouteFixture(){
  if(navigation.Busy||waiting is not null)return false;
  if(!routeExercised&&fixtureNavigationStarts==0)return false;
  InvalidatePrefetch("route_fixture_finished");steps.Clear();if(inference is not null)return true;
  bool cancel=ToolAgentGate.HandoffCase is "route_cancel_before" or "route_cancel_after";
  bool safe=!navigation.OwnerQuarantined&&!actions.IsActionRunning&&p.ComponentBody.Velocity.Length()<.1f&&p.GameWidget is null;
  string nativeReason=navigationSkill.Reason;
  // Expiry is a valid safety stop, but is NOT evidence that collision/support guard ran.
  bool mechanism=cancel?navigation.Snapshot.Status==BoundedNavigationStatus.Cancelled:
   ToolAgentGate.HandoffCase=="route_wall"?nativeReason=="native_Blocked":
   routeAirborne&&nativeReason=="native_MovementInterrupted";
  double displacement=(p.ComponentBody.Position-fixtureRouteOrigin).XZ.Length();
  bool cancelMotionEvidence=ToolAgentGate.HandoffCase!="route_cancel_before"||displacement<.05;
  Passed=routeExercised&&routeInjected&&!routeLost&&routeInjectionFailure is null&&safe&&mechanism&&cancelMotionEvidence&&prefetchTaken==0;
  Done=true;Reason=!routeExercised?"route_not_exercised":Passed?"route_specific_negative_verified":"route_negative_incomplete_or_failed";
  ToolAgentGate.Log(new{kind="route_fixture_result",scenario=ToolAgentGate.HandoffCase,Passed,routeExercised,routeInjected,routeLost,routeInjectionFailure,fixtureRouteId,fixtureLocalId,fixtureOverallId,nativeReason,cancelMotionEvidence,overallStatus=navigation.Snapshot.Status.ToString(),safe,mechanism,routeAirborne,verticalDrop=fixtureRouteOrigin.Y-routeLowestY,displacementFromRouteStart=(p.ComponentBody.Position-fixtureRouteOrigin).XZ.Length(),routeStartedAt,routeInjectedAt,prefetchTaken,note="TTL/envelope stops are preserved but do not count as collision/support mechanism validation."});return true;
 }

}
