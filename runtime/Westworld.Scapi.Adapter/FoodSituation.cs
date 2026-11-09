using System.Security.Cryptography;
using System.Text;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public enum FoodStrategyKind { EatInventory, GatherPlant, CollectDrop, HuntVisible, CraftTool, CookFood, Search }
public enum FoodResourceKind { Plant, Drop, Animal }
public enum FoodStationKind { Personal, Workbench, Furnace }
// All evidence is host-authored and actor-private. This contract cannot authenticate sensor truth.
public sealed record FoodEvidence(Guid Id,Guid SessionId,Guid ActorId,Guid ObservationId,double ObservedAt);
public sealed record FoodResource(FoodEvidence Evidence,FoodResourceKind Kind,Position Position,int Value,int Count,
 bool SupportedHarvest=false,string Risk="unassessed");
public sealed record FoodStation(FoodEvidence Evidence,FoodStationKind Kind,Position Position,bool Accessible,float Heat);
// Ingredients are host-normalized full encoded values, not guessed crafting names or world locations.
public sealed record FoodIngredient(int Value,int Count);
public sealed record FoodRecipe(string Id,FoodStrategyKind Purpose,FoodStationKind Station,int ResultValue,
 IReadOnlyList<FoodIngredient> Ingredients,float RequiredHeat=0);
public sealed record FoodSituation(ActorObservation Self,long InventoryVersion,IReadOnlyList<FoodResource> Resources,
 IReadOnlyList<FoodStation> Stations,IReadOnlyList<FoodRecipe> Recipes,IReadOnlySet<FoodStrategyKind> EnabledCapabilities);
public sealed record FoodStrategyCandidate(Guid Id,FoodStrategyKind Kind,Guid SessionId,Guid ActorId,Guid ObservationId,
 long InventoryVersion,IReadOnlyList<Guid> EvidenceIds,int Slot,int ExpectedValue,Position? Target,
 string? RecipeId,IReadOnlyList<FoodIngredient> MissingMaterials,IReadOnlyList<string> Preconditions,
 string Risk,bool Executable,string Reason);

/// <summary>Immutable decision offer. Resolve validates membership, age, scope and inventory version;
/// native executors must still reobserve and validate live targets before performing any effect.</summary>
public sealed class FoodSituationPlan {
 public Guid SessionId {get;} public Guid ActorId {get;} public Guid ObservationId {get;}
 public double ObservedAt {get;} public long InventoryVersion {get;}
 public float Health {get;} public float Food {get;}
 public IReadOnlyList<FoodStrategyCandidate> Candidates {get;}
 public IReadOnlyList<FoodStrategyCandidate> ExecutableCandidates {get;}
 internal FoodSituationPlan(ActorObservation self,long version,IEnumerable<FoodStrategyCandidate> candidates){
  SessionId=self.SessionId;ActorId=self.ActorId;ObservationId=self.ObservationId;ObservedAt=self.Time;InventoryVersion=version;Health=self.Health;Food=self.Food;
  Candidates=Array.AsReadOnly(candidates.ToArray());ExecutableCandidates=Array.AsReadOnly(Candidates.Where(c=>c.Executable).ToArray());
 }
 public FoodStrategyCandidate Resolve(Guid candidateId,Guid session,Guid actor,Guid observation,long inventoryVersion,double now){
  if(session!=SessionId||actor!=ActorId||observation!=ObservationId||inventoryVersion!=InventoryVersion
   ||!double.IsFinite(now)||now<ObservedAt||now-ObservedAt>3)throw new ArgumentException("Stale or mismatched food decision");
  return ExecutableCandidates.SingleOrDefault(c=>c.Id==candidateId)??throw new ArgumentException("Candidate is not an executable offer");
 }
}

