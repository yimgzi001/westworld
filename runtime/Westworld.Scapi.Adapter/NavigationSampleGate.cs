namespace Westworld.Scapi.Adapter;

/// <summary>Audit-only pacing. Does not advance physics, infer motion, or certify owner idle.</summary>
public sealed record NavigationSampleKey(Guid Owner,Guid Request,Guid Segment,string State,
 string Reason,bool Busy,bool OwnsLease,bool KinematicallyStopped);
public sealed record NavigationSampleDecision(bool Emit,string Trigger,double? IntervalSeconds,
 bool CoverageUnknown,int SuppressedSincePrevious);
public sealed class NavigationSampleGate {
 public const double PeriodSeconds=.1;
 public const double CoverageGapSeconds=.15;
 NavigationSampleKey? lastKey;double lastEmitted,lastObserved;int suppressed;bool seen;
 public NavigationSampleDecision Observe(double monotonicSeconds,NavigationSampleKey key){
  if(!double.IsFinite(monotonicSeconds)||monotonicSeconds<0)throw new ArgumentOutOfRangeException(nameof(monotonicSeconds));
  ArgumentNullException.ThrowIfNull(key);
  bool regressed=seen&&monotonicSeconds<lastObserved;
  double? interval=seen?monotonicSeconds-lastEmitted:null;
  string trigger=!seen?"first":regressed?"clock_regressed":key!=lastKey?"boundary":key.Busy&&interval>=PeriodSeconds?"periodic":"suppressed";
  lastObserved=monotonicSeconds;seen=true;
  if(trigger=="suppressed"){suppressed++;return new(false,trigger,interval,interval>CoverageGapSeconds,suppressed);}
  var decision=new NavigationSampleDecision(true,trigger,interval,regressed||interval>CoverageGapSeconds,suppressed);
  lastKey=key;lastEmitted=monotonicSeconds;suppressed=0;return decision;
 }
}
