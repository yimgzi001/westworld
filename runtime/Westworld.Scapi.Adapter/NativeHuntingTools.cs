using System.Collections.ObjectModel;
using System.Text.Json;
using Game.Westworld;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public sealed record HuntingVisibleTarget(string Reference,string Kind,string? Name,Position Position,double Distance,int? Value,int? Count);
public sealed record HuntingVisibleProfile(Guid SessionId,Guid ActorId,Guid ObservationId,double CapturedAt,double NativeGameTime,IReadOnlyList<HuntingVisibleTarget> Targets);
public sealed record HuntingToolContract(ActorToolDefinition NativeTool,string EvidenceRule,double? SuggestedMaxSeconds);
public sealed record HuntingTargetCheck(bool Accepted,string Reason,HuntingVisibleTarget? Target=null);
public sealed record HuntingReceipt(string Status,string Effect,int AffectedCount,string Death,string Failure);

/// <summary>
/// Stateless profile/receipt helpers for a host-owned NativeToolSession. This is NOT an
/// executor, authorization gateway, reference store, pursuit loop or death detector.
/// Inputs must come from the bound actor's actual ActorTools; never accept profiles or
/// native receipts supplied by the model. Do not make another AtomicActions owner.
/// </summary>
public static class NativeHuntingTools {
 static readonly IReadOnlyDictionary<string,(string Rule,double? Seconds)> rules =
  new ReadOnlyDictionary<string,(string,double?)>(new Dictionary<string,(string,double?)>{
   ["observe"]=("Actual eye cone/occlusion. Each capture replaces the live reference table. Missing target does not prove death; newly visible loot does not prove ownership.",null),
   ["act_FaceTowards"]=("destination must be a currently visible entity/drop reference. Aim only; does not prove approach, damage or death.",2),
   ["act_MoveTo"]=("destination is resolved to a position at start, NOT a live tracking target. Use one short approach, then observe again; do not reuse unseen positions to chase.",2),
   ["act_MeleeAttack"]=("target must be a currently visible entity reference. Native ray/reach/cooldown/tool checks apply. affectedCount=0 is no proven damage; positive damage does not prove death.",null),
   ["act_Wait"]=("Bounded waiting only. Completion does not prove a corpse exists or loot spawned. Observe actual drops afterwards.",10),
   ["act_PickUp"]=("target must be a currently visible dropped-stack reference. Count/reach/value checks are native. Successful pickup is not proof this stack came from the attacked animal.",null),
   ["action_status"]=("Poll the existing direct action; accepted/running is not completion.",null),
   ["cancel_action"]=("Cancel through the existing owner. Cancellation is not a successful hunt or proof of physical settling.",null)
  });

 /// <summary>Preserve actual native names and JSON parameter schemas. Does not invent tool availability.</summary>
 public static IReadOnlyList<HuntingToolContract> Describe(IEnumerable<ActorToolDefinition> available) =>
  Array.AsReadOnly(available.Where(t=>rules.ContainsKey(t.Name)).Select(t=>{
   var r=rules[t.Name];return new HuntingToolContract(t with {Parameters=t.Parameters.Clone()},r.Rule,r.Seconds);
  }).ToArray());