// Registration is a host integration assertion, not a model capability flag. Extend only
// after wiring an executor; native fixture success alone does not register a model route.
public sealed class FoodExecutorRegistry {
 readonly HashSet<FoodStrategyKind> kinds;
 public static FoodExecutorRegistry Current {get;}=new([FoodStrategyKind.EatInventory,FoodStrategyKind.GatherPlant,FoodStrategyKind.Search]);
 public FoodExecutorRegistry(IEnumerable<FoodStrategyKind> integrated){
  ArgumentNullException.ThrowIfNull(integrated);kinds=integrated.ToHashSet();
  if(kinds.Any(k=>!Enum.IsDefined(k)))throw new ArgumentException("Unknown executor kind");
 }
 public bool Contains(FoodStrategyKind kind)=>kinds.Contains(kind);
}
public static class FoodSituationPlanner {
 public static FoodSituationPlan Create(FoodSituation situation,FoodExecutorRegistry? executors=null){
  executors??=FoodExecutorRegistry.Current;
  ArgumentNullException.ThrowIfNull(situation);var o=situation.Self;ArgumentNullException.ThrowIfNull(o);
  if(o.SessionId==Guid.Empty||o.ActorId==Guid.Empty||o.ObservationId==Guid.Empty||!double.IsFinite(o.Time)||o.Time<0
   ||!float.IsFinite(o.Health)||o.Health<0||o.Health>1||!float.IsFinite(o.Food)||o.Food<0||o.Food>1
   ||situation.InventoryVersion<0||o.Inventory is null||o.Inventory.Length>256||o.Cells is null||o.Cells.Length>4096
   ||situation.Resources is null||situation.Resources.Count>64||situation.Stations is null||situation.Stations.Count>32
   ||situation.Recipes is null||situation.Recipes.Count>64||situation.EnabledCapabilities is null
   ||situation.EnabledCapabilities.Any(c=>!Enum.IsDefined(c)))throw new ArgumentException("Invalid bounded food situation");
  ValidatePosition(o.Position);
  if(o.Inventory.Any(s=>s is null||s.Slot<0||s.Slot>4095||s.Value<0||s.Count<0||s.Count>1000000||!float.IsFinite(s.Nutrition)||s.Nutrition<0)
   ||o.Inventory.Select(s=>s.Slot).Distinct().Count()!=o.Inventory.Length)throw new ArgumentException("Invalid inventory");
  var ids=new HashSet<Guid>();
  void Evidence(FoodEvidence e,Position p){
   ValidatePosition(p);if(e is null||e.Id==Guid.Empty||!ids.Add(e.Id)||e.SessionId!=o.SessionId||e.ActorId!=o.ActorId||e.ObservationId!=o.ObservationId
    ||!double.IsFinite(e.ObservedAt)||e.ObservedAt!=o.Time||Distance(o.Position,p)>16)throw new ArgumentException("Invalid actor-local current evidence");
  }
  foreach(var r in situation.Resources){if(r is null||!Enum.IsDefined(r.Kind)||r.Value<0||r.Count<=0||r.Count>1000000||string.IsNullOrWhiteSpace(r.Risk)||r.Risk.Length>128)throw new ArgumentException("Invalid resource");Evidence(r.Evidence,r.Position);}
  foreach(var s in situation.Stations){if(s is null||!Enum.IsDefined(s.Kind)||!float.IsFinite(s.Heat)||s.Heat<0)throw new ArgumentException("Invalid station");Evidence(s.Evidence,s.Position);}
  var recipes=new HashSet<string>();
  foreach(var r in situation.Recipes)if(r is null||string.IsNullOrWhiteSpace(r.Id)||r.Id.Length>128||!recipes.Add(r.Id)
   ||r.Purpose is not (FoodStrategyKind.CraftTool or FoodStrategyKind.CookFood)||!Enum.IsDefined(r.Station)||r.ResultValue<=0
   ||!float.IsFinite(r.RequiredHeat)||r.RequiredHeat<0||r.Ingredients is null||r.Ingredients.Count is <1 or >16
   ||r.Ingredients.Any(i=>i is null||i.Value<=0||i.Count<=0||i.Count>1000000)
   ||r.Ingredients.Select(i=>i.Value).Distinct().Count()!=r.Ingredients.Count)throw new ArgumentException("Invalid normalized recipe");
  var result=new List<FoodStrategyCandidate>();
  void Add(FoodStrategyKind kind,IEnumerable<Guid> evidence,int slot,int value,Position? target,string? recipe,
   IEnumerable<FoodIngredient> missing,IEnumerable<string> requirements,string risk,bool ready,string reason){
   // Only these native routes currently exist. Capability flags cannot fabricate other executors.
   bool implemented=executors.Contains(kind);
   bool executable=o.Health>0&&implemented&&situation.EnabledCapabilities.Contains(kind)&&ready;
   var evidenceArray=evidence.ToArray();
   string identity=string.Join("|",o.SessionId,o.ActorId,o.ObservationId,situation.InventoryVersion,kind,slot,value,recipe??"",string.Join(",",evidenceArray.Order()));
   var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0,16));
   result.Add(new(id,kind,o.SessionId,o.ActorId,o.ObservationId,situation.InventoryVersion,
    Array.AsReadOnly(evidenceArray),slot,value,target,recipe,Array.AsReadOnly(missing.ToArray()),
    Array.AsReadOnly(requirements.ToArray()),risk,executable,!implemented?"executor_not_integrated":!situation.EnabledCapabilities.Contains(kind)?"capability_disabled":!ready?reason:o.Health<=0?"actor_unavailable":reason));
  }
  foreach(var s in o.Inventory.Where(s=>s.Count>0&&s.CanEat&&s.Nutrition>0).OrderBy(s=>s.Slot))
   Add(FoodStrategyKind.EatInventory,[],s.Slot,s.Value,null,null,[],["live_slot_and_native_food_revalidation"],"host_assessed_edible",true,"observed_inventory");
  foreach(var r in situation.Resources.OrderBy(r=>Distance(o.Position,r.Position))){
   var kind=r.Kind switch{FoodResourceKind.Plant=>FoodStrategyKind.GatherPlant,FoodResourceKind.Drop=>FoodStrategyKind.CollectDrop,_=>FoodStrategyKind.HuntVisible};
   // Plant coordinates and value must also be in the actor's actual current voxel snapshot.
   bool harvest=r.Kind==FoodResourceKind.Plant&&r.SupportedHarvest&&o.Cells.Any(c=>c is not null&&c.Visibility=="known"&&c.Value==r.Value
    &&c.X==(int)Math.Floor(r.Position.X)&&c.Y==(int)Math.Floor(r.Position.Y)&&c.Z==(int)Math.Floor(r.Position.Z));
   Add(kind,[r.Evidence.Id],-1,r.Value,r.Position,null,[],["current_target_revalidation","bounded_approach_not_proven_reachable"],r.Risk,harvest,"harvest_evidence_or_capability_missing");
  }
  foreach(var recipe in situation.Recipes){
   var missing=recipe.Ingredients.Select(i=>new FoodIngredient(i.Value,(int)Math.Max(0,i.Count-o.Inventory.Where(s=>s.Value==i.Value).Sum(s=>(long)s.Count)))).Where(i=>i.Count>0).ToArray();
   var station=situation.Stations.Where(s=>s.Kind==recipe.Station&&s.Accessible).OrderBy(s=>Distance(o.Position,s.Position)).FirstOrDefault();
   var needs=new List<string>{"native_recipe_and_inventory_revalidation"};
   if(missing.Length>0)needs.Add("materials_missing");if(station is null)needs.Add("observed_accessible_station_missing");
   if(recipe.RequiredHeat>0&&(station is null||station.Heat<recipe.RequiredHeat))needs.Add("sufficient_heat_and_fuel_not_established");
   Add(recipe.Purpose,station is null?[]:[station.Evidence.Id],-1,recipe.ResultValue,station?.Position,recipe.Id,missing,needs,
    "production_chain_unverified",needs.Count==1,"preparation_required");
  }
  Add(FoodStrategyKind.Search,[],-1,0,null,null,[],["bounded_local_observation_only"],"unknown_environment",true,"bounded_search_alternative");
  return new(o,situation.InventoryVersion,result);
 }
 static void ValidatePosition(Position p){if(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z)||Math.Abs(p.X)>10_000_000||Math.Abs(p.Z)>10_000_000||p.Y<0||p.Y>255)throw new ArgumentException("Position outside supported world");}
 static double Distance(Position a,Position b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2));
}
