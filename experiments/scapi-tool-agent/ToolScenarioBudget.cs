// Experiment lifecycle only: does not choose model goals or tools.
public sealed record ToolScenarioBudget(int MaxRequests,double WallSeconds,double RequestSeconds){
 public const double DrainSeconds=7;
 public static bool TerminalStatus(string status,bool task)=>task?status is "idle" or "succeeded" or "failed" or "cancelled" or "skipped":status is "succeeded" or "failed" or "cancelled";
 public static bool DirectReceiptTerminal(bool returned,bool typedNull,string status,bool nativeRunning)=>returned&&!nativeRunning&&(typedNull||TerminalStatus(status,false));
 public static double RemainingDrain(double stopped,double now)=>double.IsFinite(stopped)&&double.IsFinite(now)?Math.Max(0,DrainSeconds-Math.Max(0,now-stopped)):0;
 public static ToolScenarioBudget For(string mode)=>mode=="food_drop"?new(4,180,45):new(12,595,120);
 public double Remaining(double elapsed)=>Math.Max(0,WallSeconds-elapsed);
 public double RequestWindow(double elapsed,bool prefetch=false)=>Math.Min(Remaining(elapsed),prefetch?Math.Min(45,RequestSeconds):RequestSeconds);
 public bool CanRequest(int issued,double elapsed)=>issued>=0&&issued<MaxRequests&&RequestWindow(elapsed)>0;
}
// Conservative evidence contract. World disappearance alone is never collection.
public static class FoodAcquisitionEvidence{
 public static bool Acquired(int initialOwned,int owned,int spawnedCount,bool originalGone,int originalRemaining,bool competingSource)=>
  initialOwned==0&&spawnedCount>0&&originalGone&&originalRemaining>=0&&originalRemaining<=spawnedCount&&owned==spawnedCount&&!competingSource;
 public static bool Eaten(bool acquired,int expectedValue,int actualValue,int beforeCount,int afterCount,long beforeStats,long afterStats,float beforeFood,float afterFood,bool nativeFoodEvent)=>
  acquired&&expectedValue==actualValue&&beforeCount>0&&afterCount==beforeCount-1&&afterStats==beforeStats+1&&float.IsFinite(beforeFood)&&float.IsFinite(afterFood)&&afterFood>beforeFood&&nativeFoodEvent;
}
