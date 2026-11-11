using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed class FoundingWorld {
        const int Version = 1;
        const int SoloVersion = 2;
        const int FounderCount = 50;
        const int CampLocation = int.MinValue;
        const int CampRadius = 80;
        const int ActorRadius = 40;
        const string MaleTemplate = "WestworldFounderMale";
        const string FemaleTemplate = "WestworldFounderFemale";
        internal const string ProducerTemplate = "WestworldProducer";
        const string SaveElement = "WestworldFoundingSave";

        sealed record Founder(int Id, string Profile);
        sealed record InitialState(float CampX, float CampZ, double Time, Founder[] Founders);

        static readonly ConditionalWeakTable<Project, FoundingWorld> m_worlds = new();
        static readonly string[] Names = ["杉", "禾", "苇", "桐", "松", "柳", "竹", "石", "泉", "岚"];
        static readonly string[] Preferences = ["你看重储备和稳妥。", "你喜欢探索陌生的地方。", "你乐于与熟悉的人共同做事。",
            "你习惯自己先动手试一试。", "你喜欢记住路线和可用材料。"];

        readonly Project m_project;
        readonly SubsystemGameInfo m_info;
        readonly SubsystemPlayers m_players;
        readonly SubsystemTerrain m_terrain;
        readonly SubsystemTime m_time;
        readonly bool m_solo;
        readonly HashSet<int> m_actorLocations = [];
        Vector2? m_camp;
        InitialState m_state;
        Entity[] m_founders = [];
        bool m_startFromInitial;
        bool m_startFromSave;
        bool m_failed;
        string m_savedObservation;

        FoundingWorld(Project project, XElement projectNode) {
            m_project = project;
            m_info = project.FindSubsystem<SubsystemGameInfo>(true);
            m_players = project.FindSubsystem<SubsystemPlayers>(true);
            m_terrain = project.FindSubsystem<SubsystemTerrain>(true);
            m_time = project.FindSubsystem<SubsystemTime>(true);
            WorldSettings settings = m_info.WorldSettings;
            if (settings.WestworldFoundingVersion is not (Version or SoloVersion)) {
                throw new InvalidDataException("Unsupported Westworld founding version.");
            }
            m_solo = settings.WestworldFoundingVersion == SoloVersion;
            int population = m_solo ? 1 : FounderCount;
            if (settings.WestworldInitialArchive || string.IsNullOrEmpty(settings.WestworldRunId)) {
                settings.WestworldRunId = Guid.NewGuid().ToString("N");
            }
            if (settings.WestworldFoundingState.Length != 0) {
                m_state = JsonSerializer.Deserialize<InitialState>(settings.WestworldFoundingState);
                if (m_state?.Founders?.Length != population || m_state.Founders.Select(member => member.Id).Distinct().Count() != population
                    || !float.IsFinite(m_state.CampX) || !float.IsFinite(m_state.CampZ) || !double.IsFinite(m_state.Time) || m_state.Time < 0) {
                    throw new InvalidDataException("Invalid Westworld founding state.");
                }
                m_camp = new Vector2(m_state.CampX, m_state.CampZ);
                m_founders = m_state.Founders.Select(member => project.FindEntity(member.Id)).ToArray();
                m_startFromInitial = settings.WestworldInitialArchive;
                if (m_startFromInitial && m_founders.Any(entity => entity == null || !IsFounder(entity)
                    || (entity.ValuesDictionary.DatabaseObject.Name == ProducerTemplate) != m_solo)) {
                    throw new InvalidDataException("The initial archive is missing founder entities.");
                }
                if (m_startFromInitial) {
                    m_time.m_gameTime = m_state.Time;
                    settings.WestworldInitialArchive = false;
                }
                else if (projectNode.Element(WorldCognitionScheduler.ObservationElement) == null) {
                    XElement save = projectNode.Element(SaveElement);
                    double time = save == null ? m_info.TotalElapsedGameTime : (double)save.Attribute("Time");
                    if (!double.IsFinite(time) || time < 0) {
                        throw new InvalidDataException("Invalid Westworld save time.");
                    }
                    m_time.m_gameTime = time;
                    m_savedObservation = save?.Element(WorldCognitionScheduler.ObservationElement)?.Value;
                    m_startFromSave = m_savedObservation != null || HasModelConfiguration();
                }
            }
            else if (settings.WestworldPlayerIndex == 0 && m_players.PlayersData.Count == 0) {
                PlayerData player = new(project) { InputDevice = WidgetInputDevice.Keyboard | WidgetInputDevice.Mouse };
                m_players.AddPlayerData(player);
                settings.WestworldPlayerIndex = player.PlayerIndex;
            }
            UpdateTerrainLocations();
        }

        public static WorldInfo Create(WorldSettings settings) => Create(settings, Version);

        public static WorldInfo CreateSolo(WorldSettings settings) => Create(settings, SoloVersion);

        static WorldInfo Create(WorldSettings settings, int version) {
            ArgumentNullException.ThrowIfNull(settings);
            if (settings.GameMode != GameMode.Survival) {
                throw new InvalidOperationException("A Westworld founding world requires Survival mode.");
            }
            settings.ResetOptionsForNonCreativeMode(null);
            settings.OriginalSerializationVersion = VersionsManager.SerializationVersion;
            settings.WestworldFoundingVersion = version;
            settings.WestworldRunId = Guid.NewGuid().ToString("N");
            return WorldsManager.CreateWorld(settings);
        }

        internal static void Register(Project project, XElement projectNode) {
            if (project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.WestworldFoundingVersion != 0) {
                m_worlds.Add(project, new FoundingWorld(project, projectNode));
            }
        }

        internal static bool HasFounders(Project project) => project != null
            && m_worlds.TryGetValue(project, out FoundingWorld world) && world.m_state != null;

        internal static XElement CaptureSave(Project project) {
            if (!m_worlds.TryGetValue(project, out FoundingWorld world) || world.m_state == null) {
                return null;
            }
            string observation = WorldCognitionScheduler.CaptureNativeObservation(project) ?? world.m_savedObservation;
            return new XElement(SaveElement, new XAttribute("Time", world.m_time.GameTime),
                observation == null ? null : new XElement(WorldCognitionScheduler.ObservationElement, observation));
        }

        internal static bool CanFly(WorldSettings settings, int playerIndex) => (settings.WestworldFoundingVersion is Version or SoloVersion)
            && settings.GameMode == GameMode.Survival && playerIndex > 0 && playerIndex == settings.WestworldPlayerIndex;

        internal static bool IsFounder(Entity entity) => entity.ValuesDictionary.DatabaseObject.Name is MaleTemplate or FemaleTemplate or ProducerTemplate;

        public static string InitialArchivePath(WorldInfo world) => Path.Combine(RunDirectory(world.WorldSettings), "initial.scworld");

        static string RunDirectory(WorldSettings settings) => Guid.TryParseExact(settings.WestworldRunId, "N", out Guid runId)
            ? Path.Combine(Storage.GetSystemPath("app:doc/Westworld"), runId.ToString("N"))
            : throw new InvalidDataException("Invalid Westworld run identifier.");

        internal static bool PrepareFrame(Project project) =>
            !m_worlds.TryGetValue(project, out FoundingWorld world) || world.Update();

        bool Update() {
            UpdateTerrainLocations();
            if (m_startFromSave) {
                return ResumeWhenTerrainReady();
            }
            if (m_failed || (m_state != null && !m_startFromInitial)) {
                return true;
            }
            ComponentPlayer player = m_players.ComponentPlayers.FirstOrDefault(candidate =>
                CanFly(m_info.WorldSettings, candidate.PlayerData.PlayerIndex));
            if (player == null || !player.PlayerData.IsReadyForPlaying) {
                return true;
            }
            m_camp ??= player.ComponentBody.Position.XZ;
            if (m_terrain.TerrainUpdater.GetUpdateProgress(CampLocation, 0f, CampRadius) < 1f) {
                return true;
            }
            try {
                if (m_state == null) {
                    CreateInitialArchive();
                }
                m_startFromInitial = false;
                StartCognition();
            }
            catch (Exception error) {
                m_failed = true;
                Log.Error($"Westworld founding setup failed: {error.GetType().Name}: {error.Message}");
                player.ComponentGui.DisplaySmallMessage("西部世界开局失败，请查看游戏日志。", Color.White, true, false);
            }
            return true;
        }

        bool ResumeWhenTerrainReady() {
            if (m_failed) {
                return false;
            }
            TerrainUpdater updater = m_terrain.TerrainUpdater;
            updater.Update();
            if (updater.GetUpdateProgress(CampLocation, 0f, CampRadius) < 1f
                || m_actorLocations.Any(id => updater.GetUpdateProgress(-id, 0f, ActorRadius) < 1f)) {
                return false;
            }
            try {
                StartCognition();
                m_savedObservation = null;
                m_startFromSave = false;
                return true;
            }
            catch (Exception error) {
                m_failed = true;
                Log.Error($"Westworld save could not resume: {error.GetType().Name}: {error.Message}");
                DialogsManager.ShowDialog(null, new MessageDialog("西部世界无法续跑",
                    "请检查模型配置和该世界的认知账本，然后重新打开世界。", LanguageControl.Ok, null, null));
                return false;
            }
        }

        void UpdateTerrainLocations() {
            if (m_camp is not Vector2 camp) {
                return;
            }
            TerrainUpdater updater = m_terrain.TerrainUpdater;
            updater.SetUpdateLocation(CampLocation, camp, 0f, CampRadius);
            foreach (Entity entity in m_founders) {
                if (entity == null) continue;
                ComponentBody body = entity.FindComponent<ComponentBody>();
                if (entity.IsAddedToProject && body != null && entity.FindComponent<ComponentHealth>() is { Health: > 0f }
                    && Vector2.DistanceSquared(camp, body.Position.XZ) > MathUtils.Sqr(CampRadius - ActorRadius - 8)) {
                    updater.SetUpdateLocation(-entity.Id, body.Position.XZ, 0f, ActorRadius);
                    m_actorLocations.Add(entity.Id);
                }
                else if (m_actorLocations.Remove(entity.Id)) {
                    updater.RemoveUpdateLocation(-entity.Id);
                }
            }
        }

        void CreateInitialArchive() {
            string directory = RunDirectory(m_info.WorldSettings);
            string archive = Path.Combine(directory, "initial.scworld");
            if (File.Exists(archive) || m_project.Entities.Any(IsFounder)) {
                throw new InvalidOperationException("Import the previously published initial archive to restart this founding world.");
            }
            Vector2 camp = m_camp.Value;
            Terrain terrain = m_terrain.Terrain;
            List<Vector3> sites = [];
            int originX = Terrain.ToCell(camp.X);
            int originZ = Terrain.ToCell(camp.Y);
            for (int dx = -44; dx <= 44; dx += 4) {
                for (int dz = -44; dz <= 44; dz += 4) {
                    if (dx * dx + dz * dz < 25 || terrain.GetChunkAtCell(originX + dx, originZ + dz) is not { State: >= TerrainChunkState.InvalidLight }) {
                        continue;
                    }
                    int x = originX + dx;
                    int z = originZ + dz;
                    int top = terrain.CalculateTopmostCellHeight(x, z);
                    if (top is > 0 and < 253 && Ground(terrain.GetCellContents(x, top, z))) {
                        sites.Add(new Vector3(x + .5f, top + 1f, z + .5f));
                    }
                }
            }
            int population = m_solo ? 1 : FounderCount;
            Vector3[] ordered = sites.OrderBy(position => Vector2.DistanceSquared(position.XZ, camp)).ToArray();
            Vector3[] positions = ordered.Take(population).ToArray();
            if (positions.Length != population) {
                throw new InvalidOperationException($"The spawn region does not have enough dry ground for {population} founders.");
            }
            Vector2? soloFacing = null;
            if (m_solo) {
                foreach (Vector3 site in ordered) {
                    if (NearbyLog(terrain, site) is Vector2 wood) {
                        positions[0] = site;
                        soloFacing = wood - site.XZ;
                        break;
                    }
                }
            }

            Directory.CreateDirectory(directory);
            int bread = BlocksManager.GetBlockIndex(nameof(BreadBlock), true);
            int axe = BlocksManager.GetBlockIndex(nameof(StoneAxeBlock), true);
            int meat = BlocksManager.GetBlockIndex(nameof(CookedMeatBlock), true);
            System.Random random = new(m_info.WorldSeed);
            random.Shuffle(positions);
            Founder[] founders = new Founder[population];
            m_founders = new Entity[population];
            SubsystemPickables pickables = m_project.FindSubsystem<SubsystemPickables>(true);
            for (int i = 0; i < population; i++) {
                bool female = (i & 1) != 0;
                Entity entity = DatabaseManager.CreateEntity(m_project, m_solo ? ProducerTemplate : female ? FemaleTemplate : MaleTemplate, true);
                ComponentBody body = entity.FindComponent<ComponentBody>(true);
                body.Position = positions[i];
                float yaw = soloFacing is Vector2 facing ? MathF.Atan2(-facing.X, -facing.Y) : random.NextSingle() * MathF.Tau;
                body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
                entity.FindComponent<ComponentActorNutrition>(true).Food = .3f + .3f * random.NextSingle();
                ComponentInventory inventory = entity.FindComponent<ComponentInventory>(true);
                inventory.AddSlotItems(0, bread, 1);
                if (i % 10 == 0) inventory.AddSlotItems(1, axe, 1);
                m_project.AddEntity(entity);
                m_founders[i] = entity;
                string name = $"{Names[i % Names.Length]}{i / Names.Length + 1}";
                string profile = $"你叫{name}，是部落成员，{(female ? "女性" : "男性")}。{Preferences[random.Next(Preferences.Length)]}"
                    + "部落带来了一些食物和简单工具。根据自己的感知、经历和需要决定现在做什么。";
                if (m_solo) {
                    profile = "你叫杉，是一位独自在野外生活的部落成员。你会用石斧取木，并知道随身的两格乘两格制造配方。"
                        + "你现在想做一张工作台，饿了先照顾自己。每次行动后看实际结果，再决定下一步。";
                }
                founders[i] = new Founder(entity.Id, profile);
                if (!m_solo && i % 5 == 0) {
                    pickables.AddPickable(meat, 4, body.Position + new Vector3(.5f, .1f, 0f), Vector3.Zero, null);
                }
            }

            // Store the generated terrain around the starting camp as part of the initial snapshot.
            foreach (TerrainChunk chunk in terrain.AllocatedChunks) {
                if (chunk.State >= TerrainChunkState.InvalidLight && Vector2.DistanceSquared(chunk.Center, camp) <= CampRadius * CampRadius) {
                    chunk.ModificationCounter++;
                }
            }
            WorldSettings settings = m_info.WorldSettings;
            InitialState state = new(camp.X, camp.Y, m_time.GameTime, founders);
            settings.WestworldFoundingState = JsonSerializer.Serialize(state);
            settings.WestworldInitialArchive = true;
            try {
                GameManager.m_saveCompleted.WaitOne();
                ActorTools.WriteTaskArchive(m_project, archive, null, null, overwrite: false);
                m_state = state;
            }
            finally {
                settings.WestworldInitialArchive = false;
                if (m_state == null) settings.WestworldFoundingState = string.Empty;
            }
            GameManager.SaveProject(waitForCompletion: true, showErrorDialog: true);
            Log.Information("Westworld initial archive: " + archive);
        }

        static bool Ground(int contents) => BlocksManager.Blocks[contents] is GrassBlock or DirtBlock or SandBlock
            or GravelBlock or GraniteBlock or LimestoneBlock or SandstoneBlock;

        static Vector2? NearbyLog(Terrain terrain, Vector3 site) {
            Point3 center = Terrain.ToCell(site);
            for (int x = center.X - 4; x <= center.X + 4; x++) {
                for (int z = center.Z - 4; z <= center.Z + 4; z++) {
                    Vector2 point = new(x + .5f, z + .5f);
                    if (Vector2.DistanceSquared(point, site.XZ) is < 4f or > 20f) continue;
                    for (int y = center.Y; y <= center.Y + 1; y++) {
                        if (BlocksManager.Blocks[terrain.GetCellContents(x, y, z)] is WoodBlock) return point;
                    }
                }
            }
            return null;
        }

        void StartCognition() {
            Entity[] living = m_founders.Where(entity => entity is { IsAddedToProject: true }
                && IsFounder(entity) && entity.FindComponent<ComponentHealth>() is { Health: > 0f }).ToArray();
            if (living.Length == 0 || !HasModelConfiguration() && m_savedObservation == null) {
                return;
            }
            string ledgerDirectory = Path.Combine(RunDirectory(m_info.WorldSettings), "cognition");
            ActorModelClient model = ActorModelClient.FromEnvironment();
            long feeLimit = RequiredAmount("WENMING_FOUNDING_BUDGET_NANO_USD");
            const string storageLimitVariable = "WENMING_FOUNDING_STORAGE_LIMIT_BYTES";
            long? storageLimit = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(storageLimitVariable))
                ? null : RequiredAmount(storageLimitVariable);
            WorldCognitionScheduler scheduler = new(model, ledgerDirectory, 15,
                feeLimit, RequiredAmount("WENMING_FOUNDING_INPUT_NANO_USD"),
                RequiredAmount("WENMING_FOUNDING_OUTPUT_NANO_USD"), "westworld-founding-v1");
            try {
                Dictionary<int, string> profiles = m_state.Founders.ToDictionary(founder => founder.Id, founder => founder.Profile);
                foreach (Entity entity in living) {
                    ActorTools tools = new(new AtomicActions(entity));
                    scheduler.Register(new ActorCognition(tools, profiles[entity.Id]), tools);
                }
                scheduler.StartNativeObservation(m_savedObservation, feeLimit, storageLimit);
            }
            catch {
                scheduler.Dispose();
                throw;
            }
        }

        static bool HasModelConfiguration() =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WENMING_FOUNDING_BUDGET_NANO_USD"));

        static long RequiredAmount(string name) => long.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None,
            CultureInfo.InvariantCulture, out long amount) && amount > 0 ? amount
            : throw new InvalidOperationException($"Set {name} to a positive integer in nano-USD.");
    }
}
