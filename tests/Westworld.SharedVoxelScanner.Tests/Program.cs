using Game.Westworld;
int n=0;void Check(bool condition,string message){n++;if(!condition)throw new Exception(message);}
var body=new VisibilityPoint(.5,65,.5);var eye=new VisibilityPoint(.5,66.6,.5);var forward=new VisibilityPoint(1,-.35,0);
int calls=0;VisibilityVoxel? World(int x,int y,int z){calls++;return new(y<=64?2:0,y<=64);}
bool Loaded(VisibilityPoint a,VisibilityPoint b)=>true;
var scan=SharedVoxelScanner.Scan(body,eye,forward,Loaded,World);
Check(scan.Complete&&!scan.BudgetExhausted,"default complete");Check(scan.Cells.Count==324,"original grid unchanged");
Check(scan.Statistics.CandidateCells==324,"candidate count");Check(scan.Statistics.VoxelReads==calls,"actual read count");
Check(scan.Statistics.CacheHits>0,"within-call cache used");Check(scan.Statistics.SampleAttempts<=1944,"sample bound");
Check(scan.Statistics.KnownCells==scan.Cells.Count(c=>c.Value.HasValue),"known count");
Check(scan.Cells.Where(c=>c.Value.HasValue).All(c=>c.VisibleSample is {} p&&VoxelSurfaceGeometry.InView(eye,forward,p,c.X,c.Y,c.Z)),"all known have genuine sample");
Check(scan.Cells.Where(c=>!c.Value.HasValue).All(c=>c.VisibleSample is null),"unknown has no sample");
Check(scan.BodyOrigin==body&&scan.Eye==eye&&scan.Forward==forward&&scan.Above==1,"pose parameters preserved");
Check(scan.Cells.Any(c=>c.X==3&&c.Y==64&&c.Z==0&&c.Value==2),"far floor surface");
Check(scan.Cells.All(c=>c.X>=-4&&c.X<=4&&c.Y>=63&&c.Y<=66),"grid limits");
Check(scan.Cells is not ScannedVoxel[],"cannot mutate result array");
var tiny=SharedVoxelScanner.Scan(body,eye,forward,Loaded,World,maximumSamples:1);
Check(!tiny.Complete&&tiny.BudgetExhausted,"sample truncation exposed");Check(tiny.Statistics.SampleAttempts==1,"sample bound exact");Check(tiny.Cells.Count==324,"truncated grid explicit unknown");Check(tiny.Cells.Skip(1).All(c=>c.Value is null),"no filling skipped cells");
var readLimited=SharedVoxelScanner.Scan(body,eye,forward,Loaded,World,maximumVoxelReads:1);
Check(readLimited.BudgetExhausted&&!readLimited.Complete,"read truncation exposed");Check(readLimited.Statistics.VoxelReads==1,"read upper limit exact");
var unknown=SharedVoxelScanner.Scan(body,eye,forward,Loaded,(x,y,z)=>null);
Check(unknown.Complete&&unknown.Cells.All(c=>c.Value is null),"unknown not budget exhaustion");
var unloaded=SharedVoxelScanner.Scan(body,eye,forward,(a,b)=>false,(x,y,z)=>throw new Exception("must not read unloaded rectangle"));
Check(unloaded.Complete&&unloaded.Statistics.VoxelReads==0,"loaded rectangle fail no read");
VisibilityVoxel? Wall(int x,int y,int z)=>new(x==2?4:y<=64?2:0,x==2||y<=64);
var wall=SharedVoxelScanner.Scan(body,eye,forward,Loaded,Wall);
Check(wall.Cells.All(c=>c.X<3||!c.Value.HasValue),"wall hides behind");
Check(SharedVoxelScanner.ReobserveCell(body,eye,forward,3,64,0,Loaded,Wall).Value is null,"reobserve uses same occlusion");
Check(SharedVoxelScanner.ReobserveCell(body,eye,forward,3,64,0,Loaded,World).Value==2,"new call no cached wall");
Check(SharedVoxelScanner.ReobserveCell(body,eye,forward,5,64,0,Loaded,World).Value is null,"single-cell cannot expand grid");
Check(SharedVoxelScanner.ReobserveCell(body,eye,new(-1,0,0),3,64,0,Loaded,World).Value is null,"new pose changes visibility");
Check(SharedVoxelScanner.ReobserveCell(body,eye,forward,3,64,0,Loaded,(x,y,z)=>x==3&&y==64&&z==0?null:World(x,y,z)).Value is null,"target must be known too");
foreach(var pair in new[]{(0,4096),(1945,4096),(1944,0),(1944,4097)}){bool throws=false;try{SharedVoxelScanner.Scan(body,eye,forward,Loaded,World,maximumSamples:pair.Item1,maximumVoxelReads:pair.Item2);}catch(ArgumentOutOfRangeException){throws=true;}Check(throws,"invalid bound");}
// Independent direct walk of the previous sensor algorithm, without caching, must select identical cells/samples.
foreach(var c in scan.Cells){int? expected=null;VisibilityPoint? sample=null;foreach(var p in VoxelSurfaceGeometry.Samples(eye,c.X,c.Y,c.Z)){
 var f=new VisibilityPoint((float)p.X,(float)p.Y,(float)p.Z);
 if(VoxelSurfaceGeometry.InView(eye,forward,f,c.X,c.Y,c.Z)&&VoxelSurfaceGeometry.LineOfSight(eye,f,(x,y,z)=>World(x,y,z)?.Opaque)){expected=World(c.X,c.Y,c.Z)?.Value;sample=expected.HasValue?f:null;break;}}
 if(expected!=c.Value||sample!=c.VisibleSample)throw new Exception("previous sensor equivalence");}
