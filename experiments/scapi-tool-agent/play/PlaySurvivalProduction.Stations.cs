#nullable enable

using Engine;
using Game;
using Game.Westworld;

internal sealed partial class PlaySurvivalProduction
{
    private const float StationReach = 4.5f;
    private static readonly Point3 Up = new(0, 1, 0);
    private Point3 stationCell;
    private PanoramicBlock? placementFloor;
    private int stationBlock;
    private Vector3? stationAim;
    private Phase afterStationClear;
    private int stationPlantsCleared;
    private readonly HashSet<Point3> refusedStationFloors = new();
    private PlaySurvivalProduction? stationApproach;
    private bool stationApproachTried;

    private static void AddStationOffers(ComponentPlayer player, ActorPanorama? panorama, Dictionary<int, Point3> stations,
        List<string> available)
    {
        var entities = player.Project.FindSubsystem<SubsystemBlockEntities>(true);
        var workbench = SeenStation(panorama, stations, CraftingTableBlock.Index);
        var table = workbench is not null ? entities.GetBlockEntity(workbench.Cell)?.Entity.FindComponent<ComponentCraftingTable>() : null;
        int stagedStone = table is null ? 0 : Count(table, value => Terrain.ExtractContents(value) == CobblestoneBlock.Index,
            table.ResultSlotIndex);
        if ((table is not null || Stock(player, Workbench) > 0) && Stock(player, Stone) + stagedStone >= 8
            && Stock(player, Furnace) == 0 && SeenStation(panorama, stations, FurnaceBlock.Index) is null)
            available.Add(Furnace);

        var furnace = SeenStation(panorama, stations, FurnaceBlock.Index);
        var oven = furnace is not null ? entities.GetBlockEntity(furnace.Cell)?.Entity.FindComponent<ComponentFurnace>() : null;
        var carry = player.ComponentMiner.Inventory;
        bool hasRawMeat = Count(carry, PlaySurvivalFood.IsRawMeat) > 0;
        bool hasFuel = Count(carry, value => FuelSuitable(value, 1f, 6.67f)) > 0;
        if (oven is not null && (oven.GetSlotCount(0) > 0 || oven.GetSlotCount(oven.ResultSlotIndex) > 0)
            || (oven is not null || Stock(player, Furnace) > 0) && hasRawMeat && hasFuel)
            available.Add(Cook);
        if (Stock(player, Fire) > 0 && player.ComponentVitalStats.Temperature < 8f) available.Add(Warm);
        if (SeenStation(panorama, stations, CampfireBlock.Index) is { } fire && Terrain.ExtractData(fire.Value) > 0)
        {
            if (Terrain.ExtractData(fire.Value) < 15 && Count(carry, CampfireFuel) > 0) available.Add(TendFire);
            if (PlayThermalRecovery.FireWithinReach(player, fire.Cell)) available.Add(WarmAtFire);
        }
    }

    private static PanoramicBlock? SeenStation(ActorPanorama? panorama, Dictionary<int, Point3> stations, int block, float reach = StationReach) =>
        stations.TryGetValue(block, out var cell)
            ? panorama?.Blocks.FirstOrDefault(item => item.Cell == cell && item.Distance <= reach && StationMatches(item.Value, block))
            : null;

    private static bool StationMatches(int value, int block)
    {
        int contents = Terrain.ExtractContents(value);
        return contents == block || block == FurnaceBlock.Index && contents == LitFurnaceBlock.Index;
    }

