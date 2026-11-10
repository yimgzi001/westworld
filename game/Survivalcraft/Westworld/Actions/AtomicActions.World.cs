using System.Text.Json;
using Engine;

namespace Game.Westworld {
    public sealed partial class AtomicActions {
        ComponentCreatureModel WorldEyes => m_miner?.ComponentCreature?.ComponentCreatureModel;
        SubsystemTerrain WorldTerrain => m_miner.m_subsystemTerrain;
        IInventory HeldInventory => m_miner?.Inventory;

        float InteractionReach => Math.Min(
            m_miner.m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Creative ? SettingsManager.CreativeReach : 5f,
            SettingsManager.VisibilityRange
        );

        void RegisterWorld() {
            Register<Dig>(StartDig, CanTargetWorld);
            Register<Place>(StartPlace, CanHoldWorldItem);
            Register<Interact>(StartInteract, CanTargetWorld);
            Register<UseItem>(StartUseItem, CanHoldWorldItem);
            Register<ConfigureBlock>(StartConfigureBlock, CanTargetWorld);
            Register<ConfigureItem>(StartConfigureItem, () => HeldInventory != null);
            Register<MeleeAttack>(StartMelee, CanTargetWorld);
            Register<UseAimedItem>(StartAimedItem, CanHoldWorldItem);
        }

        bool CanTargetWorld() => WorldEyes != null && m_body != null;

        bool CanHoldWorldItem() => CanTargetWorld() && HeldInventory is { ActiveSlotIndex: >= 0, VisibleSlotsCount: > 0 };

        AtomicActionRun StartDig(Dig action) {
            if (!float.IsFinite(action.Timeout) || action.Timeout <= 0f || action.Target == null) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (m_miner.AnyIsPending || m_miner.DigCellFace.HasValue) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            AtomicActionFailure failure = FindWorldRay(action.Target, RaycastMode.Digging, InteractionReach, out _, out object hit);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            TerrainRaycastResult terrainHit = (TerrainRaycastResult)hit;
            int tool = m_miner.ActiveBlockValue;
            int? toolSlot = HeldInventory?.ActiveSlotIndex;
            if (!m_miner.IsLevelSufficientForTool(tool) || !float.IsFinite(m_miner.CalculateDigTime(terrainHit.Value, tool))) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (AdvanceDig(terrainHit) is AtomicActionResult result) {
                return new AtomicActionRun(action, result);
            }

            double deadline = m_time.GameTime + action.Timeout;
            return StartRunning(action, () => {
                if (m_miner.AnyIsPending) {
                    return Failed(AtomicActionFailure.Busy);
                }
                if (m_miner.ActiveBlockValue != tool || HeldInventory?.ActiveSlotIndex != toolSlot) {
                    return Failed(AtomicActionFailure.TargetChanged);
                }
                if (m_time.GameTime >= deadline) {
                    return Failed(AtomicActionFailure.TimedOut);
                }
                AtomicActionFailure current = FindWorldRay(action.Target, RaycastMode.Digging, InteractionReach, out _, out object currentHit);
                if (current != AtomicActionFailure.None) {
                    return Failed(current);
                }
                if (m_miner.DigCellFace is CellFace face && face.Point != action.Target.Cell) {
                    return Failed(AtomicActionFailure.Blocked);
                }
                return AdvanceDig((TerrainRaycastResult)currentHit);
            }, () => {
                if (m_miner.DigCellFace is CellFace face && face.Point == action.Target.Cell) {
                    m_miner.DigCellFace = null;
                }
            });
        }

        AtomicActionResult? AdvanceDig(TerrainRaycastResult hit) {
            if (!m_miner.Dig(hit)) {
                return null;
            }
            return SameWorldBlock(WorldTerrain.Terrain.GetCellValue(hit.CellFace.Point), hit.Value)
                ? Failed(AtomicActionFailure.Blocked)
                : Completed(1);
        }

