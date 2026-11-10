namespace Westworld.Scapi.Adapter;

/// <summary>Observed body preconditions only. Ready is not permission to cross unseen terrain.</summary>
public sealed record NavigationReadiness(bool Ready, string[] Blockers, bool Grounded,
 bool Attached, double? Immersion, double? VerticalVelocity, double? HorizontalSpeed, double? Health) {
 public static NavigationReadiness Evaluate(bool grounded,bool attached,double immersion,double verticalVelocity,double horizontalSpeed,double health) {
  var reasons=new List<string>();
  bool valid=double.IsFinite(immersion)&&immersion>=0&&double.IsFinite(verticalVelocity)&&double.IsFinite(horizontalSpeed)&&horizontalSpeed>=0&&double.IsFinite(health);
  if(!valid)reasons.Add("invalid_body_sample");
  if(!grounded)reasons.Add("not_grounded");
  if(attached)reasons.Add("attached_to_parent");
  if(double.IsFinite(immersion)&&immersion>.1)reasons.Add("immersed");
  if(double.IsFinite(verticalVelocity)&&Math.Abs(verticalVelocity)>.1)reasons.Add("vertical_motion");
  if(double.IsFinite(horizontalSpeed)&&horizontalSpeed>.1)reasons.Add("horizontal_motion");
  if(double.IsFinite(health)&&health<=0)reasons.Add("not_alive");
  return new(reasons.Count==0,reasons.ToArray(),grounded,attached,Finite(immersion),Finite(verticalVelocity),Finite(horizontalSpeed),Finite(health));
 }
 static double? Finite(double value)=>double.IsFinite(value)?value:null;
}
public sealed class NavigationNotReadyException : InvalidOperationException {
 public NavigationReadiness Readiness {get;}
 public NavigationNotReadyException(NavigationReadiness readiness):base("Navigation body prerequisites not satisfied: "+string.Join(",",readiness.Blockers)){Readiness=readiness;}
}
public static class NavigationCompletionSemantics {
 public const string Jump="Jump success confirms jump input execution, not landing or stable ground. Navigation requires a fresh readiness sample; no automatic wait or retry is implied.";
 public const string Navigation="Arrived requires the observed local route and settled completion; failed navigation may still have made partial progress. Path is traveled distance, Remaining is endpoint distance.";
}
