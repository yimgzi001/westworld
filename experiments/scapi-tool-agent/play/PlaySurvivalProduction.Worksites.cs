#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;

internal sealed partial class PlaySurvivalProduction
{
    public const string VisitTable = "return_to_own_workbench", VisitFurnace = "return_to_own_furnace", VisitFire = "return_to_own_campfire";
    public const float KnownReturnReach = 128f;
    private const int KnownReturnLegLimit = 16;
    private bool worksiteReached;
    private int worksiteLegs;
    private double knownReturnStarted;
    private float knownReturnHealth;

    public string ActivityDescription => stationApproach?.ActivityDescription ?? (CurrentStage switch
    {
        "climb_to_worksite_level" => "先沿已见台阶上到工作站所在层",
        "descend_to_worksite_level" => "先沿已见入口下到工作站所在层",
        "follow_observed_worksite_route" => "沿已见三维路线返回工作站",
        _ => DescribeTask(Task)
    });

    public static object? ReturnJourney(string task, Dictionary<int, Point3> stations, Vector3 origin)
    {
        if (task is not (VisitTable or VisitFurnace or VisitFire) || !stations.TryGetValue(WorksiteBlock(task), out var cell)) return null;
        float distance = (Center(cell) - origin).XZ.Length();
        float height = cell.Y - origin.Y;
        return new { distance = Math.Round(distance, 1), heightDifference = Math.Round(height, 1),
            sameLevel = MathF.Abs(height) < .1f, estimatedTravelSeconds = Math.Ceiling((distance + 2 * MathF.Abs(height)) / 2f),
            routeNeedsRechecking = true, maximumReturnDistance = KnownReturnReach };
    }

    private bool CanContinueKnownReturn(PlaySurvivalTravel route, Vector3 target, double now)
    {
        var body = player.ComponentBody;
        bool segmentEnded = route.Reason == "navigation_distance_limit"
            || route.Reason == "travel_interrupted" && now - knownReturnStarted >= 24.5;
        float progress = PlayObservedPath.RemainingWork(route.Origin, target) - PlayObservedPath.RemainingWork(body.Position, target);
        // 时间段用完且确实走近才续接。受伤、落水、无路或原地绕圈不能靠重试掩盖。
        if (!segmentEnded || progress < 2f || player.ComponentHealth.Health < knownReturnHealth - 0.001f
            || !PlaySurvivalSenses.OnDryGround(body) || Math.Abs(body.Velocity.Y) >= 0.6f) return false;
        log(new { kind = "play_known_return_progress", actor = player.PlayerData.Name, task = Task,
            progress, remaining = (body.Position - target).XZ.Length(), route.Reason, gameTime = now });
        return true;
    }

    private static int WorksiteBlock(string task) => task switch
    {
        VisitTable => CraftingTableBlock.Index, VisitFurnace => FurnaceBlock.Index, _ => CampfireBlock.Index
    };

