using Game;
using Game.Westworld;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

// Privileged host result; only Situation is model input. The entity map is deliberately
// separate, and is not an execution authorization: each target must be seen again.
public sealed record NativeFoodScene(FoodSituation Situation,IReadOnlyDictionary<Guid,int> VisibleEntityIds);
public sealed partial class NativeActorSession {
 readonly Dictionary<Guid,(long Version,SlotObservation[] Slots)> foodInventoryVersions=new();
 public NativeFoodScene ObserveFood(Guid session,Guid actor,AtomicActions.ActorQueries queries){
  var player=Actor(session,actor);
  ArgumentNullException.ThrowIfNull(queries);
  if(!queries.IsBoundTo(player.Entity))throw new InvalidOperationException("Food sensor belongs to another actor");
  var o=Observe(session,actor);long version=0;
  if(foodInventoryVersions.TryGetValue(actor,out var previous)){
   version=previous.Version;
   if(!previous.Slots.SequenceEqual(o.Inventory))version=checked(version+1);
  }
  foodInventoryVersions[actor]=(version,o.Inventory.ToArray());
  var resources=new List<FoodResource>();var entities=new Dictionary<Guid,int>();
  FoodEvidence Evidence()=>new(Guid.NewGuid(),o.SessionId,o.ActorId,o.ObservationId,o.Time);
  double Distance(Position p)=>Math.Sqrt(Math.Pow(p.X-o.Position.X,2)+Math.Pow(p.Y-o.Position.Y,2)+Math.Pow(p.Z-o.Position.Z,2));
  foreach(var cell in o.Cells.Where(c=>FoodCapabilities.AssessObservedHarvest(c).Eligible).Take(16)){
   resources.Add(new(Evidence(),FoodResourceKind.Plant,new(cell.X+.5,cell.Y+.5,cell.Z+.5),cell.Value!.Value,1,true,"native_food_plant_requires_reach_and_pickup"));
  }
  // A separate semantic radius, not an enlargement of local walkable terrain evidence.
  // Native query enforces the real eye cone and ray occlusion. No global animal scan.
  var nearby=queries.FindVisibleObjects(16,24);
  foreach(var e in nearby.Entities){
   var pos=new Position(e.Position.X,e.Position.Y,e.Position.Z);if(Distance(pos)>16||!LoadedRectangle(player.ComponentCreatureModel.EyePosition,e.Position))continue;
   // Resolve ONLY an ID just returned by this actor's native visible query. Type/name
   // classification is allowed; health, loot tables and hidden creatures are not exposed.
   var entity=project.Entities.FirstOrDefault(v=>v.Id==e.EntityId);
   if(entity?.FindComponent<ComponentCreature>() is null||entity.FindComponent<ComponentPlayer>() is not null)continue;
   var evidence=Evidence();entities.Add(evidence.Id,e.EntityId);
   string label=(e.Name??"unknown");if(label.Length>72)label=label[..72];
   resources.Add(new(evidence,FoodResourceKind.Animal,pos,0,1,false,"visible_animal_unassessed:"+label));
  }
  foreach(var d in nearby.Pickables){
   var pos=new Position(d.Position.X,d.Position.Y,d.Position.Z);if(Distance(pos)>16||!LoadedRectangle(player.ComponentCreatureModel.EyePosition,d.Position)||FoodCapabilities.Describe(d.Value) is null)continue;
   var block=BlocksManager.Blocks[Terrain.ExtractContents(d.Value)];
   var assessment=FoodCapabilities.AssessObservedItem(new(0,d.Value,d.Count,block.GetNutritionalValue(d.Value)),block.GetSicknessProbability(d.Value));
   resources.Add(new(Evidence(),FoodResourceKind.Drop,pos,d.Value,d.Count,false,assessment.Eligible?"observed_safe_food_drop":assessment.Reason));
  }
  var ownStations=player.Entity.FindComponent<ComponentCraftingTable>() is not null
   ?new[]{new FoodStation(Evidence(),FoodStationKind.Personal,o.Position,true,0)}:Array.Empty<FoodStation>();
  // Station/recipe execution will be registered only with its native executor. A public
  // recipe is not evidence of material possession. Diagnostic hunting stays non-executable.
  var situation=new FoodSituation(o,version,resources.ToArray(),ownStations,[],
   new HashSet<FoodStrategyKind>{FoodStrategyKind.EatInventory,FoodStrategyKind.GatherPlant,FoodStrategyKind.Search});
  return new(situation,new System.Collections.ObjectModel.ReadOnlyDictionary<Guid,int>(entities));
 }
}
