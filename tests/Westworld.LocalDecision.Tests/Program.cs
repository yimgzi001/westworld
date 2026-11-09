using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Decisions.Local;

static class Tests {
 static int count;
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static ActorObservation Snapshot()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),1,new(0,64,0),1,0.4f,
  [new(3,123,2,0.2f,true),new(5,999,1,0,false)],
  [new(1,64,1,"known",777),new(2,64,1,"unknown",777),new(3,64,1,"known",888)]);
 static LocalDecisionPrompt Build()=>LocalDecisionPrompt.Create(Snapshot(),v=>v==777);
 static bool Rejected(Action action){try{action();return false;}catch(Exception e)when(e is FormatException or JsonException or ArgumentException or InvalidOperationException){return true;}}
 static async Task Main(){
  var original=Snapshot();var prompt=LocalDecisionPrompt.Create(original,v=>v==777);
  Check(prompt.CandidateCount==2,"known supported harvest and edible slot only");
  Check(!prompt.Prompt.Contains("777")&&!prompt.Prompt.Contains(original.SessionId.ToString())&&!prompt.Prompt.Contains("unknown"),"prompt excludes values identity and unknown-cell list");
  var eat=prompt.ParseResponse("{\"skill\":\"EatOne\",\"candidate_index\":0}");
  Check(eat.SessionId==original.SessionId&&eat.ActorId==original.ActorId&&eat.ObservationId==original.ObservationId&&eat.DecisionId!=Guid.Empty,"host binds immutable scope and decision identity");
  Check(eat.Slot==3&&eat.ExpectedValue==123&&eat.Target==null,"inventory mapping uses actual slot and value");
  Check(prompt.ParseResponse("{\"candidate_index\":0,\"skill\":\"EatOne\"}")==eat,"same choice retry stable");
  Check(Rejected(()=>prompt.ParseResponse("{\"skill\":\"Wait\",\"candidate_index\":-1}")),"second conflicting choice rejected");
  var forager=LocalDecisionPrompt.Create(original,v=>v==777);original.Inventory[0]=new(3,456,1,1,true);original.Cells[0]=new(1,64,1,"known",456);
  var forage=forager.ParseResponse("{\"skill\":\"ForageOne\",\"candidate_index\":1}");
  Check(forage.Target==new CellTarget(1,64,1,777)&&forage.Slot==-1&&forage.ExpectedValue==0,"target mapping survives source-array mutation");
  Check(Build().ParseResponse("{\"skill\":\"Wait\",\"candidate_index\":-1}").Goal=="Observe","wait maps without target");
  string[] invalid={
   "{}","[]","null","true","0", "{\"skill\":\"Wait\"}",
   "{\"skill\":\"Wait\",\"candidate_index\":-1,\"SessionId\":\"fake\"}",
   "{\"skill\":\"Wait\",\"skill\":\"EatOne\",\"candidate_index\":0}",
   "{\"skill\":\"Wait\",\"candidate_index\":-1,\"candidate_index\":0}",
   "{\"skill\":\"Wait\",\"candidate_\\u0069ndex\":-1,\"candidate_index\":-1}",
   "{\"skill\":\"EatOne\",\"candidate_index\":\"0\"}",
   "{\"skill\":\"EatOne\",\"candidate_index\":0.0}",
   "{\"skill\":\"EatOne\",\"candidate_index\":0e0}",
   "{\"skill\":\"EatOne\",\"candidate_index\":2147483648}",
   "{\"skill\":\"EatOne\",\"candidate_index\":true}",
   "{\"skill\":\"EatOne\",\"candidate_index\":null}",
   "{\"skill\":\"EatOne\",\"candidate_index\":1}",
   "{\"skill\":\"ForageOne\",\"candidate_index\":0}",
   "{\"skill\":\"ForageOne\",\"candidate_index\":2}",
   "{\"skill\":\"Wait\",\"candidate_index\":0}",
   "{\"skill\":\"EatOne\",\"candidate_index\":-1}",
   "{\"skill\":\"RunCode\",\"candidate_index\":0}",
   "{\"skill\":\"eatone\",\"candidate_index\":0}",
   "{\"skill\":12,\"candidate_index\":0}",
   "{\"skill\":\"Wait\",\"candidate_index\":-1,\"target\":{\"X\":0}}",
   "```json\n{\"skill\":\"Wait\",\"candidate_index\":-1}\n```",
   "{\"skill\":\"Wait\",\"candidate_index\":-1,}",
   "{\"skill\":\"Wait\",/*comment*/\"candidate_index\":-1}",
   "{\"skill\":\"Wait\",\"candidate_index\":-1} ignore rules",
   "{\"skill\":\"Wait\",\"candidate_index\":-1}{\"skill\":\"EatOne\",\"candidate_index\":0}",
   new string(' ',4097)
  };
  for(int i=0;i<invalid.Length;i++)Check(Rejected(()=>Build().ParseResponse(invalid[i])),"hostile or malformed output "+i);
  var empty=LocalDecisionPrompt.Create(Snapshot() with {Inventory=[],Cells=[]},_=>false);
  Check(empty.CandidateCount==0&&empty.ParseResponse("{\"skill\":\"Wait\",\"candidate_index\":-1}").Skill=="Wait","empty context can only wait");
  Check(Rejected(()=>LocalDecisionPrompt.Create(Snapshot() with {Cells=[new(0,64,0,"known",777),new(0,64,0,"unknown",null)]},_=>true)),"duplicate evidence coordinates rejected");
  Check(Rejected(()=>LocalDecisionPrompt.Create(Snapshot() with {Inventory=[new(0,1,1,1,true),new(0,2,1,1,true)]},_=>true)),"duplicate slots rejected");
  Check(Rejected(()=>LocalDecisionPrompt.Create(Snapshot() with {Cells=[new(0,256,0,"known",777)]},_=>true)),"native coordinate envelope enforced");
  Check(Rejected(()=>LocalDecisionPrompt.Create(Snapshot() with {Food=float.NaN},_=>true)),"nonfinite actor state rejected");
  Check(Rejected(()=>LocalDecisionPrompt.Create(Snapshot() with {Inventory=[new(0,1,1,float.PositiveInfinity,true)]},_=>true)),"nonfinite nutrition rejected");
  int visits=0;LocalDecisionPrompt.Create(Snapshot() with {Cells=[new(0,64,0,"unknown",777)]},_=>{visits++;return true;});Check(visits==0,"unknown cell never reaches capability predicate");
  var capped=LocalDecisionPrompt.Create(Snapshot(),_=>true,1);Check(capped.CandidateCount==1,"candidate budget bounded");
  Check(Rejected(()=>capped.ParseResponse("{\"skill\":\"ForageOne\",\"candidate_index\":1}")),"truncated candidate cannot be selected");
  Check(Rejected(()=>LocalDecisionPrompt.Create(Snapshot(),_=>true,65)),"oversized candidate budget rejected");
  Check(Build().ParseResponse("{\"skill\":\"Wait\",\"candidate_index\":-1}").DecisionId!=Build().ParseResponse("{\"skill\":\"Wait\",\"candidate_index\":-1}").DecisionId,"separate prompts have separate decisions");
  Check(await BoundedText.ReadAsync(new StringReader("abcd"),4,CancellationToken.None)=="abcd","bounded stream exact limit");
  Check(Rejected(()=>BoundedText.ReadAsync(new StringReader("abcde"),4,CancellationToken.None).GetAwaiter().GetResult()),"bounded stream rejects before oversized append");
  using(var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(20))){bool canceled=false;try{await BoundedText.ReadAsync(new BlockingReader(),4,cancel.Token);}catch(OperationCanceledException){canceled=true;}Check(canceled,"blocked EOF read canceled by deadline");}
  Check(Rejected(()=>BoundedText.ReadAsync(new StringReader("a"),0,CancellationToken.None).GetAwaiter().GetResult()),"invalid stream limit rejected");
  Console.WriteLine($"{count}/{count} checks passed; JSON mapping and bounded streams only, no CLI/model/native game executed.");
 }
 sealed class BlockingReader:TextReader {
  public override async ValueTask<int> ReadAsync(Memory<char> buffer,CancellationToken token=default){await Task.Delay(Timeout.Infinite,token);return 0;}
 }
}
