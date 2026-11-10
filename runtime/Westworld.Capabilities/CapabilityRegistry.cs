using System.Text;
using System.Text.Json;

namespace Capability39;
public enum FactState { Unknown, Unavailable, Available }
public enum ParameterKind { Scalar, LiveTarget, NavigationLocation, OwnedItem, StorageSlot, Recipe, SocialOffer, TypedSettings, NestedPlan }
public sealed record CapabilityDescriptor(string Name,string Family,IReadOnlyList<ParameterKind> Parameters,bool CatalogExists);
// Facts are private to this capture. Discovery grants no execution permission.
public sealed record CapabilityState(string Name,bool? ActorSupported,FactState Facts,string Reason,bool InExecutionMenu);
public sealed record CapabilityEntry(CapabilityDescriptor Descriptor,CapabilityState State);
public sealed record CapabilityPage(Guid Session,Guid Actor,Guid Capture,int Offset,int Total,int? NextOffset,IReadOnlyList<CapabilityEntry> Entries,string Note);
public sealed record SchemaDetail(string Name,JsonElement Schema,string Availability,bool GrantedExecutionPermission=false);
public sealed record SchemaPage(Guid Capture,int Offset,int Total,int? NextOffset,IReadOnlyList<SchemaDetail> Entries,bool ByteBudgetLimited);
public sealed class CapabilityRegistry {
 readonly SortedDictionary<string,CapabilityDescriptor> catalog=new(StringComparer.Ordinal);
 public CapabilityRegistry(IEnumerable<CapabilityDescriptor> descriptors){foreach(var d in descriptors){
  if(d is null||string.IsNullOrWhiteSpace(d.Name)||d.Name.Length>64||d.Name.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c=='_'))||!d.CatalogExists||string.IsNullOrWhiteSpace(d.Family)||d.Family.Length>64||d.Parameters is null||d.Parameters.Count>32||d.Parameters.Any(p=>!Enum.IsDefined(p))||!catalog.TryAdd(d.Name,d))throw new ArgumentException("Invalid/duplicate descriptor");
  catalog[d.Name]=d with {Parameters=Array.AsReadOnly(d.Parameters.ToArray())};
 }}
 public int Count=>catalog.Count;
 public CapabilityCapture Capture(Guid session,Guid actor,Guid capture,IEnumerable<CapabilityState> states){
  if(session==Guid.Empty||actor==Guid.Empty||capture==Guid.Empty)throw new ArgumentException("Real capture identity required");
  var map=new Dictionary<string,CapabilityState>(StringComparer.Ordinal);
  foreach(var state in states){if(!catalog.ContainsKey(state.Name)||!Enum.IsDefined(state.Facts)||state.Reason is null||state.Reason.Length>192||!map.TryAdd(state.Name,state))throw new ArgumentException("Invalid capability state");
   if(state.InExecutionMenu&&(state.ActorSupported!=true||state.Facts!=FactState.Available))throw new ArgumentException("Menu cannot grant unsupported/unavailable capability");}
  if(map.Values.Count(s=>s.InExecutionMenu)>32)throw new ArgumentException("Execution menu exceeds existing protocol limit");
  if(map.Count!=catalog.Count)throw new ArgumentException("Every catalog entry requires explicit state, including unknown");
  return new(session,actor,capture,catalog.Values.Select(d=>new CapabilityEntry(d,map[d.Name])).ToArray());
 }
}
public sealed class CapabilityCapture {
 readonly CapabilityEntry[] entries;public Guid Session{get;}public Guid Actor{get;}public Guid CaptureId{get;}
 internal CapabilityCapture(Guid s,Guid a,Guid c,CapabilityEntry[] e){Session=s;Actor=a;CaptureId=c;entries=e;}
 public string FullNameIndex(int maxUtf8Bytes=4096){
  if(maxUtf8Bytes is <64 or >8192)throw new ArgumentOutOfRangeException(nameof(maxUtf8Bytes));
  // Explicit complete name index; details/state follow through bounded discovery.
  string text=string.Join(" ",entries.Select(x=>x.Descriptor.Name));
  if(Encoding.UTF8.GetByteCount(text)>maxUtf8Bytes)throw new InvalidOperationException("complete_index_exceeds_budget");return text;
 }
 public CapabilityPage Discover(Guid session,Guid actor,Guid capture,int offset=0,int limit=8){
  Scope(session,actor,capture);Bounds(offset,limit);
  var part=entries.Skip(offset).Take(limit).ToArray();int end=offset+part.Length;
  return new(Session,Actor,CaptureId,offset,entries.Length,end<entries.Length?end:null,part,"Immutable capture. States are not execution grants; dispatch needs fresh actor facts and menu validation.");
 }
 public SchemaPage Describe(Guid session,Guid actor,Guid capture,int offset,int limit,IReadOnlyDictionary<string,JsonElement> schemas,int maxUtf8Bytes=4096){
  Scope(session,actor,capture);Bounds(offset,limit);if(maxUtf8Bytes is <256 or >8192)throw new ArgumentOutOfRangeException(nameof(maxUtf8Bytes));
  var result=new List<SchemaDetail>();bool limited=false;
  foreach(var e in entries.Skip(offset).Take(limit)){
   string name=e.Descriptor.Name;
   var detail=schemas.TryGetValue(name,out var schema)?new SchemaDetail(name,schema.Clone(),"schema_only_not_execution"):new SchemaDetail(name,JsonSerializer.SerializeToElement<object?>(null),"schema_not_registered");
   var next=result.Append(detail).ToArray();int end=offset+next.Length;
   var candidate=new SchemaPage(CaptureId,offset,entries.Length,end<entries.Length?end:null,next,false);
   if(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(candidate))>maxUtf8Bytes){
    if(result.Count==0)throw new InvalidOperationException("single_schema_exceeds_budget:"+name);
    limited=true;break;
   }result.Add(detail);
  }
  int cursor=offset+result.Count;
  return new(CaptureId,offset,entries.Length,cursor<entries.Length?cursor:null,result.AsReadOnly(),limited);
 }
 void Scope(Guid s,Guid a,Guid c){if(s!=Session||a!=Actor||c!=CaptureId)throw new InvalidOperationException("stale_or_wrong_scope");}
 void Bounds(int offset,int limit){if(offset<0||offset>entries.Length||limit is <1 or >8)throw new ArgumentOutOfRangeException("bounded_page");}
}
