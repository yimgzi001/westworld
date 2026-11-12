(function () {
    var W = importNamespace("Game.Westworld"), phase = "load", actor, tools, cognition, port, started, checks = [], at, motionId, expected, swimStart, technicalBefore, frozen, observerRef, jumpBase, jumpPeak, pilotActor, pilotTools, foreignGoal;
    function check(value, text) { if (!value) throw new Error(text); checks.push(text); }
    function read() { var raw = cognition.PrepareTurn(); return raw ? JSON.parse(raw) : null; }
    function state() { return JSON.parse(cognition.ReadCycleStateJson()); }
    function task() { return JSON.parse(tools.InvokeJson("task_status", "{}")).data; }
    function decision(input, metric, bearing) {
        var goal = input.cognitionCycle.goals.find(g => g.status === "active");
        return { reason: "Exercise a generic bounded method.", deliberation: {
            operation: goal ? "maintain" : "create", goalId: goal?.id ?? "inspect", expectedRevision: goal?.revision ?? 0,
            purpose: goal ? undefined : "Investigate my local surroundings", source: "observation", why: "Use the actual local situation.",
            method: { strategy: "Try a bounded method", expectedProgress: { metric, windowSeconds: 5 } }
        }, taskRequest: { operation: "submit", steps: [{ tool: "act_FaceTowards", arguments: { bearing } }] }, reviewAfter: .1 };
    }
    function finish(result, error) {
        Engine.Storage.WriteAllText("app:cognition-cycle-result.json", JSON.stringify({ result, error, checks, state: cognition ? state() : null }, null, 2));
        phase = "done"; Engine.Window.Close();
    }
    frameHandlers.push(function () {
        if (phase === "done") return;
        try {
            if (phase === "load") {
                if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu" || Game.ScreensManager.IsAnimating) return;
                var settings = new Game.WorldSettings(); settings.Seed = "42"; settings.Name = "Adaptive control mechanics";
                Game.ScreensManager.SwitchScreen("GameLoading", W.FoundingWorld.CreateOpen(settings, 1), null);
                started = Engine.Time.RealTime; phase = "spawn"; return;
            }
            if (Engine.Time.RealTime - started > 100) throw new Error("adaptive mechanics timeout");
            var project = getProject(); if (!project || findSubsystem("Time").GameTime < 4) return;
            if (phase === "spawn") {
                actor = Array.from(project.Entities).find(e => e.ValuesDictionary.DatabaseObject.Name === "WestworldProducer"); if (!actor) return;
                var body = actor.FindComponent("Body", true), p = body.Position;
                var terrain = findSubsystem("Terrain"), x = Math.floor(p.X), y = Math.floor(p.Y), z = Math.floor(p.Z);
                for (var dx = -6; dx <= 6; dx++) for (var dz = -6; dz <= 6; dz++) {
                    terrain.ChangeCell(x + dx, y - 1, z + dz, 1, true, null);
                    for (var h = 0; h < 4; h++) terrain.ChangeCell(x + dx, y + h, z + dz, 0, true, null);
                }
                body.Position = new Engine.Vector3(x + .5, y, z + .5);
                var observer = findSubsystem("Players").ComponentPlayers.get_Item(0);
                observer.ComponentBody.Position = new Engine.Vector3(x, y + 20, z);
                observer.ComponentLocomotion.IsCreativeFlyEnabled = true; observer.ComponentHealth.IsInvulnerable = true;
                observerRef = "person:" + observer.Entity.Id;
                port = new W.AtomicActions(actor); tools = new W.ActorTools(port); cognition = new W.ActorCognition(tools, "根据本人的处境和经历行动。");
                var input = read();
                check(!Engine.Window.IsVisible && Game.SettingsManager.SoundsVolume === 0 && Game.SettingsManager.MusicVolume === 0, "hidden and silent native execution");
                check(input.observation.somatic?.health > 0 && input.observation.somatic.air !== undefined,
                    "continuous bodily condition and breathing are available without a coarse well label");
                check(input.observation.navigation?.length > 0, "perception includes local terrain rather than only named resources");
                check(input.cognitionCycle.spatialOptions?.length > 0, "local movement options are related to the person's own visited places");
                check(input.actions.includes("act_Jump"), "safe native movement capabilities are not hidden by a production role filter");
                frozen = input; at = findSubsystem("Time").GameTime;
                var p = body.Position, f = body.Matrix.Forward;
                observer.ComponentBody.Position = new Engine.Vector3(p.X + 3 * f.X, p.Y, p.Z + 3 * f.Z);
                phase = "seenDuringFlight";
            } else if (phase === "seenDuringFlight") {
                if (findSubsystem("Time").GameTime < at + 1) return;
                read();
                check(state().visualEvents?.some(e => e.contacts.some(c => c.reference === observerRef)),
                    "a short-lived personally visible event is retained while its decision is frozen");
                var observer = findSubsystem("Players").ComponentPlayers.get_Item(0), p = actor.FindComponent("Body", true).Position;
                observer.ComponentBody.Position = new Engine.Vector3(p.X, p.Y + 20, p.Z);
                at = findSubsystem("Time").GameTime; phase = "goneDuringFlight";
            } else if (phase === "goneDuringFlight") {
                if (findSubsystem("Time").GameTime < at + 1) return;
                read();
                var result = JSON.parse(cognition.CommitTurn(frozen.id, JSON.stringify({ reason: "An older decision cannot consume later sight.",
                    deliberation: { operation: "none", source: "observation", why: "Retain the current evidence boundary." },
                    taskRequest: { operation: "none" }, reviewAfter: .1 })));
                check(result.receipt.deliberation === "accepted", "an unrelated frozen decision may still be adopted");
                phase = "retainedSight";
            } else if (phase === "retainedSight") {
                var input = read(); if (!input) return;
                check(!input.observation.entities.some(e => e.reference === observerRef)
                    && input.cognitionCycle.visualEvents.some(e => e.contacts.some(c => c.reference === observerRef)),
                    "the next input retains what was actually seen without tracking its now-hidden position");
                var accepted = JSON.parse(cognition.CommitTurn(input.id, JSON.stringify({ reason: "Acknowledge only personally presented events.",
                    deliberation: { operation: "none", source: "observation", why: "These observations were presented to me." },
                    taskRequest: { operation: "none" }, handled: input.cognitionCycle.visualEvents.map(e => e.id), reviewAfter: .1 })));
                check(accepted.ok && accepted.receipt.deliberation === "accepted", "presented visual events can be acknowledged alongside received messages");
                phase = "smallInjury";
            } else if (phase === "smallInjury") {
                var health = actor.FindComponent("Health", true);
                health.Injure(new Game.Injury(.005, null, true, "verification impact"));
                var observer = findSubsystem("Players").ComponentPlayers.get_Item(0), p = actor.FindComponent("Body", true).Position;
                observer.ComponentBody.Position = new Engine.Vector3(p.X + 2, p.Y, p.Z);
                health.Injure(new Game.FireInjury(.005, observer));
                observer.ComponentBody.Position = new Engine.Vector3(p.X, p.Y + 20, p.Z);
                observer.ComponentLocomotion.IsCreativeFlyEnabled = true;
                observer.ComponentHealth.IsInvulnerable = true;
                var input = read(); if (!input) return;
                check(input.reasons.includes("newInjury") && input.cognitionCycle.injuries.length > 0, "a sub-one-percent physical injury is retained and wakes reconsideration");
                check(input.cognitionCycle.bodyTrajectory.injuryLoss >= .009, "recent bodily cost is explicit across method changes");
                check(!JSON.stringify(input).includes("verification impact"), "private causal audit text is not inserted into personal perception");
                check(input.cognitionCycle.injuries.at(-1).directionX === undefined,
                    "persistent damage cannot reveal the current position of its hidden original attacker");
                var d = decision(input, "position", 0);
                d.taskRequest.steps = [{ tool: "act_Jump", arguments: {} }, { tool: "act_Wait", arguments: { duration: .7 } }];
                jumpBase = jumpPeak = actor.FindComponent("Body", true).Position.Y;
                check(JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d))).receipt.action === "accepted",
                    "a component-supported restorable jump can be admitted through cognition");
                phase = "jump";
            } else if (phase === "jump") {
                var body = actor.FindComponent("Body", true);
                jumpPeak = Math.max(jumpPeak, body.Position.Y);
                if (["running", "waiting", "blocked"].includes(task().status) || !port.Queries.ReadSomatic().Grounded) return;
                check(jumpPeak > jumpBase + .2, "newly exposed capability produces a real native jump rather than an imagined effect");
                var input = read(); if (!input) return;
                var d = decision(input, "position", 20);
                d.taskRequest.steps = [{ tool: "act_MoveTo", arguments: { bearing: 0, distance: 2, range: .15 } }];
                var body = actor.FindComponent("Body", true), p = body.Position, f = body.Matrix.Forward;
                expected = { x: p.X + 2 * f.X, z: p.Z + 2 * f.Z };
                body.Rotation = Engine.Quaternion.CreateFromAxisAngle(Engine.Vector3.UnitY, 1.3);
                var result = JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d)));
                check(result.receipt.action === "accepted", "rotation while thinking does not discard a bounded anchored movement");
                motionId = state().authorizedTask; at = findSubsystem("Time").GameTime; phase = "motion";
            } else if (phase === "motion") {
                if (["running", "waiting", "blocked"].includes(task().status)) return;
                var p = actor.FindComponent("Body", true).Position;
                check(task().status === "succeeded" && Math.hypot(p.X - expected.x, p.Z - expected.z) < .7,
                    "native movement reaches the original observed endpoint after the body rotates");
                var input = read(); if (!input) return;
                var d = decision(input, "position", 90), body = actor.FindComponent("Body", true), f = body.Matrix.Forward;
                expected = { x: p.X + f.X, z: p.Z + f.Z };
                d.taskRequest.steps = [{ tool: "act_FaceTowards", arguments: { bearing: 90 }, next: 2 },
                    { tool: "act_MoveTo", arguments: { bearing: 0, distance: 4 } },
                    { tool: "act_MoveTo", arguments: { bearing: 0, distance: 1, range: .15 } }];
                check(JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d))).receipt.action === "accepted", "multi-step movement is admitted in a single observation frame");
                phase = "sequence";
            } else if (phase === "sequence") {
                if (["running", "waiting", "blocked"].includes(task().status)) return;
                var p = actor.FindComponent("Body", true).Position;
                check(task().status === "succeeded" && Math.hypot(p.X - expected.x, p.Z - expected.z) < .7,
                    "turns and skipped movement steps cannot alter the selected world destination");
                var input = read(); if (!input) return;
                technicalBefore = state().activity.unproductiveAttempts;
                var d = decision(input, "inventory", 0);
                d.taskRequest.steps = [{ tool: "act_Wait", arguments: { duration: .2 } }];
                check(JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d))).receipt.action === "accepted", "a technical-conflict check is scheduled through normal authority");
                port.Start(new W.Wait(.4)); at = findSubsystem("Time").GameTime; phase = "technicalFirst";
            } else if (phase === "technicalFirst" || phase === "technicalSecond") {
                if (findSubsystem("Time").GameTime < at + .7) return;
                var input = read(); if (!input) return;
                check(task().steps[0].atomicFailure === "busy", "a real native host conflict produces technical Busy feedback");
                if (phase === "technicalFirst") {
                    var d = decision(input, "inventory", 0); d.taskRequest.steps = [{ tool: "act_Wait", arguments: { duration: .2 } }];
                    check(JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d))).receipt.action === "accepted", "a second technical-conflict check is independently admitted");
                    port.Start(new W.Wait(.4)); at = findSubsystem("Time").GameTime; phase = "technicalSecond"; return;
                }
                check(state().activity.unproductiveAttempts <= technicalBefore, "technical failures do not become cross-method world stagnation evidence");
                var d = decision(input, "inventory", 0);
                d.taskRequest.steps = [{ tool: "act_Wait", arguments: { duration: .2 } }];
                var result = JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d)));
                check(result.receipt.action === "accepted", "first zero-output attempt starts normally"); phase = "firstZero";
                actor.FindComponent("Health", true).Injure(new Game.Injury(.02, null, true, "verification method cost"));
            } else if (phase === "firstZero" || phase === "secondZero") {
                if (["running", "waiting", "blocked"].includes(task().status)) return;
                var input = read(); if (!input) return;
                if (phase === "firstZero") check(input.cognitionCycle.methodAssessment.injuryLoss >= .019,
                    "a method's bodily costs remain visible even when unrelated progress is possible");
                if (phase === "secondZero") {
                    check(input.cognitionCycle.activity?.unproductiveAttempts >= 2,
                        "changing action parameters cannot clear accumulated lack of actual progress");
                    check(input.reasons.includes("stalledActivity"), "cross-method stagnation becomes an explicit reconsideration reason");
                    cognition.AbandonTurn(input.id);
                    var body = actor.FindComponent("Body", true), p = body.Position, terrain = findSubsystem("Terrain");
                    swimStart = p.Y;
                    for (var dx = -2; dx <= 2; dx++) for (var dz = -2; dz <= 2; dz++) for (var h = 0; h <= 2; h++)
                        terrain.ChangeCell(Math.floor(p.X) + dx, Math.floor(p.Y) + h, Math.floor(p.Z) + dz, 18, true, null);
                    actor.FindComponent("Health", true).Air = .35;
                    phase = "immersed"; return;
                }
                var d = decision(input, "inventory", 0);
                d.taskRequest.steps = [{ tool: "act_Wait", arguments: { duration: .3 } }];
                check(JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d))).receipt.action === "accepted", "a distinct bounded attempt remains the person's choice");
                phase = "secondZero";
            } else if (phase === "immersed") {
                if (actor.FindComponent("Body", true).ImmersionFactor < .5) return;
                var input = read(); if (!input) return;
                check(input.reasons.includes("bodyRiskChanged") && input.observation.somatic.immersion > .5,
                    "immersion and breathing changes wake reconsideration before the health band changes");
                check(input.observation.navigation.some(route => route.surface === "WaterBlock" && route.passage === "fluidObserved"),
                    "visible fluid is not misreported as a solid obstruction");
                var d = decision(input, "position", 0);
                d.taskRequest.steps = [{ tool: "act_MoveTo", arguments: { bearing: 0, distance: 0, vertical: 1.5, range: .15, timeout: 5 } }];
                check(JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d))).receipt.action === "accepted", "bounded vertical intent uses the same native movement capability");
                phase = "swimming";
            } else if (phase === "swimming") {
                if (["running", "waiting", "blocked"].includes(task().status)) return;
                check(actor.FindComponent("Body", true).Position.Y > swimStart + .8,
                    "vertical intent produces actual displacement through native swimming");
                var input = read(); if (!input) return;
                check(state().method.zeroWindows === 0 && state().activity.unproductiveAttempts === 0,
                    "actual three-dimensional displacement counts as progress across methods");
                var d = decision(input, "position", 0);
                d.taskRequest.steps = [{ tool: "act_MoveTo", arguments: { bearing: 0, distance: 0, vertical: 1.5, range: .15, timeout: 5 } }];
                check(JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d))).receipt.action === "accepted", "further physical progress is not banned after useful vertical movement");
                phase = "surface";
            } else if (phase === "surface") {
                if (["running", "waiting", "blocked"].includes(task().status)) return;
                var input = read(); if (!input) return;
                check(input.observation.somatic.air > .8 && state().activity.unproductiveAttempts === 0,
                    "actual breathing recovery is recognized without prescribing a life goal");
                cognition.AbandonTurn(input.id);
                // Re-establish a dry, level native floor; this tests the execution contract, not a rescue policy.
                var body = actor.FindComponent("Body", true), p = body.Position, terrain = findSubsystem("Terrain");
                for (var dx = -6; dx <= 6; dx++) for (var dz = -6; dz <= 6; dz++) {
                    terrain.ChangeCell(Math.floor(p.X)+dx, Math.floor(p.Y)-1, Math.floor(p.Z)+dz, 1, true, null);
                    for (var h = 0; h < 4; h++) terrain.ChangeCell(Math.floor(p.X)+dx, Math.floor(p.Y)+h, Math.floor(p.Z)+dz, 0, true, null);
                }
                phase = "guardSubmit";
            } else if (phase === "guardSubmit") {
                if (!port.Queries.ReadSomatic().Grounded) return;
                var input = read(); if (!input) return;
                var d = decision(input, "position", 0);
                d.taskRequest.steps = [
                    { tool: "act_Wait", arguments: { duration: 20 }, interrupt_on: "body_worsened", next: 3,
                        fallback: { failure: "movement_interrupted", step: 1 } },
                    { tool: "act_MoveTo", arguments: { bearing: 0, distance: 2, range: .15 } },
                    { tool: "act_Wait", arguments: { duration: 4 } }
                ];
                check(JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d))).receipt.action === "accepted",
                    "a person can authorize a bounded bodily-change fallback before any injury");
                phase = "guardFreeze";
            } else if (phase === "guardFreeze") {
                if (task().status !== "running") return;
                frozen = read(); if (!frozen) return;
                motionId = task().taskId; at = findSubsystem("Time").GameTime;
                expected = actor.FindComponent("Body", true).Position;
                actor.FindComponent("Health", true).Injure(new Game.Injury(.03, null, true, "verification guarded wait"));
                phase = "guardTriggered";
            } else if (phase === "guardTriggered") {
                if (task().steps[0].atomicFailure !== "movementInterrupted") {
                    if (findSubsystem("Time").GameTime > at + .3) throw new Error("physical worsening did not interrupt an authorized wait promptly");
                    return;
                }
                check(task().steps[0].interruptedBy === "body_worsened", "the interruption records its actual somatic trigger");
                check(findSubsystem("Time").GameTime - at < .3, "a chosen fallback triggers during the frozen model request without waiting for another reply");
                var d = decision(frozen, "position", 0);
                d.deliberation.taskDisposition = { operation: "revoke", taskId: motionId, expectedRevision: frozen.cognitionCycle.authorizationRevision };
                d.taskRequest = { operation: "replace", steps: [{ tool: "act_Wait", arguments: { duration: 2 } }] };
                var result = JSON.parse(cognition.CommitTurn(frozen.id, JSON.stringify(d)));
                check(result.receipt.deliberation === "task_authorization_changed" && task().taskId === motionId,
                    "a stale model reply cannot revoke an already triggered emergency branch");
                phase = "guardMoved";
            } else if (phase === "guardMoved") {
                if (["running", "waiting", "blocked"].includes(task().status)) return;
                var p = actor.FindComponent("Body", true).Position;
                check(Math.hypot(p.X-expected.X,p.Z-expected.Z)>1.3 && task().steps[1].status === "succeeded",
                    "the preauthorized branch produces native movement rather than only changing cognition");
                check(task().steps[0].attempts === 1, "the interrupted wait is never retried after its fallback completes");
                phase = "guardTailSubmit";
            } else if (phase === "guardTailSubmit") {
                var input = read(); if (!input) return;
                var d = decision(input, "position", 0);
                d.taskRequest.steps = [
                    { tool: "act_MoveTo", arguments: { bearing: 0, distance: 1, range: .15 },
                        interrupt_on: "body_worsened", next: 3, fallback: { failure: "movement_interrupted", step: 1 } },
                    { tool: "act_MoveTo", arguments: { bearing: 90, distance: 2, range: .15 } },
                    { tool: "act_Wait", arguments: { duration: .5 } }
                ];
                check(JSON.parse(cognition.CommitTurn(input.id, JSON.stringify(d))).receipt.action === "accepted",
                    "guarded motion with a bounded post-action watch is admitted");
                phase = "guardTailAwait";
            } else if (phase === "guardTailAwait") {
                if (task().steps[0].status !== "succeeded") return;
                check(["waiting", "running"].includes(task().status) && task().steps[1].name === "Wait"
                    && task().steps[1].guardBaseline, "the authorized contingency survives successful short movement while the next model reply is pending");
                at = findSubsystem("Time").GameTime;
                expected = actor.FindComponent("Body", true).Position;
                actor.FindComponent("Health", true).Injure(new Game.Injury(.03, null, true, "verification post-action watch"));
                phase = "guardTailTriggered";
            } else if (phase === "guardTailTriggered") {
                if (["waiting", "running", "blocked"].includes(task().status)) return;
                check(task().steps[0].attempts === 1 && task().steps[0].status === "succeeded"
                    && task().steps[1].interruptedBy === "body_worsened" && task().steps[2].status === "succeeded",
                    "post-action harm triggers the remapped fallback without replaying the successful movement");
                var p = actor.FindComponent("Body", true).Position;
                check(Math.hypot(p.X-expected.X,p.Z-expected.Z)>1.3, "a post-action watch causes actual fallback displacement during the thinking gap");
                var value = Game.BlocksManager.GetBlockIndex("CookedMeatBlock", true);
                var p = actor.FindComponent("Body", true).Position;
                findSubsystem("Pickables").AddPickable(value, 1, new Engine.Vector3(p.X+.4,p.Y+.65,p.Z+.4),null,null);
                actor.FindComponent("Nutrition", true).Food = .4;
                var accepted = JSON.parse(tools.InvokeJson("submit_plan", JSON.stringify({ steps: [
                    { tool: "act_FaceTowards", arguments: { bearing: 0 }, interrupt_on: "body_worsened", watch_seconds: .1,
                        fallback: { failure: "movement_interrupted", step: 3 } },
                    { tool: "act_PickUp", arguments: { food_value: value, count: 1 }, minimum_affected: 1 },
                    { tool: "act_Eat", arguments: { count: 1 }, item_from_step: 1, minimum_affected: 1, next: 4 },
                    { tool: "act_Wait", arguments: { duration: .1 } }
                ] })));
                check(accepted.ok, "inserting a guard watch preserves the original PickUp-to-Eat output binding");
                phase = "guardBoundFood";
            } else if (phase === "guardBoundFood") {
                if (["waiting", "running", "blocked"].includes(task().status)) return;
                check(task().status === "succeeded" && task().steps[2].affectedCount === 1 && task().steps[3].affectedCount === 1
                    && actor.FindComponent("Nutrition", true).Food > .45,
                    "the remapped output is actually picked up and eaten once after a guarded motion");
                var locomotion = actor.FindComponent("Locomotion", true);
                locomotion.StunTime = .5;
                expected = actor.FindComponent("Body", true).Position;
                at = findSubsystem("Time").GameTime;
                check(JSON.parse(tools.InvokeJson("submit_plan", JSON.stringify({ steps: [
                    { tool: "act_MoveTo", arguments: { bearing: 0, distance: 2, range: .15, timeout: 3 } }
                ] }))).ok, "a chosen movement can await temporary recovery within its original timeout");
                phase = "stunWait";
            } else if (phase === "stunWait") {
                if (findSubsystem("Time").GameTime < at + .15) return;
                check(task().status === "running" && task().steps[0].attempts === 1,
                    "short native incapacity does not consume an immediate precondition failure or a new attempt");
                check(port.Queries.ReadSomatic().MovementStunSeconds > .2, "temporary loss of bodily movement control is personally sensed");
                phase = "stunMoved";
            } else if (phase === "stunMoved") {
                if (["waiting", "running", "blocked"].includes(task().status)) return;
                var p = actor.FindComponent("Body", true).Position;
                check(task().status === "succeeded" && task().steps[0].attempts === 1 && Math.hypot(p.X-expected.X,p.Z-expected.Z)>1.3,
                    "the same authorized action actually moves after native control recovers");
                actor.FindComponent("Locomotion", true).StunTime = 2;
                check(JSON.parse(tools.InvokeJson("submit_plan", JSON.stringify({ steps: [
                    { tool: "act_MoveTo", arguments: { bearing: 90, distance: 2, timeout: .3 } }
                ] }))).ok, "a longer incapacity is still admitted with a finite declared timeout");
                phase = "stunTimeout";
            } else if (phase === "stunTimeout") {
                if (["waiting", "running", "blocked"].includes(task().status)) return;
                check(task().steps[0].atomicFailure === "timedOut" && task().steps[0].attempts === 1,
                    "recovery waiting expires without resetting the action timeout or retry count");
                pilotActor = Game.DatabaseManager.CreateEntity(project,"Horse_White",true);
                var p=actor.FindComponent("Body",true).Position, terrain=findSubsystem("Terrain");
                pilotActor.FindComponent("Body",true).Position=new Engine.Vector3(p.X+1,p.Y,p.Z);
                for(var dx=-3;dx<=3;dx++) for(var dz=-3;dz<=3;dz++) {
                    terrain.ChangeCell(Math.floor(p.X)+dx,Math.floor(p.Y)-1,Math.floor(p.Z)+dz,1,true,null);
                    for(var h=0;h<4;h++) terrain.ChangeCell(Math.floor(p.X)+dx,Math.floor(p.Y)+h,Math.floor(p.Z)+dz,0,true,null);
                }
                project.AddEntity(pilotActor);
                var selector=pilotActor.FindComponent("BehaviorSelector",true);
                for(var behavior of Array.from(selector.m_behaviors)) behavior.IsActive=false;
                selector.m_behaviors.Clear();
                pilotTools=new W.ActorTools(new W.AtomicActions(pilotActor));
                new W.ActorCognition(pilotTools,"Native control contract check.");
                at=findSubsystem("Time").GameTime;
                phase="pilotSettle";
            } else if (phase === "pilotSettle") {
                if(findSubsystem("Time").GameTime<at+1) return;
                pilotActor.FindComponent("Pathfinding",true).Stop();
                var result=JSON.parse(pilotTools.InvokeJson("act_MoveTo",JSON.stringify({bearing:0,distance:3,timeout:5})));
                check(result.ok,"a component-supported pilot movement begins under the same recovery contract");
                phase="pilotTakeover";
            } else if (phase === "pilotTakeover" || phase === "pilotCancelTakeover") {
                var pilot=pilotActor.FindComponent("Pilot",true), path=pilotActor.FindComponent("Pathfinding",true);
                if(!pilot.Destination || !path.Destination) return;
                var p=pilotActor.FindComponent("Body",true).Position;
                foreignGoal={x:p.X+4,y:p.Y,z:p.Z+3};
                pilot.SetDestination(new Engine.Vector3(foreignGoal.x,foreignGoal.y,foreignGoal.z),.6,.2,false,false,false,null);
                at=findSubsystem("Time").GameTime;
                if(phase==="pilotTakeover") {
                    pilotActor.FindComponent("Locomotion",true).StunTime=.5;
                    phase="pilotPreserveStun";
                } else {
                    pilotTools.InvokeJson("cancel_action","{}"); phase="pilotPreserveCancel";
                }
            } else if (phase === "pilotPreserveStun" || phase === "pilotPreserveCancel") {
                if(findSubsystem("Time").GameTime<at+.25) return;
                var pilot=pilotActor.FindComponent("Pilot",true), d=pilot.Destination;
                check(d && Math.hypot(d.X-foreignGoal.x,d.Z-foreignGoal.z)<.001,
                    phase==="pilotPreserveStun" ? "temporary recovery handling preserves a pilot target taken over by another controller, including delayed path state transitions"
                        : "cancelling an owned movement preserves an externally replaced pilot goal");
                if(phase==="pilotPreserveStun") {
                    var status=JSON.parse(pilotTools.InvokeJson("action_status","{}")).data;
                    check(status.status==="failed" && status.failure==="movementInterrupted","loss of movement control ends the old action without taking over the new controller");
                    pilotActor.FindComponent("Pathfinding",true).Stop();
                    pilotActor.FindComponent("Locomotion",true).StunTime=0;
                    check(JSON.parse(pilotTools.InvokeJson("act_MoveTo",JSON.stringify({bearing:0,distance:3,timeout:5}))).ok,
                        "another owned pilot movement can be exercised independently");
                    phase="pilotCancelTakeover"; return;
                }
                pilotActor.FindComponent("Pathfinding",true).Stop();
                expected=pilotActor.FindComponent("Body",true).Position;
                check(JSON.parse(pilotTools.InvokeJson("act_MoveTo",JSON.stringify({bearing:0,distance:1,range:.15,timeout:5}))).ok,
                    "a pilot movement can still complete normally after control handoff");
                phase="pilotNormalFinish";
            } else if(phase==="pilotNormalFinish") {
                var status=JSON.parse(pilotTools.InvokeJson("action_status","{}")).data;
                if(status.status==="running") return;
                var p=pilotActor.FindComponent("Body",true).Position;
                check(status.status==="succeeded" && Math.hypot(p.X-expected.X,p.Z-expected.Z)>.5,
                    "native pilot arrival remains successful rather than being mistaken for lost ownership");
                finish("passed");
            }
        } catch (error) { finish("failed", String(error)); }
    });
})();
