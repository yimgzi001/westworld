using Engine;

namespace Game.Westworld {
    public sealed partial class AtomicActions {
        // Trusted local executor only. Observation/loaded/support proof belongs to the caller.
        // No world scan, search, random movements, teleport or extra movement owner.
        AtomicActionRun StartObservedRoute(MoveAlongObservedRoute request) {
            Vector3 origin = m_body.Position;
            if (!TryReadRoutePoints(request, origin, out Vector3[] points))
                return Reject(request, AtomicActionFailure.InvalidArgument);
            var action = request with { Points = Array.AsReadOnly(points) };
            var routeAction = action;
            bool Grounded() => CanUseLocomotion && m_locomotion.WalkSpeed > 0
                && m_body.StandingOnValue.HasValue && m_body.ImmersionFactor <= .1f
                && !m_locomotion.LadderValue.HasValue && !m_locomotion.IsCreativeFlyEnabled
                && !ComponentLocomotion.IsCreativeFlyEnabledSet && MathF.Abs(m_body.Position.Y-origin.Y) <= .15f;
            if (!Grounded() || HasMovementDestination)
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            // Complete Player has no Pilot/Pathfinding. Reuse the native locomotion
            // orders of StartDirectMove, under this same AtomicActions run/owner.
            double started = m_time.GameTime, deadline = started + action.Timeout, lastProgress = started, lastGrounded = started;
            int index = 0; Vector3 segmentOrigin = origin, target = points[0];
            bool continuationChecked = false;
            float closest=(target-origin).XZ.Length();
            Vector2? lastWalk=null, previousWalk=null;
            Vector2 lastTurn=Vector2.Zero, previousTurn=Vector2.Zero;
            Vector2? lastLook = null;
            Vector2 previousLook = Vector2.Zero;
            double orderTime=double.NegativeInfinity;
            return StartRunning(action, () => {
                double now = m_time.GameTime;
                if (now >= deadline || now - started >= 25) return Failed(AtomicActionFailure.TimedOut);
                if (!Grounded() || HasMovementDestination) {
                    // 高频物理帧可能只清空支撑标记，高度和速度尚未改变。短暂等待接触重建，此帧不写移动命令。
                    bool contactGap = !m_body.StandingOnValue.HasValue && CanUseLocomotion && !HasMovementDestination
                        && m_body.StandingOnBody == null && m_body.ImmersionDepth < .02f
                        && !m_locomotion.LadderValue.HasValue && !m_locomotion.IsCreativeFlyEnabled
                        && !ComponentLocomotion.IsCreativeFlyEnabledSet && MathF.Abs(m_body.Position.Y - origin.Y) < .02f
                        && MathF.Abs(m_body.Velocity.Y) < .1f;
                    if (contactGap && now - lastGrounded <= .12) return null;
                    routeAction.Rejected?.Invoke(FormattableString.Invariant($"support: standing={m_body.StandingOnValue}, immersion={m_body.ImmersionFactor}, dy={m_body.Position.Y-origin.Y}, velocityY={m_body.Velocity.Y}"));
                    return Failed(AtomicActionFailure.MovementInterrupted);
                }
                lastGrounded = now;
                Vector2 route = (target-segmentOrigin).XZ, offset=(m_body.Position-segmentOrigin).XZ;
                float length=route.Length(), along=Vector2.Dot(offset,route)/length;
                float cross=MathF.Abs(offset.X*route.Y-offset.Y*route.X)/length;
                if(cross > .25f || along < -.2f || along > length+.3f) {
                    routeAction.Rejected?.Invoke(FormattableString.Invariant($"corridor: along={along}, cross={cross}, length={length}, from={segmentOrigin}, to={target}, at={m_body.Position}"));
                    return Failed(AtomicActionFailure.MovementInterrupted);
                }
                float distance=(m_body.Position-target).XZ.Length();
                // 到达末段前从实际脚下取得下一段。接续成功时当帧继续写原生行走指令。
                if (index == points.Length - 1 && distance <= .65f && !continuationChecked && action.Continue is { } getNext) {
                    continuationChecked = true;
                    if (getNext(now) is { } next && TryReadRoutePoints(next.Route, m_body.Position, out var nextPoints)) {
                        routeAction = next.Route;
                        points = nextPoints;
                        index = 0;
                        segmentOrigin = m_body.Position;
                        target = points[0];
                        closest = distance = (target - segmentOrigin).XZ.Length();
                        lastProgress = now;
                        deadline = now + routeAction.Timeout;
                        continuationChecked = false;
                        next.Accepted();
                    }
                }
                if(index==points.Length-1 && distance<=routeAction.FinalRange) return Completed();
                if(index<points.Length-1 && distance<=.25f) {
                    // Advance target in the same run and same tick. No intermediate
                    // Finish, Stop, zero command, or renewal of the whole-route deadline.
                    segmentOrigin=m_body.Position;target=points[++index];
                    distance=closest=(target-segmentOrigin).XZ.Length();lastProgress=m_time.GameTime;
                    if(distance<.05f) return Failed(AtomicActionFailure.InvalidArgument);
                }
                if(distance+.04f<closest){closest=distance;lastProgress=m_time.GameTime;}
                if(m_time.GameTime-lastProgress>=1.5) return Failed(AtomicActionFailure.Blocked);
                Vector2 direction=Vector2.Normalize((target-m_body.Position).XZ);
                var forward=m_body.Matrix.Forward.XZ;var right=m_body.Matrix.Right.XZ;
                float speed=index==points.Length-1?Math.Clamp(distance,.2f,.6f):.6f;
                previousWalk=m_locomotion.WalkOrder;previousTurn=m_locomotion.TurnOrder;
                lastWalk=speed*new Vector2(Vector2.Dot(right,direction),Vector2.Dot(forward,direction));
                lastTurn=new Vector2(Math.Clamp(Vector2.Angle(forward,direction),-1,1),0);
                orderTime=m_time.GameTime;m_locomotion.WalkOrder=lastWalk;m_locomotion.TurnOrder=lastTurn;
                // 行走只沿已验证的走廊，抬头可在同一个动作里完成。
                float maxLook = m_locomotion.LookSpeed * m_time.GameTimeDelta;
                if (action.LevelGaze && maxLook > 0f) {
                    previousLook = m_locomotion.LookOrder;
                    var angles = m_locomotion.LookAngles;
                    lastLook = new Vector2(Math.Clamp(-angles.X / maxLook, -1f, 1f),
                        Math.Clamp((MathUtils.DegToRad(-8f) - angles.Y) / maxLook, -1f, 1f));
                    m_locomotion.LookOrder = lastLook.Value;
                }
                return null;
            }, () => {
                // Locomotion consumes/resets orders each frame. Never clear a later
                // writer's different value; identical-value foreign writes cannot be
                // distinguished by this native API. External host lease is mandatory.
                if(m_time.GameTime==orderTime){
                    if(m_locomotion.WalkOrder==lastWalk)m_locomotion.WalkOrder=previousWalk;
                    if(m_locomotion.TurnOrder==lastTurn)m_locomotion.TurnOrder=previousTurn;
                    if(lastLook == m_locomotion.LookOrder)m_locomotion.LookOrder=previousLook;
                }
            });
        }

        static bool TryReadRoutePoints(MoveAlongObservedRoute request, Vector3 origin, out Vector3[] points) {
            points = [];
            if (request.Points == null || request.Points.Count is < 1 or > 4
                || !float.IsFinite(request.FinalRange) || request.FinalRange is < .05f or > .4f
                || !float.IsFinite(request.Timeout) || request.Timeout <= 0 || request.Timeout > 4)
                return false;
            points = request.Points.ToArray();
            float total = 0f;
            Vector3 previous = origin;
            foreach (Vector3 point in points) {
                float length = (point - previous).XZ.Length();
                if (!IsFinite(point) || MathF.Abs(point.Y - origin.Y) > .05f
                    || !float.IsFinite(length) || length is < .1f or > 1.5f)
                    return false;
                total += length;
                previous = point;
            }
            return total <= 4f && (points[^1] - origin).XZ.Length() > request.FinalRange;
        }
    }
}
