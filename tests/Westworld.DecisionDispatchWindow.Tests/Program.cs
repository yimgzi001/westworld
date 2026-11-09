int n=0;void Check(bool v){if(!v)throw new Exception("deadline test");n++;}
Check(DecisionDispatchWindow.IsOpen(.9,1));Check(!DecisionDispatchWindow.IsOpen(1,1));Check(!DecisionDispatchWindow.IsOpen(1.1,1));
foreach(double t in new[]{double.NaN,double.PositiveInfinity,-1})Check(!DecisionDispatchWindow.IsOpen(t,1));
Check(!DecisionDispatchWindow.IsOpen(0,double.NaN));
bool dispatched=false;double start=.9,end=1.1;if(DecisionDispatchWindow.IsOpen(start,1)){if(DecisionDispatchWindow.IsOpen(end,1))dispatched=true;}Check(!dispatched);
bool queued=DecisionDispatchWindow.IsOpen(.95,1);if(queued&&DecisionDispatchWindow.IsOpen(1.01,1))dispatched=true;Check(queued&&!dispatched);
Check(DecisionDispatchWindow.ForPlan(0,1) is null);Check(DecisionDispatchWindow.ForPlan(2,1)==1);double? old=DecisionDispatchWindow.ForPlan(0,1);bool newOrdinaryPlanAllowed=old is null||DecisionDispatchWindow.IsOpen(2,old.Value);Check(newOrdinaryPlanAllowed);
Console.WriteLine($"{n}/{n} monotonic deadline boundary checks passed; no native execution claimed");
