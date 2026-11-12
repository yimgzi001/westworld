using System.Text.Json;
using Engine;

namespace Game.Westworld {
    internal sealed record PlanMotion(float X, float Y, float Z) {
        internal Vector3 Point => new(X, Y, Z);
    }

    public sealed partial class ActorTools {
        ActorTaskStep[] AnchorCognitivePlan(ActorTaskStep[] steps, ObservationPose pose) {
            Vector3 origin = pose.Position;
            Vector3 facing = Vector3.Normalize(new Vector3(pose.Facing.X, 0, pose.Facing.Z));
            ActorPlanStep[] plans = steps.Select(step => step.Plan).ToArray();
            for (int i = 0; i < plans.Length; i++) {
                ActorPlanStep step = plans[i];
                if (step.Tool is not ("act_MoveTo" or "act_FaceTowards") || step.Arguments.TryGetProperty("destination", out _)) continue;
                float bearing = step.Arguments.GetProperty("bearing").GetSingle();
                float distance = step.Tool == "act_MoveTo" ? step.Arguments.GetProperty("distance").GetSingle() : 2;
                float vertical = step.Arguments.TryGetProperty("vertical", out JsonElement height) ? height.GetSingle() : 0;
                Vector3 point = origin + Vector3.Transform(facing, Quaternion.CreateFromAxisAngle(Vector3.UnitY,
                    MathUtils.DegToRad(-bearing))) * distance + Vector3.UnitY * vertical;
                plans[i] = step with { Motion = new(point.X, point.Y, point.Z) };
                // Every destination is selected in the one observation frame. A skipped or failed
                // earlier movement must never silently translate a later destination.
            }
            return CompilePlan(plans, 0, false);
        }

        AtomicAction ResolveAnchoredStep(ActorPlanStep step, ToolSpec tool, Args args) => step.Motion is not { } motion
            ? ResolveStep(tool, args) : step.Tool == "act_MoveTo"
                ? new MoveTo(motion.Point, args.Number("range", .4f), args.Number("timeout", 15f)) { AwaitRecovery = DeliberativeMode }
                : new FaceTowards(motion.Point, args.Number("timeout", 5f));

        internal string RecheckAnchoredPlan(JsonElement steps, ObservationPose pose, SelfView current, double observedAt) {
            if (NowForAnchor - observedAt > 30 || Vector3.DistanceSquared(pose.Position, current.Position) > 16)
                return "observation_expired";
            // Perception-relative motion is bound to its old world anchor, never reapplied to the new facing.
            if (steps[0].GetProperty("tool").GetString() is "act_MoveTo" or "act_FaceTowards") return null;
            return RecheckObservationPlan(steps, pose, current);
        }
        double NowForAnchor => m_actions.Queries.GameTime;
    }
}
