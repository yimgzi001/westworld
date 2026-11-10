using Capability39;using System.Text.Json;using Westworld.Decisions.Local;
int count=0;void C(bool ok,string n){if(!ok)throw new Exception(n);count++;}
var f=new BodyCapture(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),true,true,true,true,["block"],["entity"],["live"],["memory"],[new(0,10,2),new(1,20,3)],[new("Head",0,30)]);
var caps=BodyFamily.Project(f).ToDictionary(c=>c.Descriptor.Name);
C(caps.Count==11,"all family capabilities");
bool Accept(string tool,object arguments,BodyCapture? capture=null){var c=capture is null?caps[tool]:BodyFamily.Project(capture).Single(c=>c.Descriptor.Name==tool);if(c.Offer is null)return false;var p=new ToolDecisionPrompt(f.Session,f.Actor,f.Capture,JsonSerializer.SerializeToElement(new{}),new[]{c.Offer});try{p.ParseResponse(JsonSerializer.Serialize(new{goal="test",steps=new[]{new{tool,arguments}}}));return true;}catch(FormatException){return false;}}
C(Accept("act_MoveTo",new{bearing=-180,distance=4,range=.4,timeout=15}),"native negative relative bearing");
C(Accept("act_MoveTo",new{destination="memory",range=.75,timeout=15}),"remembered navigation");
C(!Accept("act_MoveTo",new{destination="memory",bearing=0,distance=1,range=.4,timeout=15}),"exclusive move");
C(!Accept("act_MoveTo",new{bearing=0,distance=5,range=.4,timeout=15}),"native distance cap");
C(Accept("act_FaceTowards",new{bearing=-45,pitch=-82,timeout=5}),"negative pitch");C(Accept("act_FaceTowards",new{bearing=20,timeout=5}),"preserve head pitch");
C(!Accept("act_FaceTowards",new{destination="memory",timeout=5}),"no historical aim");
C(Accept("act_SetPosture",new{posture="crouching"}),"posture");C(!Accept("act_SetPosture",new{posture="fly"}),"posture enum");
C(Accept("act_Jump",new{}),"jump input");C(!Accept("act_Jump",new{force=3}),"no jump mechanics changes");
C(Accept("act_ClimbTo",new{target="block",height=70,timeout=15}),"climb actual block");C(!Accept("act_ClimbTo",new{target="entity",height=70,timeout=15}),"climb not entity");
C(Accept("act_Mount",new{target="entity"}),"mount actual entity");C(!Accept("act_Mount",new{target="block"}),"mount not block");
C(Accept("act_Steer",new{turn=-1,throttle=-1,duration=2}),"reverse steer");C(!Accept("act_Steer",new{turn=2,throttle=1,duration=2}),"native steer bound");
C(Accept("act_Dismount",new{}),"dismount");C(!Accept("act_Steer",new{turn=0,throttle=1,duration=2},f with{Mounted=false}),"unmounted unavailable");
C(Accept("read_equipment",new{}),"equipment query");
C(Accept("act_Equip",new{slot=0,value=10}),"owned pair");C(!Accept("act_Equip",new{slot=0,value=20}),"cross-slot mismatch");
C(Accept("act_Unequip",new{equipment_slot="Head",value=30}),"top equipment");C(!Accept("act_Unequip",new{equipment_slot="Head",value=99}),"stale gear");
C(!Accept("act_Equip",new{slot=0,value=10},f with{Inventory=[new(0,99,1)]}),"fresh inventory rejects prior");
C(BodyFamily.Project(f with{Locomotion=null}).Single(c=>c.Descriptor.Name=="act_MoveTo").State.Facts==FactState.Unknown,"unknown component");
C(BodyFamily.Project(f with{Inventory=null}).Single(c=>c.Descriptor.Name=="act_Equip").Offer is null,"unknown inventory");
var layered=f with{Equipment=[new("Head",0,30),new("Head",1,31),new("Torso",0,40)]};
C(Accept("act_Unequip",new{equipment_slot="Head",value=31},layered),"top head available");
C(Accept("act_Unequip",new{equipment_slot="Torso",value=40},layered),"torso paired");
C(!Accept("act_Unequip",new{equipment_slot="Head",value=40},layered),"cross string int pair rejected");
C(!Accept("act_Unequip",new{equipment_slot="Torso",value=31},layered),"reverse cross pair rejected");
C(!Accept("act_Unequip",new{equipment_slot="Head",value=30},layered),"inner layer not removable");
C(!Accept("act_Unequip",new{equipment_slot="Head",value=31},layered with{Equipment=[new("Head",0,30)]}),"fresh equipment rejects previous outer");
C(!Accept("act_Unequip",new{equipment_slot="Head",value="31"},layered),"typed value");
C(!Accept("act_Unequip",new{equipment_slot="Head",value=31,force=true},layered),"closed args");
var over=f with{Inventory=Enumerable.Range(0,33).Select(i=>new OwnSlot(i,100+i,1)).ToArray()};
C(BodyFamily.Project(over).Single(c=>c.Descriptor.Name=="act_Equip").State.Facts==FactState.Unknown,"oversized association explicit unknown");
var registry=new CapabilityRegistry(caps.Values.Select(c=>c.Descriptor));var capture=registry.Capture(f.Session,f.Actor,f.Capture,caps.Values.Select(c=>c.State));C(capture.Discover(f.Session,f.Actor,f.Capture).Entries.Count==8,"registry compatibility");
try{BodyFamily.Project(f with{Inventory=[new(0,10,2),new(0,20,2)]});throw new Exception("duplicate accepted");}catch(ArgumentException){count++;}
Console.WriteLine($"PASS {count}/{count}; pure schema/registry only, no native effects");
