#nullable enable

using Engine;
using Game;
using Game.Westworld;

internal sealed partial class PlaySurvivalProduction
{
    public const string PumpkinSeeds = "make_pumpkin_seeds", PlantPumpkins = "plant_emergency_pumpkins", Rake = "craft_wooden_rake";
    internal sealed record Patch(Point3 Cell, double PlantedAt, int LastSize, bool LastDead, double LastSeen);
    private readonly Dictionary<Point3, Patch>? ownFarm;
    private PanoramicBlock? farmPlot;
    private readonly HashSet<Point3> plantedHere = new();
    private int farmStage, plantedCount, seedBefore, seedValue, farmRakeUses;
    private double farmReadyAt;

    private static int PumpkinSeedValue => Terrain.MakeBlockValue(SeedsBlock.Index, 0, (int)SeedsBlock.SeedType.Pumpkin);
    private static bool IsPumpkinSeed(int value) => Terrain.ExtractContents(value) == SeedsBlock.Index
        && Terrain.ExtractData(value) == (int)SeedsBlock.SeedType.Pumpkin;
    private static bool IsMaturePumpkin(int value) => Terrain.ExtractContents(value) == PumpkinBlock.Index
        && BasePumpkinBlock.GetSize(Terrain.ExtractData(value)) == 7;

    private void AdvanceFarm(double now)
    {
        if (now < farmReadyAt) return;
        var carry = player.ComponentMiner.Inventory;
        if (farmStage == 3 && farmPlot is { } planted)
        {
            var terrain = player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain;
            int actual = terrain.GetCellValue(planted.Cell + new Point3(0, 1, 0));
            if (Terrain.ExtractContents(actual) != PumpkinBlock.Index || BasePumpkinBlock.GetSize(Terrain.ExtractData(actual)) != 0
                || CountExact(seedValue) != seedBefore - 1)
            {
                Finish(false, "native_pumpkin_planting_unconfirmed");
                return;
            }
            var cell = planted.Cell + new Point3(0, 1, 0);
            plantedHere.Add(cell);
            if (ownFarm is not null) ownFarm[cell] = new(cell, now, 0, false, now);
            plantedCount++;
            log(new { kind = "play_pumpkin_planted", actor = player.PlayerData.Name, cell = cell.ToString(),
                seedValue, seedBefore, seedAfter = CountExact(seedValue), plantedCount, gameTime = now });
            farmPlot = null;
            farmStage = 0;
        }
        if (plantedCount >= 3 || Count(carry, IsPumpkinSeed) == 0)
        {
            Finish(plantedCount > 0, plantedCount > 0 ? "native_pumpkin_seedlings_confirmed" : "pumpkin_seeds_missing");
            return;
        }
        if (farmStage == 0)
        {
            farmRakeUses = 0;
            var body = player.ComponentBody;
            farmPlot = owner.Queries.ReadPanorama(3.5f)?.Blocks.Where(block => block.BareTop
                && BlocksManager.Blocks[Terrain.ExtractContents(block.Value)] is GrassBlock or DirtBlock or SoilBlock
                && block.Cell.Y == Terrain.ToCell(body.Position.Y) - 1 && SeenPlantingLight(block) >= 9
                && !plantedHere.Contains(block.Cell + new Point3(0, 1, 0))
                && (Center(block.Cell) - body.Position).XZ.Length() is >= 1.1f and <= 2.6f)
                .OrderBy(block => Terrain.ExtractContents(block.Value) == SoilBlock.Index ? 0 : 1)
                .ThenBy(block => block.Distance).FirstOrDefault();
            if (farmPlot is null) { Finish(plantedCount > 0, "no_observed_sunny_planting_ground"); return; }
            action = owner.Start(new FaceTowards(farmPlot.Point, 3f));
            farmStage = 1;
            return;
        }
        var focus = owner.Queries.LookAhead(5f);
        if (focus is not { Kind: FocusKind.Block, Cell: Point3 ground, Face: 4, BlockValue: int groundValue }
            || ground != farmPlot!.Cell)
        {
            Finish(false, "planting_ground_no_longer_in_reach");
            return;
        }
        if (farmStage is 1 or 2 && Terrain.ExtractContents(groundValue) != SoilBlock.Index
            && FindSlot(carry, value => Terrain.ExtractContents(value) == WoodenRakeBlock.Index) >= 0)
        {
            if (farmRakeUses >= 2) { Finish(false, "native_tilled_soil_unconfirmed"); return; }
            if (VisibleFarmItem(value => Terrain.ExtractContents(value) == WoodenRakeBlock.Index) is not { } rake) return;
            action = owner.Start(new UseItem(rake, new BlockTarget(ground, groundValue, 4)));
            farmRakeUses++;
            farmStage = 2;
            farmReadyAt = now + 0.25;
            return;
        }
        if (VisibleFarmItem(IsPumpkinSeed) is not { } seeds) return;
        seedValue = seeds.ExpectedValue;
        seedBefore = CountExact(seedValue);
        action = owner.Start(new Place(new BlockTarget(ground, groundValue, 4), seeds));
        farmStage = 3;
        farmReadyAt = now + 0.25;
    }

    private OwnItemSlot? VisibleFarmItem(Func<int, bool> matches)
    {
        var carry = player.ComponentMiner.Inventory;
        int slot = FindSlot(carry, matches, carry.VisibleSlotsCount);
        if (slot >= 0) return new(slot, carry.GetSlotValue(slot));
        int from = FindSlot(carry, matches);
        int to = Enumerable.Range(0, carry.VisibleSlotsCount).FirstOrDefault(index => carry.GetSlotCount(index) == 0, -1);
        if (from >= 0 && to >= 0) Transfer(carry, from, carry, to);
        else Finish(false, "planting_item_cannot_be_held");
        return null;
    }

    private int SeenPlantingLight(PanoramicBlock ground) => Terrain.ExtractLight(player.Project.FindSubsystem<SubsystemTerrain>(true)
        .Terrain.GetCellValue(ground.Cell + new Point3(0, 1, 0)));
}
