using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public sealed record StepEnvelopeCheck(bool Inside,bool KnownSupportContactUsed,string Reason);
/// <summary>Frozen observed envelope only; current obstacles remain native collision's responsibility.</summary>
public static class ObservedStepGuard {
 public static StepEnvelopeCheck Check(ObservedStepRoute route,Position minimum,Position maximum,bool smoothRise,float maximumSmoothRise) {
  ArgumentNullException.ThrowIfNull(route);
  if(!Finite(minimum)||!Finite(maximum)||maximum.X<minimum.X||maximum.Y<minimum.Y||maximum.Z<minimum.Z
   ||maximum.X-minimum.X>.501||maximum.Z-minimum.Z>.501||maximum.Y-minimum.Y>1.901
   ||!float.IsFinite(maximumSmoothRise)||maximumSmoothRise<0)return new(false,false,"invalid_body_bounds");
  var cells=route.Evidence.ToDictionary(c=>(c.X,c.Y,c.Z));bool used=false;
  for(int x=(int)Math.Floor(minimum.X+1e-5);x<=(int)Math.Floor(maximum.X-1e-5);x++)
   for(int z=(int)Math.Floor(minimum.Z+1e-5);z<=(int)Math.Floor(maximum.Z-1e-5);z++)
    for(int y=(int)Math.Floor(minimum.Y+1e-5);y<=(int)Math.Floor(maximum.Y-1e-5);y++) {
     if(!cells.TryGetValue((x,y,z),out var cell))return new(false,used,"outside_observed_envelope");
     int contents=cell.Value&1023;if(contents==0)continue;
     bool ground=contents is 1 or 2 or 3 or 4 or 5 or 8;
     bool up=route.Kind==StepKind.Up&&smoothRise&&ground&&y==(int)Math.Round(route.Destination.Y)-1
       &&x==(int)Math.Floor(route.Destination.X)&&z==(int)Math.Floor(route.Destination.Z)
       &&minimum.Y>=route.Destination.Y-Math.Min(maximumSmoothRise,.5f);
     bool down=route.Kind==StepKind.Down&&ground&&y==(int)Math.Round(route.Origin.Y)-1&&minimum.Y>=route.Origin.Y-.02;
     if(!up&&!down)return new(false,used,"solid_outside_support_contact");used=true;
    }
  return new(true,used,used?"known_support_contact":"observed_air");
 }
 static bool Finite(Position p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&Math.Abs(p.X)<10_000_001&&Math.Abs(p.Z)<10_000_001&&p.Y>=0&&p.Y<=256;
}
