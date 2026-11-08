using SurvivalcraftAgentLab;
var cases=new (string? a,float x,float y,float z,float s,int slot,bool expected)[]{
("move",1,0,-1,.5f,-1,true),("stop",0,0,0,0,-1,true),("move",float.NaN,0,0,.2f,-1,false),
("move",0,0,float.PositiveInfinity,.2f,-1,false),("move",2,0,0,.2f,-1,false),("move",0,1,0,.2f,-1,false),
("stop",0,0,0,float.NaN,-1,false),("move",0,0,0,-1,-1,false),("move",0,0,0,.6f,-1,false),
("dig",0,-1,0,.2f,-1,true),("place",0,0,0,.2f,-1,false),("dig",float.MaxValue,0,0,.2f,-1,false),
("select",0,0,0,.2f,25,true),("select",0,0,0,.2f,26,false),("select",0,0,0,.2f,-1,false),
("teleport",0,0,0,.2f,-1,false),(null,0,0,0,.2f,-1,false)};
foreach(var c in cases)if(CommandPolicy.Valid(c.a,c.x,c.y,c.z,c.s,c.slot,26)!=c.expected)throw new Exception("Policy test failed");
Console.WriteLine($"{cases.Length}/{cases.Length} protocol validation cases passed");
