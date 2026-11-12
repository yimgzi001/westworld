(function () {
    var W = importNamespace("Game.Westworld"), phase = "load", started, clock, scheduler, http, ids, nextSample,
        worldDirectory, first, journal, stopReason, stopped, restored = false, lastSequence = 0, exposure;
    var config = JSON.parse(Engine.Storage.ReadAllText("app:cognition-cycle-config.json"));
    var resume = Engine.Storage.FileExists("app:cognition-cycle-checkpoint.json");
    var prior = resume ? JSON.parse(Engine.Storage.ReadAllText("app:cognition-cycle-checkpoint.json")) : null;
    var port = System.Environment.GetEnvironmentVariable("WENMING_PANEL_PORT");
    if (config.perturb && Engine.Storage.FileExists("app:cognition-cycle-intervention.json"))
        exposure = JSON.parse(Engine.Storage.ReadAllText("app:cognition-cycle-intervention.json"));
    function write(name, value) { Engine.Storage.WriteAllText("app:" + name, JSON.stringify(value, null, 2)); }
    function snapshot() {
        try { return JSON.parse(http.GetStringAsync("http://127.0.0.1:" + port + "/api/snapshot").GetAwaiter().GetResult()); }
        catch (_) { return null; }
    }
    function selected(s) { return s.actors.filter(a => ids.includes(a.id)); }
    function states(s) { return selected(s).map(a => ({ id: a.id, alive: a.alive, health: a.health, inventory: a.inventory,
        position: a.position, somatic: a.somatic, action: a.action,
        cycle: a.cognition?.cognitionCycle, task: a.task })); }
    function verify(condition, message) { if (!condition) throw new Error(message); }
    function save() {
        Game.GameManager.SaveProject(true, false);
        var doc = new (importNamespace("System.Xml").XmlDocument)();
        doc.LoadXml(Engine.Storage.ReadAllText(Engine.Storage.CombinePaths(worldDirectory, "Project.xml")));
        verify(Number(doc.SelectSingleNode("/Project/WestworldFoundingSave").GetAttribute("Time")) === clock.GameTime,
            "Native save time must equal the stopped simulation time");
        return JSON.parse(doc.SelectSingleNode("/Project/WestworldFoundingSave/WestworldObservation").InnerText);
    }
    function finish(result, s, error) {
        phase = "done";
        if (journal) { journal.Flush(); journal.Dispose(); }
        write("cognition-cycle-result.json", { result, error, startedAt: first?.at, completedAt: new Date().toISOString(),
            realSeconds: (prior?.observedSeconds ?? 0) + (started ? Engine.Time.RealTime - started : 0),
            intervention: exposure,
            requestedSeconds: config.seconds, initial: first, restored, stopReason, worldDirectory,
            runtime: { visible: Engine.Window.IsVisible, sounds: Game.SettingsManager.SoundsVolume, music: Game.SettingsManager.MusicVolume },
            model: s?.model?.status, actors: s ? states(s) : [], snapshot: s });
        Engine.Window.Close();
    }
    frameHandlers.push(function () {
        if (phase === "done") return;
        try {
            if (phase === "load") {
                if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu" || Game.ScreensManager.IsAnimating) return;
                var world;
                if (resume) world = Game.WorldsManager.GetWorldInfo(prior.worldDirectory);
                else {
                    var settings = new Game.WorldSettings();
                    settings.Name = "自由认知验证";
                    settings.Seed = "42";
                    world = W.FoundingWorld.CreateOpen(settings, config.population);
                }
                Game.ScreensManager.SwitchScreen("GameLoading", world, null);
                http = new (importNamespace("System.Net.Http").HttpClient)();
                phase = "start";
                return;
            }
            if (phase === "start") {
                var project = getProject(); if (!project) return;
                scheduler = W.WorldCognitionScheduler.ForWorld(project); if (!scheduler) return;
                clock = findSubsystem("Time");
                if (resume) verify(JSON.parse(scheduler.StatusJson()).state === "userPaused",
                    "Saved user pause must hold the restored world before verification");
                var s = snapshot(); if (!s || s.state !== "live") return;
                var actors = Array.from(project.Entities).filter(e => e.ValuesDictionary.DatabaseObject.Name === "WestworldProducer");
                ids = prior?.initial.actorIds ?? actors.map(a => a.Id);
                worldDirectory = findSubsystem("GameInfo").DirectoryName;
                // The panel snapshot can precede AI registration. Do not let an empty selection
                // vacuously pass initial inventory/goal checks or the restore comparisons.
                if (!resume && (selected(s).length !== ids.length || selected(s).some(a => !a.cognition?.cognitionCycle))) return;
                if (resume && prior.actors.some(old => old.alive && !selected(s).some(a => a.id === old.id && a.cognition?.cognitionCycle))) return;
                verify(!Engine.Window.IsVisible && Game.SettingsManager.SoundsVolume === 0 && Game.SettingsManager.MusicVolume === 0,
                    "Real verification requires a hidden and silent game");
                var players = findSubsystem("Players").ComponentPlayers;
                if (players.Count !== 1 || !resume && !players.get_Item(0).PlayerData.IsReadyForPlaying) return;
                var observer = players.get_Item(0);
                observer.ComponentHealth.IsInvulnerable = true;
                if (!resume) {
                    var body = observer.ComponentBody;
                    body.Position = new Engine.Vector3(body.Position.X, body.Position.Y + 20, body.Position.Z);
                    observer.ComponentLocomotion.IsCreativeFlyEnabled = true;
                    verify(actors.length === config.population, "Native initial population does not match");
                    verify(selected(s).every(a => a.inventory.length === 0 && a.cognition?.cognitionCycle?.goals.length === 0),
                        "Real run must start without given items or goals");
                    first = { at: new Date().toISOString(), actorIds: ids, population: ids.length,
                        model: System.Environment.GetEnvironmentVariable("WENMING_MODEL"), seed: findSubsystem("GameInfo").WorldSeed,
                        presetGoals: [], presetRoles: [], givenItems: [], placedResources: [], actors: states(s) };
                    write("cognition-cycle-initial.json", first);
                } else {
                    first = prior.initial;
                    for (var old of prior.actors) {
                        var current = selected(s).find(a => a.id === old.id);
                        if (!old.alive && !current) continue;
                        verify(current && JSON.stringify(current.inventory) === JSON.stringify(old.inventory), "Inventory changed across native resume");
                        verify(current.alive === old.alive, "Actor life state changed across native resume");
                        // Dead bodies are native world entities, not active scheduler participants after reload.
                        if (!old.alive) continue;
                        verify(JSON.stringify(current.cognition.cognitionCycle) === JSON.stringify(old.cycle),
                            "Cognitive state, receipts, method windows or event cursors changed across resume");
                        verify(JSON.stringify(current.task) === JSON.stringify(old.task), "Native task changed across resume");
                    }
                    verify(s.model.status.spent === prior.model.spent, "Settled fees changed across resume");
                    verify(clock.GameTime === prior.savedSimulationTime, "The saved simulation time advanced before verification");
                    restored = true;
                    write("cognition-cycle-restored.json", { at: new Date().toISOString(), simulationTime: clock.GameTime, actors: states(s), model: s.model.status });
                }
                journal = new System.IO.StreamWriter(Engine.Storage.GetSystemPath("app:cognition-cycle-samples.jsonl"), true);
                started = nextSample = Engine.Time.RealTime;
                if (resume) scheduler.SetPaused(false);
                phase = "observe";
                return;
            }
            var real = Engine.Time.RealTime, elapsed = real - started, remaining = config.seconds - (prior?.observedSeconds ?? 0);
            var totalElapsed = (prior?.observedSeconds ?? 0) + elapsed;
            if (phase === "observe" && config.perturb && totalElapsed >= 60 && totalElapsed <= 140) {
                var target = getProject().FindEntity(ids[0]);
                if (!exposure && target) {
                    var p = target.FindComponent("Body", true).Position;
                    exposure = { kind: "registered-local-injury-field", onset: 60, end: 140, radius: 3, interval: 8,
                        damage: .12, actorId: ids[0], center: { x: p.X, y: p.Y, z: p.Z }, next: totalElapsed, pulses: [] };
                }
                if (target && exposure && totalElapsed >= exposure.next) {
                    exposure.next += exposure.interval;
                    var p = target.FindComponent("Body", true).Position, health = target.FindComponent("Health", true);
                    var distance = Math.hypot(p.X - exposure.center.x, p.Y - exposure.center.y, p.Z - exposure.center.z);
                    if (distance <= exposure.radius && health.Health > 0) {
                        var before = health.Health;
                        health.Injure(new Game.Injury(exposure.damage, null, false, "registered verification field"));
                        exposure.pulses.push({ elapsed: totalElapsed, at: clock.GameTime, before, after: health.Health,
                            position: { x: p.X, y: p.Y, z: p.Z } });
                    }
                    write("cognition-cycle-intervention.json", exposure);
                }
            }
            if (phase === "observe" && (elapsed >= (config.reload && !resume ? config.seconds / 2 : remaining))) {
                stopReason = config.reload && !resume ? "checkpoint" : "window_completed";
                scheduler.StopObservationDispatch(); scheduler.SetPaused(true);
                stopped = { at: new Date().toISOString(), simulationTime: clock.GameTime, elapsed };
                phase = "drain";
            }
            if (real < nextSample) return;
            nextSample = real + (phase === "drain" ? .5 : config.perturb ? 1 : 5);
            var s = snapshot(); if (!s || s.state !== "live") return;
            var data = { at: new Date().toISOString(), phase, elapsed: (prior?.observedSeconds ?? 0) + elapsed,
                simulationTime: clock.GameTime, actors: states(s), model: s.model.status,
                segment: resume ? "resumed" : "initial",
                events: (s.events ?? []).filter(e => e.sequence > lastSequence),
                speech: (s.speech ?? []).filter(e => e.sequence > lastSequence) };
            lastSequence = s.sequence;
            journal.WriteLine(JSON.stringify(data)); journal.Flush();
            write("cognition-cycle-status.json", data);
            if (s.model.status.state === "faulted") throw new Error("Native cognition scheduler faulted: " + s.model.status.fault);
            if (phase === "drain") {
                var status = JSON.parse(scheduler.StatusJson());
                if (status.inFlight !== 0 || status.ledgerPending || s.model.status.inFlight !== 0
                    || s.model.status.spent !== status.spent || s.model.status.state !== "userPaused"
                    || s.model.status.time !== clock.GameTime) return;
                var nativeSave = save();
                if (stopReason === "checkpoint") {
                    // A cached panel frame may precede the last completed step. The actual native
                    // payload is the authority for what must be restored, not that earlier frame.
                    var savedActors = states(s).map(actor => {
                        var saved = nativeSave.actors.find(a => a.id === actor.id);
                        return saved ? Object.assign({}, actor, { cycle: saved.cognition.cognitionCycle,
                            task: saved.task?.view ?? saved.cognition.lastTask ?? actor.task }) : actor;
                    });
                    write("cognition-cycle-checkpoint.json", { result: "passed", observedSeconds: stopped.elapsed, initial: first,
                        worldDirectory, stopped, savedSimulationTime: nativeSave.time, actors: savedActors, model: status });
                    phase = "done"; journal.Dispose(); Engine.Window.Close();
                } else finish("passed", s);
            }
        } catch (error) {
            if (scheduler) scheduler.SetPaused(true);
            finish("failed", http ? snapshot() : null, String(error));
        }
    });
})();
