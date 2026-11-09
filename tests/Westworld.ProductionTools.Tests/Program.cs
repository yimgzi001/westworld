using Game.Westworld;
using System.Text.Json;
using Westworld.Scapi.Adapter;
int checks=0;void Check(bool v,string name){checks++;if(!v)throw new Exception(name);}
JsonElement J(string s)=>JsonSerializer.Deserialize<JsonElement>(s);
ActorToolDefinition[] defs=[new("act_TransferItems","native description",J("{\"type\":\"object\",\"additionalProperties\":false}")),new("find_recipes","native",J("{}")),new("act_Wait","native",J("{}")),new("unknown_code","not exposed",J("{}"))];
var selected=NativeProductionTools.Select(defs);
Check(selected.Count==3,"only available native subset");Check(selected[0].Parameters.GetRawText()==defs[0].Parameters.GetRawText(),"native schema unchanged");Check(selected[0].Description==defs[0].Description,"description unchanged");
Check(NativeProductionTools.Select([]).Count==0,"no manufactured capability");
foreach(var name in new[]{"observe","read_self","read_inventory","read_storage","read_item","find_items","find_recipes","plan_craft","act_Craft","act_TransferItems","act_Place","act_SelectItem","act_Wait","act_Eat","action_status","task_status","cancel_action","cancel_task"})Check(NativeProductionTools.Contains(name),name);
Check(!NativeProductionTools.Contains("smelt"),"no fabricated smelt action");Check(!NativeProductionTools.Contains("AddSlotItems"),"no direct inventory writes");Check(!NativeProductionTools.Contains("exec"),"no arbitrary code profile");
var refs=NativeProductionTools.WorkspaceReferences(J("{\"craftingReference\":\"own-issued\",\"focus\":{\"storageReference\":\"visible-issued\"}}"));Check(refs.Own=="own-issued"&&refs.Visible=="visible-issued","issued refs only");
Check(NativeProductionTools.WorkspaceReferences(J("{\"craftingReference\":\"own\",\"focus\":null}")).Visible is null,"no visible workspace");
Check(NativeProductionTools.WorkspaceReferences(J("{}")).Own is null,"missing own retained");
Check(NativeProductionTools.WorkspaceReferences(J("{\"craftingReference\":12,\"focus\":{\"storageReference\":false}}")).Visible is null,"malformed ref not invented");
bool bad=false;try{NativeProductionTools.WorkspaceReferences(J("[]"));}catch(ArgumentException){bad=true;}Check(bad,"reject wrong observation shape");
Console.WriteLine($"{checks}/{checks} passed (pure profile only; no native world)");
