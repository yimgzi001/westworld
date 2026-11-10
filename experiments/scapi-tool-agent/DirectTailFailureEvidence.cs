// Artificial native test acceptance only; no production policy or injected failure.
public sealed record DirectTailFailureFact(int CompletedSteps,int TotalSteps,string TailTool,
 Guid ExpectedRun,Guid ActualRun,long ExpectedReceipt,bool Returned,string ActualStatus,
 bool CandidateInvalidated,int CandidateTaken,bool HandoffOccupied,bool ModelInFlight,
 int QueuedSteps,bool NativeIdle,bool PhysicallySettled);
public static class DirectTailFailureEvidence {
 public static bool Verified(DirectTailFailureFact f)=>f is not null&&f.CompletedSteps==1&&f.TotalSteps==2&&
  f.TailTool=="act_FaceTowards"&&f.ExpectedRun!=Guid.Empty&&f.ActualRun==f.ExpectedRun&&f.ExpectedReceipt>0&&
  f.Returned&&f.ActualStatus is "failed" or "cancelled"&&f.CandidateInvalidated&&f.CandidateTaken==0&&
  !f.HandoffOccupied&&!f.ModelInFlight&&f.QueuedSteps==0&&f.NativeIdle&&f.PhysicallySettled;
}
