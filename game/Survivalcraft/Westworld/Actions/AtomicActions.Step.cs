using Engine;
namespace Game.Westworld {
    public partial class AtomicActions {
        // Deliberately separate from MoveTo: ordinary movement must not silently
        // jump, cross unseen ledges, or reinterpret a failed flat route.
        AtomicActionRun StartStep(StepTo step) {
            var origin=m_body.Position;var delta=step.Destination-origin;
            if(!IsFinite(step.Destination)||!float.IsFinite(step.Timeout)||step.Timeout<=0||step.Timeout>3
                ||MathF.Abs(MathF.Abs(delta.Y)-1)>.05f||delta.XZ.Length()<.5f||delta.XZ.Length()>1.6f
                ||(MathF.Abs(delta.X)>.15f&&MathF.Abs(delta.Z)>.15f))return Reject(step,AtomicActionFailure.InvalidArgument);
            if(!CanUseLocomotion||!m_body.StandingOnValue.HasValue||m_body.ImmersionFactor>.1f
                ||m_body.ParentBody!=null||m_locomotion.LadderValue.HasValue||m_locomotion.IsCreativeFlyEnabled
                ||ComponentLocomotion.IsCreativeFlyEnabledSet||m_pathfinding?.Destination!=null||m_pilot?.Destination!=null)
                return Reject(step,AtomicActionFailure.PreconditionsNotMet);
            bool up=delta.Y>0,issued=false,airborne=false;double start=m_time.GameTime;
            Vector2 direction=Vector2.Normalize(delta.XZ);float length=delta.XZ.Length();
            Vector2? lastWalk=null;Vector2 lastTurn=Vector2.Zero;double orderTime=-1;
            Vector2 previousLook=Vector2.Zero;Vector2? lastLook=null;
            return StartRunning(step,()=>{
                if(!CanUseLocomotion||m_body.ParentBody!=null||m_body.ImmersionFactor>.1f||m_locomotion.LadderValue.HasValue)
                    return Failed(AtomicActionFailure.MovementInterrupted);
                var now=m_body.Position;var offset=now-origin;
                float along=Vector2.Dot(offset.XZ,direction),cross=MathF.Abs(offset.X*direction.Y-offset.Z*direction.X);
                if(cross>.25f||along<-.2f||along>length+.3f||now.Y<MathF.Min(origin.Y,step.Destination.Y)-.15f||now.Y>origin.Y+2)
                    return Failed(AtomicActionFailure.MovementInterrupted);
                airborne|=!m_body.StandingOnValue.HasValue;
                if(Vector3.Distance(now,step.Destination)<.22f&&m_body.StandingOnValue.HasValue&&MathF.Abs(m_body.Velocity.Y)<.3f)
                    return Completed();
                if(m_time.GameTime-start>=step.Timeout)return Failed(AtomicActionFailure.TimedOut);
                if(up&&!issued){m_locomotion.JumpOrder=1;issued=true;}
                if(up&&issued&&!airborne&&m_time.GameTime-start>.5)return Failed(AtomicActionFailure.Blocked);
                // Up: native collision holds the body against the riser until lifted.
                // Delaying all walk until the apex loses the short clearance window. Down: bounded walk,
                // native gravity/collision perform the drop; no teleport or velocity injection.
                float speed=Math.Clamp((step.Destination-now).XZ.Length(),0,.6f);
                var forward=m_body.Matrix.Forward.XZ;var right=m_body.Matrix.Right.XZ;
                lastWalk=speed*new Vector2(Vector2.Dot(right,direction),Vector2.Dot(forward,direction));
                lastTurn=new Vector2(Math.Clamp(Vector2.Angle(forward,direction),-1,1),0);orderTime=m_time.GameTime;
                m_locomotion.WalkOrder=lastWalk;m_locomotion.TurnOrder=lastTurn;
                float maxLook=m_locomotion.LookSpeed*m_time.GameTimeDelta;
                if(step.LevelGaze&&maxLook>0f) {
                    previousLook=m_locomotion.LookOrder;
                    var angles=m_locomotion.LookAngles;
                    lastLook=new Vector2(Math.Clamp(-angles.X/maxLook,-1f,1f),
                        Math.Clamp((MathUtils.DegToRad(-8f)-angles.Y)/maxLook,-1f,1f));
                    m_locomotion.LookOrder=lastLook.Value;
                }
                return null;
            },()=>{
                if(m_time.GameTime==orderTime){if(m_locomotion.WalkOrder==lastWalk)m_locomotion.WalkOrder=null;
                    if(m_locomotion.TurnOrder==lastTurn)m_locomotion.TurnOrder=Vector2.Zero;}
                if(issued&&m_locomotion.JumpOrder==1)m_locomotion.JumpOrder=0;
                if(m_time.GameTime==orderTime&&m_locomotion.LookOrder==lastLook)m_locomotion.LookOrder=previousLook;
            });
        }
    }
}
