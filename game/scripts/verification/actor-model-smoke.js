var W=importNamespace("Game.Westworld"), phase=0, checks=[], actor, time, mind, runner, food, slot=3, oldFactor, inv, expected;
function yes(ok,label) { if(!ok) throw new Error(label); checks.push(label); }
function finish(result,detail) {
    phase=-1;
    Engine.Storage.WriteAllText("app:actor-model-result.json",JSON.stringify({result,detail,checks},null,2));
}
function unfreeze() { time.BasicGameTimeFactor=oldFactor; time.GameTimeFactor=oldFactor; }
frameHandlers.push(function() {
    try {
        if(phase===0) {
            if(Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen)!=="MainMenu" || Game.ScreensManager.IsAnimating) return;
            var s=new Game.WorldSettings(); s.Name="Model Adapter Verification"; s.Seed="42";
            s.OriginalSerializationVersion=Game.VersionsManager.SerializationVersion; s.ResetOptionsForNonCreativeMode(null);
            var world=Game.WorldsManager.CreateWorld(s), screen=Game.ScreensManager.m_screens.get_Item("Game");
            Game.GameManager.LoadProject(world,screen.Children.Find("GamesWidget",true));
            var player=new Game.PlayerData(getProject());
            player.Name="Model Fixture"; player.InputDevice=Game.WidgetInputDevice.Keyboard | Game.WidgetInputDevice.Mouse;
            findSubsystem("Players").AddPlayerData(player);
            Game.ScreensManager.SwitchScreen("Game"); time=findSubsystem("Time"); phase=1;
        } else if(phase===1) {
            var players=findSubsystem("Players").ComponentPlayers;
            if(players.Count<1 || time.GameTime<4) return;
            actor=players.get_Item(0); actor.ComponentVitalStats.Food=.2;
            inv=actor.ComponentMiner.Inventory;
            for(var i=0;i<inv.SlotsCount;i++) inv.RemoveSlotItems(i,inv.GetSlotCount(i));
            food=Game.BlocksManager.GetBlockIndex("CookedMeatBlock",true); inv.AddSlotItems(slot,food,1);
            var tools=new W.ActorTools(new W.AtomicActions(actor.Entity),false);
            mind=new W.ActorCognition(tools,"A hungry person who knows the one item in their pack is cooked meat. Eat one portion when hungry.");
            var initial=JSON.parse(mind.PrepareTurn());
            yes(JSON.parse(mind.CommitTurn(initial.id,JSON.stringify({
                reason:"Check my pack before eating",goal:"Eat one portion of the cooked meat already in my pack",task:"keep",reviewAfter:.15
            }))).ok,"prepared the review through the normal cognitive interface");
            expected=Engine.Storage.FileExists("app:expect-rate-limit") ? "rate_limited" :
                Engine.Storage.FileExists("app:expect-invalid-decision") ? "invalid_decision_json" : null;
            runner=new W.ActorModelTurn(mind,tools,W.ActorModelClient.FromEnvironment());
            phase=2;
        } else if(phase===2) {
            if(!mind.PrepareTurn()) return;
            phase=5;
        } else if(phase===5) {
            yes(!runner.TryStart(),"older context is rejected before dispatch");
            phase=6;
        } else if(phase===6) {
            if(!runner.TryStart()) return;
            oldFactor=time.BasicGameTimeFactor; time.BasicGameTimeFactor=0; time.GameTimeFactor=0;
            yes(!runner.TryStart(),"one in-flight request per frozen turn");
            phase=3;
        } else if(phase===3) {
            var raw=runner.Poll(); if(!raw) return;
            unfreeze();
            var result=JSON.parse(raw);
            if(expected) {
                yes(!result.ok && result.error===expected && inv.GetSlotCount(slot)===1 && !runner.TryStart(),
                    "provider error preserves the turn and does not dispatch a duplicate: "+JSON.stringify(result));
                if(expected==="rate_limited") yes(result.status===429 && result.retryAfterSeconds===2,"rate limit returns Retry-After");
                finish("passed",result);
                return;
            }
            if(!result.ok) { finish("failed",result); return; }
            yes(result.model && result.inputTokens>0 && result.outputTokens>0,"model identity and token usage returned");
            yes(JSON.parse(mind.LastDecisionJson).decision.task==="start","model decision entered the normal task executor");
            phase=4;
        } else if(phase===4) {
            if(inv.GetSlotCount(slot)!==0) return;
            yes(actor.ComponentVitalStats.Food>.2,"original Eat action consumed food and increased nutrition");
            finish("passed",JSON.parse(runner.LastResultJson));
        }
    } catch(error) {
        if(phase===3) unfreeze();
        finish("failed",String(error));
    }
});
