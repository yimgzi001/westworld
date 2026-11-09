using System.Text.Json;

// The observation made for a prompt cannot replace a dispatched action's before-state.
public sealed class ToolOutcomeFeedback {
 ToolOutcomeSnapshot pendingBefore,lastBefore,lastAfter;string pendingTool,pendingGoal,lastGoal;
 long pendingSequence;ToolOutcomeTerminal lastTerminal;IReadOnlyList<ToolUnexecutedStep> unfinished=Array.Empty<ToolUnexecutedStep>();
 public JsonElement Value {get;private set;}=JsonSerializer.SerializeToElement(new{status="no_valid_delta",reason="no_preceding_terminal",unexecuted=Array.Empty<object>()});
 static ToolOutcomeSnapshot Copy(ToolOutcomeSnapshot s)=>s with{Inventory=s.Inventory.Clone()};
 public void Begin(ToolOutcomeSnapshot before,string tool,string goal){pendingBefore=Copy(before);pendingTool=tool;pendingGoal=goal;pendingSequence=0;}
 public void Bind(long sequence){if(pendingBefore is null||sequence<=0)throw new ArgumentException("Pending actual dispatch and positive receipt required");pendingSequence=sequence;}
 public bool Complete(long sequence,string status,ToolOutcomeSnapshot after){
  if(pendingBefore is null||pendingSequence!=sequence||status is "running" or "waiting" or "blocked" or "stopping")return false;
  if(status is not("succeeded" or "failed" or "cancelled" or "arrived" or "rejected" or "skipped")){Unavailable(pendingGoal,"unknown_native_terminal",Array.Empty<ToolUnexecutedStep>());pendingBefore=null;return false;}
  lastBefore=pendingBefore;lastAfter=Copy(after);lastGoal=pendingGoal;lastTerminal=new(pendingTool,sequence,status);pendingBefore=null;unfinished=Array.Empty<ToolUnexecutedStep>();Refresh();return true;
 }
 public void Unexecuted(string goal,ToolOutcomeSnapshot after,IReadOnlyList<ToolUnexecutedStep> steps){
  unfinished=steps.ToArray();
  if(lastTerminal is null||lastBefore is null){Unavailable(goal,"no_preceding_terminal",unfinished);return;}
  lastAfter=Copy(after);lastGoal=goal;Refresh();
 }
 public void Unavailable(string goal,string reason,IReadOnlyList<ToolUnexecutedStep> steps){Value=JsonSerializer.SerializeToElement(new{status="no_valid_delta",originalGoal=goal,reason,unexecuted=steps.Select(s=>new{tool=s.Tool,reason=s.Reason,executed=false}).ToArray()});}
 public void UnknownDispatch(string goal,string tool,string reason,IReadOnlyList<ToolUnexecutedStep> remaining){pendingBefore=null;Value=JsonSerializer.SerializeToElement(new{status="outcome_unknown",originalGoal=goal,attemptedTool=tool,executed=(bool?)null,reason,replayAuthorized=false,unexecuted=remaining.Select(s=>new{tool=s.Tool,reason=s.Reason,executed=false}).ToArray()});}
 public void AbortDispatch(string reason){if(pendingBefore is not null)Unavailable(pendingGoal,reason,Array.Empty<ToolUnexecutedStep>());pendingBefore=null;}
 void Refresh(){try{Value=ToolOutcomeDelta.Build(lastBefore,lastAfter,lastGoal,lastTerminal,unfinished,maxChanges:8,maxChars:3000,maxUtf8Bytes:6000);}catch(ArgumentException){Unavailable(lastGoal,"invalid_observation_or_terminal",unfinished);}catch(InvalidOperationException){Unavailable(lastGoal,"feedback_budget_exceeded",unfinished);}}
}