        AtomicActionRun StartPlace(Place action) {
            if (action.Against == null) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (m_miner.AnyIsPending) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            AtomicActionFailure failure = CheckWorldItem(action.Item, true);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            failure = FindWorldRay(action.Against, RaycastMode.Interaction, InteractionReach, out _, out object hit);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            if (!SelectWorldItem(action.Item)) {
                return Reject(action, AtomicActionFailure.Blocked);
            }
            bool placed = m_miner.Place((TerrainRaycastResult)hit);
            if (m_miner.IsPending(ComponentMiner.PendingAction.Place)) {
                placed = m_miner.ExecutePlace(ComponentMiner.TargetMode.Pending);
            }
            return placed ? Succeed(action, 1) : Reject(action, AtomicActionFailure.Blocked);
        }

        AtomicActionRun StartInteract(Interact action) {
            if (action.Target is not (BlockTarget or MovingBlockTarget)) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (m_miner.AnyIsPending) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            AtomicActionFailure failure = FindWorldRay(action.Target, RaycastMode.Interaction, InteractionReach, out _, out object hit);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            bool interacted;
            if (hit is TerrainRaycastResult terrain) {
                // Signs with links can open a browser. Autonomous editing uses ConfigureBlock.
                if (WorldBlock(terrain.Value) is SignBlock) {
                    return Reject(action, AtomicActionFailure.PreconditionsNotMet);
                }
                interacted = m_miner.Interact(terrain);
            }
            else {
                interacted = m_miner.Interact((MovingBlocksRaycastResult)hit);
            }
            if (m_miner.IsPending(ComponentMiner.PendingAction.Interact)) {
                interacted = m_miner.ExecuteInteract(ComponentMiner.TargetMode.Pending);
            }
            if (interacted && hit is TerrainRaycastResult terrainHit
                && (WorldBlock(terrainHit.Value) is DoorBlock or FenceGateBlock or TrapdoorBlock or SwitchBlock)
                && SameWorldBlock(WorldTerrain.Terrain.GetCellValue(terrainHit.CellFace.Point), terrainHit.Value)) {
                interacted = false;
            }
            return interacted ? Succeed(action, 1) : Reject(action, AtomicActionFailure.Blocked);
        }

        AtomicActionRun StartUseItem(UseItem action) {
            if (m_miner.AnyIsPending) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            AtomicActionFailure failure = CheckWorldItem(action.Item, true);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            if (!m_miner.IsLevelSufficientForTool(action.Item.ExpectedValue)) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }

            Block item = WorldBlock(action.Item.ExpectedValue);
            if (item is MilkBucketBlock or RottenMilkBucketBlock or PumpkinSoupBucketBlock or RottenPumpkinSoupBucketBlock
                or HammerBlock) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            RaycastMode mode = item switch {
                EmptyBucketBlock => RaycastMode.Gathering,
                BoatBlock or MatchBlock or PaintBucketBlock or PaintStripperBucketBlock => RaycastMode.Digging,
                _ => RaycastMode.Interaction
            };
            failure = FindWorldRay(action.Target, mode, InteractionReach, out Ray3 ray, out object hit);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            bool painting = item is PaintBucketBlock or PaintStripperBucketBlock;
            if (mode == RaycastMode.Gathering || painting) {
                hit ??= m_miner.Raycast(ray, mode);
            }
            if (mode == RaycastMode.Gathering && hit is TerrainRaycastResult gathered
                && IsWorldRayOccluded(ray, gathered.Distance, gathered.CellFace.Point)) {
                return Reject(action, AtomicActionFailure.Blocked);
            }
            if (!SelectWorldItem(action.Item)) {
                return Reject(action, AtomicActionFailure.Blocked);
            }
            bool used = m_miner.Use(ray);
            if (m_miner.IsPending(ComponentMiner.PendingAction.Use)) {
                used = m_miner.ExecuteUse(ComponentMiner.TargetMode.Pending);
            }
            if (used && painting && hit is TerrainRaycastResult paintTarget
                && SameWorldBlock(WorldTerrain.Terrain.GetCellValue(paintTarget.CellFace.Point), paintTarget.Value)) {
                used = false;
            }
            return used ? Succeed(action, 1) : Reject(action, AtomicActionFailure.Blocked);
        }

