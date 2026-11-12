using System.Text.Json;
using Engine;

namespace Game.Westworld {
    public sealed partial class ActorCognition {
        internal sealed record CycleVisualEvent(string Id, int Sequence, double At, PerceivedContact[] Contacts, bool Salient, bool Considered);

        internal void SampleSituation() {
            if (!UsesDeliberativeCycle || HasCognitionProtocolFault || !m_tools.CognitiveWorldIsCurrent || !CanThink(m_tools.CognitiveBody)) return;
            CollectInbox();
            ObserveCycle(m_tools.CognitiveBody);
            if (Now < m_cycle.NextVisualPollAt) return;
            m_cycle.NextVisualPollAt = Now + .75;
            var visible = m_tools.CognitiveVisibleChange();
            if (m_cycle.VisibleFingerprint != visible.Fingerprint) {
                m_cycle.VisualSerial++;
                if (m_cycle.VisualEvents.Count == 16) {
                    int remove = m_cycle.VisualEvents.FindIndex(item => item.Considered);
                    if (remove < 0) remove = m_cycle.VisualEvents.FindIndex(item => !item.Salient);
                    if (remove < 0) remove = 0;
                    if (!m_cycle.VisualEvents[remove].Considered) m_cycle.DroppedVisualEvents++;
                    m_cycle.VisualEvents.RemoveAt(remove);
                }
                m_cycle.VisualEvents.Add(new(Guid.NewGuid().ToString("N"), m_cycle.VisualSerial, Now, visible.Contacts,
                    visible.Contacts.Any(contact => contact.VisibleAttackMotion || contact.Distance < 8 && contact.ClosingSpeed > .5f), false));
            }
            m_cycle.VisibleFingerprint = visible.Fingerprint;
        }

        object AssessCurrentMethod(SelfView self) {
            if (m_cycle.Method is not { } method) return null;
            CycleMetrics actual = SampleCycleMetrics(self, method.ItemValue);
            return new {
                ExpectedMetric = method.Metric, ElapsedSeconds = Math.Max(0, Now - method.StartedAt),
                ProgressSinceWindow = CycleProgress(method.Metric, method.Baseline, actual),
                LastCompletedProgress = m_cycle.Episodes.LastOrDefault(episode => episode.Method == method.Signature
                    && episode.Kind == "taskClosed" && episode.At >= method.StartedAt)?.Progress,
                HealthChangeSinceWindow = actual.Health - method.Baseline.Health,
                AirChangeSinceWindow = actual.Air - method.Baseline.Air,
                InjuryLoss = m_cycle.Injuries.Where(injury => injury.At >= method.StartedAt).Sum(injury => injury.Loss)
            };
        }

        object[] SpatialOptions(JsonElement observation, SelfView self) {
            if (!observation.TryGetProperty("navigation", out JsonElement navigation) || navigation.ValueKind != JsonValueKind.Array) return [];
            Vector3 facing = Vector3.Normalize(new Vector3(self.Facing.X, 0, self.Facing.Z));
            return navigation.EnumerateArray().Select(cue => {
                float bearing = cue.GetProperty("bearing").GetSingle(), distance = cue.GetProperty("distance").GetSingle();
                Vector3 candidate = self.Position + distance * Vector3.Transform(facing,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathUtils.DegToRad(-bearing)));
                string place = FormattableString.Invariant($"{(int)MathF.Floor(candidate.X / 2)}:{(int)MathF.Floor(candidate.Y / 2)}:{(int)MathF.Floor(candidate.Z / 2)}");
                CycleInjury[] recent = m_cycle.Injuries.Where(injury => injury.At >= Now - 120).ToArray();
                float Distance(CycleInjury injury) => Vector3.Distance(new(injury.X, injury.Y, injury.Z), candidate);
                return (object)new {
                    Bearing = bearing, Distance = distance, Passage = cue.GetProperty("passage").GetString(),
                    Candidate = new { candidate.X, candidate.Y, candidate.Z }, Visited = m_cycle.VisitedPlaces.Contains(place),
                    NearestRecentInjuryDistance = recent.Length == 0 ? (float?)null : recent.Min(Distance),
                    InjuryLossExperiencedWithinTwoBlocks = recent.Where(injury => Distance(injury) <= 2).Sum(injury => injury.Loss)
                };
            }).ToArray();
        }
    }
}
