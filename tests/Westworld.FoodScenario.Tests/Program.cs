int n=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);n++;}
var b=ToolScenarioBudget.For("food_drop");Check(b.MaxRequests==4&&b.WallSeconds==180&&b.RequestSeconds==45,"food budget 4/180/45");
for(int i=0;i<4;i++)Check(b.CanRequest(i,0),"request including failed/empty charged "+i);
Check(!b.CanRequest(4,0),"fifth request rejected");Check(b.RequestWindow(170)==10&&b.RequestWindow(170,true)==10,"ordinary and prefetch limited by same remaining wall");Check(!b.CanRequest(0,180)&&b.RequestWindow(181)==0,"wall never resets");
Check(ToolScenarioBudget.For("craft")==new ToolScenarioBudget(12,595,120),"legacy limits preserved");
Check(!FoodAcquisitionEvidence.Acquired(0,0,1,true,0,false),"disappearance alone insufficient");Check(!FoodAcquisitionEvidence.Acquired(1,1,1,true,0,false),"initial food not acquisition");Check(!FoodAcquisitionEvidence.Acquired(0,1,1,false,1,false),"inventory alone insufficient");Check(!FoodAcquisitionEvidence.Acquired(0,1,1,true,0,true),"competing source blocks attribution");Check(FoodAcquisitionEvidence.Acquired(0,1,1,true,0,false),"removed original plus matching new inventory");
Check(FoodAcquisitionEvidence.Eaten(true,89,89,1,0,0,1,.5f,.6f,true),"native food event plus inventory stats and food chain");Check(!FoodAcquisitionEvidence.Eaten(true,89,162,1,0,0,1,.5f,.6f,true),"initial fish not goal");Check(!FoodAcquisitionEvidence.Eaten(false,89,89,1,0,0,1,.5f,.6f,true),"unproven acquisition cannot complete");Check(!FoodAcquisitionEvidence.Eaten(true,89,89,1,0,0,1,.5f,.6f,false),"missing native food event rejects");Check(!FoodAcquisitionEvidence.Eaten(true,89,89,1,0,0,1,.5f,.5f,true),"no actual nutrition change rejects");Check(!FoodAcquisitionEvidence.Eaten(true,89,89,1,1,0,1,.5f,.6f,true),"item not consumed rejects");
Check(ToolScenarioBudget.DrainSeconds==7,"drain budget distinct from 180 second run");
foreach(var status in new[]{"succeeded","failed","cancelled"})Check(ToolScenarioBudget.TerminalStatus(status,false),"native direct terminal "+status);
foreach(var status in new[]{"idle","skipped","succeeded","failed","cancelled"})Check(ToolScenarioBudget.TerminalStatus(status,true),"native task terminal "+status);
foreach(var status in new string[]{null,"","unknown","running","waiting","blocked","stopping"})Check(!ToolScenarioBudget.TerminalStatus(status,false)&&!ToolScenarioBudget.TerminalStatus(status,true),"unknown or active never drained "+status);
Check(!ToolScenarioBudget.TerminalStatus("idle",false),"direct status has no invented idle enum");
Check(ToolScenarioBudget.RemainingDrain(10,16)==1&&ToolScenarioBudget.RemainingDrain(10,17)==0&&ToolScenarioBudget.RemainingDrain(10,20)==0,"dispose shares remaining seven seconds no extra three");
Check(ToolScenarioBudget.DirectReceiptTerminal(true,true,null,false),"typed native null plus no active owner is idle");
Check(!ToolScenarioBudget.DirectReceiptTerminal(true,false,null,false),"object missing status is not typed idle");
Check(!ToolScenarioBudget.DirectReceiptTerminal(true,true,null,true),"typed null while fixture action running is not idle");
Check(!ToolScenarioBudget.DirectReceiptTerminal(false,true,null,false),"failed query null is not idle evidence");
Check(!ToolScenarioBudget.DirectReceiptTerminal(true,false,"unexpected",false),"unknown direct string rejected");
Console.WriteLine("TOTAL "+n);
