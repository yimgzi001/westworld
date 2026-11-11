using Engine;
using GameEntitySystem;

namespace Game.Westworld;

public static class NativePredatorAcoustics
{
    public enum Kind { Chase, Attack }
    public sealed record Signal(Project World, Kind Sound, Vector3 Origin, double GameTime, float Gain,
        float ReferenceDistance, float MaxDistance);

    public static event Action<Signal> Emitted;
    public static long ListenerFailures { get; private set; }

    public static void Record(ComponentCreature creature, double gameTime, Kind sound, float gain,
        float referenceDistance, float maxDistance)
    {
        var listeners = Emitted;
        if (listeners is null || (creature.Category & (CreatureCategory.LandPredator | CreatureCategory.LandOther)) == 0
            || creature.Entity.FindComponent<ComponentChaseBehavior>()?.AttacksPlayer != true) return;
        // 原始声源留在宿主，感知端只保留该角色听见的方向和距离档。
        var signal = new Signal(creature.Project, sound, creature.ComponentBody.Position, gameTime,
            gain, referenceDistance, maxDistance);
        foreach (Action<Signal> listener in listeners.GetInvocationList())
        {
            try { listener(signal); }
            catch { ListenerFailures++; }
        }
    }
}
