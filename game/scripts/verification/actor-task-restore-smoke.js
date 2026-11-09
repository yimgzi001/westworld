var W = importNamespace("Game.Westworld");
var phase=0, checks=[], player, tools, inventory, food, initialFood, taskId;
function yes(condition, name) { if (!condition) throw new Error(name); checks.push(name); }
function task() { return JSON.parse(tools.InvokeJson("task_status", "{}")).data; }
function countFood() {
    var count=0;
    for (var i=0;i<inventory.SlotsCount;i++) if (inventory.GetSlotValue(i) === food) count += inventory.GetSlotCount(i);
    return count;
}
function finish(result, detail) {
    phase=-1;
    Engine.Storage.WriteAllText("app:actor-task-result.json", JSON.stringify({result,detail,checks},null,2));
}
frameHandlers.push(function () {
    try {
        if (phase === 0) {
            if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu" || Game.ScreensManager.IsAnimating) return;
            var archive=Engine.Storage.OpenFile("app:actor-task.scworld", Engine.OpenFileMode.Read);
            var directory;
            try { directory=Game.WorldsManager.ImportWorld(archive); } finally { archive.Dispose(); }
            var world=Game.WorldsManager.GetWorldInfo(directory);
            var screen=Game.ScreensManager.m_screens.get_Item("Game");
            Game.GameManager.LoadProject(world,screen.Children.Find("GamesWidget",true));
            var players=findSubsystem("Players").ComponentPlayers;
            yes(players.Count === 1,"loaded actor is available before world advancement");
            player=players.get_Item(0);
            inventory=player.ComponentMiner.Inventory;
            food=Game.BlocksManager.GetBlockIndex("CookedMeatBlock",true);
            initialFood=player.ComponentVitalStats.Food;
            var actor=player.Entity;
            tools=new W.ActorTools(new W.AtomicActions(actor),false);
            yes(countFood() === 2,"world and pickup inventory restored from the same archive");
            var pickables=findSubsystem("Pickables").Pickables;
            for (var i=0;i<pickables.Count;i++) {
                var p=pickables.get_Item(i);
                if (p.Value === food && p.Count === 0) throw new Error("depleted pickup was restored to the ground");
            }
            checks.push("depleted food did not respawn in the world");
            yes(tools.RestoreTaskCheckpoint(),"restore the saved task on the same actor");
            var restored=task();
            taskId=restored.taskId; var firstStep=restored.steps[0];
            yes(restored.currentStep === 1 && restored.completedSteps === 1
                && firstStep.attempts === 1 && firstStep.receivedItems[0].count === 1,
                "resume at next step with the original pickup receipt");
            Game.ScreensManager.SwitchScreen("Game");
            phase=2;
        } else if (phase === 2) {
            var ended=task();
            if (["waiting","running","blocked"].includes(ended.status)) return;
            yes(ended.taskId === taskId && ended.status === "succeeded" && ended.steps[0].attempts === 1
                && ended.steps[2].affectedCount === 1 && countFood() === 1 && player.ComponentVitalStats.Food > initialFood,
                "continue without replaying pickup or meal");
            yes(!tools.RestoreTaskCheckpoint() && countFood() === 1, "same checkpoint cannot be restored twice in one loaded world");
            finish("passed","task resumed in a fresh process");
        }
    } catch (error) { finish("failed",String(error)); }
});
