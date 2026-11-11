#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;

internal sealed partial class PlaySurvivalProduction
{
    public const string Earth = "collect_earth", EarthSupply = "collect_forty_earth";
    public const string BuildCamp = "build_simple_camp", ReturnCamp = "return_and_seal_camp", LeaveCamp = "leave_camp";
    internal sealed class CampSite(int x, int y, int z)
    {
        public int X { get; } = x;
        public int Y { get; } = y;
        public int Z { get; } = z;
        public bool Built { get; set; }
        public bool DoorClosed { get; set; }
        public double LastConfirmed { get; set; }
        public Vector3 Inside => new(X + 1.5f, Y, Z + 1.5f);
        public Vector3 Outside => new(X + 1.5f, Y, Z - 0.65f);
        public Point3[] Door => [new(X + 1, Y + 1, Z), new(X + 1, Y, Z)];
        public bool Contains(ComponentBody body) => body.Position.X > X + 1 && body.Position.X < X + 3
            && body.Position.Z > Z + 1 && body.Position.Z < Z + 3 && MathF.Abs(body.Position.Y - Y) < 0.1f;
        public bool Protects(Point3 cell) => cell.X >= X && cell.X <= X + 3 && cell.Z >= Z && cell.Z <= Z + 3
            && cell.Y >= Y - 1 && cell.Y <= Y + 2;
        public Point3[] Shell => (from dy in new[] { 0, 1, 2 }
            from dx in Enumerable.Range(0, 4) from dz in Enumerable.Range(0, 4)
            where dy == 2 || dx is 0 or 3 || dz is 0 or 3
            let cell = new Point3(X + dx, Y + dy, Z + dz)
            where !Door.Contains(cell)
            // 先低墙和角柱，再搭顶，最后补上层侧墙，保留搭顶所需的可见放置面。
            orderby dy == 0 ? 0 : dy == 1 && dx is 0 or 3 && dz is 0 or 3 ? 1 : dy == 2 ? 2 : 3,
                -(cell.X + 0.5f - (X + 1.5f)) * (cell.X + 0.5f - (X + 1.5f))
                - (cell.Z + 0.5f - (Z + 1.5f)) * (cell.Z + 0.5f - (Z + 1.5f))
            select cell).ToArray();
    }

    private readonly CampSite? camp;
    private PlaySurvivalTravel? campRoute;
    private Point3? pendingCampCell;
    private BlockTarget? campAnchor;
    private int campPhase, campMaterialBefore, campMaterialValue, campPlaced;
    private bool campComplete, campDiggingDoor;
    private bool campJumping, campAwaitingLanding;
    private readonly HashSet<(Point3 Cell, int Face)> refusedCampAnchors = new();
    private double campReadyAt;
    private int campRouteSegments;
    private Vector3 campRouteDestination;

    public static CampSite? PlanCamp(LocalObservationWindow window)
    {
        var p = window.Position;
        int y = (int)Math.Round(p.Y);
        if (Math.Abs(p.Y - y) > 0.05) return null;
        var cells = window.Cells.Where(cell => cell.IsCurrent && cell.Age <= 0.1).ToDictionary(cell => (cell.X, cell.Y, cell.Z));
        var site = new CampSite((int)Math.Floor(p.X) - 1, y, (int)Math.Floor(p.Z) - 1);
        for (int x = site.X; x <= site.X + 3; x++)
        for (int z = site.Z - 1; z <= site.Z + 3; z++)
        {
            if (!cells.TryGetValue((x, y - 1, z), out var ground) || !CampMaterial(ground.Value)
                && !PlaySurvivalSenses.IsDryTerrain(ground.Value)) return null;
            for (int dy = 0; dy <= 2; dy++)
                if (!cells.TryGetValue((x, y + dy, z), out var air) || Terrain.ExtractContents(air.Value) != 0
                    && BlocksManager.Blocks[Terrain.ExtractContents(air.Value)] is not (TallGrassBlock or FlowerBlock)) return null;
        }
        return site;
    }

    public static bool CampMaterial(int value) => Terrain.ExtractContents(value) == DirtBlock.Index
        || Terrain.ExtractContents(value) == PlanksBlock.Index;
    public static int CampStock(ComponentPlayer player) => Count(player.ComponentMiner.Inventory, CampMaterial);

