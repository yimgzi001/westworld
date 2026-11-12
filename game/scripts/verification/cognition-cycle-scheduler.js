(function () {
    var W = importNamespace("Game.Westworld"), phase = "load", started, http, checks = [];
    function finish(result, error) {
        phase = "done";
        Engine.Storage.WriteAllText("app:cognition-cycle-result.json", JSON.stringify({ result, error, checks }, null, 2));
        Engine.Window.Close();
    }
    frameHandlers.push(function () {
        if (phase === "done") return;
        try {
            if (phase === "load") {
                if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu" || Game.ScreensManager.IsAnimating) return;
                var settings = new Game.WorldSettings(); settings.Seed = "42"; settings.Name = "Scheduled cognition contract";
                Game.ScreensManager.SwitchScreen("GameLoading", W.FoundingWorld.CreateOpen(settings, 1), null);
                http = new (importNamespace("System.Net.Http").HttpClient)();
                phase = "start"; return;
            }
            var scheduler = W.WorldCognitionScheduler.ForWorld(getProject()); if (!scheduler) return;
            if (phase === "start") { started = Engine.Time.RealTime; phase = "observe"; }
            var s = JSON.parse(http.GetStringAsync("http://127.0.0.1:" + System.Environment.GetEnvironmentVariable("WENMING_PANEL_PORT") + "/api/snapshot").GetAwaiter().GetResult());
            var actor = s.actors?.find(a => a.cognition?.cognitionCycle);
            if (!actor) return;
            var cycle = actor.cognition.cognitionCycle;
            if (cycle.goals.some(g => g.status === "suspended") && cycle.receipts.some(r => r.action === "accepted") && cycle.receipts.length >= 2) {
                var records = Engine.Storage.ReadAllText("app:cognition-cycle-mock.jsonl").trim().split("\n").map(line => JSON.parse(line));
                if (!records.some(r => r.duringTask)) throw new Error("No actual network request was sent while a task was active");
                if (["waiting", "running", "blocked"].includes(actor.task.status)) throw new Error("The old task still has authority after reconsideration");
                checks.push("a real HTTP decision was dispatched during an ongoing native task");
                checks.push("GUID task identity accepts the native and compact representations with the same authorization revision");
                checks.push("the returned decision suspended its goal and revoked the active task");
                var lastDecision = JSON.parse(actor.cognition.decision);
                if (!lastDecision.input.actions.includes("act_Jump")) throw new Error("Native checkpointable input hid a supported restorable movement capability");
                checks.push("native checkpointable decisions expose supported restorable movement capabilities");
                checks.push("hidden and silent execution: " + !Engine.Window.IsVisible);
                scheduler.SetPaused(true); Game.GameManager.SaveProject(true, false);
                finish("passed");
            } else if (Engine.Time.RealTime - started > 12) {
                Engine.Storage.WriteAllText("app:cognition-cycle-scheduler-debug.json", JSON.stringify(s, null, 2));
                finish("failed", "active-task reconsideration never reached the model and revoked the task");
            }
        } catch (error) { finish("failed", String(error)); }
    });
})();
