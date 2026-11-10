using Westworld.Runtime.Core;

int n = 0;
void Check(bool value, string label) { n++; if (!value) throw new Exception(label); }
void Reject(Action action, string label) {
    bool rejected = false;
    try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, label);
}
var world = Guid.NewGuid(); var session = Guid.NewGuid(); var actor = Guid.NewGuid();
var scope = new PerceptionScope(world, session, actor, 1);
var key = new PerceptionCellKey(1, 63, 0);
CellObservation Known(int value = 2, int x = 1) => new(x, 63, 0, "known", value);
CellObservation Unknown(int x = 1) => new(x, 63, 0, "unknown", null);
ActorObservation O(double time, params CellObservation[] cells) =>
    new(session, actor, Guid.NewGuid(), time, new(.5, 64, .5), 1, .5f, [], cells);
PerceptionFrame F(long seq, ActorObservation o, LocalObservationWindow? memory = null) => PerceptionFrame.FromObservation(scope, seq, o, memory);
PerceptionChange Only(PerceptionChangeJournal journal) => journal.TakePending().Entries.Single();

// Capture validation and detached immutability.
var original = O(1, Known(), Unknown(2)); var frozen = F(1, original);
Check(frozen.Scope == scope && frozen.Sequence == 1 && frozen.ObservationId == original.ObservationId, "exact scope/version");
Check(frozen.CapturedAt == 1 && frozen.Position == original.Position, "same capture clock and body");
Check(frozen.ReadAt(key, 1).Knowledge == PerceptionKnowledge.Current, "known in this frame");
Check(frozen.ReadAt(new(2, 63, 0), 1).Knowledge == PerceptionKnowledge.Unknown, "unknown retained without value");
Check(frozen.ReadAt(new(3, 63, 0), 1).Knowledge == PerceptionKnowledge.Absent, "absent is not air");
Check(frozen.ReadAt(key, 4).Knowledge == PerceptionKnowledge.Current, "TTL inclusive");
var expired = frozen.ReadAt(key, 4.001);
Check(expired.Knowledge == PerceptionKnowledge.Expired && expired.Value == 2 && expired.ObservedAt == 1, "expired not realtime; preserves old provenance");
Check(frozen.ReadAt(key, 1).Knowledge == PerceptionKnowledge.Current, "expiry query never rewrites immutable frame");
original.Cells[0] = Known(99); Check(frozen.Cells[0].Value == 2, "source array detached");
bool immutable = false;
try { ((IList<CellObservation>)frozen.Cells)[0] = Known(99); } catch (NotSupportedException) { immutable = true; }
Check(immutable, "public cells immutable");
immutable = false;
try { ((IDictionary<PerceptionCellKey, PerceptionCellEvidence>)frozen.Evidence).Clear(); } catch (NotSupportedException) { immutable = true; }
Check(immutable, "public evidence immutable");
Reject(() => frozen.ReadAt(key, .9), "query clock regression");
Reject(() => frozen.ReadAt(key, double.NaN), "query invalid clock");
Reject(() => F(0, O(1)), "sequence required");
Reject(() => PerceptionFrame.FromObservation(default, 1, O(1)), "default scope rejected");
foreach (var bad in new[] { scope with { WorldId = Guid.Empty }, scope with { SessionId = Guid.Empty }, scope with { ActorId = Guid.Empty }, scope with { BindingRevision = -1 } })
    Reject(() => PerceptionFrame.FromObservation(bad, 1, O(1)), "scope field required");