    private void AdvanceCamp(double now)
    {
        if (camp is null) { Finish(false, "camp_blueprint_missing"); return; }
        if (now < campReadyAt) return;
        if (campAwaitingLanding)
        {
            if (!PlaySurvivalSenses.OnDryGround(player.ComponentBody)) return;
            campAwaitingLanding = campJumping = false;
        }
        if (campRoute is { } moving)
        {
            moving.Update(now);
            if (moving.Status is null) return;
            campRoute = null;
            if (moving.Status != "succeeded" && !CanContinueKnownReturn(moving, campRouteDestination, now))
            { Finish(false, moving.Reason ?? "camp_route_blocked"); return; }
            campReadyAt = now + 0.2;
            return;
        }
        var native = player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain;
        if (pendingCampCell is { } confirmed)
        {
            int actual = native.GetCellValue(confirmed);
            bool changed = campDiggingDoor ? Terrain.ExtractContents(actual) == 0 : CampMaterial(actual)
                && CountExact(campMaterialValue) == campMaterialBefore - 1;
            if (!changed) { Finish(false, "native_camp_change_unconfirmed"); return; }
            log(new { kind = campDiggingDoor ? "play_camp_door_opened" : "play_camp_block_placed", actor = player.PlayerData.Name,
                cell = confirmed.ToString(), value = actual, inventoryBefore = campMaterialBefore,
                inventoryAfter = CountExact(campMaterialValue), gameTime = now });
            if (!campDiggingDoor) campPlaced++;
            pendingCampCell = null;
            campAnchor = null;
            refusedCampAnchors.Clear();
            if (campJumping) campAwaitingLanding = true;
            campReadyAt = now + 0.2;
            return;
        }
        var body = player.ComponentBody;
        bool inside = camp.Contains(body);
        if (Task is ReturnCamp or LeaveCamp && !camp.Built) { Finish(false, "camp_not_built"); return; }

        if (Task is ReturnCamp or LeaveCamp && campPhase == 0)
        {
            var entry = inside ? camp.Inside : camp.Outside;
            if ((body.Position - entry).XZ.Length() > 0.45f || Math.Abs(body.Position.Y - entry.Y) > 0.1f)
            { StartCampRoute(entry, now); return; }
            campPhase = 1;
        }
        if (Task is ReturnCamp or LeaveCamp && campPhase == 1)
        {
            var door = owner.Queries.ReadPanorama(5f)?.Blocks.FirstOrDefault(block => camp.Door.Contains(block.Cell) && CampMaterial(block.Value));
            if (door is not null)
            {
                if (campAnchor is null)
                {
                    campAnchor = new(door.Cell, door.Value);
                    action = owner.Start(new FaceTowards(door.Point, 3f));
                    return;
                }
                var focus = owner.Queries.LookAhead(5f);
                if (focus is not { Kind: FocusKind.Block, Cell: Point3 doorCell, Face: int face, BlockValue: int value }
                    || !camp.Door.Contains(doorCell)) { Finish(false, "camp_door_not_in_reach"); return; }
                campDiggingDoor = true;
                pendingCampCell = doorCell;
                action = owner.Start(new Dig(new BlockTarget(doorCell, value, face), 8f));
                camp.DoorClosed = false;
                return;
            }
            campPhase = 2;
        }
        if (Task is ReturnCamp or LeaveCamp && campPhase == 2)
        {
            var destination = Task == LeaveCamp ? camp.Outside : camp.Inside;
            float reach = Task == ReturnCamp ? 0.15f : 0.45f;
            if ((body.Position - destination).XZ.Length() > reach || Math.Abs(body.Position.Y - destination.Y) > 0.1f)
            { StartCampRoute(destination, now); return; }
            if (Task == LeaveCamp) { campComplete = true; Finish(true, "native_camp_departure_confirmed"); return; }
            campPhase = 3;
        }
        // 与连续导航的到达范围一致，避免已经到达门内外却反复创建零位移路线。
        if (Task == BuildCamp && (body.Position - camp.Inside).XZ.Length() > 0.45f)
        {
            StartCampRoute(camp.Inside, now);
            return;
        }
        if (!camp.Contains(body) && !campJumping) { Finish(false, "camp_builder_not_inside"); return; }

        if (Task == BuildCamp && campPlaced == 0 && campAnchor is null)
        {
            int needed = camp.Shell.Concat(camp.Door).Count(cell => !CampMaterial(native.GetCellValue(cell)));
            if (CampStock(player) < needed) { Finish(false, "camp_materials_insufficient"); return; }
        }

        var missing = (Task == BuildCamp ? camp.Shell.Concat(camp.Door) : camp.Door)
            .FirstOrDefault(cell => !CampMaterial(native.GetCellValue(cell)), new Point3(int.MinValue, 0, 0));
        if (missing.X == int.MinValue)
        {
            if (!PlaySurvivalRest.HasSecureCover(player)) { Finish(false, "camp_native_cover_unconfirmed"); return; }
            camp.Built = camp.DoorClosed = campComplete = true;
            camp.LastConfirmed = now;
            log(new { kind = "play_camp_ready", actor = player.PlayerData.Name, camp.X, camp.Y, camp.Z, campPlaced, gameTime = now });
            Finish(true, "native_camp_cover_confirmed");
            return;
        }
        if (campAnchor is null)
        {
            var seen = owner.Queries.ReadPanorama(5f);
            var eye = player.ComponentCreatureModel.EyePosition;
            var anchors = from block in seen?.Blocks ?? [] from face in Enumerable.Range(0, 6)
                where block.Cell + CellFace.FaceToPoint3(face) == missing
                    && BlocksManager.Blocks[Terrain.ExtractContents(block.Value)] is CubeBlock
                    && !refusedCampAnchors.Contains((block.Cell, face))
                let towardEye = Vector3.Dot(eye - Center(block.Cell), new Vector3(CellFace.FaceToPoint3(face)))
                where towardEye > 0.5f || face == 4 && missing.Y == camp.Y + 2
                orderby towardEye > 0.5f ? 0 : 1, block.Distance
                select new BlockTarget(block.Cell, block.Value, face);
            campAnchor = anchors.FirstOrDefault();
            if (campAnchor is null) { Finish(false, "camp_anchor_not_visible"); return; }
            var point = Center(campAnchor.Cell) + 0.49f * new Vector3(CellFace.FaceToPoint3(campAnchor.Face!.Value));
            action = owner.Start(new FaceTowards(point, 3f));
            return;
        }
        if (campAnchor.Face == 4 && player.ComponentCreatureModel.EyePosition.Y <= campAnchor.Cell.Y + 1.05f)
        {
            if (!campJumping)
            {
                campJumping = true;
                action = owner.Start(new Jump());
                campReadyAt = now + 0.12;
            }
            else if (body.Velocity.Y <= 0f) { Finish(false, "camp_roof_jump_unavailable"); }
            return;
        }
        if (VisibleFarmItem(CampMaterial) is not { } earth) return;
        campMaterialValue = earth.ExpectedValue;
        campMaterialBefore = CountExact(campMaterialValue);
        campDiggingDoor = false;
        pendingCampCell = missing;
        action = owner.Start(new Place(campAnchor, earth));
    }