    private void AdvanceWorksite(double now)
    {
        int block = WorksiteBlock(Task);
        if (!stations.TryGetValue(block, out var known)) { Finish(false, "own_worksite_not_known"); return; }
        PlaySurvivalTravel? completed = null;
        if (campRoute is { } route)
        {
            route.Update(now);
            if (route.Status is null) return;
            campRoute = null;
            completed = route;
        }
        var panorama = PlayObservedPath.WithLocalGround(owner.Queries.ReadPanorama(40f), readDryWindow());
        float reach = block == CampfireBlock.Index ? 2.8f : StationReach;
        var visible = SeenStation(panorama, stations, block, reach);
        if (visible is not null && WorksiteAccessible(visible, block))
        {
            if (block == CampfireBlock.Index && Terrain.ExtractData(visible.Value) == 0)
            {
                stations.Remove(block);
                Finish(false, "own_campfire_has_burned_out");
                return;
            }
            worksiteReached = true;
            log(new { kind = "play_worksite_reached", actor = player.PlayerData.Name, task = Task,
                cell = known.ToString(), visible.Distance, gameTime = now });
            Finish(true, "own_worksite_reconfirmed_in_reach");
            return;
        }
        if (worksiteLegs >= KnownReturnLegLimit || panorama is null) { Finish(false, "own_worksite_not_reconfirmed"); return; }
        var origin = player.ComponentBody.Position;
        if (completed is { Status: "succeeded" })
        {
            float progressMade = PlayObservedPath.RemainingWork(completed.Origin, new(known.X + .5f, known.Y, known.Z + .5f))
                - PlayObservedPath.RemainingWork(origin, new(known.X + .5f, known.Y, known.Z + .5f));
            if (progressMade > .5f) log(new { kind = "play_known_return_progress", actor = player.PlayerData.Name, task = Task,
                progress = progressMade, remaining = (origin - Center(known)).XZ.Length(), completed.Reason, gameTime = now });
        }
        if ((Center(known) - origin).XZ.Length() > KnownReturnReach) { Finish(false, "worksite_beyond_return_range"); return; }
        bool Permit(Vector3 end) => permitLandLeg?.Invoke(end, now) != false;
        if ((Center(known) - origin).LengthSquared() < 36f)
        {
            var seenCell = readDryWindow().Cells.FirstOrDefault(cell => cell.IsCurrent
                && cell.X == known.X && cell.Y == known.Y && cell.Z == known.Z);
            if (seenCell is not null && !StationMatches(seenCell.Value, block))
            {
                stations.Remove(block);
                Finish(false, "own_worksite_observed_changed");
                return;
            }
        }
        if (completed is { Status: "failed" } && !CanContinueKnownReturn(completed, Center(known), now))
        { Finish(false, completed.Reason ?? "worksite_route_blocked"); return; }
        bool Arrived() => (Center(known) - player.ComponentBody.Position).LengthSquared() <= (reach + 1) * (reach + 1)
            && SeenStation(owner.Queries.ReadPanorama(reach + 0.6f), stations, block, reach) is { } seen
            && WorksiteAccessible(seen, block);
        void StartRoute(Vector3 destination, PlayObservedPath.Plan? path, bool intermediate = false)
        {
            worksiteLegs++;
            knownReturnStarted = now;
            knownReturnHealth = player.ComponentHealth.Health;
            var delta = (destination - origin).XZ;
            campRoute = new(player, owner, MathUtils.RadToDeg(Vector2.Angle(player.ComponentBody.Matrix.Forward.XZ, delta)),
                delta.Length(), "work", false, now, () => (JsonSerializer.SerializeToElement(new { }), []), readDryWindow, log,
                shelterTarget: destination, observedPath: path, permitLandLeg: permitLandLeg,
                arrived: () => Arrived() || intermediate && (player.ComponentBody.Position - destination).LengthSquared() < .35f * .35f,
                finishStepAtDeadline: true, settleAtArrival: intermediate);
        }
        foreach (var ground in panorama.Blocks.Where(ground => ground.BareTop && PlaySurvivalSenses.IsDryTerrain(ground.Value)
            && ground.Cell.Y + 1 == known.Y
            && (Center(ground.Cell) - Center(known)).XZ.Length() is >= 1.1f and <= 2.7f).OrderBy(ground => ground.Distance).Take(12))
        {
            var destination = new Vector3(ground.Cell.X + 0.5f, ground.Cell.Y + 1f, ground.Cell.Z + 0.5f);
            if (!Permit(destination)) continue;
            var path = PlayObservedPath.Find(panorama, origin, destination, (_, end) => Permit(end), goalReach: 0.3f,
                maximumAboveTarget: .1f, maximumBelowTarget: .1f);
            if (path is null) continue;
            StartRoute(destination, path);
            return;
        }
        // 当前只能看清局部时，先沿三维连通地面走向有实际进展的落脚点，再观察下一段。
        var goal = new Vector3(known.X + .5f, known.Y, known.Z + .5f);
        var progress = PlayObservedPath.FindProgress(panorama, origin, goal, (_, end) => Permit(end));
        if (progress is null)
        {
            log(new { kind = "play_worksite_route_unavailable", actor = player.PlayerData.Name, task = Task,
                position = origin.ToString(), target = known.ToString(),
                currentFloorSeen = panorama.Blocks.Any(block => block.BareTop && block.Cell.X == Terrain.ToCell(origin.X)
                    && block.Cell.Z == Terrain.ToCell(origin.Z) && block.Cell.Y + 1 == (int)MathF.Round(origin.Y)), gameTime = now });
            Finish(false, "no_observed_worksite_route"); return;
        }
        CurrentStage = known.Y > origin.Y + .5f ? "climb_to_worksite_level"
            : known.Y < origin.Y - .5f ? "descend_to_worksite_level" : "follow_observed_worksite_route";
        StartRoute(progress.Destination, progress.Path, true);
        log(new { kind = "play_worksite_progressive_route", actor = player.PlayerData.Name, task = Task,
            target = known.ToString(), destination = progress.Destination.ToString(), heightDifference = known.Y - origin.Y,
            progress.ForwardGain, cells = progress.Path.Cells, leg = worksiteLegs, gameTime = now });
    }

    private bool WorksiteAccessible(PanoramicBlock seen, int block)
    {
        if (!PlaySurvivalSenses.OnDryGround(player.ComponentBody) || Math.Abs(player.ComponentBody.Velocity.Y) >= 0.6f)
            return false;
        if (block == CampfireBlock.Index) return true;
        var position = player.ComponentBody.Position;
        if (Math.Abs(position.Y - seen.Cell.Y) > 0.1f || (Center(seen.Cell) - position).XZ.LengthSquared() > 9f)
            return false;
        var entity = player.Project.FindSubsystem<SubsystemBlockEntities>(true).GetBlockEntity(seen.Cell)?.Entity;
        IInventory? storage = block == CraftingTableBlock.Index
            ? entity?.FindComponent<ComponentCraftingTable>() : entity?.FindComponent<ComponentFurnace>();
        // 原生存储查询再次验证距离和通往工作站中心的交互射线，露出一个角不算已经可用。
        return storage is not null && owner.Queries.ReadStorage(storage) is not null;
    }
}
