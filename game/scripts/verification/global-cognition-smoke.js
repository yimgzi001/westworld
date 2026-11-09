var W=importNamespace("Game.Westworld"), system=importNamespace("System");
var phase=0, checks=[], scheduler, project, time, actors=[], minds=[], tools=[], originalFood, pauseTime, first, decisionsBefore, frozenFrames=0;
var requested=Engine.Storage.ReadAllText("app:global-mode").trim();
var suite=requested==="suite";
var scenarios=suite ? ["parallel","serial","budget","rate"] : [requested];
var scenarioIndex=0, reports=[], mode=scenarios[0];
var resume=mode==="resume" || mode==="unknown";
function yes(ok,label) { if(!ok) throw new Error(label); checks.push(label); }
function finish(result,detail) {
    if(suite && result==="passed") {
        phase=-1;
        var next=status();
        yes(next.batchId===detail.batchId || next.attempts.length===0,
            "next scenario did not send requests through the old scheduler");
        scheduler.Dispose();
        reports.push({mode,detail,checks});
        if(++scenarioIndex<scenarios.length) {
            // The old scheduler may have claimed the completed tasks' follow-up turns
            // in the native step that finished them. Let the new scheduler prepare them.
            for(var mind of minds) {
                var pending=mind.PrepareTurn();
                if(pending) mind.AbandonTurn(JSON.parse(pending).id);
            }
            mode=scenarios[scenarioIndex];
            checks=[];
            frozenFrames=0;
            phase=5;
            return;
        }
    }
    phase=-1;
    var report={result,detail,checks,processId:system.Environment.ProcessId};
    if(suite) {
        report.scenarios=reports;
        report.checks=reports.flatMap(scenario=>scenario.checks.map(check=>scenario.mode+": "+check));
        if(result!=="passed") report.failedMode=mode;
    }
    Engine.Storage.WriteAllText("app:global-cognition-result.json",JSON.stringify(report,null,2));
}
function status() { return JSON.parse(scheduler.StatusJson()); }
frameHandlers.push(function(){
    try {
        if(phase===0) {
            if(Game.ScreensManager.GetScreenName(Game.ScreensManager.CurrentScreen)!=="MainMenu" || Game.ScreensManager.IsAnimating) return;
            var screen=Game.ScreensManager.m_screens.get_Item("Game"), widget=screen.Children.Find("GamesWidget",true), world;
            if(resume) {
                var ledger=JSON.parse(Engine.Storage.ReadAllText("app:cognition-run/ledger.json"));
                var stream=Engine.Storage.OpenFile("app:cognition-run/"+ledger.batch.id+".scworld",Engine.OpenFileMode.Read);
                try { world=Game.WorldsManager.GetWorldInfo(Game.WorldsManager.ImportWorld(stream)); } finally { stream.Dispose(); }
            } else {
                var s=new Game.WorldSettings(); s.Name="Global Cognition Verification"; s.Seed="42";
                s.OriginalSerializationVersion=Game.VersionsManager.SerializationVersion; s.ResetOptionsForNonCreativeMode(null);
                world=Game.WorldsManager.CreateWorld(s);
            }
            Game.GameManager.LoadProject(world,widget);
            project=getProject();
            if(!resume) for(var i=0;i<3;i++) {
                var p=new Game.PlayerData(getProject()); p.Name="Cohort "+i;
                p.InputDevice=i===0 ? Game.WidgetInputDevice.Keyboard | Game.WidgetInputDevice.Mouse :
                    i===1 ? Game.WidgetInputDevice.GamePad1 : Game.WidgetInputDevice.GamePad2;
                findSubsystem("Players").AddPlayerData(p);
            }
            time=findSubsystem("Time");
            if(resume) bind();
            Game.ScreensManager.SwitchScreen("Game"); phase=resume ? 2 : 1;
        } else if(phase===1) {
            if(findSubsystem("Players").ComponentPlayers.Count<3 || time.GameTime<4) return;
            bind(); phase=2;
        } else if(phase===2) {
            var s=status();
            if(!s.batchId) return;
            yes(s.members.length===2,"only two eligible actors entered the batch");
            pauseTime=time.GameTime; originalFood=actors[2].ComponentVitalStats.Food;
            if(mode==="budget") {
                yes(s.state==="budgetPaused" && s.attempts.length===0,"whole-batch budget rejection sends nothing");
                scheduler.IncreaseBudget(10000000);
            }
            if(mode==="unknown") {
                yes(s.state==="faulted" && s.fault==="send_outcome_unknown" && s.unknown>0 && s.attempts.length===1,
                    "restart preserves unknown charge and does not resend");
                finish("passed",s); return;
            }
            if(resume) yes(s.attempts.filter(a=>a.state==="completed").length===1,"already recorded response was loaded");
            phase=3;
        } else if(phase===3) {
            var s=status();
            if(mode==="ambiguous" && s.attempts.length===1 && s.attempts[0].state==="inFlight") {
                yes(time.GameTime===pauseTime,"interrupted at a durable send intent");
                finish("passed",s); return;
            }
            if(mode==="interrupt") {
                if(s.attempts.length===1) scheduler.SetPaused(true);
                if(s.attempts.filter(a=>a.state==="completed").length!==1) return;
                yes(time.GameTime===pauseTime && s.attempts.length===1,"user pause keeps the second member unstarted");
                finish("passed",s); return;
            }
            if(s.state==="waiting") {
                frozenFrames++;
                if(time.GameTime!==pauseTime || actors[2].ComponentVitalStats.Food!==originalFood) throw new Error("world advanced during model wait");
                return;
            }
            if(s.state==="scheduled") {
                yes(frozenFrames>0 && time.GameTime<pauseTime+1,"world and third actor stayed frozen during the wait");
                yes(minds.slice(0,2).every((mind,i)=>mind.LastDecisionJson===decisionsBefore[i]),"network replies do not install decisions");
                if(mode==="parallel") yes(s.attempts[1].receivedAt<s.attempts[0].receivedAt,"fixed responses arrived out of order");
                if(mode==="serial") yes(s.attempts[0].receivedAt<=s.attempts[1].sentAt,"second request waited for the first response");
                if(mode==="rate") yes(s.attempts.some(a=>a.error==="rate_limited") && s.attempts.length===3 && s.unknown>0,"429 retried once after reservation");
                first=s; phase=4;
            } else if(s.state==="faulted") { finish("failed",s); }
        } else if(phase===4) {
            if(minds.slice(0,2).some((mind,i)=>mind.LastDecisionJson===decisionsBefore[i])
                || tools.slice(0,2).some(t=>JSON.parse(t.InvokeJson("task_status","{}")).data.status!=="succeeded")) return;
            yes(minds.slice(0,2).every((mind,i)=>{
                var decision=JSON.parse(mind.LastDecisionJson);
                return !decision.actionError && decision.taskId
                    && (!decisionsBefore[i] || decision.taskId!==JSON.parse(decisionsBefore[i]).taskId);
            }),"both decisions started new tasks");
            yes(time.GameTime>=pauseTime+1 && minds[2].LastDecisionJson===decisionsBefore[2],"both decisions install after simulated thinking time and execute their plans");
            yes(first.attempts.filter(a=>a.state==="completed").length===2 && first.spent>0,"actual usage settles both reservations");
            if(resume) yes(first.attempts.length===2,"restart sent only the missing request");
            finish("passed",first);
        } else if(phase===5) {
            bind();
            phase=2;
        }
    } catch(error) { finish("failed",String(error)); }
});
function bind() {
    var players=findSubsystem("Players").ComponentPlayers;
    if(scenarioIndex>0) {
        yes(system.Object.ReferenceEquals(project,getProject())
            && actors.every((actor,i)=>system.Object.ReferenceEquals(actor,players.get_Item(i))),
            "kept the same loaded world and three actors");
    } else {
        for(var i=0;i<3;i++) {
            actors[i]=players.get_Item(i);
            tools[i]=new W.ActorTools(new W.AtomicActions(actors[i].Entity),false);
            minds[i]=new W.ActorCognition(tools[i],"cohort "+i);
        }
        if(!resume) {
            var t=JSON.parse(minds[2].PrepareTurn());
            yes(JSON.parse(minds[2].CommitTurn(t.id,'{"reason":"Wait for later","task":"keep","reviewAfter":120}')).ok,"third actor already has a review scheduled");
        }
    }
    decisionsBefore=minds.map(mind=>mind.LastDecisionJson);
    var route=mode==="interrupt" || mode==="resume" ? "serial" : mode==="unknown" ? "ambiguous" : mode;
    var endpoint=system.Environment.GetEnvironmentVariable("WENMING_MODEL_ENDPOINT")+"/"+route+"/responses";
    var client=new W.ActorModelClient(endpoint,
        system.Environment.GetEnvironmentVariable("WENMING_MODEL"),"responses",
        system.Environment.GetEnvironmentVariable("WENMING_MODEL_API_KEY"));
    var ledgerDirectory=suite ? "app:cognition-run/"+mode : "app:cognition-run";
    scheduler=new W.WorldCognitionScheduler(client,ledgerDirectory,
        mode==="parallel" || mode==="rate" ? 2 : 1,mode==="budget" ? 1 : 10000000,10,20,"fixture-v1");
    for(var i=0;i<3;i++) scheduler.Register(minds[i],tools[i]);
    scheduler.Start(resume);
}
