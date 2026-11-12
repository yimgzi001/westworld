using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using GameEntitySystem;
using XmlUtilities;
using GameStorage = Engine.Storage;

namespace Game.Westworld {
    internal sealed record ActorTaskSnapshot(int Format, int ActorId, double GameTime, ActorPlanStep[] Plan, ActorTaskView View,
        AtomicActionContinuation Continuation = null);

    public sealed partial class ActorTools {
        const string CheckpointElement = "WestworldTask";
        const string CognitionElement = "WestworldCognition";
        sealed class LoadedTaskCheckpoint(string task, string cognition) {
            internal string Task { get; } = task;
            internal string Cognition { get; } = cognition;
            internal bool Restored { get; set; }
        }

        static readonly ConditionalWeakTable<Project, LoadedTaskCheckpoint> m_loadedCheckpoints = new();

        internal static void RegisterTaskCheckpoint(Project project, XElement projectNode) {
            string task = projectNode.Element(CheckpointElement)?.Value;
            string cognition = projectNode.Element(CognitionElement)?.Value;
            if (task != null || cognition != null) {
                m_loadedCheckpoints.Add(project, new LoadedTaskCheckpoint(task, cognition));
            }
        }

        // Host-only. Call between game updates; the published archive can be imported as a world.
        public void SaveTaskCheckpoint(string path) => SaveActorCheckpoint(path, true, null);

        internal void SaveCognitiveCheckpoint(string path, string cognition) => SaveActorCheckpoint(path, TaskActive, cognition);

