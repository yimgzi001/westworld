(function () {
    var W = importNamespace("Game.Westworld");
    var System = importNamespace("System");
    var Diagnostics = importNamespace("System.Diagnostics");
    var Templates = importNamespace("TemplatesDatabase");
    var mode = Engine.Storage.ReadAllText("app:global-mode").trim();
    var interruptLedger = mode === "observe-ledger-interrupt";
    var recoverLedger = mode === "observe-ledger-recover";
    var faultLedger = mode === "observe-ledger-write-fault";
    var interruptWorld = mode === "observe-world-interrupt", resumeWorld = mode === "observe-world-resume";
    var worldRecovery = interruptWorld || resumeWorld;
    var fixedObservation = mode === "observe-ahead" || interruptLedger || recoverLedger || faultLedger || worldRecovery;
    var observation = mode === "live-observe" || fixedObservation;
    var forage = mode === "live-forage" || observation;
    var live = mode === "live" || mode === "live-session" || mode === "live-forage" || mode === "live-observe";
    var exercise = mode === "live" || mode === "scale-action";
    var sustained = mode === "live-session" || forage;
    var actorCount = 50, parallelism = exercise || sustained ? 15 : 5;
    var inputPrice = live ? 100 : 10, outputPrice = live ? 500 : 20;
    var sessionSeconds = forage ? 45 : 20, sessionBudget = forage ? 500000000 : 250000000;
    var ledgerPath = "app:cognition-run/ledger.json";
    var actors = [], bodies = [], health = [], minds = [], tools = [], checks = [];
    var project, time, scheduler, phase = "load", budgetRaised;
    var sessionStart, sessionStartedAt, sessionEnd, finalStatus;
    var lastSample = -1, actorActivity = [];
    var nutrition = [], foodDrops = [];
    var observationElapsed, inFlightAtCutoff, observedGameTime, advancingWithRequests = 0, advancingWithTasks = 0;
    var inFlightSamples = new Map(), movedDuringOwnRequest = new Map();
    var recoveryTask, ledgerBeforeRecovery;
    var interruption, requestedCheckpoint, resumedDecision;
    var lastObservationFrameAt, observationFrameGaps = [];

    function check(condition, label) {
        if (!condition) throw new Error(label);
        checks.push(label);
    }

    function finish(result, detail) {
        phase = "done";
        Engine.Storage.WriteAllText("app:global-cognition-result.json", JSON.stringify({
            result, detail, checks, actorCount: actors.length, processId: System.Environment.ProcessId
        }, null, 2));
    }

    function spawnActors(origin) {
        if (forage) { spawnForagers(origin); return; }
        // Assemble a test body from native components, without a player slot or animal behaviors.
        var human = Game.DatabaseManager.FindEntityValuesDictionary("MalePlayer", true);
        var creature = Game.DatabaseManager.FindEntityValuesDictionary("Werewolf", true);
        var template = new Templates.ValuesDictionary();
        template.DatabaseObject = human.DatabaseObject;
        for (var name of ["Body", "Health", "HumanModel", "Locomotion", "OnFire", "Spawn", "CreatureSounds"])
            template.m_dictionary.set_Item(name, human.m_dictionary.get_Item(name));
        for (var name of ["Creature", "Factors"])
            template.m_dictionary.set_Item(name, creature.m_dictionary.get_Item(name));

        var terrain = findSubsystem("Terrain");
        var y = Math.floor(origin.Y) + 8;
        for (var i = 0; i < actorCount; i++) {
            var x = Math.floor(origin.X) - 10 + 2 * (i % 10);
            var z = Math.floor(origin.Z) + 4 + 2 * Math.floor(i / 10);
            terrain.ChangeCell(x, y - 1, z, 1, true, null);
            var actor = project.CreateEntity(template, 0);
            var body = actor.FindComponent("Body", true);
            body.Position = new Engine.Vector3(x + .5, y, z + .5);
            project.AddEntity(actor);
            actors.push(actor);
            bodies.push(body);
            health.push(actor.FindComponent("Health", true));
            var profile = "scale " + i;
            if (sustained) {
                profile = "Participant " + i + " on a small training platform. First jump once using act_Jump with empty arguments. "
                    + "After any task ends, rest using keep. At a timed review, choose a jump, a short act_Wait, or continued rest. "
                    + "Use at most one step and set reviewAfter to " + (8 + 2 * (i % 3)) + " seconds.";
            } else if (live) {
                profile = "Participant " + i + ". Your immediate objective is to test your footing with one jump in place. "
                    + "Start a one-step plan using act_Jump with empty arguments, then review after 120 simulated seconds.";
            }
            bindActor(actor, body, profile);
        }
        checkCohort();
    }

    function spawnForagers(origin) {
        var terrain = findSubsystem("Terrain"), pickables = findSubsystem("Pickables");
        var x0 = Math.floor(origin.X) - 18, z0 = Math.floor(origin.Z) + 10, y = Math.floor(origin.Y) + 10;
        for (var x = x0 - 5; x <= x0 + 41; x++) {
            for (var z = z0 - 6; z <= z0 + 26; z++) {
                terrain.ChangeCell(x, y - 1, z, 1, true, null);
                if (x === x0 - 5 || x === x0 + 41 || z === z0 - 6 || z === z0 + 26)
                    terrain.ChangeCell(x, y, z, 1, true, null);
            }
        }
        var foods = ["CookedMeatBlock", "BreadBlock"].map(name => Game.BlocksManager.GetBlockIndex(name, true));
        for (var i = 0; i < actorCount; i++) {
            var actor = Game.DatabaseManager.CreateEntity(project, "WestworldForager", true);
            var body = actor.FindComponent("Body", true);
            var reserve = actor.FindComponent("Nutrition", true);
            body.Position = new Engine.Vector3(x0 + 4 * (i % 10) + .5, y, z0 + 5 * Math.floor(i / 10) + .5);
            reserve.Food = .16 + .08 * (i % 5);
            project.AddEntity(actor);
            var food = foods[i % foods.length];
            if (i % 5 === 0) actor.FindComponent("Inventory", true).AddSlotItems(0, food, 1);
            if (interruptWorld && i === 0) actor.FindComponent("Inventory", true).AddSlotItems(0, food, 1);
            var forward = body.Matrix.Forward;
            var distance = 2.3 + .3 * (i % 4);
            var drop = new Engine.Vector3(body.Position.X + forward.X * distance, y + .08,
                body.Position.Z + forward.Z * distance);
            foodDrops.push(pickables.AddPickable(food, 1 + (i % 2), drop, new Engine.Vector3(0, 0, 0), null));
            actors.push(actor);
            bodies.push(body);
            health.push(actor.FindComponent("Health", true));
            nutrition.push(reserve);
            var profile = "Forager " + i + ". Maintain your food reserve using your own observations and inventory. Food on the ground is shared. ";
            profile += observation
                ? "Plan several compatible steps when useful. For Eat after PickUp use item_from_step. On a safe pause review in "
                    + (30 + 8 * (i % 3)) + " simulated seconds."
                : "Choose one valid step or keep if you have no useful action. On a safe pause review in "
                    + (8 + 2 * (i % 3)) + " simulated seconds.";
            bindActor(actor, body, profile);
        }
        checkCohort();
        check(nutrition.every(reserve => reserve.Food < .5), "all 50 foragers started with a food need");
    }

    function bindActor(actor, body, profile) {
        var actorTools = new W.ActorTools(new W.AtomicActions(actor), false);
        var mind = new W.ActorCognition(actorTools, profile);
        tools.push(actorTools);
        minds.push(mind);
        actorActivity.push({version: 0, lastDecision: null, baselineY: body.Position.Y, peakY: body.Position.Y, pending: null, multiStep: 0,
            x: body.Position.X, z: body.Position.Z, moved: false, eaten: 0, picked: 0,
            initialFood: forage ? nutrition[nutrition.length - 1].Food : null,
            completed: 0, failed: 0, rejected: 0, resting: 0});
        scheduler.Register(mind, actorTools);
    }

    function checkCohort() {
        check(new Set(actors.map(actor => actor.Id)).size === actorCount
            && actors.every(actor => actor.IsAddedToProject && System.Object.ReferenceEquals(actor.Project, project)),
            "50 distinct human bodies in the same loaded project");
    }

    function bodyState() {
        return JSON.stringify(bodies.map((body, i) => {
            var p = body.Position, v = body.Velocity;
            return [p.X, p.Y, p.Z, v.X, v.Y, v.Z, health[i].Health, forage ? nutrition[i].Food : null];
        }));
    }

    var completedByAction = {};
    function sampleActivity(force) {
        if (!force && time.GameTime - lastSample < (observation ? .15 : .06)) return;
        lastSample = time.GameTime;
        for (var i = 0; i < actorCount; i++) {
            var activity = actorActivity[i];
            var position = bodies[i].Position;
            activity.peakY = Math.max(activity.peakY, position.Y);
            activity.moved ||= Math.hypot(position.X - activity.x, position.Z - activity.z) > .5;
            var decisionJson = minds[i].LastDecisionJson;
            if (decisionJson && decisionJson !== activity.lastDecision) {
                var applied = JSON.parse(decisionJson);
                sampleTaskResult(activity, applied.precedingTaskOutcome);
                activity.lastDecision = decisionJson;
                activity.version = applied.version;
                if (applied.actionError) activity.rejected++;
                else if (applied.decision.task === "keep") activity.resting++;
                else if (applied.decision.task === "start" || applied.decision.task === "replace") {
                    activity.pending = applied.taskId;
                    if (applied.decision.steps.length > 1) activity.multiStep++;
                }
            }
            if (!activity.pending) continue;
            sampleTaskResult(activity, JSON.parse(tools[i].InvokeJson("task_status", "{}")).data);
        }
    }

    function sampleTaskResult(activity, task) {
        if (!task || task.taskId !== activity.pending) return;
        if (task.status === "succeeded") {
            activity.completed++;
        } else if (task.status === "failed" || task.status === "cancelled") {
            activity.failed++;
        } else {
            return;
        }
        for (var step of task.steps.filter(step => step.status === "succeeded")) {
            completedByAction[step.name] = (completedByAction[step.name] || 0) + 1;
            if (step.name === "Eat") activity.eaten += step.affectedCount;
            if (step.name === "PickUp") activity.picked += step.affectedCount;
        }
        activity.pending = null;
    }

    function sessionDetail() {
        var minimumTurns = Math.min(...actorActivity.map(activity => activity.version));
        var actorsWithActions = actorActivity.filter(activity => activity.completed > 0).length;
        var actorsWhoRose = actorActivity.filter(activity => activity.peakY - activity.baselineY > .05).length;
        var total = key => actorActivity.reduce((count, activity) => count + activity[key], 0);
        var detail = {
            stopReason: finalStatus.reason, simulatedSeconds: sessionEnd - sessionStart,
            wallSeconds: Diagnostics.Stopwatch.GetElapsedTime(sessionStartedAt).TotalSeconds,
            requests: finalStatus.status.requests,
            actorsWithActions, actorsWhoRose, minimumTurns, completedTasks: total("completed"),
            failedTasks: total("failed"), rejectedActions: total("rejected"), restingDecisions: total("resting"),
            multiStepPlans: total("multiStep"),
            unfinishedTasks: actorActivity.filter(activity => activity.pending).length,
            completedByAction, spentNanoUsd: finalStatus.status.spent, unknownNanoUsd: finalStatus.status.unknown
        };
        if (forage) {
            detail.actorsMoved = actorActivity.filter(activity => activity.moved).length;
            detail.actorsWhoPicked = actorActivity.filter(activity => activity.picked > 0).length;
            detail.actorsWhoAte = actorActivity.filter(activity => activity.eaten > 0).length;
            detail.foodPicked = total("picked");
            detail.foodEaten = total("eaten");
            detail.averageInitialFood = total("initialFood") / actorCount;
            detail.averageFinalFood = nutrition.reduce((sum, reserve) => sum + reserve.Food, 0) / actorCount;
            detail.groundFoodRemaining = foodDrops.reduce((sum, drop) => sum + Math.max(drop.Count, 0), 0);
        }
        return detail;
    }

    var observationDetail;
    function sampleOwnRequestMotion(status) {
        var nextSamples = new Map();
        for (var request of status.lookAheadRequests || []) {
            var index = actors.findIndex(actor => actor.Id === request.actorId);
            var task = JSON.parse(tools[index].InvokeJson("task_status", "{}")).data;
            if (task.taskId !== request.taskId || task.status !== "running" || task.steps[task.currentStep]?.name !== "MoveTo") continue;
            var position = bodies[index].Position;
            var start = inFlightSamples.get(request.id) || {x: position.X, z: position.Z, at: time.GameTime};
            nextSamples.set(request.id, start);
            var distance = Math.hypot(position.X - start.x, position.Z - start.z);
            if (distance >= .1 && time.GameTime > start.at && distance > (movedDuringOwnRequest.get(request.id)?.distance || 0)) {
                movedDuringOwnRequest.set(request.id, {actorId: request.actorId, distance});
            }
        }
        inFlightSamples = nextSamples;
    }

    function monitorObservation(status) {
        if (status.spent + status.unknown + status.reserved > status.budget) {
            throw new Error("observation exceeded its budget");
        }
        if (interruptLedger && status.applied > 0 && status.networkInFlight > 0) {
            scheduler.SetPaused(true);
            var durable = JSON.parse(Engine.Storage.ReadAllText(ledgerPath)).attempts;
            var completed = durable.filter(attempt => attempt.state === "completed");
            var inFlight = durable.filter(attempt => attempt.state === "inFlight");
            check(completed.length >= status.applied && inFlight.length > 0,
                "a settled decision and a dispatched request were both durable before process kill");
            finish("passed", {completed: completed.length, inFlight: inFlight.length, requests: status.requests});
            return;
        }
        var frameAt = Diagnostics.Stopwatch.GetTimestamp();
        observationFrameGaps.push(Diagnostics.Stopwatch.GetElapsedTime(lastObservationFrameAt, frameAt).TotalMilliseconds);
        lastObservationFrameAt = frameAt;
        var elapsed = Diagnostics.Stopwatch.GetElapsedTime(sessionStartedAt, frameAt).TotalSeconds;
        if (time.GameTime > observedGameTime && status.inFlight > 0) {
            advancingWithRequests++;
            if (status.activeTasks > 0) advancingWithTasks++;
        }
        sampleOwnRequestMotion(status);
        observedGameTime = time.GameTime;
        if (elapsed < (fixedObservation ? 20 : 60)) { sampleActivity(false); return; }
        sampleActivity(true);
        sessionEnd = time.GameTime;
        observationElapsed = elapsed;
        inFlightAtCutoff = status.inFlight;
        finalStatus = {status, reason: "wall_clock_limit"};
        observationDetail = sessionDetail();
        observationDetail.scheduling = JSON.parse(scheduler.ObservationMetricsJson());
        var gaps = observationFrameGaps.sort((a, b) => a - b);
        observationDetail.frameIntervals = {
            count: gaps.length, p95Ms: gaps[Math.ceil(gaps.length * .95) - 1],
            p99Ms: gaps[Math.ceil(gaps.length * .99) - 1], maxMs: gaps[gaps.length - 1],
            over50Ms: gaps.filter(gap => gap > 50).length, over100Ms: gaps.filter(gap => gap > 100).length
        };
        scheduler.StopObservationDispatch();
        phase = "observationDrain";
    }

    function finishObservation(status) {
        var attempts = JSON.parse(Engine.Storage.ReadAllText(ledgerPath)).attempts;
        var sentAttempts = attempts.filter(attempt => attempt.state !== "unsent");
        var errors = {}, actorsDispatched = new Set(), lookAheadBeforeAllFirstRequests = false;
        for (var attempt of sentAttempts) {
            if (attempt.error) errors[attempt.error] = (errors[attempt.error] || 0) + 1;
            lookAheadBeforeAllFirstRequests ||= !!attempt.precedingTask && actorsDispatched.size < actorCount;
            actorsDispatched.add(attempt.actorId);
        }
        var detail = Object.assign(observationDetail, {
            wallSeconds: observationElapsed, requests: status.requests, decisionsApplied: status.applied, peak: status.peak,
            spentNanoUsd: status.spent, unknownNanoUsd: status.unknown, reservedNanoUsd: status.reserved,
            targetSpeed: 3, measuredSpeed: observationDetail.simulatedSeconds / observationElapsed,
            advancingFramesWithRequests: advancingWithRequests, advancingFramesWithTasksAndRequests: advancingWithTasks,
            inFlightAtCutoff, rejectedResponses: errors,
            lookAheadStarted: finalStatus.status.lookAheadStarted, lookAheadBuffered: finalStatus.status.lookAheadBuffered,
            lookAheadRequests: sentAttempts.filter(attempt => attempt.precedingTask).length, lookAheadBeforeAllFirstRequests,
            unsentReservations: attempts.length - sentAttempts.length,
            requestsWithOwnMotion: movedDuringOwnRequest.size,
            actorsMovingDuringOwnRequests: new Set(Array.from(movedDuringOwnRequest.values(), sample => sample.actorId)).size,
            maxBlocksMovedDuringOwnRequest: Math.max(0, ...Array.from(movedDuringOwnRequest.values(), sample => sample.distance))
        });
        if (status.modelReplyEvents !== undefined) {
            detail.eventCounts = {
                starts: finalStatus.status.taskStartEvents,
                ends: finalStatus.status.taskEndEvents,
                replies: status.modelReplyEvents
            };
            check(detail.eventCounts.starts >= detail.lookAheadRequests && detail.eventCounts.ends > 0,
                "task starts and ends were delivered through the event queue");
            check(detail.eventCounts.replies === status.requests, "each model request delivered one completion event");
        }
        if (status.ledgerPending !== undefined) {
            check(sentAttempts.length === status.requests && !status.ledgerPending,
                "each sent request has one durable attempt and all writes were acknowledged");
            check(attempts.every(attempt => attempt.state !== "inFlight") && new Set(attempts.map(attempt => attempt.id)).size === attempts.length,
                "drained ledger contains no unsettled or duplicate attempts");
        }
        check(status.peak > 0 && status.peak <= parallelism && status.reserved === 0,
            "request cap and final ledger reservations verified");
        check(advancingWithTasks > 0 && System.Object.ReferenceEquals(project, getProject()),
            "world and ongoing actor tasks advanced while other actors waited for replies");
        check(detail.requestsWithOwnMotion > 0, "the same actor moved in its running task while its own model request was in flight");
        check(detail.lookAheadStarted > 0, "look-ahead decisions started new tasks after their predecessors succeeded");
        if (fixedObservation) {
            check(detail.lookAheadBeforeAllFirstRequests, "look-ahead requests got slots while initial requests were still queued");
            check(detail.lookAheadBuffered > 0 && errors.pose_changed > 0 && errors.preceding_task_unsuccessful > 0,
                "early replies waited for handoff and stale moves and unsuccessful predecessors were rejected");
        } else {
            check(detail.multiStepPlans > 0 && detail.foodPicked > 0 && detail.foodEaten > 0,
                "multi-step decisions and real food actions executed");
        }
        scheduler.Dispose();
        finish("passed", detail);
    }

    function createScheduler() {
        var endpoint = System.Environment.GetEnvironmentVariable("WENMING_MODEL_ENDPOINT");
        if (!live) endpoint += "/" + (worldRecovery ? "observe-world" : fixedObservation ? "observe-ahead" : mode) + "/responses";
        var client = new W.ActorModelClient(endpoint, System.Environment.GetEnvironmentVariable("WENMING_MODEL"),
            "responses", System.Environment.GetEnvironmentVariable("WENMING_MODEL_API_KEY"));
        return new W.WorldCognitionScheduler(client, "app:cognition-run", parallelism, mode === "scale" ? 1 : sessionBudget,
            inputPrice, outputPrice, live ? "openai-luna-standard-2026-11" : "fixture-v1");
    }

    function monitorStandard(status) {
        if (mode === "scale" && !budgetRaised && status.state === "budgetLimited") {
            check(status.requests === 0 && status.spent === 0 && status.reserved === 0,
                "insufficient budget allowed no sends, charges or reservations");
            scheduler.IncreaseBudget(sessionBudget);
            budgetRaised = true;
        }
        sampleActivity(false);
        var ready = mode === "scale" ? status.applied >= 55 :
            exercise ? actorActivity.every(activity => activity.completed > 0) :
                time.GameTime - sessionStart >= sessionSeconds
                    && (forage || actorActivity.every(activity => activity.version >= 2));
        if (!ready) return;
        finalStatus = {status, reason: "duration"};
        sessionEnd = time.GameTime;
        scheduler.StopObservationDispatch();
        phase = "observationDrain";
    }

    function finishStandard(status) {
        sampleActivity(true);
        if (status.activeTasks > 0) return;
        var detail = Object.assign(sessionDetail(), {peak: status.peak, requests: status.requests,
            spentNanoUsd: status.spent, unknownNanoUsd: status.unknown});
        check(status.peak > 0 && status.peak <= parallelism && status.reserved === 0,
            "request cap and final ledger reservations verified");
        if (mode === "scale") {
            check(budgetRaised && detail.minimumTurns >= 1 && status.applied >= 55,
                "all 50 initial turns and five timed reviews ran after increasing budget");
        } else if (forage) {
            check(detail.foodPicked > 0 && detail.foodEaten > 0, "native food pickup and eating completed");
        } else {
            check(detail.actorsWithActions === actorCount && detail.actorsWhoRose === actorCount,
                "all 50 jump tasks succeeded and all 50 native bodies rose");
        }
        scheduler.Dispose();
        finish("passed", detail);
    }

    function monitorLedgerRecovery() {
        if (!recoveryTask) {
            ledgerBeforeRecovery = JSON.parse(Engine.Storage.ReadAllText(ledgerPath)).attempts;
            scheduler = createScheduler();
            recoveryTask = scheduler.RecoverObservationLedgerAsync();
            return;
        }
        if (!recoveryTask.IsCompleted) return;
        var recovered = JSON.parse(recoveryTask.GetAwaiter().GetResult());
        var attempts = JSON.parse(Engine.Storage.ReadAllText(ledgerPath)).attempts;
        var spent = ledgerBeforeRecovery.filter(a => a.state === "completed").reduce((total, a) => total + a.charge, 0);
        var unknown = ledgerBeforeRecovery.filter(a => a.state === "unknown" || a.state === "inFlight")
            .reduce((total, a) => total + a.reserved, 0);
        check(attempts.length === ledgerBeforeRecovery.length && attempts.every((attempt, index) => {
            var previous = ledgerBeforeRecovery[index];
            return attempt.id === previous.id && attempt.charge === previous.charge
                && attempt.state === (previous.state === "inFlight" ? "unknown" : previous.state);
        }), "recovery changed only unresolved attempt states, with no new or duplicate attempts");
        check(recovered.spent === spent && recovered.unknown === unknown && spent > 0 && unknown > 0,
            "recovery preserved settled charges and reserved ambiguous charges without dispatching");
        scheduler.Dispose();
        finish("passed", recovered);
    }

    function monitorWorldRecovery(status) {
        if (status.state === "faulted") throw new Error("observation checkpoint fault: " + status.fault);
        // Windows cannot replace a ledger while this fixture has its old file open.
        if (status.checkpointTime == null || status.ledgerPending) return;
        var task = JSON.parse(tools[0].InvokeJson("task_status", "{}")).data;
        var inventory = actors[0].FindComponent("Inventory", true);
        var ledger = JSON.parse(Engine.Storage.ReadAllText(ledgerPath));
        if (interruptWorld) {
            var candidate = ledger.attempts.find(a => a.actorId === actors[0].Id && a.precedingTask === task.taskId
                && a.state === "completed");
            if (!requestedCheckpoint && candidate && status.lookAheadBuffered > 0 && task.completedSteps === 1) {
                check(inventory.GetSlotCount(0) === 1 && task.steps[0].affectedCount === 1,
                    "the first eat consumed exactly one item before the checkpoint");
                requestedCheckpoint = status.checkpointTime;
                scheduler.RequestObservationCheckpoint();
            }
            if (requestedCheckpoint != null && status.checkpointTime > requestedCheckpoint && candidate && task.completedSteps >= 1) {
                var uncertain = ledger.attempts.find(a => a.actorId === actors[1].Id && a.state === "inFlight");
                check(uncertain && inventory.GetSlotCount(0) === 1 && status.networkInFlight > 0,
                    "the checkpoint has a paid follow-up and another dispatched request");
                scheduler.SetPaused(true);
                finish("passed", {actorIds: actors.map(actor => actor.Id), taskId: task.taskId, completedSteps: task.completedSteps,
                    turnId: candidate.batchId, attemptId: candidate.id, charge: candidate.charge, uncertainId: uncertain.id,
                    position: JSON.parse(bodyState())[0].slice(0, 3), time: status.checkpointTime});
            }
        } else {
            var decision = minds[0].LastDecisionJson && JSON.parse(minds[0].LastDecisionJson);
            if (decision?.input.id === interruption.turnId) resumedDecision = decision;
            if (!resumedDecision || task.taskId !== resumedDecision.taskId || task.status !== "succeeded") return;
            check(status.reusedReplies === 1 && resumedDecision.precedingTaskOutcome.taskId === interruption.taskId
                && resumedDecision.precedingTaskOutcome.completedSteps === 3 && time.GameTime > interruption.time,
                "the original task finished and the saved reply started the next task");
            check(task.steps[0].affectedCount === 1 && inventory.GetSlotCount(0) === 0,
                "the resumed plan consumed only the second item");
            var attempts = ledger.attempts.filter(a => a.batchId === interruption.turnId);
            var uncertain = ledger.attempts.find(a => a.id === interruption.uncertainId);
            check(attempts.length === 1 && attempts[0].id === interruption.attemptId && attempts[0].charge === interruption.charge
                && uncertain?.state === "unknown" && status.unknown >= uncertain.reserved
                && ledger.attempts.filter(a => a.actorId === actors[1].Id).length === 1,
                "the saved reply was charged once and the unresolved dispatch was not retried");
            finish("passed", {taskId: interruption.taskId, followUpTaskId: task.taskId, reusedReplies: status.reusedReplies,
                charge: attempts[0].charge, unknown: status.unknown, time: time.GameTime});
        }
    }

    function resumeObservationWorld(screen) {
        interruption = JSON.parse(Engine.Storage.ReadAllText("app:interrupted-result.json")).detail;
        var stream = Engine.Storage.OpenFile("app:cognition-run/observation.scworld", Engine.OpenFileMode.Read);
        try {
            var world = Game.WorldsManager.GetWorldInfo(Game.WorldsManager.ImportWorld(stream));
            Game.GameManager.LoadProject(world, screen.Children.Find("GamesWidget", true));
        } finally { stream.Dispose(); }
        project = getProject();
        time = findSubsystem("Time");
        scheduler = createScheduler();
        interruption.actorIds.forEach(function (id, i) {
            var actor = project.FindEntity(id), body = actor.FindComponent("Body", true);
            actors.push(actor);
            bodies.push(body);
            health.push(actor.FindComponent("Health", true));
            nutrition.push(actor.FindComponent("Nutrition", true));
            bindActor(actor, body, "Forager " + i);
        });
        checkCohort();
        scheduler.ResumeObservation();
        var task = JSON.parse(tools[0].InvokeJson("task_status", "{}")).data;
        check(time.GameTime === interruption.time && task.taskId === interruption.taskId
            && task.completedSteps === interruption.completedSteps && task.steps[0].affectedCount === 1
            && actors[0].FindComponent("Inventory", true).GetSlotCount(0) === 1
            && JSON.parse(bodyState())[0].slice(0, 3).every((position, i) => Math.abs(position - interruption.position[i]) < .02),
            "reloaded the same world time, task ID, completed step and inventory before advancing");
        time.BasicGameTimeFactor = 3;
        time.MaxGameTimeDelta = Math.min(time.MaxGameTimeDelta, .05 / 3);
        Game.ScreensManager.SwitchScreen("Game");
        phase = "observation";
    }

    frameHandlers.push(function () {
        if (phase === "done" || phase === "spawning") return;
        try {
            if (recoverLedger) { monitorLedgerRecovery(); return; }
            if (phase === "load") {
                if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu"
                    || Game.ScreensManager.IsAnimating) return;
                if (resumeWorld) { resumeObservationWorld(Game.ScreensManager.m_screens.get_Item("Game")); return; }
                var settings = new Game.WorldSettings();
                settings.Name = "50 Actor Cognition Verification";
                settings.Seed = "42";
                settings.OriginalSerializationVersion = Game.VersionsManager.SerializationVersion;
                settings.ResetOptionsForNonCreativeMode(null);
                var screen = Game.ScreensManager.m_screens.get_Item("Game");
                Game.GameManager.LoadProject(Game.WorldsManager.CreateWorld(settings), screen.Children.Find("GamesWidget", true));
                project = getProject();
                var observer = new Game.PlayerData(project);
                observer.Name = "Scale observer";
                observer.InputDevice = Game.WidgetInputDevice.Keyboard | Game.WidgetInputDevice.Mouse;
                findSubsystem("Players").AddPlayerData(observer);
                time = findSubsystem("Time");
                Game.ScreensManager.SwitchScreen("Game");
                phase = "spawn";
                return;
            }
            if (phase === "spawn") {
                var players = findSubsystem("Players").ComponentPlayers;
                if (players.Count !== 1 || time.GameTime < 4) return;
                phase = "spawning";
                scheduler = createScheduler();
                spawnActors(players.get_Item(0).ComponentBody.Position);
                phase = "settle";
                return;
            }
            if (phase === "settle") {
                if ((exercise || sustained) && bodies.some(body => body.StandingOnValue == null)) return;
                var frameRate = Number(System.Environment.GetEnvironmentVariable("WENMING_OBSERVATION_FRAME_RATE"));
                if (frameRate > 0) {
                    Engine.Window.PresentationInterval = 0;
                    Engine.Time.FrameDurationLimit = 1 / frameRate;
                }
                if (forage) {
                    time.BasicGameTimeFactor = 3;
                    time.MaxGameTimeDelta = Math.min(time.MaxGameTimeDelta, .05 / 3);
                }
                bodies.forEach((body, index) => actorActivity[index].baselineY = actorActivity[index].peakY = body.Position.Y);
                scheduler.StartObservation(observation);
                sessionStart = observedGameTime = time.GameTime;
                sessionStartedAt = lastObservationFrameAt = Diagnostics.Stopwatch.GetTimestamp();
                phase = "observation";
                return;
            }
            var status = JSON.parse(scheduler.StatusJson());
            if (faultLedger) {
                if (status.state !== "faulted") return;
                check(status.fault === "persistence_error" && status.requests === 0,
                    "failure writing the first ledger snapshot blocked every network dispatch");
                finish("passed", status);
                return;
            }
            if (status.state === "faulted") throw new Error("observation fault: " + status.fault);
            if (phase === "observation") {
                if (worldRecovery) monitorWorldRecovery(status);
                else if (observation) monitorObservation(status);
                else monitorStandard(status);
                return;
            }
            if (phase === "observationDrain" && status.inFlight === 0 && !status.ledgerPending) {
                if (observation) finishObservation(status);
                else finishStandard(status);
            }
        } catch (error) {
            finish("failed", observationDetail ? Object.assign(observationDetail, {error: String(error)}) : String(error));
        }
    });
})();
