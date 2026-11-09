using System.Text.Json;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

// Host-only port: model code never receives this object. NativeToolSession only
// registers its own concrete, identity-checked BoundedLocalNavigation wrapper.
public interface IHostNavigationControl {
 bool Matches(object player,object actionOwner);
 bool Busy {get;}
 bool OwnerQuarantined {get;}
 Guid RequestId {get;}
 JsonElement ReadStatus();
 void Start(Guid request,double bearing,double distance,double maxSeconds);
 void Cancel();
}
public sealed class HostNavigationToolRouter {
 readonly IHostNavigationControl navigation;readonly int owner=Environment.CurrentManagedThreadId;
 public HostNavigationToolRouter(object player,object actionOwner,IHostNavigationControl navigation){
  ArgumentNullException.ThrowIfNull(player);ArgumentNullException.ThrowIfNull(actionOwner);ArgumentNullException.ThrowIfNull(navigation);
  if(!navigation.Matches(player,actionOwner))throw new ArgumentException("Navigation native binding mismatch");this.navigation=navigation;
 }
 public static bool IsHostTool(string name)=>name is "host_navigate_local" or "host_navigation_status" or "host_cancel_navigation";
 public bool Busy {get{Owner();return navigation.Busy;}}
 public bool OwnerQuarantined {get{Owner();return navigation.OwnerQuarantined;}}
 void Owner(){if(Environment.CurrentManagedThreadId!=owner)throw new InvalidOperationException("Game thread required");}
 public static ToolBackendResult Rejected(string reason)=>new(true,JsonSerializer.SerializeToElement(new{status="rejected",reason}));
 public ToolBackendResult Invoke(string tool,JsonElement args){
  Owner();if(args.ValueKind!=JsonValueKind.Object)return Rejected("arguments_object_required");
  var fields=new HashSet<string>(StringComparer.Ordinal);foreach(var p in args.EnumerateObject())if(!fields.Add(p.Name))return Rejected("duplicate_argument");
  if(tool=="host_navigation_status")return fields.Count==0?new(true,navigation.ReadStatus()):Rejected("unknown_argument");
  if(tool=="host_cancel_navigation"){
   if(fields.Count!=1||!args.TryGetProperty("request_id",out var id)||id.ValueKind!=JsonValueKind.String||!id.TryGetGuid(out var request)||request==Guid.Empty)return Rejected("navigation_request_id_required");
   if(request!=navigation.RequestId)return Rejected("navigation_request_mismatch");
   if(navigation.Busy)navigation.Cancel();return new(true,navigation.ReadStatus());
  }
  if(tool!="host_navigate_local")return Rejected("unknown_host_tool");
  if(fields.Any(f=>f is not("bearing" or "distance" or "max_seconds"))||!args.TryGetProperty("bearing",out var b)||!args.TryGetProperty("distance",out var d)
    ||b.ValueKind!=JsonValueKind.Number||d.ValueKind!=JsonValueKind.Number||!b.TryGetDouble(out double bearing)||!d.TryGetDouble(out double distance))return Rejected("invalid_navigation_arguments");
  double seconds=12;
  if(args.TryGetProperty("max_seconds",out var t)&&(t.ValueKind!=JsonValueKind.Number||!t.TryGetDouble(out seconds)))return Rejected("invalid_navigation_arguments");
  if(!double.IsFinite(bearing)||bearing<0||bearing>=360||!double.IsFinite(distance)||distance<1||distance>4||!double.IsFinite(seconds)||seconds<1||seconds>12)return Rejected("navigation_arguments_out_of_range");
  if(navigation.OwnerQuarantined)return Rejected("navigation_owner_quarantined");
  if(navigation.Busy)return Rejected("navigation_busy");
  // Only the first admitted ledger call reaches this allocation and native Start.
  navigation.Start(Guid.NewGuid(),bearing,distance,seconds);return new(true,navigation.ReadStatus());
 }
 public static IReadOnlyList<(string Name,string Description,JsonElement Parameters)> Definitions()=>new[]{
  ("host_navigate_local","Navigate toward a bearing for 1–4m through observed local segments; inspect host_navigation_status for actual completion.",JsonSerializer.SerializeToElement(new{type="object",properties=new{bearing=new{type="number",minimum=0,maximum=359.999999},distance=new{type="number",minimum=1,maximum=4},max_seconds=new{type="number",minimum=1,maximum=12}},required=new[]{"bearing","distance"},additionalProperties=false})),
  ("host_navigation_status","Read the current bounded navigation request and actual status, including stopping.",JsonSerializer.SerializeToElement(new{type="object",properties=new{},required=Array.Empty<string>(),additionalProperties=false})),
  ("host_cancel_navigation","Stop only the navigation request identified by request_id; stopping is not yet physically settled.",JsonSerializer.SerializeToElement(new{type="object",properties=new{request_id=new{type="string",maxLength=36}},required=new[]{"request_id"},additionalProperties=false}))
 };
}
