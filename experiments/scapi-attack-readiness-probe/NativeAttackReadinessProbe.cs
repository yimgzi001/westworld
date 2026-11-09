using System.Text.Json;
using Game;
using Westworld.Scapi.Adapter;
using Westworld.Runtime.Core;

// Deterministic executor control. Caller owns existing fixture, native actor, game update and gateway.
// Start only after native observation/approach/aim: no teleport, inventory injection or damage shortcuts.
public sealed class NativeAttackReadinessProbe
{
    readonly ComponentPlayer player; readonly NativeToolSession gateway; readonly Action<object> log;
    readonly string target, mode; int stage; double start, previous, deadline, firstHit; int attacks;
    public bool Done { get; private set; }
    public bool Passed { get; private set; }
    public string Reason { get; private set; } = "not_started";
    public NativeAttackReadinessProbe(ComponentPlayer player, NativeToolSession gateway,
        string actualObservedTarget, Action<object> log, string mode="cooldown")
    {
        if(mode is not("cooldown" or "cancel" or "lost"))throw new ArgumentException("unknown mode");
        this.player=player;this.gateway=gateway;target=actualObservedTarget;this.log=log;this.mode=mode;
    }
    ToolReceipt Call(string name, object args){var r=gateway.Call(name,args);log(new{kind="cooldown_probe_receipt",name,args,receipt=r});return r;}
    static string Status(ToolReceipt r)=>r.Data.ValueKind==JsonValueKind.Object&&r.Data.TryGetProperty("status",out var s)?s.GetString():null;
    bool Visible(){var r=Call("observe",new{look_distance=16,object_distance=16,limit=24});return r.State=="returned"&&r.Data.GetProperty("entities").EnumerateArray().Any(e=>e.GetProperty("reference").GetString()==target);}
    void End(bool ok,string reason){Done=true;Passed=ok;Reason=reason;log(new{kind="cooldown_probe_result",mode,passed=ok,reason,attacks,firstHit,actualLastHit=player.ComponentMiner.m_lastHitTime,gameTime=player.Project.FindSubsystem<SubsystemTime>(true).GameTime,modelUsed=false});}
    public void Update()
    {
        if(Done)return;
        double now=player.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if(stage==0){start=previous=now;deadline=now+4;stage=1;}
        if(stage==1){var ready=LocalAttackReadiness.Evaluate(now,player.ComponentMiner.m_lastHitTime,player.ComponentMiner.HitInterval,start,deadline,previous);previous=now;
            if(ready==AttackReadiness.Wait)return;if(ready!=AttackReadiness.Ready){End(false,"initial_"+ready);return;}
            if(!Visible()){End(false,"initial_target_not_visible");return;}
            double before=player.ComponentMiner.m_lastHitTime;var first=Call("act_MeleeAttack",new{target});attacks++;
            firstHit=player.ComponentMiner.m_lastHitTime;
            if(Status(first)!="succeeded"||firstHit<=before){End(false,"first_native_attempt_rejected");return;}
            // Same game tick, same fresh visible target; no simulated timestamps.
            if(!Visible()){End(false,"target_lost_before_same_tick_control");return;}
            var second=Call("act_MeleeAttack",new{target});attacks++;
            if(Status(second)!="failed"||player.ComponentMiner.m_lastHitTime!=firstHit){End(false,"same_tick_not_rejected");return;}
            log(new{kind="same_tick_rejected",now,firstHit,interval=player.ComponentMiner.HitInterval,second.Data});
            start=previous=now;deadline=now+4;stage=2;
            if(mode=="cancel"){Call("cancel_action",new{});stage=3;}
            if(mode=="lost"){Call("act_FaceTowards",new{bearing=180,timeout=2});stage=4;}
            return;
        }
        if(stage==3){if(now>=deadline){End(false,"cancel_observation_deadline");return;}if(now>=firstHit+player.ComponentMiner.HitInterval+.1)End(player.ComponentMiner.m_lastHitTime==firstHit&&attacks==2,"cancelled_queue_no_followup_attack");return;}
        if(stage==4){var status=Call("action_status",new{});if(Status(status)=="running"&&now<deadline)return;
            if(Visible()){End(false,"loss_control_target_still_visible");return;}
            // Deliberately submit no attack. The local timing hint cannot authorize an unseen reference.
            End(player.ComponentMiner.m_lastHitTime==firstHit&&attacks==2,"lost_reference_no_followup_attack");return;}
        var decision=LocalAttackReadiness.Evaluate(now,player.ComponentMiner.m_lastHitTime,player.ComponentMiner.HitInterval,start,deadline,previous);previous=now;
        log(new{kind="cooldown_local_poll",now,last=player.ComponentMiner.m_lastHitTime,interval=player.ComponentMiner.HitInterval,decision=decision.ToString()});
        if(decision==AttackReadiness.Wait)return;if(decision!=AttackReadiness.Ready){End(false,"local_"+decision);return;}
        if(!Visible()){End(false,"target_lost_after_wait");return;}
        var later=Call("act_MeleeAttack",new{target});attacks++;
        End(Status(later)=="succeeded"&&player.ComponentMiner.m_lastHitTime>firstHit,"later_native_attempt_"+Status(later));
    }
}
