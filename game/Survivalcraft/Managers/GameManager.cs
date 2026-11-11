using System.Xml.Linq;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;
using XmlUtilities;

namespace Game {
    public static class GameManager {
        public static WorldInfo m_worldInfo;

        public static Project m_project;

        public static SubsystemUpdate m_subsystemUpdate;

        public static ManualResetEvent m_saveCompleted = new(true);

        public static Project Project => m_project;

        public static WorldInfo WorldInfo => m_worldInfo;

        public static List<Func<bool>> SyncDispatcher = [];

        public static event Action<Project> ProjectDisposed;

        public const string fName = "GameManager";

        public static void RepairAndUpgradeWorld(WorldInfo worldInfo) {
            WorldsManager.RepairWorldIfNeeded(worldInfo.DirectoryName);
            VersionsManager.UpgradeWorld(worldInfo.DirectoryName);
        }

        public static void LoadProject(WorldInfo worldInfo, ContainerWidget gamesWidget) {
            DisposeProject();
            WorldsManager.RepairWorldIfNeeded(worldInfo.DirectoryName);
            VersionsManager.UpgradeWorld(worldInfo.DirectoryName);
            BlocksManager.LoadBlocksStaticly = string.IsNullOrEmpty(worldInfo.APIVersion);
            using (Stream stream = Storage.OpenFile(Storage.CombinePaths(worldInfo.DirectoryName, "Project.xml"), OpenFileMode.Read)) {
                ValuesDictionary valuesDictionary = new();
                ValuesDictionary valuesDictionary2 = new();
                valuesDictionary.SetValue("GameInfo", valuesDictionary2);
                valuesDictionary2.SetValue("WorldDirectoryName", worldInfo.DirectoryName);
                ValuesDictionary valuesDictionary3 = new();
                valuesDictionary.SetValue("Views", valuesDictionary3);
                valuesDictionary3.SetValue("GamesWidget", gamesWidget);
                XElement projectNode = XmlUtils.LoadXmlFromStream(stream, null, true);
                ModsManager.HookAction(
                    "ProjectXmlLoad",
                    loader => {
#pragma warning disable CS0618
                        loader.ProjectXmlLoad(projectNode);
#pragma warning restore CS0618
                        loader.ProjectXmlLoad(projectNode, worldInfo, gamesWidget);
                        return false;
                    }
                );
                Project.OnProjectLoad += delegate(Project project) {
                    PlayedTimeTracker.StartProject(project);
                    ModsManager.HookAction(
                        "OnProjectLoaded",
                        loader => {
                            loader.OnProjectLoaded(project);
                            return false;
                        }
                    );
                };
                Project.EntityAdded += (_, arg) => {
                    ModsManager.HookAction(
                        "OnEntityAdd",
                        loader => {
                            loader.OnEntityAdd(arg.Entity);
                            return false;
                        }
                    );
                };
                Project.EntityRemoved += (_, arg) => {
                    ModsManager.HookAction(
                        "OnEntityRemove",
                        loader => {
                            loader.OnEntityRemove(arg.Entity);
                            return false;
                        }
                    );
                };
                Entity.EntityComponentsInitialized += (entity, componentList) => {
                    ModsManager.HookAction(
                        "EntityComponentsInitialized",
                        loader => {
                            loader.EntityComponentsInitialized(entity, componentList);
                            return false;
                        }
                    );
                };
                Project.BeforeSubsystemsAndEntitiesLoad += project => {
                    ModsManager.HookAction(
                        "ProjectBeforeSubsystemsAndEntitiesLoad",
                        loader => {
                            loader.ProjectBeforeSubsystemsAndEntitiesLoad(project);
                            return false;
                        }
                    );
                };
                ProjectData projectData = new(DatabaseManager.GameDatabase, projectNode, valuesDictionary, true);
                m_project = new Project(DatabaseManager.GameDatabase, projectData);
                Westworld.ActorTools.RegisterTaskCheckpoint(m_project, projectNode);
                Westworld.WorldCognitionScheduler.RegisterLoadedObservation(m_project, projectNode);
                m_subsystemUpdate = m_project.FindSubsystem<SubsystemUpdate>(true);
                Westworld.FoundingWorld.Register(m_project, projectNode);
            }
            m_worldInfo = worldInfo;
#if WINDOWS
            Westworld.WorldPanel.OpenWorld(m_project);
#endif
            Log.Information(
                LanguageControl.Get(fName, "1"),
                LanguageControl.Get("GameMode", worldInfo.WorldSettings.GameMode.ToString()),
                LanguageControl.Get("StartingPositionMode", worldInfo.WorldSettings.StartingPositionMode.ToString()),
                worldInfo.WorldSettings.Name,
                SettingsManager.VisibilityRange.ToString(),
                LanguageControl.Get("ResolutionMode", SettingsManager.ResolutionMode.ToString())
            );
            GC.Collect();
        }

        public static void SaveProject(bool waitForCompletion, bool showErrorDialog) =>
            SaveProjectCore(waitForCompletion, showErrorDialog);

        internal static bool TrySaveQuotaPause() => SaveProjectCore(waitForCompletion: true, showErrorDialog: false);

