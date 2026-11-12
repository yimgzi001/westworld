using Engine;

namespace Game.Westworld {
    public sealed partial class AtomicActions {
        AtomicActionRun StartSurface(TraverseSurface request) {
            var origin = m_body.Position;
            var delta = request.Destination - origin;
            float length = delta.XZ.Length();
            if (!IsFinite(request.Destination) || !float.IsFinite(request.WaterSurfaceY)
                || !float.IsFinite(request.Timeout) || request.Timeout is <= 0f or > 5f
                || length > 2.2f || MathF.Abs(delta.Y) > 2.1f
                || request.Destination.Y > request.WaterSurfaceY + 1.05f
                || !request.Landing && MathF.Abs(request.Destination.Y - (request.WaterSurfaceY - .75f)) > .2f)
                return Reject(request, AtomicActionFailure.InvalidArgument);
            bool Available() => CanUseLocomotion && m_body.ParentBody == null && m_body.StandingOnBody == null
                && !m_locomotion.LadderValue.HasValue && !m_locomotion.IsCreativeFlyEnabled
                && !ComponentLocomotion.IsCreativeFlyEnabledSet && !HasMovementDestination;
            if (!Available()) return Reject(request, AtomicActionFailure.PreconditionsNotMet);
            double deadline = m_time.GameTime + request.Timeout, lastProgress = m_time.GameTime, nextJump = 0;
            float closest = float.MaxValue;
            int jumps = 0;
            Vector2? walk = null, previousWalk = null;
            Vector3? swim = null, previousSwim = null;
            Vector2 turn = default, previousTurn = default;
            Vector2 look = default, previousLook = default;
            float jump = 0, previousJump = 0;
            double orderTime = -1;
            return StartRunning(request, () => {
                if (!Available()) return Failed(AtomicActionFailure.MovementInterrupted);
                var position = m_body.Position;
                var offset = request.Destination - position;
                float horizontal = offset.XZ.Length();
                double now = m_time.GameTime;
                // 只续接同一水面的短段。靠岸、转角和落脚仍保留独立的到达验证。
                if (!request.Landing && horizontal <= .5f && MathF.Abs(offset.Y) < .7f
                    && request.Continue?.Invoke(now) is { } next && !next.Route.Landing
                    && IsFinite(next.Route.Destination) && float.IsFinite(next.Route.WaterSurfaceY)
                    && MathF.Abs(next.Route.WaterSurfaceY - request.WaterSurfaceY) < .05f
                    && MathF.Abs(next.Route.Destination.Y - (next.Route.WaterSurfaceY - .75f)) < .2f
                    && (next.Route.Destination - position).XZ.Length() is >= 1.1f and <= 2.2f
                    && MathF.Abs(next.Route.Destination.Y - position.Y) <= 2.1f
                    && float.IsFinite(next.Route.Timeout) && next.Route.Timeout is > 0f and <= 5f) {
                    request = next.Route; origin = position; delta = request.Destination - position; length = delta.XZ.Length();
                    offset = delta; horizontal = length; closest = float.MaxValue;
                    deadline = now + request.Timeout; lastProgress = now;
                    next.Accepted();
                }
                bool dry = m_body.StandingOnValue.HasValue && m_body.ImmersionDepth < .04f
                    && MathF.Abs(m_body.Velocity.Y) < .4f;
                if (horizontal <= (request.Landing ? .16f : .28f)
                    && (request.Landing ? dry && MathF.Abs(offset.Y) < .04f : MathF.Abs(offset.Y) < .5f))
                    return Completed();
                if (position.Y < Math.Min(origin.Y - .25f, request.WaterSurfaceY - 1.5f)
                    || position.Y > Math.Max(origin.Y, request.Destination.Y) + 1.6f)
                    return Failed(AtomicActionFailure.MovementInterrupted);
                if (length > .1f) {
                    var moved = (position - origin).XZ;
                    float along = Vector2.Dot(moved, delta.XZ) / length;
                    float cross = MathF.Abs(moved.X * delta.Z - moved.Y * delta.X) / length;
                    if (cross > .5f || along < -.3f || along > length + .5f)
                        return Failed(AtomicActionFailure.MovementInterrupted);
                }
                float remaining = horizontal + MathF.Abs(offset.Y) * .35f;
                if (remaining + .05f < closest) { closest = remaining; lastProgress = now; }
                if (now >= deadline) return Failed(AtomicActionFailure.TimedOut);
                if (now - lastProgress > 2) return Failed(AtomicActionFailure.Blocked);
                var direction = horizontal > .03f ? offset.XZ / horizontal : Vector2.Zero;
                var forward = m_body.Matrix.Forward.XZ;
                var right = m_body.Matrix.Right.XZ;
                previousWalk = m_locomotion.WalkOrder; previousSwim = m_locomotion.SwimOrder;
                previousTurn = m_locomotion.TurnOrder; previousJump = m_locomotion.JumpOrder;
                previousLook = m_locomotion.LookOrder;
                float speed = Math.Clamp(horizontal, 0, .7f);
                walk = speed * new Vector2(Vector2.Dot(right, direction), Vector2.Dot(forward, direction));
                // 水面节点保持头部露出水面；不把水底物品变成自动潜水目标。
                float rise = Math.Clamp((request.WaterSurfaceY - .75f - position.Y) * .8f, -.12f, .5f);
                swim = new Vector3(speed * direction.X, rise, speed * direction.Y);
                turn = horizontal > .1f ? new Vector2(Math.Clamp(Vector2.Angle(forward, direction), -1f, 1f), 0) : Vector2.Zero;
                jump = 0;
                bool surfacing = !request.Landing && position.Y < request.Destination.Y - .25f;
                if ((surfacing || request.Landing && horizontal <= 1.45f && offset.Y > .22f && jumps < 3) && now >= nextJump
                    && (m_body.StandingOnValue.HasValue || m_body.ImmersionFactor > .5f)) {
                    jump = 1; jumps++; nextJump = now + .65;
                }
                // 原生玩家 SwimSpeed 为零：按正常玩家的抬头、行走、跳跃输入游泳，不能借用鱼类游泳速度。
                float pitch = m_body.ImmersionDepth > .1f ? (surfacing ? 25f : 12f) : -8f;
                float lookStep = m_locomotion.LookSpeed * m_time.GameTimeDelta;
                look = lookStep > 0 ? new Vector2(Math.Clamp(-m_locomotion.LookAngles.X / lookStep, -1f, 1f),
                    Math.Clamp((MathUtils.DegToRad(pitch) - m_locomotion.LookAngles.Y) / lookStep, -1f, 1f)) : Vector2.Zero;
                orderTime = now;
                m_locomotion.WalkOrder = walk; m_locomotion.SwimOrder = swim;
                m_locomotion.TurnOrder = turn; m_locomotion.JumpOrder = jump;
                m_locomotion.LookOrder = look;
                return null;
            }, () => {
                // 只回收自己在当前帧写入的指令，不覆盖另一动作已写入的命令。
                if (m_time.GameTime != orderTime) return;
                if (m_locomotion.WalkOrder == walk) m_locomotion.WalkOrder = previousWalk;
                if (m_locomotion.SwimOrder == swim) m_locomotion.SwimOrder = previousSwim;
                if (m_locomotion.TurnOrder == turn) m_locomotion.TurnOrder = previousTurn;
                if (m_locomotion.LookOrder == look) m_locomotion.LookOrder = previousLook;
                if (m_locomotion.JumpOrder == jump) m_locomotion.JumpOrder = previousJump;
            });
        }
    }
}
