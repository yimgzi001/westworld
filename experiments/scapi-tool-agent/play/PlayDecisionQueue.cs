#nullable enable

using System.Diagnostics;
using Westworld.Decisions.Local;

internal sealed record PlayDecisionResult(ToolDecisionPlan? Plan, string? Error);

internal sealed class PlayDecisionQueue(int parallelism, int? requestLimit)
{
    private sealed record Job(Guid Session, Guid Observation, long Started, TimeSpan Timeout,
        CancellationTokenSource Cancellation, Task<ToolDecisionPlan> Work)
    {
        public bool CanceledByHost { get; set; }
    }

    private readonly Dictionary<Guid, Job> pending = new();
    private bool stopped;

    public long Issued { get; private set; }
    public bool BudgetReached => requestLimit is { } limit && Issued >= limit;
    public bool HasSlot => !stopped && pending.Count < parallelism && !BudgetReached;
    public bool IsPending(Guid actor) => pending.ContainsKey(actor);

    public bool TryStart(Guid session, Guid actor, Guid observation, TimeSpan timeout,
        Func<CancellationToken, Task<ToolDecisionPlan>> decide)
    {
        if (!HasSlot || pending.ContainsKey(actor)) return false;

        var started = Stopwatch.GetTimestamp();
        var cancellation = new CancellationTokenSource(timeout);
        // 文件桥和模型工作在线程池执行，游戏线程只投递已冻结的数据。
        var work = Task.Run(() => decide(cancellation.Token));
        pending.Add(actor, new Job(session, observation, started, timeout, cancellation, work));
        Issued++;
        return true;
    }

    public PlayDecisionResult? Take(Guid actor)
    {
        if (!pending.TryGetValue(actor, out var job) || !job.Work.IsCompleted) return null;

        pending.Remove(actor);
        using var cancellation = job.Cancellation;
        bool expired = Stopwatch.GetElapsedTime(job.Started) >= job.Timeout;
        try
        {
            var plan = job.Work.GetAwaiter().GetResult();
            if (job.CanceledByHost) return new(null, "model_canceled");
            if (expired || cancellation.IsCancellationRequested) return new(null, "model_timeout");
            if (plan.SessionId != job.Session || plan.ActorId != actor || plan.ObservationId != job.Observation)
                return new(null, "model_scope_mismatch");
            return new(plan, null);
        }
        catch (OperationCanceledException)
        {
            return new(null, job.CanceledByHost ? "model_canceled" : "model_timeout");
        }
        catch (Exception)
        {
            return new(null, "model_failed");
        }
    }

    public void Cancel(Guid actor)
    {
        if (pending.TryGetValue(actor, out var job))
        {
            job.CanceledByHost = true;
            job.Cancellation.Cancel();
        }
    }

    public bool Stop()
    {
        stopped = true;
        foreach (var actor in pending.Keys) Cancel(actor);
        var all = Task.WhenAll(pending.Values.Select(job => job.Work));
        try { all.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }

        foreach (var job in pending.Values)
            _ = job.Work.ContinueWith(work =>
            {
                _ = work.Exception;
                job.Cancellation.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        pending.Clear();
        return all.IsCompleted;
    }
}
