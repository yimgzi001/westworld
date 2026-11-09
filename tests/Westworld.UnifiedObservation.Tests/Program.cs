using Game.Westworld;
using Engine;
int count=0;void Check(bool yes,string name){if(!yes)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
var slots=new List<InventorySlotView>{new(0,9,2),new(1,0,0),new(2,9,3),new(3,21,4)};
var summary=ActorWorldMemory.SummarizeInventory(new(slots,2));
Check(summary.NonEmptySlots.Count==3,"all occupied slots included");
Check(summary.EmptySlotIds.SequenceEqual(new[]{1}),"empty slots explicit");
Check(summary.SelectedSlot==2,"selected slot preserved");
Check(summary.ItemCounts.SequenceEqual(new[]{new ObservationItemCount(9,5),new ObservationItemCount(21,4)}),"counts by exact native value");
slots.Clear();Check(summary.NonEmptySlots.Count==3&&summary.ItemCounts[0].Count==5,"summary independent of mutable source collection");
Check(ActorWorldMemory.SummarizeInventory(null)==null,"unavailable inventory not invented empty");
var empty=ActorWorldMemory.SummarizeInventory(new(Array.Empty<InventorySlotView>(),null));Check(empty.NonEmptySlots.Count==0&&empty.EmptySlotIds.Count==0&&empty.ItemCounts.Count==0,"actual empty inventory supported");
var exact=ActorWorldMemory.SummarizeInventory(new(new[]{new InventorySlotView(0,9,1),new InventorySlotView(1,9+1024,2)},0));Check(exact.ItemCounts.Count==2,"metadata values not collapsed into content id");
bool overflow=false;try{ActorWorldMemory.SummarizeInventory(new(new[]{new InventorySlotView(0,9,int.MaxValue),new InventorySlotView(1,9,1)},0));}catch(OverflowException){overflow=true;}Check(overflow,"count overflow does not wrap into false fact");
var old=new ActorObservation(1,null,null,Array.Empty<ObservedEntity>(),Array.Empty<ObservedPickable>(),"inventory","grid");
Check(old.VisibleBlocks.Count==0&&old.Inventory==null&&old.Coverage==null,"legacy positional constructor compatible without invented snapshot");
Check(old.SnapshotId!=Guid.Empty&&old.SnapshotId!=new ActorObservation(1,null,null,[],[],"i","c").SnapshotId,"capture instances carry distinct observation identity");
var drop=new ObservedPickable("drop",88,1,Vector3.Zero,1){Sense="proximity"};Check(drop.Sense=="proximity","proximity evidence remains explicit");
var blocks=Array.AsReadOnly(new[]{new ObservedBlock("real",new Point3(1,2,3),new Vector3(1.5f,2.999f,3.5f),2,"dirt",2,null)});
var coverage=new ObservationCoverage(5,1,3,1,2,true,false,new(324,30,10,20,10,3,10),4,false,true,16,16,null,null,"known bounded grid only");
var current=old with{VisibleBlocks=blocks,Coverage=coverage,Inventory=new(new[]{new InventorySlotView(0,9,2)},0),InventorySummary=summary};
Check(current.Coverage.OmittedBlocks==2&&current.Coverage.ReturnedBlocks==current.VisibleBlocks.Count,"output truncation distinguished from scan completion");
Check(current.Coverage.PickablesTruncated&&!current.Coverage.EntitiesTruncated,"object group truncation independent");
Check(current.VisibleBlocks[0].Face==null,"sample does not invent placement face");
Check(current.Inventory.Slots[0].Count==2,"complete inventory slot shape available in same observation");
var nativeLogPoint=new Vector3(-83.5f,65.2f,-97.5f);
var nativeLogJson=System.Text.Json.JsonSerializer.Serialize(new{position=NativeLogCoordinates.Position(nativeLogPoint)});
using(var parsed=System.Text.Json.JsonDocument.Parse(nativeLogJson)){
 var xyz=parsed.RootElement.GetProperty("position");Check(xyz.GetArrayLength()==3,"real Engine.Vector3 log emits exactly three coordinates without recursion");
 Check(xyz[0].GetSingle()==nativeLogPoint.X&&xyz[1].GetSingle()==nativeLogPoint.Y&&xyz[2].GetSingle()==nativeLogPoint.Z,"native log coordinate values preserved exactly");
}
bool nonfiniteRejected=false;try{NativeLogCoordinates.Position(new Vector3(float.NaN,0,0));}catch(ArgumentException){nonfiniteRejected=true;}
Check(nonfiniteRejected,"invalid coordinate logging fails explicitly not IgnoreCycles");
Console.WriteLine($"TOTAL {count}/{count}; pure contract/summary tests only, no native world executed");
