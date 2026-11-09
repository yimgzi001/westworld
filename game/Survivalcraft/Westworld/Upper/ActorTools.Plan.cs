using System.Text.Json;

namespace Game.Westworld {
    internal sealed record ActorTaskFallback(int Step, AtomicActionFailure Failure);

    internal sealed record ActorPlanStep(string Tool, JsonElement Arguments, int Retries, int MinimumAffected,
        int? ItemFromStep, int? Next, ActorTaskFallback Fallback);

    internal sealed class ActorTaskStepUnavailable(AtomicActionFailure failure) : Exception {
        internal AtomicActionFailure Failure { get; } = failure;
    }

    public sealed partial class ActorTools {
        static readonly Field[] ItemBindingFields = [Count()];
        static readonly IReadOnlyDictionary<string, AtomicActionFailure> m_recoverableFailures = new Dictionary<string, AtomicActionFailure> {
            ["target_changed"] = AtomicActionFailure.TargetChanged,
            ["out_of_reach"] = AtomicActionFailure.OutOfReach,
            ["blocked"] = AtomicActionFailure.Blocked,
            ["timed_out"] = AtomicActionFailure.TimedOut,
            ["inventory_full"] = AtomicActionFailure.InventoryFull,
            ["insufficient_items"] = AtomicActionFailure.InsufficientItems,
            ["movement_interrupted"] = AtomicActionFailure.MovementInterrupted
        };

        ActorTaskView SubmitPlan(JsonElement input) {
            if (TaskActive || DirectActive) {
                throw new InvalidToolCall("busy");
            }
            return SubmitSteps(CompileSubmittedPlan(input));
        }

        ActorTaskStep[] CompileSubmittedPlan(JsonElement input) {
            if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() is < 1 or > 12) {
                throw new InvalidToolCall("invalid_arguments");
            }

            List<ActorPlanStep> plan = [];
            foreach (JsonElement step in input.EnumerateArray()) {
                Args entry = new(step, PlanFields);
                ActorTaskFallback fallback = null;
                if (entry.Has("fallback")) {
                    Args branch = new(entry.Json("fallback"), FallbackFields);
                    if (!m_recoverableFailures.TryGetValue(branch.String("failure"), out AtomicActionFailure reason)) {
                        throw new InvalidToolCall("invalid_arguments");
                    }
                    fallback = new ActorTaskFallback(branch.Int("step"), reason);
                }
                plan.Add(new ActorPlanStep(entry.String("tool"), entry.Json("arguments").Clone(), entry.Int("retries"),
                    entry.Int("minimum_affected"), entry.OptionalInt("item_from_step"), entry.OptionalInt("next"), fallback));
            }
            return CompilePlan(plan, firstPendingStep: 0, restoring: false);
        }

        ActorTaskStep[] CompilePlan(IReadOnlyList<ActorPlanStep> plan, int firstPendingStep, bool restoring) {
            HashSet<string> supported = m_actions.GetCapabilities().Select(action => action.Name).ToHashSet(StringComparer.Ordinal);
            HashSet<int> boundOutputs = [];
            ActorTaskStep[] steps = new ActorTaskStep[plan.Count];
            for (int index = 0; index < plan.Count; index++) {
                ActorPlanStep step = plan[index] ?? throw new InvalidToolCall("invalid_arguments");
                ToolSpec tool = FindActionTool(step.Tool) ?? throw new InvalidToolCall("unknown_tool");
                if (index >= firstPendingStep && !supported.Contains(tool.Action)) {
                    throw new InvalidToolCall("tool_unavailable");
                }
                if (step.Retries is < 0 or > 2 || step.MinimumAffected < 0
                    || step.Next is int next && (next <= index || next > plan.Count)
                    || step.Fallback is { } branch && (branch.Step <= index || branch.Step >= plan.Count
                        || !CanRecover(tool.Action, branch.Failure))) {
                    throw new InvalidToolCall("invalid_arguments");
                }

                Func<AtomicAction> create;
                if (step.ItemFromStep is int source) {
                    if (tool.Action != nameof(Eat) || source < 0 || source >= index
                        || plan[source].Tool != "act_PickUp" || !boundOutputs.Add(source)
                        || plan.Skip(source + 1).Take(index - source - 1).Any(intermediate => !PreservesInventory(intermediate.Tool))) {
                        throw new InvalidToolCall("invalid_arguments");
                    }
                    int count = new Args(step.Arguments, ItemBindingFields).Int("count");
                    if (count <= 0) {
                        throw new InvalidToolCall("invalid_arguments");
                    }
                    create = () => EatReceivedItem(source, count);
                }
                else {
                    Args args = new(step.Arguments, tool.Fields);
                    if (index >= firstPendingStep) {
                        if (restoring && !IsRestorable(step)) {
                            throw new InvalidToolCall("checkpoint_target_unavailable");
                        }
                        tool.Create(this, args);
                    }
                    create = () => ResolveStep(tool, args);
                }
                steps[index] = new ActorTaskStep(tool.Action, create, step.Retries, step.MinimumAffected) { Plan = step };
            }
            return steps;
        }

