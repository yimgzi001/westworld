namespace Game.Westworld;

public readonly record struct VisibilityVoxel(int Value,bool Opaque);
public sealed record ScannedVoxel(int X,int Y,int Z,int? Value,VisibilityPoint? VisibleSample);
public sealed record VoxelScanStatistics(int CandidateCells,int SampleAttempts,int LineOfSightChecks,
 int VoxelReads,int CacheHits,int KnownCells,int LoadedRectangleChecks);
/// <summary>Immutable one-call facts, not actor authorization or cross-frame memory.
/// Complete means the specified bounded grid was processed, NOT all surrounding space was seen.</summary>
public sealed class VoxelScanSnapshot {
 public IReadOnlyList<ScannedVoxel> Cells {get;}
 public VoxelScanStatistics Statistics {get;}
 public VisibilityPoint BodyOrigin {get;} public VisibilityPoint Eye {get;} public VisibilityPoint Forward {get;}
 public int Above {get;} public bool AllDirections {get;} public bool Complete=>!BudgetExhausted; public bool BudgetExhausted {get;}
 internal VoxelScanSnapshot(IEnumerable<ScannedVoxel> cells,VoxelScanStatistics stats,VisibilityPoint body,VisibilityPoint eye,VisibilityPoint forward,int above,bool exhausted,bool allDirections){
  Cells=Array.AsReadOnly(cells.ToArray());Statistics=stats;BodyOrigin=body;Eye=eye;Forward=forward;Above=above;BudgetExhausted=exhausted;AllDirections=allDirections;
 }
}
/// <summary>Call on the game's simulation thread. No global/tick cache, actor refs, terrain loading,
/// clock changes or memory writes. Callbacks must be read-only and return null for unknown/unloaded voxels.</summary>
public static partial class SharedVoxelScanner {
 public const int MaximumSamples=1944,MaximumVoxelReads=4096;
 public static VoxelScanSnapshot Scan(VisibilityPoint bodyOrigin,VisibilityPoint eye,VisibilityPoint forward,
  Func<VisibilityPoint,VisibilityPoint,bool> loadedRectangle,Func<int,int,int,VisibilityVoxel?> readVoxel,
  int above=1,int maximumSamples=MaximumSamples,int maximumVoxelReads=MaximumVoxelReads,bool allDirections=false)=>
  Run(bodyOrigin,eye,forward,loadedRectangle,readVoxel,above,maximumSamples,maximumVoxelReads,null,allDirections);
 public static ScannedVoxel ReobserveCell(VisibilityPoint bodyOrigin,VisibilityPoint eye,VisibilityPoint forward,
  int x,int y,int z,Func<VisibilityPoint,VisibilityPoint,bool> loadedRectangle,Func<int,int,int,VisibilityVoxel?> readVoxel,
  int above=1,int maximumSamples=6,int maximumVoxelReads=MaximumVoxelReads){
  var snapshot=Run(bodyOrigin,eye,forward,loadedRectangle,readVoxel,above,maximumSamples,maximumVoxelReads,(x,y,z),false);
  return snapshot.Cells[0];
 }
 static VoxelScanSnapshot Run(VisibilityPoint body,VisibilityPoint eye,VisibilityPoint forward,
  Func<VisibilityPoint,VisibilityPoint,bool> loadedRectangle,Func<int,int,int,VisibilityVoxel?> readVoxel,
  int above,int maxSamples,int maxReads,(int X,int Y,int Z)? single,bool allDirections){
  ArgumentNullException.ThrowIfNull(loadedRectangle);ArgumentNullException.ThrowIfNull(readVoxel);
  if(above<1||above>3||maxSamples<1||maxSamples>MaximumSamples||maxReads<1||maxReads>MaximumVoxelReads)throw new ArgumentOutOfRangeException("scan budget/grid");
  if(!Finite(body)||!Finite(eye)||!double.IsFinite(forward.X)||!double.IsFinite(forward.Y)||!double.IsFinite(forward.Z))throw new ArgumentException("finite scan pose required");
  int bx=(int)Math.Floor(body.X),by=(int)Math.Floor(body.Y),bz=(int)Math.Floor(body.Z);
  var output=new List<ScannedVoxel>();var cache=new Dictionary<(int,int,int),VisibilityVoxel?>();
  int samples=0,los=0,reads=0,hits=0,known=0,rectangles=0;bool exhausted=false;
  VisibilityVoxel? Read(int x,int y,int z){
   if(y<0||y>=256)return null;
   if(cache.TryGetValue((x,y,z),out var value)){hits++;return value;}
   if(reads>=maxReads){exhausted=true;return null;}
   reads++;value=readVoxel(x,y,z);cache.Add((x,y,z),value);return value;
  }
  void Cell(int x,int y,int z){
   int? value=null;VisibilityPoint? visible=null;
   bool candidate=x>=bx-4&&x<=bx+4&&z>=bz-4&&z<=bz+4&&y>=by-2&&y<=by+above&&y>=0&&y<256;
   if(candidate&&!exhausted)foreach(var point in VoxelSurfaceGeometry.Samples(eye,x,y,z)){
    if(samples>=maxSamples){exhausted=true;break;}samples++;
    // Preserve actual native float conversion before endpoint/FOV/LOS checks.
    var target=new VisibilityPoint((float)point.X,(float)point.Y,(float)point.Z);
    if(!(allDirections?VoxelSurfaceGeometry.InLocalRange(eye,target,x,y,z):VoxelSurfaceGeometry.InView(eye,forward,target,x,y,z)))continue;
    rectangles++;if(!loadedRectangle(eye,target))continue;
    los++;if(!VoxelSurfaceGeometry.LineOfSight(eye,target,(cx,cy,cz)=>Read(cx,cy,cz)?.Opaque))continue;
    var observed=Read(x,y,z);
    if(exhausted)break;
    if(observed is {Value:>=0} actual){value=actual.Value;visible=target;known++;break;}
   }
   output.Add(new(x,y,z,value,visible));
  }
  if(single is {} one)Cell(one.X,one.Y,one.Z);
  else for(int x=bx-4;x<=bx+4;x++)for(int z=bz-4;z<=bz+4;z++)for(int y=by-2;y<=by+above;y++)Cell(x,y,z);
  return new(output,new(output.Count,samples,los,reads,hits,known,rectangles),body,eye,forward,above,exhausted,allDirections);
 }
 static bool Finite(VisibilityPoint p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&Math.Abs(p.X)<1e7&&Math.Abs(p.Z)<1e7&&p.Y>=0&&p.Y<=256;
}
