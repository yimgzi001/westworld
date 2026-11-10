#nullable enable

using Engine;
using Game;
using Game.Westworld;

internal sealed class PlayEarthShelter
{
    internal sealed record Site(int X, int Surface, int Z)
    {
        public Vector3 Center(float y) => new(X + 0.5f, y, Z + 0.5f);
        public Point3 Cell(int y) => new(X, y, Z);
        public bool Contains(ComponentBody body) => Terrain.ToCell(body.Position.X) == X && Terrain.ToCell(body.Position.Z) == Z
            && body.Position.Y < Surface - 0.5f;
    }

    private enum Phase { Center, SurveyFloor, DigFloor, Settle, SurveyWall, Seal, SurveyRoof, Open, SurveyExitFloor, Jump, Raise }
    private static readonly Point3[] Walls = [new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0), new(0, 0, -1)];
    private readonly ComponentPlayer player;
    private readonly AtomicActions owner;
    private readonly Site site;
    private readonly bool leaving;
    private readonly double deadline;
    private readonly Action<object> log;
    private AtomicActionRun? action;
    private Phase phase = Phase.Center;
    private double settleUntil;
    private int wall, level;
    private bool opened;

    public PlayEarthShelter(ComponentPlayer player, AtomicActions owner, Site site, bool leaving,
        double gameTime, Action<object> log)
    {
        this.player = player;
        this.owner = owner;
        this.site = site;
        this.leaving = leaving;
        this.log = log;
        deadline = gameTime + 45;
    }

    public string? Status { get; private set; }
    public string? Reason { get; private set; }
    public bool Leaving => leaving;

    public static Site? At(ComponentBody body)
    {
        var feet = Terrain.ToCell(body.Position + new Vector3(0, 0.05f, 0));
        if (body.ParentBody is not null || body.ImmersionDepth > 0.04f || body.StandingOnBody is not null
            || body.StandingOnValue is not int value || !SoftEarth(value) || MathF.Abs(body.Position.Y - feet.Y) > 0.05f)
            return null;
        return new(feet.X, feet.Y, feet.Z);
    }

    public void Update(double gameTime)
    {
        if (Status is not null) return;
        var body = player.ComponentBody;
        if (gameTime >= deadline || body.ImmersionDepth > 0.04f || player.ComponentSleep.IsSleeping
            || Terrain.ToCell(body.Position.X) != site.X || Terrain.ToCell(body.Position.Z) != site.Z
            || body.Position.Y < site.Surface - 3.2f)
        {
            Finish("failed", "shelter_position_unsafe");
            return;
        }
        if (action is { } run)
        {
            if (run.Result.Status == AtomicActionStatus.Running) return;
            action = null;
            if (run.Result.Status != AtomicActionStatus.Succeeded)
            {
                Finish("failed", $"{run.Action.GetType().Name}_{run.Result.Failure}");
                return;
            }
            log(new { kind = "play_shelter_native", actor = player.PlayerData.Name,
                action = run.Action.GetType().Name, leaving, gameTime });
            if (phase == Phase.Settle && run.Action is Dig or Place) settleUntil = gameTime + 0.25;
        }

        switch (phase)
        {
            case Phase.Center:
                phase = Phase.Settle;
                if ((body.Position - site.Center(body.Position.Y)).XZ.Length() > 0.13f)
                    action = owner.Start(new MoveTo(site.Center(body.Position.Y), 0.12f, 2f));
                break;
            case Phase.Settle:
                if (gameTime < settleUntil || !body.StandingOnValue.HasValue || MathF.Abs(body.Velocity.Y) > 0.2f) return;
                level = (int)MathF.Round(body.Position.Y);
                if (leaving && level >= site.Surface) Finish("succeeded", "surface_reached");
                else if (leaving) phase = opened ? Phase.SurveyExitFloor : Phase.SurveyRoof;
                else if (level == site.Surface - 3 && BlocksManager.Blocks[Terrain.ExtractContents(body.StandingOnValue.Value)].SleepSuitability > 0f)
                    phase = Phase.SurveyWall;
                else if (level > site.Surface - 3 && SoftEarth(body.StandingOnValue.Value)) phase = Phase.SurveyFloor;
                else Finish("failed", "no_soft_earth_or_sleep_floor");
                break;
            case Phase.SurveyFloor:
                Begin(new FaceTowards(site.Center(body.Position.Y), 2f, -81f), Phase.DigFloor);
                break;
            case Phase.DigFloor:
                if (Focus(site.Cell(level - 1), 4) is not { } floor || !SoftEarth(floor.ExpectedValue))
                {
                    Finish("failed", "floor_not_seen");
                    break;
                }
                Begin(new Dig(floor, 5), Phase.Settle);
                break;
            case Phase.SurveyWall:
                var delta = Walls[wall];
                var wallPoint = site.Center(site.Surface - 0.5f) + 0.66f * new Vector3(delta.X, 0, delta.Z);
                Begin(new FaceTowards(wallPoint, 2f), Phase.Seal);
                break;
            case Phase.Seal:
                if (Focus(site.Cell(site.Surface - 1) + Walls[wall], null) is { } wallTarget
                    && wallTarget.Face is int side && wallTarget.Cell + CellFace.FaceToPoint3(side) == site.Cell(site.Surface - 1)
                    && Dirt() is { } cover)
                    Begin(new Place(wallTarget, cover), Phase.Open);
                else if (++wall < Walls.Length) phase = Phase.SurveyWall;
                else Finish("failed", "no_roof_anchor_or_owned_earth");
                break;
            case Phase.SurveyRoof:
                Begin(new FaceTowards(site.Center(body.Position.Y), 2f, 81f), Phase.Open);
                break;
            case Phase.Open:
                if (!leaving)
                {
                    Finish("succeeded", "earth_roof_placed");
                    break;
                }
                opened = true;
                if (Focus(site.Cell(site.Surface - 1), 5) is { } roof && SoftEarth(roof.ExpectedValue))
                    Begin(new Dig(roof, 5f), Phase.SurveyExitFloor);
                else phase = Phase.SurveyExitFloor;
                break;
            case Phase.SurveyExitFloor:
                Begin(new FaceTowards(site.Center(body.Position.Y), 2f, -81f), Phase.Jump);
                break;
            case Phase.Jump:
                if (Dirt() is null || Focus(site.Cell(level - 1), 4) is null)
                {
                    Finish("failed", "no_earth_for_exit");
                    break;
                }
                Begin(new Jump(), Phase.Raise);
                break;
            case Phase.Raise:
                if (body.Position.Y >= level + 1.01f && Focus(site.Cell(level - 1), 4) is { } baseBlock && Dirt() is { } earth)
                {
                    Begin(new Place(baseBlock, earth), Phase.Settle);
                }
                else if (body.Velocity.Y <= 0f) Finish("failed", "native_jump_could_not_place_earth");
                break;
        }
    }

    private BlockTarget? Focus(Point3 cell, int? face)
    {
        var focus = owner.Queries.LookAhead(5);
        if (focus is { Kind: FocusKind.Block, Face: int hitFace, BlockValue: int value, Cell: Point3 point }
            && point == cell && (!face.HasValue || face == hitFace)) return new(point, value, hitFace);
        return null;
    }

    private OwnItemSlot? Dirt()
    {
        var inventory = player.ComponentMiner.Inventory;
        for (int slot = 0; slot < inventory.VisibleSlotsCount; slot++)
        {
            int value = inventory.GetSlotValue(slot);
            if (inventory.GetSlotCount(slot) > 0 && Terrain.ExtractContents(value) == DirtBlock.Index)
                return new(slot, value);
        }
        return null;
    }

    private static bool SoftEarth(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)] is GrassBlock or DirtBlock;

    private void Begin(AtomicAction next, Phase after)
    {
        action = owner.Start(next);
        phase = after;
    }

    public void Stop()
    {
        if (action?.Result.Status == AtomicActionStatus.Running) owner.Cancel(action);
        action = null;
    }

    private void Finish(string status, string reason)
    {
        Stop();
        Status = status;
        Reason = reason;
    }
}
