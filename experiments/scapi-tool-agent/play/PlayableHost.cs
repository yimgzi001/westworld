#nullable enable

using System.Diagnostics;
using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using GameEntitySystem;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal sealed class PlayableHost(int actorCount, string seed, int? requestLimit, string mode, bool watch)
{
    private static readonly string Root = AppContext.BaseDirectory;
    private static readonly InterestRadii ActorMapRange = new(32, 48);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly PlayDecisionQueue queue = new(2, requestLimit);
    private readonly List<PlayableActor> actors = new();
    private Project? world;
    private SubsystemUpdate? updates;
    private SubsystemTime? time;
    private NativeActorSession? senses;
    private ScapiInterestBackend? backend;
    private InterestCoordinator? interests;
    private readonly StructuredFileController? bridge = mode == "smoke" ? null
        : new(Path.Combine(Root, "control"), _ => { }, mode == "survival" ? null : 4096);
    private PlaySurvivalTrial? trial;
    private PlayFollowView? followView;
    private int nextActor;
    private double nextStopCheck;
    private bool loading, closing, closed, passed, concurrentProgress, budgetLogged;
    private string reason = "window_closed";
    private bool Smoke => mode == "smoke";
    private bool Survival => mode == "survival";

    public static int Run(string[] args)
    {
        if (args.Length is not (4 or 5) || !int.TryParse(args[0], out int count) || count is < 1 or > 4
            || args[1].Length is < 1 or > 64 || args[1].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
            || !int.TryParse(args[2], out int limit) || limit < 0
            || (args[3] != "survival" && (limit is < 1 or > 12))
            || args[3] is not ("terra" or "smoke" or "survival") || (args[3] == "survival" && count is not (1 or 3))
            || (args.Length == 5 && (args[3] != "survival" || args[4] != "watch"))
            || !Root.Contains(Path.DirectorySeparatorChar + "work" + Path.DirectorySeparatorChar)
            || Directory.Exists(Path.Combine(Root, "doc")) || Directory.Exists(Path.Combine(Root, "Mods")))
        {
            Console.Error.WriteLine("请使用 tooling/试玩西部世界.py 在新的 work 子目录启动试玩。");
            return 2;
        }

        ToolAgentGate.PrepareIsolatedWindowsSettings();
        int? requestLimit = limit == 0 ? null : limit;
        bool watch = args.Length == 5;
        Window.StartHidden = args[3] != "terra" && !watch;
        var host = new PlayableHost(count, args[1], requestLimit, args[3], watch);
        Window.Created += () => Window.Frame += host.Frame;
        Window.Closed += host.Shutdown;
        Log(new { kind = "play_start", actorCount = count, seed = args[1], mode = args[3], requestLimit, watch });
        try { Game.Program.EntryPoint(); }
        catch (Exception error)
        {
            host.passed = false;
            host.reason = "game_error";
            Log(new { kind = "play_error", error = error.ToString() });
        }
        finally { host.Shutdown(); }
        return host.passed ? 0 : 1;
    }

    private void Frame()
    {
        if (closing) return;
        try
        {
            double elapsed = clock.Elapsed.TotalSeconds;
            if (elapsed >= nextStopCheck)
            {
                nextStopCheck = elapsed + 0.25;
                if (File.Exists(Path.Combine(Root, "stop.txt"))) Close(!Smoke && !Survival, "launcher_stopped");
            }
            if (closing) return;
            if ((actors.Count == 0 && elapsed > 90) || (Smoke && elapsed > 100))
            {
                Close(false, "play_timeout");
                return;
            }
            if (!loading && ScreensManager.CurrentScreen is MainMenuScreen && !ScreensManager.IsAnimating)
            {
                loading = true;
                SettingsManager.VisibilityRange = (int)ActorMapRange.FullReady;
                var settings = new WorldSettings
                {
                    Name = "西部世界试玩", Seed = seed, GameMode = GameMode.Survival,
                    OriginalSerializationVersion = VersionsManager.SerializationVersion
                };
                if (Smoke) settings.TerrainGenerationMode = TerrainGenerationMode.FlatContinent;
                ScreensManager.SwitchScreen("GameLoading", WorldsManager.CreateWorld(settings), null);
            }
            if (world is not null && !ReferenceEquals(world, GameManager.Project))
            {
                Close(!Smoke && !Survival, "world_left");
                return;
            }
            if (!loading || GameManager.Project is not { } project) return;

            var players = project.FindSubsystem<SubsystemPlayers>(true);
            if (players.PlayersData.Count == 0)
            {
                for (int i = 0; i < actorCount; i++)
                    players.AddPlayerData(new PlayerData(project) { Name = $"AI {i + 1}", IsExternalActor = true });
                if (!Survival) players.AddPlayerData(new PlayerData(project) { Name = "观察者" });
                ScreensManager.SwitchScreen("Game");
            }
            if (world is null)
            {
                if (players.ComponentPlayers.Count != actorCount + (Survival ? 0 : 1)
                    || players.PlayersData.Any(p => !p.IsReadyForPlaying)) return;
                Bind(project, players);
            }
            if (updates!.UpdateFailureCount != 0) throw new InvalidOperationException(updates.LastUpdateFailure);
            if (ScreensManager.CurrentScreen is not GameScreen || time!.GameTimeDelta <= 0) return;

            Advance(time.GameTime);
        }
        catch (Exception error)
        {
            Log(new { kind = "play_error", error = error.ToString() });
            Close(false, "play_error");
        }
    }

    private void Bind(Project project, SubsystemPlayers players)
    {
        var external = players.ComponentPlayers.Where(p => p.PlayerData.IsExternalActor).OrderBy(p => p.PlayerData.PlayerIndex).ToArray();
        int views = project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count;
        if (external.Length != actorCount || views != (Survival ? 0 : 1) || external.Any(p => p.GameWidget is not null))
            throw new InvalidOperationException("AI 角色或玩家视口数量不符合场景配置。");

        world = project;
        var settings = project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings;
        if (Survival && (settings.GameMode != GameMode.Survival || !settings.AreAdventureSurvivalMechanicsEnabled))
            throw new InvalidOperationException("生存长测需要启用完整机制的原生生存难度。");
        updates = project.FindSubsystem<SubsystemUpdate>(true);
        time = project.FindSubsystem<SubsystemTime>(true);
        var terrain = project.FindSubsystem<SubsystemTerrain>(true);
        var worldId = Guid.NewGuid();
        senses = new NativeActorSession(project, worldId, actionsEnabled: false, observationAbove: Survival ? 3 : 1);
        foreach (var player in external)
        {
            var actor = new PlayableActor(player, new AtomicActions(player.Entity), worldId, Log, Survival, ActorMapRange.Content,
                allowSocial: !Survival || actorCount > 1);
            actors.Add(actor);
            senses.Bind(actor.Id, player);
            if (Survival) actor.BindTravel(senses);
        }
        updates.AddUpdateable(senses);
        backend = new ScapiInterestBackend(terrain, project.FindSubsystem<SubsystemSpawn>(true));
        interests = new InterestCoordinator(senses.Registry, backend, ActorMapRange);
        interests.Sync();
        foreach (var player in external) terrain.TerrainUpdater.RemoveUpdateLocation(player.PlayerData.PlayerIndex);
        if (Survival) trial = new PlaySurvivalTrial(project, external, time.GameTime, Log);
        if (Survival) NativePredatorAcoustics.Emitted += OnPredatorSound;
        if (watch) followView = new PlayFollowView(external[0], Log);
        Log(new { kind = "play_bound", worldId, views, passiveViews = watch ? 1 : 0,
            windowVisible = Window.m_gameWindow?.IsVisible, gameMode = settings.GameMode.ToString(),
            terrainMode = settings.TerrainGenerationMode.ToString(), survivalMechanics = settings.AreAdventureSurvivalMechanicsEnabled,
            mapContentRadius = ActorMapRange.Content, terrainReadyRadius = ActorMapRange.FullReady,
            actors = actors.Select(a => new { a.Name, a.Id, a.Session }).ToArray() });
    }

    private void Advance(double gameTime)
    {
        interests!.Sync();
        foreach (var actor in actors) actor.Update(queue, gameTime);

        if (trial is { } survivalTrial)
        {
            survivalTrial.Sample(gameTime, actors.All(actor => actor.VoluntarySleeps > 0));
            if (actors.Any(actor => actor.Retired && !actor.Died))
            {
                Close(false, "survival_actor_unavailable");
                return;
            }
            if (survivalTrial.AllDied)
            {
                Close(survivalTrial.Passed, "survival_all_died");
                return;
            }
        }
        if (Smoke)
        {
            if (actorCount > 1 && queue.IsPending(actors[0].Id) && actors[0].Timeouts == 0 && actors[1].Moves > 0)
                concurrentProgress = true;
            if (actors.All(actor => actor.SmokeReady)
                && (actorCount == 1 || concurrentProgress && actors[0].Timeouts > 0 && actors[0].HeardSmokeSpeech))
            {
                Close(true, "smoke_passed");
                return;
            }
            if (queue.BudgetReached && actors.All(actor => !actor.IsActing && !queue.IsPending(actor.Id)))
            {
                Close(false, "smoke_budget_exhausted");
                return;
            }
        }

        if (Survival && queue.BudgetReached && actors.All(actor => !actor.IsActing && !queue.IsPending(actor.Id)))
        {
            Close(false, "survival_budget_exhausted");
            return;
        }

        // 按角色轮转分配空位。忙碌或等回复的角色不会占住游戏线程。
        int start = nextActor;
        for (int n = 0; n < actors.Count && (Survival || queue.HasSlot); n++)
        {
            int index = (start + n) % actors.Count;
            if (actors[index].Request(queue, bridge, gameTime, delayFirst: Smoke && index == 0 && actorCount > 1,
                smokeSpeaker: Smoke && index == 1))
                nextActor = (index + 1) % actors.Count;
        }
        if (queue.BudgetReached && !budgetLogged)
        {
            budgetLogged = true;
            Log(new { kind = "play_request_budget_reached", limit = requestLimit });
        }
    }

    private void OnPredatorSound(NativePredatorAcoustics.Signal signal)
    {
        if (!ReferenceEquals(signal.World, world)) return;
        foreach (var actor in actors) actor.HearPredator(signal);
    }

    private void Close(bool success, string why)
    {
        passed = success;
        reason = why;
        closing = true;
        Window.Close();
    }

    private void Shutdown()
    {
        if (closed) return;
        closed = true;
        if (!closing && reason == "window_closed") passed = !Smoke && !Survival && world is not null;

        bool modelsDrained = false;
        bool actionsDrained = true;
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                passed = false;
                Log(new { kind = "play_cleanup_error", error = error.ToString() });
            }
        }

        Cleanup(() => File.WriteAllText(Path.Combine(Root, "exit.txt"), reason));
        if (Survival) NativePredatorAcoustics.Emitted -= OnPredatorSound;
        Cleanup(() => modelsDrained = queue.Stop());
        Cleanup(() => followView?.Detach());
        Cleanup(() => trial?.Finish(time!.GameTime));
        if (world is not null && ReferenceEquals(world, GameManager.Project))
        {
            foreach (var actor in actors) Cleanup(() => actionsDrained &= actor.Stop());
            Cleanup(() => { if (senses is not null) updates?.RemoveUpdateable(senses); });
            Cleanup(() => interests?.Release());
            Cleanup(() => backend?.Dispose());
            Cleanup(() => senses?.Dispose());
            Cleanup(() => GameManager.SaveProject(true, false));
        }
        passed &= modelsDrained && actionsDrained;
        Log(new
        {
            kind = "play_result", passed, reason, smoke = Smoke, mode, modelsDrained, actionsDrained, concurrentProgress,
            updateFailures = updates?.UpdateFailureCount, requests = queue.Issued, survivalMilestoneReached = trial?.Passed,
            deaths = trial?.DeathCount,
            actors = actors.Select(a => new { a.Name, a.Decisions, a.Moves, a.Timeouts, a.HeardSmokeSpeech, a.VoluntarySleeps }).ToArray()
        });
    }

    private static void Log(object value)
    {
        var line = JsonSerializer.Serialize(value);
        Console.WriteLine(line);
        File.AppendAllText(Path.Combine(Root, "play.jsonl"), line + Environment.NewLine);
    }
}
