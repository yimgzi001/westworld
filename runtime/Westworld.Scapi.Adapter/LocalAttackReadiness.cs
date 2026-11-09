namespace Westworld.Scapi.Adapter;

public enum AttackReadiness { Ready, Wait, Expired, Invalid }

/// <summary>A local timing hint, never attack authorization or proof that a mod will accept a hit.</summary>
public static class LocalAttackReadiness
{
    public const double MaximumWaitSeconds = 4;

    /// <summary>
    /// All times must belong to the same live game clock. The host retains the original
    /// wait start/deadline and previous sample; it must not reset them on each poll.
    /// After Ready, resolve fresh visible target evidence and invoke native attack normally.
    /// Native hooks may change the effective interval or reject the attack for other reasons.
    /// </summary>
    public static AttackReadiness Evaluate(double gameTime, double lastHitTime,
        double hitInterval, double waitStartedAt, double deadline,
        double? previousGameTime = null)
    {
        if (!double.IsFinite(gameTime) || !double.IsFinite(lastHitTime)
            || !double.IsFinite(hitInterval) || !double.IsFinite(waitStartedAt)
            || !double.IsFinite(deadline) || gameTime < 0 || waitStartedAt < 0
            || hitInterval < 0 || deadline <= waitStartedAt
            || deadline - waitStartedAt > MaximumWaitSeconds
            || gameTime < waitStartedAt || lastHitTime > gameTime
            || (previousGameTime is double previous &&
                (!double.IsFinite(previous) || previous < waitStartedAt || previous > gameTime)))
            return AttackReadiness.Invalid;

        if (gameTime >= deadline) return AttackReadiness.Expired;
        double elapsed = gameTime - lastHitTime;
        if (!double.IsFinite(elapsed)) return AttackReadiness.Invalid;
        // Native ComponentMiner.Hit uses strict >; equality must wait another game tick.
        return elapsed > hitInterval ? AttackReadiness.Ready : AttackReadiness.Wait;
    }
}
