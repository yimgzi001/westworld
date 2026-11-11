using System.Text.Json;
using Engine;

namespace Game.Westworld {
    public sealed partial class ActorTools {
        Func<ActorSocialView[]> m_peekCognitiveInbox;

        internal int CognitiveActorId => m_tasks.ActorId;
        internal bool CognitiveWorldIsCurrent => GameManager.Project == m_actions.BoundActor.Project && m_actions.IsTaskActorAvailable;
        internal bool CognitiveActionRunning => m_actions.IsActionRunning;
        internal bool CognitiveAtCheckpointBoundary => m_tasks.AtCheckpointBoundary;
        internal bool CognitiveTaskActive => TaskActive || CognitiveActionRunning;
        internal Guid? CognitiveActiveTaskId => m_tasks.ActiveTaskId;
        internal event Action<int, Guid, bool> CognitiveTaskActivityChanged {
            add => m_tasks.TaskActivityChanged += value;
            remove => m_tasks.TaskActivityChanged -= value;
        }
        internal event Action<Guid, ActorPlanStep, ActorTaskStepView> CognitiveStepFinished {
            add => m_tasks.StepFinished += value;
            remove => m_tasks.StepFinished -= value;
        }
        internal bool HasActorNutrition => m_actions.ActorNutrition != null;
        internal double CognitiveTime => m_actions.Queries.GameTime;
        internal SelfView CognitiveBody => m_actions.Queries.ReadSelf();
        internal ActorTaskView CognitiveTask => m_tasks.Status;
        internal ActorTaskView CognitiveLastTerminal => m_tasks.LastTerminal;
#if WINDOWS
        internal AtomicAction PanelAction => m_actions.PanelAction;
        internal GameEntitySystem.Entity PanelActor => m_actions.BoundActor;
#endif
        internal void RestoreCognitiveLastTerminal(ActorTaskView terminal) => m_tasks.RestoreLastTerminal(terminal);
        internal string[] CognitiveActions(bool checkpointable = false) => m_actions.GetCapabilities().Select(action => "act_" + action.Name)
            .Where(tool => !checkpointable || IsAvailableRestorableTool(tool)).ToArray();

