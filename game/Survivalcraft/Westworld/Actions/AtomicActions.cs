using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    // Bind one port per controlled entity and submit commands on the game update thread.
    public sealed partial class AtomicActions : IUpdateable {
        sealed record Handler(Func<AtomicAction, AtomicActionRun> Start, Func<bool> Available);

        readonly Entity m_actor;
        readonly ComponentBody m_body;
        readonly ComponentHealth m_health;
        readonly ComponentPathfinding m_pathfinding;
        readonly ComponentPilot m_pilot;
        readonly ComponentMiner m_miner;
        readonly ComponentSleep m_sleep;
        readonly SubsystemTime m_time;
        readonly SubsystemUpdate m_update;
        readonly Dictionary<Type, Handler> m_handlers = [];

        AtomicActionRun m_active;
        Func<AtomicActionResult?> m_advance;
        Action m_cleanup;
        double m_lastAdvanceTime = double.NegativeInfinity;
        bool m_detached;

        public ActorQueries Queries { get; }

        internal Entity BoundActor => m_actor;
        internal bool IsActionRunning => m_active != null;
#if WINDOWS
        internal AtomicAction PanelAction => m_active?.Action;
#endif

        public AtomicActions(Entity actor) {
            ArgumentNullException.ThrowIfNull(actor);
            m_actor = actor;
            m_body = actor.FindComponent<ComponentBody>();
            m_health = actor.FindComponent<ComponentHealth>();
            m_pathfinding = actor.FindComponent<ComponentPathfinding>();
            m_pilot = actor.FindComponent<ComponentPilot>();
            m_miner = actor.FindComponent<ComponentMiner>();
            m_sleep = actor.FindComponent<ComponentSleep>();
            m_time = actor.Project.FindSubsystem<SubsystemTime>(true);
            m_update = actor.Project.FindSubsystem<SubsystemUpdate>(true);

            Register<MoveTo>(StartMove, () => m_body != null && (m_pathfinding != null && m_pilot != null
                || m_locomotion is { WalkSpeed: > 0f } or { SwimSpeed: > 0f }));
            Register<Wait>(StartWait);
            Register<SelectItem>(StartSelectItem, () => m_miner?.Inventory is { VisibleSlotsCount: > 0, SlotsCount: > 0 });
            RegisterMovement();
            RegisterInventory();
            RegisterWorld();
            RegisterFurniture();
            RegisterSocial();
            Queries = new ActorQueries(this);

            if (actor.IsAddedToProject) {
                m_update.AddUpdateable(this);
            }
            else {
                actor.EntityAdded += OnActorAdded;
            }
            actor.EntityRemoved += OnActorRemoved;
        }

        // Consume action orders after player/creature AI and before locomotion.
        public float FloatUpdateOrder => 0.5f;

        void IUpdateable.Update(float dt) => Advance();

        bool IsActorAvailable => !m_detached && m_actor.IsAddedToProject && m_health is { Health: > 0f };

        void OnActorAdded(object sender, EventArgs args) {
            m_actor.EntityAdded -= OnActorAdded;
            m_update.AddUpdateable(this);
        }

        void OnActorRemoved(object sender, EventArgs args) {
            m_actor.EntityRemoved -= OnActorRemoved;
            m_detached = true;
            m_update.RemoveUpdateable(this);
            if (m_active != null) {
                Finish(Failed(AtomicActionFailure.ActorUnavailable));
            }
            RemoveSocialParticipant();
        }

        void Register<T>(Func<T, AtomicActionRun> start, Func<bool> available = null) where T : AtomicAction =>
            m_handlers.Add(typeof(T), new Handler(action => start((T)action), available));

        public IReadOnlyList<AtomicActionDefinition> GetCapabilities() => IsActorAvailable
            ? AtomicActionCatalog.Definitions.Where(definition => IsSupported(definition.RequestType)).ToArray()
            : [];

        bool IsSupported(Type type) =>
            m_handlers.TryGetValue(type, out Handler handler) && (handler.Available?.Invoke() ?? true);

        public AtomicActionRun Start(AtomicAction action) {
            AtomicActionRun run = StartCore(action);
#if WINDOWS
            WorldPanel.RecordAction(m_actor, run);
#endif
            return run;
        }

        AtomicActionRun StartCore(AtomicAction action) {
            ArgumentNullException.ThrowIfNull(action);
            if (!IsSupported(action.GetType())) {
                return Reject(action, AtomicActionFailure.UnsupportedAction);
            }
            if (!IsActorAvailable) {
                return Reject(action, AtomicActionFailure.ActorUnavailable);
            }
            if (m_active != null) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            if (CannotActWhileSleeping(action)) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            try {
                return m_handlers[action.GetType()].Start(action);
            }
            catch (Exception error) {
                Log.Error($"Atomic action {action.GetType().Name}: {error}");
                return Reject(action, AtomicActionFailure.ExecutionError);
            }
        }

        bool CannotActWhileSleeping(AtomicAction action) => m_sleep is { IsSleeping: true } && action is not (WakeUp or Wait or Sleep);

        AtomicActionRun StartMove(MoveTo move) {
            if (!IsFinite(move.Destination) || move.Range <= 0f || !float.IsFinite(move.Range * move.Range)
                || move.Timeout <= 0f || !float.IsFinite(move.Timeout)) {
                return Reject(move, AtomicActionFailure.InvalidArgument);
            }
            if (m_body.ParentBody != null || (m_locomotion != null && !CanUseLocomotion)) {
                return Reject(move, AtomicActionFailure.PreconditionsNotMet);
            }
            if (m_pathfinding?.Destination != null || m_pilot?.Destination != null) {
                return Reject(move, AtomicActionFailure.Busy);
            }
            if (IsNear(move.Destination, move.Range)) {
                return Succeed(move);
            }

            // Follow a known destination using local movement, without searching new terrain.
            double deadline = m_time.GameTime + move.Timeout;
            if (m_pathfinding == null || m_pilot == null) {
                return StartDirectMove(move, deadline);
            }
            m_pathfinding.SetDestination(move.Destination, 0.6f, move.Range, 0, false, false, false, null);
            return StartRunning(
                move,
                () => CheckMove(move, deadline),
                () => {
                    if (m_actor.IsAddedToProject && m_pathfinding.Destination == move.Destination) {
                        m_pathfinding.Stop();
                    }
                }
            );
        }

        AtomicActionRun StartDirectMove(MoveTo move, double deadline) {
            float closest = Vector3.Distance(m_body.Position, move.Destination);
            double lastProgress = m_time.GameTime;
            return StartRunning(move, () => {
                if (!CanUseLocomotion || m_locomotion.LadderValue.HasValue) {
                    return Failed(AtomicActionFailure.MovementInterrupted);
                }
                Vector3 offset = move.Destination - m_body.Position;
                float distance = offset.Length();
                if (distance <= move.Range) {
                    return Completed();
                }
                if (m_time.GameTime >= deadline) {
                    return Failed(AtomicActionFailure.TimedOut);
                }
                if (distance + 0.08f < closest) {
                    closest = distance;
                    lastProgress = m_time.GameTime;
                }
                if (m_time.GameTime - lastProgress >= 2.5) {
                    return Failed(AtomicActionFailure.Blocked);
                }

                if (m_locomotion.SwimSpeed > 0f && m_body.ImmersionFactor > 0.5f) {
                    m_locomotion.SwimOrder = 0.6f * offset / distance;
                }
                else if (offset.XZ.LengthSquared() > 0.01f) {
                    Vector2 direction = Vector2.Normalize(offset.XZ);
                    Vector2 forward = m_body.Matrix.Forward.XZ;
                    Vector2 right = m_body.Matrix.Right.XZ;
                    float speed = Math.Clamp(offset.XZ.Length(), 0.2f, 0.6f);
                    m_locomotion.WalkOrder = speed * new Vector2(Vector2.Dot(right, direction), Vector2.Dot(forward, direction));
                    m_locomotion.TurnOrder = new Vector2(Math.Clamp(Vector2.Angle(forward, direction), -1f, 1f), 0f);
                }
                return null;
            });
        }

        AtomicActionResult? CheckMove(MoveTo move, double deadline) {
            if (m_body.ParentBody != null || (m_locomotion != null && !CanUseLocomotion)) {
                return Failed(AtomicActionFailure.MovementInterrupted);
            }
            if (m_pathfinding.Destination is Vector3 destination && destination != move.Destination) {
                return Failed(AtomicActionFailure.MovementInterrupted);
            }
            if (IsNear(move.Destination, move.Range)) {
                return Completed();
            }
            if (!m_pathfinding.Destination.HasValue || m_pathfinding.IsStuck) {
                return Failed(AtomicActionFailure.Blocked);
            }
            return m_time.GameTime >= deadline ? Failed(AtomicActionFailure.TimedOut) : null;
        }

        AtomicActionRun StartWait(Wait wait) {
            if (wait.Duration <= 0f || !float.IsFinite(wait.Duration)) {
                return Reject(wait, AtomicActionFailure.InvalidArgument);
            }
            double deadline = m_time.GameTime + wait.Duration;
            return StartRunning(wait, () => m_time.GameTime >= deadline ? Completed() : null);
        }

        AtomicActionRun StartSelectItem(SelectItem select) {
            IInventory inventory = m_miner.Inventory;
            if (select.Slot < 0 || select.Slot >= inventory.VisibleSlotsCount || select.Slot >= inventory.SlotsCount) {
                return Reject(select, AtomicActionFailure.InvalidArgument);
            }
            if (inventory.GetSlotValue(select.Slot) != select.ExpectedValue) {
                return Reject(select, AtomicActionFailure.TargetChanged);
            }
            inventory.ActiveSlotIndex = select.Slot;
            return inventory.ActiveSlotIndex == select.Slot ? Succeed(select) : Reject(select, AtomicActionFailure.Blocked);
        }

        AtomicActionRun StartRunning(AtomicAction action, Func<AtomicActionResult?> advance, Action cleanup = null) {
            m_advance = advance;
            m_cleanup = cleanup;
            m_active = new AtomicActionRun(action, new AtomicActionResult(AtomicActionStatus.Running));
            return m_active;
        }

        public void Advance() {
            if (m_active == null) {
                return;
            }
            if (!IsActorAvailable) {
                Finish(Failed(AtomicActionFailure.ActorUnavailable));
                return;
            }
            if (CannotActWhileSleeping(m_active.Action)) {
                Finish(Failed(AtomicActionFailure.PreconditionsNotMet));
                return;
            }
            if (m_time.GameTime <= m_lastAdvanceTime) {
                return;
            }
            m_lastAdvanceTime = m_time.GameTime;
            try {
                if (m_advance() is AtomicActionResult result && m_active != null) {
                    Finish(result);
                }
            }
            catch (Exception error) {
                Log.Error($"Atomic action update: {error}");
                if (m_active != null) {
                    Finish(Failed(AtomicActionFailure.ExecutionError));
                }
            }
        }

        public bool Cancel(AtomicActionRun run) {
            if (m_active == null || run != m_active) {
                return false;
            }
            Finish(new AtomicActionResult(AtomicActionStatus.Cancelled));
            return true;
        }

        void Finish(AtomicActionResult result) {
            Action cleanup = m_cleanup;
            AtomicActionRun run = m_active;
            run.Result = result;
            m_active = null;
            m_advance = null;
            m_cleanup = null;
            try {
                cleanup?.Invoke();
            }
            catch (Exception error) {
                Log.Error($"Atomic action cleanup: {error}");
            }
#if WINDOWS
            WorldPanel.RecordAction(m_actor, run);
#endif
        }

        bool IsLiveEntity(Entity entity) => entity != null && entity.Project == m_actor.Project
            && entity.IsAddedToProject && entity.FindComponent<ComponentHealth>() is not { Health: <= 0f };

        bool IsNear(Vector3 point, float range) => m_body != null && Vector3.DistanceSquared(m_body.Position, point) <= range * range;

        static bool IsFinite(Vector3 vector) => float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

        static AtomicActionResult Completed(int affectedCount = 0) => new(AtomicActionStatus.Succeeded, AffectedCount: affectedCount);

        static AtomicActionResult Failed(AtomicActionFailure failure) => new(AtomicActionStatus.Failed, failure);

        static AtomicActionRun Reject(AtomicAction action, AtomicActionFailure failure) => new(action, Failed(failure));

        static AtomicActionRun Succeed(AtomicAction action, int affectedCount = 0, Guid? offerId = null) =>
            new(action, new AtomicActionResult(AtomicActionStatus.Succeeded, AffectedCount: affectedCount, OfferId: offerId));
    }
}