        // These actions have an established resource-effect boundary for the listed failures.
        static bool CanRecover(string action, AtomicActionFailure failure) => action switch {
            nameof(MoveTo) => failure is AtomicActionFailure.Blocked or AtomicActionFailure.TimedOut
                or AtomicActionFailure.MovementInterrupted,
            nameof(PickUp) => failure is AtomicActionFailure.TargetChanged or AtomicActionFailure.OutOfReach
                or AtomicActionFailure.Blocked or AtomicActionFailure.InventoryFull,
            nameof(Eat) => failure is AtomicActionFailure.TargetChanged or AtomicActionFailure.InsufficientItems
                or AtomicActionFailure.InventoryFull,
            _ => false
        };

        static bool PreservesInventory(string tool) => tool is "act_Wait" or "act_MoveTo" or "act_FaceTowards"
            or "act_SetPosture" or "act_Jump" or "act_ClimbTo" or "act_Sleep" or "act_WakeUp" or "act_Speak";

        static bool IsRestorableTool(string tool) => tool is "act_Wait" or "act_Eat" or "act_SelectItem" or "act_Drop"
            or "act_Equip" or "act_Unequip" or "act_SetPosture" or "act_Jump" or "act_Sleep" or "act_WakeUp";

        static bool IsRestorable(ActorPlanStep step) => IsRestorableTool(step.Tool)
            || step.Tool is "act_MoveTo" or "act_FaceTowards" && !step.Arguments.TryGetProperty("destination", out _)
            || step.Tool == "act_PickUp" && step.Arguments.TryGetProperty("food_value", out _)
            // A session reference cannot be restored, and retrying can repeat words already heard.
            || step.Tool == "act_Speak" && step.Retries == 0 && step.MinimumAffected == 0
                && !step.Arguments.TryGetProperty("target", out _);

        AtomicAction ResolveStep(ToolSpec tool, Args args) {
            try {
                return tool.Create(this, args);
            }
            catch (InvalidToolCall error) when (error.Code == "unknown_reference") {
                throw new ActorTaskStepUnavailable(AtomicActionFailure.TargetChanged);
            }
        }

        Eat EatReceivedItem(int step, int count) {
            IInventory inventory = m_actions.BoundActor.FindComponent<ComponentMiner>()?.Inventory;
            IReadOnlyList<InventorySlotView> received = m_tasks.ReceivedItems(step);
            if (inventory != null && received != null) {
                foreach (InventorySlotView item in received) {
                    if (item.Slot < inventory.SlotsCount && item.Count >= count && inventory.GetSlotValue(item.Slot) == item.Value
                        && inventory.GetSlotCount(item.Slot) >= count) {
                        return new Eat(item.Slot, item.Value, count);
                    }
                }
            }
            throw new ActorTaskStepUnavailable(AtomicActionFailure.InsufficientItems);
        }
    }
}