Check(true,"all 324 cells match prior algorithm");
var distant=new VisibilityPoint(30.5,66.6,.5);
Check(!VoxelSurfaceGeometry.LineOfSight(eye,distant,(_,_,_)=>false),"原近距观察保持五格范围");
Check(VoxelSurfaceGeometry.LineOfSight(eye,distant,(_,_,_)=>false,32),"全向观察能看到视距内的远处表面");
Check(!VoxelSurfaceGeometry.LineOfSight(eye,distant,(x,_,_)=>x==18,32),"远处目标仍会被墙挡住");
Check(!VoxelSurfaceGeometry.LineOfSight(eye,distant,(x,_,_)=>x==18?null:false,32),"远处射线不会穿过未加载区");
Check(!VoxelSurfaceGeometry.LineOfSight(eye,new(33.5,66.6,.5),(_,_,_)=>false,32),"全向观察止于视距边界");
Check(VoxelSurfaceGeometry.LineOfSight(eye,new(45.5,66.6,.5),(_,_,_)=>false,48),"全向观察可以匹配较大的地图刷新半径");
var around=SharedVoxelScanner.Scan(body,eye,forward,Loaded,World,allDirections:true);
Check(!scan.AllDirections&&around.AllDirections,"普通观察仍使用朝向");
Check(around.Cells.Any(c=>c.X==-3&&c.Y==64&&c.Z==0&&c.Value==2),"走路的全向近距观察能看到身后地面");
var settled=SharedVoxelScanner.Scan(new(.5,64.999996,.5),eye,forward,Loaded,World,above:3,allDirections:true);
Check(settled.Cells.Any(c=>c.X==1&&c.Y==68&&c.Z==0&&c.Value==0),"格面浮点误差不丢失上台阶的净空层");
var walledAround=SharedVoxelScanner.Scan(body,eye,forward,Loaded,Wall,allDirections:true);
Check(walledAround.Cells.All(c=>c.X<3||!c.Value.HasValue),"全向近距观察仍受墙遮挡");
Console.WriteLine($"PASS {n}/{n} shared scanner pure contracts; no native game executed");
