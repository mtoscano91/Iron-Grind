using System.Collections.Generic;
using System.Linq;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 006 — the CR-NET-7.7 priority-path (R-OD)
    /// per-client, per-tick message queue (<see cref="PriorityPathQueue{T}"/>). Covers AC-NC-34
    /// (12-message cap overflow across two flushes), AC-NC-35 (enhancement-path exemption
    /// displacement of the oldest non-exempt message), and AC-CCR-05 (9-message overflow, the
    /// same underlying mechanism as AC-NC-34 at a smaller scale, exercised as its own named
    /// blocking AC).
    /// </summary>
    /// <remarks>
    /// <b>PathCapacity_effective-vs-AC-NC-35 resolution (recorded here alongside the source-level
    /// note on <see cref="PriorityPathQueue{T}"/>):</b> the story's Implementation Notes formula
    /// <c>PathCapacity_effective = PRIORITY_PATH_CAP + ExemptMessages_queued</c> reads, literally,
    /// as an additive capacity increase (8 non-exempt + 1 exempt = 9 flushed, nothing displaced).
    /// AC-NC-35 (BLOCKING) — repeated independently in the QA Test Cases section — instead
    /// describes displacement: at 8-cap capacity, adding 1 exempt message still yields exactly 8
    /// total flushed (the exempt one plus 7 of the original 8, oldest bumped to the next tick).
    /// These tests assert AC-NC-35's explicit displacement behavior, not the literal
    /// <c>PathCapacity_effective</c> formula, which is judged to describe the separate,
    /// unmodeled-by-this-story bulk-transfer exemption instead.
    /// </remarks>
    [TestFixture]
    internal sealed class WireProtocol_PriorityPathCap_Tests
    {
        // -----------------------------------------------------------------------
        // AC-NC-34: 12 non-exempt messages queued (4 above PRIORITY_PATH_CAP=8) —
        // exactly 8 emitted at tick T in enqueue order, remaining 4 at tick T+1 in
        // the same relative order, all 12 delivered across the two flushes with
        // none dropped and none duplicated.
        // -----------------------------------------------------------------------

        [Test]
        public void Flush_TwelveNonExemptMessagesQueued_EmitsFirstEightThenRemainingFourAcrossTwoFlushes()
        {
            // Arrange
            var queue = new PriorityPathQueue<string>();
            var enqueued = new List<string>();
            for (int i = 1; i <= 12; i++)
            {
                string payload = $"Msg#{i}";
                enqueued.Add(payload);
                queue.Enqueue(payload, isExempt: false);
            }

            // Act
            IReadOnlyList<QueuedMessage<string>> tickN = queue.Flush(tickNumber: 100u);
            IReadOnlyList<QueuedMessage<string>> tickN1 = queue.Flush(tickNumber: 101u);

            // Assert — exactly 8 emitted at tick T, in original enqueue order.
            Assert.AreEqual(PriorityPathQueue<string>.PRIORITY_PATH_CAP, tickN.Count, "Tick T must emit exactly PRIORITY_PATH_CAP messages.");
            CollectionAssert.AreEqual(
                enqueued.Take(8).ToList(),
                tickN.Select(m => m.Payload).ToList(),
                "Tick T must emit the first 8 messages in original enqueue order.");

            // Assert — remaining 4 emitted at tick T+1, same relative order.
            Assert.AreEqual(4, tickN1.Count, "Tick T+1 must emit the remaining 4 deferred messages.");
            CollectionAssert.AreEqual(
                enqueued.Skip(8).ToList(),
                tickN1.Select(m => m.Payload).ToList(),
                "Tick T+1 must emit the deferred 4 messages in original relative order.");

            // Assert — all 12 delivered across both flushes, none dropped, none duplicated.
            var delivered = tickN.Select(m => m.Payload).Concat(tickN1.Select(m => m.Payload)).ToList();
            CollectionAssert.AreEqual(enqueued, delivered, "All 12 messages must be delivered across exactly two flushes, none dropped or duplicated.");
        }

        // -----------------------------------------------------------------------
        // AC-NC-35: queue already at 8 queued non-exempt R-OD messages for the
        // current (not-yet-flushed) tick; an EnhancementAttemptResult-style
        // exempt message is enqueued before that tick's flush. It is placed at
        // position 1 of the current tick's capture, displacing the oldest
        // non-exempt message to the next tick; the displaced message appears at
        // position 1 of the following tick's capture.
        // -----------------------------------------------------------------------

        [Test]
        public void Flush_ExemptMessageEnqueuedWhileAtCap_TakesFrontSlotAndDisplacesOldestNonExemptToNextTick()
        {
            // Arrange — 8 non-exempt messages already queued for the current tick (at cap).
            var queue = new PriorityPathQueue<string>();
            for (int i = 1; i <= 8; i++)
            {
                queue.Enqueue($"NonExempt#{i}", isExempt: false);
            }

            // Act — an exempt (enhancement-path) message is enqueued before the tick's flush.
            queue.Enqueue("EnhancementOutcome", isExempt: true);
            IReadOnlyList<QueuedMessage<string>> tickN = queue.Flush(tickNumber: 200u);
            IReadOnlyList<QueuedMessage<string>> tickN1 = queue.Flush(tickNumber: 201u);

            // Assert — tick T emits exactly 8: the exempt message at position 1, then NonExempt#2..#8.
            Assert.AreEqual(PriorityPathQueue<string>.PRIORITY_PATH_CAP, tickN.Count, "Tick T must still emit exactly PRIORITY_PATH_CAP messages, not 9.");
            Assert.AreEqual("EnhancementOutcome", tickN[0].Payload, "The exempt message must occupy position 1 of the current tick's capture.");
            Assert.IsTrue(tickN[0].IsExempt, "Position 1 of the current tick's capture must be flagged exempt.");

            var expectedKept = Enumerable.Range(2, 7).Select(i => $"NonExempt#{i}").ToList();
            CollectionAssert.AreEqual(
                expectedKept,
                tickN.Skip(1).Select(m => m.Payload).ToList(),
                "Positions 2-8 must be NonExempt#2..NonExempt#8 — the newest 7 of the original 8 — in original relative order.");

            // Assert — tick T+1 emits the displaced (oldest, first-enqueued) non-exempt message at position 1.
            Assert.AreEqual(1, tickN1.Count, "Tick T+1 must emit exactly the one displaced message.");
            Assert.AreEqual("NonExempt#1", tickN1[0].Payload, "The very first-enqueued non-exempt message must be the one displaced to tick T+1, position 1.");
            Assert.IsFalse(tickN1[0].IsExempt, "The displaced message must retain its non-exempt flag.");
        }

        // -----------------------------------------------------------------------
        // AC-CCR-05: 9 non-exempt R-OD messages queued for the same tick — exactly
        // 8 delivered that tick, the 9th sent at the start of the next tick's
        // flush, no message dropped. Same underlying mechanism as AC-NC-34, at a
        // smaller scale, exercised as its own explicit test per its own blocking AC.
        // -----------------------------------------------------------------------

        [Test]
        public void Flush_NineNonExemptMessagesQueued_EmitsEightThenNinthAtNextTickWithNoneDropped()
        {
            // Arrange
            var queue = new PriorityPathQueue<string>();
            var enqueued = new List<string>();
            for (int i = 1; i <= 9; i++)
            {
                string payload = $"Msg#{i}";
                enqueued.Add(payload);
                queue.Enqueue(payload, isExempt: false);
            }

            // Act
            IReadOnlyList<QueuedMessage<string>> tickN = queue.Flush(tickNumber: 300u);
            IReadOnlyList<QueuedMessage<string>> tickN1 = queue.Flush(tickNumber: 301u);

            // Assert — exactly 8 delivered at tick T.
            Assert.AreEqual(8, tickN.Count, "Tick T must deliver exactly 8 messages.");
            CollectionAssert.AreEqual(enqueued.Take(8).ToList(), tickN.Select(m => m.Payload).ToList(), "Tick T must deliver the first 8 messages in enqueue order.");

            // Assert — the 9th message is delivered at the start of tick T+1's flush.
            Assert.AreEqual(1, tickN1.Count, "Tick T+1 must deliver exactly the 1 remaining message.");
            Assert.AreEqual("Msg#9", tickN1[0].Payload, "The 9th message must be delivered at position 1 of tick T+1's flush.");

            // Assert — no message dropped across the two flushes.
            var delivered = tickN.Select(m => m.Payload).Concat(tickN1.Select(m => m.Payload)).ToList();
            CollectionAssert.AreEqual(enqueued, delivered, "All 9 messages must be delivered across exactly two flushes, none dropped.");
        }

        // -----------------------------------------------------------------------
        // Supporting coverage (not independently required by a named AC, but
        // pins behavior the three blocking ACs above rely on).
        // -----------------------------------------------------------------------

        [Test]
        public void Flush_NothingEnqueued_ReturnsEmptyNotNull()
        {
            // Arrange
            var queue = new PriorityPathQueue<string>();

            // Act
            IReadOnlyList<QueuedMessage<string>> result = queue.Flush(tickNumber: 1u);

            // Assert
            Assert.IsNotNull(result, "Flush must never return null, even with nothing queued.");
            Assert.AreEqual(0, result.Count, "Flush with nothing enqueued must return an empty list.");
        }

        [Test]
        public void Flush_ExemptCountExceedsCap_ReturnsAllExemptUncappedAndNoNonExempt()
        {
            // Arrange — 10 exempt messages in one tick, exceeding PRIORITY_PATH_CAP (8). Exempt
            // admission is uncapped by design (upstream business logic, not this queue, is
            // responsible for rate-limiting enhancement-outcome production) — this test locks in
            // that documented behavior rather than leaving it as an untested emergent property.
            var queue = new PriorityPathQueue<string>();
            var enqueuedExempt = new List<string>();
            for (int i = 1; i <= 10; i++)
            {
                string payload = $"Exempt#{i}";
                enqueuedExempt.Add(payload);
                queue.Enqueue(payload, isExempt: true);
            }
            queue.Enqueue("NonExempt#1", isExempt: false);

            // Act
            IReadOnlyList<QueuedMessage<string>> tickN = queue.Flush(tickNumber: 400u);

            // Assert — all 10 exempt messages returned uncapped (exceeding PRIORITY_PATH_CAP),
            // in enqueue order; the non-exempt message is fully evicted (0 non-exempt slots left).
            Assert.AreEqual(10, tickN.Count, "All pending exempt messages must be returned even though this exceeds PRIORITY_PATH_CAP.");
            CollectionAssert.AreEqual(enqueuedExempt, tickN.Select(m => m.Payload).ToList(), "Exempt messages must be returned in enqueue order.");
            Assert.IsTrue(tickN.All(m => m.IsExempt), "All returned messages in this scenario must be exempt — the sole non-exempt message had no available slot.");

            // Assert — the evicted non-exempt message is deferred, not dropped.
            IReadOnlyList<QueuedMessage<string>> tickN1 = queue.Flush(tickNumber: 401u);
            Assert.AreEqual(1, tickN1.Count);
            Assert.AreEqual("NonExempt#1", tickN1[0].Payload, "The fully-evicted non-exempt message must still be delivered at the next tick, not dropped.");
        }

        [Test]
        public void PRIORITY_PATH_CAP_IsEightPerCrNet77()
        {
            // Assert — pins the tuning-knob value so a future edit cannot silently drift it
            // without this test failing.
            Assert.AreEqual(8, PriorityPathQueue<string>.PRIORITY_PATH_CAP, "PRIORITY_PATH_CAP must be 8 per CR-NET-7.7.");
        }

        // -----------------------------------------------------------------------
        // Networking Core Story 039: the allocation-free Flush(uint, List) overload
        // must return the same messages in the same order as Flush(uint).
        // -----------------------------------------------------------------------

        private static List<string> Describe(IEnumerable<QueuedMessage<string>> messages)
        {
            return messages.Select(m => m.Payload + (m.IsExempt ? "!" : string.Empty)).ToList();
        }

        private static List<string> OverloadFlush(PriorityPathQueue<string> queue, uint tick)
        {
            var destination = new List<QueuedMessage<string>>();
            queue.Flush(tick, destination);
            return Describe(destination);
        }

        [Test]
        public void FlushOverload_TwelveNonExemptOverTwoTicks_ReturnsSameMessagesAsFlushUint()
        {
            var reference = new PriorityPathQueue<string>();
            var overload = new PriorityPathQueue<string>();
            for (int i = 1; i <= 12; i++)
            {
                reference.Enqueue($"Msg#{i}", isExempt: false);
                overload.Enqueue($"Msg#{i}", isExempt: false);
            }

            CollectionAssert.AreEqual(Describe(reference.Flush(100u)), OverloadFlush(overload, 100u));
            CollectionAssert.AreEqual(Describe(reference.Flush(101u)), OverloadFlush(overload, 101u));
        }

        [Test]
        public void FlushOverload_EightNonExemptPlusOneExempt_ReturnsSameMessagesAsFlushUint()
        {
            var reference = new PriorityPathQueue<string>();
            var overload = new PriorityPathQueue<string>();
            for (int i = 1; i <= 8; i++)
            {
                reference.Enqueue($"NonExempt#{i}", isExempt: false);
                overload.Enqueue($"NonExempt#{i}", isExempt: false);
            }
            reference.Enqueue("Exempt", isExempt: true);
            overload.Enqueue("Exempt", isExempt: true);

            List<string> expectedFirst = Describe(reference.Flush(200u));
            List<string> actualFirst = OverloadFlush(overload, 200u);

            CollectionAssert.AreEqual(expectedFirst, actualFirst);
            Assert.AreEqual("Exempt!", actualFirst[0]);
            Assert.AreEqual(8, actualFirst.Count);
            CollectionAssert.AreEqual(Describe(reference.Flush(201u)), OverloadFlush(overload, 201u));
        }

        [Test]
        public void FlushOverload_ThreeTickSequenceWithExemptAndBacklog_ReturnsSameMessagesAsFlushUint()
        {
            var reference = new PriorityPathQueue<string>();
            var overload = new PriorityPathQueue<string>();
            void Both(string payload, bool exempt)
            {
                reference.Enqueue(payload, exempt);
                overload.Enqueue(payload, exempt);
            }

            for (int i = 1; i <= 10; i++) { Both($"T1#{i}", false); }
            Both("T1Exempt#1", true);
            Both("T1Exempt#2", true);
            CollectionAssert.AreEqual(Describe(reference.Flush(1u)), OverloadFlush(overload, 1u));

            for (int i = 1; i <= 3; i++) { Both($"T2#{i}", false); }
            Both("T2Exempt", true);
            CollectionAssert.AreEqual(Describe(reference.Flush(2u)), OverloadFlush(overload, 2u));

            CollectionAssert.AreEqual(Describe(reference.Flush(3u)), OverloadFlush(overload, 3u));
            CollectionAssert.AreEqual(Describe(reference.Flush(4u)), OverloadFlush(overload, 4u));
        }

        [Test]
        public void FlushOverload_DestinationHoldsOldContent_IsClearedFirst()
        {
            var queue = new PriorityPathQueue<string>();
            queue.Enqueue("Msg#1", isExempt: false);
            var destination = new List<QueuedMessage<string>> { new QueuedMessage<string>("Stale", false) };

            queue.Flush(1u, destination);

            CollectionAssert.AreEqual(new[] { "Msg#1" }, Describe(destination));
        }

        [Test]
        public void FlushOverload_NullDestination_Throws()
        {
            var queue = new PriorityPathQueue<string>();

            Assert.Throws<System.ArgumentNullException>(() => queue.Flush(1u, null));
        }

        [Test]
        public void CountAndClear_ReportPendingPlusDeferredAndDiscardAll()
        {
            var queue = new PriorityPathQueue<string>();
            for (int i = 1; i <= 10; i++)
            {
                queue.Enqueue($"Msg#{i}", isExempt: false);
            }
            queue.Enqueue("Exempt", isExempt: true);
            Assert.AreEqual(11, queue.Count);

            queue.Flush(1u, new List<QueuedMessage<string>>());
            Assert.AreEqual(3, queue.Count, "Eight sent (the exempt one and seven others); three deferred.");

            queue.Clear();
            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(0, OverloadFlush(queue, 2u).Count);
        }

        [Test]
        public void FlushOverload_ThousandFlushesAfterReserve_AllocateNothing()
        {
            var queue = new PriorityPathQueue<int>();
            queue.Reserve(16);
            var destination = new List<QueuedMessage<int>>(16);
            for (int i = 0; i < 20; i++)
            {
                queue.Enqueue(i, isExempt: i % 2 == 0);
                queue.Enqueue(i, isExempt: false);
                queue.Flush((uint)i, destination);
            }

            Assert.That(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    queue.Enqueue(i, isExempt: true);
                    queue.Enqueue(i, isExempt: false);
                    queue.Flush((uint)i, destination);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.AreEqual(2, destination.Count);
        }

        [Test]
        public void FlushOverload_MixedWithFlushUintOnOneInstanceOverFourTicks_MatchesAnInstanceDrivenByFlushUintOnly()
        {
            var reference = new PriorityPathQueue<string>();
            var mixed = new PriorityPathQueue<string>();
            void Both(string payload, bool exempt)
            {
                reference.Enqueue(payload, exempt);
                mixed.Enqueue(payload, exempt);
            }

            // Tick 1 (Flush(uint) on the mixed instance): a backlog of 12 non-exempt and an exempt message.
            for (int i = 1; i <= 12; i++) { Both($"T1#{i}", false); }
            Both("T1Exempt", true);
            List<string> expected1 = Describe(reference.Flush(1u));
            CollectionAssert.AreEqual(expected1, Describe(mixed.Flush(1u)));

            // Tick 2 (overload): more messages and another exempt one on top of the deferred backlog.
            for (int i = 1; i <= 4; i++) { Both($"T2#{i}", false); }
            Both("T2Exempt", true);
            List<string> expected2 = Describe(reference.Flush(2u));
            CollectionAssert.AreEqual(expected2, OverloadFlush(mixed, 2u));

            // Tick 3 (Flush(uint) again, after the overload swapped the working lists).
            for (int i = 1; i <= 2; i++) { Both($"T3#{i}", false); }
            List<string> expected3 = Describe(reference.Flush(3u));
            CollectionAssert.AreEqual(expected3, Describe(mixed.Flush(3u)));

            // Tick 4 (overload): the rest of the backlog drains.
            List<string> expected4 = Describe(reference.Flush(4u));
            CollectionAssert.AreEqual(expected4, OverloadFlush(mixed, 4u));

            Assert.AreEqual(reference.Count, mixed.Count);
            Assert.AreEqual(8, expected1.Count, "Precondition: the first tick sent 8, with the exempt message first.");
            Assert.AreEqual("T1Exempt!", expected1[0]);
        }
    }
}
