#nullable enable

using Engine;
using Game;
using Game.Westworld;

internal sealed partial class PlaySurvivalProduction
{
    public const string TendFire = "add_fuel_to_own_campfire", WarmAtFire = "warm_at_own_campfire";
    private int fireStage, fireFuelValue, fireStockBefore, fireSizeBefore, fireFuelAdded;

    private static bool CampfireFuel(int value) => FuelSuitable(value, 0.1f, 10f);

    private void ConfirmRecoveryFire()
    {
        if (!stations.TryGetValue(CampfireBlock.Index, out var known)
            || SeenStation(owner.Queries.ReadPanorama(4f), stations, CampfireBlock.Index, 2.8f) is not { } fire)
        { Finish(false, "own_campfire_not_in_warming_reach"); return; }
        if (Terrain.ExtractData(fire.Value) == 0)
        {
            stations.Remove(CampfireBlock.Index);
            Finish(false, "own_campfire_has_burned_out");
            return;
        }
        Finish(PlayThermalRecovery.FireWithinReach(player, known), "native_fire_ready_for_recovery");
    }

    private void AdvanceFireTending()
    {
        var carry = player.ComponentMiner.Inventory;
        if (fireStage == 2)
        {
            if (player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain
                .GetChunkAtCell(stationCell.X, stationCell.Z)?.State != TerrainChunkState.Valid) return;
            var fire = SeenStation(owner.Queries.ReadPanorama(5f), stations, CampfireBlock.Index);
            int after = CountExact(fireFuelValue);
            int size = fire is null ? 0 : Terrain.ExtractData(fire.Value);
            bool conserved = after == fireStockBefore - 1 && size > fireSizeBefore;
            if (conserved) fireFuelAdded = 1;
            log(new { kind = "play_campfire_fuel_result", actor = player.PlayerData.Name, conserved,
                fuelValue = fireFuelValue, before = fireStockBefore, after, fireBefore = fireSizeBefore, fireAfter = size });
            Finish(conserved, conserved ? "native_campfire_fuel_added" : "native_campfire_fuel_not_confirmed");
            return;
        }
        if (fireStage == 0)
        {
            var fire = SeenStation(owner.Queries.ReadPanorama(5f), stations, CampfireBlock.Index);
            if (fire is null) { Finish(false, "own_campfire_not_in_fuel_reach"); return; }
            if (Terrain.ExtractData(fire.Value) == 0) { Finish(false, "cannot_refuel_extinguished_campfire"); return; }
            if (Terrain.ExtractData(fire.Value) >= 15) { Finish(true, "campfire_already_fueled"); return; }
            int slot = FindSlot(carry, CampfireFuel, carry.VisibleSlotsCount);
            if (slot < 0)
            {
                int from = FindSlot(carry, CampfireFuel);
                int to = Enumerable.Range(0, carry.VisibleSlotsCount).FirstOrDefault(index => carry.GetSlotCount(index) == 0, -1);
                if (from < 0 || to < 0) { Finish(false, "campfire_fuel_unavailable_or_not_holdable"); return; }
                Transfer(carry, from, carry, to);
                return;
            }
            fireFuelValue = carry.GetSlotValue(slot);
            if (player.ComponentMiner.ActiveBlockValue != fireFuelValue)
            {
                action = owner.Start(new SelectItem(slot, fireFuelValue));
                return;
            }
            stationCell = fire.Cell;
            stationAim = fire.Point;
            fireStage = 1;
            action = owner.Start(new FaceTowards(fire.Point, 3f));
            return;
        }
        var currentFire = SeenStation(owner.Queries.ReadPanorama(5f), stations, CampfireBlock.Index);
        if (currentFire is null || currentFire.Cell != stationCell || Terrain.ExtractData(currentFire.Value) == 0)
        { Finish(false, "campfire_fuel_target_changed"); return; }
        if (player.ComponentMiner.ActiveBlockValue != fireFuelValue)
        {
            int slot = FindSlot(carry, value => value == fireFuelValue, carry.VisibleSlotsCount);
            if (slot < 0) { Finish(false, "campfire_fuel_item_changed"); return; }
            action = owner.Start(new SelectItem(slot, fireFuelValue));
            return;
        }
        fireStockBefore = CountExact(fireFuelValue);
        fireSizeBefore = Terrain.ExtractData(currentFire.Value);
        action = owner.Start(new Interact(new BlockTarget(currentFire.Cell, currentFire.Value)));
        fireStage = 2;
    }

    private static bool CampfireSeenOut(ActorPanorama? panorama, Dictionary<int, Point3> stations) =>
        SeenStation(panorama, stations, CampfireBlock.Index, ProjectReach) is { } fire && Terrain.ExtractData(fire.Value) == 0;

    public static object[] WorksiteSnapshot(Dictionary<int, Point3> stations, ActorPanorama? panorama, Vector3 origin) =>
        stations.Select(site =>
        {
            var seen = panorama?.Blocks.FirstOrDefault(block => block.Cell == site.Value && StationMatches(block.Value, site.Key));
            return (object)new { type = site.Key == CraftingTableBlock.Index ? "workbench" : site.Key == FurnaceBlock.Index ? "furnace" : "campfire",
                cell = site.Value.ToString(), distance = Math.Round((Center(site.Value) - origin).XZ.Length(), 1),
                currentlySeen = seen is not null,
                fireState = site.Key != CampfireBlock.Index || seen is null ? null : Terrain.ExtractData(seen.Value) > 0 ? "burning" : "out",
                fireFuelSteps = site.Key == CampfireBlock.Index && seen is not null ? Terrain.ExtractData(seen.Value) : (int?)null };
        }).ToArray();
}