Reject(() => F(1, O(1) with { ActorId = Guid.NewGuid() }), "frame actor mismatch");
Reject(() => F(1, O(1) with { SessionId = Guid.NewGuid() }), "frame session mismatch");
Reject(() => F(1, O(1) with { ObservationId = Guid.Empty }), "snapshot id required");
foreach (var time in new[] { -1d, double.NaN, double.PositiveInfinity, 1e13 }) Reject(() => F(1, O(time)), "invalid capture time");
Reject(() => F(1, O(1) with { Health = float.NaN }), "invalid health");
Reject(() => F(1, O(1) with { Position = new(.5, 256, .5) }), "height envelope");
Reject(() => F(1, O(1) with { Position = new(double.NaN, 64, 0) }), "finite position");
Reject(() => F(1, O(1, new CellObservation(1, 63, 0, "unknown", 0))), "unknown cannot smuggle air");
Reject(() => F(1, O(1, new CellObservation(1, 63, 0, "known", null))), "known needs value");
Reject(() => F(1, O(1, Known(-1))), "negative value");
Reject(() => F(1, O(1, new CellObservation(1, 63, 0, "historical", 2))), "history not current input");
Reject(() => F(1, O(1, Known(), Unknown())), "duplicate cells");
Reject(() => F(1, O(1, new CellObservation(0, 256, 0, "known", 0))), "cell height");
Reject(() => F(1, O(1, Known(2, int.MinValue))), "cell coordinate overflow");
Reject(() => F(1, O(1, Enumerable.Range(0, 4097).Select(x => Known(2, x)).ToArray())), "cell capacity");
Reject(() => PerceptionFrame.FromObservation(scope, 1, O(1), evidenceLifetimeSeconds: 4), "bounded TTL");
Reject(() => PerceptionFrame.FromObservation(scope, 1, O(1), valueSemantics: ""), "explicit value semantics");

// Reuse LocalObservationMemory; no second memory or synthetic pose is introduced.
var memory = new LocalObservationMemory(session, actor);
var seen = O(10, Known()); var window = memory.Observe(seen); var seenFrame = F(1, seen, window);
Check(seenFrame.RetainedCells.Single().IsCurrent, "same-window current source");
Reject(() => F(2, seen with { ObservationId = Guid.NewGuid() }, window), "memory wrong snapshot");
Reject(() => F(2, seen with { Time = 11 }, window), "memory wrong time");
Reject(() => F(2, seen with { Position = new(.6, 64, .5) }, window), "memory wrong body pose");
Reject(() => F(2, seen with { Health = .5f }, window), "memory wrong self version");
Reject(() => F(2, seen with { Cells = [Known(3)] }, window), "same id cannot disagree about current values");
Reject(() => F(2, seen with { Cells = [Unknown()] }, window), "same id cannot turn current into unknown while keeping current memory");
var otherActor = Guid.NewGuid(); var otherMem = new LocalObservationMemory(session, otherActor);
var otherWindow = otherMem.Observe(seen with { ActorId = otherActor });
Reject(() => F(2, seen, otherWindow), "memory another actor");
var hidden = O(11, Unknown()); var hiddenWindow = memory.Observe(hidden); var hiddenFrame = F(2, hidden, hiddenWindow);
var historical = hiddenFrame.ReadAt(key, 11);
Check(historical.Knowledge == PerceptionKnowledge.Historical && historical.Value == 2, "occlusion preserves old value only as history");
Check(historical.SourceObservationId == seen.ObservationId && historical.ObservedAt == 10, "unknown does not refresh provenance");
Check(hiddenFrame.Cells.Single().Value is null, "current unknown never acquires remembered value");
Check(hiddenFrame.ReadAt(key, 13.01).Knowledge == PerceptionKnowledge.Expired, "history expiry uses original source time");
Reject(() => PerceptionFrame.FromObservation(scope, 2, hidden, hiddenWindow, .5), "overage retained evidence rejected");
var outside = O(12) with { Position = new(30, 64, 30) }; var outsideFrame = F(3, outside, memory.Observe(outside));
Check(outsideFrame.Evidence.Count == 0 && hiddenFrame.ReadAt(key, 11).Value == 2, "old immutable frame not mutated by memory eviction");

