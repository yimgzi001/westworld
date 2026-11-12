(function () {
    var W = importNamespace("Game.Westworld");
    var phase = "load", checks = [], actors = [], tools = [], bags = [];
    var clock, planks, workbench, archiveTask, started, mind, original, waitStarted, waitChecked = false;

    function check(condition, message) {
        if (!condition) throw new Error(message);
        checks.push(message);
    }
    function call(index, name, args) {
        return JSON.parse(tools[index].InvokeJson(name, JSON.stringify(args ?? {})));
    }
    function read(index, name, args) {
        var response = call(index, name, args);
        if (!response.ok) throw new Error(name + ": " + response.error);
        return response.data;
    }
    function count(index, value) {
        var total = 0;
        for (var slot = 0; slot < bags[index].SlotsCount; slot++) {
            if (bags[index].GetSlotValue(slot) === value) total += bags[index].GetSlotCount(slot);
        }
        return total;
    }
    function offer(quantity) {
        read(0, "act_OfferItem", { target: "person:" + actors[1].Id, slot: 0, value: planks, count: quantity });
        return read(1, "read_social").find(message => message.kind === "itemOffered")?.offerReference;
    }
    function respond(id, accept) {
        return read(1, "act_RespondToOffer", { offer: id, accept });
    }
    function finish(result, error) {
        phase = "done";
        Engine.Storage.WriteAllText("app:cooperation-result.json", JSON.stringify({ result, checks, error }, null, 2));
        Engine.Window.Close();
    }
    frameHandlers.push(function () {
        if (phase === "done") return;
        try {
            if (phase === "load") {
                if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu" || Game.ScreensManager.IsAnimating) return;
                var settings = new Game.WorldSettings();
                settings.Name = "Cooperation Mechanics Verification";
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
                started = Engine.Time.RealTime;
                phase = "spawn";
                return;
            }
            if (Engine.Time.RealTime - started > 70) throw new Error("mechanics verification timed out");
            if (phase === "spawn") {
                var players = findSubsystem("Players").ComponentPlayers;
                if (players.Count !== 1 || clock.GameTime < 4) return;
                var position = players.get_Item(0).ComponentBody.Position;
                planks = Game.BlocksManager.GetBlockIndex("PlanksBlock", true);
                workbench = Game.BlocksManager.GetBlockIndex("CraftingTableBlock", true);
                for (var index = 0; index < 2; index++) {
                    var actor = Game.DatabaseManager.CreateEntity(getProject(), "WestworldProducer", true);
                    actor.FindComponent("Body", true).Position = new Engine.Vector3(position.X + index * .6, position.Y + .1, position.Z + 2 - index * 1.2);
                    actor.FindComponent("Nutrition", true).Food = .8;
                    actor.FindComponent("Creature", true).DisplayName = index ? "禾" : "杉";
                    getProject().AddEntity(actor);
                    actors.push(actor);
                    bags.push(actor.FindComponent("Inventory", true));
                    tools.push(new W.ActorTools(new W.AtomicActions(actor), false));
                }
                bags[0].AddSlotItems(0, planks, 4);
                var legacyFields = JSON.parse(tools[0].DescribeToolsJson()).find(t => t.name === "submit_plan")
                    .parameters.properties.steps.items.properties;
                check(!legacyFields.interrupt_on && !legacyFields.watch_seconds,
                    "legacy worlds do not advertise a new-protocol-only interruption guard");
                read(1, "act_Speak", { text: "我在这里", mode: "normal" });
                phase = "hear";
                return;
            }
            if (phase === "hear") {
                if (read(1, "action_status").status === "running") return;
                var heard = read(0, "read_social").find(message => message.kind === "heardSpeech");
                check(heard?.sourceName === "禾" && heard.source === "person:" + actors[1].Id
                    && Number.isFinite(heard.sourceBearing) && heard.sourceHorizontalDistance < 2,
                    "actual received speech provides the speaker identity and local auditory direction while playback is muted");
                phase = "offers";
                return;
            }
            if (phase === "offers") {
                if (System.Environment.GetEnvironmentVariable("WENMING_TEST_BACKGROUND") === "1") check(!Engine.Window.IsVisible, "the native game window stays hidden");
                if (System.Environment.GetEnvironmentVariable("WENMING_TEST_MUTE") === "1") check(Game.SettingsManager.SoundsVolume === 0 && Game.SettingsManager.MusicVolume === 0, "game effects and music stay muted");
                mind = new W.ActorCognition(tools[0], "Observe the nearby person.");
                var context = JSON.parse(mind.PrepareTurn());
                var partner = context.observation.entities.find(person => person.reference === "person:" + actors[1].Id);
                var own = actors[0].FindComponent("Body", true).Position;
                var other = actors[1].FindComponent("Body", true).Position;
                var expectedDistance = Math.hypot(other.X - own.X, other.Z - own.Z);
                check(partner?.supportsItemOffers && Number.isFinite(partner.bearing) && Math.abs(partner.horizontalDistance - expectedDistance) < .01,
                    "a genuinely visible partner includes a movement bearing and actual horizontal distance");
                var observer = findSubsystem("Players").ComponentPlayers.get_Item(0).ComponentBody;
                var observedPlayer = context.observation.entities.find(person => person.reference === "person:" + observer.Entity.Id);
                check(observedPlayer && !observedPlayer.supportsItemOffers,
                    "the visible observation player is distinguished from supported handoff recipients");
                var installed = JSON.parse(mind.CommitTurn(context.id, JSON.stringify({ reason: "Plan my material delivery", task: "keep",
                    goal: "Deliver four planks", reviewAfter: .1,
                    plan: { operation: "create", purpose: "Deliver four planks", next: "Offer the planks", completion: "I receive a transfer receipt", source: "observation" } })));
                check(installed.ok, "a personal plan is installed with the actor's own stated goal");
                original = offer(4);
                check(original != null, "the recipient actually receives the original offer");
                read(0, "act_OfferItem", { target: "person:" + actors[1].Id, slot: 0, value: planks, count: 4 });
                check(read(1, "read_social").length === 0, "an identical live offer does not generate duplicate messages");
                check(count(0, planks) === 4 && count(1, planks) === 0, "offering and retrying do not transfer any material");
                phase = "accept";
                return;
            }
            if (phase === "accept") {
                var input = mind.PrepareTurn();
                if (!input) return;
                var ownContext = JSON.parse(input);
                var pending = ownContext.observation.production.outgoingOffers;
                check(pending.length === 1 && pending[0].offerReference === original && pending[0].count === 4
                    && pending[0].itemValue === planks && pending[0].recipient === "person:" + actors[1].Id
                    && pending[0].secondsRemaining > 0,
                    "the sender's next decision sees its own pending offer with recipient and actual quantity");
                check(ownContext.personalPlan.version === 1 && ownContext.personalPlan.status === "active", "the personal plan is present in the next decision");
                var dropped = JSON.parse(mind.CommitTurn(ownContext.id, JSON.stringify({ reason: "Change the goal silently", task: "keep", goal: "Craft alone" })));
                check(!dropped.ok && dropped.error === "goal_change_requires_plan_revision", "an active plan cannot silently lose its goal");
                var waiting = JSON.parse(mind.CommitTurn(ownContext.id, JSON.stringify({ reason: "Await the real transfer", task: "keep", reviewAfter: .1,
                    wait: { kind: "itemsTransferred", person: "person:" + actors[1].Id, offer: original, itemValue: planks, count: 4, timeout: 10 } })));
                check(waiting.ok, "the actor chooses to wait for a specific transfer event");
                waitStarted = clock.GameTime;
                phase = "waiting";
                return;
            }
            if (phase === "waiting") {
                if (clock.GameTime < waitStarted + .2) return;
                check(mind.PrepareTurn() == null, "an unresolved event wait suppresses repeated review calls");
                check(respond(original, true).status === "succeeded", "the original offer remains acceptable after retry");
                check(count(0, planks) === 0 && count(1, planks) === 4, "one acceptance transfers exactly four planks");
                check(respond(original, true).status === "failed" && count(1, planks) === 4, "repeated acceptance cannot transfer twice");
                // Test a genuine replacement and a declined offer using the same native inventories.
                bags[0].AddSlotItems(0, planks, 2);
                var replaced = offer(1), changed = offer(2);
                check(changed !== replaced && respond(replaced, true).status === "failed", "changing the quantity replaces the old offer");
                check(respond(changed, false).status === "succeeded" && count(0, planks) === 2, "declining preserves ownership");
                bags[0].RemoveSlotItems(0, 2);
                phase = "eventResult";
                return;
            }
            if (phase === "eventResult") {
                if (!waitChecked) {
                    var eventInput = mind.PrepareTurn();
                    if (!eventInput) return;
                    var eventContext = JSON.parse(eventInput);
                    check(eventContext.reasons.includes("eventWait") && eventContext.waitingFor.status === "satisfied"
                        && eventContext.waitingFor.matchedMessage != null, "the actual transfer receipt wakes the sender exactly through the selected event");
                    check(eventContext.recentProcess.some(entry => entry.kind === "received" && entry.details.value.kind === "itemsTransferred"),
                        "recent process input retains the personally received transfer result");
                    var revised = JSON.parse(mind.CommitTurn(eventContext.id, JSON.stringify({ reason: "The material transfer is confirmed", task: "keep", reviewAfter: .1,
                        handled: eventContext.messages.map(message => message.id),
                        personalPlan: { operation: "revise", version: 1, purpose: "Deliver four planks", next: "Record the actual handoff",
                            completion: "I receive a transfer receipt", source: eventContext.recentProcess.find(entry => entry.kind === "received").id } })));
                    check(revised.ok, "plan revisions accept owned recent evidence and the personalPlan update alias");
                    waitChecked = true;
                }
                actors[0].FindComponent("Body", true).Rotation = Engine.Quaternion.CreateFromAxisAngle(Engine.Vector3.UnitY, Math.PI);
                phase = "knownPerson";
                return;
            }
            if (phase === "knownPerson") {
                var retainedInput = mind.PrepareTurn();
                if (!retainedInput) return;
                var retained = JSON.parse(retainedInput);
                check(!retained.messages.some(message => message.value.source === "person:" + actors[1].Id)
                    && !retained.observation.entities.some(person => person.reference === "person:" + actors[1].Id),
                    "the old partner is known only through retained personal experience");
                var oldMessage = retained.recentProcess.find(entry => entry.kind === "received");
                var known = JSON.parse(mind.CommitTurn(retained.id, JSON.stringify({ reason: "Check the known recipient", task: "start", reviewAfter: 10,
                    remember: { source: oldMessage.details.id, text: "The partner received the planks." },
                    steps: [{ tool: "act_OfferItem", arguments: { target: "person:" + actors[1].Id, slot: 0, value: planks, count: 1 } }] })));
                check(known.ok, "owned recent messages remain valid evidence and authorize a known counterpart");
                phase = "prepareCraft";
                return;
            }
            if (phase === "prepareCraft") {
                if (["waiting", "running", "blocked"].includes(read(0, "task_status").status)) return;
                check(count(0, planks) === 0 && count(1, planks) === 4, "the resource check rejects a new empty-handed offer without any transfer");
                var args = { workspace: "personal", result_value: workbench, prepare: true };
                var guarded = JSON.parse(tools[1].InvokeJson("submit_plan", JSON.stringify({ steps: [
                    { tool: "act_Craft", arguments: args, interrupt_on: "body_worsened" }
                ] })));
                check(!guarded.ok && guarded.error === "invalid_arguments", "prepared crafting rejects an unsupported interruption guard instead of silently dropping it");
                var missing = call(0, "submit_plan", { steps: [{ tool: "act_Craft", arguments: args }] });
                check(!missing.ok && missing.error === "plan_unavailable", "crafting without ingredients fails before changing inventories");
                var plan = read(1, "submit_plan", { steps: [{ tool: "act_Craft", arguments: args }] });
                check(plan.totalSteps === 5 && count(1, planks) === 4, "one craft request compiles to four transfers and one craft without effects at submission");
                phase = "save";
                return;
            }
            if (phase === "save") {
                var task = read(1, "task_status");
                if (task.status !== "waiting" || task.completedSteps < 1 || task.completedSteps >= 4) return;
                var archive = "app:cooperation-craft.scworld";
                archiveTask = task;
                tools[1].SaveTaskCheckpoint(archive);
                var stream = Engine.Storage.OpenFile(archive, Engine.OpenFileMode.Read);
                var directory;
                try { directory = Game.WorldsManager.ImportWorld(stream); } finally { stream.Dispose(); }
                var gameScreen = Game.ScreensManager.m_screens.get_Item("Game");
                Game.GameManager.LoadProject(Game.WorldsManager.GetWorldInfo(directory), gameScreen.Children.Find("GamesWidget", true));
                var recipient = getProject().FindEntity(actors[1].Id);
                actors[1] = recipient;
                bags[1] = recipient.FindComponent("Inventory", true);
                tools[1] = new W.ActorTools(new W.AtomicActions(recipient), false);
                check(tools[1].RestoreTaskCheckpoint(), "a partially filled craft task restores from its archive");
                var restored = read(1, "task_status");
                check(restored.taskId === archiveTask.taskId && restored.completedSteps === archiveTask.completedSteps,
                    "restore keeps the original task and completed transfer cursor");
                Game.ScreensManager.SwitchScreen("Game");
                phase = "finish";
                return;
            }
            if (phase === "finish") {
                var ended = read(1, "task_status");
                if (["waiting", "running", "blocked"].includes(ended.status)) return;
                check(ended.status === "succeeded" && count(1, workbench) === 1 && count(1, planks) === 0,
                    "restored craft consumes four planks and creates exactly one workbench");
                check(ended.steps.every(step => step.attempts === 1), "completed transfers and crafting execute once across restore");
                finish("passed");
            }
        } catch (error) { finish("failed", String(error)); }
    });
})();
