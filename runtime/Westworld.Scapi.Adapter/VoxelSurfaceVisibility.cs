using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

/// <summary>Voxel surface samples, not a claim that all faces/space around a voxel are visible.
/// No world reads: host supplies loaded-region and opaque-voxel checks separately.</summary>
public static class VoxelSurfaceVisibility {
 public const double Inset=.001;
 public static Position SupportTop(int x,int y,int z)=>new(x+.5,y+1-Inset,z+.5);
 public static IReadOnlyList<Position> Samples(Position eye,int x,int y,int z){
  var result=new List<Position>{new(x+.5,y+.5,z+.5)};
  if(eye.X<x)result.Add(new(x+Inset,y+.5,z+.5));else if(eye.X>x+1)result.Add(new(x+1-Inset,y+.5,z+.5));
  if(eye.Y<y)result.Add(new(x+.5,y+Inset,z+.5));else if(eye.Y>y+1)result.Add(SupportTop(x,y,z));
  if(eye.Z<z)result.Add(new(x+.5,y+.5,z+Inset));else if(eye.Z>z+1)result.Add(new(x+.5,y+.5,z+1-Inset));
  return result.AsReadOnly();
 }
 public static bool InVoxel(Position p,int x,int y,int z)=>Finite(p)&&(int)Math.Floor(p.X)==x&&(int)Math.Floor(p.Y)==y&&(int)Math.Floor(p.Z)==z;
 public static bool InView(Position eye,Position forward,Position sample,int x,int y,int z){
  if(!Finite(eye)||!double.IsFinite(forward.X)||!double.IsFinite(forward.Y)||!double.IsFinite(forward.Z)||!InVoxel(sample,x,y,z)||y<0||y>=256)return false;
  // Preserve old center-radius eligibility; a nearer face cannot expand the observed grid/range.
  if(DistanceSquared(eye,new(x+.5,y+.5,z+.5))>25||DistanceSquared(eye,sample)>25)return false;
  double dx=sample.X-eye.X,dy=sample.Y-eye.Y,dz=sample.Z-eye.Z,ds=dx*dx+dy*dy+dz*dz;
  double fs=forward.X*forward.X+forward.Y*forward.Y+forward.Z*forward.Z;
  return fs>1e-12&&(ds<=1e-8||(dx*forward.X+dy*forward.Y+dz*forward.Z)/Math.Sqrt(ds*fs)>=.5);
 }
 /// <summary>Unknown intermediate cells block; target cell itself may be opaque and is not read here.</summary>
 public static bool LineOfSight(Position start,Position end,Func<int,int,int,bool?> opaque){
  ArgumentNullException.ThrowIfNull(opaque);if(!Finite(start)||!Finite(end)||DistanceSquared(start,end)>25)return false;
  int x=(int)Math.Floor(start.X),y=(int)Math.Floor(start.Y),z=(int)Math.Floor(start.Z),tx=(int)Math.Floor(end.X),ty=(int)Math.Floor(end.Y),tz=(int)Math.Floor(end.Z);
  double vx=end.X-start.X,vy=end.Y-start.Y,vz=end.Z-start.Z;
  int sx=Math.Sign(vx),sy=Math.Sign(vy),sz=Math.Sign(vz);
  double Delta(double v)=>v==0?double.PositiveInfinity:Math.Abs(1/v);
  double First(double a,double v,int cell)=>v==0?double.PositiveInfinity:((v>0?cell+1:cell)-a)/v;
  double dx=Delta(vx),dy=Delta(vy),dz=Delta(vz),mx=First(start.X,vx,x),my=First(start.Y,vy,y),mz=First(start.Z,vz,z);
  for(int i=0;i<32;i++){
   if(y<0||y>=256)return false;
   if(x==tx&&y==ty&&z==tz)return true;
   if(opaque(x,y,z)!=false)return false;
   double next=Math.Min(mx,Math.Min(my,mz));
   bool cx=Math.Abs(mx-next)<=1e-12,cy=Math.Abs(my-next)<=1e-12,cz=Math.Abs(mz-next)<=1e-12;
   int mask=(cx?1:0)|(cy?2:0)|(cz?4:0);
   // Supercover at an exact edge/corner: never see diagonally through an opaque touching voxel.
   if((mask&(mask-1))!=0)for(int subset=1;subset<8;subset++){
    if((subset&mask)!=subset||subset==mask)continue;
    int ax=x+((subset&1)!=0?sx:0),ay=y+((subset&2)!=0?sy:0),az=z+((subset&4)!=0?sz:0);
    if(ay<0||ay>=256)return false;
    if(!(ax==tx&&ay==ty&&az==tz)&&opaque(ax,ay,az)!=false)return false;
   }
   if(cx){x+=sx;mx+=dx;}if(cy){y+=sy;my+=dy;}if(cz){z+=sz;mz+=dz;}
  }return false;
 }
 static bool Finite(Position p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&Math.Abs(p.X)<1e7&&Math.Abs(p.Z)<1e7&&p.Y>=0&&p.Y<=256;
 static double DistanceSquared(Position a,Position b)=>Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2);
}