    private bool RetryCampPlacement(AtomicActionRun native, double now)
    {
        if (Task is not (BuildCamp or ReturnCamp) || native.Action is not Place
            || native.Result.Failure is not (AtomicActionFailure.Blocked or AtomicActionFailure.OutOfReach)
            || campAnchor is not { Face: int face } anchor || pendingCampCell is not { } destination
            || CountExact(campMaterialValue) != campMaterialBefore) return false;
        refusedCampAnchors.Add((anchor.Cell, face));
        log(new { kind = "play_camp_anchor_rejected", actor = player.PlayerData.Name, destination = destination.ToString(),
            anchor = anchor.Cell.ToString(), face, failure = native.Result.Failure.ToString(), gameTime = now });
        campAnchor = null;
        pendingCampCell = null;
        campReadyAt = now + 0.2;
        if (campJumping) campAwaitingLanding = true;
        return true;
    }

    private void StartCampRoute(Vector3 destination, double now)
    {
        var origin = player.ComponentBody.Position;
        var offset = (destination - origin).XZ;
        if (offset.Length() > KnownReturnReach) { Finish(false, "camp_beyond_return_range"); return; }
        if (campRouteSegments++ >= KnownReturnLegLimit) { Finish(false, "camp_return_segment_limit"); return; }
        var known = owner.Queries.ReadPanorama(40f);
        // 封门前须真正站到屋内，四十五厘米的到达范围仍可能让身体挡住门格。
        float reach = destination == camp!.Inside ? 0.15f : 0.45f;
        bool Permit(Vector3 point) => permitLandLeg?.Invoke(point, now) != false;
        var path = PlayObservedPath.Find(known, origin, destination, (_, end) => Permit(end), goalReach: 0.15f);
        knownReturnStarted = now;
        knownReturnHealth = player.ComponentHealth.Health;
        campRouteDestination = destination;
        campRoute = new(player, owner, MathUtils.RadToDeg(Vector2.Angle(player.ComponentBody.Matrix.Forward.XZ, offset)),
            offset.Length(), "work", false, now, () => (JsonSerializer.SerializeToElement(new { }), []), readDryWindow, log,
            shelterTarget: destination, permitLandLeg: permitLandLeg, observedPath: path,
            arrived: () => Math.Abs(player.ComponentBody.Position.Y - destination.Y) < 0.1f
                && (player.ComponentBody.Position - destination).XZ.LengthSquared() < reach * reach,
            goalRange: reach, finishStepAtDeadline: true);
    }
}
