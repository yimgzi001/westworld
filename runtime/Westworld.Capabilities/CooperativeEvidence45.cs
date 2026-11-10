namespace Capability39.Cooperation;

// Audit-only values. These records do not grant target references or supply model facts.
public readonly record struct CoopBinding45(Guid World,Guid Session,Guid Actor,Guid Request) {
 public void Validate(){if(World==Guid.Empty||Session==Guid.Empty||Actor==Guid.Empty||Request==Guid.Empty)throw new ArgumentException("Complete world/session/actor/request binding required");}
}
public sealed record CoopActionEvidence45(CoopBinding45 Binding,string Tool,Guid NativeRunId,double StartedAt,double EndedAt,string Status,int AffectedCount);
public sealed record CoopInventory45(Guid World,Guid ActorA,Guid ActorB,int ItemValue,int CountA,int CountB);
public static class CooperativeEvidence45 {
 static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
 public static void ValidateIdentity(CoopBinding45 expected,CoopBinding45 actual){expected.Validate();actual.Validate();Require(expected==actual,"cooperative_receipt_binding_mismatch");}
 static void Action(CoopActionEvidence45 e){e.Binding.Validate();Require(e.NativeRunId!=Guid.Empty,"missing_native_run_id");Require(double.IsFinite(e.StartedAt)&&double.IsFinite(e.EndedAt)&&e.StartedAt>=0&&e.EndedAt>=e.StartedAt,"invalid_monotonic_interval");Require(e.Status=="succeeded"&&e.AffectedCount>=0,"action_not_successful");}
 static void Inventories(CoopInventory45 before,CoopInventory45 after){
  Require(before.World!=Guid.Empty&&before.ActorA!=Guid.Empty&&before.ActorB!=Guid.Empty&&before.ActorA!=before.ActorB&&before.ItemValue>0,"invalid_inventory_scope");
  Require(before.World==after.World&&before.ActorA==after.ActorA&&before.ActorB==after.ActorB&&before.ItemValue==after.ItemValue,"inventory_scope_changed");
  Require(before.CountA>=0&&before.CountB>=0&&after.CountA>=0&&after.CountB>=0,"negative_inventory_count");
 }
 // Explicitly A -> B. Caller must isolate unrelated mutations and audit dropped items separately.
 public static void ValidateDelivery(CoopInventory45 before,CoopInventory45 after,int transferred){
  Inventories(before,after);Require(transferred>0,"no_actual_delivery");
  Require((long)before.CountA-after.CountA==transferred&&(long)after.CountB-before.CountB==transferred,"delivery_inventory_deltas_mismatch");
  Require((long)before.CountA+before.CountB==(long)after.CountA+after.CountB,"delivery_not_conserved");
 }
 public static void ValidateEating(CoopInventory45 before,CoopInventory45 after,Guid eaterActor,int affectedCount){
  Inventories(before,after);Require(affectedCount>0,"no_native_food_consumption");Require(eaterActor==before.ActorA||eaterActor==before.ActorB,"eater_not_in_inventory_scope");
  bool a=eaterActor==before.ActorA;
  Require(a?(long)before.CountA-after.CountA==affectedCount&&before.CountB==after.CountB:(long)before.CountB-after.CountB==affectedCount&&before.CountA==after.CountA,"food_consumption_delta_mismatch");
 }
 // A single-clock host sample must establish Wait still running at/after Eat completion.
 // This validates overlap of native actions, not concurrent model inference or autonomous cooperation.
 public static void ValidateOverlap(CoopActionEvidence45 wait,CoopActionEvidence45 eat,double waitObservedRunningAt){
  Action(wait);Action(eat);
  Require(wait.Tool=="act_Wait"&&eat.Tool=="act_Eat","unexpected_overlap_tools");
  Require(wait.Binding.World==eat.Binding.World&&wait.Binding.Actor!=eat.Binding.Actor&&wait.Binding.Session!=eat.Binding.Session&&wait.Binding.Request!=eat.Binding.Request&&wait.NativeRunId!=eat.NativeRunId,"overlap_owners_not_independent");
  Require(eat.AffectedCount>0,"eat_has_no_effect");
  Require(double.IsFinite(waitObservedRunningAt)&&wait.StartedAt<=eat.StartedAt&&eat.EndedAt<=waitObservedRunningAt&&waitObservedRunningAt<wait.EndedAt,"native_execution_overlap_not_proven");
 }
}
