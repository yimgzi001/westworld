(function () {
    var W = importNamespace("Game.Westworld");
    var phase = "load", checks = [], actors = [], bags = [], trace = [];
    var clock, scheduler, start, planks, workbench, nextPoll = 0, http, url;
    var gather = Engine.Storage.FileExists("app:cooperation-gather");
    var terrain, log, axe, source, pickables, groundDrop;
    var reload = Engine.Storage.FileExists("app:cooperation-reload"), resumed = false, prefix = null, profilesByActor = [], savedRun, checkpointRequestedAt;

    function connectPanel() {
        http = new (importNamespace("System.Net.Http").HttpClient)();
        url = "http://127.0.0.1:" + System.Environment.GetEnvironmentVariable("WENMING_PANEL_PORT") + "/api/snapshot";
    }

    function newScheduler() {
        var client = new W.ActorModelClient("https://api.openai.com/v1/responses",
            System.Environment.GetEnvironmentVariable("WENMING_MODEL"), "responses",
            System.Environment.GetEnvironmentVariable("WENMING_MODEL_API_KEY"));
        return new W.WorldCognitionScheduler(client, "app:cognition-run", 2, gather ? 50000000 : 30000000,
            100, 500, "openai-luna-standard-2026-11");
    }

    function check(condition, detail) {
        if (!condition) throw new Error(detail);
        checks.push(detail);
    }

    function finish(result, extra) {
        phase = "done";
        var output = reload && !resumed ? "app:cooperation-checkpoint.json" : "app:cooperation-result.json";
        Engine.Storage.WriteAllText(output, JSON.stringify({ result, checks, inventoryTrace: trace,
            runtime: { visible: Engine.Window.IsVisible, sounds: Game.SettingsManager.SoundsVolume, music: Game.SettingsManager.MusicVolume }, ...extra }, null, 2));
        Engine.Window.Close();
    }

    function count(inventory, value) {
        var total = 0;
        for (var slot = 0; slot < inventory.SlotsCount; slot++) {
            if (inventory.GetSlotValue(slot) === value) total += inventory.GetSlotCount(slot);
        }
        return total;
    }

    function inventoryState() {
        return bags.map(bag => ({
            ...(gather ? { logs: count(bag, log) } : {}),
            planks: count(bag, planks), workbenches: count(bag, workbench)
        }));
    }

    function recordInventory() {
        var state = inventoryState();
        if (!trace.length || JSON.stringify(trace[trace.length - 1].bags) !== JSON.stringify(state)) {
            trace.push({ time: clock.GameTime, bags: state });
        }
        return state;
    }

    function setup(project, origin) {
        if (System.Environment.GetEnvironmentVariable("WENMING_TEST_BACKGROUND") === "1") check(!Engine.Window.IsVisible, "the native game window stays hidden");
        if (System.Environment.GetEnvironmentVariable("WENMING_TEST_MUTE") === "1") check(Game.SettingsManager.SoundsVolume === 0 && Game.SettingsManager.MusicVolume === 0, "game effects and music stay muted");
        terrain = findSubsystem("Terrain");
        var x = Math.floor(origin.X), y = Math.floor(origin.Y), z = Math.floor(origin.Z) - 4;
        for (var dx = -5; dx <= 5; dx++) {
            for (var dz = -5; dz <= 5; dz++) {
                terrain.ChangeCell(x + dx, y - 1, z + dz, 1, true, null);
                for (var dy = 0; dy < 3; dy++) terrain.ChangeCell(x + dx, y + dy, z + dz, 0, true, null);
            }
        }
        planks = Game.BlocksManager.GetBlockIndex("PlanksBlock", true);
        workbench = Game.BlocksManager.GetBlockIndex("CraftingTableBlock", true);
        if (gather) {
            log = Game.BlocksManager.GetBlockIndex("OakWoodBlock", true);
            axe = Game.BlocksManager.GetBlockIndex("StoneAxeBlock", true);
            source = { x: x + 1, y, z: z + 1, value: log };
            terrain.ChangeCell(source.x, source.y, source.z, log, true, null);
            pickables = findSubsystem("Pickables");
        }
        scheduler = newScheduler();
        var profiles = [
            "你叫杉。你和身旁的禾想合作做一张工作台，禾愿意负责制作。你有三块木板，愿意贡献。先跟他口头沟通，听见他的需要就回应。交给他物品时发出交接提议，等他自行决定是否接收。用简短中文讲话。",
            "你叫禾。你和身旁的杉想合作做一张工作台，你愿意负责制作。你只有一块木板。四块木板放进随身2×2制造格可以做一张工作台。先用简短中文和杉商量缺少的材料，听取他的回答，按收到的提议决定是否接收，再根据实际背包制作。"
        ];
        ["杉", "禾"].forEach((name, index) => {
            var actor = Game.DatabaseManager.CreateEntity(project, "WestworldProducer", true);
            var body = actor.FindComponent("Body", true);
            body.Position = new Engine.Vector3(x + .5, y + .1, z + .5 + index * 1.4);
            var yaw = gather ? Math.atan2(-.7, index ? 1 : -1) : index ? 0 : Math.PI;
            body.Rotation = Engine.Quaternion.CreateFromAxisAngle(Engine.Vector3.UnitY, yaw);
            actor.FindComponent("Creature", true).DisplayName = name;
            actor.FindComponent("Nutrition", true).Food = .8;
            var inventory = actor.FindComponent("Inventory", true);
            if (!gather) inventory.AddSlotItems(0, planks, index ? 1 : 3);
            else if (!index) inventory.AddSlotItems(0, axe, 1);
            project.AddEntity(actor);
            actors.push(actor);
            bags.push(inventory);
            var tools = new W.ActorTools(new W.AtomicActions(actor), false);
            var profile = gather
                ? "你叫" + name + "，身旁的" + (index ? "杉" : "禾") + "和你想合作做一张工作台。"
                    + "这次共同目标包括采集原木，把原木或木板从一人的背包实际交给另一人，再由收到材料的人制作工作台。"
                    + "谁去采集、交接原木还是木板、谁来制作，由你们用简短中文自行商量。"
                    + "一份原木可做四块木板，正好做一张工作台。交接时双方相距不超过两格，收到提议后独立决定是否接收。"
                : profiles[index];
            scheduler.Register(new W.ActorCognition(tools, profile), tools);
            profilesByActor.push(profile);
        });
        check(actors[0].Id !== actors[1].Id && actors.every(actor => actor.IsAddedToProject), "two independently scheduled producers in one survival world");
        var initial = recordInventory();
        if (gather) {
            check(initial.every(bag => bag.logs === 0 && bag.planks === 0 && bag.workbenches === 0)
                && count(bags[0], axe) === 1 && count(bags[1], axe) === 0,
                "both backpacks started without raw materials or products, with one stone axe in total");
            check(terrain.Terrain.GetCellContents(source.x, source.y, source.z) === log
                && !Array.from(pickables.Pickables).some(item => Game.Terrain.ExtractContents(item.Value) === log),
                "the test oak log started as a terrain block with no loose oak log on the ground");
        } else {
            check(initial[0].planks === 3 && initial[1].planks === 1 && initial.every(bag => bag.workbenches === 0),
                "initial backpacks contain three and one planks and no workbench");
        }
        scheduler.StartObservation(false);
        connectPanel();
        start = Engine.Time.RealTime;
    }

    function evidence() {
        var snapshot = JSON.parse(http.GetStringAsync(url).GetAwaiter().GetResult());
        var result = {
            speech: (snapshot.speech ?? []).filter(event => actors.some(actor => actor.Id === event.actorId)),
            transfers: (snapshot.events ?? []).filter(event => event.kind === "transfer"),
            crafts: (snapshot.events ?? []).filter(event => event.kind === "action" && event.data.action?.name === "Craft"),
            ...(gather ? { gathering: (snapshot.events ?? []).filter(event => event.kind === "action"
                && ["Dig", "PickUp"].includes(event.data.action?.name)) } : {})
        };
        if (prefix) for (var key of ["speech", "transfers", "crafts", "gathering"]) result[key] = (prefix[key] ?? []).concat(result[key] ?? []);
        return result;
    }

    function gatherEvidence(report, state) {
        var harvest = report.gathering.find(event => {
            var cell = event.data.action.cell;
            return event.data.status === "succeeded" && event.data.action.name === "Dig"
                && cell?.x === source.x && cell.y === source.y && cell.z === source.z && event.data.action.value === log;
        });
        var pickup = report.gathering.find(event => event.data.status === "succeeded" && event.data.action.name === "PickUp"
            && event.time > harvest?.time && event.data.items?.some(item => item.value === log && item.count === 1));
        var maker = state.findIndex(bag => bag.workbenches === 1);
        var craft = report.crafts.find(event => event.actorId === actors[maker]?.Id && event.data.status === "succeeded"
            && event.data.affectedCount === 1);
        var handoff = report.transfers.find(event => {
            if (event.data.status !== "transferred" || event.data.recipient.id !== actors[maker]?.Id
                || !(event.time > pickup?.time && event.time < craft?.time)) return false;
            var material = event.data.value === log ? "logs" : event.data.value === planks ? "planks" : null;
            var giver = actors.findIndex(actor => actor.Id === event.actorId);
            return material != null && giver >= 0 && giver !== maker && trace.some((entry, index) => index > 0
                && Math.abs(entry.time - event.time) < .1
                && trace[index - 1].bags[giver][material] - entry.bags[giver][material] === event.data.count
                && entry.bags[maker][material] - trace[index - 1].bags[maker][material] === event.data.count);
        });
        return {
            source: { ...source, remainingContents: terrain.Terrain.GetCellContents(source.x, source.y, source.z) },
            groundDrop, harvest, pickup, handoff, craft,
            passed: !!(harvest && groundDrop && pickup && handoff && craft
                && terrain.Terrain.GetCellContents(source.x, source.y, source.z) === 0
                && state.every(bag => bag.logs === 0 && bag.planks === 0)
                && state.reduce((total, bag) => total + bag.workbenches, 0) === 1)
        };
    }

    frameHandlers.push(function () {
        if (phase === "done") return;
        try {
            if (phase === "load") {
                if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu" || Game.ScreensManager.IsAnimating) return;
                if (reload && Engine.Storage.FileExists("app:cooperation-resume.json")) {
                    savedRun = JSON.parse(Engine.Storage.ReadAllText("app:cooperation-resume.json"));
                    var savedScreen = Game.ScreensManager.m_screens.get_Item("Game");
                    Game.GameManager.LoadProject(Game.WorldsManager.GetWorldInfo(savedRun.directory), savedScreen.Children.Find("GamesWidget", true));
                    clock = findSubsystem("Time");
                    terrain = findSubsystem("Terrain");
                    pickables = findSubsystem("Pickables");
                    ({ source, groundDrop, log, axe, planks, workbench } = savedRun);
                    trace = savedRun.trace;
                    checks = savedRun.checks;
                    prefix = savedRun.evidence;
                    scheduler = newScheduler();
                    savedRun.actorIds.forEach((id, index) => {
                        var actor = getProject().FindEntity(id);
                        actor.FindComponent("Creature", true).DisplayName = savedRun.names?.[index] ?? (index ? "禾" : "杉");
                        actors.push(actor);
                        bags.push(actor.FindComponent("Inventory", true));
                        var tools = new W.ActorTools(new W.AtomicActions(actor), false);
                        scheduler.Register(new W.ActorCognition(tools, savedRun.profiles[index]), tools);
                    });
                    scheduler.ResumeObservation();
                    scheduler.SetPaused(true);
                    connectPanel();
                    Game.ScreensManager.SwitchScreen("Game");
                    resumed = true;
                    start = Engine.Time.RealTime;
                    phase = "verifyResume";
                    return;
                }
                var settings = new Game.WorldSettings();
                settings.Name = "Two-person Cooperation Verification";
                settings.Seed = "42";
                settings.OriginalSerializationVersion = Game.VersionsManager.SerializationVersion;
                settings.ResetOptionsForNonCreativeMode(null);
                var screen = Game.ScreensManager.m_screens.get_Item("Game");
                Game.GameManager.LoadProject(Game.WorldsManager.CreateWorld(settings), screen.Children.Find("GamesWidget", true));
                var observer = new Game.PlayerData(getProject());
                observer.InputDevice = Game.WidgetInputDevice.Keyboard | Game.WidgetInputDevice.Mouse;
                findSubsystem("Players").AddPlayerData(observer);
                clock = findSubsystem("Time");
                Game.ScreensManager.SwitchScreen("Game");
                phase = "spawn";
                return;
            }
            if (phase === "spawn") {
                var players = findSubsystem("Players").ComponentPlayers;
                if (players.Count !== 1 || clock.GameTime < 4) return;
                setup(getProject(), players.get_Item(0).ComponentBody.Position);
                phase = "observe";
                return;
            }
            if (phase === "verifyResume") {
                var panel = JSON.parse(http.GetStringAsync(url).GetAwaiter().GetResult());
                if (panel.actors?.length < actors.length) return;
                check(clock.GameTime === savedRun.snapshot.time, "the second process restored the exact checkpoint tick");
                savedRun.snapshot.actors.forEach(saved => {
                    var observed = panel.actors.find(actor => actor.id === saved.id);
                    check(observed && JSON.stringify(observed.cognition.personalPlan) === JSON.stringify(saved.cognition.personalPlan)
                        && JSON.stringify(observed.cognition.waitingFor) === JSON.stringify(saved.cognition.waitingFor),
                        "personal plan and event wait restored for actor " + saved.id);
                    check((saved.cognition.recentProcess ?? []).every(entry => observed.cognition.recentProcess.some(item => item.id === entry.id)),
                        "recent personal process survived restore for actor " + saved.id);
                    if (saved.task) check(observed.task?.taskId === saved.task.view.taskId
                        && observed.task.currentStep === saved.task.view.currentStep && observed.task.completedSteps === saved.task.view.completedSteps,
                        "the saved task cursor resumes after settled steps for actor " + saved.id);
                });
                scheduler.SetPaused(false);
                phase = "observe";
                return;
            }
            var state = recordInventory();
            if (gather && !groundDrop) {
                var drop = Array.from(pickables.Pickables).find(item => item.Value === log && item.Count > 0);
                if (drop) groundDrop = { time: clock.GameTime, value: drop.Value, count: drop.Count,
                    x: drop.Position.X, y: drop.Position.Y, z: drop.Position.Z };
            }
            var madeWorkbench = gather ? state.some(bag => bag.workbenches > 0) : state[1].workbenches > 0;
            if (reload && !resumed && phase === "observe" && evidence().transfers.some(event => event.data.status === "offered")) {
                check(!madeWorkbench, "the mid-run checkpoint is requested before workbench completion");
                scheduler.RequestObservationCheckpoint();
                scheduler.StopObservationDispatch();
                checkpointRequestedAt = clock.GameTime;
                phase = "checkpoint";
            }
            if (phase === "checkpoint") {
                var checkpoint = JSON.parse(scheduler.StatusJson());
                if (checkpoint.inFlight !== 0 || checkpoint.ledgerPending || checkpoint.checkpointTime < checkpointRequestedAt) return;
                var stream = Engine.Storage.OpenFile("app:cognition-run/observation.scworld", Engine.OpenFileMode.Read);
                var directory;
                try { directory = Game.WorldsManager.ImportWorld(stream); } finally { stream.Dispose(); }
                var xml = new (importNamespace("System.Xml").XmlDocument)();
                xml.LoadXml(Engine.Storage.ReadAllText(Engine.Storage.CombinePaths(directory, "Project.xml")));
                var snapshot = JSON.parse(xml.SelectSingleNode("/Project/WestworldObservation").InnerText);
                if (snapshot.time < checkpointRequestedAt) return;
                var before = evidence();
                for (var key of ["speech", "transfers", "crafts", "gathering"]) before[key] = before[key].filter(event => event.time <= snapshot.time);
                var metadata = { directory, snapshot, source, groundDrop, log, axe, planks, workbench,
                    trace: trace.filter(entry => entry.time <= snapshot.time), checks, evidence: before,
                    actorIds: actors.map(actor => actor.Id), names: actors.map(actor => actor.FindComponent("Creature", true).DisplayName),
                    profiles: profilesByActor, elapsed: Engine.Time.RealTime - start };
                Engine.Storage.WriteAllText("app:cooperation-resume.json", JSON.stringify(metadata, null, 2));
                finish("passed", { checkpointTime: snapshot.time, actorIds: metadata.actorIds });
                return;
            }
            if (phase === "observe" && (madeWorkbench || Engine.Time.RealTime - start > (gather ? 320 : 215))) {
                scheduler.StopObservationDispatch();
                phase = "drain";
                nextPoll = Engine.Time.RealTime + 1;
            }
            if (phase !== "drain" || Engine.Time.RealTime < nextPoll) return;
            var status = JSON.parse(scheduler.StatusJson());
            if (status.inFlight !== 0 || status.ledgerPending) return;
            var report = evidence();
            var transferred = report.transfers.filter(event => event.data.status === "transferred");
            var heard = (speaker, listener) => report.speech.some(event => event.actorId === speaker
                && event.data.listeners.some(person => person.id === listener));
            var chain = gather ? gatherEvidence(report, state) : null;
            var passed = heard(actors[0].Id, actors[1].Id) && heard(actors[1].Id, actors[0].Id) && (gather ? chain.passed :
                transferred.length === 1 && transferred[0].actorId === actors[0].Id
                && transferred[0].data.recipient.id === actors[1].Id && transferred[0].data.count === 3
                && trace.some(entry => entry.bags[0].planks === 0 && entry.bags[1].planks === 4)
                && state[0].workbenches === 0 && state[1].workbenches === 1 && state.every(bag => bag.planks === 0)
                && report.crafts.some(event => event.actorId === actors[1].Id && event.data.status === "succeeded" && event.data.affectedCount === 1));
            if (reload) {
                passed = passed && resumed && report.gathering.filter(event => event.data.action.name === "Dig" && event.data.status === "succeeded").length === 1
                    && report.gathering.filter(event => event.data.action.name === "PickUp" && event.data.status === "succeeded").length === 1
                    && transferred.length === 1 && report.crafts.filter(event => event.data.status === "succeeded").length === 2;
                if (passed) checks.push("harvest, pickup, material transfer and both recipe batches settled exactly once across processes");
            }
            if (passed) {
                checks.push("both speakers were heard by the other actor");
                if (gather) checks.push("the original oak terrain block was cut, dropped a log and was picked up",
                    "a material handoff changed both backpacks before the recipient crafted one native workbench");
                else checks.push("one accepted handoff moved three planks from the giver to the recipient",
                    "the recipient consumed four planks and produced one native workbench");
            }
            Game.GameManager.SaveProject(true, true);
            finish(passed ? "passed" : "failed", { model: "gpt-6-luna", actorIds: actors.map(actor => actor.Id),
                elapsed: Engine.Time.RealTime - start + (savedRun?.elapsed ?? 0), status, ...report, ...(gather ? { chain } : {}),
                ...(reload ? { restored: resumed, checkpoint: savedRun?.snapshot } : {}) });
        } catch (error) {
            finish("failed", { error: String(error) });
        }
    });
})();
