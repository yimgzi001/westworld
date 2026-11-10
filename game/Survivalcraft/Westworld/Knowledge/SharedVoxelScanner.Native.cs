using Engine;
namespace Game.Westworld;

public static partial class SharedVoxelScanner {
 public static VoxelScanSnapshot Scan(SubsystemTerrain terrain,Vector3 bodyOrigin,Vector3 eye,Vector3 forward,
  int above=1,int maximumSamples=MaximumSamples,int maximumVoxelReads=MaximumVoxelReads,bool allDirections=false){
  ArgumentNullException.ThrowIfNull(terrain);
  return Scan(Point(bodyOrigin),Point(eye),Point(forward),(a,b)=>Loaded(terrain,a,b,allDirections),
   (x,y,z)=>Read(terrain,x,y,z,allDirections),above,maximumSamples,maximumVoxelReads,allDirections);
 }
 public static ScannedVoxel ReobserveCell(SubsystemTerrain terrain,Vector3 bodyOrigin,Vector3 eye,Vector3 forward,
  int x,int y,int z,int above=1,int maximumSamples=6,int maximumVoxelReads=MaximumVoxelReads){
  ArgumentNullException.ThrowIfNull(terrain);
  return ReobserveCell(Point(bodyOrigin),Point(eye),Point(forward),x,y,z,(a,b)=>Loaded(terrain,a,b),(cx,cy,cz)=>Read(terrain,cx,cy,cz),above,maximumSamples,maximumVoxelReads);
 }
 static VisibilityPoint Point(Vector3 p)=>new(p.X,p.Y,p.Z);
 static VisibilityVoxel? Read(SubsystemTerrain terrain,int x,int y,int z,bool contentReady=false){
  if(y<0||y>=TerrainChunk.Height||!Ready(terrain,x,z,contentReady))return null;
  int value=terrain.Terrain.GetCellValue(x,y,z);
  return new(value,!BlocksManager.Blocks[Terrain.ExtractContents(value)].IsTransparent_(value));
 }
 static bool Loaded(SubsystemTerrain terrain,VisibilityPoint a,VisibilityPoint b,bool contentReady=false){
  int minX=(int)Math.Floor(Math.Min(a.X,b.X))>>4,maxX=(int)Math.Floor(Math.Max(a.X,b.X))>>4;
  int minZ=(int)Math.Floor(Math.Min(a.Z,b.Z))>>4,maxZ=(int)Math.Floor(Math.Max(a.Z,b.Z))>>4;
  for(int x=minX;x<=maxX;x++)for(int z=minZ;z<=maxZ;z++)if(!Ready(terrain,x*16,z*16,contentReady))return false;
  return true;
 }
 // 全向落脚读取与地图快照一样用已经生成方块和光照的区块。
 static bool Ready(SubsystemTerrain terrain,int x,int z,bool contentReady)=>
  terrain.Terrain.GetChunkAtCell(x,z)?.State is { } state
   && (contentReady ? state >= TerrainChunkState.InvalidVertices1 : state == TerrainChunkState.Valid);
}
