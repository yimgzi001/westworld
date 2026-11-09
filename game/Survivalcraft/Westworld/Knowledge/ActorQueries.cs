using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed record SelfView(Vector3 Position, Vector3 Facing, float Health, float? Food, float? Stamina,
        float? Sleep, float? Temperature, float? Wetness, float? Level, bool IsSleeping);

    public sealed record InventorySlotView(int Slot, int Value, int Count);
    public sealed record InventoryView(IReadOnlyList<InventorySlotView> Slots, int? SelectedSlot);
    public sealed record EquippedItemView(string Slot, int Layer, int Value);

    public sealed record CraftingPreview(RecipeGuideEntry Recipe, int MaterialBatches, string Message, int ResultSlot, int RemainsSlot);
    public sealed record FurnaceView(float Heat, float Progress, float FuelSecondsRemaining, int FuelSlot, int ResultSlot, int RemainsSlot);
    public sealed record StorageView(InventoryView Inventory, CraftingPreview Crafting = null, FurnaceView Furnace = null);

    public enum FocusKind { Block, MovingBlocks, Entity }
    public sealed record FocusView(FocusKind Kind, float Distance, Vector3 Point, string Name,
        Point3? Cell = null, int? Face = null, int? BlockValue = null, int? EntityId = null);

    public sealed record NearbyEntityView(int EntityId, string Name, Vector3 Position, float Distance);

    public sealed class NearbyPickableView {
        internal Pickable Target { get; }

        public int Value { get; }
        public int Count { get; }
        public Vector3 Position { get; }
        public float Distance { get; }
        internal bool InReach { get; }

        internal NearbyPickableView(Pickable target, float distance, bool inReach) {
            Target = target;
            Value = target.Value;
            Count = target.Count;
            Position = target.Position;
            Distance = distance;
            InReach = inReach;
        }

        public PickUp CreatePickup(int count) => new(Target, count);
    }

    public sealed record NearbyView(IReadOnlyList<NearbyEntityView> Entities, IReadOnlyList<NearbyPickableView> Pickables);

    public sealed record SignView(IReadOnlyList<string> Lines, IReadOnlyList<Color> Colors, string Url);
    public sealed record EnvironmentView(double GameTime, float? TimeOfDay, int Light, float Immersion,
        float? Temperature, PrecipitationType? Precipitation, float PrecipitationIntensity);

    public sealed partial class AtomicActions {
        // The game thread reads snapshots. A trusted resolver supplies authorized world targets.
        public sealed partial class ActorQueries {
            readonly AtomicActions m_port;

            internal ActorQueries(AtomicActions port) => m_port = port;

            internal bool IsBoundTo(Entity actor) => ReferenceEquals(actor, m_port.m_actor);

            internal double GameTime => m_port.m_time.GameTime;

            internal IInventory OwnInventory => m_port.IsActorAvailable ? m_port.CarryInventory : null;

            internal IInventory OwnCrafting => m_port.IsActorAvailable ? m_port.m_actor.FindComponent<ComponentCraftingTable>() : null;

            public SelfView ReadSelf() {
                if (!m_port.IsActorAvailable || m_port.m_body == null) {
                    return null;
                }
                ComponentBody body = m_port.m_body;
                ComponentVitalStats stats = m_port.m_actor.FindComponent<ComponentVitalStats>();
                ComponentPlayer player = m_port.m_actor.FindComponent<ComponentPlayer>();
                return new SelfView(body.Position, body.Matrix.Forward, m_port.m_health.Health,
                    stats?.Food ?? m_port.ActorNutrition?.Food, stats?.Stamina, stats?.Sleep, stats?.Temperature, stats?.Wetness,
                    player?.PlayerData.Level, m_port.m_sleep?.IsSleeping == true);
            }

            public InventoryView ReadInventory() => m_port.IsActorAvailable && m_port.CarryInventory is IInventory inventory
                ? CopyInventory(inventory, includeSelectedSlot: true) : null;

            public IReadOnlyList<EquippedItemView> ReadEquipment() {
                if (!m_port.IsActorAvailable || m_port.ActorClothing is not ComponentClothing clothing) {
                    return [];
                }
                List<EquippedItemView> items = [];
                foreach (ClothingSlot slot in ClothingSlot.ClothingSlotsByInt.Values.OrderBy(slot => slot.StableId)) {
                    var layers = clothing.GetClothes(slot);
                    for (int index = 0; index < layers.Count; index++) {
                        items.Add(new EquippedItemView(slot.Name, index, layers[index]));
                    }
                }
                return items.ToArray();
            }

            public StorageView ReadStorage(IInventory inventory) {
                if (!m_port.IsActorAvailable || !m_port.CanTargetWorld() || m_port.m_sleep?.IsSleeping == true
                    || m_port.CheckInventoryAccess(inventory) != AtomicActionFailure.None) {
                    return null;
                }
                InventoryView snapshot = CopyInventory(inventory, includeSelectedSlot: inventory == m_port.CarryInventory);
                if (inventory is ComponentCraftingTable table) {
                    float level = m_port.m_actor.FindComponent<ComponentPlayer>()?.PlayerData.Level ?? 1f;
                    (CraftingRecipe recipe, int batches) = table.ReadCraftingRecipe(level);
                    RecipeGuideEntry preview = recipe is { ResultValue: not 0 } ? GameKnowledge.SnapshotRecipe(recipe) : null;
                    return new StorageView(snapshot, new CraftingPreview(preview, batches, recipe?.Message,
                        table.ResultSlotIndex, table.RemainsSlotIndex));
                }
                if (inventory is ComponentFurnace furnace) {
                    return new StorageView(snapshot, Furnace: new FurnaceView(furnace.HeatLevel, furnace.SmeltingProgress,
                        furnace.FireTimeRemaining, furnace.FuelSlotIndex, furnace.ResultSlotIndex, furnace.RemainsSlotIndex));
                }
                return new StorageView(snapshot);
            }

            public FocusView LookAhead(float maxDistance = 12f) => LookAhead(out _, maxDistance);

            internal FocusView LookAhead(out ActionTarget target, float maxDistance = 12f) {
                target = null;
                if (!m_port.IsActorAvailable || !m_port.CanTargetWorld() || m_port.m_sleep?.IsSleeping == true) {
                    return null;
                }
                if (!float.IsFinite(maxDistance) || maxDistance <= 0f || maxDistance > 16f) {
                    throw new ArgumentOutOfRangeException(nameof(maxDistance));
                }
                float reach = Math.Min(maxDistance, SettingsManager.VisibilityRange);
                ComponentCreatureModel model = m_port.WorldEyes;
                Ray3 ray = new(model.EyePosition, model.EyeRotation.GetForwardVector());
                object hit = m_port.m_miner.Raycast(ray, RaycastMode.Digging, Reach: reach);
                switch (hit) {
                    case TerrainRaycastResult block when !m_port.IsWorldRayOccluded(ray, block.Distance, block.CellFace.Point):
                        BlockTarget blockTarget = new(block.CellFace.Point, Terrain.ReplaceLight(block.Value, 0), block.CellFace.Face);
                        target = blockTarget;
                        return new FocusView(FocusKind.Block, block.Distance, block.HitPoint(),
                            WorldBlock(block.Value).GetDisplayName(m_port.WorldTerrain, block.Value), blockTarget.Cell,
                            blockTarget.Face, blockTarget.ExpectedValue);
                    case BodyRaycastResult body when m_port.IsLiveEntity(body.ComponentBody.Entity)
                        && !m_port.IsWorldRayOccluded(ray, ray.Intersection(body.ComponentBody.BoundingBox) ?? body.Distance):
                        Entity entity = body.ComponentBody.Entity;
                        target = new EntityTarget(entity);
                        return new FocusView(FocusKind.Entity, body.Distance, body.HitPoint(),
                            entity.FindComponent<ComponentCreature>()?.DisplayName, EntityId: entity.Id);
                    case MovingBlocksRaycastResult moving when moving.Distance <= reach && !m_port.IsWorldRayOccluded(ray, moving.Distance):
                        if (moving.MovingBlock is MovingBlock movedBlock && !MovingBlock.IsNullOrStopped(movedBlock)) {
                            target = new MovingBlockTarget(moving.MovingBlockSet, movedBlock.Offset, Terrain.ReplaceLight(movedBlock.Value, 0));
                        }
                        return new FocusView(FocusKind.MovingBlocks, moving.Distance, moving.HitPoint(), null);
                    default:
                        return null;
                }
            }

            internal IInventory FindVisibleStorage(ActionTarget focus) {
                SubsystemBlockEntities blocks = m_port.m_actor.Project.FindSubsystem<SubsystemBlockEntities>();
                if (blocks == null) {
                    return null;
                }
                ComponentBlockEntity blockEntity = focus switch {
                    BlockTarget block => blocks.GetBlockEntity(block.Cell),
                    MovingBlockTarget moving when moving.Set.Blocks.FirstOrDefault(block => block.Offset == moving.Offset)
                        is MovingBlock movingBlock => blocks.GetBlockEntity(movingBlock),
                    _ => null
                };
                IInventory inventory = blockEntity?.Entity.FindComponent<IInventory>();
                return inventory != null && m_port.CheckInventoryAccess(inventory) == AtomicActionFailure.None ? inventory : null;
            }

            public NearbyView FindVisibleObjects(float maxDistance = 8f, int limit = 24) =>
                FindVisibleObjects(out _, maxDistance, limit);

            internal NearbyView FindVisibleObjects(out Entity[] visibleEntities, float maxDistance = 8f, int limit = 24) {
                visibleEntities = [];
                if (!m_port.IsActorAvailable || !m_port.CanTargetWorld() || m_port.m_sleep?.IsSleeping == true) {
                    return new NearbyView([], []);
                }
                if (!float.IsFinite(maxDistance) || maxDistance <= 0f || maxDistance > 16f) {
                    throw new ArgumentOutOfRangeException(nameof(maxDistance));
                }
                ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);

                ComponentCreatureModel model = m_port.WorldEyes;
                Vector3 eye = model.EyePosition;
                Vector3 forward = model.EyeRotation.GetForwardVector();
                float reach = Math.Min(maxDistance, SettingsManager.VisibilityRange);
                bool IsVisible(Vector3 point, float distance, bool withinReach = false) => distance < 0.0001f
                    || ((withinReach || Vector3.Dot(forward, point - eye) >= 0.5f * distance)
                        && !m_port.IsWorldRayOccluded(new Ray3(eye, (point - eye) / distance), distance));

                DynamicArray<ComponentBody> bodies = [];
                m_port.m_actor.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(eye.XZ, reach, bodies);
                (NearbyEntityView View, Entity Entity)[] entities = bodies
                    .Where(body => body.Entity != m_port.m_actor && !body.IsRaycastTransparent && m_port.IsLiveEntity(body.Entity))
                    .Select(body => (Body: body, Point: body.BoundingBox.Center()))
                    .Select(item => (item.Body, item.Point, Distance: Vector3.Distance(eye, item.Point)))
                    .Where(item => item.Distance <= reach)
                    .OrderBy(item => item.Distance)
                    .Where(item => IsVisible(item.Point, item.Distance))
                    .DistinctBy(item => item.Body.Entity)
                    .Take(limit)
                    .Select(item => (new NearbyEntityView(item.Body.Entity.Id,
                        item.Body.Entity.FindComponent<ComponentCreature>()?.DisplayName, item.Body.Position, item.Distance), item.Body.Entity))
                    .ToArray();
                bool canFeelNearbyFood = m_port.ActorNutrition != null;
                Vector3 pickupCenter = m_port.m_body.Position + new Vector3(0f, m_port.m_body.StanceBoxSize.Y * .5f, 0f);
                NearbyPickableView[] pickables = m_port.WorldPickables.Pickables
                    .Where(pickable => !pickable.ToRemove && pickable.Count > 0)
                    .Select(pickable => (Pickable: pickable, Distance: Vector3.Distance(eye, pickable.Position)))
                    .Where(item => item.Distance <= reach)
                    .OrderBy(item => item.Distance)
                    .Select(item => (item.Pickable, item.Distance, InReach: canFeelNearbyFood
                        && Vector3.DistanceSquared(pickupCenter, item.Pickable.Position)
                            <= item.Pickable.DistanceToPick * item.Pickable.DistanceToPick))
                    .Where(item => IsVisible(item.Pickable.Position, item.Distance, item.InReach))
                    .Take(limit)
                    .Select(item => new NearbyPickableView(item.Pickable, item.Distance, item.InReach))
                    .ToArray();
                visibleEntities = entities.Select(item => item.Entity).ToArray();
                return new NearbyView(entities.Select(item => item.View).ToArray(), pickables);
            }

            public SignView ReadSign(BlockTarget target) {
                if (!m_port.IsActorAvailable || !m_port.CanTargetWorld() || m_port.m_sleep?.IsSleeping == true || target == null
                    || m_port.FindWorldRay(target, RaycastMode.Interaction, m_port.InteractionReach, out _, out object hit)
                        != AtomicActionFailure.None
                    || hit is not TerrainRaycastResult block || WorldBlock(block.Value) is not SignBlock) {
                    return null;
                }
                SubsystemSignBlockBehavior signs = m_port.m_actor.Project.FindSubsystem<SubsystemSignBlockBehavior>();
                if (signs == null) {
                    return null;
                }
                SignData sign = signs.GetSignData(target.Cell) ?? new SignData();
                return new SignView(sign.Lines, sign.Colors, sign.Url);
            }

            public EnvironmentView ReadEnvironment() {
                if (!m_port.IsActorAvailable || m_port.m_body == null) {
                    return null;
                }
                var project = m_port.m_actor.Project;
                Vector3 position = m_port.WorldEyes?.EyePosition ?? m_port.m_body.Position;
                Point3 cell = Terrain.ToCell(position);
                SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
                SubsystemWeather weather = project.FindSubsystem<SubsystemWeather>();
                PrecipitationShaftInfo precipitation = weather?.GetPrecipitationShaftInfo(cell.X, cell.Z) ?? default;
                bool exposed = precipitation.Intensity > 0f && position.Y >= precipitation.YLimit;
                return new EnvironmentView(
                    m_port.m_time.GameTime, project.FindSubsystem<SubsystemTimeOfDay>()?.TimeOfDay,
                    terrain.Terrain.GetCellLight(cell), m_port.m_body.ImmersionFactor,
                    m_port.m_actor.FindComponent<ComponentVitalStats>()?.EnvironmentTemperature,
                    exposed ? precipitation.Type : null, exposed ? precipitation.Intensity : 0f
                );
            }

            static InventoryView CopyInventory(IInventory inventory, bool includeSelectedSlot) {
                int slotCount = inventory is ComponentCreativeInventory creative ? creative.OpenSlotsCount : inventory.SlotsCount;
                List<InventorySlotView> slots = [];
                for (int slot = 0; slot < slotCount; slot++) {
                    if (IsInventorySlot(inventory, slot)) {
                        int count = inventory.GetSlotCount(slot);
                        slots.Add(new InventorySlotView(slot, count > 0 ? inventory.GetSlotValue(slot) : 0, count));
                    }
                }
                return new InventoryView(slots.ToArray(), includeSelectedSlot ? inventory.ActiveSlotIndex : null);
            }
        }
    }
}
