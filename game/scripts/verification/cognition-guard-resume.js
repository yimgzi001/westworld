(function () {
    var W = importNamespace("Game.Westworld"), phase = "load", http, scheduler, clock, directory, actor, at, began = Engine.Time.RealTime;
    var stage = Engine.Storage.FileExists("app:cognition-guard-checkpoint3.json") ? 4
        : Engine.Storage.FileExists("app:cognition-guard-checkpoint2.json") ? 3
        : Engine.Storage.FileExists("app:cognition-guard-checkpoint1.json") ? 2 : 1;
    var prior = stage > 1 ? JSON.parse(Engine.Storage.ReadAllText("app:cognition-guard-checkpoint" + (stage-1) + ".json")) : null;
    var checks = prior?.checks ?? [];
    function check(value, text) { if (!value) throw new Error(text); checks.push(text); }
    function write(name, value) { Engine.Storage.WriteAllText("app:"+name, JSON.stringify(value,null,2)); }
    function snap() { return JSON.parse(http.GetStringAsync("http://127.0.0.1:"+System.Environment.GetEnvironmentVariable("WENMING_PANEL_PORT")+"/api/snapshot").GetAwaiter().GetResult()); }
    function current(s) { return s.actors.find(a => a.id === actor.Id); }
    function save() {
        Game.GameManager.SaveProject(true,false);
        var doc = new (importNamespace("System.Xml").XmlDocument)();
        doc.LoadXml(Engine.Storage.ReadAllText(Engine.Storage.CombinePaths(directory,"Project.xml")));
        return JSON.parse(doc.SelectSingleNode("/Project/WestworldFoundingSave/WestworldObservation").InnerText);
    }
    function checkpoint(number) {
        scheduler.SetPaused(true);
        var payload = save(), saved = payload.actors.find(a => a.id === actor.Id);
        check(saved.task?.plan[0].interruptOn === "body_worsened", "native save retains the model-authored guard at stage "+stage);
        check(saved.task.view.steps[0].guardBaseline.health > 0, "native save retains the original bodily baseline at stage "+stage);
        if (stage===2) {
            check(saved.task.continuation?.awaitRecovery===true && actor.FindComponent("Locomotion",true).StunTime>1,
                "native save captures a running recovery-wait with its continuation mode and remaining bodily incapacity");
        }
        write("cognition-guard-checkpoint"+number+".json", {result:"passed", checks, worldDirectory:directory,
            actorId:actor.Id, time:payload.time, task:saved.task.view, cycle:saved.cognition.cognitionCycle,
            health:actor.FindComponent("Health",true).Health, stun:actor.FindComponent("Locomotion",true).StunTime});
        phase="done"; Engine.Window.Close();
    }
    frameHandlers.push(function () {
        if (phase === "done") return;
        try {
            if (Engine.Time.RealTime-began>75) throw new Error("guard persistence timeout at "+phase);
            if (phase === "load") {
                if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen)!=="MainMenu" || Game.ScreensManager.IsAnimating) return;
                var settings = new Game.WorldSettings(); settings.Seed="42"; settings.Name="Native contingency persistence";
                var world = prior ? Game.WorldsManager.GetWorldInfo(prior.worldDirectory) : W.FoundingWorld.CreateOpen(settings,1);
                Game.ScreensManager.SwitchScreen("GameLoading",world,null);
                http=new (importNamespace("System.Net.Http").HttpClient)(); phase="start"; return;
            }
            if (phase === "start") {
                var project=getProject(); if (!project) return;
                scheduler=W.WorldCognitionScheduler.ForWorld(project); if (!scheduler) return;
                clock=findSubsystem("Time"); directory=findSubsystem("GameInfo").DirectoryName;
                actor=Array.from(project.Entities).find(e=>e.ValuesDictionary.DatabaseObject.Name==="WestworldProducer"); if (!actor) return;
                var s=snap(), a=current(s); if (!a?.cognition?.cognitionCycle) return;
                check(!Engine.Window.IsVisible && Game.SettingsManager.SoundsVolume===0 && Game.SettingsManager.MusicVolume===0,
                    "guard persistence runs hidden and silent at stage "+stage);
                if (prior) {
                    check(JSON.parse(scheduler.StatusJson()).state==="userPaused" && clock.GameTime===prior.time,
                        "native resume holds the exact paused simulation time at stage "+stage);
                    check(JSON.stringify(a.task)===JSON.stringify(prior.task), "task cursor, guard baseline and trigger restore exactly at stage "+stage);
                    check(JSON.stringify(a.cognition.cognitionCycle)===JSON.stringify(prior.cycle), "cognition authority and effects restore exactly at stage "+stage);
                    check(actor.FindComponent("Health",true).Health===prior.health, "settled injury is not replayed at stage "+stage);
                    check(Math.abs(actor.FindComponent("Locomotion",true).StunTime-prior.stun)<.00001,
                        "temporary native incapacity restores without advancing or vanishing at stage "+stage);
                    scheduler.SetPaused(false); at=clock.GameTime;
                    actor.FindComponent("Health",true).Injure(new Game.Injury(.03,null,true,"verification restored contingency"));
                    phase=stage===2 ? "trigger" : stage===3 ? "recoveryContinuation" : "finishBranch";
                } else {
                    var p=actor.FindComponent("Body",true).Position, t=findSubsystem("Terrain");
                    for (var dx=-5;dx<=5;dx++) for (var dz=-5;dz<=5;dz++) {
                        t.ChangeCell(Math.floor(p.X)+dx,Math.floor(p.Y)-1,Math.floor(p.Z)+dz,1,true,null);
                        for (var h=0;h<4;h++) t.ChangeCell(Math.floor(p.X)+dx,Math.floor(p.Y)+h,Math.floor(p.Z)+dz,0,true,null);
                    }
                    phase="armed";
                }
                return;
            }
            var a=current(snap()); if (!a) return;
            if (phase==="armed" && a.task?.status==="running" && a.task.currentStep===1) {
                check(a.task.steps[0].status==="succeeded" && a.task.steps[1].name==="Wait"
                    && a.task.steps[1].guardBaseline && a.task.steps[1].interruptedBy===undefined,
                    "a successful motion retains its original guard while waiting for the next decision");
                actor.FindComponent("Locomotion",true).StunTime = 3;
                checkpoint(1);
            } else if (phase==="trigger") {
                if (a.task.steps[1].interruptedBy==="body_worsened" && at!==null) {
                    // The panel is sampled; reaction time comes from the executor's native step receipt.
                    var event = a.cognition.cognitionCycle.episodes.find(e => e.id === "task:"+a.task.taskId.replaceAll("-","")+":1:1");
                    check(event && event.at-at<.3,"restored guarded action reacts within 0.3 simulated seconds"); at=null;
                }
                if (a.task.currentStep!==2 || a.task.status!=="running") return;
                check(a.task.steps[0].attempts===1 && a.task.steps[2].attempts===1,
                    "the restored guard enters its chosen movement while waiting for native recovery");
                check(a.cognition.cognitionCycle.authorizationRevision>prior.cycle.authorizationRevision, "triggered branch advances cognitive authority");
                checkpoint(2);
            } else if (phase==="recoveryContinuation") {
                if(clock.GameTime-at<.5) return;
                check(actor.FindComponent("Locomotion",true).StunTime>1 && a.task.currentStep===2
                    && a.task.status==="running" && a.task.steps[2].attempts===1,
                    "a saved recovery-wait remains active while movement control is still absent, without starting another attempt");
                phase="recoveredMove";
            } else if (phase==="recoveredMove") {
                if(a.task.currentStep!==3 || a.task.status!=="running") return;
                check(a.task.steps[2].status==="succeeded" && a.task.steps[2].attempts===1,
                    "the saved movement actually completes once control returns");
                checkpoint(3);
            } else if (phase==="finishBranch") {
                check(a.task.taskId===prior.task.taskId && a.task.steps[0].attempts===1 && a.task.steps[1].attempts===1 && a.task.steps[2].attempts===1,
                    "completed guard and movement remain settled after branch resume");
                // Keep the result compact: retain one check for the invariant while observing its actual completion.
                if (["running","waiting","blocked"].includes(a.task.status)) { checks.pop(); return; }
                check(a.task.status==="succeeded" && a.task.steps[3].attempts===1, "the saved remainder completes without retriggering or replaying prior actions");
                write("cognition-cycle-result.json",{result:"passed",checks,task:a.task}); phase="done"; Engine.Window.Close();
            }
        } catch (error) { write("cognition-cycle-result.json",{result:"failed",error:String(error),checks}); phase="done"; Engine.Window.Close(); }
    });
})();
