using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Decisions.Local;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

sealed partial class Runner
{
    private PlayableActor? goalActor;
    private NativeActorSession? goalSenses;
    private InterestCoordinator? goalInterest;
    private readonly PlayDecisionQueue goalQueue = new(1, null);
    private StructuredFileController? goalController;
    private readonly HashSet<string> goalReplies = new();
    private readonly List<JsonElement> goalEvents = new();
    private string? goalControlRoot;
    private Vector3 goalOrigin;
    private int goalEggValue;
    private Pickable? goalEgg;
    private ComponentCreatureSounds? goalWolfSounds;
    private double goalStarted;
    private bool goalSoundIssued, goalNightIssued;
    private bool goalAcquiredExact, goalAcquiredDecoy;
    private bool goalContractChecked;
    private int goalSections;
    private double goalWarmingBefore;
    private float? targetBeforeFire;
    private bool pendingVitalsChanged;

    private void RunSurvivalGoal()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            if (mode == "survival-goal-explore"
                && !(terrain.Terrain.GetChunkAtCell(cell.X + 65, cell.Z)?.State >= TerrainChunkState.InvalidVertices1)) return;
            if (Window.m_gameWindow.IsVisible) throw new InvalidOperationException("自动验收不应显示桌面窗口。");
            // 地形和初始库存仅由验收夹具布置，后续全部走正常角色入口。
            for (int x = cell.X - 5; x <= cell.X + (mode == "survival-goal-explore" ? 68 : 24); x++)
            for (int z = cell.Z - 6; z <= cell.Z + 6; z++)
            for (int y = cell.Y - 5; y <= cell.Y + 4; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y ? DirtBlock.Index : 0);
            body.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            body.Velocity = Vector3.Zero;
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
            p.ComponentLocomotion.LookAngles = new(0f, MathUtils.DegToRad(-70f));
            goalOrigin = body.Position;
            var carry = p.ComponentMiner.Inventory;
            for (int slot = 0; slot < carry.SlotsCount; slot++) carry.RemoveSlotItems(slot, carry.GetSlotCount(slot));
            p.ComponentVitalStats.Food = mode is "survival-goal-hearing" or "survival-goal-urgent-food" or "survival-goal-crop" ? 0.16f : 0.65f;
            p.ComponentVitalStats.Sleep = 0.65f;
            p.ComponentVitalStats.Temperature = 12f;
            var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
            calendar.TimeOfDayOffset += 0.3f - calendar.TimeOfDay;
            var eggs = (EggBlock)BlocksManager.Blocks[EggBlock.Index];
            int type = eggs.EggTypes.First(egg => egg.NutritionalValue > 0.15f).EggTypeIndex;
            goalEggValue = Terrain.MakeBlockValue(EggBlock.Index, 0, EggBlock.SetIsLaid(EggBlock.SetEggType(0, type), true));
            if (mode is "survival-goal-target" or "survival-goal-urgent-food" or "survival-goal-hearing")
            {
                var drops = p.Project.FindSubsystem<SubsystemPickables>(true);
                goalEgg = drops.AddPickable(goalEggValue, 1, goalOrigin + new Vector3(mode == "survival-goal-target" ? 9f : 6f, 0.2f, 0),
                    Vector3.Zero, null);
                if (mode == "survival-goal-target")
                    drops.AddPickable(goalEggValue, 1, goalOrigin + new Vector3(2.5f, 0.2f, 0), Vector3.Zero, null);
                NativeAcquisitionAudit.Recorded += ObserveGoalPickup;
            }
            if (mode == "survival-goal-crop")
            {
                int pumpkin = Terrain.MakeBlockValue(PumpkinBlock.Index, 0, BasePumpkinBlock.SetSize(0, 7));
                terrain.ChangeCell(cell.X + 5, cell.Y, cell.Z + 1, pumpkin);
            }
            if (mode == "survival-goal-hearing")
            {
                for (int z = cell.Z - 4; z <= cell.Z + 4; z++)
                for (int y = cell.Y; y <= cell.Y + 3; y++) terrain.ChangeCell(cell.X + 8, y, z, GraniteBlock.Index);
                var wolf = DatabaseManager.CreateEntity(p.Project, "Wolf_Gray", true);
                wolf.FindComponent<ComponentBody>(true).Position = goalOrigin + 10f * Vector3.UnitX;
                wolf.FindComponent<ComponentLocomotion>(true).WalkSpeed = 0f;
                p.Project.AddEntity(wolf);
                p.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(wolf.FindComponent<ComponentChaseBehavior>(true));
                goalWolfSounds = wolf.FindComponent<ComponentCreatureSounds>(true);
            }
            if (mode is "survival-goal-warming" or "survival-goal-pending-cold")
            {
                terrain.ChangeCell(cell.X + 5, cell.Y, cell.Z + 1, OakWoodBlock.Index);
                if (mode == "survival-goal-warming")
                {
                    p.ComponentVitalStats.Temperature = 5.5f;
                    p.ComponentVitalStats.Wetness = 0.5f;
                }
                goalWarmingBefore = p.ComponentVitalStats.Temperature;
            }
            goalActor = new(p, actions, Guid.NewGuid(), ObserveGoalEvent, survival: true, panoramaRadius: 32f, allowSocial: false);
            goalSenses = new(p.Project, Guid.NewGuid(), false, 3);
            goalSenses.Bind(goalActor.Id, p);
            p.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(goalSenses);
            goalActor.BindTravel(goalSenses);
            goalInterest = new(goalSenses.Registry, backend, new(16, 32));
            goalInterest.Sync();
            NativePredatorAcoustics.Emitted += goalActor.HearPredator;
            goalControlRoot = Path.Combine(AppContext.BaseDirectory, "goal-control");
            goalController = new(goalControlRoot, ObserveGoalEvent);
            if (mode == "survival-goal-fallback")
                goalQueue.TryStart(goalActor.Session, goalActor.Id, Guid.NewGuid(), TimeSpan.FromSeconds(2),
                    _ => Task.FromException<ToolDecisionPlan>(new IOException("本地验收模拟控制器暂时失效")));
            stage = 1;
            goalStarted = now;
            return;
        }
        if (now - goalStarted < 1) return;
        goalInterest!.Sync();
        if (!pendingVitalsChanged && goalQueue.IsPending(goalActor!.Id)
            && mode is "survival-goal-pending-hunger" or "survival-goal-pending-cold")
        {
            pendingVitalsChanged = true;
            if (mode == "survival-goal-pending-cold")
            {
                p.ComponentVitalStats.Temperature = 3.2f;
                goalWarmingBefore = 3.2f;
            }
            else
            {
                var carry = p.ComponentMiner.Inventory;
                int pumpkin = Terrain.MakeBlockValue(PumpkinBlock.Index, 0, BasePumpkinBlock.SetSize(0, 7));
                carry.AddSlotItems(0, pumpkin, 1);
                p.ComponentVitalStats.Food = 0.11f;
            }
        }
        float moved = (body.Position - goalOrigin).XZ.Length();
        if (mode == "survival-goal-hearing" && !goalSoundIssued && moved >= 0.7f)
        {
            bool hidden = !actions.Queries.FindVisibleObjectsAround(24f).Entities
                .Any(entity => entity.EntityId == goalWolfSounds!.Entity.Id);
            if (!hidden) throw new InvalidOperationException("听觉验收中的灰狼应该被墙挡住。");
            goalSoundIssued = true;
            goalWolfSounds!.PlayAttackSound();
        }
        if (mode == "survival-goal-night" && !goalNightIssued && moved >= 4f)
        {
            goalNightIssued = true;
            var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
            calendar.TimeOfDayOffset += calendar.DuskStart - 35f / calendar.DayDuration - calendar.TimeOfDay;
        }
        goalActor!.Update(goalQueue, now);
        goalActor.Request(goalQueue, goalController, now, delayFirst: false, smokeSpeaker: false);
        ReplyToGoalRequest();

        bool warmingConfirmed = goalEvents.Any(entry => Kind(entry) == "play_survival_warming_result"
            && Text(entry, "reason") == "native_body_recovery_confirmed");
        bool finished = goalEvents.Any(entry => Kind(entry) == "play_survival_goal_finished"
            && entry.GetProperty("goal").GetProperty("activity").GetString() == "explore"
            && entry.GetProperty("goal").GetProperty("status").GetString() == "succeeded");
        bool heardPaused = goalEvents.Any(entry => Kind(entry) == "play_predator_heard_route_paused");
        bool resumed = goalEvents.Any(entry => Kind(entry) == "play_food_objective" && entry.GetProperty("resuming").GetBoolean());
        bool fallback = goalEvents.Any(entry => Kind(entry) == "play_survival_goal_started" && Text(entry, "source") == "model_fallback");
        bool success = mode switch
        {
            "survival-goal-contract" => goalContractChecked,
            "survival-goal-crop" => goalQueue.Issued == 0 && goalEvents.Any(entry => Kind(entry) == "play_survival_goal_finished"
                && Text(entry.GetProperty("goal"), "activity") == "collect"
                && Text(entry.GetProperty("goal"), "status") == "succeeded"),
            "survival-goal-target" => goalAcquiredExact && goalAcquiredDecoy && goalQueue.Issued == 1,
            "survival-goal-urgent-food" => goalAcquiredExact && goalQueue.Issued == 0,
            "survival-goal-explore" => finished && goalSections == 3 && moved >= 45f && goalQueue.Issued == 1,
            "survival-goal-hearing" => goalSoundIssued && heardPaused && resumed && goalAcquiredExact,
            "survival-goal-fallback" => fallback && moved >= 4f && goalQueue.Issued == 1,
            "survival-goal-night" => goalNightIssued && goalActor.VoluntarySleeps == 1
                && !goalEvents.Any(entry => Kind(entry) == "play_outcome" && Text(entry, "tool") == PlaySurvivalIntents.BuildShelter
                    && Text(entry, "status") != "succeeded")
                && goalEvents.Any(entry => Kind(entry) == "play_survival_goal_finished"
                    && Text(entry.GetProperty("goal"), "reason") == "return_before_dark"),
            "survival-goal-warming" => warmingConfirmed && p.ComponentVitalStats.Temperature > goalWarmingBefore + 0.4
                && targetBeforeFire.HasValue && p.ComponentVitalStats.m_targetTemperature > targetBeforeFire.Value + 1f,
            "survival-goal-pending-cold" => pendingVitalsChanged && warmingConfirmed && goalQueue.Issued == 1,
            "survival-goal-pending-hunger" => pendingVitalsChanged && p.ComponentVitalStats.Food > 0.2f && goalQueue.Issued == 1
                && goalEvents.Any(entry => Kind(entry) == "play_survival_emergency_meal"),
            _ => false
        };
        if (!success && now - goalStarted < 76 && !goalActor.Died && !goalActor.Retired) return;
        Done = true;
        Passed = success && p.ComponentHealth.Health >= 0.95f;
        Gate.Log(new { kind = "survival_goal_fixture_result", mode, Passed, goalAcquiredExact, goalAcquiredDecoy,
            goalSections, moved, requests = goalQueue.Issued, goalContractChecked, heardPaused, resumed, fallback,
            goalNightIssued, sleeps = goalActor.VoluntarySleeps, warmingConfirmed,
            temperature = p.ComponentVitalStats.Temperature, targetBeforeFire,
            targetTemperature = p.ComponentVitalStats.m_targetTemperature, pendingVitalsChanged, health = p.ComponentHealth.Health,
            modelCalls = 0, windowVisible = Window.m_gameWindow.IsVisible, nativeSoundListenerFailures = NativePredatorAcoustics.ListenerFailures });
    }

    private static string? Kind(JsonElement entry) => Text(entry, "kind");
    private static string? Text(JsonElement entry, string key) => entry.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private void ObserveGoalEvent(object record)
    {
        var entry = JsonSerializer.SerializeToElement(record);
        goalEvents.Add(entry);
        if (Kind(entry) == "play_survival_exploration_section") goalSections = entry.GetProperty("section").GetInt32();
        if (Kind(entry) == "play_station_placed" && entry.GetProperty("stationBlock").GetInt32() == CampfireBlock.Index)
            targetBeforeFire = p.ComponentVitalStats.m_targetTemperature;
        Gate.Log(record);
    }

    private void ObserveGoalPickup(NativeAcquisitionFact fact)
    {
        if (fact.Value != goalEggValue || fact.ActorId != NativeAcquisitionAudit.IdentityFor(p.Entity)) return;
        if (goalEgg is { } egg && fact.SourceId == NativeAcquisitionAudit.IdentityFor(egg)) goalAcquiredExact = true;
        else goalAcquiredDecoy = true;
    }

    private void ReplyToGoalRequest()
    {
        if (mode is "survival-goal-pending-hunger" or "survival-goal-pending-cold") return;
        foreach (string requestPath in Directory.EnumerateFiles(goalControlRoot!, "request.json", SearchOption.AllDirectories))
        {
            if (!goalReplies.Add(requestPath)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(requestPath));
            var request = doc.RootElement;
            var offers = request.GetProperty("offers");
            var prompt = request.GetProperty("prompt").GetString()!;
            var responseSchema = request.GetProperty("responseSchema");
            if (offers.GetArrayLength() != 1 || offers[0].GetProperty("Name").GetString() != PlaySurvivalGoals.Tool
                || responseSchema.GetProperty("properties").GetProperty("steps").GetProperty("minItems").GetInt32() != 1
                || responseSchema.GetProperty("properties").GetProperty("steps").GetProperty("maxItems").GetInt32() != 1
                || prompt.Contains("MoveTo:") || !prompt.Contains(PlaySurvivalGoals.Guide))
                throw new InvalidOperationException("生存提示没有采用单目标协议。");
            var options = request.GetProperty("context").GetProperty("goalOptions").EnumerateArray().ToArray();
            var choice = mode == "survival-goal-target"
                ? options.First(option => Text(option, "activity") == "collect"
                    && option.GetProperty("details").GetProperty("distance").GetDouble() > 6)
                : options.Where(option => Text(option, "activity") == "explore")
                    .OrderBy(option => Math.Abs(option.GetProperty("details").GetProperty("bearing").GetDouble())).FirstOrDefault();
            if (choice.ValueKind == JsonValueKind.Undefined) choice = options[0];
            goalContractChecked = true;
            var result = new
            {
                sessionId = request.GetProperty("sessionId"), actorId = request.GetProperty("actorId"),
                observationId = request.GetProperty("observationId"), requestId = request.GetProperty("requestId"),
                decision = new { goal = "完成已核对的生存目标", steps = new[] { new
                {
                    tool = PlaySurvivalGoals.Tool, arguments = new { target = choice.GetProperty("id").GetString() }
                } } }
            };
            string temporary = Path.Combine(Path.GetDirectoryName(requestPath)!, "fixture-response.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(result));
            File.Move(temporary, Path.Combine(Path.GetDirectoryName(requestPath)!, "response.json"));
        }
    }

    private void DisposeSurvivalGoal()
    {
        if (goalActor is null) return;
        NativePredatorAcoustics.Emitted -= goalActor.HearPredator;
        NativeAcquisitionAudit.Recorded -= ObserveGoalPickup;
        goalQueue.Stop();
        goalActor.Stop();
        goalInterest?.Release();
        var updates = p.Project.FindSubsystem<SubsystemUpdate>(true);
        updates.RemoveUpdateable(goalSenses!);
        goalSenses!.Dispose();
    }
}
