using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

sealed partial class Runner
{
    private NativeToolSession? forageTools;
    private PlaySurvivalSurvey? forageSurvey;
    private PlayEarthShelter? earthShelter;
    private PlayEarthShelter.Site? testSite;
    private Pickable? testEgg;
    private int eggValue;
    private bool sawLevelWalking;
    private long mealsBefore;
    private float vitalBefore;
    private double sleepDay;

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
                testEgg = p.Project.FindSubsystem<SubsystemPickables>(true).AddPickable(eggValue, 1,
                    body.Position + new Vector3(0, 0.2f, 3.25f), Vector3.Zero, null);
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
            forageSurvey = new(p, actions, now, ReadForage);
            stage = 2;
        }
        if (stage == 2)
        {
            forageSurvey!.Update(now);
            if (!forageSurvey.Done) return;
            var sweep = forageSurvey.Result ?? throw new InvalidOperationException("原地环视没有完成。");
            var food = sweep.Sectors.SelectMany(sector => sector.Food).First(item => item.Value == eggValue);
            if (sweep.Sectors.Length != 4 || food.InReach || (body.Position - sweep.Origin).XZ.Length() > 0.1f)
                throw new InvalidOperationException("环视期间移动了位置，或初始距离不符合测试条件。");
            float bearing = Vector2.Angle(body.Matrix.Forward.XZ, (food.Point - body.Position).XZ) * (180f / MathF.PI);
            travel = new(p, actions, bearing, 4.2f, "food", false, now, ReadForage,
                () => memory.Observe(senses.Observe(senses.SessionId, actor)), Gate.Log, foodTarget: food);
            Gate.Log(new { kind = "forage_survey", sectors = sweep.Sectors.Length, food.Name, food.Distance, bearing });
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
        Passed = earthShelter.Status == "succeeded" && p.ComponentBody.Position.Y >= testSite!.Surface - 0.05f;
        Done = true;
        Gate.Log(new { kind = "shelter_exit", Passed, earthShelter.Status, earthShelter.Reason, floor = p.ComponentBody.Position.Y });
    }
}
