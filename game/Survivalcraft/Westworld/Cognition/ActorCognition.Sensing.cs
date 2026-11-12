using Engine;

namespace Game.Westworld {
    public sealed partial class ActorCognition {
        internal sealed record CycleInjury(string Id, double At, float Loss, float X, float Y, float Z, float? DirectionX, float? DirectionZ);
        internal sealed record CycleActivity(double LastProgressAt, int UnproductiveAttempts, int LastReviewedAttempts,
            CycleMetrics Baseline, int NewPlaces) {
            public float Health { get; init; }
            public float Air { get; init; }
            public Dictionary<int, int> Items { get; init; } = [];
        }

        void RecordCycleInjury(Injury injury) {
            if (!(injury.Amount > 0) || m_cycle == null) return;
            SelfView self = m_tools.CognitiveBody;
            if (self == null) return;
            Vector2? direction = null;
            if (injury.Attackment is { } impact && float.IsFinite(impact.HitDirection.X + impact.HitDirection.Z)) {
                Vector2 offset = -impact.HitDirection.XZ;
                if (offset.LengthSquared() > .0001f) direction = Vector2.Normalize(offset);
            }
            if (m_cycle.Injuries.Count == 16) m_cycle.Injuries.RemoveAt(0);
            m_cycle.Injuries.Add(new(Guid.NewGuid().ToString("N"), Now, Math.Min(self.Health, injury.Amount),
                self.Position.X, self.Position.Y, self.Position.Z, direction?.X, direction?.Y));
            m_cycle.SituationSerial++;
#if WINDOWS
            WorldPanel.RecordInjury(m_tools.PanelActor, injury);
#endif
        }

        void ObserveActivity(SelfView self, bool completedAttempt) {
            string place = FormattableString.Invariant($"{(int)MathF.Floor(self.Position.X / 2)}:{(int)MathF.Floor(self.Position.Y / 2)}:{(int)MathF.Floor(self.Position.Z / 2)}");
            bool newPlace = !m_cycle.VisitedPlaces.Contains(place) && m_cycle.VisitedPlaces.Count < 2048;
            if (newPlace) m_cycle.VisitedPlaces.Add(place);
            CycleMetrics actual = SampleCycleMetrics(self);
            float air = m_tools.CognitiveSomatic?.Air ?? 1;
            Dictionary<int, int> items = (m_tools.CognitiveInventory()?.Slots ?? []).Where(slot => slot.Count > 0)
                .GroupBy(slot => slot.Value).ToDictionary(group => group.Key, group => group.Sum(slot => slot.Count));
            CycleActivity previous = m_cycle.Activity;
            if (previous == null) {
                m_cycle.Activity = new(Now, 0, 0, actual, m_cycle.VisitedPlaces.Count) { Air = air, Health = self.Health, Items = items };
                return;
            }
            bool progress = newPlace || completedAttempt && previous.LastProgressAt > (m_cycle.Method?.StartedAt ?? Now) + .001
                || actual.Inventory > previous.Baseline.Inventory || actual.Food > previous.Baseline.Food + .01
                || actual.Interactions > previous.Baseline.Interactions || air > previous.Air + .05f || self.Health > previous.Health + .01f
                || items.Any(item => item.Value > previous.Items.GetValueOrDefault(item.Key));
            m_cycle.Activity = previous with {
                LastProgressAt = progress ? Now : previous.LastProgressAt,
                UnproductiveAttempts = progress ? 0 : previous.UnproductiveAttempts + (completedAttempt ? 1 : 0),
                LastReviewedAttempts = progress ? 0 : previous.LastReviewedAttempts,
                Baseline = actual, NewPlaces = m_cycle.VisitedPlaces.Count, Items = items,
                Air = progress ? air : Math.Min(air, previous.Air), Health = progress ? self.Health : Math.Min(self.Health, previous.Health)
            };
        }
    }
}