 /// <summary>Project ONE already-captured native observation; never call observe twice for this projection.</summary>
 public static HuntingVisibleProfile ProjectObservation(Guid session,Guid actor,Guid observation,double capturedAt,JsonElement nativeObservation){
  if(session==Guid.Empty||actor==Guid.Empty||observation==Guid.Empty||!double.IsFinite(capturedAt)||capturedAt<0)throw new ArgumentException("Invalid host scope/time");
  if(nativeObservation.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Native observation object required");
  double gameTime=Finite(nativeObservation.GetProperty("gameTime"));if(gameTime<0)throw new ArgumentException("Invalid native time");
  var targets=new List<HuntingVisibleTarget>();var refs=new HashSet<string>(StringComparer.Ordinal);
  void Read(string array,string kind){var list=nativeObservation.GetProperty(array);if(list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()>100)throw new ArgumentException("Invalid native visible array");
   foreach(var item in list.EnumerateArray()){
    string reference=item.GetProperty("reference").GetString()??"";
    if(reference.Length is <1 or >256||!refs.Add(reference))throw new ArgumentException("Invalid or duplicate native reference");
    var p=item.GetProperty("position");var position=new Position(Finite(p.GetProperty("x")),Finite(p.GetProperty("y")),Finite(p.GetProperty("z")));
    double distance=Finite(item.GetProperty("distance"));if(distance<0||distance>16)throw new ArgumentException("Visible target outside supported semantic radius");
    int? value=null,count=null;string? name=null;
    if(kind=="drop"){value=item.GetProperty("value").GetInt32();count=item.GetProperty("count").GetInt32();if(count<=0)throw new ArgumentException("Empty visible drop");}
    else if(item.TryGetProperty("name",out var n)&&n.ValueKind!=JsonValueKind.Null)name=n.GetString();
    targets.Add(new(reference,kind,name,position,distance,value,count));
   }
  }
  Read("entities","entity");Read("pickables","drop");
  return new(session,actor,observation,capturedAt,gameTime,Array.AsReadOnly(targets.ToArray()));
 }
 static double Finite(JsonElement value){double n=value.GetDouble();if(!double.IsFinite(n))throw new ArgumentException("Non-finite native observation");return n;}

 /// <summary>
 /// Checks a reference against the host's LATEST profile, not model-provided evidence.
 /// The session still owns authentication, freshness admission, game thread, request IDs,
 /// actor availability, action deadlines and cancellation. Native tools recheck visibility.
 /// </summary>
 public static HuntingTargetCheck CheckTarget(HuntingVisibleProfile latest,Guid session,Guid actor,Guid observation,double now,string tool,string reference){
  if(latest.SessionId!=session||latest.ActorId!=actor||latest.ObservationId!=observation)return new(false,"wrong_or_superseded_observation");
  if(!double.IsFinite(now)||now<latest.CapturedAt||now-latest.CapturedAt>3)return new(false,"stale_observation");
  string? required=tool switch{"act_MeleeAttack"=>"entity","act_PickUp"=>"drop","act_MoveTo" or "act_FaceTowards"=>"any",_=>null};
  if(required is null)return new(false,"not_a_targeted_hunting_tool");
  var target=latest.Targets.SingleOrDefault(t=>string.Equals(t.Reference,reference,StringComparison.Ordinal));
  if(target is null)return new(false,"not_currently_observed");
  if(required!="any"&&required!=target.Kind)return new(false,"wrong_reference_kind");
  return new(true,"current_native_reference",target);
 }

 /// <summary>
 /// Classifies actual native action_status data (not the outer {ok,data} envelope).
 /// There is intentionally no Killed label: native affectedCount alone cannot prove it.
 /// </summary>
 public static HuntingReceipt ClassifyAction(string tool,JsonElement action){
  if(action.ValueKind!=JsonValueKind.Object)return new("unknown","unknown",0,"unknown","missing_native_action");
  if(!action.TryGetProperty("status",out var s)||s.ValueKind!=JsonValueKind.String)return new("unknown","unknown",0,"unknown","malformed_native_status");
  string status=s.GetString()??"unknown";
  if(!tool.StartsWith("act_",StringComparison.Ordinal)||!action.TryGetProperty("action",out var actual)||actual.ValueKind!=JsonValueKind.String||actual.GetString()!=tool[4..])return new("unknown","unknown",0,"unknown","action_identity_mismatch");
  if(!action.TryGetProperty("affectedCount",out var a)||!a.TryGetInt32(out var affected)||affected<0)return new("unknown","unknown",0,"unknown","malformed_affected_count");
  string failure=action.TryGetProperty("failure",out var f)&&f.ValueKind==JsonValueKind.String?f.GetString()??"none":"none";
  string effect=status switch{
   "running"=>"pending",
   "cancelled"=>"cancelled",
   "failed" or "rejected"=>"not_completed",
   "succeeded"=>tool switch{
    "act_MeleeAttack"=>affected>0?"hit_death_unconfirmed":"no_damage_proven",
    "act_PickUp"=>affected>0?"picked_up_source_unconfirmed":"no_pickup_proven",
    "act_MoveTo"=>"reached_snapshot_position",
    "act_FaceTowards"=>"aimed",
    "act_Wait"=>"wait_completed_loot_unconfirmed",
    _=>"native_action_completed"},
   _=>"unknown"
  };
  return new(status,effect,affected,"unknown",failure);
 }

 public static IReadOnlyList<HuntingVisibleTarget> NewlyObservedDrops(HuntingVisibleProfile before,HuntingVisibleProfile after){
  if(before.SessionId!=after.SessionId||before.ActorId!=after.ActorId||after.CapturedAt<before.CapturedAt)throw new ArgumentException("Incompatible observation scope/time");
  var old=before.Targets.Where(t=>t.Kind=="drop").Select(t=>t.Reference).ToHashSet(StringComparer.Ordinal);
  // A reference can disappear/reappear with a new token: NEWLY OBSERVED, not newly spawned.
  return Array.AsReadOnly(after.Targets.Where(t=>t.Kind=="drop"&&!old.Contains(t.Reference)).ToArray());
 }
}
