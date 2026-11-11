using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed partial class AtomicActions {
        ComponentClothing ActorClothing => m_actor.FindComponent<ComponentClothing>();

        ComponentSleep ActorSleep => m_actor.FindComponent<ComponentSleep>();

        internal ComponentActorNutrition ActorNutrition => m_actor.FindComponent<ComponentActorNutrition>();

        IInventory CarryInventory => m_miner?.Inventory;

        SubsystemPickables WorldPickables => m_actor.Project.FindSubsystem<SubsystemPickables>();

        void RegisterInventory() {
            Register<Eat>(StartEat, () => CarryInventory != null && (ActorClothing != null || ActorNutrition != null));
            Register<Sleep>(StartSleep, () => ActorSleep != null);
            Register<WakeUp>(StartWakeUp, () => ActorSleep != null);
            Register<PickUp>(StartPickUp, () => m_body != null && CarryInventory != null && WorldPickables != null);
            Register<Drop>(StartDrop, () => m_body != null && CarryInventory != null && WorldPickables != null);
            Register<TransferItems>(StartTransfer, () => m_miner != null && m_body != null);
            Register<Equip>(StartEquip, () => CarryInventory != null && ActorClothing != null);
            Register<Unequip>(StartUnequip, () => CarryInventory != null && ActorClothing != null);
            Register<Craft>(StartCraft, () => m_body != null && CarryInventory != null && WorldPickables != null);
        }

        AtomicActionRun StartEat(Eat action) {
            IInventory inventory = CarryInventory;
            AtomicActionFailure failure = ValidateInventorySource(inventory, action.Slot, action.ExpectedValue, action.Count);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            int value = action.ExpectedValue;
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
            float nutrition = block.GetNutritionalValue(value);
            if (nutrition <= 0f) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }

            ComponentClothing clothing = ActorClothing;
            if (clothing == null && ActorNutrition is { } reserve) {
                if (block is BucketBlock || block.GetSicknessProbability(value) > 0f || reserve.Food >= 0.98f) {
                    return Reject(action, AtomicActionFailure.PreconditionsNotMet);
                }
                int consumed = 0;
                while (consumed < action.Count && reserve.Food < 0.98f
                    && inventory.GetSlotValue(action.Slot) == value && inventory.RemoveSlotItems(action.Slot, 1) == 1) {
                    reserve.Food += nutrition;
                    consumed++;
                }
                return consumed > 0 ? Succeed(action, consumed) : Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            int eaten = 0;
            int available = Math.Min(action.Count, inventory.GetSlotCount(action.Slot));
            while (eaten < available && inventory.GetSlotValue(action.Slot) == value) {
                if (block is BucketBlock && inventory.GetSlotCount(action.Slot) > 1
                    && FindInsertionSlot(inventory, EmptyBucketBlock.Index) < 0) {
                    return eaten > 0 ? Succeed(action, eaten) : Reject(action, AtomicActionFailure.InventoryFull);
                }
                if (inventory.RemoveSlotItems(action.Slot, 1) != 1) {
                    break;
                }

                // The native clothing processor handles eating, sickness and returning empty containers.
                clothing.ProcessSlotItems((int)ClothingSlot.Head, value, 1, 1, out int returnedValue, out int returnedCount);
                if (returnedCount > 0 && returnedValue != 0) {
                    int returnedSlot = GetInsertCapacity(inventory, action.Slot, returnedValue) > 0
                        ? action.Slot : FindInsertionSlot(inventory, returnedValue);
                    int inserted = returnedSlot >= 0
                        ? InsertStack(inventory, returnedSlot, returnedValue, Math.Min(returnedCount, GetInsertCapacity(inventory, returnedSlot, returnedValue)))
                        : 0;
                    if (inserted < returnedCount) {
                        WorldPickables?.AddPickable(returnedValue, returnedCount - inserted, m_body.Position, Vector3.Zero, null, m_actor);
                    }
                    if (returnedValue == value) {
                        break;
                    }
                }
                eaten++;
            }
            return eaten > 0 ? Succeed(action, eaten) : Reject(action, AtomicActionFailure.PreconditionsNotMet);
        }

        AtomicActionRun StartSleep(Sleep action) {
            ComponentSleep sleep = ActorSleep;
            if (sleep.IsSleeping) {
                return Succeed(action);
            }
            if (!sleep.CanSleep(out _)) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            sleep.Sleep(true);
            return sleep.IsSleeping ? Succeed(action) : Reject(action, AtomicActionFailure.Blocked);
        }

        AtomicActionRun StartWakeUp(WakeUp action) {
            ComponentSleep sleep = ActorSleep;
            if (!sleep.IsSleeping) {
                return Succeed(action);
            }
            sleep.WakeUp();
            return !sleep.IsSleeping ? Succeed(action) : Reject(action, AtomicActionFailure.Blocked);
        }

        AtomicActionRun StartPickUp(PickUp action) {
            Pickable target = action.Target;
            Vector3 center = m_body.Position + new Vector3(0f, m_body.StanceBoxSize.Y * 0.5f, 0f);
            if ((action.FoodValue ?? action.WoodValue) is int observedValue) {
                Block block = BlocksManager.Blocks[Terrain.ExtractContents(observedValue)];
                if (target != null || observedValue <= 0 || action.FoodValue.HasValue && action.WoodValue.HasValue
                    || (action.WoodValue.HasValue ? block is not WoodBlock : block.GetNutritionalValue(observedValue) <= 0f)) {
                    return Reject(action, AtomicActionFailure.InvalidArgument);
                }
                target = WorldPickables.Pickables
                    .Where(item => !item.ToRemove && item.Count > 0 && item.Value == observedValue
                        && Vector3.DistanceSquared(center, item.Position) <= item.DistanceToPick * item.DistanceToPick)
                    .MinBy(item => Vector3.DistanceSquared(center, item.Position));
                if (target == null) {
                    return Reject(action, AtomicActionFailure.OutOfReach);
                }
            }
            if (target == null || action.Count <= 0) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (!WorldPickables.Pickables.Contains(target) || target.ToRemove || target.Count <= 0) {
                return Reject(action, AtomicActionFailure.TargetChanged);
            }
            float range = target.DistanceToPick;
            if (Vector3.DistanceSquared(center, target.Position) > range * range) {
                return Reject(action, AtomicActionFailure.OutOfReach);
            }
            if (target.FlyToGatherer is { } gatherer && gatherer.Entity != m_actor) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            Vector3 direction = target.Position - center;
            float length = direction.Length();
            if (length > 0f && m_actor.Project.FindSubsystem<SubsystemTerrain>()?.Raycast(
                    center, target.Position, false, true,
                    (value, distance) => distance < length - 0.05f && BlocksManager.Blocks[Terrain.ExtractContents(value)].IsCollidable_(value)
                ).HasValue == true) {
                return Reject(action, AtomicActionFailure.Blocked);
            }

            int requested = Math.Min(action.Count, target.Count);
            int acquired = 0;
            List<InventorySlotView> received = [];
            if (BlocksManager.Blocks[Terrain.ExtractContents(target.Value)] is ExperienceBlock
                && m_actor.FindComponent<ComponentLevel>() is { } level) {
                level.AddExperience(requested, true);
                acquired = requested;
            }
            else {
                IInventory inventory = CarryInventory;
                for (int slot = 0; slot < inventory.SlotsCount && acquired < requested; slot++) {
                    int count = Math.Min(requested - acquired, GetInsertCapacity(inventory, slot, target.Value));
                    if (count > 0) {
                        int inserted = InsertStack(inventory, slot, target.Value, count);
                        if (inserted > 0) {
                            received.Add(new InventorySlotView(slot, target.Value, inserted));
                            acquired += inserted;
                        }
                    }
                }
            }
            if (acquired <= 0) {
                return Reject(action, AtomicActionFailure.InventoryFull);
            }
            target.Count -= acquired;
            target.ToRemove = target.Count == 0;
            m_actor.Project.FindSubsystem<SubsystemAudio>()?.PlaySound("Audio/PickableCollected", 0.7f, -0.4f, center, 2f, false);
            return new AtomicActionRun(action, Completed(acquired) with { ReceivedItems = received.AsReadOnly() });
        }

        AtomicActionRun StartDrop(Drop action) {
            IInventory inventory = CarryInventory;
            int slot = action.Item.Slot;
            int value = action.Item.ExpectedValue;
            AtomicActionFailure failure = ValidateInventorySource(inventory, slot, value, action.Count);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }

            Vector3 forward = m_body.Matrix.Forward;
            Vector3 position = m_body.Position + new Vector3(0f, m_body.StanceBoxSize.Y * 0.66f, 0f) + 0.25f * forward;
            Vector3 throwDirection = m_miner.ComponentCreature?.ComponentCreatureModel?.EyeRotation.GetForwardVector() ?? forward;
            SubsystemPickables pickables = WorldPickables;
            int count = Math.Min(action.Count, inventory.GetSlotCount(slot));
            Pickable pickable = pickables.CreatePickable(value, count, position, 8f * throwDirection, null, m_actor);
            if (pickable == null) {
                return Reject(action, AtomicActionFailure.Blocked);
            }
            int removed = inventory.RemoveSlotItems(slot, count);
            if (removed <= 0) {
                return Reject(action, AtomicActionFailure.InsufficientItems);
            }
            pickable.Count = removed;
            if (!PublishPickable(pickables, pickable)) {
                inventory.AddSlotItems(slot, value, removed);
                return Reject(action, AtomicActionFailure.Blocked);
            }
            return Succeed(action, removed);
        }

        AtomicActionRun StartTransfer(TransferItems action) {
            InventorySlot source = action.Source;
            InventorySlot target = action.Destination;
            if (action.Count <= 0 || (source.Inventory == target.Inventory && source.Slot == target.Slot)) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            AtomicActionFailure failure = CheckInventoryAccess(source.Inventory);
            if (failure == AtomicActionFailure.None) {
                failure = CheckInventoryAccess(target.Inventory);
            }
            if (failure == AtomicActionFailure.None) {
                failure = ValidateInventorySource(source.Inventory, source.Slot, source.ExpectedValue, action.Count);
            }
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            if (!IsInventorySlot(target.Inventory, target.Slot)) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (target.Inventory.GetSlotValue(target.Slot) != target.ExpectedValue) {
                return Reject(action, AtomicActionFailure.TargetChanged);
            }
            if (source.Inventory is ComponentCraftingTable table && source.Slot == table.ResultSlotIndex) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            int moved = TransferStack(source.Inventory, source.Slot, target.Inventory, target.Slot, source.ExpectedValue, action.Count);
            return moved > 0 ? Succeed(action, moved) : Reject(action, AtomicActionFailure.InventoryFull);
        }

        AtomicActionRun StartEquip(Equip action) {
            IInventory inventory = CarryInventory;
            int slot = action.Item.Slot;
            int value = action.Item.ExpectedValue;
            AtomicActionFailure failure = ValidateInventorySource(inventory, slot, value, 1);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
            ComponentClothing clothing = ActorClothing;
            ClothingData data = block.GetClothingData(value);
            if (!block.CanWear(value) || data == null || !clothing.CanWearClothing(value)
                || (clothing.EnableDressLimit && data.PlayerLevelRequired > (m_actor.FindComponent<ComponentPlayer>()?.PlayerData.Level ?? 1f))) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (inventory.RemoveSlotItems(slot, 1) != 1) {
                return Reject(action, AtomicActionFailure.InsufficientItems);
            }
            int layerCount = clothing.GetClothes(data.Slot).Count;
            clothing.ProcessSlotItems((int)data.Slot, value, 1, 1, out int returnedValue, out int returnedCount);
            if (returnedValue != 0 && returnedCount > 0) {
                inventory.AddSlotItems(slot, returnedValue, returnedCount);
            }
            if (clothing.GetClothes(data.Slot).Count > layerCount && clothing.GetSlotValue((int)data.Slot) == value) {
                return Succeed(action, 1);
            }
            if (returnedCount <= 0) {
                inventory.AddSlotItems(slot, value, 1);
            }
            return Reject(action, AtomicActionFailure.Blocked);
        }

        AtomicActionRun StartUnequip(Unequip action) {
            if (action.Slot == null || !ClothingSlot.ClothingSlotsByInt.TryGetValue((int)action.Slot, out ClothingSlot slot)
                || slot != action.Slot || Terrain.ExtractContents(action.ExpectedValue) == 0) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            ComponentClothing clothing = ActorClothing;
            int slotIndex = (int)slot;
            int value = action.ExpectedValue;
            if (clothing.GetSlotValue(slotIndex) != value) {
                return Reject(action, AtomicActionFailure.TargetChanged);
            }
            IInventory inventory = CarryInventory;
            int targetSlot = FindInsertionSlot(inventory, value);
            if (targetSlot < 0) {
                return Reject(action, AtomicActionFailure.InventoryFull);
            }
            if (clothing.RemoveSlotItems(slotIndex, 1) != 1) {
                return Reject(action, AtomicActionFailure.Blocked);
            }
            if (InsertStack(inventory, targetSlot, value, 1) != 1) {
                clothing.ProcessSlotItems(slotIndex, value, 1, 1, out _, out _);
                return Reject(action, AtomicActionFailure.Blocked);
            }
            return Succeed(action, 1);
        }

        AtomicActionRun StartCraft(Craft action) {
            if (action.Workspace is not ComponentCraftingTable table || action.Count <= 0
                || Terrain.ExtractContents(action.ExpectedResultValue) == 0) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            AtomicActionFailure failure = CheckInventoryAccess(table);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            return CraftAtTable(action, table);
        }

        AtomicActionRun CraftAtTable(Craft action, ComponentCraftingTable table) {
            int value = action.ExpectedResultValue;
            ComponentPlayer player = m_actor.FindComponent<ComponentPlayer>();
            table.UpdateCraftingResultForActor(player);
            if (table.m_matchedRecipe is not { ResultCount: > 0 } recipe || recipe.ResultValue != value) {
                return Reject(action, AtomicActionFailure.TargetChanged);
            }
            if (action.Count < recipe.ResultCount) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }

            IInventory inventory = CarryInventory;
            int made = 0;
            bool spaceForBatch = false;
            for (int slot = 0; slot < inventory.SlotsCount && action.Count - made >= recipe.ResultCount; slot++) {
                int capacity = GetInsertCapacity(inventory, slot, value);
                if (capacity < recipe.ResultCount) {
                    continue;
                }
                spaceForBatch = true;
                int count = Math.Min(action.Count - made, capacity);
                int removed = table.RemoveCraftingResultForActor(count, player);
                if (removed <= 0) {
                    break;
                }
                int inserted = InsertStack(inventory, slot, value, removed);
                if (inserted < removed) {
                    WorldPickables.AddPickable(value, removed - inserted, m_body.Position, Vector3.Zero, null, m_actor);
                }
                made += removed;
                if (table.m_resetWhenSlotItemsRemoved) {
                    table.UpdateCraftingResultForActor(player);
                    if (table.GetSlotValue(table.ResultSlotIndex) != value) {
                        break;
                    }
                }
            }
            return made > 0
                ? Succeed(action, made)
                : Reject(action, spaceForBatch ? AtomicActionFailure.PreconditionsNotMet : AtomicActionFailure.InventoryFull);
        }

        AtomicActionFailure CheckInventoryAccess(IInventory inventory) {
            if (inventory == null) {
                return AtomicActionFailure.InvalidArgument;
            }
            if (inventory is not Component component || !IsLiveEntity(component.Entity)) {
                return AtomicActionFailure.TargetChanged;
            }
            if (component.Entity == m_actor) {
                return inventory == CarryInventory || inventory is ComponentCraftingTable
                    ? AtomicActionFailure.None : AtomicActionFailure.PreconditionsNotMet;
            }
            if (component.Entity.FindComponent<ComponentBlockEntity>() is not { } block) {
                return AtomicActionFailure.PreconditionsNotMet;
            }
            Vector3 point = block.Position + new Vector3(0.5f);
            if (!IsNear(point, 5f)) {
                return AtomicActionFailure.OutOfReach;
            }
            Vector3 eye = m_miner.ComponentCreature.ComponentCreatureModel.EyePosition;
            object hit = m_miner.Raycast(new Ray3(eye, point - eye), RaycastMode.Interaction, true, false, true);
            if (!MovingBlock.IsNullOrStopped(block.MovingBlock)) {
                return hit is MovingBlocksRaycastResult moving && moving.MovingBlock == block.MovingBlock
                    ? AtomicActionFailure.None : AtomicActionFailure.Blocked;
            }
            return hit is TerrainRaycastResult terrain && terrain.CellFace.Point == block.Coordinates
                ? AtomicActionFailure.None : AtomicActionFailure.Blocked;
        }

        static AtomicActionFailure ValidateInventorySource(IInventory inventory, int slot, int value, int count) {
            if (count <= 0 || Terrain.ExtractContents(value) == 0 || !IsInventorySlot(inventory, slot)) {
                return AtomicActionFailure.InvalidArgument;
            }
            if (inventory.GetSlotCount(slot) <= 0) {
                return AtomicActionFailure.InsufficientItems;
            }
            return inventory.GetSlotValue(slot) == value ? AtomicActionFailure.None : AtomicActionFailure.TargetChanged;
        }

        static bool IsInventorySlot(IInventory inventory, int slot) => inventory != null && slot >= 0 && slot < inventory.SlotsCount
            && (inventory is not (ComponentInventory or ComponentCreativeInventory)
                || slot >= ComponentInventory.ShortInventorySlotsCount || slot < inventory.VisibleSlotsCount);

        static int FindInsertionSlot(IInventory inventory, int value) {
            int emptySlot = -1;
            for (int slot = 0; slot < inventory.SlotsCount; slot++) {
                if (GetInsertCapacity(inventory, slot, value) <= 0) {
                    continue;
                }
                if (inventory.GetSlotCount(slot) > 0) {
                    return slot;
                }
                if (emptySlot < 0) {
                    emptySlot = slot;
                }
            }
            return emptySlot;
        }

        static int GetInsertCapacity(IInventory inventory, int slot, int value) {
            if (!IsInventorySlot(inventory, slot) || (inventory is ComponentCreativeInventory creative && slot >= creative.OpenSlotsCount)) {
                return 0;
            }
            int existing = inventory.GetSlotCount(slot);
            return existing <= 0 || inventory.GetSlotValue(slot) == value
                ? Math.Max(0, inventory.GetSlotCapacity(slot, value) - existing) : 0;
        }

        static int InsertStack(IInventory inventory, int slot, int value, int count) {
            int before = inventory.GetSlotCount(slot);
            try {
                inventory.AddSlotItems(slot, value, count);
                if (inventory is ComponentCreativeInventory && inventory.GetSlotValue(slot) == value) {
                    return count;
                }
            }
            catch (Exception e) {
                Log.Error($"Atomic action inventory insertion failed: {e}");
            }
            return inventory.GetSlotValue(slot) == value ? Math.Clamp(inventory.GetSlotCount(slot) - before, 0, count) : 0;
        }

        static int TransferStack(IInventory source, int sourceSlot, IInventory destination, int destinationSlot, int value, int count) {
            if (source == destination && sourceSlot == destinationSlot
                || ValidateInventorySource(source, sourceSlot, value, count) != AtomicActionFailure.None) {
                return 0;
            }
            int possible = Math.Min(source.GetSlotCount(sourceSlot), Math.Min(count, GetInsertCapacity(destination, destinationSlot, value)));
            if (possible <= 0) {
                return 0;
            }
            int removed = source.RemoveSlotItems(sourceSlot, possible);
            if (removed <= 0) {
                return 0;
            }
            int inserted = InsertStack(destination, destinationSlot, value, removed);
            if (inserted < removed) {
                source.AddSlotItems(sourceSlot, value, removed - inserted);
            }
            return inserted;
        }

        static bool PublishPickable(SubsystemPickables pickables, Pickable pickable) {
            try {
                return pickables.AddPickable(pickable) != null;
            }
            catch (Exception e) {
                Log.Error($"Atomic action item drop failed: {e}");
                return pickables.Pickables.Contains(pickable);
            }
        }
    }
}
