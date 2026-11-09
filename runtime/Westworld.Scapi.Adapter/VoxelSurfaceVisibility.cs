using Westworld.Runtime.Core;
using Game.Westworld;
namespace Westworld.Scapi.Adapter;

/// <summary>Compatibility conversion only. Authoritative geometry is in Game, not duplicated here.</summary>
public static class VoxelSurfaceVisibility {
 public const double Inset=VoxelSurfaceGeometry.Inset;
 static VisibilityPoint Point(Position p)=>new(p.X,p.Y,p.Z);
 static Position Core(VisibilityPoint p)=>new(p.X,p.Y,p.Z);
 public static Position SupportTop(int x,int y,int z)=>Core(VoxelSurfaceGeometry.SupportTop(x,y,z));
 public static IReadOnlyList<Position> Samples(Position eye,int x,int y,int z)=>Array.AsReadOnly(VoxelSurfaceGeometry.Samples(Point(eye),x,y,z).Select(Core).ToArray());
 public static bool InVoxel(Position p,int x,int y,int z)=>VoxelSurfaceGeometry.InVoxel(Point(p),x,y,z);
 public static bool InView(Position eye,Position forward,Position sample,int x,int y,int z)=>VoxelSurfaceGeometry.InView(Point(eye),Point(forward),Point(sample),x,y,z);
 public static bool LineOfSight(Position start,Position end,Func<int,int,int,bool?> opaque)=>VoxelSurfaceGeometry.LineOfSight(Point(start),Point(end),opaque);
}
