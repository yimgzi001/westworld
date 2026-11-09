using Engine;
using Game;
int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
// Real compiled upstream SubsystemSpawn, but NOT a loaded Project or running world.
var centers=Enumerable.Range(0,5).Select(i=>new Vector3(i*100,65,-i*100)).ToArray();
var spawn=new SubsystemSpawn {SimulationCentersProvider=()=>centers};
var result=spawn.GetSimulationCenters();Check(result.Length==5,"five centers without GameWidgets");
Check(result[4].X==400,"provider preserves remote center");result[0]=new(999,999,999);Check(centers[0].X==0,"caller cannot change provider array");
spawn.SimulationCentersProvider=()=>Array.Empty<Vector3>();Check(spawn.GetSimulationCenters().Length==0,"empty actor set does not fall back to cameras");
int calls=0;spawn.SimulationCentersProvider=()=>{calls++;return centers;};spawn.GetSimulationCenters();Check(calls==1,"one provider snapshot call");
spawn.SimulationCentersProvider=()=>throw new InvalidOperationException("injected");bool failed=false;try{spawn.GetSimulationCenters();}catch(InvalidOperationException){failed=true;}Check(failed,"provider failure is not silently empty");
bool unboundRejected=false;try{using var backend=new Westworld.Scapi.Adapter.ScapiInterestBackend(new SubsystemTerrain(),new SubsystemSpawn());}catch(ArgumentException){unboundRejected=true;}Check(unboundRejected,"unloaded subsystems cannot be bound as a real world");
Console.WriteLine($"{checks}/{checks} compiled native spawn-hook checks passed; NO world/chunk/actor runtime tested");
