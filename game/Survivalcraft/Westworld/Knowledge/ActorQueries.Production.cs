using Engine;

namespace Game.Westworld {
    public sealed partial class AtomicActions {
        public sealed partial class ActorQueries {
            internal (BlockTarget Target, bool InReach)[] FindVisibleLogs() {
                const int radius = 7;
                Vector3 eye = m_port.WorldEyes.EyePosition;
                Vector3 forward = m_port.WorldEyes.EyeRotation.GetForwardVector();
                Point3 center = Terrain.ToCell(eye);
                Terrain terrain = m_port.WorldTerrain.Terrain;
                List<(BlockTarget Target, float Distance)> seen = [];
                for (int x = center.X - radius; x <= center.X + radius; x++) {
                    for (int z = center.Z - radius; z <= center.Z + radius; z++) {
                        for (int y = Math.Max(0, center.Y - 2); y <= Math.Min(255, center.Y + 2); y++) {
                            int value = terrain.GetCellValue(x, y, z);
                            if (WorldBlock(value) is not WoodBlock) continue;
                            Point3 cell = new(x, y, z);
                            Vector3 offset = new Vector3(cell) + new Vector3(.5f) - eye;
                            float distance = offset.Length();
                            if (distance > radius || Vector3.Dot(forward, offset) < .5f * distance) continue;
                            BlockTarget target = new(cell, Terrain.ReplaceLight(value, 0));
                            if (m_port.FindWorldRay(target, RaycastMode.Digging, radius, out _, out object hit) == AtomicActionFailure.None) {
                                seen.Add((target, ((TerrainRaycastResult)hit).Distance));
                            }
                        }
                    }
                }
                return seen.OrderBy(log => log.Distance).Take(6)
                    .Select(log => (log.Target, log.Distance <= m_port.InteractionReach)).ToArray();
            }
        }
    }
}
