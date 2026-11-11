(function () {
    var W = importNamespace("Game.Westworld");
    var phase = "load", checks = [], actor, inventory, log, planks, terrain, cell, axe, result, started, food;
    var gathered = 0, madePlanks = 0;
    var reload = Engine.Storage.FileExists("app:solo-reload-during-dig"), savedDig = null;

    function check(condition, detail) {
        if (!condition) throw new Error(detail);
        checks.push(detail);
    }

    function finish(outcome, details) {
        var report = { result: outcome, phase, checks, ...details };
        phase = "done";
        Engine.Storage.WriteAllText("app:solo-production-result.json", JSON.stringify(report, null, 2));
    }

    function count(contents) {
        return Array.from({ length: inventory.SlotsCount }, (_, slot) => slot)
            .filter(slot => Game.Terrain.ExtractContents(inventory.GetSlotValue(slot)) === contents)
            .reduce((total, slot) => total + inventory.GetSlotCount(slot), 0);
    }

    function producer() {
        return Array.from(getProject()?.Entities ?? []).filter(entity => entity.ValuesDictionary.DatabaseObject.Name === "WestworldProducer");
    }

    function savedObservation(directory) {
        var xml = Engine.Storage.ReadAllText(Engine.Storage.CombinePaths(directory, "Project.xml"));
        return JSON.parse(xml.match(/<WestworldObservation>([^<]+)<\/WestworldObservation>/)[1]);
    }

    frameHandlers.push(function () {
        if (phase === "done") return;
        try {
            if (phase === "load") {
                if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu" || Game.ScreensManager.IsAnimating) return;
                if (typeof W.FoundingWorld.CreateSolo !== "function") throw new Error("the single-producer founding entry is missing");
                var settings = new Game.WorldSettings();
                settings.Name = "Solo Production Verification";
                settings.Seed = "42";
                Game.ScreensManager.SwitchScreen("GameLoading", W.FoundingWorld.CreateSolo(settings), null);
                phase = "setup";
                return;
            }
            if (phase === "setup") {
                var producers = producer();
                if (!producers.length) return;
                check(producers.length === 1, "a native survival world has exactly one AI producer");
                actor = producers[0];
                inventory = actor.FindComponent("Inventory", true);
                var body = actor.FindComponent("Body", true);
                var x = Math.floor(body.Position.X), y = Math.floor(body.Position.Y), z = Math.floor(body.Position.Z);
                terrain = findSubsystem("Terrain");
                log = Game.BlocksManager.GetBlockIndex("OakWoodBlock", true);
                axe = Game.BlocksManager.GetBlockIndex("StoneAxeBlock", true);
                result = Game.BlocksManager.GetBlockIndex("CraftingTableBlock", true);
                planks = Game.BlocksManager.GetBlockIndex("PlanksBlock", true);
                check(count(log) === 0 && count(planks) === 0 && count(result) === 0 && count(axe) === 1,
                    "the producer starts with a stone axe and without production materials or output");
                for (var dx = -5; dx <= 5; dx++) {
                    for (var dz = -6; dz <= 5; dz++) {
                        terrain.ChangeCell(x + dx, y - 1, z + dz, 1, true, null);
                        for (var dy = 0; dy < 4; dy++) terrain.ChangeCell(x + dx, y + dy, z + dz, 0, true, null);
                    }
                }
                body.Rotation = Engine.Quaternion.Identity;
                cell = new Engine.Point3(x, y, z - 3);
                terrain.ChangeCell(cell.X, cell.Y, cell.Z, log, true, null);
                food = actor.FindComponent("Nutrition", true);
                food.Food = .15;
                started = Engine.Time.RealTime;
                phase = "produce";
                return;
            }
            if (phase === "produce") {
                if (reload && !savedDig && actor.FindComponent("Miner", true).DigCellFace != null) {
                    var directory = findSubsystem("GameInfo").DirectoryName;
                    Game.GameManager.SaveProject(true, true);
                    var before = savedObservation(directory);
                    savedDig = before.actors[0].task;
                    check(savedDig.view.status === "running" && savedDig.continuation.$type === "dig"
                        && terrain.Terrain.GetCellContents(cell.X, cell.Y, cell.Z) === log,
                        "the native save captured an in-progress dig before the log was removed");
                    var screen = Game.ScreensManager.m_screens.get_Item("Game");
                    Game.GameManager.LoadProject(Game.WorldsManager.GetWorldInfo(directory), screen.Children.Find("GamesWidget", true));
                    check(findSubsystem("Time").GameTime === before.time, "the same world reopened at the mining save tick");
                    actor = producer()[0];
                    inventory = actor.FindComponent("Inventory", true);
                    food = actor.FindComponent("Nutrition", true);
                    terrain = findSubsystem("Terrain");
                    return;
                }
                gathered = Math.max(gathered, count(log));
                madePlanks = Math.max(madePlanks, count(planks));
                if (count(result) === 0) {
                    if (Engine.Time.RealTime - started > 210) throw new Error("the actor did not craft the native workbench");
                    return;
                }
                check(terrain.Terrain.GetCellContents(cell.X, cell.Y, cell.Z) === 0, "the original log was removed from the world");
                check(inventory.GetSlotValue(1) !== axe && Game.Terrain.ExtractContents(inventory.GetSlotValue(1)) === axe,
                    "using the stone axe consumed native tool durability");
                check(gathered === 1 && madePlanks === 4, "the actor collected one dropped log and made four planks");
                check(count(result) === 1 && count(log) === 0 && count(planks) === 0,
                    "one native workbench is in the backpack and the materials were consumed");
                check(food.Food > .15, "the same producer ate from its own inventory");
                if (reload) check(savedDig != null, "production completed after the mid-dig save and reload");
                Game.GameManager.SaveProject(true, true);
                finish("passed", { gameTime: findSubsystem("Time").GameTime, elapsed: Engine.Time.RealTime - started });
                Engine.Window.Close();
            }
        } catch (error) {
            finish("failed", { error: String(error) });
        }
    });
})();
