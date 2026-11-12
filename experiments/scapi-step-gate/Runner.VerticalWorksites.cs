using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;

sealed partial class Runner
{
    private Vector3 verticalWorkOrigin;
    private Point3 verticalWorkStation;
    private bool verticalWorkSnapshot;
    private bool verticalWorkInteractable;
    private int verticalWorkRoutes;

    private void RunVerticalWorksite()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool below = mode != "worksite-pit-up";
        bool blocked = mode == "worksite-pit-blocked";
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            for (int x = cell.X - 17; x <= cell.X + 10; x += 5)
            for (int z = cell.Z - 5; z <= cell.Z + 5; z += 5)
                if (!(terrain.Terrain.GetChunkAtCell(x, z)?.State >= TerrainChunkState.InvalidVertices1)) return;
            for (int x = cell.X - 17; x <= cell.X + 10; x++)
            for (int z = cell.Z - 5; z <= cell.Z + 5; z++)
            for (int y = cell.Y - 6; y <= cell.Y + 4; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y ? DirtBlock.Index : 0);
            // 坑底到地面的四级楼梯朝工作台的反方向，不能只沿平面距离贪心移动。
            for (int x = cell.X - 2; x <= cell.X + 4; x++)
            for (int z = cell.Z - 2; z <= cell.Z + 2; z++)
            {
                int floor = cell.Y - 4 + Math.Clamp(x - cell.X, 0, 4);
                for (int y = floor; y <= cell.Y + 3; y++) terrain.ChangeCell(x, y, z, 0);
            }
            verticalWorkStation = below ? cell + new Point3(-1, -4, 0) : cell + new Point3(-12, 0, 0);
            terrain.ChangeCell(verticalWorkStation.X, verticalWorkStation.Y, verticalWorkStation.Z, CraftingTableBlock.Index);
            if (below)
            {
                // 站在工作台正上方完整地板，入口仍在四格外，验证同平面坐标并不等于到达。
                for (int x = cell.X - 2; x <= cell.X; x++)
                for (int z = cell.Z - 1; z <= cell.Z + 1; z++)
                    terrain.ChangeCell(x, cell.Y - 1, z, DirtBlock.Index);
                for (int x = cell.X - 2; x <= cell.X + 4; x++)
                    terrain.ChangeCell(x, cell.Y - 1, cell.Z - 2, DirtBlock.Index);
            }
            if (blocked)
                for (int x = cell.X + 1; x <= cell.X + 4; x++)
                for (int z = cell.Z - 2; z <= cell.Z + 2; z++)
                for (int y = cell.Y - 4; y < cell.Y; y++) terrain.ChangeCell(x, y, z, DirtBlock.Index);
            body.Position = verticalWorkOrigin = new(cell.X + .5f, cell.Y - (below ? 0 : 4), cell.Z + .5f);
            body.Velocity = Vector3.Zero;
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            productionStations[CraftingTableBlock.Index] = verticalWorkStation;
            var inventory = p.ComponentMiner.Inventory;
            for (int slot = 0; slot < inventory.SlotsCount; slot++) inventory.RemoveSlotItems(slot, inventory.GetSlotCount(slot));
            inventory.AddSlotItems(0, PlanksBlock.Index, 5);
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            elapsed = 0; stage = 1; return;
        }
        if (elapsed < 1) return;
        if (production is null)
        {
            Gate.Log(new { kind = "vertical_worksite_geometry", mode, position = body.Position.ToString(),
                layers = PlayDryRoutes.LayerSnapshot(memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true))) });
            var snapshot = JsonSerializer.SerializeToElement(PlaySurvivalProduction.WorksiteSnapshot(productionStations,
                actions.Queries.ReadPanorama(30), body.Position));
            verticalWorkSnapshot = snapshot[0].TryGetProperty("heightDifference", out var height)
                && MathF.Abs(height.GetSingle() - (verticalWorkStation.Y - body.Position.Y)) < .1f;
            production = new(p, actions, forageTools!, PlaySurvivalProduction.VisitTable, now, record =>
            {
                Gate.Log(record);
                var row = JsonSerializer.SerializeToElement(record);
                if (row.GetProperty("kind").GetString() is "play_worksite_progressive_route" or "play_worksite_local_route") verticalWorkRoutes++;
            }, productionStations, () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)));
        }
        production.Update(now);
        if (production.Status is null) return;
        if (stage == 1 && production.Status == "succeeded")
        {
            if (MathF.Abs(body.Position.Y - verticalWorkStation.Y) > .1f)
                throw new InvalidOperationException("不同楼层被误判成已到工作台。");
            var ready = JsonSerializer.SerializeToElement(PlaySurvivalProduction.WorksiteSnapshot(productionStations,
                actions.Queries.ReadPanorama(8), body.Position, p, actions.Queries));
            verticalWorkInteractable = ready[0].GetProperty("sameLevel").GetBoolean()
                && ready[0].GetProperty("interactable").GetBoolean();
            production = new(p, actions, forageTools!, PlaySurvivalProduction.Spear, now, Gate.Log, productionStations,
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)));
            stage = 2; return;
        }
        Passed = blocked ? production.Status == "failed" && (body.Position - verticalWorkOrigin).Length() < .5f
            : stage == 2 && verticalWorkInteractable && production.Status == "succeeded"
                && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Spear) == 1;
        Passed &= verticalWorkSnapshot && p.ComponentHealth.Health >= .99f;
        Done = true;
        Gate.Log(new { kind = "vertical_worksite_result", mode, Passed, production.Status, production.Reason, verticalWorkSnapshot,
            verticalWorkRoutes, verticalWorkInteractable, position = body.Position.ToString(), station = verticalWorkStation.ToString(),
            sameLevel = MathF.Abs(body.Position.Y - verticalWorkStation.Y) < .1f });
    }
}
