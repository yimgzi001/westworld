#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using GameEntitySystem;

internal sealed partial class PlaySurvivalProduction
{
    private const float HuntReach = 2.35f;
    private readonly HashSet<Guid> preyDrops = new();
    private Entity? prey;
    private PlaySurvivalTravel? huntApproach;
    private bool approachedPreyDrop;
    private Guid preyId, hunterId;
    private int strikes, meatPicked;
    private double nextStrike;

    private static bool IsSpear(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)] is SpearBlock;

    private static int HuntWeapon(ComponentPlayer player) => FindSlot(player.ComponentMiner.Inventory, IsSpear,
        player.ComponentMiner.Inventory.VisibleSlotsCount);

    private static Entity? FindFoodBird(ComponentPlayer player, IEnumerable<NearbyEntityView> visible, float distance) =>
        visible.Where(view => view.Distance <= distance).Select(view => player.Project.FindEntity(view.EntityId)).FirstOrDefault(bird =>
            bird?.FindComponent<ComponentCreature>() is { Category: CreatureCategory.Bird } creature
            && bird.FindComponent<ComponentChaseBehavior>()?.AttacksPlayer != true
            && creature.ComponentHealth.Health > 0f && PlaySurvivalSenses.OnDryGround(creature.ComponentBody)
            && MathF.Abs(creature.ComponentBody.Position.Y - player.ComponentBody.Position.Y) <= 1f
            && bird.FindComponent<ComponentLoot>() is { } loot && loot.m_lootList.Any(item =>
                item.MinCount > 0 && item.Probability >= 0.9f && PlaySurvivalFood.IsRawMeat(item.Value)));

    private void StartHunt(double gameTime)
    {
        var carry = player.ComponentMiner.Inventory;
        int weapon = HuntWeapon(player);
        var target = FindFoodBird(player, owner.Queries.FindVisibleObjectsAround(HuntReach).Entities, HuntReach);
        if (weapon < 0 || target is null)
        {
            Finish(false, "hunt_weapon_or_visible_prey_unavailable");
            return;
        }
        prey = target;
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
        }
        var health = prey!.FindComponent<ComponentHealth>(true);
        if (health.Health > 0f)
        {
            if (gameTime < nextStrike) return;
            if (strikes >= 2 || !IsSpear(player.ComponentMiner.ActiveBlockValue)
                || !owner.Queries.FindVisibleObjectsAround(HuntReach).Entities.Any(view => view.EntityId == prey.Id))
            {
                Finish(false, "prey_left_native_reach");
                return;
            }
            strikes++;
            nextStrike = gameTime + Math.Max(0.4, player.ComponentMiner.HitInterval + 0.05);
            action = owner.Start(new MeleeAttack(prey));
            return;
        }

        if (gameTime < nextPoll) return;
        nextPoll = gameTime + 0.25;
        var drop = VisiblePreyDrop();
        if (drop is not null) action = owner.Start(drop.CreatePickup(1));
        else if (gameTime > nextStrike + health.CorpseDuration + 4) Finish(false, "native_prey_drop_unconfirmed");
    }

    private NearbyPickableView? VisiblePreyDrop() => owner.Queries.FindVisibleObjectsAround(8f).Pickables
        .FirstOrDefault(item => preyDrops.Contains(NativeAcquisitionAudit.IdentityFor(item.CreatePickup(1).Target)));

    private bool TryApproachPreyDrop(double gameTime)
    {
        if (approachedPreyDrop || VisiblePreyDrop() is not { } drop || PlaySurvivalFood.DescribeDrop(drop) is not { } food)
            return false;
        var body = player.ComponentBody;
        var offset = (drop.Position - body.Position).XZ;
        if (offset.LengthSquared() < 0.04f || !PlaySurvivalFood.CanApproachOnLand(food, body.Position)) return false;
        approachedPreyDrop = true;
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
        huntApproach = new(player, owner, bearing, Math.Clamp(offset.Length() + 1f, 2f, 9f), "food", false,
            gameTime, ObserveHuntFood, readDryWindow, log, foodTarget: food);
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
        if (fact.OriginEntityId == preyId && PlaySurvivalFood.IsRawMeat(fact.Value)) preyDrops.Add(fact.SourceId);
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
        prey = null;
    }
}
