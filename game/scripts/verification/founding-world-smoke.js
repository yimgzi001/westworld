(function () {
    var W = importNamespace("Game.Westworld");
    var phase = "load";
    var checks = [];
    var reloading = Engine.Storage.FileExists("app:founding-baseline.json");
    var saving = Engine.Storage.FileExists("app:founding-save");
    var resuming = Engine.Storage.FileExists("app:founding-resume");
    var quota = Engine.Storage.FileExists("app:founding-quota");
    var world, archive, initial, player, flightStart, actors, actionTime, farDistance, camp, eaten, moved, saveAfter, nextSample = 0;
    var pauseTime, firstFees, firstUnknown, quotaReport, queuedTurn, quotaRun;
    var accountPath = "app:doc/Westworld/quota-account.json";

    function check(condition, description) {
        if (!condition) throw new Error(description);
        checks.push(description);
    }

    function finish(result, detail) {
        phase = "done";
        var file = resuming ? "app:founding-resume-result.json" : "app:founding-result.json";
        Engine.Storage.WriteAllText(file, JSON.stringify({ result, checks, ...detail }, null, 2));
    }

    function founders() {
        return Array.from(getProject().Entities).filter(entity => entity.ValuesDictionary.DatabaseObject.Name.startsWith("WestworldFounder"))
            .sort((a, b) => a.Id - b.Id);
    }

    function position(vector) { return [vector.X, vector.Y, vector.Z]; }

    function fly(up, forward, right = 0) {
        // Feed the flight controller and the same player terrain location normally maintained by drawing.
        var locomotion = player.ComponentLocomotion;
        locomotion.WalkOrder = new Engine.Vector2(right, forward);
        locomotion.FlyOrder = new Engine.Vector3(0, up, 0);
        locomotion.NormalMovement(findSubsystem("Time").GameTimeDelta);
        findSubsystem("Terrain").TerrainUpdater.SetUpdateLocation(player.PlayerData.PlayerIndex,
            player.ComponentBody.Position.XZ, findSubsystem("Sky").VisibilityRange, 64);
    }

    function flyToward(x, z) {
        var body = player.ComponentBody;
        var dx = x - body.Position.X, dz = z - body.Position.Z;
        var distance = Math.hypot(dx, dz);
        var forward = body.Matrix.Forward;
        var right = body.Matrix.Right;
        fly(0, (dx * forward.X + dz * forward.Z) / distance, (dx * right.X + dz * right.Z) / distance);
    }

    function snapshot() {
        var entities = founders();
        check(entities.length === 50, "the loaded survival world contains exactly 50 founders");
        check(entities.filter(entity => entity.ValuesDictionary.DatabaseObject.Name === "WestworldFounderFemale").length === 25,
            "25 founders use the female body and 25 use the male body");
        check(entities.every(entity => !entity.FindComponent("Player", false) && !entity.FindComponent("Locomotion", true).IsCreativeFlyEnabled),
            "founders are separate from player slots and cannot fly");
        check(findSubsystem("GameInfo").WorldSettings.GameMode === Game.GameMode.Survival, "native survival rules are active");
        return entities.map(entity => {
            var body = entity.FindComponent("Body", true);
            var inventory = entity.FindComponent("Inventory", true);
            return { id: entity.Id, template: entity.ValuesDictionary.DatabaseObject.Name, position: position(body.Position),
                forward: position(body.Matrix.Forward), food: entity.FindComponent("Nutrition", true).Food,
                health: entity.FindComponent("Health", true).Health, speed: entity.FindComponent("Locomotion", true).WalkSpeed,
                items: Array.from({ length: inventory.SlotsCount }, (_, slot) => [inventory.GetSlotValue(slot), inventory.GetSlotCount(slot)]) };
        });
    }

    function loadArchive(path) {
        var stream = Engine.Storage.OpenFile(path, Engine.OpenFileMode.Read);
        try { world = Game.WorldsManager.GetWorldInfo(Game.WorldsManager.ImportWorld(stream)); }
        finally { stream.Dispose(); }
        var screen = Game.ScreensManager.m_screens.get_Item("Game");
        Game.GameManager.LoadProject(world, screen.Children.Find("GamesWidget", true));
        return snapshot();
    }

    function toggleFlight(component) {
        var input = new Game.PlayerInput();
        input.ToggleCreativeFly = true;
        component.ComponentInput.m_playerInput = input;
        component.ComponentGui.HandleInput();
    }

    function groundCell(body) {
        var p = body.Position;
        return new Engine.Point3(Math.floor(p.X), Math.floor(p.Y - 0.25), Math.floor(p.Z));
    }

    function breadCounts() {
        return founders().map(entity => entity.FindComponent("Inventory", true).GetSlotCount(0));
    }

    function sameSavedActors(restored, saved) {
        function close(a, b) { return Math.abs(a - b) < .0001; }
        return restored.length === saved.length && restored.every((actor, index) => {
            var previous = saved[index];
            return actor.id === previous.id && actor.template === previous.template
                && JSON.stringify(actor.items) === JSON.stringify(previous.items)
                && close(actor.food, previous.food) && close(actor.health, previous.health) && close(actor.speed, previous.speed)
                && actor.position.every((value, axis) => close(value, previous.position[axis]))
                && actor.forward.every((value, axis) => close(value, previous.forward[axis]));
        });
    }

    function quotaPause(reason) {
        var xml = Engine.Storage.ReadAllText(Engine.Storage.CombinePaths(world.DirectoryName, "Project.xml"));
        var match = xml.match(/<WestworldObservation>([^<]+)<\/WestworldObservation>/);
        var checkpoint = match && JSON.parse(match[1]);
        return checkpoint?.quotaPause?.reason === reason && checkpoint;
    }

    function requireQuotaPause(reason) {
        var checkpoint = quotaPause(reason);
        if (!checkpoint && Engine.Time.RealTime > actionTime + 30) throw new Error("the experiment did not save a " + reason + " pause");
        return checkpoint;
    }

    function quotaAccount() {
        return JSON.parse(Engine.Storage.ReadAllText(accountPath));
    }

    function increaseQuota(feeNanoUsd, storageBytes) {
        Engine.Storage.WriteAllText("app:doc/Westworld/quota-limits.json", JSON.stringify({ feeNanoUsd, storageBytes }));
    }

    function quotaLedger() {
        var path = "app:doc/Westworld/" + quotaRun + "/cognition/ledger.json";
        return JSON.parse(Engine.Storage.ReadAllText(path));
    }

    function simulationTime() { return findSubsystem("Time").GameTime; }

    frameHandlers.push(function () {
        if (phase === "done") return;
        try {
            if (phase === "load") {
                if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu"
                    || Game.ScreensManager.IsAnimating) return;
                if (resuming) {
                    var saved = JSON.parse(Engine.Storage.ReadAllText("app:founding-result.json"));
                    world = Game.WorldsManager.GetWorldInfo(saved.worldDirectory);
                    var screen = Game.ScreensManager.m_screens.get_Item("Game");
                    Game.GameManager.LoadProject(world, screen.Children.Find("GamesWidget", true));
                    if (!quota) check(sameSavedActors(snapshot(), saved.actors), "the same save retained all 50 founders and their settled food effects");
                    check(simulationTime() === saved.time, "the same save restored the saved simulation time");
                    Game.ScreensManager.SwitchScreen("Game");
                    actionTime = Engine.Time.RealTime;
                    pauseTime = saved.time;
                    phase = quota ? "quota-reopen" : "resume-task";
                } else if (reloading) {
                    var baseline = JSON.parse(Engine.Storage.ReadAllText("app:founding-baseline.json"));
                    initial = loadArchive("app:initial.scworld");
                    check(JSON.stringify(initial) === JSON.stringify(baseline.initial), "a new process restored the same founder identities, bodies and inventories");
                    camp = baseline.camp;
                    if (saving) {
                        founders().forEach((entity, index) => {
                            var inventory = entity.FindComponent("Inventory", true);
                            inventory.AddSlotItems(0, inventory.GetSlotValue(0), 2);
                            entity.FindComponent("Nutrition", true).Food = .1;
                            if (index === 1) entity.FindComponent("Locomotion", true).TurnSpeed = .1;
                        });
                    }
                    Game.ScreensManager.SwitchScreen("Game");
                    phase = saving ? "save-task" : quota ? "quota-first" : "player";
                    actionTime = Engine.Time.RealTime;
                } else {
                    var settings = new Game.WorldSettings();
                    settings.Name = "Westworld Founding Verification";
                    settings.Seed = "42";
                    world = W.FoundingWorld.Create(settings);
                    archive = W.FoundingWorld.InitialArchivePath(world);
                    Game.ScreensManager.SwitchScreen("GameLoading", world, null);
                    phase = "founding";
                }
                return;
            }
            if (phase === "founding") {
                if (!Engine.Storage.FileExists(archive)) return;
                check(founders().length === 50, "new world generated exactly 50 AI founders");
                var owner = findSubsystem("Players").ComponentPlayers.get_Item(0);
                camp = position(owner.ComponentBody.Position);
                initial = loadArchive(archive);
                finish("passed", { archive, initial, camp });
                return;
            }
            if (phase === "save-task") {
                var counts = breadCounts();
                if (counts.some(count => count < 2)) throw new Error("a founder consumed another portion before closing");
                if (!counts.every(count => count === 2)) return;
                var time = findSubsystem("Time").GameTime;
                saveAfter ??= time + 1.8;
                if (time < saveAfter) return;
                checks.push("all 50 founders consumed exactly one portion before closing");
                finish("passed", { worldDirectory: findSubsystem("GameInfo").DirectoryName, time, actors: snapshot() });
                Engine.Window.Close();
                return;
            }
            if (phase === "resume-task") {
                var counts = breadCounts();
                if (counts.every(count => count === 1) || Engine.Time.RealTime >= actionTime + 40) {
                    check(counts.every(count => count === 1),
                        "all 50 founders continued the existing task and consumed exactly one further portion (" +
                        counts.filter(count => count === 1).length + " did so)");
                    finish("passed", { time: findSubsystem("Time").GameTime, counts, actors: snapshot() });
                    Engine.Window.Close();
                }
                return;
            }
            if (phase === "quota-first") {
                var first = requireQuotaPause("budget");
                if (!first) return;
                var firstRun = Object.values(quotaAccount().runs)[0];
                firstFees = firstRun.spent;
                firstUnknown = firstRun.unknown;
                check(firstFees > 0 && firstUnknown > 0 && first.time === simulationTime(),
                    "the first experiment saved its pause with both actual fees and an unknown reservation");
                var firstDirectory = world.DirectoryName;
                loadArchive("app:initial.scworld");
                check(world.DirectoryName !== firstDirectory, "the second experiment has an independent native world");
                actionTime = Engine.Time.RealTime;
                phase = "quota-second";
                return;
            }
            if (phase === "quota-second") {
                var second = requireQuotaPause("budget");
                if (!second) return;
                var account = quotaAccount();
                var runs = Object.values(account.runs);
                var combined = runs.reduce((sum, run) => sum + run.spent + run.unknown + run.reserved, 0);
                check(runs.length === 2 && combined >= firstFees + firstUnknown && combined <= account.feeNanoUsd
                    && runs.some(run => run.unknown === firstUnknown)
                    && account.feeNanoUsd - combined < second.quotaPause.required && combined - firstFees - firstUnknown < firstFees,
                    "the independent second experiment reached the remaining allowance from the shared account");
                queuedTurn = second.actors.find(actor => actor.turn && !actor.turn.attemptId)?.turn?.input?.context?.id;
                check(!!queuedTurn, "the next unsubmitted decision was saved with its original input");
                quotaRun = Object.keys(account.runs).find(key => account.runs[key].experimentId === second.experimentId);
                pauseTime = second.time;
                quotaReport = { firstFees, firstUnknown, combined, budgetPause: pauseTime };
                actionTime = Engine.Time.RealTime;
                phase = "quota-budget-frozen";
                return;
            }
            if (phase === "quota-budget-frozen") {
                if (Engine.Time.RealTime < actionTime + 1.1) return;
                check(simulationTime() === pauseTime, "model allowance stopped the simulation clock");
                increaseQuota(500000000, undefined);
                actionTime = Engine.Time.RealTime;
                phase = "quota-budget-resume";
                return;
            }
            if (phase === "quota-budget-resume") {
                if (simulationTime() <= pauseTime) {
                    if (Engine.Time.RealTime > actionTime + 10) throw new Error("the fee top-up did not resume the world");
                    return;
                }
                check(simulationTime() < pauseTime + .25 && quotaAccount().feeNanoUsd === 500000000,
                    "raising the fee allowance resumed from the saved tick");
                actionTime = Engine.Time.RealTime;
                phase = "quota-budget-turn";
                return;
            }
            if (phase === "quota-budget-turn") {
                if (Engine.Time.RealTime < actionTime + 1) return;
                check(quotaLedger().attempts.some(attempt => attempt.batchId === queuedTurn && attempt.state === "completed"),
                    "the original queued decision ran with the same turn ID after the top-up");
                var sample = Engine.Storage.OpenFile("app:doc/Westworld/quota-test-data.bin", Engine.OpenFileMode.Create);
                try { sample.SetLength(45000000); }
                finally { sample.Dispose(); }
                actionTime = Engine.Time.RealTime;
                phase = "quota-storage";
                return;
            }
            if (phase === "quota-storage") {
                var stopped = requireQuotaPause("storage");
                if (!stopped) return;
                pauseTime = stopped.time;
                actionTime = Engine.Time.RealTime;
                phase = "quota-storage-frozen";
                return;
            }
            if (phase === "quota-storage-frozen") {
                if (Engine.Time.RealTime < actionTime + 1.1) return;
                check(simulationTime() === pauseTime, "managed storage growth stopped the world while the pause save still fit");
                finish("passed", { ...quotaReport, worldDirectory: world.DirectoryName, time: pauseTime });
                Engine.Window.Close();
                return;
            }
            if (phase === "quota-reopen") {
                if (Engine.Time.RealTime < actionTime + 3) return;
                check(simulationTime() === pauseTime && quotaPause("storage"), "a new process kept the original disk pause and simulation tick");
                check(quotaAccount().feeNanoUsd === 500000000 && Object.values(quotaAccount().runs).some(run => run.unknown > 0),
                    "reopening kept the previous fee top-up and unknown reservation");
                increaseQuota(undefined, 200000000);
                actionTime = Engine.Time.RealTime;
                phase = "quota-storage-resume";
                return;
            }
            if (phase === "quota-storage-resume") {
                if (simulationTime() <= pauseTime) {
                    if (Engine.Time.RealTime > actionTime + 10) throw new Error("the storage top-up did not resume the world");
                    return;
                }
                check(simulationTime() < pauseTime + .25 && quotaAccount().storageBytes === 200000000,
                    "raising storage allowance resumed from the original saved tick");
                finish("passed", { time: simulationTime(), resumedFrom: pauseTime });
                Engine.Window.Close();
                return;
            }
            var players = findSubsystem("Players");
            if (phase === "player") {
                if (players.ComponentPlayers.Count !== 1 || Game.ScreensManager.IsAnimating) return;
                player = players.ComponentPlayers.get_Item(0);
                if (!player.PlayerData.IsReadyForPlaying || !player.ComponentGui.m_creativeFlyButtonWidget.IsVisible) return;
                var guest = new Game.PlayerData(getProject());
                guest.InputDevice = Game.WidgetInputDevice.GamePad1;
                players.AddPlayerData(guest);
                phase = "guest";
                return;
            }
            if (phase === "guest") {
                if (players.ComponentPlayers.Count !== 2) return;
                var guestPlayer = players.ComponentPlayers.get_Item(1);
                if (!guestPlayer.PlayerData.IsReadyForPlaying) return;
                toggleFlight(guestPlayer);
                check(!guestPlayer.ComponentLocomotion.IsCreativeFlyEnabled && !guestPlayer.ComponentGui.m_creativeFlyButtonWidget.IsVisible,
                    "the second local player cannot use the survival flight control");
                players.RemovePlayerData(guestPlayer.PlayerData);
                Game.SettingsManager.HorizontalCreativeFlight = true;
                player.ComponentInput.IsControlledByTouch = false;
                toggleFlight(player);
                check(player.ComponentLocomotion.IsCreativeFlyEnabled, "the normal flight control toggled flight for the original survival player");
                var time = findSubsystem("Time");
                time.BasicGameTimeFactor = 2;
                time.MaxGameTimeDelta = Math.min(time.MaxGameTimeDelta, .025);
                flightStart = player.ComponentBody.Position.Y;
                phase = "ascend";
                return;
            }
            if (phase === "ascend") {
                if (player.ComponentBody.Position.Y < flightStart + 100) {
                    fly(1, 0);
                } else {
                    phase = "outbound";
                }
            }
            if (player && Engine.Time.RealTime >= nextSample) {
                nextSample = Engine.Time.RealTime + 2;
                Engine.Storage.WriteAllText("app:founding-progress.json", JSON.stringify({ phase,
                    position: position(player.ComponentBody.Position), camp, flight: player.ComponentLocomotion.IsCreativeFlyEnabled, moved, eaten }));
            }
            if (phase === "outbound") {
                var p = player.ComponentBody.Position;
                var distance = Math.hypot(p.X - camp[0], p.Z - camp[2]);
                var beyondView = Math.max(64, findSubsystem("Sky").VisibilityRange) + 130;
                if (distance < beyondView) {
                    flyToward(camp[0] + beyondView + 20, camp[2]);
                    return;
                }
                farDistance = distance;
                actionTime = findSubsystem("Time").GameTime;
                var terrain = findSubsystem("Terrain").Terrain;
                var entities = founders();
                check(entities.every(entity => terrain.GetCellContents(groundCell(entity.FindComponent("Body", true))) !== 0),
                    "all 50 founders still have loaded camp terrain outside the player's loading range");
                actors = entities.map(entity => {
                    var body = entity.FindComponent("Body", true);
                    var nutrition = entity.FindComponent("Nutrition", true);
                    return { body, nutrition, inventory: entity.FindComponent("Inventory", true), origin: position(body.Position),
                        food: nutrition.Food, moved: false };
                });
                check(actors.every(actor => actor.inventory.GetSlotCount(0) === 1), "no founder ate the test food before departure");
                Engine.Storage.WriteAllText("app:founding-player-away", "ready");
                phase = "far-action";
            }
            if (phase === "far-action") {
                actors.forEach(actor => {
                    var p = actor.body.Position;
                    actor.moved ||= Math.hypot(p.X - actor.origin[0], p.Z - actor.origin[2]) > 0.35;
                });
                moved = actors.filter(actor => actor.moved).length;
                eaten = actors.filter(actor => actor.inventory.GetSlotCount(0) === 0 && actor.nutrition.Food > actor.food).length;
                if ((eaten < 50 || moved < 40) && findSubsystem("Time").GameTime < actionTime + 12) return;
                check(eaten >= 40, "at least 40 distant founders consumed real inventory food and raised their food reserve");
                check(moved >= 25, "at least 25 distant founders walked on fixed model decisions");
                phase = "return";
            }
            if (phase === "return") {
                var p = player.ComponentBody.Position;
                var distance = Math.hypot(p.X - camp[0], p.Z - camp[2]);
                if (distance > Math.max(64, findSubsystem("Sky").VisibilityRange) - 8) {
                    flyToward(camp[0], camp[2]);
                    return;
                }
                check(founders().length === 50 && actors.filter(actor => actor.inventory.GetSlotCount(0) === 0).length >= eaten,
                    "returning to camp preserved the founder count and the settled inventory result");
                finish("passed", { founderCount: 50, farDistance, moved, eaten, returnedDistance: distance });
            }
        } catch (error) {
            finish("failed", { phase, farDistance, moved, eaten, error: String(error) });
        }
    });
})();