        AtomicActionRun StartMelee(MeleeAttack action) {
            if (m_miner.AnyIsPending) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            AtomicActionFailure failure = FindWorldRay(new EntityTarget(action.Target), RaycastMode.Interaction, 2f, out Ray3 ray, out object hit);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            int tool = m_miner.ActiveBlockValue;
            if (!m_miner.IsLevelSufficientForTool(tool) || WorldBlock(tool).GetMeleeHitProbability(tool) <= 0f) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            BodyRaycastResult bodyHit = (BodyRaycastResult)hit;
            ComponentHealth targetHealth = action.Target.FindComponent<ComponentHealth>();
            ComponentDamage targetDamage = action.Target.FindComponent<ComponentDamage>();
            float? health = targetHealth?.Health ?? targetDamage?.Hitpoints;
            double lastHit = m_miner.m_lastHitTime;
            m_miner.Hit(bodyHit.ComponentBody, bodyHit.HitPoint(), ray.Direction);
            if (m_miner.IsPending(ComponentMiner.PendingAction.Attack)) {
                m_miner.ExecuteHit(ComponentMiner.TargetMode.Pending);
            }
            if (m_miner.m_lastHitTime == lastHit) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            return Succeed(action, (targetHealth?.Health ?? targetDamage?.Hitpoints) < health ? 1 : 0);
        }

        AtomicActionRun StartConfigureBlock(ConfigureBlock action) {
            if (action.Target == null || action.Settings.ValueKind != JsonValueKind.Object) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            AtomicActionFailure failure = FindWorldRay(action.Target, RaycastMode.Interaction, InteractionReach, out _, out object hit);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            int value = ((TerrainRaycastResult)hit).Value;
            Point3 cell = action.Target.Cell;
            Block block = WorldBlock(value);
            if (block is SignBlock) {
                return WriteSign(action, cell);
            }
            if (block is FurnitureBlock) {
                return RenameFurnitureDesign(action, value, action.Settings);
            }
            if (!block.IsEditable_(value)) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (block is TruthTableCircuitBlock or MemoryBankBlock) {
                return ConfigureCircuit(action, value, block, cell);
            }
            failure = GetConfiguredBlockValue(block, value, action.Settings, out int configured);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            if (configured != value) {
                WorldTerrain.ChangeCell(cell.X, cell.Y, cell.Z, configured);
                if (!SameWorldBlock(WorldTerrain.Terrain.GetCellValue(cell), configured)) {
                    return Reject(action, AtomicActionFailure.Blocked);
                }
                switch (block) {
                    case PistonBlock:
                        QueueConfiguredCircuit(cell, 0);
                        break;
                    case AdjustableDelayGateBlock delay:
                        QueueConfiguredCircuit(cell, delay.GetFace(configured));
                        break;
                    case BatteryBlock:
                        QueueConfiguredCircuit(cell, 4, true);
                        break;
                }
            }
            return Succeed(action, configured == value ? 0 : 1);
        }

