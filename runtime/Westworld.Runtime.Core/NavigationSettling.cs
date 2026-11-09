namespace Westworld.Runtime.Core;

public enum NavigationSettlingStatus { Waiting, Settled, Failed }
public sealed record NavigationSettlingSample(double Time,bool Alive,bool Grounded,bool Parented,
    double Immersion,double VelocityX,double VelocityY,double VelocityZ);
public sealed record NavigationSettlingResult(NavigationSettlingStatus Status,string Reason,double StableFor);

/// <summary>Game-thread post-input-stop gate. It never changes input or physics.
/// Caller must first revoke movement ownership/inputs, then supply actual body samples
/// on one monotonic clock (seconds). Only Settled allows a new land navigation plan.
/// Sparse samples cannot prove continuous residence: gaps over maxSampleGap reset it.</summary>
public sealed class NavigationSettling {
    readonly int owner=Environment.CurrentManagedThreadId;
    readonly double start,timeout,stableDuration,maxGap;double last,stableSince;
    NavigationSettlingResult? terminal;
    public NavigationSettling(double startTime,double timeoutSeconds=3,double stableSeconds=.25,double maxSampleGap=.1) {
        if(!double.IsFinite(startTime)||startTime<0||!double.IsFinite(timeoutSeconds)||timeoutSeconds<=0||timeoutSeconds>30
            ||!double.IsFinite(stableSeconds)||stableSeconds<=0||stableSeconds>timeoutSeconds
            ||!double.IsFinite(maxSampleGap)||maxSampleGap<=0||maxSampleGap>stableSeconds)
            throw new ArgumentOutOfRangeException();
        start=last=startTime;timeout=timeoutSeconds;stableDuration=stableSeconds;maxGap=maxSampleGap;stableSince=double.NaN;
    }
    public NavigationSettlingResult Update(NavigationSettlingSample sample) {
        if(Environment.CurrentManagedThreadId!=owner)throw new InvalidOperationException("Game thread required");
        ArgumentNullException.ThrowIfNull(sample);
        if(!double.IsFinite(sample.Time)||sample.Time<last||!double.IsFinite(sample.Immersion)||sample.Immersion<0||sample.Immersion>1
            ||!double.IsFinite(sample.VelocityX)||!double.IsFinite(sample.VelocityY)||!double.IsFinite(sample.VelocityZ))
            throw new ArgumentException("Invalid settling sample");
        if(terminal is not null)return terminal;
        double gap=sample.Time-last;last=sample.Time;
        if(!sample.Alive)return terminal=new(NavigationSettlingStatus.Failed,"actor_dead",0);
        if(sample.Parented)return terminal=new(NavigationSettlingStatus.Failed,"actor_parented",0);
        string? moving=!sample.Grounded?"airborne":sample.Immersion>.1?"immersed":
            Math.Abs(sample.VelocityY)>.1?"vertical_motion":
            Math.Sqrt(sample.VelocityX*sample.VelocityX+sample.VelocityZ*sample.VelocityZ)>.1?"horizontal_motion":null;
        if(moving is not null||gap>maxGap+1e-9)stableSince=double.NaN;
        if(moving is null&&double.IsNaN(stableSince))stableSince=sample.Time;
        double stableFor=double.IsNaN(stableSince)?0:sample.Time-stableSince;
        // Deadline is strict; a late first poll must not retroactively claim success.
        if(sample.Time-start>timeout+1e-9)return terminal=new(NavigationSettlingStatus.Failed,"settling_timeout",stableFor);
        if(moving is null&&stableFor+1e-9>=stableDuration)return terminal=new(NavigationSettlingStatus.Settled,"stable_ground_residence",stableFor);
        if(sample.Time-start+1e-9>=timeout)return terminal=new(NavigationSettlingStatus.Failed,"settling_timeout",stableFor);
        return new(NavigationSettlingStatus.Waiting,moving??(gap>maxGap+1e-9?"sampling_gap":"confirming_stability"),stableFor);
    }
}
