using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed record ActorTaskStep(string Name, Func<AtomicAction> CreateAction, int Retries = 0, int MinimumAffectedCount = 0) {
        internal ActorPlanStep Plan { get; init; }
    }

    public enum ActorTaskStatus { Idle, Waiting, Running, Blocked, Succeeded, Failed, Cancelled, Skipped }

    public enum ActorTaskFailure { None, ActionFailed, ActionFactoryFailed, InsufficientEffect }

    public sealed record ActorTaskStepView(int Index, string Name, ActorTaskStatus Status, int Attempts,
        int RetriesRemaining, int MinimumAffectedCount, int AffectedCount, AtomicActionFailure AtomicFailure) {
        public IReadOnlyList<InventorySlotView> ReceivedItems { get; init; }
    }

    public sealed record ActorTaskView(int Version, ActorTaskStatus Status, int? CurrentStep, int CompletedSteps,
        int TotalSteps, double? LastProgressTime, ActorTaskFailure Failure, IReadOnlyList<ActorTaskStepView> Steps) {
        public Guid TaskId { get; init; }
    }

    // Submit and cancel on the game update thread. Only the current attempt owns an atomic action run.
    public sealed class ActorTaskExecutor : IUpdateable {
        public const int MaxSteps = 64;
        public const int MaxRetriesPerStep = 3;

        readonly Entity m_actor;
        readonly AtomicActions m_actions;
        readonly SubsystemTime m_time;
        readonly ActorTaskScheduler m_scheduler;

        ActorTaskStep[] m_steps = [];
        ActorTaskStepView[] m_stepViews = [];
        ActorTaskStatus m_status;
        ActorTaskFailure m_failure;
        AtomicActionRun m_run;
        MoveTo m_retryMove;
        int m_version;
        int m_completed;
        int m_current;
        Guid m_taskId;
        double? m_lastProgressTime;
        double m_lastStepTime;
        bool m_starting;
        bool m_cancelRequested;
        bool m_detached;

        public ActorTaskExecutor(Entity actor, AtomicActions actions) {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(actions);
            if (actor != actions.BoundActor) {
                throw new ArgumentException("The action port belongs to another actor.", nameof(actions));
            }
            m_actor = actor;
            m_actions = actions;
            m_time = actor.Project.FindSubsystem<SubsystemTime>(true);
            m_scheduler = ActorTaskScheduler.For(actor.Project);
            actions.BindTaskExecutor();
            if (actor.IsAddedToProject) {
                m_scheduler.Add(this);
            }
            else {
                actor.EntityAdded += OnActorAdded;
            }
            actor.EntityRemoved += OnActorRemoved;
        }

        public float FloatUpdateOrder => 0.6f;

        public ActorTaskView Status => new ActorTaskView(m_version, m_status, m_current < m_stepViews.Length ? m_current : null,
            m_completed, m_stepViews.Length, m_lastProgressTime, m_failure, Array.AsReadOnly(m_stepViews.ToArray())) { TaskId = m_taskId };

        internal bool IsActive => m_status is ActorTaskStatus.Waiting or ActorTaskStatus.Running or ActorTaskStatus.Blocked;
        internal int ActorId => m_actor.Id;
        internal ActorTaskView LastTerminal { get; private set; }
        internal bool IsActionRunning => m_actions.IsActionRunning;
        internal bool OwnsRunningAction => m_run?.Result.Status == AtomicActionStatus.Running;
        internal bool CanCaptureCheckpoint => m_actions.IsTaskActorAvailable && m_scheduler.CanCheckpoint(this);
        internal bool CanRestoreCheckpoint => m_time.GameTime == 0 && CanCaptureCheckpoint;
        internal IReadOnlyList<InventorySlotView> ReceivedItems(int step) => m_stepViews[step].ReceivedItems;

        public bool Submit(IReadOnlyList<ActorTaskStep> steps) {
            if (IsActive || m_detached || !m_actions.IsTaskActorAvailable || steps == null || steps.Count is < 1 or > MaxSteps) {
                return false;
            }
            ActorTaskStep[] plan = steps.ToArray();
            if (plan.Length is < 1 or > MaxSteps || plan.Any(step => step == null || string.IsNullOrWhiteSpace(step.Name) || step.CreateAction == null
                    || step.Retries is < 0 or > MaxRetriesPerStep || step.MinimumAffectedCount < 0)) {
                return false;
            }

            m_steps = plan;
            m_stepViews = plan.Select((step, index) => new ActorTaskStepView(index, step.Name, ActorTaskStatus.Waiting,
                0, step.Retries, step.MinimumAffectedCount, 0, AtomicActionFailure.None)).ToArray();
            m_status = ActorTaskStatus.Waiting;
            m_failure = ActorTaskFailure.None;
            m_version++;
            m_completed = 0;
            m_current = 0;
            m_taskId = Guid.NewGuid();
            m_lastProgressTime = null;
            m_lastStepTime = m_time.GameTime;
            return true;
        }

        internal ActorTaskSnapshot Capture(bool cohort = false) {
            if (m_status != ActorTaskStatus.Waiting || m_run != null || m_starting || m_actor.Id <= 0
                || (!cohort && !m_scheduler.CanCheckpoint(this)) || m_steps.Any(step => step.Plan == null)) {
                throw new InvalidOperationException("A checkpoint requires a data plan between actions in a single active task.");
            }
            return new ActorTaskSnapshot(1, m_actor.Id, m_time.GameTime, m_steps.Select(step => step.Plan).ToArray(), Status);
        }

        internal bool Restore(ActorTaskStep[] steps, ActorTaskSnapshot snapshot) {
            ActorTaskView view = snapshot.View;
            if (!Submit(steps)) {
                return false;
            }
            m_taskId = view.TaskId;
            m_version = view.Version;
            m_current = view.CurrentStep.Value;
            m_completed = view.CompletedSteps;
            m_lastProgressTime = view.LastProgressTime is double last ? m_time.GameTime - (snapshot.GameTime - last) : null;
            m_stepViews = view.Steps.Select(step => step with {
                ReceivedItems = step.ReceivedItems == null ? null : Array.AsReadOnly(step.ReceivedItems.ToArray())
            }).ToArray();
            return true;
        }

        public bool Cancel() {
            if (!IsActive) {
                return false;
            }
            if (m_starting) {
                m_cancelRequested = true;
                return true;
            }
            return Stop(ActorTaskStatus.Cancelled, AtomicActionFailure.None);
        }

        void IUpdateable.Update(float dt) {
            if (Observe(dt)) {
                StartReadyStep();
            }
        }

        internal bool Observe(float dt) {
            if (!IsActive) {
                return false;
            }
            if (m_run != null) {
                if (m_run.Result.Status != AtomicActionStatus.Running) {
                    m_lastStepTime = m_time.GameTime;
                    CompleteAttempt(m_run.Result, m_run.Action);
                }
                return false;
            }
            if (!m_actions.IsTaskActorAvailable) {
                Stop(ActorTaskStatus.Failed, AtomicActionFailure.ActorUnavailable);
                return false;
            }
            return dt > 0f && m_time.GameTime > m_lastStepTime;
        }

        internal void StartReadyStep() {
            if (IsActive && m_run == null && m_actions.IsTaskActorAvailable && m_time.GameTime > m_lastStepTime) {
                m_lastStepTime = m_time.GameTime;
                StartStep();
            }
        }

        void StartStep() {
            ActorTaskStepView step = m_stepViews[m_current];
            m_stepViews[m_current] = step with {
                Status = ActorTaskStatus.Running,
                Attempts = step.Attempts + 1,
                RetriesRemaining = step.Attempts == 0 ? step.RetriesRemaining : step.RetriesRemaining - 1,
                AtomicFailure = AtomicActionFailure.None
            };
            m_status = ActorTaskStatus.Running;
            ActorTaskFailure failure = ActorTaskFailure.ActionFactoryFailed;
            AtomicActionFailure atomicFailure = AtomicActionFailure.InvalidArgument;
            m_starting = true;
            try {
                AtomicAction action = m_retryMove ?? m_steps[m_current].CreateAction();
                if (action != null && !m_detached && !m_cancelRequested) {
                    failure = ActorTaskFailure.ActionFailed;
                    m_run = m_actions.Start(action);
                }
            }
            catch (ActorTaskStepUnavailable error) {
                failure = ActorTaskFailure.ActionFailed;
                atomicFailure = error.Failure;
            }
            catch (Exception error) {
                Log.Error($"Actor task step failed to start: {error}");
                atomicFailure = AtomicActionFailure.ExecutionError;
            }
            finally {
                m_starting = false;
            }

            if (m_detached || m_cancelRequested) {
                Stop(m_detached ? ActorTaskStatus.Failed : ActorTaskStatus.Cancelled,
                    m_detached ? AtomicActionFailure.ActorUnavailable : AtomicActionFailure.None);
            }
            else if (m_run == null) {
                CompleteAttempt(new AtomicActionResult(AtomicActionStatus.Failed, atomicFailure), failure: failure);
            }
            else if (m_run.Result.Status != AtomicActionStatus.Running) {
                CompleteAttempt(m_run.Result, m_run.Action, immediate: true);
            }
        }

        void CompleteAttempt(AtomicActionResult result, AtomicAction action = null, bool immediate = false,
            ActorTaskFailure failure = ActorTaskFailure.ActionFailed, bool allowRecovery = true) {
            m_run = null;
            ActorPlanStep plan = m_steps[m_current].Plan;
            ActorTaskStepView step = m_stepViews[m_current] with {
                AffectedCount = result.AffectedCount,
                AtomicFailure = result.Failure,
                ReceivedItems = result.ReceivedItems
            };
            if (result.AffectedCount > 0 || result.Status == AtomicActionStatus.Succeeded && result.AffectedCount >= step.MinimumAffectedCount) {
                m_lastProgressTime = m_time.GameTime;
            }

            if (result.Status == AtomicActionStatus.Succeeded && result.AffectedCount >= step.MinimumAffectedCount) {
                m_stepViews[m_current] = step with { Status = ActorTaskStatus.Succeeded };
                m_completed++;
                AdvanceTo(plan?.Next ?? m_current + 1);
            }
            // Movement can retry towards the same destination. Resource actions only retry an immediate Busy rejection.
            else if (allowRecovery && step.RetriesRemaining > 0 && result.Status == AtomicActionStatus.Failed
                    && result.AffectedCount == 0 && (immediate && result.Failure == AtomicActionFailure.Busy
                        || plan != null && action is MoveTo && result.Failure is AtomicActionFailure.Blocked or AtomicActionFailure.TimedOut)) {
                m_retryMove = action as MoveTo;
                m_stepViews[m_current] = step with { Status = ActorTaskStatus.Blocked };
                m_status = ActorTaskStatus.Blocked;
            }
            else if (allowRecovery && result.Status == AtomicActionStatus.Failed && result.AffectedCount == 0
                    && plan?.Fallback is { } fallback && fallback.Failure == result.Failure) {
                m_stepViews[m_current] = step with { Status = ActorTaskStatus.Failed };
                AdvanceTo(fallback.Step);
            }
            else {
                ActorTaskStatus status = result.Status == AtomicActionStatus.Cancelled ? ActorTaskStatus.Cancelled : ActorTaskStatus.Failed;
                m_stepViews[m_current] = step with { Status = status };
                Finish(status, result.Status switch {
                    AtomicActionStatus.Succeeded => ActorTaskFailure.InsufficientEffect,
                    AtomicActionStatus.Failed => failure,
                    _ => ActorTaskFailure.None
                });
            }
        }

        void AdvanceTo(int next) {
            m_retryMove = null;
            for (int index = m_current + 1; index < next; index++) {
                m_stepViews[index] = m_stepViews[index] with { Status = ActorTaskStatus.Skipped };
            }
            m_current = next;
            if (m_current == m_steps.Length) {
                Finish(ActorTaskStatus.Succeeded);
            }
            else {
                m_status = ActorTaskStatus.Waiting;
            }
        }

        bool Stop(ActorTaskStatus status, AtomicActionFailure failure) {
            AtomicActionRun run = m_run;
            if (run != null && run.Result.Status != AtomicActionStatus.Running) {
                CompleteAttempt(run.Result, run.Action, allowRecovery: false);
            }
            if (!IsActive) {
                return false;
            }
            m_stepViews[m_current] = m_stepViews[m_current] with { Status = status, AtomicFailure = failure };
            Finish(status, status == ActorTaskStatus.Failed ? ActorTaskFailure.ActionFailed : ActorTaskFailure.None);
            if (run?.Result.Status == AtomicActionStatus.Running) {
                m_actions.Cancel(run);
            }
            return true;
        }

        void Finish(ActorTaskStatus status, ActorTaskFailure failure = ActorTaskFailure.None) {
            m_status = status;
            m_failure = failure;
            m_run = null;
            m_steps = [];
            m_retryMove = null;
            m_cancelRequested = false;
            LastTerminal = Status;
        }

        void OnActorAdded(object sender, EventArgs args) {
            m_actor.EntityAdded -= OnActorAdded;
            m_scheduler.Add(this);
        }

        void OnActorRemoved(object sender, EventArgs args) {
            m_actor.EntityAdded -= OnActorAdded;
            m_actor.EntityRemoved -= OnActorRemoved;
            m_detached = true;
            m_scheduler.Remove(this);
            if (IsActive && !m_starting) {
                Stop(ActorTaskStatus.Failed, AtomicActionFailure.ActorUnavailable);
            }
        }
    }

    public sealed partial class AtomicActions {
        bool m_taskExecutorBound;

        internal bool IsTaskActorAvailable => IsActorAvailable;

        internal void BindTaskExecutor() {
            if (m_taskExecutorBound) {
                throw new InvalidOperationException("This action port already has a task executor.");
            }
            m_taskExecutorBound = true;
        }
    }
}