        void SaveActorCheckpoint(string path, bool captureTask, string cognition) {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Project project = CurrentTaskProject();
            if (!m_tasks.CanCaptureCheckpoint) {
                throw new InvalidOperationException("A checkpoint requires an available actor and no running direct action or other active task.");
            }
            ActorTaskSnapshot snapshot = captureTask ? m_tasks.Capture() : null;
            if (snapshot != null && snapshot.Plan.Skip(snapshot.View.CurrentStep.Value).Any(step => !IsRestorable(step))) {
                throw new InvalidOperationException("The remaining steps depend on live session references.");
            }
            if (!string.Equals(Path.GetExtension(path), ".scworld", StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException("Use a .scworld checkpoint path.", nameof(path));
            }
            string task = snapshot == null ? null : JsonSerializer.Serialize(snapshot, m_json);
            WriteTaskArchive(project, GameStorage.GetSystemPath(path), task, cognition);
        }

        // Call on the rebound actor after loading a world imported from a task checkpoint.
        public bool RestoreTaskCheckpoint() => RestoreActorCheckpoint(false);

        internal string ReadCognitiveCheckpoint() => FindLoadedCheckpoint()?.Cognition;

        internal bool RestoreCognitiveCheckpoint() => RestoreActorCheckpoint(true);

        LoadedTaskCheckpoint FindLoadedCheckpoint() {
            Project project = CurrentTaskProject();
            return !TaskActive && m_tasks.CanRestoreCheckpoint && m_loadedCheckpoints.TryGetValue(project, out LoadedTaskCheckpoint checkpoint)
                && !checkpoint.Restored ? checkpoint : null;
        }

        bool RestoreActorCheckpoint(bool withCognition) {
            LoadedTaskCheckpoint checkpoint = FindLoadedCheckpoint();
            if (checkpoint == null || (checkpoint.Cognition != null) != withCognition) {
                return false;
            }
            if (checkpoint.Task == null) {
                checkpoint.Restored = true;
                return true;
            }
            ActorTaskSnapshot snapshot = JsonSerializer.Deserialize<ActorTaskSnapshot>(checkpoint.Task, m_json);
            if (snapshot == null || snapshot.Format != 1 || snapshot.ActorId != m_tasks.ActorId) {
                return false;
            }
            if (!IsValidSnapshot(snapshot)) {
                throw new InvalidDataException("Invalid task checkpoint.");
            }
            ActorTaskStep[] plan = CompilePlan(snapshot.Plan, snapshot.View.CurrentStep.Value, restoring: true);
            if (!m_tasks.Restore(plan, snapshot)) {
                return false;
            }
            checkpoint.Restored = true;
            return true;
        }

        Project CurrentTaskProject() {
            Project project = m_actions.BoundActor.Project;
            if (project != GameManager.Project || !m_actions.BoundActor.IsAddedToProject) {
                throw new InvalidOperationException("The actor must belong to the current loaded game.");
            }
            return project;
        }

        internal ActorTaskSnapshot CaptureWorldTask(bool nativeSave = false) {
            if (!nativeSave && m_actions.IsActionRunning) {
                throw new InvalidOperationException("A running atomic action cannot be checkpointed.");
            }
            if (!TaskActive) {
                return null;
            }
            ActorTaskSnapshot snapshot = nativeSave ? m_tasks.CaptureForNativeSave() : m_tasks.Capture(checkpointable: true);
            if (snapshot != null && snapshot.Plan.Skip(snapshot.View.CurrentStep.Value).Any(step => !IsRestorable(step))) {
                throw new InvalidOperationException("The task contains session-bound steps.");
            }
            return snapshot;
        }

        internal bool RestoreWorldTask(ActorTaskSnapshot snapshot, bool nativeSave) => snapshot == null ||
            ((snapshot.Format == 1 || nativeSave && snapshot.Format == 2)
                && snapshot.ActorId == m_tasks.ActorId && IsValidSnapshot(snapshot)
                && m_tasks.Restore(CompilePlan(snapshot.Plan, snapshot.View.CurrentStep.Value, restoring: true,
                    continuing: snapshot.Continuation != null), snapshot));

        static bool IsValidSnapshot(ActorTaskSnapshot snapshot) {
            ActorTaskView view = snapshot.View;
            ActorPlanStep[] plan = snapshot.Plan;
            if (view == null || plan == null || plan.Length is < 1 or > 12 || view.Steps == null
                || view.Steps.Count != plan.Length || view.TotalSteps != plan.Length || view.TaskId == Guid.Empty
                || view.Version < 1 || view.Failure != ActorTaskFailure.None
                || view.CurrentStep is not int cursor || cursor < 0 || cursor >= plan.Length
                || !double.IsFinite(snapshot.GameTime) || snapshot.GameTime < 0
                || view.LastProgressTime is double time && (!double.IsFinite(time) || time > snapshot.GameTime)) {
                return false;
            }
            if (snapshot.Format == 1 ? view.Status != ActorTaskStatus.Waiting || snapshot.Continuation != null
                : view.Status is not (ActorTaskStatus.Running or ActorTaskStatus.Blocked)
                    || view.Status == ActorTaskStatus.Running && snapshot.Continuation == null
                    || view.Status == ActorTaskStatus.Blocked && snapshot.Continuation is not (null or MotionContinuation)
                    || snapshot.Continuation != null && !snapshot.Continuation.Matches(plan[cursor]?.Tool)) {
                return false;
            }

            int completed = 0;
            int next = 0;
            for (int index = 0; index < plan.Length; index++) {
                ActorTaskStepView result = view.Steps[index];
                ActorPlanStep step = plan[index];
                if (step == null || result == null || result.Index != index || result.MinimumAffectedCount != step.MinimumAffected
                    || result.Attempts < 0 || result.Attempts > step.Retries + 1 || result.RetriesRemaining < 0
                    || result.RetriesRemaining > step.Retries || result.AffectedCount < 0) {
                    return false;
                }
                if ((step.InterruptOn == "body_worsened") != (result.GuardBaseline != null)
                    || result.GuardBaseline is { } baseline && (!float.IsFinite(baseline.Health + baseline.Air + baseline.Immersion + baseline.SwimSpeed)
                        || baseline.Health is < 0 or > 1 || baseline.Air is < 0 or > 1)
                    || result.InterruptedBy != null && (result.InterruptedBy != step.InterruptOn
                        || result.AtomicFailure != AtomicActionFailure.MovementInterrupted || result.Status != ActorTaskStatus.Failed)) return false;
                if (index == cursor && snapshot.Format == 2) {
                    if (result.Status != view.Status || result.Attempts == 0 || result.AffectedCount != 0 || result.ReceivedItems != null
                        || view.Status == ActorTaskStatus.Blocked && result.RetriesRemaining == 0) {
                        return false;
                    }
                    continue;
                }
                if (index >= cursor || index < next) {
                    ActorTaskStatus expected = index >= cursor ? ActorTaskStatus.Waiting : ActorTaskStatus.Skipped;
                    if (result.Status != expected || result.Attempts != 0 || result.AffectedCount != 0 || result.ReceivedItems != null) {
                        return false;
                    }
                    continue;
                }
                if (result.Attempts == 0 || !ValidReceipt(step, result)) {
                    return false;
                }
                if (result.Status == ActorTaskStatus.Succeeded && result.AtomicFailure == AtomicActionFailure.None
                    && result.AffectedCount >= step.MinimumAffected) {
                    completed++;
                    next = step.Next ?? index + 1;
                }
                else if (result.Status == ActorTaskStatus.Failed && result.AffectedCount == 0
                    && step.Fallback is { } branch && branch.Failure == result.AtomicFailure) {
                    next = branch.Step;
                }
                else {
                    return false;
                }
                if (next <= index || next > cursor) {
                    return false;
                }
            }
            return next == cursor && completed == view.CompletedSteps;
        }

        static bool ValidReceipt(ActorPlanStep step, ActorTaskStepView result) {
            if (result.ReceivedItems == null || result.ReceivedItems.Count == 0) {
                return true;
            }
            return step.Tool == "act_PickUp"
                && result.ReceivedItems.All(item => item != null && item.Slot >= 0 && item.Value > 0 && item.Count > 0)
                && result.ReceivedItems.Sum(item => (long)item.Count) == result.AffectedCount;
        }

        internal static void WriteTaskArchive(Project project, string destination, string task, string cognition,
            string observation = null, bool overwrite = true) {
            SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
            ProjectData projectData = TerrainSerializer23.SaveForCheckpoint(project.Save);
            if (terrain.Terrain.AllocatedChunks.Any(chunk => chunk.ModificationCounter > 0)) {
                throw new InvalidOperationException("Terrain changes are still pending a native save.");
            }
            XElement root = new("Project");
            ModsManager.HookAction("ProjectXmlSave", loader => { loader.ProjectXmlSave(root); return false; });
            projectData.Save(root);
            XmlUtils.SetAttributeValue(root, "Version", VersionsManager.SerializationVersion);
            XmlUtils.SetAttributeValue(root, "APIVersion", ModsManager.APIVersionString);
            ModsManager.HookAction("OnProjectXmlSaved", loader => { loader.OnProjectXmlSaved(root); return false; });
            if (task != null) {
                root.Add(new XElement(CheckpointElement, task));
            }
            if (cognition != null) {
                root.Add(new XElement(CognitionElement, cognition));
            }

            if (observation != null) {
                root.Add(new XElement(WorldCognitionScheduler.ObservationElement, observation));
            }

            string directory = project.FindSubsystem<SubsystemGameInfo>(true).DirectoryName;
            string regionDirectory = GameStorage.GetSystemPath(GameStorage.CombinePaths(directory, "Regions"));
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                int entries = 1;
                using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) {
                    using (ZipArchive archive = ZipArchive.Create(file, keepStreamOpen: true)) {
                        using MemoryStream xml = new();
                        root.Save(xml);
                        xml.Position = 0;
                        archive.AddStream("Project.xml", xml);
                        foreach (string path in Directory.EnumerateFiles(regionDirectory, "Region *.dat")) {
                            using FileStream region = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            archive.AddStream("Regions/" + Path.GetFileName(path), region);
                            entries++;
                        }
                    }
                    file.Flush(flushToDisk: true);
                    file.Position = 0;
                    using ZipArchive check = ZipArchive.Open(file, keepStreamOpen: true);
                    List<ZipArchiveEntry> contents = check.ReadCentralDir();
                    using MemoryStream verification = new();
                    if (contents.Count != entries || contents[0].FilenameInZip != "Project.xml"
                        || contents.Any(entry => (long)entry.FileOffset + entry.CompressedSize > file.Length)) {
                        throw new InvalidDataException("The checkpoint archive is incomplete.");
                    }
                    check.ExtractFile(contents[0], verification);
                    verification.Position = 0;
                    XElement verified = XmlUtils.LoadXmlFromStream(verification, null, true);
                    if (verified.Element(CheckpointElement)?.Value != task || verified.Element(CognitionElement)?.Value != cognition
                        || verified.Element(WorldCognitionScheduler.ObservationElement)?.Value != observation) {
                        throw new InvalidDataException("The checkpoint payload could not be verified.");
                    }
                }
                File.Move(temporary, destination, overwrite);
            }
            finally {
                if (File.Exists(temporary)) {
                    GameStorage.TryDeleteFile(temporary);
                }
            }
        }
    }
}
