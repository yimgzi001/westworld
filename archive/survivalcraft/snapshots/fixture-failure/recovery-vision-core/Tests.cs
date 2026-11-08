using RecoveryVision;
using V=RecoveryVision.VisionCore.Vec;
int count=0;
void Check(bool value,string label) {count++;if(!value)throw new Exception(label);}
bool In(double x,double y,double z)=>VisionCore.ContainsLocal(x,y,z);
Check(In(0,0,1),"front"); Check(In(0,0,8),"radius inclusive");
Check(!In(0,0,Math.BitIncrement(8d)),"beyond radius");
Check(!In(0,0,0),"origin");Check(!In(0,0,-1),"behind");
Check(In(4,0,4),"right boundary");Check(In(-4,0,4),"left boundary");
Check(!In(Math.BitIncrement(4d),0,4),"outside right");Check(!In(-Math.BitIncrement(4d),0,4),"outside left");
double vertical=4*VisionCore.VerticalSlope;
Check(In(0,vertical,4),"up boundary");Check(In(0,-vertical,4),"down boundary");
Check(!In(0,Math.BitIncrement(vertical),4),"outside up");Check(!In(0,-Math.BitIncrement(vertical),4),"outside down");
Check(In(4,vertical,4),"rectangle corner inside sphere");
Check(!In(6,0,6),"diagonal beyond sphere");
foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,double.MaxValue}) {
 Check(!In(bad,0,1),"bad x");Check(!In(0,bad,1),"bad y");Check(!In(0,0,bad),"bad z");
}
foreach(double x in new[]{-9d,-4,-1,0,1,4,9})
 foreach(double y in new[]{-4d,0,4}) Check(In(x,y,4)==In(-x,y,4),"left/right symmetry");
foreach(double y in new[]{-4d,-1,0,1,4})Check(In(1,y,4)==In(1,-y,4),"up/down symmetry");
V eye=new(10,20,30),right=new(1,0,0),up=new(0,1,0),forward=new(0,0,1);
Check(VisionCore.ContainsWorld(eye,new(10,20,34),right,up,forward),"translated eye");
Check(!VisionCore.ContainsWorld(eye,new(10,20,26),right,up,forward),"world behind");
Check(!VisionCore.ContainsWorld(eye,new(10,20,34),new(2,0,0),up,forward),"bad scaled basis");
Check(!VisionCore.ContainsWorld(eye,new(10,20,34),right,right,forward),"bad nonorthogonal basis");
Check(!VisionCore.ContainsWorld(new(double.NaN,0,0),forward,right,up,forward),"bad eye");
double a=30*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a);
V pitchedForward=new(0,s,c),pitchedUp=new(0,c,-s);
Check(VisionCore.ContainsWorld(new(0,0,0),new(0,4*s,4*c),right,pitchedUp,pitchedForward),"pitch forward");
Check(!VisionCore.ContainsWorld(new(0,0,0),new(0,-4*s,-4*c),right,pitchedUp,pitchedForward),"pitch behind");
Check(VisionCore.ContainsWorld(new(0,0,0),new(0,0,4),right,pitchedUp,pitchedForward),"pitch 30 world forward inside");
double a40=40*Math.PI/180;
Check(!VisionCore.ContainsWorld(new(0,0,0),new(0,0,4),right,new(0,Math.Cos(a40),-Math.Sin(a40)),new(0,Math.Sin(a40),Math.Cos(a40))),"pitch 40 world forward outside");
foreach(float good in new[]{0f,-0f,.1f,-.1f,MathF.BitDecrement(.1f),-MathF.BitDecrement(.1f)})Check(VisionCore.ValidYaw(good),"valid yaw");
foreach(float bad in new[]{MathF.BitIncrement(.1f),-MathF.BitIncrement(.1f),float.NaN,float.PositiveInfinity,float.NegativeInfinity,float.MaxValue})Check(!VisionCore.ValidYaw(bad),"invalid yaw");
Check((double).1f>.1d,"original float/double pitfall reproduced");


bool Ray(V e,int x,int y,int z,Func<int,int,int,bool> clear)=>VoxelVisibility.HasClearLine(e,x,y,z,clear);
Check(Ray(new(.5,.5,.5),0,0,4,(_,_,_)=>true),"clear axis");
Check(!Ray(new(.5,.5,.5),0,0,4,(x,y,z)=>z!=2),"wall axis");
Check(!Ray(new(.5,.5,.5),0,0,4,(_,_,_)=>false),"unknown fails closed");
Check(Ray(new(.5,.5,.5),0,0,4,(x,y,z)=>z!=4),"target ignored");
Check(Ray(new(.5,.5,.5),0,0,0,(_,_,_)=>false),"same target cell");
Check(!Ray(new(.5,.5,.5),0,0,4,(x,y,z)=>z!=0),"source obstruction");
Check(Ray(new(-.5,-.5,-.5),-4,-1,-1,(_,_,_)=>true),"negative coordinates");
Check(!Ray(new(-.5,-.5,-.5),-4,-1,-1,(x,y,z)=>x!=-2),"negative wall");
Check(!Ray(new(.5,.5,.5),3,3,0,(x,y,z)=>!(x==1&&y==0)),"diagonal corner side cell");
Check(!Ray(new(.5,.5,.5),3,3,3,(x,y,z)=>!(x==1&&y==0&&z==1)),"triple tie edge cell");
Check(Ray(new(.5,.5,.5),3,3,3,(_,_,_)=>true),"clear triple tie");
Check(!Ray(new(.5,.5,.5),0,0,4,(_,_,_)=>throw new Exception()),"callback error fails closed");
Check(!Ray(new(double.NaN,.5,.5),0,0,4,(_,_,_)=>true),"invalid ray");
Check(!Ray(new(.5,.5,.5),0,0,9,(_,_,_)=>true),"ray beyond radius");
Check(Ray(new(.5,.5,.5),0,0,8,(_,_,_)=>true),"ray radius inclusive");
Check(!Ray(new(double.MaxValue,0,0),0,0,0,(_,_,_)=>true),"ray integer range");
Check(Ray(new(0,.5,.5),-3,0,0,(_,_,_)=>true),"start on negative boundary");
Check(!Ray(new(0,.5,.5),-3,0,0,(x,y,z)=>x!=-1),"negative immediate boundary wall");
for(int tx=-3;tx<=3;tx++)for(int tz=-3;tz<=3;tz++){
 var visited=new HashSet<(int,int,int)>();
 Check(Ray(new(.5,.5,.5),tx,0,tz,(x,y,z)=>{visited.Add((x,y,z));return true;}),"grid clear");
 Check(visited.All(c=>!(c.Item1==tx&&c.Item2==0&&c.Item3==tz)),"target never queried");
}
Console.WriteLine($"{count}/{count} reconstructed vision, yaw and voxel checks passed.");
