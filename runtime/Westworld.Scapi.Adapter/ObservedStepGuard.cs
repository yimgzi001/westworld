using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public sealed record StepEnvelopeCheck(bool Inside,bool KnownSupportContactUsed,string Reason);
/// <summary>Frozen observed envelope only; current obstacles remain native collision's responsibility.</summary>
public static class ObservedStepGuard {
 const double RiserSeamTolerance=.005;
 public static StepEnvelopeCheck Check(ObservedStepRoute route,Position minimum,Position maximum,bool smoothRise,float maximumSmoothRise,
  Func<int,bool>? openCell=null) {
  ArgumentNullException.ThrowIfNull(route);
  if(!Finite(minimum)||!Finite(maximum)||maximum.X<minimum.X||maximum.Y<minimum.Y||maximum.Z<minimum.Z
   ||maximum.X-minimum.X>.501||maximum.Z-minimum.Z>.501||maximum.Y-minimum.Y>1.901
   ||!float.IsFinite(maximumSmoothRise)||maximumSmoothRise<0)return new(false,false,"invalid_body_bounds");
  var cells=route.Evidence.ToDictionary(c=>(c.X,c.Y,c.Z));bool used=false;
  for(int x=(int)Math.Floor(minimum.X+1e-5);x<=(int)Math.Floor(maximum.X-1e-5);x++)
   for(int z=(int)Math.Floor(minimum.Z+1e-5);z<=(int)Math.Floor(maximum.Z-1e-5);z++)
    for(int y=(int)Math.Floor(minimum.Y+1e-5);y<=(int)Math.Floor(maximum.Y-1e-5);y++) {
     if(!cells.TryGetValue((x,y,z),out var cell))return new(false,used,"outside_observed_envelope");
     int contents=cell.Value&1023;if(openCell?.Invoke(cell.Value)??contents==0)continue;
     bool ground=ObservedStepRoutes.Ground(contents,route.AllowsLooseGround);
     bool up=route.Kind==StepKind.Up&&smoothRise&&ground&&y==(int)Math.Round(route.Destination.Y)-1
       &&x==(int)Math.Floor(route.Destination.X)&&z==(int)Math.Floor(route.Destination.Z)
       &&(minimum.Y>=route.Destination.Y-Math.Min(maximumSmoothRise,.5f)
          ||minimum.Y>=route.Origin.Y-.02&&AtLaunchSideOfRiser(route,minimum,maximum,x,z));
     bool down=route.Kind==StepKind.Down&&ground&&y==(int)Math.Round(route.Origin.Y)-1&&minimum.Y>=route.Origin.Y-.02;
     if(!up&&!down)return new(false,used,"solid_outside_support_contact");used=true;
    }
  return new(true,used,used?"known_support_contact":"observed_air");
 }
 // 原生贴台阶起跳时碰撞箱有极小的浮点越界。只允许接触已观察到的目标台阶迎面。
 static bool AtLaunchSideOfRiser(ObservedStepRoute route,Position minimum,Position maximum,int x,int z) =>
  (x-(int)Math.Floor(route.Origin.X),z-(int)Math.Floor(route.Origin.Z)) switch {
   (1,0)=>maximum.X-x<=RiserSeamTolerance,
   (-1,0)=>x+1-minimum.X<=RiserSeamTolerance,
   (0,1)=>maximum.Z-z<=RiserSeamTolerance,
   (0,-1)=>z+1-minimum.Z<=RiserSeamTolerance,
   _=>false
  };
 static bool Finite(Position p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&Math.Abs(p.X)<10_000_001&&Math.Abs(p.Z)<10_000_001&&p.Y>=0&&p.Y<=256;
}
