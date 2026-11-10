int count=0;void Check(bool b){if(!b)throw new Exception("Check "+count);count++;}
var meal=new NaturalFoodMeal(Guid.NewGuid(),1,Guid.NewGuid(),99,0,1,1,0,1,true,.2f,.5f,.7f,0,1,"succeeded");
Check(NaturalFoodEvidence.Verified(meal));
foreach(var bad in new[]{meal with{InitialCount=1},meal with{AcquiredCount=0},meal with{EventCount=0},meal with{MatchingEvents=false},meal with{InventoryAfter=1},meal with{FoodAfter=.5f},meal with{FoodAfter=float.NaN},meal with{StatsAfter=0},meal with{NativeStatus="running"},meal with{RunId=null},meal with{RequestId=Guid.Empty},meal with{ReceiptSequence=null},meal with{Nutrition=0}})Check(!NaturalFoodEvidence.Verified(bad));
var ledger=new NaturalResourceAcquisitionLedger(Guid.NewGuid(),Guid.NewGuid(),new Dictionary<int,int>{{162,1}},0,0);
ledger.ConsumeInitial(Guid.NewGuid(),162,1,0,true,1);ledger.Reconcile(new Dictionary<int,int>(),new Dictionary<Guid,(int,int)>(),0,2);
Check(!ledger.State.Unknown);Check(ledger.State.Consumed.Count==0);Check(ledger.State.Sources.Count==0);Check(ledger.State.ExpectedInventory[162]==0);
ledger.ConsumeInitial(Guid.NewGuid(),162,0,0,true,3);Check(ledger.State.Unknown);
Check(ToolScenarioBudget.For("natural_food")==ToolScenarioBudget.For("natural"));
Console.WriteLine($"PASS {count}; synthetic acceptance contracts, no native world or model executed");