        AtomicActionRun StartConfigureItem(ConfigureItem action) {
            AtomicActionFailure failure = CheckWorldItem(action.Item, false);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            if (action.Settings.ValueKind != JsonValueKind.Object) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            int value = action.Item.ExpectedValue;
            Block block = WorldBlock(value);
            if (block is FurnitureBlock) {
                return RenameFurnitureDesign(action, value, action.Settings);
            }
            if (block is CrossbowBlock && OnlySetting(action.Settings, "cock", out JsonElement cock)
                && cock.ValueKind == JsonValueKind.True) {
                int data = CrossbowBlock.SetDraw(Terrain.ExtractData(value), 15);
                return ReplaceConfiguredItem(action, Terrain.ReplaceData(value, data), true);
            }
            if (block is BowBlock or CrossbowBlock or MusketBlock) {
                return LoadWeapon(action);
            }
            if (!block.IsEditable_(value)) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (block is TruthTableCircuitBlock or MemoryBankBlock) {
                return ConfigureCircuitItem(action, value, block);
            }
            failure = GetConfiguredBlockValue(block, value, action.Settings, out int configured);
            return failure == AtomicActionFailure.None
                ? ReplaceConfiguredItem(action, configured, false)
                : Reject(action, failure);
        }

        AtomicActionRun ReplaceConfiguredItem(ConfigureItem action, int configured, bool singleItem) {
            int slot = action.Item.Slot;
            int value = action.Item.ExpectedValue;
            if (value == configured) {
                return Succeed(action);
            }
            IInventory inventory = HeldInventory;
            int count = inventory.GetSlotCount(slot);
            if (inventory is not ComponentCreativeInventory) {
                if (singleItem && count != 1) {
                    return Reject(action, AtomicActionFailure.PreconditionsNotMet);
                }
                if (inventory.GetSlotCapacity(slot, configured) < count) {
                    return Reject(action, AtomicActionFailure.InventoryFull);
                }
                int removed = inventory.RemoveSlotItems(slot, count);
                if (removed != count) {
                    inventory.AddSlotItems(slot, value, removed);
                    return Reject(action, AtomicActionFailure.Blocked);
                }
            }
            inventory.AddSlotItems(slot, configured, count);
            return inventory.GetSlotValue(slot) == configured ? Succeed(action, 1) : Reject(action, AtomicActionFailure.Blocked);
        }

        AtomicActionRun LoadWeapon(ConfigureItem action) {
            if (action.Settings.EnumerateObject().Count() != 2
                || !ReadSettingInt(action.Settings, "sourceSlot", out int sourceSlot)
                || !ReadSettingInt(action.Settings, "expectedSourceValue", out int sourceValue) || sourceSlot == action.Item.Slot) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            AtomicActionFailure failure = CheckWorldItem(new OwnItemSlot(sourceSlot, sourceValue), false);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            IInventory inventory = HeldInventory;
            int targetSlot = action.Item.Slot;
            if (inventory is not ComponentCreativeInventory && inventory.GetSlotCount(targetSlot) != 1) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            SubsystemBlockBehavior behavior = m_miner.m_subsystemBlockBehaviors
                .GetBlockBehaviors(Terrain.ExtractContents(action.Item.ExpectedValue))
                .FirstOrDefault(b => b is SubsystemBowBlockBehavior or SubsystemCrossbowBlockBehavior or SubsystemMusketBlockBehavior);
            if (behavior == null || behavior.GetProcessInventoryItemCapacity(inventory, targetSlot, sourceValue) < 1) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (inventory.RemoveSlotItems(sourceSlot, 1) != 1) {
                return Reject(action, AtomicActionFailure.Blocked);
            }
            behavior.ProcessInventoryItem(inventory, targetSlot, sourceValue, 1, 1, out int returnedValue, out int returnedCount);
            if (returnedValue != 0 && returnedCount > 0) {
                inventory.AddSlotItems(sourceSlot, returnedValue, returnedCount);
            }
            return inventory.GetSlotValue(targetSlot) != action.Item.ExpectedValue ? Succeed(action, 1) : Reject(action, AtomicActionFailure.Blocked);
        }

        static bool OnlySetting(JsonElement settings, string name, out JsonElement value) =>
            settings.TryGetProperty(name, out value) && settings.EnumerateObject().Count() == 1;

