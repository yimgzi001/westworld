using Game;
using System.Text.Json;

// Current-menu Eat regression: artificial cooked meat only. No hunger, inventory
// result, eating statistic or native event is overwritten to force success.
internal sealed partial class ToolAgentScenario {
 int eat44Value=CookedMeatBlock.Index,eat44Events,eat44ForeignEvents;
 float eat44FoodBefore,eat44FoodAtEvent;
 double eat44StatsBefore;
 bool eat44Subscribed;
 void Eat44Event(int value){
  if(value!=eat44Value){eat44ForeignEvents++;return;}
  eat44Events++;eat44FoodAtEvent=p.ComponentVitalStats.Food;
  ToolAgentGate.Log(new{kind="eat44_native_event",value,ordinal=eat44Events,inventoryAtEvent=ExactCount(value),foodAtEvent=eat44FoodAtEvent,statisticAtEvent=p.PlayerStats.FoodItemsEaten,note="Native event precedes statistic increment; final statistic checked after action.",modelApi=false});
 }
 void Eat44Prepare(){
  Matrix40Check(!eat44Subscribed&&ExactCount(eat44Value)==0,"eat44_no_existing_same_value_food");
  var slot=Matrix40Read("read_inventory",new{}).GetProperty("slots").EnumerateArray().Last(s=>s.GetProperty("count").GetInt32()==0).GetProperty("slot").GetInt32();
  p.ComponentMiner.Inventory.AddSlotItems(slot,eat44Value,1);
  Matrix40Check(ExactCount(eat44Value)==1,"eat44_exact_one_artificial_cooked_meat");
  eat44Events=0;eat44ForeignEvents=0;p.ComponentVitalStats.FoodEaten+=Eat44Event;eat44Subscribed=true;
  ToolAgentGate.Log(new{kind="eat44_fixture",artificial=true,inventoryInjected=true,slot,value=eat44Value,count=1,hungerEdited=false,statisticsEdited=false,nativeEventInjected=false,modelApi=false});
 }
 object Eat44Args(){
  var inventory=Matrix40Read("read_inventory",new{});var slot=inventory.GetProperty("slots").EnumerateArray().Single(s=>s.GetProperty("value").GetInt32()==eat44Value&&s.GetProperty("count").GetInt32()==1);
  eat44FoodBefore=p.ComponentVitalStats.Food;eat44StatsBefore=p.PlayerStats.FoodItemsEaten;
  Matrix40Check(eat44FoodBefore<.98f,"eat44_native_hunger_available_without_edit");
  ToolAgentGate.Log(new{kind="eat44_before",inventoryCount=1,value=eat44Value,food=eat44FoodBefore,statistic=eat44StatsBefore,publicSelf=Matrix40Read("read_self",new{}),modelApi=false});
  return new{slot=slot.GetProperty("slot").GetInt32(),value=slot.GetProperty("value").GetInt32(),count=1};
 }
 void Eat44Verify(){
  var receipt=Matrix40Read("action_status",new{});var self=Matrix40Read("read_self",new{});
  Matrix40Check(receipt.GetProperty("action").GetString()=="Eat"&&receipt.GetProperty("affectedCount").GetInt32()==1,"eat44_native_eat_affected_one");
  Matrix40Check(eat44Events==1&&eat44ForeignEvents==0,"eat44_exact_matching_native_food_event");
  Matrix40Check(ExactCount(eat44Value)==0,"eat44_actual_inventory_one_to_zero");
  Matrix40Check(p.PlayerStats.FoodItemsEaten==eat44StatsBefore+1,"eat44_actual_food_statistic_increased_one");
  Matrix40Check(eat44FoodAtEvent>eat44FoodBefore&&p.ComponentVitalStats.Food>eat44FoodBefore&&self.GetProperty("food").GetSingle()>eat44FoodBefore,"eat44_native_and_public_food_increased");
  ToolAgentGate.Log(new{kind="eat44_effect",receipt,foodBefore=eat44FoodBefore,foodAtEvent=eat44FoodAtEvent,foodAfter=p.ComponentVitalStats.Food,publicFoodAfter=self.GetProperty("food").GetSingle(),inventoryBefore=1,inventoryAfter=0,statisticBefore=eat44StatsBefore,statisticAfter=p.PlayerStats.FoodItemsEaten,nativeEvents=eat44Events,foreignEvents=eat44ForeignEvents,artificial=true,modelApi=false});
  DisposeEat44();
 }
 void Matrix44EnqueueEat(){
  Matrix40Enqueue("act_Wait",()=>new{duration=.1},Eat44Prepare);
  Matrix40Enqueue("read_inventory",()=>new{});
  Matrix40Enqueue("act_Eat",Eat44Args,Eat44Verify);
 }
 void DisposeEat44(){if(!eat44Subscribed)return;p.ComponentVitalStats.FoodEaten-=Eat44Event;eat44Subscribed=false;}
}
