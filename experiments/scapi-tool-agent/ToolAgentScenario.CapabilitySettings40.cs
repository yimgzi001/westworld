using Engine;
using Game;
using System.Text.Json;

// Artificial representative settings fixture. Never called in natural survival.
// All reads and configuration actions are queued through the matrix's current
// capability menu validation and native gateway, not applied as direct effects.
internal sealed partial class ToolAgentScenario {
 Point3 matrix40SettingsCell;
 int matrix40SettingsItemSlot=-1;
 int matrix40SettingsItemValue;
 string Matrix40SettingsTarget()=>Matrix40Block(matrix40SettingsCell.X,matrix40SettingsCell.Y,matrix40SettingsCell.Z);
 JsonElement Matrix40SettingsRead()=>Matrix40Read("read_block_settings",new{target=Matrix40SettingsTarget()}).GetProperty("settings");
 void Matrix40SettingsPlace(int value,string fixture){
  var forward=p.ComponentBody.Matrix.Forward;var pos=p.ComponentBody.Position+2*new Vector3(forward.X,0,forward.Z);
  matrix40SettingsCell=new(Terrain.ToCell(pos.X),Terrain.ToCell(p.ComponentBody.Position.Y),Terrain.ToCell(pos.Z));
  Matrix40Set(matrix40SettingsCell.X,matrix40SettingsCell.Y,matrix40SettingsCell.Z,value);
  ToolAgentGate.Log(new{kind="matrix40_settings_fixture",fixture,artificial=true,worldEdited=true,inventoryInjected=false,cell=new[]{matrix40SettingsCell.X,matrix40SettingsCell.Y,matrix40SettingsCell.Z},value,modelApi=false});
 }
 void Matrix40EnqueueSettings(){
  Matrix40Enqueue("act_FaceTowards",()=>new{bearing=0,pitch=0,timeout=4},()=>Matrix40SettingsPlace(Terrain.MakeBlockValue(BatteryBlock.Index,0,BatteryBlock.SetVoltageLevel(0,1)),"battery_voltage_1"));
  Matrix40Enqueue("act_Wait",()=>new{duration=.25});
  Matrix40Enqueue("read_block_settings",()=>new{target=Matrix40SettingsTarget()},()=>Matrix40Check(Matrix40SettingsRead().GetProperty("voltage").GetInt32()==1,"battery_initial_native_voltage_1"));
  Matrix40Enqueue("act_ConfigureBlock",()=>new{target=Matrix40SettingsTarget(),settings=new{voltage=11}});
  Matrix40Enqueue("read_block_settings",()=>new{target=Matrix40SettingsTarget()},()=>Matrix40Check(Matrix40SettingsRead().GetProperty("voltage").GetInt32()==11,"battery_configuration_native_effect_11"));
  Matrix40Enqueue("act_Wait",()=>new{duration=.1},()=>{
   int data=PistonBlock.SetMode(0,PistonMode.Pulling);data=PistonBlock.SetMaxExtension(data,0);data=PistonBlock.SetPullCount(data,0);data=PistonBlock.SetSpeed(data,0);
   Matrix40SettingsPlace(Terrain.MakeBlockValue(PistonBlock.Index,0,data),"pulling_piston_initial_1_1_0");
  });
  Matrix40Enqueue("read_block_settings",()=>new{target=Matrix40SettingsTarget()},()=>{
   var s=Matrix40SettingsRead();Matrix40Check(s.GetProperty("mode").GetString()=="Pulling"&&s.GetProperty("maxExtension").GetInt32()==1&&s.GetProperty("pullCount").GetInt32()==1&&s.GetProperty("speed").GetInt32()==0,"piston_initial_native_settings");
  });
  Matrix40Enqueue("act_ConfigureBlock",()=>new{target=Matrix40SettingsTarget(),settings=new{maxExtension=5,pullCount=3,speed=2}});
  Matrix40Enqueue("read_block_settings",()=>new{target=Matrix40SettingsTarget()},()=>{
   var s=Matrix40SettingsRead();Matrix40Check(s.GetProperty("mode").GetString()=="Pulling"&&s.GetProperty("maxExtension").GetInt32()==5&&s.GetProperty("pullCount").GetInt32()==3&&s.GetProperty("speed").GetInt32()==2,"piston_configuration_native_effect_5_3_2");
  });
  Matrix40Enqueue("act_Wait",()=>new{duration=.1},()=>{
   var inventory=Matrix40Read("read_inventory",new{});matrix40SettingsItemSlot=inventory.GetProperty("slots").EnumerateArray().Where(s=>s.GetProperty("count").GetInt32()==0).Select(s=>s.GetProperty("slot").GetInt32()).Last();
   matrix40SettingsItemValue=Terrain.MakeBlockValue(BatteryBlock.Index,0,BatteryBlock.SetVoltageLevel(0,2));p.ComponentMiner.Inventory.AddSlotItems(matrix40SettingsItemSlot,matrix40SettingsItemValue,1);
   ToolAgentGate.Log(new{kind="matrix40_settings_fixture",fixture="owned_battery_voltage_2",artificial=true,worldEdited=false,inventoryInjected=true,slot=matrix40SettingsItemSlot,value=matrix40SettingsItemValue,count=1,modelApi=false});
  });
  Matrix40Enqueue("read_item_settings",()=>new{slot=matrix40SettingsItemSlot,value=matrix40SettingsItemValue},()=>Matrix40Check(Matrix40Read("read_item_settings",new{slot=matrix40SettingsItemSlot,value=matrix40SettingsItemValue}).GetProperty("settings").GetProperty("voltage").GetInt32()==2,"owned_battery_initial_voltage_2"));
  Matrix40Enqueue("act_ConfigureItem",()=>new{slot=matrix40SettingsItemSlot,value=matrix40SettingsItemValue,settings=new{voltage=9}},()=>{
   var current=Matrix40Read("read_inventory",new{}).GetProperty("slots").EnumerateArray().Single(s=>s.GetProperty("slot").GetInt32()==matrix40SettingsItemSlot);
   Matrix40Check(current.GetProperty("count").GetInt32()==1&&current.GetProperty("value").GetInt32()!=matrix40SettingsItemValue,"owned_config_changed_value_preserved_count");matrix40SettingsItemValue=current.GetProperty("value").GetInt32();
  });
  Matrix40Enqueue("read_item_settings",()=>new{slot=matrix40SettingsItemSlot,value=matrix40SettingsItemValue},()=>Matrix40Check(Matrix40Read("read_item_settings",new{slot=matrix40SettingsItemSlot,value=matrix40SettingsItemValue}).GetProperty("settings").GetProperty("voltage").GetInt32()==9,"owned_battery_configuration_native_effect_9"));
 }
}