        static bool ReadSettingInt(JsonElement settings, string name, out int value) {
            value = 0;
            return settings.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out value);
        }

        AtomicActionRun StartAimedItem(UseAimedItem action) {
            if (!float.IsFinite(action.HoldDuration) || action.HoldDuration < 0f || action.Target == null) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (m_miner.AnyIsPending) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            AtomicActionFailure failure = CheckWorldItem(action.Item, true);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            int currentValue = action.Item.ExpectedValue;
            Block block = WorldBlock(currentValue);
            if (!block.IsAimable_(currentValue) || !m_miner.IsLevelSufficientForTool(currentValue) || !HasAmmunition(block, currentValue)) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            failure = FindWorldRay(action.Target, RaycastMode.Interaction, SettingsManager.VisibilityRange, out Ray3 ray, out _);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            if (!SelectWorldItem(action.Item)) {
                return Reject(action, AtomicActionFailure.Blocked);
            }

            IInventory inventory = HeldInventory;
            int slot = action.Item.Slot;
            int count = inventory.GetSlotCount(slot);
            bool aiming = false;
            bool released = false;
            double deadline = m_time.GameTime + action.HoldDuration;
            double lastAimStep = double.NegativeInfinity;
            AtomicActionResult? Step() {
                if (m_time.GameTime <= lastAimStep) {
                    return null;
                }
                lastAimStep = m_time.GameTime;
                if (m_miner.AnyIsPending) {
                    return Failed(AtomicActionFailure.Busy);
                }
                if (inventory != HeldInventory || inventory.ActiveSlotIndex != slot || inventory.GetSlotValue(slot) != currentValue
                    || inventory.GetSlotCount(slot) != count) {
                    return Failed(AtomicActionFailure.TargetChanged);
                }
                AtomicActionFailure current = FindWorldRay(action.Target, RaycastMode.Interaction, SettingsManager.VisibilityRange, out ray, out _);
                if (current != AtomicActionFailure.None) {
                    return Failed(current);
                }
                aiming = true;
                bool interrupted = m_miner.Aim(ray, AimState.InProgress);
                currentValue = inventory.GetSlotValue(slot);
                if (interrupted || WorldBlock(currentValue) != block || inventory.GetSlotCount(slot) != count) {
                    return Failed(AtomicActionFailure.Blocked);
                }
                if (m_time.GameTime < deadline) {
                    return null;
                }
                int data = Terrain.ExtractData(currentValue);
                if ((block is BowBlock && BowBlock.GetDraw(data) == 0) || (block is MusketBlock && !MusketBlock.GetHammerState(data))) {
                    return Failed(AtomicActionFailure.PreconditionsNotMet);
                }
                int projectiles = ReleaseAimedItem(ray);
                released = true;
                return projectiles > 0 ? Completed(projectiles) : Failed(AtomicActionFailure.Blocked);
            }

            void CancelAim() {
                if (released || !aiming) {
                    return;
                }
                if (inventory == HeldInventory && slot < inventory.VisibleSlotsCount && inventory.GetSlotValue(slot) == currentValue
                    && WorldBlock(currentValue) == block && inventory.GetSlotCount(slot) == count) {
                    int activeSlot = inventory.ActiveSlotIndex;
                    inventory.ActiveSlotIndex = slot;
                    try {
                        m_miner.Aim(ray, AimState.Cancelled);
                    }
                    finally {
                        inventory.ActiveSlotIndex = activeSlot;
                    }
                }
                // Clear native per-miner aim timers even if another system replaced the held item.
                foreach (SubsystemBlockBehavior behavior in m_miner.m_subsystemBlockBehaviors.GetBlockBehaviors(block.BlockIndex)) {
                    switch (behavior) {
                        case SubsystemBowBlockBehavior bow: bow.m_aimStartTimes.Remove(m_miner); break;
                        case SubsystemCrossbowBlockBehavior crossbow: crossbow.m_aimStartTimes.Remove(m_miner); break;
                        case SubsystemMusketBlockBehavior musket: musket.m_aimStartTimes.Remove(m_miner); break;
                    }
                }
            }

            AtomicActionResult? first;
            try {
                first = Step();
            }
            catch {
                CancelAim();
                throw;
            }
            if (!first.HasValue) {
                return StartRunning(action, Step, CancelAim);
            }
            CancelAim();
            return new AtomicActionRun(action, first.Value);
        }

        int ReleaseAimedItem(Ray3 ray) {
            SubsystemProjectiles projectiles = m_actor.Project.FindSubsystem<SubsystemProjectiles>(true);
            int count = 0;
            void CountProjectile(Projectile projectile) {
                if (projectile.OwnerEntity == m_actor) {
                    count++;
                }
            }
            projectiles.ProjectileAdded += CountProjectile;
            try {
                m_miner.Aim(ray, AimState.Completed);
                if (m_miner.IsPending(ComponentMiner.PendingAction.Aim)) {
                    m_miner.ExecuteAim(ComponentMiner.TargetMode.Pending);
                }
                return count;
            }
            finally {
                projectiles.ProjectileAdded -= CountProjectile;
            }
        }

        static bool HasAmmunition(Block block, int value) {
            int data = Terrain.ExtractData(value);
            return block switch {
                BowBlock => BowBlock.GetArrowType(data).HasValue,
                CrossbowBlock => CrossbowBlock.GetDraw(data) == 15 && CrossbowBlock.GetArrowType(data).HasValue,
                MusketBlock => MusketBlock.GetLoadState(data) == MusketBlock.LoadState.Loaded && MusketBlock.GetBulletType(data).HasValue,
                _ => true
            };
        }

        AtomicActionFailure CheckWorldItem(OwnItemSlot item, bool selectable) {
            IInventory inventory = HeldInventory;
            int limit = selectable ? Math.Min(inventory.VisibleSlotsCount, inventory.SlotsCount) : inventory.SlotsCount;
            if (!selectable && inventory is ComponentCreativeInventory creative) {
                limit = Math.Min(limit, creative.OpenSlotsCount);
            }
            if (item.Slot < 0 || item.Slot >= limit || Terrain.ExtractContents(item.ExpectedValue) == 0) {
                return AtomicActionFailure.InvalidArgument;
            }
            if (inventory.GetSlotCount(item.Slot) == 0) {
                return AtomicActionFailure.InsufficientItems;
            }
            return inventory.GetSlotValue(item.Slot) == item.ExpectedValue ? AtomicActionFailure.None : AtomicActionFailure.TargetChanged;
        }

        bool SelectWorldItem(OwnItemSlot item) {
            HeldInventory.ActiveSlotIndex = item.Slot;
            return HeldInventory.ActiveSlotIndex == item.Slot && m_miner.ActiveBlockValue == item.ExpectedValue;
        }

        static Block WorldBlock(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)];

        static bool SameWorldBlock(int value, int expected) => Terrain.ReplaceLight(value, 0) == Terrain.ReplaceLight(expected, 0);

        AtomicActionFailure FindWorldRay(ActionTarget target, RaycastMode mode, float reach, out Ray3 ray, out object hit) {
            Vector3 eye = WorldEyes.EyePosition;
            ray = default;
            hit = null;
            switch (target) {
                case null:
                    ray = new Ray3(eye, WorldEyes.EyeRotation.GetForwardVector());
                    return AtomicActionFailure.None;
                case PointTarget point:
                    Vector3 delta = point.Point - eye;
                    float distanceSquared = delta.LengthSquared();
                    if (!float.IsFinite(distanceSquared) || distanceSquared <= 0f) {
                        return AtomicActionFailure.InvalidArgument;
                    }
                    if (distanceSquared > reach * reach) {
                        return AtomicActionFailure.OutOfReach;
                    }
                    ray = new Ray3(eye, delta / MathF.Sqrt(distanceSquared));
                    return AtomicActionFailure.None;
                case EntityTarget entity:
                    if (entity.Entity == null || entity.Entity == m_actor) {
                        return AtomicActionFailure.InvalidArgument;
                    }
                    if (!IsLiveEntity(entity.Entity) || entity.Entity.FindComponent<ComponentBody>() is not ComponentBody body) {
                        return AtomicActionFailure.TargetChanged;
                    }
                    return FindWorldHit(target, Vector3.Zero, [body.BoundingBox], null, mode, reach, out ray, out hit);
                case BlockTarget block:
                    if (block.Face is < 0 or > 5 || !WorldTerrain.Terrain.IsCellValid(block.Cell)
                        || Terrain.ExtractContents(block.ExpectedValue) == 0) {
                        return AtomicActionFailure.InvalidArgument;
                    }
                    int value = WorldTerrain.Terrain.GetCellValue(block.Cell);
                    if (!SameWorldBlock(value, block.ExpectedValue)) {
                        return AtomicActionFailure.TargetChanged;
                    }
                    return FindWorldHit(target, new Vector3(block.Cell), WorldBlock(value).GetCustomInteractionBoxes(WorldTerrain, value),
                        block.Face, mode, reach, out ray, out hit);
                case MovingBlockTarget moving:
                    if (moving.Face is < 0 or > 5 || moving.Set == null || Terrain.ExtractContents(moving.ExpectedValue) == 0) {
                        return AtomicActionFailure.InvalidArgument;
                    }
                    if (moving.Set.Stopped || !m_miner.m_subsystemMovingBlocks.MovingBlockSetEnumerable.Contains(moving.Set)) {
                        return AtomicActionFailure.TargetChanged;
                    }
                    MovingBlock movingBlock = moving.Set.Blocks.FirstOrDefault(b => b.Offset == moving.Offset);
                    if (movingBlock == null || !SameWorldBlock(movingBlock.Value, moving.ExpectedValue)) {
                        return AtomicActionFailure.TargetChanged;
                    }
                    Vector3 origin = moving.Set.Position + new Vector3(moving.Offset);
                    return FindWorldHit(target, origin, WorldBlock(movingBlock.Value).GetCustomInteractionBoxes(WorldTerrain, movingBlock.Value),
                        moving.Face, mode, reach, out ray, out hit);
                default:
                    return AtomicActionFailure.InvalidArgument;
            }
        }

        AtomicActionFailure FindWorldHit(ActionTarget target,
            Vector3 origin,
            BoundingBox[] boxes,
            int? face,
            RaycastMode mode,
            float reach,
            out Ray3 ray,
            out object hit) {
            Vector3 eye = WorldEyes.EyePosition;
            reach = Math.Min(reach, SettingsManager.VisibilityRange);
            ray = default;
            hit = null;
            bool inRange = false;
            foreach (BoundingBox box in boxes) {
                if (BoundingBox.Distance(box, eye - origin) > reach) {
                    continue;
                }
                inRange = true;
                Vector3 sample = box.Center() + origin;
                if (face.HasValue) {
                    sample += (box.Max - box.Min) * CellFace.FaceToVector3(face.Value) * 0.5f;
                }
                if (TrySample(sample, box, out ray, out hit)) return AtomicActionFailure.None;
                // 南瓜等矮方块的碰撞盒中心可能被地形遮住，仍可核对露出的边缘。
                // 每条候选射线继续验证原生命中、距离和遮挡。
                var size = box.Max - box.Min;
                if (mode == RaycastMode.Digging && target is BlockTarget && !face.HasValue && size.Y < 0.95f) {
                    var upper = sample + new Vector3(0f, 0.45f * size.Y, 0f);
                    var x = new Vector3(0.35f * size.X, 0f, 0f);
                    var z = new Vector3(0f, 0f, 0.35f * size.Z);
                    foreach (var edge in new[] { upper, upper + x, upper - x, upper + z, upper - z }) {
                        if (TrySample(edge, box, out ray, out hit)) return AtomicActionFailure.None;
                    }
                }
            }
            return inRange || boxes.Length == 0 ? AtomicActionFailure.Blocked : AtomicActionFailure.OutOfReach;

            bool TrySample(Vector3 sample, BoundingBox box, out Ray3 acceptedRay, out object acceptedHit) {
                acceptedRay = default;
                acceptedHit = null;
                Vector3 delta = sample - eye;
                float lengthSquared = delta.LengthSquared();
                if (!float.IsFinite(lengthSquared) || lengthSquared <= 0f) {
                    return false;
                }
                Ray3 candidate = new(eye, delta / MathF.Sqrt(lengthSquared));
                object candidateHit = m_miner.Raycast(candidate, mode, Reach: reach);
                if (!TryMatchWorldHit(target, candidateHit, out float distance)) {
                    return false;
                }
                if (target is EntityTarget) {
                    // The native body ray inflates hitboxes. Check for walls up to the physical body as well.
                    distance = candidate.Intersection(box) ?? distance;
                }
                if (distance > reach || IsWorldRayOccluded(candidate, distance, (target as BlockTarget)?.Cell)) {
                    return false;
                }
                acceptedRay = candidate;
                acceptedHit = candidateHit;
                return true;
            }
        }

        static bool TryMatchWorldHit(ActionTarget target, object hit, out float distance) {
            distance = 0f;
            switch (target, hit) {
                case (BlockTarget block, TerrainRaycastResult terrain) when block.Cell == terrain.CellFace.Point
                    && (!block.Face.HasValue || block.Face.Value == terrain.CellFace.Face) && SameWorldBlock(terrain.Value, block.ExpectedValue):
                    distance = terrain.Distance;
                    return true;
                case (EntityTarget entity, BodyRaycastResult body) when entity.Entity == body.ComponentBody.Entity:
                    distance = body.Distance;
                    return true;
                case (MovingBlockTarget moving, MovingBlocksRaycastResult movingHit) when movingHit.MovingBlockSet == moving.Set
                    && movingHit.MovingBlock is MovingBlock movingBlock && movingBlock.Offset == moving.Offset
                    && SameWorldBlock(movingBlock.Value, moving.ExpectedValue) && MatchesMovingFace(moving, movingHit):
                    distance = movingHit.Distance;
                    return true;
                default:
                    return false;
            }
        }

        static bool MatchesMovingFace(MovingBlockTarget target, MovingBlocksRaycastResult hit) {
            if (!target.Face.HasValue) {
                return true;
            }
            if (!hit.BlockBoundingBox.HasValue) {
                return false;
            }
            Vector3 point = hit.HitPoint() - target.Set.Position - new Vector3(target.Offset);
            BoundingBox box = hit.BlockBoundingBox.Value;
            Vector3 halfSize = (box.Max - box.Min) * 0.5f;
            Vector3 fromCenter = point - box.Center();
            Vector3 normal = CellFace.FaceToVector3(target.Face.Value);
            return MathF.Abs(Vector3.Dot(fromCenter, normal) - Vector3.Dot(halfSize * normal, normal)) <= 0.001f;
        }

        bool IsWorldRayOccluded(Ray3 ray, float distance, Point3? targetCell = null) {
            // Native gathering rays skip walls. Check physical terrain along the same ray before the intended hit.
            const float surfaceTolerance = 0.005f;
            return distance > surfaceTolerance && WorldTerrain.Raycast(ray.Position, ray.Position + ray.Direction * distance,
                false, true, (value, hitDistance) => hitDistance < distance - surfaceTolerance && WorldBlock(value).IsCollidable_(value))
                is TerrainRaycastResult obstruction && obstruction.CellFace.Point != targetCell;
        }
    }
}
