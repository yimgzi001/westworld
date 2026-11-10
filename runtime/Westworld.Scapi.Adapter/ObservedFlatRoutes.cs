using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public sealed record ObservedFlatRoute(Guid Id,Guid SessionId,Guid ActorId,Guid ObservationId,
 double ObservedAt,Position Origin,Position Destination,IReadOnlyList<Position> Points,
 IReadOnlyList<LocalObservedCell> Evidence,double MaximumAge);
public sealed record FlatRouteCellIssue(int X,int Y,int Z,string Reason,bool Support);
public sealed record FlatRouteInspection(ObservedFlatRoute? Route,string Reason,int RequiredCount,IReadOnlyList<FlatRouteCellIssue> Issues);

/// <summary>Observed axis-aligned full-cube corridor only. Diagnostics never query the world.</summary>
public static class ObservedFlatRoutes {
 public static ObservedFlatRoute? Project(LocalObservationWindow window,Position destination)=>Inspect(window,destination).Route;
 public static FlatRouteInspection Inspect(LocalObservationWindow window,Position destination,double maximumEvidenceAge=3,
  RouteGroundPolicy groundPolicy=RouteGroundPolicy.StrictStable,Func<int,bool>? openCell=null) {
  ArgumentNullException.ThrowIfNull(window);var origin=window.Position;
  FlatRouteInspection No(string reason)=>new(null,reason,0,Array.Empty<FlatRouteCellIssue>());
  if(!double.IsFinite(maximumEvidenceAge)||maximumEvidenceAge<0||maximumEvidenceAge>3)return No("invalid_evidence_age_limit");
  if(!Enum.IsDefined(groundPolicy))return No("invalid_ground_policy");
  if(window.SessionId==Guid.Empty||window.ActorId==Guid.Empty||window.ObservationId==Guid.Empty
   ||!double.IsFinite(window.Time)||window.Time<0||!float.IsFinite(window.Health)||window.Health<=0
   ||!Valid(origin)||!Valid(destination)||window.Cells.Count>4096)return No("invalid_scope_or_body");
  double dx=destination.X-origin.X,dz=destination.Z-origin.Z;
  double distance=Math.Sqrt(dx*dx+dz*dz);
  if(distance<1-.001||distance>4+1e-6)return No("distance_out_of_range");
  if(Math.Abs(destination.Y-origin.Y)>1e-5||Math.Abs(origin.Y-Math.Round(origin.Y))>.05)return No("not_same_level");
  if(Math.Abs(dx)>.001&&Math.Abs(dz)>.001)return No("not_axis_aligned");
  var cells=new Dictionary<(int,int,int),LocalObservedCell>();
  foreach(var c in window.Cells)if(c is null||!cells.TryAdd((c.X,c.Y,c.Z),c))return No("duplicate_or_null_cell");
  var proof=new List<LocalObservedCell>();var issues=new List<FlatRouteCellIssue>();int required=0,feet=(int)Math.Round(origin.Y);
  // Whole conservative swept AABB, not discrete samples. Even tiny corner overlaps require evidence.
  for(int x=(int)Math.Floor(Math.Min(origin.X,destination.X)-.25+1e-5);x<=(int)Math.Floor(Math.Max(origin.X,destination.X)+.25-1e-5);x++)
   for(int z=(int)Math.Floor(Math.Min(origin.Z,destination.Z)-.25+1e-5);z<=(int)Math.Floor(Math.Max(origin.Z,destination.Z)+.25-1e-5);z++) {
    Need(x,feet-1,z,true);
    for(int y=(int)Math.Floor(origin.Y+1e-5);y<=(int)Math.Floor(origin.Y+1.9-1e-5);y++)Need(x,y,z,false);
   }
  if(issues.Count>0)return new(null,"corridor_evidence_incomplete",required,Array.AsReadOnly(issues.ToArray()));
  var points=new List<Position>();int legs=Math.Min(4,(int)Math.Ceiling(distance));
  for(int i=1;i<=legs;i++){double t=(double)i/legs;points.Add(new(origin.X+dx*t,origin.Y,origin.Z+dz*t));}
  var evidence=Array.AsReadOnly(proof.OrderBy(c=>c.X).ThenBy(c=>c.Y).ThenBy(c=>c.Z).ToArray());
  var route=new ObservedFlatRoute(Guid.NewGuid(),window.SessionId,window.ActorId,window.ObservationId,window.Time,origin,destination,
   Array.AsReadOnly(points.ToArray()),evidence,evidence.Max(c=>c.Age));
  return new(route,"complete_observed_corridor",required,Array.Empty<FlatRouteCellIssue>());
  void Need(int x,int y,int z,bool support) {
   required++;string? reason=null;
   if(!cells.TryGetValue((x,y,z),out var c))reason="missing_or_unknown";
   else if(c.Value<0||c.SourceObservationId==Guid.Empty||!double.IsFinite(c.Age)||c.Age<0
    ||!double.IsFinite(c.ObservedAt)||c.ObservedAt<0||Math.Abs(window.Time-c.ObservedAt-c.Age)>1e-6)reason="invalid_evidence";
   else if(c.Age>3)reason="stale_evidence";
   else if(c.Age>maximumEvidenceAge)reason="refresh_needed";
   else if(support?!LocalRouteCandidates.SupportedGround(c.Value,groundPolicy):!(openCell?.Invoke(c.Value)??(c.Value&1023)==0))
    reason=support?"unsupported_ground":"observed_solid";
   if(reason is not null)issues.Add(new(x,y,z,reason,support));else proof.Add(c!);
  }
 }
 static bool Valid(Position p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)
  &&Math.Abs(p.X)<1e7&&Math.Abs(p.Z)<1e7&&p.Y>=2&&p.Y<=250;
}
