int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
bool R(double x=4,double y=0,double z=0,double speed=0,double health=1,bool prepared=true,bool busy=false,bool quarantine=false,bool view=false)=>DirectModelEndpoint.Reached(x,y,z,speed,health,prepared,busy,quarantine,view);
Check(R(),"Endpoint success requires no prefetch lease, tool type or model response");
Check(R(x:3.2)&&R(x:4.8)&&R(z:.5)&&R(y:-.5),"inclusive position tolerance");
Check(!R(x:3.19)&&!R(x:4.81)&&!R(z:.51)&&!R(y:-.51),"outside endpoint");
Check(!R(speed:.1)&&!R(speed:-1),"settled nonnegative speed");
Check(!R(health:.249)&&R(health:.25),"health boundary");
Check(!R(prepared:false)&&!R(busy:true)&&!R(quarantine:true)&&!R(view:true),"execution safety guards");
Check(!R(x:double.NaN)&&!R(y:double.PositiveInfinity)&&!R(z:double.NaN)&&!R(speed:double.NaN)&&!R(health:double.NaN),"finite physical evidence");
var budget=ToolScenarioBudget.For("food_drop");
Check(budget.MaxRequests==4&&budget.WallSeconds==180&&budget.RequestSeconds==45,"bounded model fixture budget");
Check(budget.CanRequest(3,179)&&!budget.CanRequest(4,0)&&!budget.CanRequest(0,180),"request and run caps");
Check(budget.RequestWindow(179)==1&&ToolScenarioBudget.DrainSeconds==7,"remaining request and drain window");
Console.WriteLine($"PASS {checks} endpoint/budget checks");
