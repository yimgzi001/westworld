using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;
public static class LocalNavigationSkillGeometry {
 public static bool InObservedAir(IReadOnlyList<LocalObservedCell> evidence,Position min,Position max,Func<int,bool>? openCell=null,
  Func<LocalObservedCell,bool>? additionalOpenCell=null) {
  if(!Valid(min)||!Valid(max)||max.X<min.X||max.Y<min.Y||max.Z<min.Z||max.X-min.X>.501||max.Z-min.Z>.501||max.Y-min.Y>1.901)return false;
  if(evidence.Any(c=>c.Value<0||c.SourceObservationId==Guid.Empty||!double.IsFinite(c.Age)||c.Age<0||c.Age>3)
    ||evidence.Select(c=>(c.X,c.Y,c.Z)).Distinct().Count()!=evidence.Count)return false;
  var air=evidence.Where(c=>(openCell?.Invoke(c.Value)??(c.Value&1023)==0)||additionalOpenCell?.Invoke(c)==true)
   .Select(c=>(c.X,c.Y,c.Z)).ToHashSet();
  for(int x=(int)Math.Floor(min.X+1e-5);x<=(int)Math.Floor(max.X-1e-5);x++)
   for(int z=(int)Math.Floor(min.Z+1e-5);z<=(int)Math.Floor(max.Z-1e-5);z++)
    for(int y=(int)Math.Floor(min.Y+1e-5);y<=(int)Math.Floor(max.Y-1e-5);y++)if(!air.Contains((x,y,z)))return false;
  return true;
 }
 static bool Valid(Position p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&Math.Abs(p.X)<1e7&&Math.Abs(p.Z)<1e7&&p.Y>=0&&p.Y<=256;
 public static IReadOnlyList<NavigationMissingCell> MissingCells(LocalObservationWindow w,bool allowObservedSandAndGravel=false){
  int fx=(int)Math.Floor(w.Position.X),fy=(int)Math.Round(w.Position.Y),fz=(int)Math.Floor(w.Position.Z);var needed=new List<NavigationMissingCell>();
  int minX=(int)Math.Floor(w.Position.X-.25+1e-5),maxX=(int)Math.Floor(w.Position.X+.25-1e-5),minZ=(int)Math.Floor(w.Position.Z-.25+1e-5),maxZ=(int)Math.Floor(w.Position.Z+.25-1e-5);
  for(int x=minX;x<=maxX;x++)for(int z=minZ;z<=maxZ;z++){needed.Add(new(x,fy-1,z,NavigationLookStage.CurrentSupport));for(int y=fy;y<=fy+1;y++)needed.Add(new(x,y,z,NavigationLookStage.CurrentBody));}
  bool upSeen=false;var known=w.Cells.ToDictionary(c=>(c.X,c.Y,c.Z));
  foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)}) {
   int x=fx+dx,z=fz+dz;bool raised=known.TryGetValue((x,fy,z),out var riser)&&
    ((riser.Value&1023) is 1 or 2 or 3 or 4 or 5 or 8 || allowObservedSandAndGravel&&(riser.Value&1023) is 6 or 7);
   bool lower=known.TryGetValue((x,fy-1,z),out var floor)&&(floor.Value&1023)==0;
   if(raised){upSeen=true;for(int y=fy+1;y<=fy+3;y++)needed.Add(new(x,y,z,NavigationLookStage.HeadClearance));}
   else {needed.Add(new(x,lower?fy-2:fy-1,z,NavigationLookStage.ForwardSupport));for(int y=lower?fy-1:fy;y<=fy+1;y++)needed.Add(new(x,y,z,NavigationLookStage.ForwardSupport));}
  }
  if(upSeen)for(int y=fy+2;y<=fy+3;y++)needed.Add(new(fx,y,fz,NavigationLookStage.HeadClearance));
  return needed.Where(c=>c.Y>=0&&c.Y<256&&Math.Pow(c.X+.5-w.Position.X,2)+Math.Pow(c.Y+.5-w.Position.Y,2)+Math.Pow(c.Z+.5-w.Position.Z,2)<=25).DistinctBy(c=>(c.X,c.Y,c.Z)).ToArray();
 }
}
