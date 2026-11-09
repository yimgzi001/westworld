using System.Text.Json;
using Engine;

namespace Game.Westworld {
    public sealed partial class ActorTools {
        Func<ActorSocialView[]> m_peekCognitiveInbox;

        internal int CognitiveActorId => m_tasks.ActorId;
        internal bool CognitiveWorldIsCurrent => GameManager.Project == m_actions.BoundActor.Project && m_actions.IsTaskActorAvailable;
        internal bool CognitiveActionRunning => m_actions.IsActionRunning;
        internal bool CognitiveTaskActive => TaskActive || CognitiveActionRunning;
        internal Guid? CognitiveActiveTaskId => m_tasks.ActiveTaskId;
        internal event Action<int, Guid, bool> CognitiveTaskActivityChanged {
            add => m_tasks.TaskActivityChanged += value;
            remove => m_tasks.TaskActivityChanged -= value;
        }
        internal bool HasActorNutrition => m_actions.ActorNutrition != null;
        internal double CognitiveTime => m_actions.Queries.GameTime;
        internal SelfView CognitiveBody => m_actions.Queries.ReadSelf();
        internal ActorTaskView CognitiveTask => m_tasks.Status;
        internal ActorTaskView CognitiveLastTerminal => m_tasks.LastTerminal;
        internal string[] CognitiveActions(bool cohort = false) => m_actions.GetCapabilities().Select(action => "act_" + action.Name)
            .Where(tool => !cohort || IsAvailableCohortTool(tool)).ToArray();

        bool IsAvailableCohortTool(string tool) => HasActorNutrition
            ? tool is "act_MoveTo" or "act_FaceTowards" or "act_PickUp" or "act_Eat" or "act_Wait"
            : IsCohortTool(tool);
        internal ActorSocialView[] CognitiveInbox() => DrainSocialViews();
        internal JsonElement CognitiveObservation() {
            ActorObservation observation = Observe(12f, 8f, 8);
            if (HasActorNutrition && observation.Self is { } self) {
                Vector2 forward = self.Facing.XZ;
                Vector2 right = new(-forward.Y, forward.X);
                var food = observation.Pickables.Select(item => {
                    Vector2 offset = (item.Position - self.Position).XZ;
                    Block block = BlocksManager.Blocks[Terrain.ExtractContents(item.Value)];
                    return new {
                        item.Value, item.Count, item.InReach, Name = block.GetType().Name,
                        Nutrition = block.GetNutritionalValue(item.Value),
                        Bearing = MathUtils.RadToDeg(MathF.Atan2(Vector2.Dot(right, offset), Vector2.Dot(forward, offset))),
                        HorizontalDistance = offset.Length()
                    };
                }).Where(item => item.Nutrition > 0f);
                return ToJson(new { observation.Focus, observation.Entities, FoodReserve = self.Food, Food = food });
            }
            return ToJson(new { observation.Focus, observation.Entities, observation.Pickables,
                observation.InventoryReference, observation.CraftingReference });
        }
        internal string DescribeDecisionTools(bool cohort = false) => JsonSerializer.Serialize(GetTools().Where(tool =>
            tool.Name == "submit_plan" || (cohort ? IsAvailableCohortTool(tool.Name) : tool.Name.StartsWith("act_", StringComparison.Ordinal)))
            .Select(tool => cohort && HasActorNutrition ? ForagingTool(tool) : tool), m_json);

        static ActorToolDefinition ForagingTool(ActorToolDefinition tool) => tool.Name switch {
            "act_MoveTo" => Describe(tool.Name, "Walk in the observed direction on local terrain.", [
                Number("bearing", "Relative bearing in degrees, from observation.food."),
                Number("distance", "Horizontal distance from 1 to 4 blocks."),
                Number("range", "Arrival radius in blocks. Use 0.25 to approach food.", true), Timeout(8)]),
            "act_FaceTowards" => Describe(tool.Name, "Look in a new direction to observe local surroundings.", [
                Number("bearing", "Relative bearing from -180 to 180 degrees."), Timeout(5)]),
            "act_PickUp" => Describe(tool.Name, "Pick up food whose observation shows inReach=true, using its exact observed value.", [
                Int("food_value", "Exact integer value of the observed food."), Count()]),
            _ => tool
        };
        internal InventoryView CognitiveInventory() => ReadInventory();
        internal EnvironmentView CognitiveEnvironment() => m_actions.Queries.ReadEnvironment();

