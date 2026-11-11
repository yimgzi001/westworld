#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;

internal sealed partial class PlaySurvivalProduction
{
    public const string RecoverSpear = "recover_visible_spear";
    private Guid equipmentSource, equipmentActor;
    private int equipmentValue, equipmentRecovered, equipmentLegs;
    private bool equipmentTracking;
    private PlaySurvivalTravel? equipmentRoute;

    public static bool RecoverableSpear(int value) => IsSpear(value);

    private void BindEquipment(NearbyPickableView item)
    {
        equipmentSource = NativeAcquisitionAudit.IdentityFor(item.CreatePickup(1).Target);
        equipmentActor = NativeAcquisitionAudit.IdentityFor(player.Entity);
        equipmentValue = item.Value;
        equipmentTracking = true;
        NativeAcquisitionAudit.Recorded += TrackEquipmentPickup;
    }

    private void TrackEquipmentPickup(NativeAcquisitionFact fact)
    {
        // 另一根同类木矛入包不能完成这次已绑定的回收。
        if (fact.ActorId == equipmentActor && fact.SourceId == equipmentSource && fact.Value == equipmentValue
            && fact.InventoryBefore is int before && fact.InventoryAfter is int after && after > before
            && after - before == fact.SourceBefore - fact.SourceAfter)
            equipmentRecovered += after - before;
    }

    private NearbyPickableView? ReadRecoveryTarget() => owner.Queries.FindVisibleObjectsAround(40f).Pickables
        .FirstOrDefault(item => NativeAcquisitionAudit.IdentityFor(item.CreatePickup(1).Target) == equipmentSource);

    private void AdvanceEquipment(double now)
    {
        var body = player.ComponentBody;
        CurrentStage = "recover_bound_visible_spear";
        if (!equipmentTracking || !RecoverableSpear(equipmentValue)) { Finish(false, "equipment_target_not_bound"); return; }
        if (now >= deadline || player.ComponentSleep.IsSleeping || body.ImmersionDepth > 0.04f)
        { Finish(false, "equipment_recovery_interrupted"); return; }
        if (equipmentRoute is { } route)
        {
            route.Update(now);
            if (route.Status is null) return;
            equipmentRoute = null;
            log(new { kind = "play_equipment_recovery_route", actor = player.PlayerData.Name,
                route.Status, route.Reason, source = equipmentSource, gameTime = now });
        }
        bool grounded = PlaySurvivalSenses.OnDryGround(body) && Math.Abs(body.Velocity.Y) < 0.6f;
        if (equipmentRecovered > 0)
        {
            if (!grounded) return;
            log(new { kind = "play_equipment_recovered", actor = player.PlayerData.Name, source = equipmentSource,
                value = equipmentValue, count = equipmentRecovered, gameTime = now });
            Finish(true, "bound_native_spear_recovered");
            return;
        }
        if (!grounded) return;
        var target = ReadRecoveryTarget();
        if (target is null) { Finish(false, "equipment_target_no_longer_visible"); return; }
        if (target.InReach)
        {
            var pickup = owner.Start(target.CreatePickup(1));
            if (pickup.Result.Status == AtomicActionStatus.Succeeded) return;
            if (pickup.Result.Failure is not (AtomicActionFailure.OutOfReach or AtomicActionFailure.Blocked))
            { Finish(false, $"equipment_pickup_{pickup.Result.Failure}"); return; }
        }
        if (equipmentLegs >= 2) { Finish(false, "equipment_drop_not_reachable"); return; }
        var offset = target.Position - body.Position;
        float distance = offset.XZ.Length();
        if (distance < 0.15f || distance > 40f || Math.Abs(offset.Y) > 3f
            || permitLandLeg?.Invoke(target.Position, now) == false)
        { Finish(false, "equipment_target_outside_safe_approach"); return; }
        var path = PlayObservedPath.Find(owner.Queries.ReadPanorama(40f), body.Position, target.Position,
            (_, end) => permitLandLeg?.Invoke(end, now) != false, goalReach: 0.7f, maximumAboveTarget: 0.2f);
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset.XZ));
        equipmentLegs++;
        equipmentRoute = new(player, owner, bearing, distance + 1f, "food", false, now,
            () => (JsonSerializer.SerializeToElement(new { }), ReadRecoveryTarget() is { } seen ? [MaterialTarget(seen)] : []),
            readDryWindow, log, foodTarget: MaterialTarget(target), permitLandLeg: permitLandLeg, observedPath: path);
        log(new { kind = "play_equipment_recovery_approach", actor = player.PlayerData.Name,
            source = equipmentSource, distance, leg = equipmentLegs, gameTime = now });
    }

    private void StopEquipment()
    {
        equipmentRoute?.Stop();
        equipmentRoute = null;
        if (equipmentTracking) NativeAcquisitionAudit.Recorded -= TrackEquipmentPickup;
        equipmentTracking = false;
    }
}
