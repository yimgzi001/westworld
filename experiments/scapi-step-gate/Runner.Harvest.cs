using Engine;
using Game;
using Game.Westworld;

sealed partial class Runner
{
    private Point3 pumpkinCell;
    private bool firstPumpkinBlocked;
    private bool centerHitsPumpkin;

    private void RunPumpkinHarvest()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        int ripe = PumpkinBlock.Index;
        int harvested = Terrain.MakeBlockValue(ripe, 0, BasePumpkinBlock.SetIsDead(0, true));
        var inventory = p.ComponentMiner.Inventory;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var start = Terrain.ToCell(body.Position);
            pumpkinCell = start + new Point3(3, -1, 0);
            for (int x = start.X - 2; x <= start.X + 7; x++)
            for (int z = start.Z - 4; z <= start.Z + 4; z++)
            for (int y = start.Y - 3; y <= start.Y + 3; y++)
            {
                int floor = x < pumpkinCell.X ? start.Y - 1 : start.Y - 2;
                terrain.ChangeCell(x, y, z, y <= floor ? DirtBlock.Index : 0);
            }
            terrain.ChangeCell(pumpkinCell.X, pumpkinCell.Y, pumpkinCell.Z, ripe);
            body.Position = new(start.X + 0.5f, start.Y, start.Z + 0.5f);
            body.Velocity = Vector3.Zero;
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
            if (NativeAcquisitionAudit.InventoryCount(inventory, harvested) != 0)
                throw new InvalidOperationException("夹具开局应没有熟南瓜。");
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            elapsed = 0;
            stage = 1;
            return;
        }
        if (stage == 1)
        {
            if (elapsed < 0.9) return;
            var plant = ReadForage().Foods.FirstOrDefault(item => item.Value == ripe && item.Source == "harvest");
            if (plant is null) throw new InvalidOperationException("一格落差后的南瓜没有进入原生观察。");
            var box = BlocksManager.Blocks[ripe].GetCustomInteractionBoxes(terrain, ripe).Single();
            var eye = p.ComponentCreatureModel.EyePosition;
            var ray = new Ray3(eye, Vector3.Normalize(new Vector3(pumpkinCell) + box.Center() - eye));
            var center = p.ComponentMiner.Raycast(ray, RaycastMode.Digging, Reach: 5);
            centerHitsPumpkin = center is TerrainRaycastResult hit && hit.CellFace.Point == pumpkinCell;
            var receipt = forageTools!.Call("act_Dig", new { target = plant.Reference, timeout = 5 });
            string? status = receipt.State == "returned" ? receipt.Data.GetProperty("status").GetString() : null;
            Gate.Log(new { kind = "pumpkin_harvest_started", centerHitsPumpkin, plant = plant.Point.ToString(),
                origin = body.Position.ToString(), box = box.ToString(), receipt = receipt.Data });
            if (!firstPumpkinBlocked && status == "failed" && receipt.Data.GetProperty("failure").GetString() == "blocked")
            {
                firstPumpkinBlocked = true;
                travel = new(p, actions, 0f, 1.05f, "survey", false, now, ReadForage,
                    () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), Gate.Log);
                stage = 4;
                return;
            }
            if (status != "running")
                throw new InvalidOperationException("南瓜边缘的原生挖掘没有开始。");
            stage = 2;
            return;
        }
        if (stage == 4)
        {
            travel!.Update(now);
            if (travel.Status is null) return;
            if (travel.Status != "succeeded" || (body.Position - travel.Origin).XZ.Length() < 0.5f)
                throw new InvalidOperationException("南瓜受阻后没有移动到已确认的落脚点。");
            travel = null;
            stage = 1;
            return;
        }
        if (stage == 2)
        {
            if (actions.IsActionRunning) return;
            var receipt = forageTools!.Call("action_status", new { });
            if (receipt.Data.GetProperty("status").GetString() != "succeeded"
                || terrain.Terrain.GetCellContents(pumpkinCell.X, pumpkinCell.Y, pumpkinCell.Z) != 0)
                throw new InvalidOperationException("南瓜方块没有经原生挖掘被移除。");
            stage = 3;
        }
        if (travel is { Status: null } moving)
        {
            moving.Update(now);
            if (moving.Status is null) return;
        }
        if (NativeAcquisitionAudit.InventoryCount(inventory, harvested) > 0)
        {
            bool landed = PlaySurvivalSenses.OnDryGround(body) && MathF.Abs(body.Position.Y - pumpkinCell.Y) < 0.05f;
            Passed = firstPumpkinBlocked && !centerHitsPumpkin && landed && p.ComponentHealth.Health >= 0.99f;
            Done = true;
            Gate.Log(new { kind = "pumpkin_harvest_result", Passed, centerHitsPumpkin, landed,
                received = NativeAcquisitionAudit.InventoryCount(inventory, harvested), health = p.ComponentHealth.Health });
            return;
        }
        var visible = ReadForage().Foods.FirstOrDefault(item => item.Value == harvested && item.Source == "pickup");
        if (visible is { InReach: true })
        {
            travel?.Stop();
            var receipt = forageTools!.Call("act_PickUp", new { target = visible.Reference, count = 1 });
            if (receipt.State != "returned" || receipt.Data.GetProperty("status").GetString() != "succeeded")
                throw new InvalidOperationException("南瓜原生拾取失败。");
            return;
        }
        if (travel is null)
        {
            if (visible is null) return;
            var offset = (visible.Point - body.Position).XZ;
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
            travel = new(p, actions, bearing, offset.Length() + 1f, "food", false, now, ReadForage,
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), Gate.Log, foodTarget: visible);
        }
        if (travel.Status == "failed") throw new InvalidOperationException($"无法走到南瓜掉落旁。{travel.Reason}");
    }
}
