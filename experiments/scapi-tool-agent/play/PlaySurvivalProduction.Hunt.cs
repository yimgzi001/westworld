#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using GameEntitySystem;

internal sealed partial class PlaySurvivalProduction
{
    private const float HuntReach = 2.35f;
    private const float HuntThrowReach = 12f;
    private const float HuntApproachReach = 18f;
    private readonly HashSet<Guid> preyDrops = new();
    private Entity? prey;
    private ComponentHealth? preyHealth;
    private PlaySurvivalTravel? huntApproach;
    private bool approachedLivePrey, approachedPreyDrop, preparingThrow, thrown, lastHitByHunter;
    private Guid preyId, hunterId;
    private int strikes, meatPicked;
    private double nextStrike;

    private static bool IsSpear(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)] is SpearBlock;

    private static int HuntWeapon(ComponentPlayer player) => FindSlot(player.ComponentMiner.Inventory, IsSpear,
        player.ComponentMiner.Inventory.VisibleSlotsCount);

    private static bool CanReadyHuntWeapon(ComponentPlayer player)
    {
        var inventory = player.ComponentMiner.Inventory;
        return HuntWeapon(player) >= 0 || FindSlot(inventory, IsSpear) >= 0
            && Enumerable.Range(0, inventory.VisibleSlotsCount).Any(slot => inventory.GetSlotCount(slot) == 0);
    }

    public static bool HasHuntingGear(ComponentPlayer player) => CanReadyHuntWeapon(player);

    public static NearbyEntityView[] HuntingTargets(ComponentPlayer player, IEnumerable<NearbyEntityView> visible, float reach = 18f) =>
        visible.Where(view => FindFoodBird(player, [view], reach) is not null).OrderBy(view => view.Distance).Take(4).ToArray();

    private static Entity? FindFoodBird(ComponentPlayer player, IEnumerable<NearbyEntityView> visible, float distance) =>
        visible.Where(view => view.Distance <= distance).OrderBy(view => view.Distance)
            .Select(view => player.Project.FindEntity(view.EntityId)).FirstOrDefault(bird =>
            bird?.FindComponent<ComponentCreature>() is { Category: CreatureCategory.Bird } creature
            && bird.FindComponent<ComponentChaseBehavior>()?.AttacksPlayer != true
            && creature.ComponentHealth.Health > 0f
            && MathF.Abs(creature.ComponentBody.Position.Y - player.ComponentBody.Position.Y) <= 6f
            && bird.FindComponent<ComponentLoot>() is { } loot && loot.m_lootList.Any(item =>
                item.MinCount > 0 && item.Probability >= 0.9f && PlaySurvivalFood.IsRawMeat(item.Value)));

    private void StartHunt(double gameTime)
    {
        var carry = player.ComponentMiner.Inventory;
        int weapon = HuntWeapon(player);
        var visible = owner.Queries.FindVisibleObjectsAround(HuntApproachReach).Entities;
        var target = FindFoodBird(player, visible.Where(animal => selectedBird is null || animal.EntityId == selectedBird), HuntApproachReach);
        if (weapon < 0 && target is not null && CanReadyHuntWeapon(player))
        {
            int to = Enumerable.Range(0, carry.VisibleSlotsCount).First(slot => carry.GetSlotCount(slot) == 0);
            Transfer(carry, FindSlot(carry, IsSpear), carry, to);
            return;
        }
        if (weapon < 0 || target is null)
        {
            Finish(false, "hunt_weapon_or_visible_prey_unavailable");
            return;
        }
        prey = target;
        preyHealth = target.FindComponent<ComponentHealth>(true);
        preyHealth.Injured += TrackPreyInjury;
        preyId = NativeAcquisitionAudit.IdentityFor(target);
        hunterId = NativeAcquisitionAudit.IdentityFor(player.Entity);
        NaturalResourceOriginAudit.Recorded += TrackPreyDrop;
        NativeAcquisitionAudit.Recorded += TrackPreyPickup;
        if (carry.ActiveSlotIndex != weapon) action = owner.Start(new SelectItem(weapon, carry.GetSlotValue(weapon)));
        phase = Phase.Hunt;
        log(new { kind = "play_hunt_started", actor = player.PlayerData.Name,
            prey = target.ValuesDictionary.DatabaseObject.Name, gameTime });
    }

    private void AdvanceHunt(double gameTime)
    {
        if (meatPicked > 0)
        {
            log(new { kind = "play_hunt_meat_acquired", actor = player.PlayerData.Name, count = meatPicked, strikes, gameTime });
            Finish(true, "prey_meat_acquired");
            return;
        }
        if (huntApproach is { } route)
        {
            route.Update(gameTime);
            if (route.Status is null) return;
            huntApproach = null;
            if (approachedLivePrey && !approachedPreyDrop && route.Status != "succeeded")
            {
                Finish(false, route.Reason ?? "live_prey_approach_failed");
                return;
            }
        }
        var health = preyHealth!;
        if (health.Health > 0f)
        {
            if (gameTime < nextStrike) return;
            if (thrown)
            {
                log(new { kind = "play_hunt_throw_missed", actor = player.PlayerData.Name, health = health.Health,
                    lastHitByHunter, gameTime });
                Finish(false, "spear_throw_did_not_take_prey");
                return;
            }
            var visible = owner.Queries.FindVisibleObjectsAround(HuntApproachReach).Entities
                .FirstOrDefault(view => view.EntityId == prey!.Id);
            if (strikes >= 2 || !IsSpear(player.ComponentMiner.ActiveBlockValue) || visible is null)
            {
                string reason = strikes >= 2 ? "hunt_strike_limit" : visible is null ? "prey_no_longer_visible" : "hunt_weapon_not_selected";
                log(new { kind = "play_hunt_conditions_changed", actor = player.PlayerData.Name, reason,
                    activeValue = player.ComponentMiner.ActiveBlockValue, activeSlot = player.ComponentMiner.Inventory.ActiveSlotIndex,
                    preyVisible = visible is not null, distance = visible?.Distance, strikes, gameTime });
                Finish(false, reason);
                return;
            }
            if (visible.Distance > HuntThrowReach)
            {
                if (approachedLivePrey || strikes != 0 || !TryApproachLivePrey(visible, gameTime))
                    Finish(false, "prey_left_native_reach");
                return;
            }
            if (visible.Distance > HuntReach)
            {
            if (strikes == 0) PrepareSpearThrow(gameTime);
                else Finish(false, "prey_left_native_reach");
                return;
            }
            strikes++;
            nextStrike = gameTime + Math.Max(0.4, player.ComponentMiner.HitInterval + 0.05);
            action = owner.Start(new MeleeAttack(prey));
            return;
        }
        if (!lastHitByHunter)
        {
            Finish(false, "prey_death_unattributed");
            return;
        }

        if (gameTime < nextPoll) return;
        nextPoll = gameTime + 0.25;
        var drop = VisiblePreyDrop();
        if (drop is not null) action = owner.Start(drop.CreatePickup(1));
        else if (gameTime > nextStrike + health.CorpseDuration + 4) Finish(false, "native_prey_drop_unconfirmed");
    }

    private bool TryApproachLivePrey(NearbyEntityView visible, double gameTime)
    {
        var body = player.ComponentBody;
        var offset = (visible.Position - body.Position).XZ;
        float distance = offset.Length() - 3.8f;
        if (distance < 0.8f || !PlaySurvivalSenses.OnDryGround(preyHealth!.Entity.FindComponent<ComponentBody>(true)))
            return false;
        approachedLivePrey = true;
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
        huntApproach = new(player, owner, bearing, distance, "prey", false, gameTime,
            ObserveHuntFood, readDryWindow, log, permitLandLeg: permitLandLeg,
            readMovingTarget: () => ReadFoodBirdPoint(visible.EntityId, HuntApproachReach), movingTargetReach: 3.8f);
        log(new { kind = "play_hunt_stalk", actor = player.PlayerData.Name, gameTime, observedDistance = visible.Distance, distance });
        return true;
    }

    private Vector3? ReadFoodBirdPoint(int entity, float range)
    {
        var visible = owner.Queries.FindVisibleObjectsAround(range).Entities.FirstOrDefault(view => view.EntityId == entity);
        return visible is not null && player.Project.FindEntity(entity)?.FindComponent<ComponentCreature>() is { } bird
            && bird.ComponentHealth.Health > 0f && MathF.Abs(visible.Position.Y - player.ComponentBody.Position.Y) <= 6f
                ? visible.Position : null;
    }

    private void PrepareSpearThrow(double gameTime)
    {
        var quarry = prey!.FindComponent<ComponentBody>(true);
        var point = quarry.BoundingBox.Center();
        if (!preparingThrow)
        {
            preparingThrow = true;
            action = owner.Start(new FaceTowards(point, 2f));
            return;
        }
        var body = player.ComponentBody;
        var eye = player.ComponentCreatureModel.EyePosition;
        var miner = player.ComponentMiner;
        int value = miner.ActiveBlockValue;
        var block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
        float speed = block.GetProjectileSpeed(value) * (0.5f * ((miner.ComponentFactors?.StrengthFactor ?? 1f) - 1f) + 1f);
        if (!float.IsFinite(speed) || speed <= 0f)
        {
            Finish(false, "prey_throw_conditions_changed");
            return;
        }
        // 预测只用仍在视野内的猎物和本人的动作。命中、伤害和掉落仍由原生系统结算。
        var origin = eye + 0.4f * body.Matrix.Right;
        var direction = Vector3.Normalize(point - origin);
        var launch = origin;
        float damping = block.GetProjectileDamping(value);
        float drag = damping is > 0f and < 1f ? -MathF.Log(damping) : 0f;
        float flight = 0f;
        for (int refinement = 0; refinement < 5; refinement++)
        {
            float low = 0.015f, high = 2.2f;
            Vector3 needed = default;
            for (int iteration = 0; iteration < 26; iteration++)
            {
                flight = (low + high) * 0.5f;
                float travel = drag > 0.0001f ? (1f - MathF.Exp(-drag * flight)) / drag : flight;
                float falling = drag > 0.0001f ? 10f * (flight - travel) / drag : 5f * flight * flight;
                needed = (point + flight * quarry.Velocity - launch - block.ProjectileTipOffset * direction
                    + falling * Vector3.UnitY) / travel - body.Velocity;
                if (needed.Length() > speed) low = flight; else high = flight;
            }
            if (needed.LengthSquared() < 0.01f) { Finish(false, "prey_intercept_unavailable"); return; }
            direction = Vector3.Normalize(needed);
            if (!player.Project.FindSubsystem<SubsystemProjectiles>(true).CanFireProjectile(value, origin,
                body.Velocity + speed * direction, player, out launch)) { Finish(false, "native_spear_launch_blocked"); return; }
        }
        // 原生木矛不需要蓄力。瞄准完成的同一帧投出，使用真实出手位置、阻力和当前可见速度。
        var aim = eye + direction * Math.Clamp((point - eye).Length() + 4f, 4f, 24f);
        thrown = true;
        strikes++;
        nextStrike = gameTime + Math.Max(1.25, flight + 0.65);
        action = owner.Start(new UseAimedItem(new OwnItemSlot(miner.Inventory.ActiveSlotIndex, value), new PointTarget(aim), 0f));
        log(new { kind = "play_hunt_throw_started", actor = player.PlayerData.Name, gameTime,
            distance = (point - eye).Length(), flight, speed, damping, preyVelocity = quarry.Velocity.ToString() });
    }

    private void TrackPreyInjury(Injury injury)
    {
        if (preyHealth?.Health > 0f) lastHitByHunter = injury.Attacker?.Entity == player.Entity;
    }

    private NearbyPickableView? VisiblePreyDrop() => owner.Queries.FindVisibleObjectsAround(24f).Pickables
        .FirstOrDefault(item => preyDrops.Contains(NativeAcquisitionAudit.IdentityFor(item.CreatePickup(1).Target)));

    private bool TryApproachPreyDrop(double gameTime)
    {
        if (approachedPreyDrop || VisiblePreyDrop() is not { } drop || PlaySurvivalFood.DescribeDrop(drop) is not { } food)
            return false;
        var body = player.ComponentBody;
        var offset = (drop.Position - body.Position).XZ;
        if (offset.LengthSquared() < 0.04f || offset.Length() > 24f || food.Point.Y - body.Position.Y is < -3f or > 3f
            || permitLandLeg?.Invoke(food.Point, gameTime) == false) return false;
        var panorama = owner.Queries.ReadPanorama(26f);
        var path = PlayObservedPath.Find(panorama, body.Position, food.Point,
            (_, end) => permitLandLeg?.Invoke(end, gameTime) != false, goalReach: 0.9f, maximumAboveTarget: 0.2f);
        if (offset.Length() > 5f && path is null) return false;
        approachedPreyDrop = true;
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
        huntApproach = new(player, owner, bearing, Math.Clamp(offset.Length() + 1f, 2f, 25f), "food", false,
            gameTime, ObserveHuntFood, readDryWindow, log, foodTarget: food, permitLandLeg: permitLandLeg, observedPath: path);
        log(new { kind = "play_hunt_approach", actor = player.PlayerData.Name, distance = offset.Length(), gameTime });
        return true;
    }

    private (JsonElement, PlaySurvivalFood.Food[]) ObserveHuntFood()
    {
        var observed = tools.Call("observe", new { block_limit = 1, limit = 8 });
        return observed.State == "returned" ? (observed.Data, PlaySurvivalFood.VisibleDrops(observed.Data).ToArray()) : (default, []);
    }

    // 只把这只猎物的原生掉落和当前角色实际收到的库存增量算作猎获。
    private void TrackPreyDrop(NaturalResourceOriginFact fact)
    {
        if (lastHitByHunter && fact.OriginEntityId == preyId && PlaySurvivalFood.IsRawMeat(fact.Value))
        {
            preyDrops.Add(fact.SourceId);
            log(new { kind = "play_hunt_native_drop", actor = player.PlayerData.Name, source = fact.SourceId, value = fact.Value });
        }
    }

    private void TrackPreyPickup(NativeAcquisitionFact fact)
    {
        if (fact.ActorId == hunterId && preyDrops.Contains(fact.SourceId)
            && fact.InventoryBefore is int before && fact.InventoryAfter is int after && after > before)
            meatPicked += after - before;
    }

    private void StopHunt()
    {
        if (prey is null) return;
        huntApproach?.Stop();
        huntApproach = null;
        NaturalResourceOriginAudit.Recorded -= TrackPreyDrop;
        NativeAcquisitionAudit.Recorded -= TrackPreyPickup;
        preyHealth!.Injured -= TrackPreyInjury;
        preyHealth = null;
        prey = null;
    }
}
