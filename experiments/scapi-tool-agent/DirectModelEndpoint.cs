// Artificial endpoint fixture acceptance, not a model policy or tool selection rule.
public static class DirectModelEndpoint {
 public const string Goal="Move approximately 4 metres toward absolute +X from your starting position and stop near that destination while preserving your health. Choose available tools from actual observations.";
 public static bool Reached(double x,double y,double z,double speed,double health,
  bool prepared,bool busy,bool quarantined,bool hasView)=>
  prepared&&!busy&&!quarantined&&!hasView&&double.IsFinite(x)&&double.IsFinite(y)&&double.IsFinite(z)&&
  double.IsFinite(speed)&&double.IsFinite(health)&&x>=3.2&&x<=4.8&&Math.Abs(y)<=.5&&Math.Abs(z)<=.5&&speed>=0&&speed<.1&&health>=.25;
}