// No missing-list, occlusion or age event asserts destruction or resource pickup.
var journal = new PerceptionChangeJournal(scope);
journal.Observe(seenFrame); var initial = journal.TakePending();
Check(!initial.BaselinePresent && initial.ResyncRequired && initial.Entries.Single().Kinds.Single() == PerceptionChangeKind.FirstObserved, "initial snapshot baseline explicitly absent");
journal.Observe(hiddenFrame); var loss = Only(journal);
Check(loss.Kinds.Single() == PerceptionChangeKind.NotCurrentlyObserved && loss.Current.Knowledge == PerceptionKnowledge.Historical, "occlusion not deletion");
Check(loss.Previous.Value == 2 && loss.Current.SourceObservationId == seen.ObservationId, "loss preserves source");
journal.Observe(outsideFrame); Check(Only(journal).Kinds.Single() == PerceptionChangeKind.KnowledgeEvicted, "radius/capacity loss is memory eviction");
var mem2 = new LocalObservationMemory(session, actor); var j2 = new PerceptionChangeJournal(scope);
var o1 = O(20, Known()); j2.Observe(F(1, o1, mem2.Observe(o1))); j2.TakePending();
var o2 = O(21, Unknown()); j2.Observe(F(2, o2, mem2.Observe(o2))); j2.TakePending();
var o3 = O(23.01, Unknown()); j2.Observe(F(3, o3, mem2.Observe(o3)));
var expiredEvent = Only(j2);
Check(expiredEvent.Kinds.Single() == PerceptionChangeKind.KnowledgeExpired && expiredEvent.Current.Value is null, "TTL expiry not world removal");
var o4 = O(24, Known(0)); j2.Observe(F(4, o4, mem2.Observe(o4)));
Check(Only(j2).Current.Value == 0, "air requires new actual known zero");
var noMemory = new PerceptionChangeJournal(scope);
noMemory.Observe(F(1, O(1, Known()))); noMemory.TakePending();
noMemory.Observe(F(2, O(2))); var omittedEvent = Only(noMemory);
Check(omittedEvent.Kinds.Single() == PerceptionChangeKind.NotCurrentlyObserved && omittedEvent.Current.Knowledge == PerceptionKnowledge.Absent, "omitted list item not deleted");
noMemory.Observe(F(3, O(3, Unknown()))); Check(noMemory.TakePending().Entries.Count == 0, "absent to unknown no invented world fact");
noMemory.Observe(F(4, O(4, Known(5)))); Check(Only(noMemory).Kinds.Single() == PerceptionChangeKind.FirstObserved, "new known fact");
noMemory.Observe(F(5, O(5, Known(5)))); Check(noMemory.TakePending().Entries.Count == 0, "same known value no event storm");
noMemory.Observe(F(6, O(6, Known(0)))); var actualEmpty = Only(noMemory);
Check(actualEmpty.Kinds.Single() == PerceptionChangeKind.ObservedValueChanged && actualEmpty.Previous.Value == 5 && actualEmpty.Current.Value == 0, "actual known empty change retains before/after evidence");
Check(actualEmpty.Current.SourceObservationId == noMemory.Latest!.ObservationId, "new empty evidence belongs to exact snapshot");

// Historical reappearance versus changed observations; mailbox retains intermediate kinds.
var merge = new PerceptionChangeJournal(scope); var mm = new LocalObservationMemory(session, actor);
var m1 = O(30, Known(1)); merge.Observe(F(1, m1, mm.Observe(m1))); merge.TakePending();
var m2 = O(30.2, Known(2)); merge.Observe(F(2, m2, mm.Observe(m2)));
var m3 = O(30.4, Unknown()); merge.Observe(F(3, m3, mm.Observe(m3)));
var m4 = O(30.6, Known(2)); merge.Observe(F(4, m4, mm.Observe(m4)));
var m5 = O(30.8, Known(1)); merge.Observe(F(5, m5, mm.Observe(m5)));
var changes = merge.PeekPending(); var coalesced = changes.Entries.Single();
Check(coalesced.Occurrences == 4 && coalesced.FirstSequence == 2 && coalesced.LastSequence == 5, "same cell transitions coalesced with count/versions");
Check(coalesced.Kinds.SequenceEqual(new[] { PerceptionChangeKind.ObservedValueChanged, PerceptionChangeKind.NotCurrentlyObserved, PerceptionChangeKind.ObservedAgain }), "intermediate occlusion/change not erased");
Check(coalesced.Previous.Value == 1 && coalesced.Current.Value == 1, "same endpoints do not suppress actual intermediate changes");
Check(changes.FromObservationId == m1.ObservationId && changes.ToObservationId == m5.ObservationId, "mailbox baseline and latest version");
merge.TakePending(); Check(merge.PendingCount == 0 && changes.Entries.Single().Occurrences == 4, "taken batches immutable and detached");
var emptyBatch = merge.TakePending(); Check(emptyBatch.BaselinePresent && !emptyBatch.ResyncRequired && emptyBatch.Entries.Count == 0, "empty drain explicit unchanged baseline");

