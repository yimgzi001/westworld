using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed partial class AtomicActions {
        const float FacingTolerance = 0.035f;
        const float ClimbTolerance = 0.08f;

        ComponentLocomotion m_locomotion;
        ComponentRider m_rider;

        void RegisterMovement() {
            m_locomotion = m_actor.FindComponent<ComponentLocomotion>();
            m_rider = m_actor.FindComponent<ComponentRider>();
            Register<FaceTowards>(StartFacing, () => m_body != null && m_locomotion is { TurnSpeed: > 0f });
            Register<SetPosture>(StartPosture, () => m_body != null);
            Register<Jump>(StartJump, () => m_body != null && m_locomotion is { JumpSpeed: > 0f });
            Register<ClimbTo>(StartClimb, () => m_body != null && m_locomotion is { LadderSpeed: > 0f, WalkSpeed: > 0f });
            Register<Mount>(StartMount, () => m_body != null && m_rider != null && m_locomotion != null);
            Register<Dismount>(StartDismount, () => m_body != null && m_rider != null);
            Register<Steer>(StartSteer, () => m_rider != null);
        }

        bool HasMovementDestination => m_pathfinding?.Destination != null || m_pilot?.Destination != null;

        bool CanUseLocomotion => m_body.ParentBody == null && !m_body.IsEmbeddedInIce
            && m_locomotion.StunTime <= 0f && !m_locomotion.DisableInputMovement;

        AtomicActionRun StartFacing(FaceTowards action) {
            if (!IsFinite(action.Point) || !float.IsFinite(action.Timeout) || action.Timeout <= 0f) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (HasMovementDestination) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            if (!CanUseLocomotion) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }

            float AngleToPoint() {
                Vector2 direction = action.Point.XZ - m_body.Position.XZ;
                return direction.LengthSquared() < 0.000001f ? 0f : Vector2.Angle(m_body.Matrix.Forward.XZ, direction);
            }

            if (MathF.Abs(AngleToPoint()) <= FacingTolerance) {
                return Succeed(action);
            }
            double deadline = m_time.GameTime + action.Timeout;
            double orderTime = double.NegativeInfinity;
            Vector2 order = Vector2.Zero;
            Vector2 previousOrder = Vector2.Zero;
            return StartRunning(action,
                () => {
                    if (!CanUseLocomotion || HasMovementDestination) {
                        return Failed(AtomicActionFailure.MovementInterrupted);
                    }
                    float angle = AngleToPoint();
                    if (MathF.Abs(angle) <= FacingTolerance) {
                        return Completed();
                    }
                    if (m_time.GameTime >= deadline) {
                        return Failed(AtomicActionFailure.TimedOut);
                    }
                    float maxTurn = m_locomotion.TurnSpeed * m_time.GameTimeDelta;
                    if (maxTurn > 0f) {
                        order = new Vector2(Math.Clamp(angle / maxTurn, -1f, 1f), 0f);
                        orderTime = m_time.GameTime;
                        previousOrder = m_locomotion.TurnOrder;
                        m_locomotion.TurnOrder = order;
                    }
                    return null;
                },
                () => {
                    if (m_time.GameTime == orderTime && m_locomotion.TurnOrder == order) {
                        m_locomotion.TurnOrder = previousOrder;
                    }
                },
                () => new MotionContinuation(action.Point, Remaining(deadline)));
        }

        AtomicActionRun StartPosture(SetPosture action) {
            if (!Enum.IsDefined(action.Posture)) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            float desired = action.Posture == BodyPosture.Crouching ? 1f : 0f;
            if (desired > 0f && (!m_body.CanCrouch || !m_body.StandingOnValue.HasValue)) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (m_body.ParentBody != null || m_body.IsEmbeddedInIce) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            float previous = m_body.TargetCrouchFactor;
            m_body.TargetCrouchFactor = desired;
            if (m_body.CrouchFactor == desired) {
                return Succeed(action);
            }

            double deadline = m_time.GameTime + 2.0;
            return StartRunning(action,
                () => {
                    if (m_body.ParentBody != null || m_body.IsEmbeddedInIce) {
                        return Failed(AtomicActionFailure.MovementInterrupted);
                    }
                    if (m_body.TargetCrouchFactor != desired) {
                        return Failed(AtomicActionFailure.Blocked);
                    }
                    return m_body.CrouchFactor == desired ? Completed()
                        : m_time.GameTime >= deadline ? Failed(AtomicActionFailure.TimedOut) : null;
                },
                () => {
                    if (m_body.TargetCrouchFactor == desired && m_body.CrouchFactor != desired) {
                        m_body.TargetCrouchFactor = previous;
                    }
                });
        }

        AtomicActionRun StartJump(Jump action) {
            bool CanJump() => CanUseLocomotion && !m_locomotion.IsCreativeFlyEnabled && !ComponentLocomotion.IsCreativeFlyEnabledSet
                && (m_locomotion.LadderValue.HasValue || m_body.StandingOnValue.HasValue || m_body.ImmersionFactor > 0.5f);

            if (!CanJump()) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            double? orderTime = null;
            double deadline = m_time.GameTime + 1.0;
            Vector3 startPosition = m_body.Position;
            bool jumpedFromLadder = false;
            bool consumed = false;
            return StartRunning(action,
                () => {
                    if (orderTime.HasValue && m_time.GameTime > orderTime.Value) {
                        consumed |= m_locomotion.LastJumpOrder > 0f;
                    }
                    if (consumed && (jumpedFromLadder ? !m_locomotion.LadderValue.HasValue : m_body.Position.Y > startPosition.Y)) {
                        return Completed();
                    }
                    if (m_time.GameTime >= deadline) {
                        return Failed(AtomicActionFailure.Blocked);
                    }
                    if (!orderTime.HasValue && m_time.GameTimeDelta > 0f) {
                        if (!CanJump()) {
                            return Failed(AtomicActionFailure.MovementInterrupted);
                        }
                        if (m_locomotion.JumpOrder > 0f) {
                            return Failed(AtomicActionFailure.Busy);
                        }
                        startPosition = m_body.Position;
                        jumpedFromLadder = m_locomotion.LadderValue.HasValue;
                        orderTime = m_time.GameTime;
                        m_locomotion.JumpOrder = 1f;
                    }
                    return null;
                },
                () => {
                    if (orderTime == m_time.GameTime && m_locomotion.JumpOrder == 1f) {
                        m_locomotion.JumpOrder = 0f;
                    }
                });
        }

        AtomicActionRun StartClimb(ClimbTo action) {
            if (action.Ladder == null || !float.IsFinite(action.Height) || action.Height < 0f || action.Height >= TerrainChunk.Height - 0.2f
                || !float.IsFinite(action.Timeout) || action.Timeout <= 0f || action.Ladder.Face is < 0 or > 5) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (HasMovementDestination) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            if (!CanUseLocomotion || m_locomotion.IsCreativeFlyEnabled || ComponentLocomotion.IsCreativeFlyEnabledSet) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            Point3 ladder = action.Ladder.Cell;
            Terrain terrain = m_locomotion.m_subsystemTerrain.Terrain;
            int ladderValue = terrain.GetCellValue(ladder);
            if (Terrain.ReplaceLight(ladderValue, 0) != Terrain.ReplaceLight(action.Ladder.ExpectedValue, 0)) {
                return Reject(action, AtomicActionFailure.TargetChanged);
            }
            if (BlocksManager.Blocks[Terrain.ExtractContents(ladderValue)] is not LadderBlock) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            Vector3 center = new(ladder.X + 0.5f, ladder.Y + 0.5f, ladder.Z + 0.5f);
            if (!IsNear(center, 2f)) {
                return Reject(action, AtomicActionFailure.OutOfReach);
            }
            int face = LadderBlock.GetFace(Terrain.ExtractData(ladderValue));
            bool IsRung(int y) {
                int value = terrain.GetCellValue(ladder.X, y, ladder.Z);
                return BlocksManager.Blocks[Terrain.ExtractContents(value)] is LadderBlock && LadderBlock.GetFace(Terrain.ExtractData(value)) == face;
            }

            int startY = Terrain.ToCell(m_body.Position.Y + 0.2f);
            int goalY = Terrain.ToCell(action.Height + 0.2f);
            int bottom = Math.Min(ladder.Y, Math.Min(startY, goalY));
            int top = Math.Max(ladder.Y, Math.Max(startY, goalY));
            for (int y = bottom; y <= top; y++) {
                if (!IsRung(y)) {
                    return Reject(action, AtomicActionFailure.PreconditionsNotMet);
                }
            }

            Vector2 approach = center.XZ - 0.48f * CellFace.FaceToVector3(face).XZ;
            double deadline = m_time.GameTime + action.Timeout;
            double orderTime = double.NegativeInfinity;
            Vector2 order = Vector2.Zero;
            Vector2? previousOrder = null;
            bool engaged = false;
            return StartRunning(action,
                () => {
                    if (!CanUseLocomotion || HasMovementDestination || m_locomotion.IsCreativeFlyEnabled
                        || ComponentLocomotion.IsCreativeFlyEnabledSet) {
                        return Failed(AtomicActionFailure.MovementInterrupted);
                    }
                    if (Terrain.ReplaceLight(terrain.GetCellValue(ladder), 0) != Terrain.ReplaceLight(ladderValue, 0) || !IsRung(goalY)) {
                        return Failed(AtomicActionFailure.TargetChanged);
                    }
                    Vector3 position = m_body.Position;
                    int currentY = Terrain.ToCell(position.Y + 0.2f);
                    bool inColumn = Terrain.ToCell(position.X) == ladder.X && Terrain.ToCell(position.Z) == ladder.Z;
                    bool onLadder = inColumn && m_locomotion.LadderValue.HasValue && IsRung(currentY);
                    float error = action.Height - position.Y;
                    if ((onLadder || (engaged && inColumn)) && MathF.Abs(error) <= ClimbTolerance && MathF.Abs(m_body.Velocity.Y) < 0.06f) {
                        return Completed();
                    }
                    if (engaged && !onLadder) {
                        return Failed(IsRung(currentY) ? AtomicActionFailure.MovementInterrupted : AtomicActionFailure.TargetChanged);
                    }
                    if (m_time.GameTime >= deadline) {
                        return Failed(AtomicActionFailure.TimedOut);
                    }
                    if (onLadder) {
                        engaged = true;
                        order = new Vector2(0f, MathF.Abs(error) <= ClimbTolerance ? 0f : Math.Clamp(3f * error / m_locomotion.LadderSpeed, -1f, 1f));
                    }
                    else {
                        if (m_locomotion.LadderValue.HasValue || !IsRung(currentY)) {
                            return Failed(AtomicActionFailure.MovementInterrupted);
                        }
                        Vector2 offset = approach - position.XZ;
                        Vector2 direction = offset == Vector2.Zero ? -CellFace.FaceToVector3(face).XZ : Vector2.Normalize(offset);
                        order = 0.55f * new Vector2(Vector2.Dot(direction, m_body.Matrix.Right.XZ), Vector2.Dot(direction, m_body.Matrix.Forward.XZ));
                    }
                    orderTime = m_time.GameTime;
                    previousOrder = m_locomotion.WalkOrder;
                    m_locomotion.WalkOrder = order;
                    return null;
                },
                () => {
                    if (m_time.GameTime == orderTime && m_locomotion.WalkOrder == order) {
                        m_locomotion.WalkOrder = previousOrder;
                    }
                });
        }

        AtomicActionRun StartMount(Mount action) {
            Entity target = action.Target;
            if (target == null || target == m_actor) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (!IsLiveEntity(target)) {
                return Reject(action, AtomicActionFailure.TargetChanged);
            }
            ComponentMount mount = target.FindComponent<ComponentMount>();
            if (mount == null || target.FindComponent<ComponentSteedBehavior>() is { m_isEnabled: false }) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (m_rider.Mount == mount && !m_rider.m_isAnimating) {
                return Succeed(action);
            }
            if (m_rider.m_isAnimating || m_body.ParentBody != null || mount.Rider != null || HasMovementDestination) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            if (!CanUseLocomotion) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            Vector3 eye = m_rider.ComponentCreature.ComponentCreatureModel.EyePosition;
            Vector3 seat = mount.ComponentBody.Position + Vector3.Transform(mount.MountOffset, mount.ComponentBody.Rotation);
            float range = mount.MountAllowToStartRange;
            if (Vector3.DistanceSquared(eye, seat) >= range * range) {
                return Reject(action, AtomicActionFailure.OutOfReach);
            }
            if (m_rider.ScoreMount(mount, range) <= 0f) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (m_rider.m_subsystemTerrain.Raycast(eye, seat, false, true,
                    (value, _) => BlocksManager.Blocks[Terrain.ExtractContents(value)].GetIsCollidable(m_body, value)).HasValue) {
                return Reject(action, AtomicActionFailure.Blocked);
            }

            m_rider.StartMounting(mount);
            if (m_rider.Mount != mount) {
                return Reject(action, AtomicActionFailure.Blocked);
            }
            double deadline = m_time.GameTime + 2.0;
            return StartRunning(action, () => {
                if (!IsLiveEntity(target)) {
                    return Failed(AtomicActionFailure.TargetChanged);
                }
                if (m_rider.Mount != mount || m_rider.m_isDismounting) {
                    return Failed(AtomicActionFailure.MovementInterrupted);
                }
                return !m_rider.m_isAnimating ? Completed()
                    : m_time.GameTime >= deadline ? Failed(AtomicActionFailure.TimedOut) : null;
            });
        }

        AtomicActionRun StartDismount(Dismount action) {
            if (m_body.ParentBody == null) {
                return Succeed(action);
            }
            ComponentMount mount = m_rider.Mount;
            if (mount == null) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (m_rider.m_isAnimating) {
                return Reject(action, AtomicActionFailure.Busy);
            }

            m_rider.StartDismounting();
            if (m_body.ParentBody == null) {
                return Succeed(action);
            }
            if (!m_rider.m_isAnimating || !m_rider.m_isDismounting) {
                return Reject(action, AtomicActionFailure.Blocked);
            }
            double deadline = m_time.GameTime + 2.0;
            return StartRunning(action, () => {
                if (m_body.ParentBody == null) {
                    return Completed();
                }
                if (m_rider.Mount != mount || !m_rider.m_isDismounting) {
                    return Failed(AtomicActionFailure.MovementInterrupted);
                }
                return m_time.GameTime >= deadline ? Failed(AtomicActionFailure.TimedOut) : null;
            });
        }

        AtomicActionRun StartSteer(Steer action) {
            if (!float.IsFinite(action.Turn) || action.Turn is < -1f or > 1f || !float.IsFinite(action.Throttle) || action.Throttle is < -1f or > 1f
                || !float.IsFinite(action.Duration) || action.Duration <= 0f) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            ComponentMount mount = m_rider.Mount;
            if (mount == null || !IsLiveEntity(mount.Entity) || mount.Rider != m_rider || m_rider.m_isDismounting) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            ComponentBoat boat = mount.Entity.FindComponent<ComponentBoat>();
            ComponentSteedBehavior steed = mount.Entity.FindComponent<ComponentSteedBehavior>();
            if (boat == null && steed is not { m_isEnabled: true }) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (boat != null && !boat.CanBeSteered) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (boat?.AtomicSteeringOrder != null || steed?.AtomicSteeringOrder != null) {
                return Reject(action, AtomicActionFailure.Busy);
            }

            int targetGear = steed == null ? 0 : GetSteedGear(steed.m_speedLevels, action.Throttle);
            int? previousGear = null;
            bool needsMovement = boat != null ? action.Throttle != 0f : steed.m_speedLevels[targetGear] != 0f;
            double controlledTime = 0.0;
            double lastControlTime = m_time.GameTime;
            Vector2 previousPosition = mount.ComponentBody.Position.XZ;
            Vector2 previousForward = mount.ComponentBody.Matrix.Forward.XZ;
            float distance = 0f;
            float turn = 0f;

            bool StillRiding() => IsLiveEntity(mount.Entity) && m_rider.Mount == mount
                && mount.Rider == m_rider && !m_rider.m_isDismounting;

            void ApplyOrders(float dt) {
                if (dt <= 0f || controlledTime >= action.Duration || m_rider.m_isAnimating || !IsLiveEntity(m_actor) || !StillRiding()) {
                    return;
                }
                if (boat != null) {
                    boat.TurnOrder = action.Turn;
                    boat.MoveOrder = action.Throttle;
                }
                else {
                    if (!previousGear.HasValue) {
                        previousGear = steed.m_speedLevel;
                        steed.SpeedOrder = targetGear - previousGear.Value;
                    }
                    else {
                        steed.SpeedOrder = 0;
                    }
                    steed.TurnOrder = action.Turn;
                }
                controlledTime += dt;
                lastControlTime = m_time.GameTime;
            }

            Action<float> orders = ApplyOrders;
            if (boat != null) {
                boat.AtomicSteeringOrder = orders;
            }
            else {
                steed.AtomicSteeringOrder = orders;
            }

            return StartRunning(action,
                () => {
                    if (!StillRiding() || (boat != null ? boat.AtomicSteeringOrder : steed.AtomicSteeringOrder) != orders) {
                        return Failed(AtomicActionFailure.MovementInterrupted);
                    }
                    Vector2 position = mount.ComponentBody.Position.XZ;
                    Vector2 forward = mount.ComponentBody.Matrix.Forward.XZ;
                    if (controlledTime > 0.0) {
                        distance += Vector2.Distance(previousPosition, position);
                        turn += MathF.Abs(Vector2.Angle(previousForward, forward));
                    }
                    previousPosition = position;
                    previousForward = forward;
                    if (controlledTime >= action.Duration) {
                        return (!needsMovement || distance > 0f) && (action.Turn == 0f || turn > 0f)
                            ? Completed() : Failed(AtomicActionFailure.Blocked);
                    }
                    if (boat is { CanBeSteered: false } || m_time.GameTime >= lastControlTime + 3.0) {
                        return Failed(AtomicActionFailure.Blocked);
                    }
                    return null;
                },
                () => {
                    if (boat != null) {
                        if (boat.AtomicSteeringOrder == orders) {
                            boat.AtomicSteeringOrder = null;
                        }
                    }
                    else if (steed.AtomicSteeringOrder == orders) {
                        steed.AtomicSteeringOrder = null;
                        if (previousGear.HasValue && StillRiding() && steed.SpeedOrder == 0 && steed.m_speedLevel == targetGear) {
                            steed.SpeedOrder = previousGear.Value - targetGear;
                        }
                    }
                });
        }

        static int GetSteedGear(float[] speeds, float throttle) {
            int gear = 0;
            for (int i = 1; i < speeds.Length; i++) {
                if (MathF.Abs(speeds[i] - throttle) < MathF.Abs(speeds[gear] - throttle)) {
                    gear = i;
                }
            }
            return gear;
        }
    }
}
