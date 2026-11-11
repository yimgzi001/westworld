#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;

internal sealed partial class PlaySurvivalProduction
{
    public const string VisitTable = "return_to_own_workbench", VisitFurnace = "return_to_own_furnace", VisitFire = "return_to_own_campfire";
    private bool worksiteMoved, worksiteReached;

    private static int WorksiteBlock(string task) => task switch
    {
        VisitTable => CraftingTableBlock.Index, VisitFurnace => FurnaceBlock.Index, _ => CampfireBlock.Index
    };

    private void AdvanceWorksite(double now)
    {
        int block = WorksiteBlock(Task);
        if (!stations.TryGetValue(block, out var known)) { Finish(false, "own_worksite_not_known"); return; }
        if (campRoute is { } route)
        {
            route.Update(now);
            if (route.Status is null) return;
            campRoute = null;
            if (route.Status != "succeeded") { Finish(false, route.Reason ?? "worksite_route_blocked"); return; }
            worksiteMoved = true;
        }
        var panorama = owner.Queries.ReadPanorama(40f);
        var visible = SeenStation(panorama, stations, block);
        if (visible is not null)
        {
            worksiteReached = true;
            log(new { kind = "play_worksite_reached", actor = player.PlayerData.Name, task = Task,
                cell = known.ToString(), visible.Distance, gameTime = now });
            Finish(true, "own_worksite_reconfirmed_in_reach");
            return;
        }
        if (worksiteMoved || panorama is null) { Finish(false, "own_worksite_not_reconfirmed"); return; }
        var origin = player.ComponentBody.Position;
        if ((Center(known) - origin).XZ.Length() > 40f) { Finish(false, "worksite_beyond_observed_navigation_range"); return; }
        bool Permit(Vector3 end) => permitLandLeg?.Invoke(end, now) != false;
        foreach (var ground in panorama.Blocks.Where(ground => ground.BareTop && PlaySurvivalSenses.IsDryTerrain(ground.Value)
            && Math.Abs(ground.Cell.Y + 1 - known.Y) <= 1
            && (Center(ground.Cell) - Center(known)).XZ.Length() is >= 1.1f and <= 2.7f).OrderBy(ground => ground.Distance).Take(12))
        {
            var destination = new Vector3(ground.Cell.X + 0.5f, ground.Cell.Y + 1f, ground.Cell.Z + 0.5f);
            if (!Permit(destination)) continue;
            var path = PlayObservedPath.Find(panorama, origin, destination, (_, end) => Permit(end), goalReach: 0.4f);
            if (path is null) continue;
            var delta = (destination - origin).XZ;
            campRoute = new(player, owner, MathUtils.RadToDeg(Vector2.Angle(player.ComponentBody.Matrix.Forward.XZ, delta)),
                delta.Length(), "work", false, now, () => (JsonSerializer.SerializeToElement(new { }), []), readDryWindow, log,
                shelterTarget: destination, observedPath: path, permitLandLeg: permitLandLeg);
            return;
        }
        Finish(false, "observed_worksite_approach_unavailable");
    }
}
