namespace Westworld.Runtime.Core;

// Host diagnostics only. Counts are native registry observations, NOT completed actions,
// scheduling fairness, physical movement, or facts visible to an AI player.
public sealed record ActorHealth(Guid ActorId,bool Active,bool Bound,long Revision,long UpdateCount);
public sealed record CohortHealthPage(Guid WorldId,Guid SessionId,long Version,int Offset,
    int TotalActors,int ActiveActors,int BoundActors,int ActiveBoundActors,int RetiredActors,
    long TotalUpdates,int? NextOffset,IReadOnlyList<ActorHealth> Actors);

public sealed partial class ActorRegistry {
    // Only current actors retain a counter. Retirement history remains owned by the existing
    // bounded retired-ID set; no new history, timers, task owner or scheduler is introduced.
    readonly Dictionary<Guid,long> healthUpdates=new();
    long healthVersion,healthTotalUpdates;

    /// <summary>
    /// Read bounded host health on the game thread. Pass the first page's Version for
    /// subsequent pages. Every successful actor mutation invalidates that cursor;
    /// retry from page zero rather than silently combining different actor states.
    /// Restore starts new session-local counts and preserves existing rebind requirements.
    /// </summary>
    public CohortHealthPage CaptureHealthPage(Guid session,long? expectedVersion=null,int offset=0,int limit=128) {
        Owner();
        if(session!=SessionId)throw new InvalidOperationException("Wrong health session");
        if(offset<0||offset>actors.Count||limit<1||limit>128)throw new ArgumentOutOfRangeException(nameof(offset));
        if(offset>0&&!expectedVersion.HasValue)throw new ArgumentException("Continuation requires health version");
        if(expectedVersion.HasValue&&expectedVersion.Value!=healthVersion)throw new InvalidOperationException("Stale health cursor");
        var all=actors.Values.OrderBy(a=>a.Id).ToArray();
        var page=all.Skip(offset).Take(limit).Select(a=>new ActorHealth(a.Id,a.Active,bound.Contains(a.Id),a.Revision,healthUpdates[a.Id])).ToArray();
        int next=offset+page.Length;
        return new(WorldId,SessionId,healthVersion,offset,all.Length,all.Count(a=>a.Active),bound.Count,
            all.Count(a=>a.Active&&bound.Contains(a.Id)),retired.Count,healthTotalUpdates,
            next<all.Length?next:null,Array.AsReadOnly(page));
    }
}