        bool IsAvailableRestorableTool(string tool) => HasActorNutrition
            ? IsProductionTool(tool) || tool is "act_MoveTo" or "act_FaceTowards" or "act_PickUp" or "act_Eat" or "act_Wait" or "act_Speak"
            : IsRestorableTool(tool);
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
                if (HasProduction) {
                    var focus = observation.Focus is { } seen ? new { seen.Kind, seen.Name, seen.Distance } : null;
                    var entities = observation.Entities.Select(entity => {
                        var direction = RelativeDirection(entity.Position, self);
                        var target = m_memory.ResolveEntity(entity.Reference);
                        return new {
                            Reference = PersonReference(target), entity.Name, entity.Position, entity.Distance,
                            Bearing = direction.Bearing, HorizontalDistance = direction.Distance,
                            SupportsItemOffers = m_actions.CanReceiveItemOffers(target)
                        };
                    }).Where(entity => entity.Reference != null).ToArray();
                    return ToJson(new { Focus = focus, Entities = entities, FoodReserve = self.Food, Food = food,
                        Production = ProductionObservation(observation, self) });
                }
                return ToJson(new { observation.Focus, observation.Entities, FoodReserve = self.Food, Food = food });
            }
            return ToJson(new { observation.Focus, observation.Entities, observation.Pickables,
                observation.InventoryReference, observation.CraftingReference });
        }
        internal string DescribeDecisionTools(bool checkpointable = false) => JsonSerializer.Serialize(GetTools().Where(tool =>
            tool.Name == "submit_plan" || (checkpointable ? IsAvailableRestorableTool(tool.Name) : tool.Name.StartsWith("act_", StringComparison.Ordinal)))
            .Select(tool => checkpointable && HasActorNutrition ? ForagingTool(tool) : tool), m_json);

        ActorToolDefinition ForagingTool(ActorToolDefinition tool) => tool.Name switch {
            "act_MoveTo" => Describe(tool.Name, "Walk in the observed direction on local terrain.", [
                Number("bearing", HasProduction ? "Relative bearing in degrees from observed people, food, logs, or materials."
                    : "Relative bearing in degrees, from observation.food."),
                Number("distance", HasProduction ? "Horizontal distance from 0.2 to 4 blocks." : "Horizontal distance from 1 to 4 blocks."),
                Number("range", "Arrival radius in blocks. Use 0.25 to approach food.", true), Timeout(8)]),
            "act_FaceTowards" => Describe(tool.Name, "Look in a new direction to observe local surroundings.", [
                Number("bearing", "Relative bearing from -180 to 180 degrees."), Timeout(5)]),
            "act_PickUp" when HasProduction => Describe(tool.Name, "Pick up visible food or dropped wood within reach. Provide exactly one of food_value or material_value.", [
                Int("food_value", "Exact value from observation.food.", true),
                Int("material_value", "Exact value from observation.production.materials.", true), Count()]),
            "act_PickUp" => Describe(tool.Name, "Pick up food whose observation shows inReach=true, using its exact observed value.", [
                Int("food_value", "Exact integer value of the observed food."), Count()]),
            "act_Dig" when HasProduction => Describe(tool.Name, "Cut a visible log using the held tool.", [
                Ref("target", "Copy the exact target from observation.production.logs."), Timeout(15)]),
            "act_Craft" when HasProduction => m_personalCraftTool.Definition,
            "act_TransferItems" when HasProduction => Describe(tool.Name, "Transfer ingredients between your backpack (carried) and your 2x2 crafting grid (personal).", [
                Choice("source", "Use carried for your backpack, or personal for your crafting grid.", false, CarriedStorage, PersonalStorage),
                Int("source_slot", "Slot in the source inventory."), Int("source_value", "Exact value in that slot."),
                Choice("destination", "Use personal for your crafting grid, or carried for your backpack.", false, CarriedStorage, PersonalStorage),
                Int("destination_slot", "Slot in the destination inventory."), Int("destination_value", "Exact value in that slot, or zero if empty."),
                Count()]),
            "act_OfferItem" when HasProduction => Describe(tool.Name, "Offer items from your own bag to a person within two blocks. The recipient decides separately whether to accept.", [
                Ref("target", "Use the intended person's exact received message source, or their observation.entities reference with supportsItemOffers=true. Other visible people are separate individuals."), Slot(), Value(), Count()]),
            "act_RespondToOffer" when HasProduction => Describe(tool.Name, "Accept or decline a live offer. Accepting transfers the items into your bag.", [
                Ref("offer", "Copy offerReference from the itemOffered message."), Bool("accept", "Whether to accept.")]),
            "act_Speak" => Describe(tool.Name, "Speak to nearby listeners without a target. Use zero retries and no minimum_affected.", [
                Text("text", "Short words to speak."), Choice("mode", "Speech volume.", true, "whisper", "normal", "shout")]),
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
                    if (CompileSubmittedPlan(steps).Any(step => !IsAvailableRestorableTool(step.Plan.Tool) || !IsRestorable(step.Plan))) {
                        return "checkpoint_unsafe_action";
                    }
                }
                return null;
            }
            catch (Exception error) when (error is InvalidToolCall or InvalidOperationException) {
                return error is InvalidToolCall invalid ? invalid.Code : "invalid_decision";
            }
        }

        internal string RecheckObservationPlan(JsonElement steps, ObservationPose observed, SelfView current) {
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
                int value = PickupValue(arguments);
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
                    int value = PickupValue(steps[1].GetProperty("arguments"));
                    MoveTo move = MoveCommand(new Args(arguments, FindActionTool(tool).Fields));
                    if (!m_actions.Queries.FindVisibleObjects().Pickables.Any(item => item.Value == value
                        && Vector2.DistanceSquared(item.Position.XZ, move.Destination.XZ) <= 1f)) {
                        return "food_at_destination_changed";
                    }
                }
            }
            return null;
        }

        static int PickupValue(JsonElement arguments) => (arguments.TryGetProperty("food_value", out JsonElement value)
            ? value : arguments.GetProperty("material_value")).GetInt32();

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
