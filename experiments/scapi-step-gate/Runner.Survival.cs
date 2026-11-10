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
    private Pickable? farEgg;
    private Point3 farMarker, hiddenMarker;
    private int eggValue;
    private bool sawLevelWalking;
    private long mealsBefore;
    private float vitalBefore;
    private double sleepDay;
    private bool changedDigSlot;
    private bool openedForRepair;
    private PlaySurvivalRest? fixtureDrying;
    private double dryingStarted;
    private bool refusedOpenCover;
    private bool latePredatorPlaced, exitThreatDuringJump;
    private Point3 threatOrigin;
    private Vector3 threatPosition;
    private float sightDistance;
    private readonly PlaySurvivalMemory predatorMemory = new();
    private float closestRememberedThreat = float.PositiveInfinity;

    private void RunThreatRange()
    {
        var body = p.ComponentBody;
        bool withMemory = mode == "predator-memory";
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            if (!(terrain.Terrain.GetChunkAtCell(cell.X + 23, cell.Z)?.State >= TerrainChunkState.InvalidVertices1)) return;
            threatOrigin = cell;
            for (int x = cell.X - 5; x <= cell.X + 23; x++)
            for (int z = cell.Z - 5; z <= cell.Z + 5; z++)
            for (int y = cell.Y - 1; y <= cell.Y + 3; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y ? DirtBlock.Index : 0);
            body.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            body.Velocity = Vector3.Zero;
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
            var bear = DatabaseManager.CreateEntity(p.Project, "Bear_Black", true);
            threatPosition = body.Position + (withMemory ? 17f : 20.5f) * Vector3.UnitX;
            bear.FindComponent<ComponentBody>(true).Position = threatPosition;
            var locomotion = bear.FindComponent<ComponentLocomotion>(true);
            locomotion.WalkSpeed = 0f;
            locomotion.JumpSpeed = 0f;
            p.Project.AddEntity(bear);
            elapsed = 0;
            stage = 1;
            return;
        }
        if (elapsed < 0.8) return;
        bool IsFixtureThreat(PlaySurvivalSenses.Threat animal) => MathF.Abs(animal.X - threatPosition.X) < 1f
            && MathF.Abs(animal.Z - threatPosition.Z) < 1f;
        var visible = PlaySurvivalSenses.Threats(p, actions.Queries);
        if (stage == 1)
        {
            var first = visible.FirstOrDefault(IsFixtureThreat);
            float oldRadius = withMemory ? 16f : 18f;
            if (first is null || first.Distance <= oldRadius || first.Distance > 22f || !PlaySurvivalSenses.NeedsAvoidance(first, false)
                || PlaySurvivalSenses.Threats(p, actions.Queries, oldRadius).Any(IsFixtureThreat))
                throw new InvalidOperationException("预警夹具未能从旧范围以外的实际视野识别黑熊。");
            sightDistance = first.Distance;
            predatorMemory.SeePredators([first], now);
            for (int y = threatOrigin.Y; y <= threatOrigin.Y + 2; y++)
            for (int z = threatOrigin.Z - 2; z <= threatOrigin.Z + 2; z++)
                terrain.ChangeCell(threatOrigin.X + 7, y, z, GraniteBlock.Index);
            elapsed = 0;
            stage = 2;
            return;
        }
        if (withMemory)
        {
            if (stage == 2)
            {
                var towardBear = body.Position + 4f * Vector3.UnitX;
                if (visible.Any(IsFixtureThreat) || predatorMemory.AvoidsPredatorAreas(body.Position, towardBear, now)
                    || !predatorMemory.AvoidsPredatorAreas(body.Position, towardBear, now + 49))
                    throw new InvalidOperationException("看不见黑熊后，短期记忆未正确限制接近方向或未过期。");
                travel = new(p, actions, 0f, 6f, "survey", false, now,
                    () => (JsonSerializer.SerializeToElement(new { }), []),
                    () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), Gate.Log,
                    permitLandLeg: (end, time) => predatorMemory.AvoidsPredatorAreas(body.Position, end, time));
                stage = 3;
            }
            closestRememberedThreat = Math.Min(closestRememberedThreat, (body.Position - threatPosition).XZ.Length());
            travel!.Update(now);
            if (travel.Status is null) return;
            float moved = (body.Position - travel.Origin).XZ.Length();
            Passed = travel.Reason == "scout_detoured_to_open_ground" && moved >= 3f && closestRememberedThreat > 16.9f
                && p.ComponentHealth.Health >= 0.99f;
            Done = true;
            Gate.Log(new { kind = "predator_memory_result", Passed, sightDistance, moved, closestRememberedThreat,
                health = p.ComponentHealth.Health });
            return;
        }
        Passed = !visible.Any(IsFixtureThreat);
        Done = true;
        Gate.Log(new { kind = "threat_range_result", Passed, sightDistance, blockedByWall = Passed });
    }

    private void RunFoodSafety()
    {
        if (elapsed < 1) return;
        var eggs = (EggBlock)BlocksManager.Blocks[118];
        var type = eggs.EggTypes.First(egg => egg.NutritionalValue > 0f);
        int egg = Terrain.MakeBlockValue(118, 0, EggBlock.SetIsLaid(EggBlock.SetEggType(0, type.EggTypeIndex), true));
        int pumpkin = Terrain.MakeBlockValue(PumpkinBlock.Index, 0, BasePumpkinBlock.SetSize(0, 7));
        var stats = p.ComponentVitalStats;
        stats.Food = 0.55f;
        bool normal = !PlaySurvivalFood.CanEat(p, egg) && PlaySurvivalFood.CanEat(p, pumpkin);
        stats.Food = 0.23f;
        bool hungry = PlaySurvivalFood.CanEat(p, egg) && !PlaySurvivalFood.CanEat(p, RawBirdBlock.Index)
            && !PlaySurvivalFood.CanEat(p, SeaUrchinBlock.Index);
        p.ComponentSickness.StartSickness();
        bool sick = !PlaySurvivalFood.CanEat(p, egg) && PlaySurvivalFood.CanEat(p, pumpkin);
        stats.m_satiation[PumpkinBlock.Index] = 3.25f;
        bool repeated = !PlaySurvivalFood.CanEat(p, pumpkin);
        var weak = eggs.EggTypes.Where(item => item.NutritionalValue > 0f).MinBy(item => item.NutritionalValue)!;
        var rich = eggs.EggTypes.MaxBy(item => item.NutritionalValue)!;
        int weakEgg = Terrain.MakeBlockValue(118, 0, EggBlock.SetIsLaid(EggBlock.SetEggType(0, weak.EggTypeIndex), true));
        int richEgg = Terrain.MakeBlockValue(118, 0, EggBlock.SetIsLaid(EggBlock.SetEggType(0, rich.EggTypeIndex), true));
        var inventory = p.ComponentMiner.Inventory;
        int slot = Enumerable.Range(0, inventory.SlotsCount).First(index => inventory.GetSlotCount(index) == 0);
        inventory.AddSlotItems(slot, weakEgg, 2);
        var reserve = PlaySurvivalFood.RawEggReserve(p);
        PlaySurvivalFood.Food Drop(int value) => new("", "鸟蛋", "pickup", 1, eggs.GetNutritionalValue(value),
            eggs.GetSicknessProbability(value), false, true, value, default);
        bool reserveQuality = reserve.Count == 2 && reserve.Nutrition < 0.4f && !PlaySurvivalFood.CanSeek(p, Drop(weakEgg))
            && PlaySurvivalFood.CanSeek(p, Drop(richEgg));
        Passed = normal && hungry && sick && repeated && reserveQuality;
        Done = true;
        Gate.Log(new { kind = "food_safety_result", Passed, normal, hungry, sick, repeated, reserveQuality, reserve.Nutrition,
            eggRisk = BlocksManager.Blocks[118].GetSicknessProbability(egg) });
    }

    private void RunCoast()
    {
        var body = p.ComponentBody;
        bool reboard = mode == "coast-boat";
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            int water = Terrain.MakeBlockValue(WaterBlock.Index, 0, FluidBlock.SetIsTop(0, true));
            for (int x = cell.X - 5; x <= cell.X + 6; x++)
            for (int z = cell.Z - 4; z <= cell.Z + 4; z++)
            {
                bool pool = x < cell.X + (reboard ? 5 : 2) && x > cell.X - 5 && Math.Abs(z - cell.Z) < 4;
                terrain.ChangeCell(x, cell.Y - 3, z, DirtBlock.Index);
                terrain.ChangeCell(x, cell.Y - 2, z, pool ? WaterBlock.Index : DirtBlock.Index);
                terrain.ChangeCell(x, cell.Y - 1, z, pool ? water : GravelBlock.Index);
                for (int y = cell.Y; y <= cell.Y + 3; y++) terrain.ChangeCell(x, y, z, 0);
            }
            var boat = DatabaseManager.CreateEntity(p.Project, "Boat", true);
            var hull = boat.FindComponent<ComponentBody>(true);
            hull.Position = new(cell.X + 0.5f, cell.Y - 0.35f, cell.Z + 0.5f);
            hull.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
            p.Project.AddEntity(boat);
            body.Position = hull.Position + new Vector3(0, hull.BoxSize.Y + 0.2f, 0);
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            body.Velocity = Vector3.Zero;
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            elapsed = 0;
            stage = 1;
            return;
        }
        if (stage == 1)
        {
            if (elapsed < 0.65) return;
            if (PlaySurvivalSenses.BoatDeck(body) is null)
            {
                if (elapsed > 4) throw new InvalidOperationException("角色没有在夹具里站上船壳。");
                return;
            }
            if (PlaySurvivalSenses.OnDryGround(body)) throw new InvalidOperationException("船壳被误判为干地。");
            var panorama = PlaySurvivalPanorama.Capture(p, actions.Queries, mapRange.Content, now);
            if (panorama?.ShoreInDirection(body.Position, Vector2.UnitX) is not { } shore)
            {
                if (elapsed > 4) throw new InvalidOperationException("全向观察没有找到身后的岸。");
                return;
            }
            float distance = (shore - body.Position).XZ.Length();
            if (Vector2.Dot(body.Matrix.Forward.XZ, (shore - body.Position).XZ) >= 0 || (distance > 3.4f) != reboard)
                throw new InvalidOperationException("靠岸夹具距离或朝向不符。");
            travel = new(p, actions, 180f, 8f, "dry_land", false, now, ReadForage,
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), Gate.Log, shoreTarget: shore);
            stage = 2;
            Gate.Log(new { kind = "coast_fixture_deck", position = body.Position.ToString(), onDryGround = false, distance, reboard });
        }
        travel!.Update(now);
        if (reboard && p.Entity.FindComponent<ComponentRider>() is { Mount: not null, m_isAnimating: false })
        {
            travel.Stop();
            Passed = Done = true;
            Gate.Log(new { kind = "coast_reboard", Passed });
            return;
        }
        if (travel.Status is null) return;
        float displacement = (body.Position - travel.Origin).XZ.Length();
        Passed = !reboard && travel.Status == "succeeded" && travel.Reason == "dry_ground_reached"
            && PlaySurvivalSenses.OnDryGround(body) && displacement > 1.2f;
        Done = true;
        Gate.Log(new { kind = "coast_result", Passed, travel.Status, travel.Reason, displacement, onDryGround = PlaySurvivalSenses.OnDryGround(body) });
    }

    private void RunSurvivalSkill()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            if (mode == "forage" && !ForageCorridorReady(cell.X, cell.Z)) return;
            int endX = cell.X + (mode == "forage" ? 39 : mode == "shelter-late-predator" ? 10 : 5);
            for (int x = cell.X - 5; x <= endX; x++)
            for (int z = cell.Z - 5; z <= cell.Z + 5; z++)
            for (int y = cell.Y - 4; y <= cell.Y + 3; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y ? DirtBlock.Index : 0);

            if (mode == "shelter-illness")
                for (int x = cell.X - 1; x <= cell.X + 1; x++)
                for (int z = cell.Z - 1; z <= cell.Z + 1; z++)
                    terrain.ChangeCell(x, cell.Y + 3, z, DirtBlock.Index);

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
                farEgg = pickables.AddPickable(eggValue, 1,
                    body.Position + new Vector3(38f, 0.2f, -3.25f), Vector3.Zero, null);
                for (int x = cell.X - 2; x <= cell.X + 2; x++)
                for (int y = cell.Y; y <= cell.Y + 3; y++)
                    terrain.ChangeCell(x, y, cell.Z - 2, GraniteBlock.Index);
                farMarker = new(cell.X + 38, cell.Y, cell.Z + 2);
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
            if (!ForageCorridorReady(farMarker.X - 38, farMarker.Z - 2)) return;
            if (ReadForage().Foods.Any(food => food.Value == eggValue))
                throw new InvalidOperationException("测试鸟蛋应在初始朝向之外。");
            var position = body.Position;
            var direction = p.ComponentCreatureModel.EyeRotation.GetForwardVector();
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var sweep = PlaySurvivalPanorama.Capture(p, actions.Queries, mapRange.Content, now)
                ?? throw new InvalidOperationException("全向观察未返回。");
            var food = sweep.Sectors.SelectMany(sector => sector.Food).Single(item => item.Value == eggValue
                && (item.Point - testEgg!.Position).LengthSquared() < 0.5f);
            bool samePose = position == body.Position && direction == p.ComponentCreatureModel.EyeRotation.GetForwardVector()
                && now == p.Project.FindSubsystem<SubsystemTime>(true).GameTime && !actions.IsActionRunning;
            bool hidden = sweep.Panorama.Objects.Pickables.All(item => (item.Position - hiddenEgg!.Position).LengthSquared() > 0.5f)
                && sweep.Panorama.Blocks.All(block => block.Cell != hiddenMarker);
            bool farBlock = sweep.Panorama.Blocks.Any(block => block.Cell == farMarker && block.Distance > 32f);
            bool farItem = sweep.Panorama.Objects.Pickables.Any(item => item.Distance > 32f
                && (item.Position - farEgg!.Position).LengthSquared() < 0.5f);
            if (sweep.Sectors.Length != 8 || sweep.Panorama.Radius != mapRange.Content || food.InReach || !samePose || !hidden || !farBlock || !farItem)
                throw new InvalidOperationException($"全向观察范围或遮挡不符合测试条件。原姿态 {samePose}，墙后隐藏 {hidden}，远处方块 {farBlock}，远处拾取物 {farItem}。");
            var towardFood = (food.Point - body.Position).XZ;
            var reserve = sweep.FoodInDirection(p, Vector2.Normalize(towardFood), 4.2f);
            bool reserveTarget = reserve?.Matches(food) == true && !PlaySurvivalFood.CanEat(p, eggValue);
            if (!reserveTarget) throw new InvalidOperationException("正常饱食时应保留生蛋的采集目标并限制进食。");
            float bearing = Vector2.Angle(body.Matrix.Forward.XZ, towardFood) * (180f / MathF.PI);
            travel = new(p, actions, bearing, 4.2f, "food", false, now, ReadForage,
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), Gate.Log, foodTarget: food);
            Gate.Log(new { kind = "forage_survey", sectors = sweep.Sectors.Length, sweep.Panorama.Radius, food.Name, food.Distance,
                bearing, samePose, hidden, farBlock, farItem, reserveTarget,
                durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds });
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

    private bool ForageCorridorReady(int startX, int z)
    {
        for (int x = startX; x <= startX + 38; x++)
        for (int offset = -4; offset <= 4; offset += 4)
            if (!(terrain.Terrain.GetChunkAtCell(x, z + offset)?.State >= TerrainChunkState.InvalidVertices1)) return false;
        return true;
    }

    private void RunShelter(double now)
    {
        if (stage >= 8)
        {
            RunCampReturn(now);
            return;
        }
        var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        bool wetTest = mode == "shelter-wet";
        bool latePredator = mode == "shelter-late-predator";
        if (stage == 1)
        {
            if (mode == "shelter-illness")
            {
                if (!p.ComponentSleep.CanSleep(out _)) return;
                refusedOpenCover = !PlaySurvivalRest.HasSecureCover(p);
            }
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
            if (mode == "shelter-predator" || latePredator)
            {
                if (!latePredator) PlaceShelterThreat(0.61f);
                vitalBefore = p.ComponentHealth.Health;
                earthShelter = new(p, actions, testSite, true, now, entry =>
                {
                    Gate.Log(entry);
                    var record = JsonSerializer.SerializeToElement(entry);
                    if (record.GetProperty("kind").GetString() == "play_shelter_exit_threat")
                        exitThreatDuringJump = record.GetProperty("duringJump").GetBoolean();
                });
                stage = 7;
                return;
            }
            if (wetTest && !openedForRepair)
            {
                earthShelter = new(p, actions, testSite, true, now, Gate.Log);
                stage = 6;
                return;
            }
            stage = 3;
        }
        if (stage == 6)
        {
            earthShelter!.Update(now);
            var roof = testSite!.Cell(testSite.Surface - 1);
            if (terrain.Terrain.GetCellContents(roof.X, roof.Y, roof.Z) != 0)
            {
                if (earthShelter.Status is not null) throw new InvalidOperationException("未能在出坑中断前拆顶。");
                return;
            }
            // 真实拆顶后停止出坑，保留已有地形和掉落，验证同一位置能再次封顶。
            earthShelter.Stop();
            openedForRepair = true;
            earthShelter = new(p, actions, testSite, false, now, Gate.Log);
            stage = 2;
            Gate.Log(new { kind = "shelter_repair_started", now });
            return;
        }
        if (stage == 7)
        {
            var body = p.ComponentBody;
            if (latePredator && !latePredatorPlaced && body.Position.Y >= testSite!.Surface - 0.45f
                && body.Position.Y < testSite.Surface && body.Velocity.Y > 0f)
            {
                PlaceShelterThreat(7.2f);
                latePredatorPlaced = true;
            }
            jumped |= p.ComponentLocomotion.LastJumpOrder > 0;
            earthShelter!.Update(now);
            if (earthShelter.Status is null) return;
            bool covered = PlaySurvivalRest.HasUsableCover(p);
            Passed = earthShelter.Reason == "predator_at_exit_resealed" && covered
                && (latePredator ? latePredatorPlaced && jumped && exitThreatDuringJump : !jumped)
                && p.ComponentBody.Position.Y <= testSite!.Surface - 2.95f && p.ComponentHealth.Health >= vitalBefore;
            Done = true;
            Gate.Log(new { kind = "shelter_predator_result", Passed, earthShelter.Reason, covered, jumped, exitThreatDuringJump,
                health = p.ComponentHealth.Health, floor = p.ComponentBody.Position.Y });
            return;
        }
        if (stage == 3)
        {
            if (mode == "shelter-illness")
            {
                if (!PlaySurvivalRest.HasSecureCover(p) || !p.ComponentSleep.CanSleep(out _)) return;
                p.ComponentHealth.Health = 0.4f;
                p.ComponentSickness.StartSickness();
                bool daytime = calendar.TimeOfDay >= calendar.DayStart && calendar.TimeOfDay < calendar.DuskStart;
                bool recovering = PlaySurvivalRest.NeedsRecovery(p);
                run = actions.Start(new Sleep());
                Passed = refusedOpenCover && daytime && recovering
                    && run.Result.Status == AtomicActionStatus.Succeeded && p.ComponentSleep.IsSleeping;
                Done = true;
                Gate.Log(new { kind = "shelter_illness_result", Passed, refusedOpenCover, daytime, recovering,
                    sleeping = p.ComponentSleep.IsSleeping, health = p.ComponentHealth.Health });
                return;
            }
            // 等角色自然产生疲劳，避免把刚出生时的睡眠前提误判为搭建失败。
            if (wetTest)
            {
                if (fixtureDrying is null)
                {
                    if (!p.ComponentSleep.CanSleep(out _)) return;
                    p.ComponentVitalStats.Wetness = 1f;
                    if (p.ComponentSleep.CanSleep(out _) || !PlaySurvivalRest.HasUsableCover(p))
                        throw new InvalidOperationException("湿睡夹具的庇护或湿度前提不符。");
                    fixtureDrying = new(p, now);
                    dryingStarted = now;
                    Gate.Log(new { kind = "shelter_fixture_wet", wetness = p.ComponentVitalStats.Wetness, now });
                }
                var dryingState = fixtureDrying.Update(now);
                if (dryingState == PlaySurvivalRest.State.Drying) return;
                if (dryingState != PlaySurvivalRest.State.Ready)
                    throw new InvalidOperationException("原生晾干等待没有完成。");
                Gate.Log(new { kind = "shelter_fixture_dried", wetness = p.ComponentVitalStats.Wetness,
                    seconds = now - dryingStarted, openedForRepair });
            }
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
            if (wetTest) p.ComponentLocomotion.JumpSpeed *= 0.95f;
            earthShelter = new(p, actions, testSite!, true, now, Gate.Log);
            stage = 5;
        }
        if (stage != 5) return;
        earthShelter!.Update(now);
        if (earthShelter.Status is null) return;
        Passed = earthShelter.Status == "succeeded" && p.ComponentBody.Position.Y >= testSite!.Surface - 0.05f
            && (mode != "shelter-retry" || changedDigSlot);
        Gate.Log(new { kind = "shelter_exit", Passed, earthShelter.Status, earthShelter.Reason, floor = p.ComponentBody.Position.Y });
        if (Passed && mode == "shelter-camp")
        {
            var body = p.ComponentBody;
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, Vector2.UnitX));
            travel = new(p, actions, bearing, 4f, "survey", false, now,
                () => (JsonSerializer.SerializeToElement(new { }), []),
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), Gate.Log);
            Passed = false;
            stage = 8;
        }
        else Done = true;
    }

    private void PlaceShelterThreat(float offset)
    {
        // 夹具固定猎食者移动能力，保留原生实体和遮挡。晚出现的棕熊在最后一跳进入场景。
        var bear = DatabaseManager.CreateEntity(p.Project, offset > 1f ? "Bear_Brown" : "Bear_Black", true);
        bear.FindComponent<ComponentBody>(true).Position = testSite!.Center(testSite.Surface) + offset * Vector3.UnitX;
        var locomotion = bear.FindComponent<ComponentLocomotion>(true);
        locomotion.WalkSpeed = 0f;
        locomotion.JumpSpeed = 0f;
        p.Project.AddEntity(bear);
    }

    private void RunCampReturn(double now)
    {
        var site = testSite!;
        var body = p.ComponentBody;
        if (stage == 10)
        {
            earthShelter!.Update(now);
            if (earthShelter.Status is null) return;
            bool contained = site.Contains(body);
            bool covered = PlaySurvivalRest.HasSecureCover(p);
            Passed = earthShelter.Status == "succeeded" && contained && covered;
            Done = true;
            Gate.Log(new { kind = "shelter_camp_result", Passed, earthShelter.Reason, contained, covered,
                floor = body.Position.Y, site.X, site.Z });
            return;
        }
        travel!.Update(now);
        if (travel.Status is null) return;
        if (travel.Status != "succeeded") throw new InvalidOperationException($"庇护复用导航失败。{travel.Reason}");
        if (stage == 8)
        {
            var center = site.Center(site.Surface);
            var offset = (center - body.Position).XZ;
            if (offset.Length() <= 3f) throw new InvalidOperationException("夹具需要先走离庇护位置。");
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
            travel = new(p, actions, bearing, offset.Length(), "camp", false, now,
                () => (JsonSerializer.SerializeToElement(new { }), []),
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), Gate.Log, shelterTarget: center);
            stage = 9;
            return;
        }
        if (travel.Reason != "remembered_earth_shelter_reached" || PlayEarthShelter.At(body) != site)
            throw new InvalidOperationException("夹具没有回到原生确认的旧庇护格。");
        earthShelter = new(p, actions, site, false, now, Gate.Log);
        stage = 10;
    }

    private void RunShallowShelter()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            testSite = new(cell.X, cell.Y, cell.Z);
            for (int x = cell.X - 3; x <= cell.X + 3; x++)
            for (int z = cell.Z - 3; z <= cell.Z + 3; z++)
            for (int y = cell.Y - 2; y <= cell.Y + 3; y++)
            {
                int floor = x == cell.X && z == cell.Z ? cell.Y - 2 : cell.Y - 1;
                terrain.ChangeCell(x, y, z, y <= floor ? DirtBlock.Index : 0);
            }
            body.Position = new(cell.X + 0.424f, cell.Y - 1, cell.Z + 0.475f);
            body.Velocity = Vector3.Zero;
            elapsed = 0;
            stage = 1;
            return;
        }
        if (elapsed < 0.75) return;
        var site = testSite!;
        if (travel is null)
        {
            if (NativeAcquisitionAudit.InventoryCount(p.ComponentMiner.Inventory, DirtBlock.Index) != 0 || !site.Contains(body)
                || !PlaySurvivalSenses.OnDryGround(body)) throw new InvalidOperationException("夹具需要在没有垫脚土的一格浅坑内。");
            var window = memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true));
            if (PlayDryRoutes.ShallowExit(window, site.Surface) is not { } point)
            {
                Gate.Log(new { kind = "shallow_exit_geometry", window.Position, site.Surface,
                    eye = p.ComponentCreatureModel.EyePosition.ToString(), cells = window.Cells
                        .Where(cell => Math.Abs(cell.X - site.X) + Math.Abs(cell.Z - site.Z) <= 1 && cell.Y >= site.Surface - 2)
                        .Select(cell => new { x = cell.X - site.X, y = cell.Y - site.Surface, z = cell.Z - site.Z,
                            value = Terrain.ExtractContents(cell.Value) }).ToArray() });
                throw new InvalidOperationException("全向观察没有确认浅坑的安全出口。");
            }
            var offset = (point - body.Position).XZ;
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
            travel = new(p, actions, bearing, offset.Length(), "survey", false, now,
                () => (JsonSerializer.SerializeToElement(new { }), []),
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), Gate.Log);
        }
        jumped |= p.ComponentLocomotion.LastJumpOrder > 0f;
        travel.Update(now);
        if (travel.Status is null) return;
        Passed = travel.Status == "succeeded" && jumped && !site.Contains(body)
            && MathF.Abs(body.Position.Y - site.Surface) <= 0.05f && p.ComponentHealth.Health >= 0.99f;
        Done = true;
        Gate.Log(new { kind = "shelter_shallow_result", Passed, travel.Status, travel.Reason, jumped, floor = body.Position.Y });
    }
}
