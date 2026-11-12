namespace Game.Westworld {
    public sealed partial class ActorTools {
        // A declared guarded movement includes a bounded watch after success, so its
        // authorization can cover a network thinking gap. Expand into ordinary persisted
        // steps once; restoration must never reinsert the wait or repeat the movement.
        static ActorPlanStep[] ExpandGuardedMotion(IReadOnlyList<ActorPlanStep> submitted) {
            float Watch(ActorPlanStep step) => step.InterruptOn == "body_worsened"
                && step.Tool is "act_MoveTo" or "act_FaceTowards" ? step.WatchSeconds ?? 30 : 0;
            int[] indices = new int[submitted.Count + 1];
            for (int i = 0; i < submitted.Count; i++) indices[i + 1] = indices[i] + (Watch(submitted[i]) > 0 ? 2 : 1);
            if (indices[^1] > 12) throw new InvalidToolCall("invalid_arguments");
            List<ActorPlanStep> result = [];
            for (int i = 0; i < submitted.Count; i++) {
                ActorPlanStep step = submitted[i];
                if (step.Next is int next && (next <= i || next > submitted.Count)
                    || step.Fallback is { } branch && (branch.Step <= i || branch.Step >= submitted.Count)
                    || step.ItemFromStep is int source && (source < 0 || source >= i))
                    throw new InvalidToolCall("invalid_arguments");
                int normal = indices[step.Next ?? i + 1];
                ActorTaskFallback fallback = step.Fallback is { } original
                    ? original with { Step = indices[original.Step] } : null;
                float seconds = Watch(step);
                result.Add(step with { Next = seconds > 0 ? indices[i] + 1 : normal, Fallback = fallback, WatchSeconds = null,
                    ItemFromStep = step.ItemFromStep is int from ? indices[from] : null });
                if (seconds > 0) result.Add(new ActorPlanStep("act_Wait", ToJson(new { duration = seconds }), 0, 0,
                    null, normal, fallback) { InterruptOn = step.InterruptOn });
            }
            return result.ToArray();
        }
    }
}
