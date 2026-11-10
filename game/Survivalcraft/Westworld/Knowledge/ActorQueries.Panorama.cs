#nullable enable

using Engine;

namespace Game.Westworld {
    public sealed record PanoramicBlock(Point3 Cell, Vector3 Point, int Value, float Distance, bool BareTop);
    public sealed record ActorPanorama(float Radius, NearbyView Objects, IReadOnlyList<PanoramicBlock> Blocks, bool UnloadedColumns);

    public sealed partial class AtomicActions {
        public sealed partial class ActorQueries {
            public ActorPanorama? ReadPanorama() {
                if (!m_port.IsActorAvailable || !m_port.CanTargetWorld() || m_port.m_sleep?.IsSleeping == true) return null;
                float radius = Math.Min(SettingsManager.VisibilityRange, 32);
                if (radius <= 0) return null;
                var eye = m_port.WorldEyes.EyePosition;
                if (!float.IsFinite(eye.X) || !float.IsFinite(eye.Y) || !float.IsFinite(eye.Z) || eye.Y is < 0 or > 256)
                    return null;
                var terrain = m_port.WorldTerrain.Terrain;
                var objects = FindVisibleObjectsAround(radius);
                var blocks = ReadPanoramicSurfaces(terrain, eye, radius, out bool unloaded);
                return new(radius, objects, blocks, unloaded);
            }

            private static bool PanoramaRayLoaded(Terrain terrain, Vector3 eye, Vector3 target, float radius) {
                if (terrain.GetChunkAtCell(Terrain.ToCell(target.X), Terrain.ToCell(target.Z))?.State != TerrainChunkState.Valid)
                    return false;
                return VoxelSurfaceGeometry.LineOfSight(Point(eye), Point(target),
                    (x, _, z) => terrain.GetChunkAtCell(x, z)?.State == TerrainChunkState.Valid ? false : null, radius);
            }

            private static IReadOnlyList<PanoramicBlock> ReadPanoramicSurfaces(Terrain terrain, Vector3 eye, float radius,
                out bool unloaded) {
                // 仅缓存视距包围盒内的已加载列。只有有表面且射线无遮挡的方块才进入观察结果。
                var min = Terrain.ToCell(eye - new Vector3(radius + 1));
                var max = Terrain.ToCell(eye + new Vector3(radius + 1));
                min.Y = Math.Max(0, min.Y);
                max.Y = Math.Min(TerrainChunk.Height - 1, max.Y);
                int height = max.Y - min.Y + 1;
                int width = max.X - min.X + 1;
                var values = new int[width * (max.Z - min.Z + 1) * height];
                unloaded = false;
                int Column(int x, int z) => ((z - min.Z) * width + x - min.X) * height;
                int Value(int x, int y, int z) => x < min.X || x > max.X || y < min.Y || y > max.Y || z < min.Z || z > max.Z
                    ? -1 : values[Column(x, z) + y - min.Y];
                for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++) {
                    int column = Column(x, z);
                    var chunk = terrain.GetChunkAtCell(x, z);
                    if (chunk?.State != TerrainChunkState.Valid) {
                        Array.Fill(values, -1, column, height);
                        unloaded = true;
                        continue;
                    }
                    int start = TerrainChunk.CalculateCellIndex(x & 15, min.Y, z & 15);
                    for (int offset = 0; offset < height; offset++) values[column + offset] = chunk.GetCellValueFast(start + offset);
                }

                var result = new List<PanoramicBlock>();
                var viewpoint = Point(eye);
                float limit = radius * radius;
                bool? Opaque(int x, int y, int z) {
                    int value = Value(x, y, z);
                    return value < 0 ? null : !BlocksManager.Blocks[Terrain.ExtractContents(value)].IsTransparent_(value);
                }
                bool Exposed(int x, int y, int z, int contents) {
                    int neighbor = Value(x, y, z);
                    int type = Terrain.ExtractContents(neighbor);
                    return neighbor >= 0 && type != contents && BlocksManager.Blocks[type].IsTransparent_(neighbor);
                }
                for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++)
                for (int y = min.Y; y <= max.Y; y++) {
                    int value = Value(x, y, z);
                    int contents = Terrain.ExtractContents(value);
                    if (value < 0 || contents == 0) continue;
                    var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                    if ((center - eye).LengthSquared() > limit) continue;
                    bool exposedX = eye.X < x && Exposed(x - 1, y, z, contents)
                        || eye.X > x + 1 && Exposed(x + 1, y, z, contents);
                    bool exposedY = eye.Y < y && Exposed(x, y - 1, z, contents)
                        || eye.Y > y + 1 && Exposed(x, y + 1, z, contents);
                    bool exposedZ = eye.Z < z && Exposed(x, y, z - 1, contents)
                        || eye.Z > z + 1 && Exposed(x, y, z + 1, contents);
                    if (!exposedX && !exposedY && !exposedZ) continue;
                    int above = Value(x, y + 1, z);
                    var top = VoxelSurfaceGeometry.SupportTop(x, y, z);
                    if (eye.Y > y + 1 && above >= 0 && Terrain.ExtractContents(above) == 0
                        && VoxelSurfaceGeometry.LineOfSight(viewpoint, top, Opaque, radius)) {
                        Add(top, bareTop: true);
                        continue;
                    }
                    foreach (var sample in VoxelSurfaceGeometry.Samples(viewpoint, x, y, z)) {
                        if (!VoxelSurfaceGeometry.LineOfSight(viewpoint, sample, Opaque, radius)) continue;
                        Add(sample, bareTop: false);
                        break;
                    }
                    void Add(VisibilityPoint sample, bool bareTop) {
                        var point = new Vector3((float)sample.X, (float)sample.Y, (float)sample.Z);
                        result.Add(new(new Point3(x, y, z), point, Terrain.ReplaceLight(value, 0), (point - eye).Length(), bareTop));
                    }
                }
                return result;
            }

            private static VisibilityPoint Point(Vector3 value) => new(value.X, value.Y, value.Z);
        }
    }
}
