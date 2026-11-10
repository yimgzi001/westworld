using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public sealed record ObservedAlignmentRoute(Guid Id,Guid SessionId,Guid ActorId,Guid ObservationId,
 double ObservedAt,Position Origin,Position Destination,IReadOnlyList<LocalObservedCell> Evidence,double MaximumAge);

/// <summary>Skill-internal same-level centering, not an exploration decision or global route.
/// The caller chooses when to align and reobserves before executing. Frozen evidence cannot
/// guarantee future clearance; native collision and bounded interruption remain mandatory.</summary>
public static class LocalNavigationAlignment {
 public static ObservedAlignmentRoute? Project(LocalObservationWindow window,bool allowObservedSandAndGravel=false) {
  ArgumentNullException.ThrowIfNull(window);var p=window.Position;
  if(window.SessionId==Guid.Empty||window.ActorId==Guid.Empty||window.ObservationId==Guid.Empty
   ||!double.IsFinite(window.Time)||window.Time<0||!float.IsFinite(window.Health)||window.Health<=0
   ||!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z)
   ||Math.Abs(p.X)>10_000_000||Math.Abs(p.Z)>10_000_000||p.Y<1||p.Y>253
   ||Math.Abs(p.Y-Math.Round(p.Y))>.05||window.Cells.Count>4096)return null;
  var target=new Position(Math.Floor(p.X)+.5,p.Y,Math.Floor(p.Z)+.5);
  double length=Math.Sqrt(Math.Pow(target.X-p.X,2)+Math.Pow(target.Z-p.Z,2));
  if(length<.05-1e-9||length>.7+1e-9)return null;
  var cells=new Dictionary<(int,int,int),LocalObservedCell>();
  foreach(var cell in window.Cells)if(cell is null||!cells.TryAdd((cell.X,cell.Y,cell.Z),cell))return null;
  var proof=new List<LocalObservedCell>();int feet=(int)Math.Round(p.Y);
  int minX=(int)Math.Floor(Math.Min(p.X,target.X)-.25+1e-5),maxX=(int)Math.Floor(Math.Max(p.X,target.X)+.25-1e-5);
  int minZ=(int)Math.Floor(Math.Min(p.Z,target.Z)-.25+1e-5),maxZ=(int)Math.Floor(Math.Max(p.Z,target.Z)+.25-1e-5);
  int top=(int)Math.Floor(Math.Max(p.Y,feet)+1.9-1e-5);
  for(int x=minX;x<=maxX;x++)for(int z=minZ;z<=maxZ;z++) {
   if(!Need(x,feet-1,z,true))return null;
   for(int y=feet;y<=top;y++)if(!Need(x,y,z,false))return null;
  }
  return new(Guid.NewGuid(),window.SessionId,window.ActorId,window.ObservationId,window.Time,p,target,
   Array.AsReadOnly(proof.ToArray()),proof.Max(c=>c.Age));
  bool Need(int x,int y,int z,bool ground) {
   if(!cells.TryGetValue((x,y,z),out var c)||c.Value<0||c.SourceObservationId==Guid.Empty
    ||!double.IsFinite(c.ObservedAt)||c.ObservedAt<0||!double.IsFinite(c.Age)||c.Age<0||c.Age>3
    ||Math.Abs(window.Time-c.ObservedAt-c.Age)>1e-6
    ||(c.IsCurrent&&(c.SourceObservationId!=window.ObservationId||c.Age!=0)))return false;
   int content=c.Value&1023;
   if(ground?!(content is 1 or 2 or 3 or 4 or 5 or 8 || allowObservedSandAndGravel && content is 6 or 7):content!=0)return false;
   proof.Add(c);return true;
  }
 }
}
