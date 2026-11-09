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
        internal Guid? SpeechId { get; init; }
        internal bool? SpeechComplete { get; init; }
        internal int? SpeechChunk { get; init; }
    }

    public sealed partial class AtomicActions {
        sealed record PendingItemOffer(Guid Id, Entity Sender, Entity Recipient, OwnItemSlot Item, int Count, double ExpiresAt);

        sealed class SocialWorld {
            public readonly Dictionary<Entity, Queue<AtomicSocialEvent>> Inboxes = [];
            public readonly Dictionary<Guid, PendingItemOffer> Offers = [];
            long m_offerSequence;

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
            Register<Speak>(StartSpeak, () => m_body != null);
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

        AtomicActionRun StartSpeak(Speak speak) {
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
            int start = 0;
            int end = Math.Min(starts.Length, SpeechUnitsPerChunk);
            int delivered = 0;
            Guid speechId = Guid.NewGuid();
            int chunkNumber = 0;
            double chunkEnd = m_time.GameTime + end / SpeechUnitsPerSecond;
            HashSet<Entity> listeners = FindListeners(radius);
            return StartRunning(speak, () => {
                listeners.RemoveWhere(listener => !CanHear(listener, radius));
                if (m_time.GameTime < chunkEnd) {
                    return null;
                }
                int lastChar = end < starts.Length ? starts[end] : speak.Text.Length;
                string chunk = speak.Text[starts[start]..lastChar];
                AtomicSocialEvent heard = new(AtomicSocialEventKind.HeardSpeech, m_time.GameTime, m_actor, Text: chunk) {
                    SpeechId = speechId,
                    SpeechChunk = chunkNumber++,
                    SpeechComplete = end == starts.Length
                };
                foreach (Entity listener in listeners) {
                    PublishSocialEvent(listener, heard);
                    delivered++;
                }
                if (end == starts.Length) {
                    return Completed(delivered);
                }
                start = end;
                end = Math.Min(starts.Length, start + SpeechUnitsPerChunk);
                chunkEnd = m_time.GameTime + (end - start) / SpeechUnitsPerSecond;
                listeners = FindListeners(radius);
                return null;
            });
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
            m_social.PruneOffers(now, m_actor, replaceOutgoing: true);
            Guid id = m_social.NextOfferId();
            m_social.Offers.Add(id, new PendingItemOffer(id, m_actor, action.Recipient, action.Item, action.Count, now + OfferLifetime));
            PublishSocialEvent(action.Recipient, new AtomicSocialEvent(
                AtomicSocialEventKind.ItemOffered, now, m_actor, OfferId: id, ItemValue: action.Item.ExpectedValue, Count: action.Count));
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
            return Succeed(action, moved, offer.Id);
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
