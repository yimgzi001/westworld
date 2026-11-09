var W = importNamespace("Game.Westworld");
var phase = 0, checks = [], time, terrain, tools, player, inv, x, y, z, food, stone, drop, turn;
function v(x,y,z) { return new Engine.Vector3(x,y,z); }
function yes(condition, name) { if (!condition) throw new Error(name); checks.push(name); }
function call(name,args) { return JSON.parse(tools.InvokeJson(name, JSON.stringify(args || {}))); }
function data(name,args) { var result=call(name,args); if (!result.ok) throw new Error(name+": "+result.error); return result.data; }
function done(task) { return ["waiting","running","blocked"].indexOf(task.status) < 0; }
function finish(result, detail) {
    phase = -1;
    Engine.Storage.WriteAllText("app:actor-task-result.json", JSON.stringify({result,detail,checks},null,2));
}
function foodCount() {
    var count=0;
    for (var i=0;i<inv.SlotsCount;i++) if (inv.GetSlotValue(i) === food) count += inv.GetSlotCount(i);
    return count;
}
function observeDrop() {
    var observed = data("observe");
    var item = observed.pickables.find(o => o.value === food);
    if (!item) throw new Error("food missing from observation: "+JSON.stringify(observed));
    return item.reference;
}
function putFood() {
    var at=player.ComponentBody.Position;
    drop=findSubsystem("Pickables").AddPickable(food,1,v(at.X+0.5,at.Y+0.7,at.Z+0.5),null,null);
    return observeDrop();
}
function eatPlan(reference) {
    return {steps:[
        {tool:"act_PickUp",arguments:{target:reference,count:1},minimum_affected:1},
        {tool:"act_Eat",arguments:{count:1},item_from_step:0,minimum_affected:1}
    ]};
}
frameHandlers.push(function () {
    try {
        if (phase === 0) {
            if (Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen) !== "MainMenu" || Game.ScreensManager.IsAnimating) return;
            var settings=new Game.WorldSettings();
            settings.Name="Actor Task Verification"; settings.Seed="42";
            settings.OriginalSerializationVersion=Game.VersionsManager.SerializationVersion;
            settings.ResetOptionsForNonCreativeMode(null);
            var world=Game.WorldsManager.CreateWorld(settings);
            var screen=Game.ScreensManager.m_screens.get_Item("Game");
            Game.GameManager.LoadProject(world,screen.Children.Find("GamesWidget",true));
            var d=new Game.PlayerData(getProject());
            d.Name="Task Fixture"; d.InputDevice=Game.WidgetInputDevice.Keyboard | Game.WidgetInputDevice.Mouse;
            findSubsystem("Players").AddPlayerData(d);
            Game.ScreensManager.SwitchScreen("Game");
            time=findSubsystem("Time"); phase=1;
        } else if (phase === 1) {
            var players=findSubsystem("Players").ComponentPlayers;
            if (players.Count < 1 || time.GameTime < 4) return;
            player=players.get_Item(0);
            var origin=player.ComponentBody.Position;
            x=Math.floor(origin.X)+8; y=Math.floor(origin.Y)+6; z=Math.floor(origin.Z);
            terrain=findSubsystem("Terrain");
            stone=Game.BlocksManager.GetBlockIndex("GraniteBlock",true);
            food=Game.BlocksManager.GetBlockIndex("CookedMeatBlock",true);
            for (var dx=-4;dx<=5;dx++) for (var dz=-4;dz<=5;dz++) {
                terrain.ChangeCell(x+dx,y,z+dz,stone,true,null);
                for (var h=1;h<=4;h++) terrain.ChangeCell(x+dx,y+h,z+dz,0,true,null);
            }
            player.ComponentBody.Position=v(x+.5,y+1.02,z+.5);
            player.ComponentBody.Velocity=v(0,0,0);
            player.ComponentVitalStats.Food=.2;
            var actions=new W.AtomicActions(player.Entity);
            tools=new W.ActorTools(actions,false);
            inv=player.ComponentMiner.Inventory;
            for (var i=0;i<inv.SlotsCount;i++) inv.RemoveSlotItems(i,inv.GetSlotCount(i));
            // Give the actor an existing stack. The task must report the slot it actually used.
            inv.AddSlotItems(3,food,1);
            var at=player.ComponentBody.Position;
            turn=actions.Start(new W.FaceTowards(v(at.X+.5,at.Y,at.Z+.5),5));
            phase=2;
        } else if (phase === 2) {
            if (turn.Result.Status === W.AtomicActionStatus.Running) return;
            yes(turn.Result.Status === W.AtomicActionStatus.Succeeded,"fixture has finished turning");
            var schema=JSON.parse(tools.DescribeToolsJson()).find(t => t.name === "submit_plan").parameters.properties.steps.items;
            yes(schema.properties.item_from_step.type === "integer" && schema.properties.fallback.required.includes("failure"),
                "tool schema describes receipt bindings and fallback fields");
            var reference=putFood();
            var unsafePlan=eatPlan(reference);
            unsafePlan.steps.splice(1,0,{tool:"act_Drop",arguments:{slot:0,value:food,count:1}});
            yes(call("submit_plan",unsafePlan).error === "invalid_arguments","receipt cannot span an inventory-changing step");
            yes(data("submit_plan",eatPlan(reference)).status === "waiting","submit a plan with an output binding");
            phase=3;
        } else if (phase === 3) {
            var task=data("task_status"); if (!done(task)) return;
            yes(task.status === "succeeded" && task.steps[0].receivedItems[0].value === food
                && task.steps[0].receivedItems[0].count === 1 && task.steps[1].affectedCount === 1
                && foodCount() === 1 && player.ComponentVitalStats.Food > .2,
                "actual pickup receipt drives one meal");
            var target=putFood();
            var plan={steps:[
                {tool:"act_PickUp",arguments:{target:target,count:1},fallback:{step:2,failure:"target_changed"}},
                {tool:"act_Drop",arguments:{slot:3,value:food,count:1}},
                {tool:"act_Wait",arguments:{duration:.1}}
            ]};
            yes(data("submit_plan",plan).status === "waiting","submit a bounded recovery branch");
            drop.ToRemove=true;
            phase=4;
        } else if (phase === 4) {
            var task=data("task_status"); if (!done(task)) return;
            yes(task.status === "succeeded" && task.steps[0].atomicFailure === "targetChanged"
                && task.steps[1].status === "skipped" && task.steps[2].status === "succeeded" && foodCount() === 1,
                "stale target selects declared branch and skips the resource effect");
            yes(call("submit_plan",{steps:[{tool:"act_Wait",arguments:{duration:.1},next:0}]}).error === "invalid_arguments",
                "backward branches are rejected");
            yes(call("submit_plan",{steps:[
                {tool:"act_Drop",arguments:{slot:3,value:food,count:1},fallback:{step:1,failure:"blocked"}},
                {tool:"act_Wait",arguments:{duration:.1}}
            ]}).error === "invalid_arguments","fallback is rejected for actions whose zero count can hide effects");
            data("submit_plan",{steps:[
                {tool:"act_MoveTo",arguments:{bearing:0,distance:3,timeout:.08},retries:1,fallback:{step:1,failure:"timed_out"}},
                {tool:"act_Wait",arguments:{duration:.1},next:3},
                {tool:"act_Drop",arguments:{slot:3,value:food,count:1}}
            ]});
            phase=5;
        } else if (phase === 5) {
            var task=data("task_status"); if (!done(task)) return;
            yes(task.status === "succeeded" && task.steps[0].attempts === 2 && task.steps[0].atomicFailure === "timedOut"
                && task.steps[1].status === "succeeded" && task.steps[2].status === "skipped" && foodCount() === 1,
                "movement retries once then takes the declared branch");
            player.ComponentVitalStats.Food=.2;
            var plan=eatPlan(putFood());
            plan.steps.splice(1,0,{tool:"act_Wait",arguments:{duration:.1}});
            data("submit_plan",plan);
            phase=6;
        } else if (phase === 6) {
            var task=data("task_status");
            if (task.completedSteps === 0 && !done(task)) return;
            yes(task.status === "waiting" && task.currentStep === 1 && foodCount() === 2,"stop after pickup at a safe action boundary");
            phase=-1;
            tools.SaveTaskCheckpoint("app:actor-task.scworld");
            finish("passed","checkpoint published");
        }
    } catch (error) { finish("failed",String(error)); }
});
