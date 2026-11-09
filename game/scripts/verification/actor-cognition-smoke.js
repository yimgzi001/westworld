var W=importNamespace("Game.Westworld");
var phase=0, checks=[], actor, peer, tools, other, mind, second, time, food, inv, look, drop, replaced, finishedTask, followingTask, archive="app:actor-cognition.scworld";
var resumed=Engine.Storage.FileExists(archive);
function v(x,y,z) { return new Engine.Vector3(x,y,z); }
function yes(ok,label) { if (!ok) throw new Error(label); checks.push(label); }
function read(session,name,args) { var r=JSON.parse(session.InvokeJson(name,JSON.stringify(args||{}))); if(!r.ok) throw new Error(name+": "+r.error); return r.data; }
function prepare(m) { var value=m.PrepareTurn(); return value ? JSON.parse(value) : null; }
function commit(m,turn,decision) { return JSON.parse(m.CommitTurn(turn.id,JSON.stringify(decision))); }
function finish(result,detail) { phase=-1; Engine.Storage.WriteAllText("app:actor-cognition-result.json",JSON.stringify({result,detail,checks},null,2)); }
function count() {
    var n=0;
    for(var i=0;i<inv.SlotsCount;i++) if(inv.GetSlotValue(i)===food) n+=inv.GetSlotCount(i);
    return n;
}
function spawn(player,point) { player.ComponentBody.Position=point; player.ComponentBody.Velocity=v(0,0,0); }
frameHandlers.push(function(){
    try {
        if(phase===0) {
            if(Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen)!=="MainMenu" || Game.ScreensManager.IsAnimating) return;
            yes(typeof W.ActorCognition !== "undefined","cognitive runtime is available");
            var screen=Game.ScreensManager.m_screens.get_Item("Game"), world;
            if(resumed) {
                var stream=Engine.Storage.OpenFile(archive,Engine.OpenFileMode.Read);
                try { world=Game.WorldsManager.GetWorldInfo(Game.WorldsManager.ImportWorld(stream)); } finally { stream.Dispose(); }
                Game.GameManager.LoadProject(world,screen.Children.Find("GamesWidget",true));
                actor=findSubsystem("Players").ComponentPlayers.get_Item(0);
                inv=actor.ComponentMiner.Inventory;
                food=Game.BlocksManager.GetBlockIndex("CookedMeatBlock",true);
                tools=new W.ActorTools(new W.AtomicActions(actor.Entity),false);
                mind=new W.ActorCognition(tools,"fresh binding");
                yes(mind.RestoreCheckpoint() && !mind.RestoreCheckpoint(),"restore cognition and task together once");
                yes(count()===1 && read(tools,"task_status").currentStep===1,"world and task restore after one pickup");
                Game.ScreensManager.SwitchScreen("Game"); phase=7;
            } else {
                var s=new Game.WorldSettings(); s.Name="Cognition Verification"; s.Seed="42";
                s.OriginalSerializationVersion=Game.VersionsManager.SerializationVersion; s.ResetOptionsForNonCreativeMode(null);
                world=Game.WorldsManager.CreateWorld(s);
                Game.GameManager.LoadProject(world,screen.Children.Find("GamesWidget",true));
                for(var i=0;i<2;i++) {
                    var d=new Game.PlayerData(getProject()); d.Name="Fixture "+i;
                    d.InputDevice=i===0 ? Game.WidgetInputDevice.Keyboard | Game.WidgetInputDevice.Mouse : Game.WidgetInputDevice.GamePad1;
                    findSubsystem("Players").AddPlayerData(d);
                }
                Game.ScreensManager.SwitchScreen("Game"); time=findSubsystem("Time"); phase=1;
            }
        } else if(phase===1) {
            var players=findSubsystem("Players").ComponentPlayers;
            if(players.Count<2 || time.GameTime<4) return;
            actor=players.get_Item(0); peer=players.get_Item(1); inv=actor.ComponentMiner.Inventory;
            var origin=actor.ComponentBody.Position, terrain=findSubsystem("Terrain");
            var x=Math.floor(origin.X)+8,y=Math.floor(origin.Y)+6,z=Math.floor(origin.Z);
            var stone=Game.BlocksManager.GetBlockIndex("GraniteBlock",true);
            food=Game.BlocksManager.GetBlockIndex("CookedMeatBlock",true);
            for(var dx=-4;dx<=5;dx++) for(var dz=-4;dz<=5;dz++) {
                terrain.ChangeCell(x+dx,y,z+dz,stone,true,null);
                for(var h=1;h<=4;h++) terrain.ChangeCell(x+dx,y+h,z+dz,0,true,null);
            }
            spawn(actor,v(x+.5,y+1.02,z+.5)); spawn(peer,v(x+2.5,y+1.02,z+1.5));
            for(var i=0;i<inv.SlotsCount;i++) inv.RemoveSlotItems(i,inv.GetSlotCount(i));
            actor.ComponentVitalStats.Food=.2;
            var port=new W.AtomicActions(actor.Entity); tools=new W.ActorTools(port,false);
            other=new W.ActorTools(new W.AtomicActions(peer.Entity),false);
            mind=new W.ActorCognition(tools,"prefers known routes");
            second=new W.ActorCognition(other,"curious");
            look=port.Start(new W.FaceTowards(v(x+3,y+1,z+2),5)); phase=2;
        } else if(phase===2) {
            if(look.Result.Status===W.AtomicActionStatus.Running) return;
            var initial=prepare(mind);
            yes(initial.reasons.includes("initial") && initial.profile==="prefers known routes" && initial.observation
                && initial.messages.length===0 && !initial.inventory && initial.actions.includes("act_Eat"),"initial context includes scene, without social inventory");
            var response={reason:"Wait until something changes",task:"keep",goal:"Find food",reviewAfter:30};
            yes(commit(mind,initial,response).ok && !commit(mind,initial,response).ok && !prepare(mind),"one accepted decision, replay rejected, stable frame stays quiet");
            var b=prepare(second);
            yes(b.profile==="curious" && !b.goal && b.memories.length===0,"separate cognitive state for the second actor");
            yes(commit(second,b,{reason:"I will ask about wood",task:"keep",reviewAfter:30}).ok,"second actor can decide independently");
            read(other,"act_Speak",{text:"I can trade food for wood."});
            phase=3;
        } else if(phase===3) {
            var inbox=read(tools,"read_social");
            var social=prepare(mind);
            if(!social) return;
            yes(social.reasons.includes("social") && social.goal==="Find food" && social.inventory
                && social.messages[0].value.text==="I can trade food for wood." && inbox.some(m=>m.text===social.messages[0].value.text),"social context assembles delivered words, inventory and active goal");
            var evidence=social.messages[0].id;
            var frozen=mind.PrepareTurn();
            var rejected=commit(mind,social,{reason:"bad evidence",task:"keep",reviewAfter:30,remember:{source:"foreign",text:"unseen"}});
            yes(!rejected.ok && mind.PrepareTurn()===frozen,"invalid decision leaves frozen input and pending message intact");
            var decision={reason:"Remember the offer and revisit later",task:"keep",reviewAfter:30,
                remember:{source:evidence,text:"Food for wood was proposed."},commitment:{source:evidence,text:"Ask about the trade later."},handled:[evidence]};
            yes(commit(mind,social,decision).ok && !prepare(mind),"successful decision acknowledges the message once");
            var at=actor.ComponentBody.Position;
            drop=findSubsystem("Pickables").AddPickable(food,1,v(at.X+.55,at.Y+.65,at.Z+.35),null,null);
            var observed=read(tools,"observe"), item=observed.pickables.find(p=>p.value===food);
            yes(!!item,"food target is locally observed");
            read(tools,"submit_plan",{steps:[{tool:"act_PickUp",arguments:{target:item.reference,count:1}}]});
            drop.ToRemove=true; phase=4;
        } else if(phase===4) {
            var failed=prepare(mind); if(!failed) return;
            yes(failed.reasons.includes("taskEnded") && failed.task.status==="failed" && failed.task.steps[0].atomicFailure==="targetChanged"
                && failed.messages.length===0 && failed.commitments.length===1,"terminal task wakes the actor with actual failure and outstanding commitment");
            yes(commit(mind,failed,{reason:"Look again before choosing another target",task:"keep",reviewAfter:.15}).ok,"review can defer action");
            phase=5;
        } else if(phase===5) {
            var review=prepare(mind); if(!review) return;
            yes(review.reasons.includes("review") && review.memories.some(m=>m.text.includes("Food for wood") && !m.evidence),"review recalls the actor's earlier sourced experience");
            yes(commit(mind,review,{reason:"The food is still nearby",task:"keep",reviewAfter:30}).ok,"scheduled review can keep its current intent");
            read(other,"act_Speak",{text:"Meet later."}); phase=8;
        } else if(phase===8) {
            if(read(other,"action_status").status==="running") return;
            if(!drop || drop.ToRemove) {
                var at=actor.ComponentBody.Position;
                drop=findSubsystem("Pickables").AddPickable(food,1,v(at.X+.55,at.Y+.65,at.Z+.35),null,null);
            }
            var review=prepare(mind); if(!review) return;
            var item=review.observation.pickables.find(p=>p.value===food);
            yes(review.reasons.includes("social") && review.messages[0].value.text==="Meet later." && item,
                "second message remains an open affair");
            var plan=[{tool:"act_PickUp",arguments:{target:item.reference,count:1},minimum_affected:1},
                {tool:"act_Eat",arguments:{count:1},item_from_step:0,minimum_affected:1}];
            yes(commit(mind,review,{reason:"Eat the food I can see",task:"start",steps:plan,reviewAfter:30}).ok,
                "structured decision submits a normal data plan");
            phase=6;
        } else if(phase===6) {
            var task=read(tools,"task_status"); if(task.completedSteps===0 && task.status!=="failed") return;
            yes(task.status==="waiting" && task.currentStep===1 && count()===1,"first atomic action updates actual inventory");
            phase=-1; mind.SaveCheckpoint(archive);
            finish("passed","cognitive checkpoint published");
        } else if(phase===7) {
            var next=prepare(mind); if(!next) return;
            yes(next.reasons.includes("taskEnded") && next.task.status==="succeeded" && next.task.steps[0].attempts===1
                && count()===0 && actor.ComponentVitalStats.Food>.2,"resumed plan eats once without replaying pickup");
            yes(next.profile==="prefers known routes" && next.goal==="Find food" && next.commitments[0].text.includes("trade")
                && next.messages.length===1 && next.messages[0].value.text==="Meet later.",
                "profile, goal, commitment, pending message and prior acknowledgment survive restart");
            yes(commit(mind,next,{reason:"Review the unfinished message shortly",task:"keep",reviewAfter:.15}).ok,
                "resumed result can be accepted by cognition");
            replaced=read(tools,"submit_plan",{steps:[{tool:"act_Wait",arguments:{duration:2}}]}).taskId;
            phase=9;
        } else if(phase===9) {
            var review=prepare(mind); if(!review) return;
            var invalid=commit(mind,review,{reason:"Try an unsupported action",task:"replace",reviewAfter:30,
                steps:[{tool:"act_Invalid",arguments:{}}]});
            yes(!invalid.ok && read(tools,"task_status").taskId===replaced && read(tools,"task_status").status==="running",
                "invalid replacement keeps the running task");
            var valid=commit(mind,review,{reason:"Switch to a shorter wait",task:"replace",reviewAfter:30,
                steps:[{tool:"act_Wait",arguments:{duration:.1}}]});
            yes(valid.ok && read(tools,"task_status").taskId!==replaced,"valid replacement installs through the task executor");
            phase=10;
        } else if(phase===10) {
            if(!followingTask) {
                var ended=read(tools,"task_status");
                if(ended.status!=="succeeded") return;
                finishedTask=ended.taskId;
                followingTask=read(tools,"submit_plan",{steps:[{tool:"act_Wait",arguments:{duration:2}}]}).taskId;
            }
            var next=prepare(mind); if(!next) return;
            yes(next.reasons.includes("taskEnded") && next.lastOutcome.taskId===finishedTask && next.task.taskId===followingTask,
                "completed task outcome remains available after a newer task starts");
            finish("passed","cognition and task resumed in a fresh process");
        }
    } catch(error) { finish("failed",String(error)); }
});
