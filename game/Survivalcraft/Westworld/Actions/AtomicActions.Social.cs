using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    public enum AtomicSocialEventKind {
        HeardSpeech,
        ItemOffered,
        OfferDeclined,
        ItemsTransferred
    }

    public sealed record AtomicSocialEvent(AtomicSocialEventKind Kind, double GameTime, Entity Source,
        string Text = null, Guid? OfferId = null, int ItemValue = 0, int Count = 0) {
        internal Vector3? SourcePosition { get; init; }
        internal string SourceName { get; init; }
        internal Guid? SpeechId { get; init; }
        internal bool? SpeechComplete { get; init; }
        internal int? SpeechChunk { get; init; }
    }

    public sealed partial class AtomicActions {
        internal sealed record PendingItemOffer(Guid Id, Entity Sender, Entity Recipient, OwnItemSlot Item, int Count, double ExpiresAt);
        internal sealed record SavedItemOffer(Guid Id, int Sender, int Recipient, int Slot, int Value, int Count, double ExpiresAt);

        internal static SavedItemOffer[] CaptureItemOffers(Project project, double gameTime) {
            if (!m_socialWorlds.TryGetValue(project, out SocialWorld social)) return [];
            social.PruneOffers(gameTime, null);
            return social.Offers.Values.Select(offer => new SavedItemOffer(offer.Id, offer.Sender.Id, offer.Recipient.Id,
                offer.Item.Slot, offer.Item.ExpectedValue, offer.Count, offer.ExpiresAt)).ToArray();
        }

        internal static void RestoreItemOffers(Project project, double gameTime, SavedItemOffer[] saved) {
            if (saved == null) return;
            SocialWorld social = m_socialWorlds.GetValue(project, _ => new SocialWorld());
            if (saved.Length > social.Inboxes.Count || saved.Select(offer => offer?.Id).Distinct().Count() != saved.Length
                || saved.Select(offer => offer?.Sender).Distinct().Count() != saved.Length) {
                throw new InvalidDataException("Invalid saved item offers.");
            }
            List<PendingItemOffer> restored = [];
            foreach (SavedItemOffer offer in saved) {
                if (offer == null || offer.Id == Guid.Empty || offer.Sender == offer.Recipient || offer.Slot < 0
                    || offer.Value <= 0 || offer.Count <= 0 || !double.IsFinite(offer.ExpiresAt)) {
                    throw new InvalidDataException("Invalid saved item offer.");
                }
                Entity sender = project.FindEntity(offer.Sender), recipient = project.FindEntity(offer.Recipient);
                if (sender == null || recipient == null || !social.Inboxes.ContainsKey(sender) || !social.Inboxes.ContainsKey(recipient)) {
                    throw new InvalidDataException("The saved offer references an unavailable participant.");
                }
                if (offer.ExpiresAt > gameTime) restored.Add(new PendingItemOffer(offer.Id, sender, recipient,
                    new OwnItemSlot(offer.Slot, offer.Value), offer.Count, offer.ExpiresAt));
            }
            social.Offers.Clear();
            foreach (PendingItemOffer offer in restored) social.RestoreOffer(offer);
        }

        sealed class SocialWorld {
            public readonly Dictionary<Entity, Queue<AtomicSocialEvent>> Inboxes = [];
            public readonly Dictionary<Guid, PendingItemOffer> Offers = [];
            long m_offerSequence;

            public void RestoreOffer(PendingItemOffer offer) {
                Offers.Add(offer.Id, offer);
                byte[] bytes = offer.Id.ToByteArray();
                if (bytes.Skip(8).All(value => value == 0)) {
                    m_offerSequence = Math.Max(m_offerSequence, BinaryPrimitives.ReadInt64LittleEndian(bytes));
                }
            }

            public Guid NextOfferId() {
                Span<byte> bytes = stackalloc byte[16];
                bytes.Clear();
                BinaryPrimitives.WriteInt64LittleEndian(bytes, ++m_offerSequence);
                return new Guid(bytes);
            }

            public void PruneOffers(double gameTime, Entity actor, bool replaceOutgoing = false) {
                foreach (PendingItemOffer offer in Offers.Values.Where(offer =>
                    offer.ExpiresAt <= gameTime || (replaceOutgoing && offer.Sender == actor)).ToArray()) {
                    Offers.Remove(offer.Id);
                }
            }
        }

        static readonly ConditionalWeakTable<Project, SocialWorld> m_socialWorlds = new();

        const int MaxInboxSize = 64;
        const int SpeechUnitsPerChunk = 12;
        const float SpeechUnitsPerSecond = 12f;
        const float HandoffRange = 2f;
        const double OfferLifetime = 15.0;

        SocialWorld m_social;
        SubsystemTerrain m_socialTerrain;

        void RegisterSocial() {
            m_social = m_socialWorlds.GetValue(m_actor.Project, _ => new SocialWorld());
            m_social.Inboxes.Add(m_actor, new Queue<AtomicSocialEvent>());
            m_socialTerrain = m_actor.Project.FindSubsystem<SubsystemTerrain>(true);
            Register<Speak>(speak => StartSpeak(speak, null), () => m_body != null);
            Register<OfferItem>(StartOfferItem, () => m_body != null && m_miner?.Inventory != null);
            Register<RespondToOffer>(StartRespondToOffer, () => m_body != null && m_miner?.Inventory != null);
        }

        void RemoveSocialParticipant() {
            m_social.Inboxes.Remove(m_actor);
            foreach (PendingItemOffer offer in m_social.Offers.Values.Where(offer =>
                offer.Sender == m_actor || offer.Recipient == m_actor).ToArray()) {
                m_social.Offers.Remove(offer.Id);
            }
        }

        // Trusted observation code drains only the bound actor's received events.
        public IReadOnlyList<AtomicSocialEvent> DrainSocialEvents() {
            if (!m_social.Inboxes.TryGetValue(m_actor, out Queue<AtomicSocialEvent> inbox)) {
                return [];
            }
            AtomicSocialEvent[] events = inbox.ToArray();
            inbox.Clear();
            return events;
        }

        AtomicActionRun StartSpeak(Speak speak, SpeechContinuation saved) {
            if (string.IsNullOrWhiteSpace(speak.Text) || speak.Text.Length > 512 || !Enum.IsDefined(speak.Mode)
                || speak.Recipient == m_actor) {
                return Reject(speak, AtomicActionFailure.InvalidArgument);
            }
            if (speak.Recipient != null && !IsLiveEntity(speak.Recipient)) {
                return Reject(speak, AtomicActionFailure.TargetChanged);
            }

            float radius = speak.Mode switch {
                SpeechMode.Whisper => 3f,
                SpeechMode.Normal => 8f,
                _ => 16f
            };
            int[] starts = StringInfo.ParseCombiningCharacters(speak.Text);
            int chunkNumber = saved?.Chunk ?? 0;
            if (chunkNumber > (starts.Length - 1) / SpeechUnitsPerChunk) {
                return Reject(speak, AtomicActionFailure.InvalidArgument);
            }
            int start = chunkNumber * SpeechUnitsPerChunk;
            int end = Math.Min(starts.Length, start + SpeechUnitsPerChunk);
            int delivered = saved?.Delivered ?? 0;
            Guid speechId = saved?.Id ?? Guid.NewGuid();
#if WINDOWS
            bool speechCompleted = false;
#endif
            double chunkEnd = m_time.GameTime + (saved?.Seconds ?? (end - start) / SpeechUnitsPerSecond);
            HashSet<Entity> listeners = saved == null ? FindListeners(radius)
                : saved.Listeners.Select(m_actor.Project.FindEntity).Where(entity => entity != null).ToHashSet();
            return StartRunning(speak, () => {
                listeners.RemoveWhere(listener => !CanHear(listener, radius));
                if (m_time.GameTime < chunkEnd) {
                    return null;
                }
                int lastChar = end < starts.Length ? starts[end] : speak.Text.Length;
                string chunk = speak.Text[starts[start]..lastChar];
                AtomicSocialEvent heard = new(AtomicSocialEventKind.HeardSpeech, m_time.GameTime, m_actor, Text: chunk) {
                    SourcePosition = m_body.Position,
                    SourceName = m_actor.FindComponent<ComponentCreature>()?.DisplayName,
                    SpeechId = speechId,
                    SpeechChunk = chunkNumber++,
                    SpeechComplete = end == starts.Length
                };
                foreach (Entity listener in listeners) {
                    PublishSocialEvent(listener, heard);
                    delivered++;
                }
#if WINDOWS
                speechCompleted = end == starts.Length;
                WorldPanel.RecordSpeech(m_actor, speechId, chunkNumber - 1, speak.Mode, chunk, listeners,
                    speechCompleted ? "completed" : "speaking");
#endif
                if (end == starts.Length) {
                    return Completed(delivered);
                }
                start = end;
                end = Math.Min(starts.Length, start + SpeechUnitsPerChunk);
                chunkEnd = m_time.GameTime + (end - start) / SpeechUnitsPerSecond;
                listeners = FindListeners(radius);
                return null;
            }
#if WINDOWS
            , () => {
                if (chunkNumber > 0 && !speechCompleted) {
                    WorldPanel.RecordSpeech(m_actor, speechId, chunkNumber, speak.Mode, null, [], "interrupted");
                }
            }
#endif
            , capture: speak.Recipient != null ? null : () => new SpeechContinuation(speak.Text, speak.Mode, speechId,
                chunkNumber, delivered, Math.Max(0, chunkEnd - m_time.GameTime), listeners.Select(listener => listener.Id).ToArray())
            );
        }

        HashSet<Entity> FindListeners(float radius) =>
            m_social.Inboxes.Keys.Where(listener => listener != m_actor && CanHear(listener, radius)).ToHashSet();

        bool CanHear(Entity listener, float radius) =>
            IsLiveEntity(listener) && listener.FindComponent<ComponentHealth>() is { Health: > 0f }
            && listener.FindComponent<ComponentBody>() is ComponentBody body
            && listener.FindComponent<ComponentSleep>() is not { IsSleeping: true }
            && IsInSocialReach(body, radius);

        bool IsInSocialReach(ComponentBody other, float radius) {
            Vector3 from = m_body.Position + 0.8f * m_body.StanceBoxSize.Y * Vector3.UnitY;
            Vector3 to = other.Position + 0.8f * other.StanceBoxSize.Y * Vector3.UnitY;
            float distanceSquared = Vector3.DistanceSquared(from, to);
            return distanceSquared <= radius * radius && (distanceSquared < 0.0001f
                || !m_socialTerrain.Raycast(from, to, false, true,
                    (value, _) => BlocksManager.Blocks[Terrain.ExtractContents(value)].GetIsCollidable(m_body, value)).HasValue);
        }

        AtomicActionRun StartOfferItem(OfferItem action) {
            if (action.Count <= 0 || action.Recipient == null || action.Recipient == m_actor
                || action.Item.Slot < 0 || action.Item.ExpectedValue == 0) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (!TryGetHandoffPartner(action.Recipient, out ComponentBody body, out _)) {
                return Reject(action, AtomicActionFailure.TargetChanged);
            }
            if (!IsInSocialReach(body, HandoffRange)) {
                return Reject(action, AtomicActionFailure.OutOfReach);
            }

            IInventory inventory = m_miner.Inventory;
            if (!HasOfferItems(inventory, action.Item, action.Count)) {
                return Reject(action, AtomicActionFailure.InsufficientItems);
            }

            double now = m_time.GameTime;
            m_social.PruneOffers(now, m_actor);
            PendingItemOffer pending = m_social.Offers.Values.FirstOrDefault(offer => offer.Sender == m_actor
                && offer.Recipient == action.Recipient && offer.Item == action.Item && offer.Count == action.Count);
            if (pending != null) {
                return Succeed(action, offerId: pending.Id);
            }
            m_social.PruneOffers(now, m_actor, replaceOutgoing: true);
            bool producer = m_actor.ValuesDictionary.DatabaseObject.Name == FoundingWorld.ProducerTemplate;
            Guid id = producer ? Guid.NewGuid() : m_social.NextOfferId();
            double lifetime = producer ? 60.0 : OfferLifetime;
            m_social.Offers.Add(id, new PendingItemOffer(id, m_actor, action.Recipient, action.Item, action.Count, now + lifetime));
            PublishSocialEvent(action.Recipient, new AtomicSocialEvent(
                AtomicSocialEventKind.ItemOffered, now, m_actor, OfferId: id, ItemValue: action.Item.ExpectedValue, Count: action.Count));
#if WINDOWS
            WorldPanel.RecordTransfer(m_actor, action.Recipient, id, "offered", action.Item.ExpectedValue, action.Count);
#endif
            return Succeed(action, offerId: id);
        }

        AtomicActionRun StartRespondToOffer(RespondToOffer action) {
            double now = m_time.GameTime;
            m_social.PruneOffers(now, m_actor);
            if (!m_social.Offers.TryGetValue(action.OfferId, out PendingItemOffer offer) || offer.Recipient != m_actor) {
                return Reject(action, AtomicActionFailure.TargetChanged);
            }
            if (!TryGetHandoffPartner(offer.Sender, out ComponentBody body, out IInventory source)) {
                return Reject(action, AtomicActionFailure.TargetChanged);
            }
            if (!IsInSocialReach(body, HandoffRange)) {
                return Reject(action, AtomicActionFailure.OutOfReach);
            }
            if (!action.Accept) {
                m_social.Offers.Remove(offer.Id);
                PublishSocialEvent(offer.Sender, new AtomicSocialEvent(AtomicSocialEventKind.OfferDeclined, now, m_actor, OfferId: offer.Id));
#if WINDOWS
                WorldPanel.RecordTransfer(offer.Sender, m_actor, offer.Id, "declined", offer.Item.ExpectedValue, offer.Count);
#endif
                return Succeed(action);
            }
            if (!HasOfferItems(source, offer.Item, offer.Count)) {
                m_social.Offers.Remove(offer.Id);
                return Reject(action, AtomicActionFailure.TargetChanged);
            }

            IInventory destination = m_miner.Inventory;
            if (source == destination) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            int moved = TransferOfferedItems(offer, source, destination);
            if (moved == 0) {
                return Reject(action, AtomicActionFailure.InventoryFull);
            }
            m_social.Offers.Remove(offer.Id);
            PublishSocialEvent(offer.Sender, new AtomicSocialEvent(
                AtomicSocialEventKind.ItemsTransferred, now, m_actor, OfferId: offer.Id, ItemValue: offer.Item.ExpectedValue, Count: moved));
            PublishSocialEvent(m_actor, new AtomicSocialEvent(
                AtomicSocialEventKind.ItemsTransferred, now, offer.Sender, OfferId: offer.Id, ItemValue: offer.Item.ExpectedValue, Count: moved));
#if WINDOWS
            WorldPanel.RecordTransfer(offer.Sender, m_actor, offer.Id, "transferred", offer.Item.ExpectedValue, moved);
#endif
            return Succeed(action, moved, offer.Id);
        }

        internal bool CanReceiveItemOffers(Entity entity) => TryGetHandoffPartner(entity, out _, out _);

        internal IReadOnlyList<PendingItemOffer> ReadOutgoingItemOffers() {
            m_social.PruneOffers(m_time.GameTime, m_actor);
            return m_social.Offers.Values.Where(offer => offer.Sender == m_actor).ToArray();
        }

        bool TryGetHandoffPartner(Entity entity, out ComponentBody body, out IInventory inventory) {
            body = null;
            inventory = null;
            if (!IsLiveEntity(entity) || !m_social.Inboxes.ContainsKey(entity)
                || entity.FindComponent<ComponentHealth>() is not { Health: > 0f }
                || entity.FindComponent<ComponentSleep>() is { IsSleeping: true }) {
                return false;
            }
            body = entity.FindComponent<ComponentBody>();
            inventory = entity.FindComponent<ComponentMiner>()?.Inventory;
            return body != null && inventory != null;
        }

        static bool HasOfferItems(IInventory inventory, OwnItemSlot item, int count) =>
            item.Slot >= 0 && item.Slot < inventory.SlotsCount && inventory.GetSlotValue(item.Slot) == item.ExpectedValue
            && inventory.GetSlotCount(item.Slot) >= count;

        static int TransferOfferedItems(PendingItemOffer offer, IInventory source, IInventory destination) {
            int moved = 0;
            int value = offer.Item.ExpectedValue;
            // Fill matching stacks first, then use empty slots.
            for (int pass = 0; pass < 2 && moved < offer.Count; pass++) {
                for (int slot = 0; slot < destination.SlotsCount && moved < offer.Count; slot++) {
                    bool empty = destination.GetSlotCount(slot) == 0;
                    if (empty != (pass == 1) || (!empty && destination.GetSlotValue(slot) != value)) {
                        continue;
                    }
                    moved += TransferStack(source, offer.Item.Slot, destination, slot, value, offer.Count - moved);
                }
            }
            return moved;
        }

        void PublishSocialEvent(Entity recipient, AtomicSocialEvent item) {
            if (!m_social.Inboxes.TryGetValue(recipient, out Queue<AtomicSocialEvent> inbox)) {
                return;
            }
            if (inbox.Count >= MaxInboxSize) {
                inbox.Dequeue();
            }
            inbox.Enqueue(item);
        }
    }
}