        internal void BindCognition(Func<ActorSocialView[]> peekInbox) {
            if (m_peekCognitiveInbox != null) {
                throw new InvalidOperationException("This actor tool session already has a cognitive runtime.");
            }
            m_peekCognitiveInbox = peekInbox;
        }

        internal string ValidateCognitiveTask(string operation, JsonElement steps) {
            try {
                if (operation is "start" or "replace") {
                    if (CompileSubmittedPlan(steps).Any(step => !IsAvailableCohortTool(step.Plan.Tool) || !IsRestorable(step.Plan))) {
                        return "checkpoint_unsafe_action";
                    }
                }
                return null;
            }
            catch (Exception error) when (error is InvalidToolCall or InvalidOperationException) {
                return error is InvalidToolCall invalid ? invalid.Code : "invalid_decision";
            }
        }

        internal string RecheckObservationPlan(JsonElement steps, SelfView observed, SelfView current) {
            JsonElement first = steps[0];
            string tool = first.GetProperty("tool").GetString();
            JsonElement arguments = first.GetProperty("arguments");
            if (tool == "act_Eat") {
                int slot = arguments.GetProperty("slot").GetInt32();
                int value = arguments.GetProperty("value").GetInt32();
                if (CognitiveInventory()?.Slots.Any(item => item.Slot == slot && item.Value == value && item.Count > 0) != true
                    || current.Food is >= .98f) {
                    return "food_slot_changed";
                }
            }
            if (tool == "act_PickUp") {
                int value = arguments.GetProperty("food_value").GetInt32();
                if (!m_actions.Queries.FindVisibleObjects().Pickables.Any(item => item.Value == value && item.InReach)) {
                    return "food_out_of_reach";
                }
            }
            if (tool is "act_MoveTo" or "act_FaceTowards") {
                if (Vector3.DistanceSquared(observed.Position, current.Position) > .16f
                    || Vector3.Dot(observed.Facing, current.Facing) < .98f) {
                    return "pose_changed";
                }
                if (tool == "act_MoveTo" && steps.GetArrayLength() > 1
                    && steps[1].GetProperty("tool").GetString() == "act_PickUp") {
                    int value = steps[1].GetProperty("arguments").GetProperty("food_value").GetInt32();
                    MoveTo move = MoveCommand(new Args(arguments, FindActionTool(tool).Fields));
                    if (!m_actions.Queries.FindVisibleObjects().Pickables.Any(item => item.Value == value
                        && Vector2.DistanceSquared(item.Position.XZ, move.Destination.XZ) <= 1f)) {
                        return "food_at_destination_changed";
                    }
                }
            }
            return null;
        }

        internal string ApplyCognitiveTask(string operation, JsonElement steps) {
            try {
                switch (operation) {
                    case "keep":
                        return null;
                    case "cancel":
                        return m_tasks.Cancel() ? null : "no_active_task";
                    case "start":
                        if (TaskActive || m_actions.IsActionRunning) {
                            return "busy";
                        }
                        break;
                    case "replace":
                        if (m_actions.IsActionRunning && !m_tasks.OwnsRunningAction) {
                            return "busy";
                        }
                        if (!TaskActive) {
                            return "no_active_task";
                        }
                        break;
                }
                ActorTaskStep[] plan = CompileSubmittedPlan(steps);
                if (operation == "replace" && !m_tasks.Cancel()) {
                    return "no_active_task";
                }
                SubmitSteps(plan);
                return null;
            }
            catch (Exception error) when (error is InvalidToolCall or InvalidOperationException) {
                return error is InvalidToolCall invalid ? invalid.Code : "invalid_decision";
            }
        }
    }
}
