using Engine;

namespace Game.Westworld {
    public sealed partial class AtomicActions {
        // Trusted local executor only. Observation/loaded/support proof belongs to the caller.
        // No world scan, search, random movements, teleport or extra movement owner.
        AtomicActionRun StartObservedRoute(MoveAlongObservedRoute request) {
            if (request.Points == null || request.Points.Count is < 1 or > 4
                || !float.IsFinite(request.FinalRange) || request.FinalRange is < .05f or > .4f
                || !float.IsFinite(request.Timeout) || request.Timeout <= 0 || request.Timeout > 4)
                return Reject(request, AtomicActionFailure.InvalidArgument);
            Vector3[] points = request.Points.ToArray(); // Never retain the caller's mutable list.
            var action = request with { Points = Array.AsReadOnly(points) };
            Vector3 origin = m_body.Position, previous = origin;
            float total = 0;
            foreach (Vector3 point in points) {
                float length = (point - previous).XZ.Length();
                if (!IsFinite(point) || MathF.Abs(point.Y-origin.Y) > .05f
                    || !float.IsFinite(length) || length < .1f || length > 1.5f)
                    return Reject(action, AtomicActionFailure.InvalidArgument);
                total += length; previous = point;
            }
            if (total > 4f || (points[^1]-origin).XZ.Length() <= request.FinalRange)
                return Reject(action, AtomicActionFailure.InvalidArgument);
            bool Grounded() => CanUseLocomotion && m_locomotion.WalkSpeed > 0
                && m_body.StandingOnValue.HasValue && m_body.ImmersionFactor <= .1f
                && !m_locomotion.LadderValue.HasValue && !m_locomotion.IsCreativeFlyEnabled
                && !ComponentLocomotion.IsCreativeFlyEnabledSet && MathF.Abs(m_body.Position.Y-origin.Y) <= .15f;
            if (!Grounded() || HasMovementDestination)
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            // Complete Player has no Pilot/Pathfinding. Reuse the native locomotion
            // orders of StartDirectMove, under this same AtomicActions run/owner.
            double deadline = m_time.GameTime + action.Timeout, lastProgress=m_time.GameTime;
            int index = 0; Vector3 segmentOrigin = origin, target = points[0];
            float closest=(target-origin).XZ.Length();
            Vector2? lastWalk=null, previousWalk=null;
            Vector2 lastTurn=Vector2.Zero, previousTurn=Vector2.Zero;
            double orderTime=double.NegativeInfinity;
            return StartRunning(action, () => {
                if (!Grounded() || HasMovementDestination)
                    return Failed(AtomicActionFailure.MovementInterrupted);
                if (m_time.GameTime >= deadline) return Failed(AtomicActionFailure.TimedOut);
                Vector2 route = (target-segmentOrigin).XZ, offset=(m_body.Position-segmentOrigin).XZ;
                float length=route.Length(), along=Vector2.Dot(offset,route)/length;
                float cross=MathF.Abs(offset.X*route.Y-offset.Y*route.X)/length;
                if(cross > .25f || along < -.2f || along > length+.3f)
                    return Failed(AtomicActionFailure.MovementInterrupted);
                float distance=(m_body.Position-target).XZ.Length();
                if(index==points.Length-1 && distance<=action.FinalRange) return Completed();
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
                return null;
            }, () => {
                // Locomotion consumes/resets orders each frame. Never clear a later
                // writer's different value; identical-value foreign writes cannot be
                // distinguished by this native API. External host lease is mandatory.
                if(m_time.GameTime==orderTime){
                    if(m_locomotion.WalkOrder==lastWalk)m_locomotion.WalkOrder=previousWalk;
                    if(m_locomotion.TurnOrder==lastTurn)m_locomotion.TurnOrder=previousTurn;
                }
            });
        }
    }
}
