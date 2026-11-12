using Engine;

namespace Game.Westworld {
    public sealed record SomaticView(float Health, float Air, float Immersion, bool Burning, bool Grounded, float SwimSpeed) {
        public float MovementStunSeconds { get; init; }
    }
    public sealed record LocalTerrainView(float Bearing, float Distance, string Surface, float? Elevation, string Passage);
    internal sealed record PerceivedContact(string Reference, string Name, float X, float Y, float Z, float Distance,
        float ClosingSpeed, bool VisibleAttackMotion);

    public sealed partial class AtomicActions {
        public sealed partial class ActorQueries {
            public SomaticView ReadSomatic() => !m_port.IsActorAvailable ? null : new(
                m_port.m_health.Health, m_port.m_health.Air, m_port.m_body.ImmersionFactor,
                m_port.m_actor.FindComponent<ComponentOnFire>()?.IsOnFire == true,
                m_port.m_body.StandingOnValue.HasValue || m_port.m_body.StandingOnBody != null,
                m_port.m_locomotion?.SwimSpeed ?? 0) { MovementStunSeconds = m_port.m_locomotion?.StunTime ?? 0 };

            // Rays are restricted to the person's current visual cone and four blocks. No global height map.
            public LocalTerrainView[] ReadLocalTerrain() {
                if (!m_port.IsActorAvailable || !m_port.CanTargetWorld()) return [];
                SelfView self = ReadSelf();
                Vector3 eye = m_port.WorldEyes.EyePosition, forward = m_port.WorldEyes.EyeRotation.GetForwardVector();
                List<LocalTerrainView> result = [];
                foreach (float bearing in new[] { -60f, -30f, 0f, 30f, 60f }) {
                    Vector3 direction = Vector3.Transform(Vector3.Normalize(new Vector3(self.Facing.X, 0, self.Facing.Z)),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathUtils.DegToRad(-bearing)));
                    foreach (float distance in new[] { 2f, 4f }) {
                        Vector3 floor = self.Position + direction * distance - Vector3.UnitY * .5f;
                        Vector3 ray = floor - eye;
                        if (Vector3.Dot(forward, ray) < .5f * ray.Length()) continue;
                        var hit = m_port.WorldTerrain.Raycast(eye, floor, false, true,
                            (value, _) => WorldBlock(value).IsCollidable_(value) || WorldBlock(value) is FluidBlock);
                        if (hit is { } surface) {
                            Vector3 point = surface.HitPoint();
                            Block block = WorldBlock(surface.Value);
                            bool atEnd = Vector2.DistanceSquared(point.XZ, floor.XZ) < 1.2f;
                            result.Add(new(bearing, distance, block.GetType().Name, point.Y - self.Position.Y,
                                !block.IsCollidable_(surface.Value) ? "fluidObserved" : atEnd ? "surfaceObserved" : "obstructed"));
                        }
                        else result.Add(new(bearing, distance, null, null, "groundNotObserved"));
                    }
                }
                return result.ToArray();
            }
        }
    }
}