// Bounded merge loss and frame gaps require a new current snapshot, not silent delta-only use.
var bounded = new PerceptionChangeJournal(scope, capacity: 1);
bounded.Observe(F(1, O(1, Known(1, 1), Known(1, 2), Known(1, 3))));
var over = bounded.PeekPending(); Check(bounded.PendingCount == 1 && over.OmittedTransitions == 2 && over.ResyncRequired, "bounded mailbox reports omitted transitions");
bounded.Observe(F(2, O(2, Known(2, 1), Known(2, 2), Known(2, 3))));
over = bounded.TakePending(); Check(over.OmittedTransitions == 4 && over.Entries.Single().Occurrences == 2, "existing subject can merge despite capacity; omissions exact");
bounded.Observe(F(5, O(3, Known(2, 1), Known(2, 2), Known(2, 3))));
var gap = bounded.TakePending(); Check(gap.SkippedFrames == 2 && gap.ResyncRequired && gap.Entries.Count == 0, "forward gaps explicit even with same endpoint facts");
Check(bounded.PeekPending().SkippedFrames == 0, "new batch resets gap counter, not chronology");

// Rejected input cannot partially alter state, baseline, event counters or replay ledger.
var strict = new PerceptionChangeJournal(scope); var valid = F(10, O(10, Known())); strict.Observe(valid);
var before = strict.PeekPending();
foreach (var badScope in new[] { scope with { WorldId = Guid.NewGuid() }, scope with { SessionId = Guid.NewGuid() }, scope with { ActorId = Guid.NewGuid() }, scope with { BindingRevision = 2 } }) {
    var badObservation = O(11, Known(3)) with { SessionId = badScope.SessionId, ActorId = badScope.ActorId };
    Reject(() => strict.Observe(PerceptionFrame.FromObservation(badScope, 11, badObservation)), "cross scope rejected");
}
Reject(() => strict.Observe(F(10, O(11, Known(3)))), "same sequence rejected");
Reject(() => strict.Observe(F(9, O(11, Known(3)))), "reordered sequence rejected");
Reject(() => strict.Observe(F(11, O(9, Known(3)))), "clock regression rejected");
Reject(() => strict.Observe(F(11, O(11, Known(3)) with { ObservationId = valid.ObservationId })), "same snapshot new version rejected");
Reject(() => strict.Observe(PerceptionFrame.FromObservation(scope, 11, O(11), valueSemantics: "different")), "value interpretation must not silently change");
Reject(() => strict.Observe(PerceptionFrame.FromObservation(scope, 11, O(11), evidenceLifetimeSeconds: 2)), "TTL must not silently change");
Check(strict.FrameCount == 1 && strict.Latest == valid && strict.PeekPending().Entries.Single().Occurrences == before.Entries.Single().Occurrences, "all rejections atomic");
strict.Observe(F(11, O(10, Known(3)))); Check(strict.FrameCount == 2, "same clock valid with new ordered snapshot");
Reject(() => strict.Observe(valid), "old snapshot replay rejected after subsequent frame");
Reject(() => Task.Run(() => strict.PeekPending()).GetAwaiter().GetResult(), "journal thread affinity");
var capped = new PerceptionChangeJournal(scope, maximumFrames: 1); capped.Observe(F(1, O(1)));
Reject(() => capped.Observe(F(2, O(2))), "replay ledger bound fails explicitly, never forgets old identities");
Check(capped.FrameCount == 1, "capacity rejection atomic");
Reject(() => new PerceptionChangeJournal(scope, capacity: 0), "mailbox lower bound");
Reject(() => new PerceptionChangeJournal(scope, capacity: 257), "mailbox upper bound");
Reject(() => new PerceptionChangeJournal(scope, maximumFrames: 4097), "snapshot ledger bound");

// Source versions in retained memory cannot be reissued with different data/timestamps.
var provenance = new PerceptionChangeJournal(scope); var source = O(1, Known(2)); provenance.Observe(F(1, source));
var hostileMemory = new LocalObservationMemory(session, actor);
hostileMemory.Observe(source with { Cells = [Known(3)] });
var later = O(2, Unknown()); var conflicting = F(2, later, hostileMemory.Observe(later));
Reject(() => provenance.Observe(conflicting), "same historical source cannot change cell value");
var retimedMemory = new LocalObservationMemory(session, actor); retimedMemory.Observe(source with { Time = 1.5 });
var retimed = F(2, later, retimedMemory.Observe(later));
Reject(() => provenance.Observe(retimed), "same historical snapshot cannot change capture timestamp");
Check(provenance.FrameCount == 1, "provenance rejection atomic");

Console.WriteLine($"PASS {n}/{n} pure perception checks; no native capture, model, GUI, or tool authorization");
