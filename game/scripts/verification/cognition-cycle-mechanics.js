(function () {
    var W = importNamespace("Game.Westworld"), phase = "load", checks = [], started, tools, cognition, actor, before,
        firstDecision, firstInput, waitId, waitAt, heldInput, heldAt, lateAt, idleAt, idleZero, created = 0, shortAttempts = 0,
        lateShortAttempts = 0;
    function check(condition, description) {
        if (!condition) throw new Error(description);
        checks.push(description);
    }
    function finish(result, error) {
        phase = "done";
        Engine.Storage.WriteAllText("app:cognition-cycle-result.json", JSON.stringify({ result, checks, error }, null, 2));
        Engine.Window.Close();
    }
    function state() { return JSON.parse(cognition.ReadCycleStateJson()); }
    function active() { return ["waiting", "running", "blocked"].includes(JSON.parse(tools.InvokeJson("task_status", "{}")).data.status); }
    function read() { var raw = cognition.PrepareTurn(); return raw ? JSON.parse(raw) : null; }
    function commit(input, decision) { return JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(decision))); }
    function goal(input) { return input.cognitionCycle.goals.find(g => g.id === input.cognitionCycle.activeGoal); }
    function method(strategy, metric = "inventory") { return { strategy, expectedProgress: { metric, windowSeconds: 5 } }; }
    function maintain(input) {
        var g = goal(input);
        return { operation: "maintain", goalId: g.id, expectedRevision: g.revision, source: "observation", why: "Review actual evidence before continuing." };
    }
    frameHandlers.push(function () {
        if (phase === "done") return;
        try {
            if (phase === "load") {
                if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu" || Game.ScreensManager.IsAnimating) return;
                var settings = new Game.WorldSettings();
                settings.Name = "Cognition protocol mechanics";
                settings.Seed = "42";
                Game.ScreensManager.SwitchScreen("GameLoading", W.FoundingWorld.CreateOpen(settings, 1), null);
                started = Engine.Time.RealTime;
                phase = "spawn";
                return;
            }
            if (Engine.Time.RealTime - started > 100) throw new Error("cognition mechanics timeout");
            var project = getProject();
            if (!project) return;
            var actors = Array.from(project.Entities).filter(e => e.ValuesDictionary.DatabaseObject.Name === "WestworldProducer");
            if (actors.length !== 1 || findSubsystem("Time").GameTime < 4) return;
            if (phase === "spawn") {
                check(!Engine.Window.IsVisible && Game.SettingsManager.SoundsVolume === 0 && Game.SettingsManager.MusicVolume === 0, "hidden and silent native execution");
                actor = actors[0];
                tools = new W.ActorTools(new W.AtomicActions(actor));
                cognition = new W.ActorCognition(tools, "依据自己的身体、局部观察和经历行动。");
                var input = read();
                check(input.cognitionCycle?.protocol === "deliberative-v1", "new cognition context and versioned protocol are connected");
                check(input.cognitionCycle.goals.length === 0, "no goal is assigned at startup");
                check(!JSON.stringify(input).includes("Select the stone axe"), "scenario-specific woodworking guidance is absent");
                firstInput = input;
                firstDecision = { reason: "Retain a valid observation even if the physical action becomes stale.",
                    understandingPatch: [{ id: "b1", expectedRevision: 0, text: "My last observation is local, not a complete map.", source: "observation" }],
                    deliberation: { operation: "create", goalId: "g1", expectedRevision: 0, purpose: "Investigate my surroundings", source: "observation",
                        why: "I need information about the local situation.", method: method("Inspect a bounded local area", "observation"), dependsOnBeliefs: ["b1"] },
                    taskRequest: { operation: "submit", steps: [{ tool: "act_MoveTo", arguments: { bearing: 0, distance: 1 } }] }, reviewAfter: .1 };
                var body = actor.FindComponent("Body", true);
                var originalPosition = body.Position;
                body.Position = new Engine.Vector3(body.Position.X + 6, body.Position.Y, body.Position.Z);
                var adopted = commit(input, firstDecision);
                check(adopted.ok && adopted.receipt.action === "observation_expired", "movement beyond the authorized local anchor is rejected independently");
                body.Position = originalPosition;
                check(state().beliefs.length === 1 && state().goals.length === 1 && state().activeGoal, "valid belief and goal survive action rejection");
                var duplicate = commit(input, firstDecision);
                check(duplicate.duplicate && state().beliefs.length === 1 && state().receipts.length === 1, "repeated response does not duplicate cognition");
                phase = "injury";
            } else if (phase === "injury") {
                var input = read(); if (!input) return;
                var health = actor.FindComponent("Health", true);
                health.Health = health.Health - .1;
                var d = maintain(input); d.operation = "revise"; d.purpose = "Reconsider what matters using my own experienced situation";
                var result = commit(input, { reason: "A new bodily signal must not erase a valid cognitive reassessment.",
                    deliberation: d, taskRequest: { operation: "none" }, reviewAfter: .1 });
                check(result.receipt.deliberation === "accepted", "new injury does not blanket-reject a valid historical cognitive update");
                phase = "injuryNotice";
            } else if (phase === "injuryNotice") {
                var input = read(); if (!input) return;
                check(input.reasons.includes("newInjury"), "an old reply cannot consume a newer bodily signal");
                cognition.AbandonTurn(input.id);
                phase = "foreign";
            } else if (phase === "foreign") {
                var input = read(); if (!input) return;
                var d = maintain(input); d.dependsOnBeliefs = ["foreign"];
                var result = commit(input, { reason: "Exercise source isolation.",
                    understandingPatch: [{ id: "foreign", expectedRevision: 0, text: "A hidden claim", source: "another-person-private-evidence" }],
                    deliberation: d, taskRequest: { operation: "submit", steps: [{ tool: "act_Wait", arguments: { duration: 30 } }] }, reviewAfter: .1 });
                check(result.receipt.rejectedBeliefs.includes("foreign") && result.receipt.deliberation === "belief_dependency_rejected", "foreign evidence and dependent deliberation are rejected");
                check(state().beliefs.length === 1 && !active(), "rejected dependency has no physical task");
                phase = "wait";
            } else if (phase === "wait") {
                var input = read(); if (!input) return;
                var d = maintain(input); d.method = method("Wait while monitoring actual item gain");
                var result = commit(input, { reason: "Measure progress rather than counting successful motion.", deliberation: d,
                    taskRequest: { operation: "submit", steps: [{ tool: "act_Wait", arguments: { duration: 30 } }] }, reviewAfter: .1 });
                check(result.receipt.action === "accepted" && active(), "a real restorable task is authorized");
                waitId = state().authorizedTask; waitAt = findSubsystem("Time").GameTime;
                phase = "progress";
            } else if (phase === "progress") {
                var input = read(); if (!input) return;
                if (!heldInput) { heldInput = input; heldAt = findSubsystem("Time").GameTime; return; }
                if (findSubsystem("Time").GameTime < heldAt + 11) return;
                check(state().method.zeroWindows >= 2, "actual progress monitoring continues while a decision is frozen");
                var d = maintain(heldInput);
                d.taskDisposition = { operation: "retain", taskId: heldInput.cognitionCycle.taskId, expectedRevision: heldInput.cognitionCycle.authorizationRevision };
                var result = commit(heldInput, { reason: "This old reply has not considered later progress evidence.", deliberation: d,
                    taskRequest: { operation: "continue" }, reviewAfter: 120 });
                Engine.Storage.WriteAllText("app:cognition-cycle-late-debug.json", JSON.stringify({ heldAt, heldInput,
                    now: findSubsystem("Time").GameTime, result, state: state() }, null, 2));
                check(result.receipt.deliberation === "accepted", "an unrelated old decision can still preserve valid authority");
                lateAt = findSubsystem("Time").GameTime;
                phase = "late";
            } else if (phase === "late") {
                var input = read();
                if (!input) { if (findSubsystem("Time").GameTime > lateAt + 1) throw new Error("new in-flight evidence was incorrectly marked reviewed"); return; }
                if (!input.reasons.includes("methodReview")) Engine.Storage.WriteAllText("app:cognition-cycle-late-next.json", JSON.stringify(input, null, 2));
                check(input.reasons.includes("methodReview"), "new in-flight evidence remains unreviewed after an old response");
                check(input.cognitionCycle.method.zeroWindows >= 2 && input.cognitionCycle.methodEpisodes.some(e => e.outcome === "noProgress"), "zero actual progress triggers method reassessment during an active task");
                var d = maintain(input);
                d.taskDisposition = { operation: "revoke", taskId: input.cognitionCycle.taskId, expectedRevision: input.cognitionCycle.authorizationRevision };
                var result = commit(input, { reason: "An omitted method must not bypass method checks.", deliberation: d,
                    taskRequest: { operation: "replace", steps: [{ tool: "act_Wait", arguments: { duration: 30 } }] }, reviewAfter: .1 });
                check(result.receipt.deliberation === "method_required" && active(), "omitting a method cannot bypass failed-method validation");
                phase = "rename";
            } else if (phase === "rename") {
                var input = read(); if (!input) return;
                var d = maintain(input); d.method = method("Renamed method with unchanged behavior");
                d.taskDisposition = { operation: "revoke", taskId: input.cognitionCycle.taskId, expectedRevision: input.cognitionCycle.authorizationRevision };
                var result = commit(input, { reason: "Attempt to reset a failed method using only a new label.", deliberation: d,
                    taskRequest: { operation: "replace", steps: [{ tool: "act_Wait", arguments: { duration: 30 } }] }, reviewAfter: .1 });
                check(result.receipt.deliberation === "method_has_no_progress" && active(), "renaming does not bypass repeated no-progress detection or revoke valid old authority");
                phase = "switch";
            } else if (phase === "switch") {
                var input = read(); if (!input) return;
                var result = commit(input, { reason: "Abandon the unproductive intention and use another method.",
                    deliberation: { operation: "create", goalId: "g2", expectedRevision: 0, purpose: "Reassess current bodily needs", source: "body",
                        why: "The previous method has no item progress.", previousGoalAction: "abandon", method: method("Try another supported approach", "food"),
                        taskDisposition: { operation: "revoke", taskId: input.cognitionCycle.taskId, expectedRevision: input.cognitionCycle.authorizationRevision } },
                    taskRequest: { operation: "replace", steps: [{ tool: "act_Unsupported", arguments: {} }] }, reviewAfter: .1 });
                check(result.receipt.deliberation === "accepted" && result.receipt.action !== "accepted", "new goal can be accepted while its action request is rejected");
                check(!active() && state().goals.some(g => g.status === "abandoned") && state().intentionStatus === "awaitingTask", "revoked old task cannot resume after replacement rejection");
                before = state();
                phase = "revision";
            } else if (phase === "revision") {
                var input = read(); if (!input) return;
                var d = maintain(input); d.expectedRevision += 1;
                var result = commit(input, { reason: "Check local version conflicts.",
                    understandingPatch: [{ id: before.beliefs[0].id, expectedRevision: 999, text: "Stale overwrite", source: "observation" }],
                    deliberation: d, taskRequest: { operation: "none" }, reviewAfter: .1 });
                check(result.receipt.deliberation === "goal_version_changed" && state().beliefs[0].revision === before.beliefs[0].revision, "stale revisions cannot overwrite goals or beliefs");
                phase = "short";
            } else if (phase === "short") {
                var input = read(); if (!input) return;
                var d = maintain(input); d.method = method("One short bounded task", "inventory");
                var result = commit(input, { reason: "Observe task closure separately from idle time.", deliberation: d,
                    taskRequest: { operation: "submit", steps: [{ tool: "act_Wait", arguments: { duration: .2 } }] }, reviewAfter: 120 });
                check(result.receipt.action === "accepted", "short task starts through the same authority path");
                phase = "idle";
            } else if (phase === "idle") {
                var input = read(); if (input) cognition.AbandonTurn(input.id);
                if (active()) return;
                if (!idleAt) { idleAt = findSubsystem("Time").GameTime; idleZero = state().method.zeroWindows; return; }
                if (findSubsystem("Time").GameTime < idleAt + 11) return;
                check(state().intentionStatus === "awaitingTask" && state().method.zeroWindows === idleZero, "finished-task idle time does not accumulate method failures");
                phase = "shortRepeat";
            } else if (phase === "shortRepeat") {
                var input = read(); if (!input) return;
                if (active()) { cognition.AbandonTurn(input.id); return; }
                var d = maintain(input); d.method = method("The same brief attempt under the same conditions", "inventory");
                var result = commit(input, { reason: "Repeated short attempts must not reset no-progress accounting.", deliberation: d,
                    taskRequest: { operation: "submit", steps: [{ tool: "act_Wait", arguments: { duration: .2 } }] }, reviewAfter: .1 });
                if (result.receipt.deliberation === "method_has_no_progress") {
                    check(shortAttempts <= 2, "completed short attempts cannot bypass the no-progress bound");
                    phase = "capacity";
                } else {
                    if (++shortAttempts > 2) throw new Error("short completed attempts reset the no-progress bound indefinitely");
                    check(result.receipt.action === "accepted", "a bounded short retry uses actual task completion");
                }
            } else if (phase === "capacity") {
                var input = read(); if (!input) return;
                var result = commit(input, { reason: "Check that the working goal limit is not a lifetime limit.",
                    deliberation: { operation: "create", goalId: "capacity" + created, expectedRevision: 0, purpose: "Bounded reflection " + created,
                        source: "observation", why: "Retire the prior intention without a physical task.", previousGoalAction: "abandon" },
                    taskRequest: { operation: "none" }, reviewAfter: .1 });
                check(result.receipt.deliberation === "accepted", "retired goal capacity remains reusable: " + created);
                if (++created < 9) return;
                check(state().goals.length <= 8 && state().archivedGoals.length >= 3, "terminal goals have bounded durable history outside the working set");
                phase = "lateShortStart";
            } else if (phase === "lateShortStart") {
                var input = read(); if (!input) return;
                var d = maintain(input); d.method = method("A brief task whose reply can arrive after it ends", "inventory");
                var result = commit(input, { reason: "Exercise closure before late reply adoption.", deliberation: d,
                    taskRequest: { operation: "submit", steps: [{ tool: "act_Wait", arguments: { duration: .5 } }] }, reviewAfter: .1 });
                check(result.receipt.action === "accepted", "late-reply short attempt is running");
                phase = "lateShortFreeze";
            } else if (phase === "lateShortFreeze") {
                var input = read(); if (!input) return;
                heldInput = input; heldAt = findSubsystem("Time").GameTime;
                check(active(), "late reply is frozen while its old task is still active");
                phase = "lateShortDeliver";
            } else if (phase === "lateShortDeliver") {
                if (findSubsystem("Time").GameTime < heldAt + .8) return;
                check(!active(), "the old attempt ended without any intervening PrepareTurn call");
                var d = maintain(heldInput); d.method = method("A brief task whose reply can arrive after it ends", "inventory");
                d.taskDisposition = { operation: "retain", taskId: heldInput.cognitionCycle.taskId, expectedRevision: heldInput.cognitionCycle.authorizationRevision };
                var result = commit(heldInput, { reason: "Try to reissue exactly the same method after a late response.", deliberation: d,
                    taskRequest: { operation: "submit", steps: [{ tool: "act_Wait", arguments: { duration: .5 } }] }, reviewAfter: .1 });
                if (result.receipt.deliberation !== "method_has_no_progress") {
                    if (++lateShortAttempts > 2) throw new Error("late replies bypassed short-attempt closure");
                    phase = "lateShortFreeze"; return;
                }
                check(!active(), "adopting a late response cannot erase zero progress from the completed old attempt");
                Engine.Storage.WriteAllText("app:cognition-cycle-mechanics-state.json", cognition.ReadCycleStateJson());
                finish("passed");
            }
        } catch (error) { finish("failed", String(error)); }
    });
})();
