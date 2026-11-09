// Pure monotonic deadline predicate, called AFTER validation and immediately before first dispatch.
public static class DecisionDispatchWindow {
 public static double? ForPlan(int steps,double deadline){if(steps<0||!double.IsFinite(deadline))throw new ArgumentException("Invalid plan deadline");return steps==0?null:deadline;}
 public static bool IsOpen(double now,double deadline)=>double.IsFinite(now)&&double.IsFinite(deadline)&&now>=0&&now<deadline;
}
