using Engine;
using Game;
int checks=0;void Check(bool b,string reason){checks++;if(!b)throw new Exception(reason);}
var c=new Vector3(100.5f,64,-200.5f);
Probe New(Func<IEnumerable<Vector3>> provider)=>new(){m_subsystemSpawn=new SubsystemSpawn{SimulationCentersProvider=provider}};
var p=New(()=>new[]{c});p.SpawnRandomCreature();Check(p.Processed.Count==10,"zero viewport provider executes ten failed native point trials");
Check(p.Processed.All(q=>Math.Abs(q.X-Terrain.ToCell(c.X)) is >=24 and <=48&&Math.Abs(q.Z-Terrain.ToCell(c.Z)) is >=24 and <=48&&Math.Abs(q.Y-Terrain.ToCell(c.Y))<=30),"native coordinate offsets retained");
p=New(()=>Array.Empty<Vector3>());p.SpawnRandomCreature();Check(p.Processed.Count==0,"empty provider authoritative no camera access");
p=New(()=>new[]{c,c});p.SpawnRandomCreature();Check(p.Processed.Count==10,"duplicate centers one attempt set");
p=New(()=>new[]{new Vector3(float.NaN,64,0),new Vector3(0,-1,0),new Vector3(10_000_001,64,0)});p.SpawnRandomCreature();Check(p.Processed.Count==0,"invalid centers rejected");
p=New(()=>new[]{c});p.Total=SubsystemCreatureSpawn.m_totalLimit;p.SpawnRandomCreature();Check(p.Processed.Count==0,"global cap retained");
p=New(()=>new[]{c,c+Vector3.UnitX});p.Outer=52;p.SpawnRandomCreature();Check(p.Processed.Count==0&&p.AreaCalls==1,"outer cap keeps break");
p=New(()=>new[]{c});p.Accept=true;p.Inner=3;p.SpawnRandomCreature();Check(p.Processed.Count==1&&p.Spawned==0,"inner cap retained");
p=New(()=>new[]{c});p.Accept=true;p.InstallTypes();p.SpawnRandomCreature();Check(p.Processed.Count==1&&p.Spawned==1&&p.WrongSpawned==0,"random flag and spawn location filtered");
p=New(()=>new[]{c});p.Accept=true;p.InstallTypes();p.Suitability=0;p.SpawnRandomCreature();Check(p.Spawned==0,"unsuitable type never spawned");
p=New(()=>new[]{c});p.GetRandomSpawnPoint(new Vector3(0,0,0),SpawnLocationType.Surface);Check(p.Processed.All(q=>q.Y>=2&&q.Y<=254),"native height clamps retained");
p=New(()=>new[]{c});var result=p.GetRandomSpawnPoint(new Vector3(float.PositiveInfinity,64,0),SpawnLocationType.Surface);Check(result is null&&p.Processed.Count==0,"invalid direct location fails closed");
Console.WriteLine($"PASS {checks}/{checks}; native class probes only, not a live-world periodic test");
class Probe:SubsystemCreatureSpawn {
 public readonly List<Point3> Processed=new();public int Total,Outer,Inner,AreaCalls,Spawned,WrongSpawned;public bool Accept;public float Suitability=1;
 public override int CountCreatures(bool constantSpawn)=>Total;
 public override int CountCreaturesInArea(Vector2 min,Vector2 max,bool constantSpawn){AreaCalls++;return max.X-min.X>100?Outer:Inner;}
 public override SpawnLocationType GetRandomSpawnLocationType()=>SpawnLocationType.Surface;
 public override Point3? ProcessSpawnPoint(Point3 point,SpawnLocationType type){Processed.Add(point);return Accept?point:null;}
 public override float CalculateSpawnSuitability(CreatureType type,Point3 point)=>Suitability;
 public void InstallTypes(){m_creatureTypes.Add(new CreatureType("eligible",SpawnLocationType.Surface,true,false){SpawnFunction=(_,_)=>++Spawned});m_creatureTypes.Add(new CreatureType("nonrandom",SpawnLocationType.Surface,false,false){SpawnFunction=(_,_)=>++WrongSpawned});m_creatureTypes.Add(new CreatureType("wrong_location",SpawnLocationType.Cave,true,false){SpawnFunction=(_,_)=>++WrongSpawned});}
}
