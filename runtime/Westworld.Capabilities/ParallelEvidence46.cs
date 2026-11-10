namespace Capability39.Cooperation;

public readonly record struct ParallelPosition46(double X,double Y,double Z) {
 internal bool Finite=>double.IsFinite(X)&&double.IsFinite(Y)&&double.IsFinite(Z);
 internal double Distance(ParallelPosition46 other)=>Math.Sqrt(Math.Pow(X-other.X,2)+Math.Pow(Y-other.Y,2)+Math.Pow(Z-other.Z,2));
}
// Request is the original dispatch binding, NOT the ID of this polling query.
public sealed record ParallelSample46(CoopBinding45 Binding,Guid RunId,double GameTime,string Status,ParallelPosition46 Position);
public sealed record ParallelPair46(ParallelSample46 A,ParallelSample46 B);
public sealed record ParallelWindow46(int Samples,double ObservedSpanSeconds,double MaximumSampleGapSeconds,double DisplacementA,double DisplacementB,bool ContinuousCoverageProven=false);
public static class ParallelEvidence46 {
 static void Need(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
 static void Valid(ParallelSample46 s){s.Binding.Validate();Need(s.RunId!=Guid.Empty&&double.IsFinite(s.GameTime)&&s.GameTime>=0&&s.Position.Finite,"invalid_parallel_sample");}
 static void Same(ParallelSample46 expected,ParallelSample46 actual){Valid(actual);CooperativeEvidence45.ValidateIdentity(expected.Binding,actual.Binding);Need(expected.RunId==actual.RunId,"parallel_run_replaced");}
 static void Independent(ParallelSample46 a,ParallelSample46 b){Valid(a);Valid(b);Need(a.Binding.World==b.Binding.World&&a.Binding.Actor!=b.Binding.Actor&&a.Binding.Session!=b.Binding.Session&&a.Binding.Request!=b.Binding.Request&&a.RunId!=b.RunId,"parallel_owners_not_independent");}
 public static ParallelWindow46 ValidateRunningWindow(IReadOnlyList<ParallelPair46> samples,double minimumSpanSeconds=1,double minimumDisplacement=.1,double maximumAllowedSampleGap=1){
  Need(double.IsFinite(minimumSpanSeconds)&&minimumSpanSeconds>=1&&double.IsFinite(minimumDisplacement)&&minimumDisplacement>0&&double.IsFinite(maximumAllowedSampleGap)&&maximumAllowedSampleGap>0&&maximumAllowedSampleGap<=1,"invalid_parallel_thresholds");
  Need(samples.Count>=3,"insufficient_parallel_samples");var first=samples[0];Independent(first.A,first.B);double last=first.A.GameTime,maxGap=0;
  for(int index=0;index<samples.Count;index++){var pair=samples[index];Same(first.A,pair.A);Same(first.B,pair.B);Need(pair.A.GameTime==pair.B.GameTime,"samples_not_same_game_tick");Need(pair.A.Status=="running"&&pair.B.Status=="running","both_not_observed_running");if(index>0){double gap=pair.A.GameTime-last;Need(gap>0,"nonmonotonic_parallel_samples");maxGap=Math.Max(maxGap,gap);}last=pair.A.GameTime;}
  double span=last-first.A.GameTime;var end=samples[^1];double da=first.A.Position.Distance(end.A.Position),db=first.B.Position.Distance(end.B.Position);
  Need(span>=minimumSpanSeconds,"parallel_observed_span_too_short");Need(maxGap<=maximumAllowedSampleGap,"parallel_sampling_gap_exceeded");Need(da>=minimumDisplacement&&db>=minimumDisplacement,"both_actual_displacements_required");
  // Only observed endpoints/ticks are certified, not unobserved time between samples.
  return new(samples.Count,span,maxGap,da,db);
 }
 // after must be a settled cancelled-A sample while B remains running; report pre-settle inertia separately.
 public static void ValidateCancellationIsolation(ParallelPair46 before,ParallelPair46 after,ParallelSample46 finalB,ParallelSample46 finalA,double maximumCancelledDrift=.03){
  Need(double.IsFinite(maximumCancelledDrift)&&maximumCancelledDrift>=0,"invalid_drift_threshold");Independent(before.A,before.B);Same(before.A,after.A);Same(before.B,after.B);Same(before.B,finalB);Same(before.A,finalA);
  Need(before.A.GameTime==before.B.GameTime&&after.A.GameTime==after.B.GameTime&&before.A.Status=="running"&&before.B.Status=="running","invalid_before_cancel_sample");
  Need(after.A.GameTime>before.A.GameTime&&after.A.Status=="cancelled"&&after.B.Status=="running","cancellation_did_not_preserve_other_running_owner");
  Need(finalB.GameTime>after.B.GameTime&&finalB.Status=="succeeded"&&finalA.GameTime==finalB.GameTime&&finalA.Status=="cancelled","isolated_completion_not_observed");
  Need(after.A.Position.Distance(finalA.Position)<=maximumCancelledDrift,"cancelled_actor_continued_displacing");
  Need(after.B.Position.Distance(finalB.Position)>.01,"remaining_actor_has_no_later_displacement");
 }
}