    private void StartStation()
    {
        stationBlock = Task switch { Furnace or Spear or Rake or Axe => CraftingTableBlock.Index, Cook => FurnaceBlock.Index, _ => CampfireBlock.Index };
        // 全景先按方块中心筛选，再量到可见表面。留出中心到表面的距离，交互仍按原距离核对。
        var panorama = owner.Queries.ReadPanorama(StationReach + 0.6f);
        if (Task != Warm && SeenStation(panorama, stations, stationBlock) is { } existing
            && Math.Abs(player.ComponentBody.Position.Y - existing.Cell.Y) < 0.1f
            && (Center(existing.Cell) - player.ComponentBody.Position).XZ.LengthSquared() <= 9f)
        {
            stationCell = existing.Cell;
            AimStation(Center(existing.Cell));
            return;
        }
        if (Task != Warm && !stationApproachTried && stations.TryGetValue(stationBlock, out var known)
            && (Center(known) - player.ComponentBody.Position).XZ.LengthSquared() <= 40f * 40f)
        {
            // AI 已决定制作或烤肉；走近本人工作站属于该动作的执行前置，不另替它安排发展路线。
            stationApproachTried = true;
            CurrentStage = "approach_own_station_for_selected_job";
            stationApproach = new(player, owner, tools, stationBlock == CraftingTableBlock.Index ? VisitTable : VisitFurnace,
                player.Project.FindSubsystem<SubsystemTime>(true).GameTime, log, stations, readDryWindow, permitLandLeg,
                ownFarm: ownFarm, camp: camp, preserveGround: preserveGround);
            return;
        }
        var carry = player.ComponentMiner.Inventory;
        int slot = FindSlot(carry, value => Terrain.ExtractContents(value) == stationBlock, carry.VisibleSlotsCount);
        if (slot < 0)
        {
            int from = FindSlot(carry, value => Terrain.ExtractContents(value) == stationBlock);
            int to = Enumerable.Range(0, carry.VisibleSlotsCount).FirstOrDefault(index => carry.GetSlotCount(index) == 0, -1);
            if (from < 0 || to < 0) { Finish(false, "station_item_unavailable"); return; }
            Transfer(carry, from, carry, to);
            return;
        }

        var body = player.ComponentBody.Position;
        int ground = Terrain.ToCell(body.Y + 0.05f) - 1;
        placementFloor = panorama?.Blocks.Where(block => block.BareTop && block.Cell.Y == ground && !refusedStationFloors.Contains(block.Cell)
                && BlocksManager.Blocks[Terrain.ExtractContents(block.Value)] is CubeBlock cube && cube.IsCollidable_(block.Value))
            .Where(block => (Center(block.Cell) - body).XZ.Length() is >= 1.05f and <= 2.55f
                && (stationBlock != CampfireBlock.Index || PlayFireSafety.CampfireSpacingSafe(player, block.Cell + Up)))
            .MinBy(block => (Center(block.Cell) - body).XZ.LengthSquared());
        if (placementFloor is null) { Finish(false, "no_observed_station_floor"); return; }
        stationCell = placementFloor.Cell + Up;
        stationAim = Center(stationCell) - 0.55f * Vector3.UnitY;
        action = owner.Start(new FaceTowards(stationAim.Value, 3f));
        phase = Phase.PlaceStation;
    }

    private void PlaceStation()
    {
        if (ClearTrivialStationObstruction(Phase.PlaceStation)) return;
        var carry = player.ComponentMiner.Inventory;
        int slot = FindSlot(carry, value => Terrain.ExtractContents(value) == stationBlock, carry.VisibleSlotsCount);
        var seen = owner.Queries.ReadPanorama(StationReach + 0.6f);
        var floor = seen?.Blocks.FirstOrDefault(block => block.Cell == placementFloor!.Cell && block.BareTop
            && Terrain.ExtractContents(block.Value) == Terrain.ExtractContents(placementFloor.Value));
        var cover = seen?.Blocks.FirstOrDefault(block => block.Cell == stationCell);
        if (cover is not null && TrivialPlant(cover.Value) && stationPlantsCleared < 8)
        {
            ClearStationPlant(cover.Cell, cover.Value, Phase.PlaceStation);
            return;
        }
        if (slot < 0 || floor is null || cover is not null)
        {
            log(new { kind = "play_station_floor_rejected", actor = player.PlayerData.Name, stationBlock,
                expected = placementFloor?.Cell.ToString(), obstruction = cover?.Cell.ToString() });
            if (placementFloor is { } refused && refusedStationFloors.Count < 6)
            {
                refusedStationFloors.Add(refused.Cell);
                phase = Phase.Start;
            }
            else Finish(false, "station_floor_no_longer_usable");
            return;
        }
        if (stationBlock == CampfireBlock.Index && !PlayFireSafety.CampfireSpacingSafe(player, floor.Cell + Up))
        {
            Finish(false, "campfire_placement_too_close_to_body");
            return;
        }
        // 可见地面绑定具体格与原值，原生放置再次校验射线、面、遮挡和碰撞，不要求准星恰好命中方块中心。
        action = owner.Start(new Place(new BlockTarget(floor.Cell, floor.Value, 4), new OwnItemSlot(slot, carry.GetSlotValue(slot))));
        phase = Phase.AimStation;
    }

