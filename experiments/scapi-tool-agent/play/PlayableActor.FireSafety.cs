#nullable enable

using Engine;
using Game;

internal sealed partial class PlayableActor
{
    private PlaySurvivalTravel? heatEscape;
    private PlayFireSafety.Heat[] observedHeat = [];
    private double nextFireEscape;
    private bool wasOnFire, wasTouchingFire;

    private bool CheckFireDanger(PlayDecisionQueue queue, double now)
    {
        var fire = player.Entity.FindComponent<ComponentOnFire>();
        bool burning = fire?.IsOnFire == true;
        bool touching = fire?.CheckIfBodyTouchesFire() == true;
        if (burning != wasOnFire || touching != wasTouchingFire)
            log(new { kind = "play_fire_state", actor = Name, burning, touching,
                health = player.ComponentHealth.Health, position = player.ComponentBody.Position.ToString(), gameTime = now });
        bool newlyBurning = burning && !wasOnFire;
        wasOnFire = burning;
        wasTouchingFire = touching;
        if (heatEscape is not null || now < nextFireEscape || !touching && !newlyBurning) return false;
        // 日常避火复用导航已有的局部观察；睡眠加速时不能按游戏时间额外创建观察票据。
        ReadDryWindow();
        var body = player.ComponentBody;
        var source = observedHeat.Where(heat => (heat.Center - body.Position).XZ.LengthSquared() <= 16f)
            .MinBy(heat => (heat.Center - body.Position).LengthSquared());
        // 着火可以直接感觉到，火源位置仍只来自实际观察，不读取遮挡后的地形。
        if (source is null) return false;
        if (!PauseForSurvivalSafety(queue, "touching_fire", now)) return true;
        BeginSurvivalGoal(new("escape_fire", PlaySurvivalGoals.Kind.Evade, "立即离开已接触的火焰，随后重新决策"),
            "native_fire_contact", now);
        nextFireEscape = now + 2;
        var away = body.Position.XZ - source.Center.XZ;
        if (away.LengthSquared() < 0.01f) away = body.Matrix.Forward.XZ;
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, away));
        heatEscape = new(player, owner, bearing, 4f, "survey", true, now, ObserveSurvival, ReadDryWindow, log,
            escapeFrom: () => source.Center.XZ, escapeClearance: 3f,
            permitLandLeg: (end, time) => PermitSurvivalLeg(end, time));
        log(new { kind = "play_fire_escape", actor = Name, burning, touching, observedSource = source.Center.ToString(), gameTime = now });
        return true;
    }

    private void UpdateFireEscape(double now)
    {
        var escape = heatEscape!;
        escape.Update(now);
        if (escape.Status is null) return;
        heatEscape = null;
        bool separated = player.Entity.FindComponent<ComponentOnFire>()?.CheckIfBodyTouchesFire() != true;
        string reason = separated ? "native_fire_contact_ended" : escape.Reason ?? "fire_escape_blocked";
        Report("escape_fire", separated ? "succeeded" : "failed", reason, now,
            movement: (player.ComponentBody.Position - escape.Origin).XZ);
        FinishStep(separated, reason, now);
    }
}