        static bool SaveProjectCore(bool waitForCompletion, bool showErrorDialog) {
            if (m_project != null) {
                double realTime = Time.RealTime;
                m_saveCompleted.WaitOne();
                XElement foundingSave;
                ProjectData projectData;
                try {
                    foundingSave = Westworld.FoundingWorld.CaptureSave(m_project);
                    projectData = foundingSave == null ? m_project.Save() : TerrainSerializer23.SaveForCheckpoint(m_project.Save);
                }
                catch (Exception error) when (Westworld.FoundingWorld.HasFounders(m_project)) {
                    ReportSaveError(error, showErrorDialog);
                    return false;
                }
                m_saveCompleted.Reset();
                SubsystemGameInfo subsystemGameInfo = m_project.FindSubsystem<SubsystemGameInfo>(true);
                Task<bool> save = Task.Run(() => InternalSaveProject(projectData, subsystemGameInfo.DirectoryName, showErrorDialog, foundingSave));
                bool saved = !waitForCompletion || save.GetAwaiter().GetResult();
                Log.Verbose(string.Format(LanguageControl.Get(fName, "4"), Math.Round((Time.RealTime - realTime) * 1000.0)));
                return saved;
            }
            return false;
        }

        public static void InternalSaveProject(ProjectData projectData, string directoryName, bool showErrorDialog) =>
            InternalSaveProject(projectData, directoryName, showErrorDialog, null);

        static bool InternalSaveProject(ProjectData projectData, string directoryName, bool showErrorDialog, XElement foundingSave) {
            try {
                string projectFileName = Storage.CombinePaths(directoryName, "Project.xml");
                WorldsManager.MakeQuickWorldBackup(directoryName);
                XElement xElement = new("Project");
                ModsManager.HookAction(
                    "ProjectXmlSave",
                    loader => {
                        loader.ProjectXmlSave(xElement);
                        return false;
                    }
                );
                projectData.Save(xElement);
                XmlUtils.SetAttributeValue(xElement, "Version", VersionsManager.SerializationVersion);
                XmlUtils.SetAttributeValue(xElement, "APIVersion", ModsManager.APIVersionString);
                Storage.CreateDirectory(directoryName);
                PlayedTimeTracker.OnProjectXmlSaved();
                ModsManager.HookAction(
                    "OnProjectXmlSaved",
                    loader => {
                        loader.OnProjectXmlSaved(xElement);
                        return false;
                    }
                );
                if (foundingSave == null) {
                    using Stream stream = Storage.OpenFile(projectFileName, OpenFileMode.Create);
                    XmlUtils.SaveXmlToStream(xElement, stream, null, true);
                }
                else {
                    xElement.Add(foundingSave);
                    WriteFoundingProject(projectFileName, xElement);
                }
#if WINDOWS
                Westworld.WorldPanel.NativeSaveCompleted(directoryName);
#endif
                return true;
            }
            catch (Exception ex) {
                ReportSaveError(ex, showErrorDialog);
                return false;
            }
            finally {
                m_saveCompleted.Set();
            }
        }

        static void WriteFoundingProject(string projectFileName, XElement project) {
            string path = Storage.GetSystemPath(projectFileName);
            string temporary = path + ".tmp";
            try {
                using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    project.Save(stream);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally {
                Storage.TryDeleteFile(temporary);
            }
        }

        static void ReportSaveError(Exception error, bool showErrorDialog) {
            if (showErrorDialog) {
                Dispatcher.Dispatch(() => DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Get(fName, "2"),
                    $"{error.Message}\n{LanguageControl.Get(fName, "3")}", LanguageControl.Ok, null, null)));
            }
            Log.Error($"{LanguageControl.Get(fName, "2")}\n{error}");
        }

        public static void UpdateProject() {
            if (m_project != null && (!Westworld.FoundingWorld.PrepareFrame(m_project)
                || !Westworld.WorldCognitionScheduler.PumpWorld(m_project))) {
#if WINDOWS
                Westworld.WorldPanel.Capture(m_project, canAdvance: false);
#endif
                return;
            }
            if (SyncDispatcher.Count > 0
                && SyncDispatcher[0].Invoke()) {
                SyncDispatcher.RemoveAt(0);
            }
            if (m_project != null) {
                m_subsystemUpdate.Update();
#if WINDOWS
                Westworld.WorldPanel.Capture(m_project, canAdvance: true);
#endif
            }
        }

        public static void DisposeProject() {
            if (m_project != null) {
#if WINDOWS
                Westworld.WorldPanel.CloseWorld(m_project);
#endif
                Westworld.WorldCognitionScheduler.CloseWorld(m_project);
                ProjectDisposed?.Invoke(m_project);
                m_project.Dispose();
                m_project = null;
                m_subsystemUpdate = null;
                m_worldInfo = null;
                PlayedTimeTracker.StopProject();
                ModsManager.HookAction(
                    "OnProjectDisposed",
                    loader => {
                        loader.OnProjectDisposed();
                        return false;
                    }
                );
                GC.Collect();
            }
        }
    }
}
