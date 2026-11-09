using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed record ObservedFocus(string Reference, FocusKind Kind, Vector3 Position, float Distance,
        string Name, int? Face, int? BlockValue, string StorageReference);
    public sealed record ObservedEntity(string Reference, string Name, Vector3 Position, float Distance);
    public sealed record ObservedPickable(string Reference, int Value, int Count, Vector3 Position, float Distance) {
        internal bool InReach { get; init; }
        public string Sense { get; init; } = "vision";
    }
    public sealed record ActorObservation(double GameTime, SelfView Self, ObservedFocus Focus,
        IReadOnlyList<ObservedEntity> Entities, IReadOnlyList<ObservedPickable> Pickables, string InventoryReference, string CraftingReference) {
        public IReadOnlyList<ObservedBlock> VisibleBlocks { get; init; } = Array.Empty<ObservedBlock>();
        public ObservationInventorySummary InventorySummary { get; init; }
        public InventoryView Inventory { get; init; }
        public Guid SnapshotId { get; init; } = Guid.NewGuid();
        public ObservationCoverage Coverage { get; init; }
    }
    public sealed record ObservedBlock(string Reference, Point3 Cell, Vector3 Position, int Value, string Name, float Distance, int? Face);
    public sealed record ObservationItemCount(int Value, int Count);
    public sealed record ObservationInventorySummary(IReadOnlyList<InventorySlotView> NonEmptySlots, IReadOnlyList<int> EmptySlotIds,
        int? SelectedSlot, IReadOnlyList<ObservationItemCount> ItemCounts);
    public sealed record ObservationCoverage(float BlockRadius, int BlockLimit, int VisibleBlockTotal, int ReturnedBlocks, int OmittedBlocks,
        bool ScanComplete, bool BudgetExhausted, VoxelScanStatistics ScanStatistics, int ObjectLimit,
        bool EntitiesTruncated, bool PickablesTruncated, float FocusRange, float ObjectRange, VisibilityPoint? Eye, VisibilityPoint? Forward, string Note);
    public sealed record RememberedPlace(string Reference, Vector3 Position, double LastSeenGameTime, string Name);

    public sealed record RememberedMapPage(IReadOnlyList<RememberedPlace> Entries, int Offset, int Total, bool HasMore, int? NextOffset, double SnapshotGameTime, string Note);

    // This memory and its reference table belong to one actor and are used on the game update thread.
    public sealed class ActorWorldMemory {
        const int MaxRememberedPlaces = 128;

        sealed record LiveReference(ActionTarget Target = null, Pickable Pickable = null, int? PickableValue = null,
            MovingBlock MovingBlock = null, IInventory Storage = null, bool SurfaceObserved = false) {
            public bool Equals(LiveReference other) => other != null && SameTarget(Target, other.Target)
                && ReferenceEquals(Pickable, other.Pickable) && PickableValue == other.PickableValue
                && ReferenceEquals(MovingBlock, other.MovingBlock) && ReferenceEquals(Storage, other.Storage) && SurfaceObserved == other.SurfaceObserved;

            public override int GetHashCode() => HashCode.Combine(
                Target is EntityTarget entity ? RuntimeHelpers.GetHashCode(entity.Entity) : Target?.GetHashCode() ?? 0,
                RuntimeHelpers.GetHashCode(Pickable), PickableValue, RuntimeHelpers.GetHashCode(MovingBlock), RuntimeHelpers.GetHashCode(Storage), SurfaceObserved);
        }
        sealed record PlaceMemory((Point3 Cell, bool Landmark) Key, RememberedPlace Place);

        readonly AtomicActions.ActorQueries m_queries;
        readonly string m_inventoryReference = NewReference();
        readonly string m_craftingReference = NewReference();
        readonly Dictionary<(Point3 Cell, bool Landmark), LinkedListNode<PlaceMemory>> m_places = [];
        readonly LinkedList<PlaceMemory> m_recentPlaces = new();
        Dictionary<LiveReference, string> m_liveByIdentity = [];
        Dictionary<string, LiveReference> m_liveByReference = [];
        float m_lookDistance = 12f;
        float m_objectDistance = 8f;
        int m_objectLimit = 24;

        public ActorWorldMemory(Entity actor, AtomicActions actions) {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(actions);
            if (!actions.Queries.IsBoundTo(actor)) {
                throw new ArgumentException("The action port belongs to a different actor.", nameof(actions));
            }
            m_queries = actions.Queries;
        }

        public ActorObservation Capture(float lookDistance = 12f, float objectDistance = 8f, int objectLimit = 24, int blockLimit = 32) {
            if (blockLimit is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(blockLimit));
            SelfView self = m_queries.ReadSelf();
            FocusView focus = m_queries.LookAhead(out ActionTarget focusTarget, lookDistance);
            NearbyView nearby = m_queries.FindVisibleObjects(out Entity[] nearbyEntities, objectDistance, objectLimit);
            var terrainScan = m_queries.ReadVisibleTerrain();
            var inventory = m_queries.ReadInventory();
            var inventorySummary = SummarizeInventory(inventory);
            double now = m_queries.GameTime;
            Dictionary<LiveReference, string> live = [];

            string RememberLive(LiveReference item) {
                if (!live.TryGetValue(item, out string reference)) {
                    reference = m_liveByIdentity.GetValueOrDefault(item) ?? NewReference();
                    live.Add(item, reference);
                }
                return reference;
            }

            ObservedFocus observedFocus = null;
            if (focus != null) {
                MovingBlock movedBlock = FindMovingBlock(focusTarget);
                string reference = focusTarget == null ? null : RememberLive(new LiveReference(focusTarget, MovingBlock: movedBlock));
                IInventory storage = focusTarget is BlockTarget or MovingBlockTarget ? m_queries.FindVisibleStorage(focusTarget) : null;
                string storageReference = storage == null ? null :
                    RememberLive(new LiveReference(focusTarget, MovingBlock: movedBlock, Storage: storage));
                observedFocus = new ObservedFocus(reference, focus.Kind, focus.Point, focus.Distance, focus.Name, focus.Face,
                    (focusTarget as MovingBlockTarget)?.ExpectedValue ?? focus.BlockValue, storageReference);
            }

            ObservedEntity[] entities = new ObservedEntity[nearbyEntities.Length];
            for (int i = 0; i < nearbyEntities.Length; i++) {
                NearbyEntityView visible = nearby.Entities[i];
                string reference = RememberLive(new LiveReference(new EntityTarget(nearbyEntities[i])));
                entities[i] = new ObservedEntity(reference, visible.Name, visible.Position, visible.Distance);
            }
            ObservedPickable[] pickables = nearby.Pickables.Select(item => new ObservedPickable(
                RememberLive(new LiveReference(Pickable: item.Target, PickableValue: item.Value)),
                item.Value, item.Count, item.Position, item.Distance) { InReach = item.InReach, Sense = item.Sense }).ToArray();

            var knownBlocks = terrainScan == null ? Array.Empty<ScannedVoxel>() : terrainScan.Cells
                .Where(c => c.Value.HasValue && Terrain.ExtractContents(c.Value.Value) != 0 && c.VisibleSample != null)
                .OrderBy(c => Vector3.DistanceSquared(self.Position, new Vector3(c.X + .5f, c.Y + .5f, c.Z + .5f)))
                .ThenBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z).ToArray();
            var observedBlocks = knownBlocks.Take(blockLimit).Select(c => {
                int value = Terrain.ReplaceLight(c.Value.Value, 0);
                var sample = c.VisibleSample.Value;
                var point = new Vector3((float)sample.X, (float)sample.Y, (float)sample.Z);
                var target = new BlockTarget(new Point3(c.X,c.Y,c.Z), value);
                string reference = RememberLive(new LiveReference(target, SurfaceObserved: true));
                return new ObservedBlock(reference, target.Cell, point, value, m_queries.VisibleBlockName(value),
                    self == null ? 0 : Vector3.Distance(self.Position,point), null);
            }).ToArray();
            if (self != null) {
                RememberPlace(Terrain.ToCell(self.Position), landmark: false, self.Position, now, null);
            }
            if (focusTarget is BlockTarget landmark) {
                RememberPlace(landmark.Cell, landmark: true, focus.Point, now, focus.Name);
            }
            m_liveByIdentity = live;
            m_liveByReference = live.ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);
            m_lookDistance = lookDistance;
            m_objectDistance = objectDistance;
            m_objectLimit = objectLimit;
            return new ActorObservation(now, self, observedFocus, entities, pickables,
                m_queries.OwnInventory == null ? null : m_inventoryReference,
                m_queries.OwnCrafting == null ? null : m_craftingReference) {
                VisibleBlocks = Array.AsReadOnly(observedBlocks), InventorySummary = inventorySummary, Inventory = inventory,
                Coverage = new(5, blockLimit, knownBlocks.Length, observedBlocks.Length, knownBlocks.Length-observedBlocks.Length,
                    terrainScan?.Complete ?? false, terrainScan?.BudgetExhausted ?? false, terrainScan?.Statistics,
                    objectLimit, nearby.EntitiesTruncated, nearby.PickablesTruncated, lookDistance, objectDistance, terrainScan?.Eye, terrainScan?.Forward,
                    "VisibleBlockTotal counts only observed non-air cells inside the bounded scan, not hidden world contents. " +
                    "OmittedBlocks is output truncation; incomplete scans may contain additional unknown cells. " +
                    "Entity/pickable truncation signals additional sensed results, not their total count. Pickable sense distinguishes vision and proximity. " +
                    "Position is an actual visible sample; Face may be unknown. Action reach and native prerequisites still apply.")
            };
        }

        public static ObservationInventorySummary SummarizeInventory(InventoryView inventory) {
            if (inventory == null) return null;
            var occupied = inventory.Slots.Where(s => s.Count > 0).ToArray();
            return new(Array.AsReadOnly(occupied), Array.AsReadOnly(inventory.Slots.Where(s => s.Count == 0).Select(s => s.Slot).ToArray()),
                inventory.SelectedSlot, Array.AsReadOnly(occupied.GroupBy(s => s.Value).OrderBy(g => g.Key)
                    .Select(g => new ObservationItemCount(g.Key, checked(g.Sum(s => s.Count)))).ToArray()));
        }

        public IReadOnlyList<RememberedPlace> ReadMap() => m_recentPlaces.Select(item => item.Place).ToArray();
        public RememberedMapPage ReadMap(int offset, int limit) {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            if (offset > MaxRememberedPlaces || limit is < 1 or > 32) throw new ArgumentOutOfRangeException();
            var entries = ReadMap();
            int total = entries.Count;
            if (offset > total) throw new ArgumentOutOfRangeException(nameof(offset));
            var page = entries.Skip(offset).Take(limit).ToArray();
            int next = offset + page.Length;
            return new RememberedMapPage(page, offset, total, next < total, next < total ? next : null,
                m_queries.GameTime, "Historical places only; existence is not current. Live LRU order may change between calls; offset is not a stable snapshot cursor.");
        }


        internal string FindObservedEntityReference(Entity entity) =>
            entity == null ? null : m_liveByIdentity.GetValueOrDefault(new LiveReference(new EntityTarget(entity)));

        internal BlockTarget ResolveBlock(string reference) => ResolveTarget(reference) as BlockTarget;

        internal Entity ResolveEntity(string reference) => (ResolveTarget(reference) as EntityTarget)?.Entity;

        internal ActionTarget ResolveTarget(string reference) {
            LiveReference item = FindLive(reference);
            return item is { Target: { } target, Storage: null } && ResolveLivePosition(reference, item).HasValue ? target : null;
        }

        internal PickUp ResolvePickup(string reference, int count) {
            LiveReference item = FindLive(reference);
            if (count <= 0 || item?.Pickable == null) {
                return null;
            }
            NearbyPickableView pickable = FindVisiblePickable(item);
            if (pickable == null) {
                ForgetLive(reference, item);
            }
            return pickable?.CreatePickup(count);
        }

        internal IInventory ResolveStorage(string reference) {
            if (reference == m_inventoryReference) {
                return m_queries.OwnInventory;
            }
            if (reference == m_craftingReference) {
                return m_queries.OwnCrafting;
            }
            LiveReference item = FindLive(reference);
            return item?.Storage != null && ResolveLivePosition(reference, item).HasValue ? item.Storage : null;
        }

        internal Vector3? ResolvePosition(string reference) =>
            m_recentPlaces.FirstOrDefault(item => item.Place.Reference == reference)?.Place.Position ?? ResolveObservedPosition(reference);

        internal Vector3? ResolveObservedPosition(string reference) => ResolveLivePosition(reference, FindLive(reference));

        LiveReference FindLive(string reference) => reference == null ? null : m_liveByReference.GetValueOrDefault(reference);

        Vector3? ResolveLivePosition(string reference, LiveReference item) {
            if (item == null) {
                return null;
            }
            Vector3? position = ObservePosition(item);
            if (!position.HasValue || (item.Storage != null && !ReferenceEquals(item.Storage, m_queries.FindVisibleStorage(item.Target)))) {
                ForgetLive(reference, item);
                return null;
            }
            return position;
        }

        void ForgetLive(string reference, LiveReference item) {
            m_liveByReference.Remove(reference);
            m_liveByIdentity.Remove(item);
        }

        Vector3? ObservePosition(LiveReference item) {
            if (item.Pickable != null) {
                return FindVisiblePickable(item)?.Position;
            }
            if (item.SurfaceObserved && item.Target is BlockTarget surfaceBlock)
                return m_queries.ReobserveKnownBlock(surfaceBlock, 5, surfaceObserved: true);
            FocusView focus = m_queries.LookAhead(out ActionTarget current, m_lookDistance);
            if (SameTarget(item.Target, current) && ReferenceEquals(item.MovingBlock, FindMovingBlock(current))) {
                return current is EntityTarget seenEntity && seenEntity.Entity.FindComponent<ComponentBody>() is { } body
                    ? body.Position : focus.Point;
            }
            if (item.Target is BlockTarget block) return m_queries.ReobserveKnownBlock(block, m_lookDistance);
            if (item.Target is EntityTarget entity) {
                NearbyView nearby = m_queries.FindVisibleObjects(out Entity[] entities, m_objectDistance, m_objectLimit);
                int index = Array.FindIndex(entities, candidate => ReferenceEquals(candidate, entity.Entity));
                if (index >= 0) {
                    return nearby.Entities[index].Position;
                }
            }
            return null;
        }

        NearbyPickableView FindVisiblePickable(LiveReference item) =>
            m_queries.FindVisibleObjects(m_objectDistance, m_objectLimit).Pickables.FirstOrDefault(pickable =>
                ReferenceEquals(pickable.Target, item.Pickable) && pickable.Value == item.PickableValue);

        static MovingBlock FindMovingBlock(ActionTarget target) => target is MovingBlockTarget moving
            ? moving.Set.Blocks.FirstOrDefault(block => block.Offset == moving.Offset) : null;

        static bool SameTarget(ActionTarget first, ActionTarget second) => first is EntityTarget entity
            ? second is EntityTarget other && ReferenceEquals(entity.Entity, other.Entity)
            : first == second;

        void RememberPlace(Point3 cell, bool landmark, Vector3 position, double now, string name) {
            (Point3 Cell, bool Landmark) key = (cell, landmark);
            string reference;
            if (m_places.TryGetValue(key, out LinkedListNode<PlaceMemory> previous)) {
                reference = previous.Value.Place.Reference;
                m_recentPlaces.Remove(previous);
            }
            else {
                reference = NewReference();
                if (m_places.Count == MaxRememberedPlaces) {
                    m_places.Remove(m_recentPlaces.Last.Value.Key);
                    m_recentPlaces.RemoveLast();
                }
            }
            m_places[key] = m_recentPlaces.AddFirst(new PlaceMemory(key, new RememberedPlace(reference, position, now, name)));
        }

        static string NewReference() => Guid.NewGuid().ToString("N");
    }
}
