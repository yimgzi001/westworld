#nullable enable

using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

sealed partial class Runner
{
    private Vector3 equipmentFixtureOrigin;
    private Guid equipmentFixtureSource;
    private NearbyPickableView? equipmentFixtureTarget;
    private bool equipmentFixtureExact, equipmentFixtureHidden;

    private void ObserveEquipmentFixture(NativeAcquisitionFact fact)
    {
        if (fact.ActorId == NativeAcquisitionAudit.IdentityFor(p.Entity) && fact.SourceId == equipmentFixtureSource
            && fact.InventoryBefore is int before && fact.InventoryAfter is int after && after > before)
            equipmentFixtureExact = true;
    }

    private void RunEquipmentFixture()
    {
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        var carry = p.ComponentMiner.Inventory;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(p.ComponentBody.Position);
            for (int x = cell.X - 5; x <= cell.X + 14; x++)
            for (int z = cell.Z - 5; z <= cell.Z + 5; z++)
            for (int y = cell.Y - 4; y <= cell.Y + 4; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y - (x >= cell.X + 5 ? 1 : 0) ? DirtBlock.Index : 0);
            p.ComponentBody.Position = equipmentFixtureOrigin = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            p.ComponentBody.Velocity = Vector3.Zero;
            p.ComponentVitalStats.Food = 0.7f;
            p.ComponentVitalStats.Temperature = 12f;
            var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
            calendar.TimeOfDayOffset += 0.3f - calendar.TimeOfDay;
            for (int slot = 0; slot < carry.SlotsCount; slot++) carry.RemoveSlotItems(slot, carry.GetSlotCount(slot));
            var drops = p.Project.FindSubsystem<SubsystemPickables>(true);
            var targetDrop = drops.AddPickable(WoodenSpearBlock.Index, 1, equipmentFixtureOrigin + new Vector3(8, -0.8f, 0), Vector3.Zero, null);
            equipmentFixtureSource = NativeAcquisitionAudit.IdentityFor(targetDrop);
            // 路上另有同类矛，验收指定目标入包，不能拿近处同类物品冒充完成。
            drops.AddPickable(WoodenSpearBlock.Index, 1, equipmentFixtureOrigin + new Vector3(2.5f, 0.2f, 0), Vector3.Zero, null);
            NativeAcquisitionAudit.Recorded += ObserveEquipmentFixture;
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            stage = 1;
            elapsed = 0;
            return;
        }
        if (elapsed < 1) return;
        if (equipmentFixtureTarget is null)
        {
            equipmentFixtureTarget = actions.Queries.FindVisibleObjectsAround(16f).Pickables
                .FirstOrDefault(item => NativeAcquisitionAudit.IdentityFor(item.CreatePickup(1).Target) == equipmentFixtureSource);
            if (equipmentFixtureTarget is null) throw new InvalidOperationException("夹具中的低处投矛应该已被看见。");
            if (mode == "equipment-recover-hidden")
            {
                var cell = Terrain.ToCell(equipmentFixtureOrigin);
                for (int z = cell.Z - 4; z <= cell.Z + 4; z++)
                for (int y = cell.Y; y <= cell.Y + 3; y++) terrain.ChangeCell(cell.X + 4, y, z, GraniteBlock.Index);
                equipmentFixtureHidden = true;
                return;
            }
        }
        var at = Terrain.ToCell(p.ComponentBody.Position);
        if (terrain.Terrain.GetChunkAtCell(at.X, at.Z)?.State != TerrainChunkState.Valid) return;
        production ??= new(p, actions, forageTools!, PlaySurvivalProduction.RecoverSpear, now, Gate.Log, productionStations,
            () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), selectedPickup: equipmentFixtureTarget);
        production.Update(now);
        if (production.Status is null) return;
        int count = PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Spear);
        float moved = (p.ComponentBody.Position - equipmentFixtureOrigin).XZ.Length();
        Passed = equipmentFixtureHidden ? production.Status == "failed" && !equipmentFixtureExact && count == 0 && moved < 0.1f
                && production.Reason == "equipment_target_no_longer_visible"
            : production.Status == "succeeded" && equipmentFixtureExact && production.Added == 1 && count >= 2 && moved >= 6f
                && Math.Abs(p.ComponentBody.Position.Y - equipmentFixtureOrigin.Y + 1f) < 0.1f;
        Passed &= p.ComponentHealth.Health >= 0.95f && PlaySurvivalSenses.OnDryGround(p.ComponentBody);
        NativeAcquisitionAudit.Recorded -= ObserveEquipmentFixture;
        Done = true;
        Gate.Log(new { kind = "equipment_fixture_result", mode, Passed, production.Status, production.Reason,
            equipmentFixtureExact, equipmentFixtureHidden, count, moved, production.Added, modelCalls = 0 });
    }
}
