using System.Text.Json;
using Capability39;
using Westworld.Decisions.Local;

// Pure host menu policy. It performs no world reads, native dispatch or model calls.
// Rebuild with a new capture before dispatch; discovery is never an execution grant.
public sealed record CapabilityMenuCandidate(string Name, bool? ActorSupported, FactState Facts,
 string Reason, ToolOffer? Offer, JsonElement? Schema = null);
public sealed record CapabilityMenuSelection(IReadOnlyList<string> Tools, IReadOnlyList<string> Families);
public sealed record CapabilityMenuView(CapabilityCapture Capture, IReadOnlyList<ToolOffer> Offers,
 IReadOnlyList<string> RequestedButUnavailable, string CompleteIndex);

public sealed class CapabilityMenu {
 public const string DiscoverTool = "read_capabilities", SchemaTool = "read_capability_schema";
 readonly Guid session, actor, capture;
 readonly CapabilityDescriptor[] descriptors;
 readonly Dictionary<string, CapabilityMenuCandidate> candidates;
 readonly CapabilityRegistry registry;
 readonly Dictionary<string, JsonElement> schemas;
 CapabilityMenuSelection selected = new(Array.Empty<string>(), Array.Empty<string>());
 public CapabilityMenu(Guid session, Guid actor, Guid capture, IEnumerable<CapabilityDescriptor> catalog,
  IEnumerable<CapabilityMenuCandidate> legacy, IEnumerable<CapabilityMenuCandidate> typedFamilies) {
  this.session=session; this.actor=actor; this.capture=capture;
  descriptors=catalog.ToArray(); registry=new(descriptors);
  if(descriptors.Any(x=>x.Name is DiscoverTool or SchemaTool))throw new ArgumentException("Discovery names reserved");
  candidates=new(StringComparer.Ordinal); schemas=new(StringComparer.Ordinal);
  var names=descriptors.Select(x=>x.Name).ToHashSet(StringComparer.Ordinal);
  void Merge(IEnumerable<CapabilityMenuCandidate> source, bool replace) {
   var seen=new HashSet<string>(StringComparer.Ordinal);
   foreach(var c in source) {
    if(!names.Contains(c.Name)||!seen.Add(c.Name)||(!replace&&candidates.ContainsKey(c.Name)))throw new ArgumentException("Unknown/duplicate candidate");
    if(c.Offer!=null&&(c.Offer.Name!=c.Name||c.ActorSupported!=true||c.Facts!=FactState.Available))throw new ArgumentException("Offer needs confirmed same-capture facts");
    // Explicit typed-family replacement prevents legacy loose scalar bindings winning.
    candidates[c.Name]=c;
   }
  }
  Merge(legacy,false); Merge(typedFamilies,true);
  foreach(var c in candidates.Values) {
   var schema=c.Schema ?? c.Offer?.Parameters;
   if(schema.HasValue)schemas[c.Name]=schema.Value.Clone();
  }
  _=Build(); // Validate all states and real nonempty identities immediately.
 }
 public CapabilityMenuView Select(Guid s, Guid a, Guid c, CapabilityMenuSelection selection) {
  Scope(s,a,c);
  if(selection.Tools==null||selection.Families==null||selection.Tools.Count>30||selection.Families.Count>32)throw new ArgumentException("Bounded explicit selection required");
  var names=descriptors.Select(x=>x.Name).ToHashSet(StringComparer.Ordinal);
  var families=descriptors.Select(x=>x.Family).ToHashSet(StringComparer.Ordinal);
  if(selection.Tools.Distinct().Count()!=selection.Tools.Count||selection.Families.Distinct().Count()!=selection.Families.Count||selection.Tools.Any(x=>!names.Contains(x))||selection.Families.Any(x=>!families.Contains(x)))throw new ArgumentException("Unknown/duplicate selection");
  var next=new CapabilityMenuSelection(selection.Tools.ToArray(),selection.Families.ToArray());
  var view=Build(next); selected=next; return view; // Failed expansion leaves previous selection unchanged.
 }
 public CapabilityMenuView Build()=>Build(selected);
 CapabilityMenuView Build(CapabilityMenuSelection selection) {
  var requested=descriptors.Where(d=>selection.Tools.Contains(d.Name)||selection.Families.Contains(d.Family)).Select(d=>d.Name).ToHashSet(StringComparer.Ordinal);
  var offered=candidates.Values.Where(x=>requested.Contains(x.Name)&&x.Offer!=null).OrderBy(x=>x.Name,StringComparer.Ordinal).ToArray();
  if(offered.Length>30)throw new InvalidOperationException("selection_exceeds_30_execution_slots_select_fewer_tools_or_families");
  var offeredNames=offered.Select(x=>x.Name).ToHashSet(StringComparer.Ordinal);
  var states=descriptors.Select(d=>candidates.TryGetValue(d.Name,out var c)?new CapabilityState(d.Name,c.ActorSupported,c.Facts,c.Reason,offeredNames.Contains(d.Name)):new CapabilityState(d.Name,null,FactState.Unknown,"same_capture_adapter_not_available",false));
  var snapshot=registry.Capture(session,actor,capture,states);
  return new(snapshot,offered.Select(c=>c.Offer!).Concat(DiscoveryOffers()).ToArray(),requested.Except(offeredNames).OrderBy(x=>x,StringComparer.Ordinal).ToArray(),snapshot.FullNameIndex());
 }
 public CapabilityPage Discover(Guid s,Guid a,Guid c,int offset=0,int limit=8)=>Build().Capture.Discover(s,a,c,offset,limit);
 public SchemaPage Describe(Guid s,Guid a,Guid c,int offset=0,int limit=8,int maxUtf8Bytes=4096)=>Build().Capture.Describe(s,a,c,offset,limit,schemas,maxUtf8Bytes);
 public bool IsDiscovery(string name)=>name is DiscoverTool or SchemaTool;
 public JsonElement HandleDiscovery(Guid s,Guid a,Guid c,string name,JsonElement arguments) {
  Scope(s,a,c); if(!IsDiscovery(name))throw new ArgumentException("Not a discovery tool");
  if(arguments.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Object arguments required");
  var props=arguments.EnumerateObject().ToArray();
  if(props.Select(x=>x.Name).Distinct().Count()!=props.Length||props.Any(x=>x.Name is not("offset" or "limit" or "select_tool" or "select_family" or "plan_page")))throw new ArgumentException("Unknown/duplicate argument");
  bool hasTool=arguments.TryGetProperty("select_tool",out var tool), hasFamily=arguments.TryGetProperty("select_family",out var family);
  if(arguments.TryGetProperty("plan_page",out var page)&&(!hasTool||tool.ValueKind!=JsonValueKind.String||tool.GetString()!="submit_plan"||!page.TryGetInt32(out int pageNumber)||pageNumber<0||pageNumber>127))throw new ArgumentException("plan_page_requires_submit_plan_selection_and_bounded_integer");
  if(hasTool||hasFamily) {
   if(name!=SchemaTool||hasTool&&hasFamily)throw new ArgumentException("Select exactly one tool or family through schema discovery");
   var value=hasTool?tool:family;
   if(value.ValueKind!=JsonValueKind.String)throw new ArgumentException("Selection must be a catalog name");
   var selection=hasTool?new CapabilityMenuSelection(new[]{value.GetString()!},Array.Empty<string>()):new CapabilityMenuSelection(Array.Empty<string>(),new[]{value.GetString()!});
   var updated=Select(s,a,c,selection);
   return JsonSerializer.SerializeToElement(new{selectionApplied=true,executionGranted=false,scope=new{session,actor,capture},selectedTools=selection.Tools,selectedFamilies=selection.Families,nextMenu=updated.Offers.Select(x=>x.Name).ToArray(),unavailable=updated.RequestedButUnavailable,note="Menu only. Host must publish a new decision prompt and revalidate current facts before native dispatch."});
  }
  int offset=arguments.TryGetProperty("offset",out var o)?o.GetInt32():0,limit=arguments.TryGetProperty("limit",out var l)?l.GetInt32():8;
  return name==DiscoverTool?JsonSerializer.SerializeToElement(Discover(s,a,c,offset,limit)):JsonSerializer.SerializeToElement(Describe(s,a,c,offset,limit));
 }
 // Selection can be requested with read_capability_schema select_tool/select_family. Never infer it from hunger.
 // Call Select separately and publish its resulting fresh menu before accepting an action.
 IEnumerable<ToolOffer> DiscoveryOffers() {
  var p=JsonSerializer.SerializeToElement(new{type="object",properties=new{offset=new{type="integer",minimum=0,maximum=descriptors.Length},limit=new{type="integer",minimum=1,maximum=8}},required=Array.Empty<string>(),additionalProperties=false});
  yield return new(DiscoverTool,"Page all capability states. Discovery does not authorize execution; explicitly select tools/families to expand the next menu.",p);
  var selectProperties=new Dictionary<string,object>{
   ["offset"]=new{type="integer",minimum=0,maximum=descriptors.Length},["limit"]=new{type="integer",minimum=1,maximum=8},
   ["select_tool"]=new{type="string",@enum=descriptors.Select(x=>x.Name).ToArray()},
   ["select_family"]=new{type="string",@enum=descriptors.Select(x=>x.Family).Distinct().ToArray()},
   ["plan_page"]=new{type="integer",minimum=0,maximum=127}};
  var sp=JsonSerializer.SerializeToElement(new{type="object",properties=selectProperties,required=Array.Empty<string>(),additionalProperties=false});
  yield return new(SchemaTool,"Page schemas, or set exactly one select_tool/select_family to replace the NEXT execution menu. For submit_plan selection, optional plan_page chooses an explicit finite-plan schema page. Selection changes only host menu, never executes game actions. Await the new prompt; unavailable facts stay unavailable.",sp);
 }
 void Scope(Guid s,Guid a,Guid c){if(s!=session||a!=actor||c!=capture)throw new InvalidOperationException("stale_or_wrong_scope");}
}
