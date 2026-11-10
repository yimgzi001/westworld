using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

sealed partial class Runner
{
    private NativeToolSession? forageTools;
    private PlayEarthShelter? earthShelter;
    private PlayEarthShelter.Site? testSite;
    private Pickable? testEgg;
    private Pickable? hiddenEgg;
    private Point3 farMarker, hiddenMarker;
    private int eggValue;
    private bool sawLevelWalking;
    private long mealsBefore;
    private float vitalBefore;
    private double sleepDay;
    private bool changedDigSlot;

    private void RunSurvivalSkill()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            for (int x = cell.X - 5; x <= cell.X + 5; x++)
            for (int z = cell.Z - 5; z <= cell.Z + 5; z++)
            for (int y = cell.Y - 4; y <= cell.Y + 3; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y ? DirtBlock.Index : 0);

            body.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            body.Velocity = Vector3.Zero;
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
            if (mode == "forage")
            {
                var eggType = ((EggBlock)BlocksManager.Blocks[118]).EggTypes.First(egg => egg.NutritionalValue > 0f);
                eggValue = Terrain.MakeBlockValue(118, 0, EggBlock.SetIsLaid(EggBlock.SetEggType(0, eggType.EggTypeIndex), true));
                var pickables = p.Project.FindSubsystem<SubsystemPickables>(true);
                testEgg = pickables.AddPickable(eggValue, 1,
                    body.Position + new Vector3(0, 0.2f, 3.25f), Vector3.Zero, null);
                hiddenEgg = pickables.AddPickable(eggValue, 1,
                    body.Position + new Vector3(0, 0.2f, -4.25f), Vector3.Zero, null);
                for (int x = cell.X - 2; x <= cell.X + 2; x++)
                for (int y = cell.Y; y <= cell.Y + 3; y++)
                    terrain.ChangeCell(x, y, cell.Z - 2, GraniteBlock.Index);
                farMarker = new(cell.X + 12, cell.Y, cell.Z + 2);
                hiddenMarker = new(cell.X, cell.Y, cell.Z - 4);
                terrain.ChangeCell(farMarker.X, farMarker.Y, farMarker.Z, OakWoodBlock.Index);
                terrain.ChangeCell(hiddenMarker.X, hiddenMarker.Y, hiddenMarker.Z, OakWoodBlock.Index);
                forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            }
            elapsed = 0;
            stage = 1;
            return;
        }
        if (elapsed < 1) return;
        if (mode == "forage") RunForage(now);
        else RunShelter(now);
    }

    private void RunForage(double now)
    {
        var body = p.ComponentBody;
        if (stage == 1)
        {
            if (ReadForage().Foods.Any(food => food.Value == eggValue))
                throw new InvalidOperationException("测试鸟蛋应在初始朝向之外。");
            var position = body.Position;
            var direction = p.ComponentCreatureModel.EyeRotation.GetForwardVector();
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var sweep = PlaySurvivalSurvey.Capture(p, actions.Queries, now) ?? throw new InvalidOperationException("全向观察未返回。");
            var food = sweep.Sectors.SelectMany(sector => sector.Food).Single(item => item.Value == eggValue);
            bool samePose = position == body.Position && direction == p.ComponentCreatureModel.EyeRotation.GetForwardVector()
                && now == p.Project.FindSubsystem<SubsystemTime>(true).GameTime && !actions.IsActionRunning;
            bool hidden = sweep.Panorama.Objects.Pickables.All(item => (item.Position - hiddenEgg!.Position).LengthSquared() > 0.5f)
                && sweep.Panorama.Blocks.All(block => block.Cell != hiddenMarker);
            bool far = sweep.Panorama.Blocks.Any(block => block.Cell == farMarker && block.Distance > 10f);
            if (sweep.Sectors.Length != 8 || food.InReach || !samePose || !hidden || !far)
                throw new InvalidOperationException($"全向观察范围或遮挡不符合测试条件。原姿态 {samePose}，墙后隐藏 {hidden}，远处可见 {far}。");
            float bearing = Vector2.Angle(body.Matrix.Forward.XZ, (food.Point - body.Position).XZ) * (180f / MathF.PI);
            travel = new(p, actions, bearing, 4.2f, "food", false, now, ReadForage,
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), Gate.Log, foodTarget: food);
            Gate.Log(new { kind = "forage_survey", sectors = sweep.Sectors.Length, sweep.Panorama.Radius, food.Name, food.Distance,
                bearing, samePose, hidden, far, durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds });
            stage = 3;
        }
        if (stage == 3)
        {
            var inventory = p.ComponentMiner.Inventory;
            var slot = Enumerable.Range(0, inventory.SlotsCount)
                .FirstOrDefault(index => inventory.GetSlotValue(index) == eggValue && inventory.GetSlotCount(index) > 0, -1);
            if (slot < 0)
            {
                travel!.Update(now);
                var movement = p.ComponentLocomotion.LastWalkOrder;
                sawLevelWalking |= movement is { } order && order.LengthSquared() > 0.04f
                    && MathF.Abs(p.ComponentLocomotion.LookAngles.Y) < MathUtils.DegToRad(15f);
                if (travel.Status is null) return;
                var food = ReadForage().Foods.FirstOrDefault(item => item.Value == eggValue && item.InReach);
                if (food is null) throw new InvalidOperationException($"鸟蛋仍未进入原生拾取范围。{travel.Reason}");
                var receipt = forageTools!.Call("act_PickUp", new { target = food.Reference, count = 1 });
                if (receipt.State != "returned" || receipt.Data.GetProperty("status").GetString() != "succeeded")
                    throw new InvalidOperationException("原生拾取没有成功。");
                return;
            }
            travel!.Stop();
            mealsBefore = p.PlayerStats.FoodItemsEaten;
            vitalBefore = p.ComponentVitalStats.Food;
            run = actions.Start(new Eat(slot, eggValue, 1));
            stage = 4;
        }
        if (stage != 4 || run!.Result.Status == AtomicActionStatus.Running) return;
        bool consumed = run.Result.Status == AtomicActionStatus.Succeeded && p.PlayerStats.FoodItemsEaten == mealsBefore + 1
            && p.ComponentVitalStats.Food > vitalBefore && testEgg is { ToRemove: true };
        Passed = consumed && sawLevelWalking;
        Done = true;
        Gate.Log(new { kind = "forage_result", Passed, consumed, sawLevelWalking, foodBefore = vitalBefore,
            foodAfter = p.ComponentVitalStats.Food, travel!.Legs });
    }

    private (JsonElement Observation, PlaySurvivalFood.Food[] Foods) ReadForage()
    {
        var read = forageTools!.Call("observe", new { look_distance = 12, object_distance = 16, block_limit = 128, limit = 8 });
        if (read.State != "returned") throw new InvalidOperationException("测试观察没有返回。");
        var (_, food) = PlaySurvivalFood.Project(read.Data);
        return (read.Data, food);
    }

    private void RunShelter(double now)
    {
        var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        if (stage == 1)
        {
            testSite = PlayEarthShelter.At(p.ComponentBody) ?? throw new InvalidOperationException("土坑测试地面不可用。");
            earthShelter = new(p, actions, testSite, false, now, Gate.Log);
            stage = 2;
        }
        if (stage == 2)
        {
            if (mode == "shelter-retry" && !changedDigSlot && p.ComponentMiner.DigCellFace is { } face
                && face.Y == testSite!.Surface - 2)
            {
                var inventory = p.ComponentMiner.Inventory;
                int before = inventory.ActiveSlotIndex;
                inventory.ActiveSlotIndex = (before + 1) % inventory.VisibleSlotsCount;
                changedDigSlot = inventory.ActiveSlotIndex != before;
                Gate.Log(new { kind = "shelter_fixture_dig_slot_change", changedDigSlot, now });
            }
            earthShelter!.Update(now);
            if (earthShelter.Status is null) return;
            var roof = testSite!.Cell(testSite.Surface - 1);
            bool ready = p.ComponentSleep.CanSleep(out string blocker);
            Gate.Log(new { kind = "shelter_built", earthShelter.Status, earthShelter.Reason, ready, blocker,
                floor = p.ComponentBody.Position.Y, roof = terrain.Terrain.GetCellContents(roof.X, roof.Y, roof.Z) });
            if (earthShelter.Status != "succeeded") { Done = true; return; }
            stage = 3;
        }
        if (stage == 3)
        {
            // 等角色自然产生疲劳，避免把刚出生时的睡眠前提误判为搭建失败。
            if (!p.ComponentSleep.CanSleep(out _)) return;
            // 固定测试起始时间在黄昏后，等待游戏自行完成睡眠和醒来。
            calendar.TimeOfDayOffset += calendar.DuskStart + 0.02f - calendar.TimeOfDay;
            vitalBefore = p.ComponentVitalStats.Sleep;
            sleepDay = calendar.Day;
            run = actions.Start(new Sleep());
            if (run.Result.Status != AtomicActionStatus.Succeeded || !p.ComponentSleep.IsSleeping)
                throw new InvalidOperationException("原生睡眠没有开始。");
            stage = 4;
        }
        if (stage == 4)
        {
            if (p.ComponentSleep.IsSleeping) return;
            bool slept = p.ComponentVitalStats.Sleep > vitalBefore && calendar.Day > sleepDay
                && calendar.TimeOfDay >= calendar.DayStart && calendar.TimeOfDay < calendar.DuskStart;
            Gate.Log(new { kind = "shelter_morning", slept, sleepBefore = vitalBefore, sleepAfter = p.ComponentVitalStats.Sleep,
                days = calendar.Day - sleepDay, calendar.TimeOfDay });
            if (!slept) { Done = true; return; }
            earthShelter = new(p, actions, testSite!, true, now, Gate.Log);
            stage = 5;
        }
        if (stage != 5) return;
        earthShelter!.Update(now);
        if (earthShelter.Status is null) return;
        Passed = earthShelter.Status == "succeeded" && p.ComponentBody.Position.Y >= testSite!.Surface - 0.05f
            && (mode != "shelter-retry" || changedDigSlot);
        Done = true;
        Gate.Log(new { kind = "shelter_exit", Passed, earthShelter.Status, earthShelter.Reason, floor = p.ComponentBody.Position.Y });
    }
}
