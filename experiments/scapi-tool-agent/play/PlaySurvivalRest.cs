#nullable enable

using Game;

internal sealed class PlaySurvivalRest(ComponentPlayer player, double started)
{
    internal enum State { Drying, Ready, Interrupted, TimedOut }
    private readonly double deadline = started + 45;
    private double nextCheck;

    public static bool HasUsableCover(ComponentPlayer player) => PlaySurvivalSenses.OnDryGround(player.ComponentBody)
        && !Blocked(player, "BlockIsComfortable") && !Blocked(player, "Ceiling");

    public static bool WetnessBlocksSleep(ComponentPlayer player) => Blocked(player, "NotTooWet");

    public State Update(double gameTime)
    {
        if (gameTime < nextCheck) return State.Drying;
        nextCheck = gameTime + 0.4;
        if (!HasUsableCover(player)) return State.Interrupted;
        if (player.ComponentSleep.CanSleep(out _)) return State.Ready;
        return gameTime >= deadline ? State.TimedOut : State.Drying;
    }

    private static bool Blocked(ComponentPlayer player, string condition) =>
        !player.ComponentSleep.m_conditionsToSleep.TryGetValue(condition, out var check) || !string.IsNullOrEmpty(check());
}