    private bool ConfirmStationPlacement(double gameTime)
    {
        var terrain = player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain;
        int value = terrain.GetCellValue(stationCell.X, stationCell.Y, stationCell.Z);
        if (!StationMatches(value, stationBlock))
        {
            Finish(false, "station_placement_not_confirmed");
            return false;
        }
        stations[stationBlock] = stationCell;
        log(new { kind = "play_station_placed", actor = player.PlayerData.Name, stationBlock,
            cell = stationCell.ToString(), gameTime });
        if (Task != Warm) return true;
        bool heat = BlocksManager.Blocks[Terrain.ExtractContents(value)].GetHeat(value) > 0f;
        Finish(heat, heat ? "campfire_lit" : "campfire_heat_unconfirmed");
        return false;
    }

    private void AimStation(Vector3 point)
    {
        stationAim = point;
        action = owner.Start(new FaceTowards(point, 3f));
        phase = Task is Furnace or Spear or Rake or Axe ? Phase.Craft : Phase.Cook;
    }

    private bool ClearTrivialStationObstruction(Phase resume)
    {
        if (stationAim is not { } aim || stationPlantsCleared >= 8) return false;
        var focus = owner.Queries.LookAhead(5f);
        if (focus is not { Kind: FocusKind.Block, Cell: Point3 cell, Face: int face, BlockValue: int value }
            || focus.Distance > (aim - player.ComponentCreatureModel.EyePosition).Length() + 0.2f
            || !TrivialPlant(value)) return false;
        // 只清理实际挡住交互射线的草、花和小枯灌木；作物、木块与未知方块不擅自挖掉。
        ClearStationPlant(cell, value, resume);
        return true;
    }

    private void ClearStationPlant(Point3 cell, int value, Phase resume)
    {
        stationPlantsCleared++;
        afterStationClear = resume;
        action = owner.Start(new Dig(new BlockTarget(cell, value), 4f));
        phase = Phase.ClearStationSight;
        log(new { kind = "play_station_plant_clearing", actor = player.PlayerData.Name, cell = cell.ToString(),
            plant = BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(null, value), stationPlantsCleared });
    }

    private static bool TrivialPlant(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)]
        is TallGrassBlock or FlowerBlock or DryBushBlock;

    private T? ObservedStation<T>() where T : GameEntitySystem.Component
    {
        var focus = owner.Queries.LookAhead(5);
        if (focus is not { Kind: FocusKind.Block, Cell: Point3 cell, BlockValue: int value }
            || cell != stationCell || !StationMatches(value, stationBlock)) return null;
        return player.Project.FindSubsystem<SubsystemBlockEntities>(true).GetBlockEntity(cell)?.Entity.FindComponent<T>();
    }

    private static Vector3 Center(Point3 cell) => new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

    private static int FindSlot(IInventory inventory, Func<int, bool> matches, int? limit = null) =>
        Enumerable.Range(0, limit ?? inventory.SlotsCount).FirstOrDefault(slot => inventory.GetSlotCount(slot) > 0
            && matches(inventory.GetSlotValue(slot)), -1);

    private void Transfer(IInventory source, int from, IInventory destination, int to) => action = owner.Start(new TransferItems(
        new InventorySlot(source, from, source.GetSlotValue(from)), new InventorySlot(destination, to, destination.GetSlotValue(to)), 1));
}
