using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;

sealed partial class Runner
{
    private int frontierFailures;
    private bool frontierWet;
    private Vector3 frontierFixtureOrigin;
    private void RunNaturalFrontier()
    {
        const int terrainKey = 33006;
        bool terrace = mode is "navigate-terrace-loop" or "navigate-momentum-loop";
        bool bay = mode == "explore-water-bay";
        var body = p.ComponentBody;
        var origin = bay ? new Vector3(Terrain.ToCell(body.Position.X) + .5f, 65, Terrain.ToCell(body.Position.Z) + .5f)
            : terrace ? new Vector3(-118.64353f, 68, 73.01958f) : new Vector3(-147.50978f, 65, 78.20406f);
        var heading = bay ? Vector2.UnitX : terrace ? -Vector2.UnitX : -Vector2.UnitY;
        if (stage == 0)
        {
            terrain.TerrainUpdater.SetUpdateLocation(terrainKey, origin.XZ, 26, 48);
            stage = 1; return;
        }
        if (stage == 1)
        {
            if (!DismountedForFixture(body)) return;
            for (int x = Terrain.ToCell(origin.X) - 22; x <= origin.X + 22; x += 11)
            for (int z = Terrain.ToCell(origin.Z) - 22; z <= origin.Z + 22; z += 11)
                if (!(terrain.Terrain.GetChunkAtCell(x, z)?.State >= TerrainChunkState.InvalidVertices1)) return;
            if (bay)
            {
                var cell = Terrain.ToCell(origin);
                for (int x = cell.X - 5; x <= cell.X + 26; x++)
                for (int z = cell.Z - 10; z <= cell.Z + 10; z++)
                for (int y = cell.Y - 3; y <= cell.Y + 4; y++)
                {
                    bool water = x >= cell.X + 3 && x <= cell.X + 18 && Math.Abs(z - cell.Z) <= 3;
                    int value = y < cell.Y ? water && y >= cell.Y - 2 ? WaterBlock.Index : DirtBlock.Index : 0;
                    if (water && y == cell.Y - 1) value = Terrain.MakeBlockValue(WaterBlock.Index, 0, FluidBlock.SetIsTop(0, true));
                    terrain.ChangeCell(x, y, z, value);
                }
            }
            body.Position = origin; body.Velocity = Vector3.Zero;
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, bay ? -MathF.PI / 2 : terrace ? MathF.PI / 2 : 0);
            stage = 2; elapsed = 0; return;
        }
        if (elapsed < .9) return;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if (travel is null)
        {
            terrain.TerrainUpdater.RemoveUpdateLocation(terrainKey);
            if (!PlaySurvivalSenses.OnDryGround(body)) throw new InvalidOperationException("自然场岸沿或台阶卡点没有实际干地支撑。");
            frontierFixtureOrigin = body.Position;
            if (mode == "navigate-momentum-loop") body.Velocity = new(0, 0, 2.2f);
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, heading));
            travel = new(p, actions, bearing, 20, "search", false, now, () => (default, []),
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), record =>
                {
                    Gate.Log(record);
                    var row = JsonSerializer.SerializeToElement(record);
                    if (row.GetProperty("kind").GetString() == "play_scout_leg" && row.GetProperty("nativeStatus").GetString() == "Failed")
                        frontierFailures++;
                });
        }
        frontierWet |= body.ImmersionDepth > .08f;
        travel.Update(now);
        if (travel.Status is null) return;
        float moved = (body.Position - frontierFixtureOrigin).XZ.Length();
        float gain = Vector2.Dot((body.Position - frontierFixtureOrigin).XZ, heading);
        Passed = travel.Status == "succeeded" && moved >= 3 && gain >= 2 && !frontierWet && frontierFailures <= 2
            && PlaySurvivalSenses.OnStableDryGround(body) && p.ComponentHealth.Health >= .99f;
        if (bay) Passed &= gain >= 14;
        Done = true;
        Gate.Log(new { kind = "natural_frontier_result", mode, Passed, travel.Status, travel.Reason, travel.Legs,
            frontierFailures, frontierWet, moved, gain, position = body.Position.ToString() });
    }
}
