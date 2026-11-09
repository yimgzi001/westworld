using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
int checks=0;void Check(bool b,string n){checks++;if(!b)throw new Exception(n);Console.WriteLine("PASS "+n);}
void Reject(Action a,string n){bool hit=false;try{a();}catch(ArgumentException){hit=true;}Check(hit,n);}
var sid=Guid.NewGuid();var aid=Guid.NewGuid();var oid=Guid.NewGuid();
var self=new ActorObservation(sid,aid,oid,1,new(.5,64,.5),1,.4f,[new(0,89,1,.2f,true),new(1,3,2)], [new(1,64,0,"known",131)]);
FoodEvidence Evidence()=>new(Guid.NewGuid(),sid,aid,oid,1);
var plant=new FoodResource(Evidence(),FoodResourceKind.Plant,new(1.5,64,.5),131,1,true,"host_assessed");
var pig=new FoodResource(Evidence(),FoodResourceKind.Animal,new(2,64,.5),0,1,false,"combat_unassessed");
var drop=new FoodResource(Evidence(),FoodResourceKind.Drop,new(3,64,.5),88,1,false,"raw_food_risk");
var station=new FoodStation(Evidence(),FoodStationKind.Furnace,new(2,64,2),true,0);
var recipe=new FoodRecipe("cook-meat",FoodStrategyKind.CookFood,FoodStationKind.Furnace,89,[new(88,1)],1);
var all=Enum.GetValues<FoodStrategyKind>().ToHashSet();
var situation=new FoodSituation(self,7,[plant,pig,drop],[station],[recipe],all);
var plan=FoodSituationPlanner.Create(situation);
Check(plan.ExecutableCandidates.Select(c=>c.Kind).SequenceEqual(new[]{FoodStrategyKind.EatInventory,FoodStrategyKind.GatherPlant,FoodStrategyKind.Search}),"only wired enabled feasible routes executable");
Check(plan.Candidates.Single(c=>c.Kind==FoodStrategyKind.HuntVisible).Reason=="executor_not_integrated","pig does not imply integrated hunting");
Check(!plan.Candidates.Single(c=>c.Kind==FoodStrategyKind.CollectDrop).Executable,"drop handler not invented");
var cook=plan.Candidates.Single(c=>c.Kind==FoodStrategyKind.CookFood);
Check(!cook.Executable&&cook.MissingMaterials.Single()==new FoodIngredient(88,1),"recipe is not inventory");
Check(cook.Preconditions.Contains("sufficient_heat_and_fuel_not_established"),"cold furnace cannot imply cooking");
Check(plan.Candidates.All(c=>c.InventoryVersion==7&&c.ActorId==aid&&c.SessionId==sid&&c.ObservationId==oid),"candidates retain scope and version");
Check(plan.Candidates.Select(c=>c.Id).SequenceEqual(FoodSituationPlanner.Create(situation).Candidates.Select(c=>c.Id)),"candidate IDs stable for same snapshot");
Check(!plan.Candidates.Select(c=>c.Id).Intersect(FoodSituationPlanner.Create(situation with{InventoryVersion=8}).Candidates.Select(c=>c.Id)).Any(),"inventory revision changes candidate identities");
var eat=plan.ExecutableCandidates[0];Check(plan.Resolve(eat.Id,sid,aid,oid,7,3)==eat,"resolve exact executable snapshot offer");
Reject(()=>plan.Resolve(cook.Id,sid,aid,oid,7,1),"model cannot select diagnostic candidate");
Reject(()=>plan.Resolve(Guid.NewGuid(),sid,aid,oid,7,1),"model cannot invent candidate");
Reject(()=>plan.Resolve(eat.Id,sid,Guid.NewGuid(),oid,7,1),"cross actor selection refused");
Reject(()=>plan.Resolve(eat.Id,sid,aid,oid,8,1),"changed inventory selection refused");
Reject(()=>plan.Resolve(eat.Id,sid,aid,oid,7,4.01),"expired decision refused");
Reject(()=>plan.Resolve(eat.Id,sid,aid,oid,7,0),"future decision refused");
Reject(()=>FoodSituationPlanner.Create(situation with{Resources=[pig with{Evidence=pig.Evidence with{ActorId=Guid.NewGuid()}}]}),"foreign animal evidence refused");
Reject(()=>FoodSituationPlanner.Create(situation with{Resources=[pig with{Evidence=pig.Evidence with{ObservedAt=0}}]}),"old entity evidence not current sight");
Reject(()=>FoodSituationPlanner.Create(situation with{Resources=[pig with{Position=new(30,64,0)}]}),"out of local sensor radius refused");
Reject(()=>FoodSituationPlanner.Create(situation with{Resources=[pig,pig]}),"duplicate evidence refused");
Reject(()=>FoodSituationPlanner.Create(situation with{Recipes=[recipe with{Ingredients=[new(88,1),new(88,2)]}]}),"ambiguous recipe quantities refused");
Check(!FoodSituationPlanner.Create(situation with{Self=self with{Cells=[]}}).Candidates.Single(c=>c.Kind==FoodStrategyKind.GatherPlant).Executable,"resource label without current voxel proof rejected");
Check(FoodSituationPlanner.Create(situation with{Self=self with{Health=0}}).ExecutableCandidates.Count==0,"dead actor gets no executable work");
Check(FoodSituationPlanner.Create(situation with{Self=self with{Food=1}}).ExecutableCandidates.Any(c=>c.Kind==FoodStrategyKind.Search),"full actor may explore or prepare food at model discretion");
var empty=situation with{Self=self with{Inventory=[],Cells=[]},Resources=[],Stations=[],Recipes=[]};
Check(FoodSituationPlanner.Create(empty).ExecutableCandidates.Single().Kind==FoodStrategyKind.Search,"no evidence yields bounded search not invented resource");
Check(FoodSituationPlanner.Create(empty with{EnabledCapabilities=new HashSet<FoodStrategyKind>()}).ExecutableCandidates.Count==0,"disabled search stays disabled");
var ready=situation with{Self=self with{Inventory=[new(0,88,1)]},Resources=[],Stations=[station with{Heat=2}]};
Check(!FoodSituationPlanner.Create(ready).Candidates.Single(c=>c.Kind==FoodStrategyKind.CookFood).Executable,"even complete supplies do not fabricate cooking executor");
Check(FoodSituationPlanner.Create(ready,new FoodExecutorRegistry([FoodStrategyKind.CookFood])).ExecutableCandidates.Single().Kind==FoodStrategyKind.CookFood,"future host registration extensible without capability-flag bypass");
var prompt=new FoodSituationPrompt(plan);
var response=System.Text.Json.JsonSerializer.Serialize(new{candidate_id=eat.Id.ToString("D")});
Check(prompt.ParseResponse(response)==eat,"prompt selects exact executable offer");
Check(prompt.ParseResponse("{\"candidate_id\":\"wait\"}") is null,"wait is explicit no-op");
Check(!prompt.Prompt.Contains(pig.Evidence.Id.ToString())&&!prompt.Prompt.Contains(cook.Id.ToString()),"diagnostic candidates have no selectable IDs in prompt");
using(var schema=System.Text.Json.JsonDocument.Parse(prompt.ResponseSchema)){
 var choices=schema.RootElement.GetProperty("properties").GetProperty("candidate_id").GetProperty("enum").EnumerateArray().Select(x=>x.GetString()).ToArray();
 Check(choices.Length==plan.ExecutableCandidates.Count+1&&!choices.Contains(cook.Id.ToString()),"grammar excludes unsupported candidate IDs");
}
void BadJson(string json,string name){bool hit=false;try{prompt.ParseResponse(json);}catch(Exception e)when(e is FormatException or ArgumentException or System.Text.Json.JsonException){hit=true;}Check(hit,name);}
BadJson(System.Text.Json.JsonSerializer.Serialize(new{candidate_id=cook.Id.ToString()}),"parser independently refuses diagnostic selection");
BadJson(System.Text.Json.JsonSerializer.Serialize(new{candidate_id=Guid.NewGuid().ToString()}),"invented model ID refused");
BadJson("{\"candidate_id\":\"wait\",\"x\":100}","model cannot add arbitrary coordinate");
BadJson("{\"candidate_id\":\"wait\",\"candidate_id\":\"wait\"}","duplicate fields refused");
BadJson("{\"candidate_id\":1}","numeric ID refused");
BadJson("{\"candidate_id\":\"wait\",\"code\":\"run()\"}","model code field refused");
Check(prompt.ParseResponse(response)==eat,"slow inference can select old offer for mandatory host revalidation");
var waitOnly=new FoodSituationPrompt(FoodSituationPlanner.Create(empty with{EnabledCapabilities=new HashSet<FoodStrategyKind>()}));
Check(waitOnly.ParseResponse("{\"candidate_id\":\"wait\"}") is null,"empty capability set allows only wait");
Check(plan.ExecutableCandidates.Any(c=>c.Kind==FoodStrategyKind.Search)&&plan.ExecutableCandidates.Any(c=>c.Kind==FoodStrategyKind.EatInventory),"search remains available alongside eating");
Check(prompt.Prompt.Contains("HuntVisible")&&prompt.Prompt.Contains("executor_not_integrated"),"visible animal diagnostics not hidden as absent world");
Check(prompt.Prompt.Contains("missing_materials")&&prompt.Prompt.Contains("cook-meat"),"production preparation gaps visible but unselectable");
Check(prompt.Prompt.Contains("health")&&prompt.Prompt.Contains("food")&&plan.Food==.4f,"real observed body state provided for model choice");
var many=situation with{Self=self with{Inventory=Enumerable.Range(0,100).Select(i=>new SlotObservation(i,89,1,.2f,true)).ToArray()},Resources=Enumerable.Range(0,40).Select(i=>pig with{Evidence=Evidence(),Risk=new string('x',128)}).ToArray()};
var manyPlan=FoodSituationPlanner.Create(many);var bounded=new FoodSituationPrompt(manyPlan);
using(var schema=System.Text.Json.JsonDocument.Parse(bounded.ResponseSchema)){
 var shown=schema.RootElement.GetProperty("properties").GetProperty("candidate_id").GetProperty("enum").EnumerateArray().Select(x=>x.GetString()).ToArray();
 Check(shown.Length<=17&&bounded.Prompt.Length<=5600,"worst populated fixture stays within 16 offers and 5600 chars");
 Check(bounded.OmittedExecutableCount>0&&bounded.OmittedDiagnosticCount>=32,"prompt reports omitted counts explicitly");
 Check(shown.Contains(manyPlan.ExecutableCandidates.Single(c=>c.Kind==FoodStrategyKind.Search).Id.ToString()),"many inventory choices cannot hide search alternative");
 var omitted=manyPlan.ExecutableCandidates.First(c=>!shown.Contains(c.Id.ToString()));
 Reject(()=>bounded.ParseResponse(System.Text.Json.JsonSerializer.Serialize(new{candidate_id=omitted.Id.ToString()})),"parser rejects executable but unshown candidate");
}
Console.WriteLine($"{checks}/{checks} food strategy contract tests passed; synthetic trusted observations only, no native hunting/crafting/cooking executed.");
