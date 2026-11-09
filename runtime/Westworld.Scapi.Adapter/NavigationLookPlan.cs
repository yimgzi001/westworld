using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public enum NavigationLookStage { CurrentSupport, CurrentBody, ForwardSupport, HeadClearance }
public sealed record NavigationMissingCell(int X,int Y,int Z,NavigationLookStage Stage);
public sealed record NavigationLook(NavigationMissingCell Cell,Position Target,Position Direction,
    double YawRadians,double PitchRadians,Guid ObservationId,double ObservedAt);
public sealed record NavigationLookDecision(NavigationLook? Look,string Reason);

/// <summary>Bounded observation-only planner. Caller supplies actual missing route cells;
/// no world queries, movement permission or inference of occluded air. Angles use
/// yaw atan2(-dx,-dz), pitch asin(dy); Direction is authoritative for host adaptation.
/// One instance per actor on the simulation thread. Attempts are consumed on Next;
/// repeated observations/time passing do not refill the scan budget.</summary>
public sealed class NavigationLookPlan {
    readonly int limit;
    readonly HashSet<(int,int,int)> attempted=[];
    Guid session,actor,lastObservation; Position anchor=new(0,0,0); double lastTime=-1;
    int count;
    public NavigationLookPlan(int maxAttempts=8) {
        if(maxAttempts is <1 or >16)throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        limit=maxAttempts;
    }
    public NavigationLookDecision Next(LocalObservationWindow window,Position eye,
        IReadOnlyList<NavigationMissingCell> missing,double maximumKnownAge=3) {
        ArgumentNullException.ThrowIfNull(window);ArgumentNullException.ThrowIfNull(missing);
        NavigationLookDecision No(string reason)=>new(null,reason);
        if(!double.IsFinite(maximumKnownAge)||maximumKnownAge<0||maximumKnownAge>3)return No("invalid_evidence_age_limit");
        if(window.SessionId==Guid.Empty||window.ActorId==Guid.Empty||window.ObservationId==Guid.Empty
            ||!double.IsFinite(window.Time)||window.Time<0||!Finite(window.Position)||!Finite(eye)
            ||!float.IsFinite(window.Health)||window.Health<=0||missing.Count>512||window.Cells.Count>4096
            ||Distance(window.Position,eye)>2.5)return No("invalid_input");
        if(session!=Guid.Empty&&(session!=window.SessionId||actor!=window.ActorId))return No("wrong_actor_session");
        if(window.Time<lastTime)return No("time_regressed");
        if(window.ObservationId==lastObservation)return No("fresh_observation_required");
        var known=new HashSet<(int,int,int)>();
        foreach(var c in window.Cells) {
            if(c is null||!known.Add((c.X,c.Y,c.Z)))return No("invalid_window");
        }
        // A stricter dispatch freshness limit can request real re-observation.
        // It never updates evidence timestamps or resets the finite attempt budget.
        // Invalid/expired evidence is not upgraded into knowledge.
        known.Clear();
        foreach(var c in window.Cells)if(c.Value>=0&&c.SourceObservationId!=Guid.Empty
            &&double.IsFinite(c.Age)&&c.Age>=0&&c.Age<=maximumKnownAge&&double.IsFinite(c.ObservedAt)&&c.ObservedAt>=0
            &&Math.Abs(window.Time-c.ObservedAt-c.Age)<=1e-6)known.Add((c.X,c.Y,c.Z));
        foreach(var c in missing)if(c is null||!Enum.IsDefined(c.Stage)||c.Y is <0 or >255
            ||Math.Abs((long)c.X)>10_000_000||Math.Abs((long)c.Z)>10_000_000
            ||Distance(window.Position,new(c.X+.5,c.Y+.5,c.Z+.5))>5)return No("invalid_missing_cells");
        if(session==Guid.Empty){session=window.SessionId;actor=window.ActorId;anchor=window.Position;}
        if(Distance(anchor,window.Position)>.5){anchor=window.Position;attempted.Clear();count=0;}
        lastTime=window.Time;lastObservation=window.ObservationId;
        if(count>=limit)return No("scan_budget_exhausted");
        var candidates=missing.Where(c=>!known.Contains((c.X,c.Y,c.Z))&&!attempted.Contains((c.X,c.Y,c.Z)))
            .OrderBy(c=>c.Stage).ThenBy(c=>Distance(eye,new(c.X+.5,c.Y+.5,c.Z+.5)))
            .ThenBy(c=>c.X).ThenBy(c=>c.Y).ThenBy(c=>c.Z);
        foreach(var c in candidates) {
            var target=(c.Stage is NavigationLookStage.CurrentSupport or NavigationLookStage.ForwardSupport)&&c.Y<Math.Round(window.Position.Y)?VoxelSurfaceVisibility.SupportTop(c.X,c.Y,c.Z):new Position(c.X+.5,c.Y+.5,c.Z+.5);double length=Distance(eye,target);
            if(length<1e-6)continue;
            var d=new Position((target.X-eye.X)/length,(target.Y-eye.Y)/length,(target.Z-eye.Z)/length);
            attempted.Add((c.X,c.Y,c.Z));count++;
            return new(new(c,target,d,Math.Atan2(-d.X,-d.Z),Math.Asin(Math.Clamp(d.Y,-1,1)),window.ObservationId,window.Time),"look_at_missing_cell");
        }
        return No("no_untried_missing_cells");
    }
    static bool Finite(Position p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)
        &&Math.Abs(p.X)<=10_000_000&&Math.Abs(p.Z)<=10_000_000&&p.Y>=0&&p.Y<=256;
    static double Distance(Position a,Position b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2));
}
