using System.Runtime.CompilerServices;
using GameEntitySystem;

namespace Game.Westworld {
    // The game still owns the physical step. Task starts are arbitrated together after observing prior results.
    internal sealed class ActorTaskScheduler : IUpdateable {
        static readonly ConditionalWeakTable<Project, ActorTaskScheduler> m_schedulers = new();
        readonly SortedDictionary<int, ActorTaskExecutor> m_tasks = [];
        readonly List<ActorTaskExecutor> m_ready = [];
        readonly List<ActorTaskExecutor> m_frameTasks = [];
        readonly Project m_project;
        internal int LastFirstActor { get; set; }

        ActorTaskScheduler(Project project) {
            m_project = project;
            project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(this);
        }

        internal static ActorTaskScheduler For(Project project) => m_schedulers.GetValue(project, p => new ActorTaskScheduler(p));

        internal void Add(ActorTaskExecutor task) => m_tasks.Add(task.ActorId, task);

        internal void Remove(ActorTaskExecutor task) => m_tasks.Remove(task.ActorId);

        internal bool CanCheckpoint(ActorTaskExecutor owner) =>
            m_tasks.Values.All(task => !task.IsActionRunning && (task == owner || !task.IsActive));

        public float FloatUpdateOrder => 0.6f;

        public void Update(float dt) {
            if (!WorldCognitionScheduler.OwnsTaskPhase(m_project)) {
                RunPhase(dt);
            }
        }

        internal void RunPhase(float dt) {
            m_ready.Clear();
            m_frameTasks.AddRange(m_tasks.Values);
            foreach (ActorTaskExecutor task in m_frameTasks) {
                if (task.Observe(dt)) {
                    m_ready.Add(task);
                }
            }
            m_frameTasks.Clear();
            if (m_ready.Count == 0) {
                return;
            }

            int first = m_ready.FindIndex(task => task.ActorId > LastFirstActor);
            if (first < 0) {
                first = 0;
            }
            LastFirstActor = m_ready[first].ActorId;
            for (int offset = 0; offset < m_ready.Count; offset++) {
                m_ready[(first + offset) % m_ready.Count].StartReadyStep();
            }
            m_ready.Clear();
        }
    }
}
