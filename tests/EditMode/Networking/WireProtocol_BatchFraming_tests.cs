using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 007 — R-U/U-U batch framing
    /// (<see cref="RUBatchWriter"/>, <see cref="CycleBroadcastPacketWriter"/>,
    /// <see cref="PositionPacketWriter"/>), the four concrete sub-message schemas/codecs
    /// (<see cref="DamageEvent"/>, <see cref="GoldSyncEvent"/>, <see cref="CycleTimerBroadcast"/>,
    /// <see cref="EntityPositionUpdate"/> via <see cref="BatchSubMessageCodec"/>), and buffer
    /// pooling (<see cref="ClientBufferSet"/>, <see cref="ZoneBufferPool"/>). Covers AC-NC-19
    /// (GoldSyncEvent absolute-balance invariant), AC-NC-21 (50-client/200-tick per-client byte
    /// budget), AC-NC-33 (Scenario C 100-tick per-packet 512-byte cap), and AC-BUF-1 (buffer pool
    /// exhaustion rejection + critical anomaly).
    /// </summary>
    [TestFixture]
    internal sealed class WireProtocol_BatchFraming_Tests
    {
        private const int ClientCount = 50; // n=50, matches ZoneBufferPool.MAX_PLAYERS_PER_ZONE default.
        private const int DamageEventsPerTickZoneWide = 10; // Scenario C peak-combat density.

        private static EntityID Eid(uint value) => new EntityID(value);

        // ===========================================================================================
        // AC-NC-19: three sequential gold mutations produce three GoldSyncEvent sub-messages
        // carrying absolute balances (100g, 50g, 250g), never deltas. Each is captured both by
        // round-tripping through the codec and by the OnServerGoldSyncBatched observer hook.
        // ===========================================================================================

        [Test]
        public void GoldSyncEvent_ThreeSequentialMutations_RUBatchCarriesAbsoluteBalancesInOrderAndFiresObserverHook()
        {
            // Arrange — three sequential mutations on the same character: add 100g, spend 50g (net 50g), add 200g (net 250g).
            var mutations = new[]
            {
                new GoldSyncEvent(new CharacterID(7), newBalance: 100u, version: 1u, GoldTransactionReason.MonsterDrop),
                new GoldSyncEvent(new CharacterID(7), newBalance: 50u, version: 2u, GoldTransactionReason.ScrollPurchase),
                new GoldSyncEvent(new CharacterID(7), newBalance: 250u, version: 3u, GoldTransactionReason.MonsterDrop),
            };
            var observer = new NetworkTestObserver();
            byte[] buffer = new byte[ClientBufferSet.BufferSize];
            var decodedBalances = new List<uint>();

            // Act — one R-U batch per mutation (one tick each), as three sequential ticks.
            for (int i = 0; i < mutations.Length; i++)
            {
                int bytesWritten = RUBatchWriter.Write(
                    buffer, sequenceNumber: (uint)(i + 1), tickNumber: (uint)(i + 1),
                    damageEvents: Array.Empty<DamageEvent>(),
                    goldSyncEvents: new[] { mutations[i] },
                    otherSubMessages: Array.Empty<PendingSubMessage>(),
                    clientIdForLogging: 1u, observer: observer);

                // Assert — round-trips unchanged through the batch header + sub-message codec.
                Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out ServerMessageEnvelope envelope, out ushort subMessageCount));
                Assert.AreEqual(1, subMessageCount, "Each tick's batch must carry exactly one GoldSyncEvent sub-message.");
                Assert.IsTrue(BatchSubMessageCodec.TryReadGoldSyncEvent(buffer.AsSpan(BatchHeaderCodec.HeaderSize), out GoldSyncEvent decoded, out int subBytesRead));
                Assert.AreEqual(mutations[i], decoded, "Decoded GoldSyncEvent must equal the original — round-trip must be unchanged.");
                Assert.AreEqual(bytesWritten, BatchHeaderCodec.HeaderSize + subBytesRead, "Total bytes written must equal header + sub-message bytes.");

                decodedBalances.Add(decoded.NewBalance);
            }

            // Assert — absolute balances, never deltas.
            CollectionAssert.AreEqual(new uint[] { 100u, 50u, 250u }, decodedBalances, "NewBalance must be the absolute post-mutation balance at each step, not a delta.");

            // Assert — the observer hook fires once per GoldSyncEvent actually placed into the batch, in order, with the same absolute balances.
            Assert.AreEqual(3, observer.ServerGoldSyncBatchedCalls.Count);
            Assert.AreEqual((7u, 100u, 1u), observer.ServerGoldSyncBatchedCalls[0]);
            Assert.AreEqual((7u, 50u, 2u), observer.ServerGoldSyncBatchedCalls[1]);
            Assert.AreEqual((7u, 250u, 3u), observer.ServerGoldSyncBatchedCalls[2]);
        }

        // ===========================================================================================
        // Shared deterministic load fixture for AC-NC-21 / AC-NC-33: a 50-client zone in active
        // combat, 10 DamageEvent sub-messages/tick zone-wide (round-robin distributed across
        // clients), plus a full CycleBroadcast packet (49 entries) and a full Position packet (49
        // entries) per client per tick. Plain nested for-loops — no wall-clock dependency, no
        // random seeds.
        // ===========================================================================================

        private static (long[] perClientTotalBytes, int maxSinglePacketBytes) RunLoadFixture(int tickCount)
        {
            var perClientTotalBytes = new long[ClientCount];
            int maxSinglePacketBytes = 0;
            byte[] scratch = new byte[ClientBufferSet.BufferSize];

            // 49 "other zone entities" reused every tick/client — content values are irrelevant to
            // the byte-budget assertions this fixture proves; only sizes and counts matter.
            var cycleEntries = new CycleTimerBroadcast[49];
            var positionEntries = new EntityPositionUpdate[49];
            for (int i = 0; i < 49; i++)
            {
                cycleEntries[i] = new CycleTimerBroadcast(Eid((uint)(i + 1)), (ushort)(i * 100));
                positionEntries[i] = new EntityPositionUpdate(Eid((uint)(i + 1)), (short)(i * 10), 0, (short)(i * 5));
            }

            for (uint tick = 1; tick <= (uint)tickCount; tick++)
            {
                // Round-robin distribute this tick's 10 zone-wide DamageEvents across the 50 clients.
                var damageEventsByClient = new List<DamageEvent>[ClientCount];
                for (int e = 0; e < DamageEventsPerTickZoneWide; e++)
                {
                    int targetClient = (int)(((tick - 1) * DamageEventsPerTickZoneWide) + (uint)e) % ClientCount;
                    if (damageEventsByClient[targetClient] == null)
                    {
                        damageEventsByClient[targetClient] = new List<DamageEvent>();
                    }

                    damageEventsByClient[targetClient].Add(new DamageEvent(Eid(1000), Eid(1001), finalDamage: 50, isCrit: false, DamageType.Physical));
                }

                for (int c = 0; c < ClientCount; c++)
                {
                    IReadOnlyList<DamageEvent> clientDamageEvents = (IReadOnlyList<DamageEvent>)damageEventsByClient[c] ?? Array.Empty<DamageEvent>();

                    int ruBytes = RUBatchWriter.Write(
                        scratch, sequenceNumber: tick, tickNumber: tick,
                        damageEvents: clientDamageEvents, goldSyncEvents: Array.Empty<GoldSyncEvent>(),
                        otherSubMessages: Array.Empty<PendingSubMessage>(), clientIdForLogging: (uint)c);
                    perClientTotalBytes[c] += ruBytes;
                    maxSinglePacketBytes = Math.Max(maxSinglePacketBytes, ruBytes);

                    int cycleBytes = CycleBroadcastPacketWriter.Write(scratch, tick, tick, cycleEntries, (uint)c);
                    perClientTotalBytes[c] += cycleBytes;
                    maxSinglePacketBytes = Math.Max(maxSinglePacketBytes, cycleBytes);

                    int posBytes = PositionPacketWriter.Write(scratch, tick, tick, positionEntries, (uint)c);
                    perClientTotalBytes[c] += posBytes;
                    maxSinglePacketBytes = Math.Max(maxSinglePacketBytes, posBytes);
                }
            }

            return (perClientTotalBytes, maxSinglePacketBytes);
        }

        // -----------------------------------------------------------------------
        // AC-NC-21: no client's total outbound bytes across 200 ticks exceeds 300,000.
        // -----------------------------------------------------------------------

        [Test]
        public void LoadFixture_50Clients200TicksPeakCombat_NoClientExceeds300000TotalBytes()
        {
            // This fixture deliberately supplies 49 EntityPositionUpdate entries per client per
            // tick, which exceeds PositionPacketWriter's 35-entry cap and logs a BatchOverflow
            // warning on every one of the 200 x 50 writes — expected, and already covered in
            // isolation by PositionPacketWriter_49EntriesUnsorted_.... Silence the resulting log
            // flood here (rather than asserting on it 10,000 times) so it can't mask a genuine
            // future failure or trip a future strict-logging CI configuration.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                // Arrange & Act
                (long[] perClientTotalBytes, _) = RunLoadFixture(tickCount: 200);

                // Assert
                for (int c = 0; c < ClientCount; c++)
                {
                    Assert.LessOrEqual(perClientTotalBytes[c], 200L * 1500L,
                        $"Client {c} exceeded the AC-NC-21 budget of 200 x 1,500 = 300,000 total bytes across the 200-tick window.");
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        // -----------------------------------------------------------------------
        // AC-NC-33: no single R-U/CycleBroadcast/Position packet body exceeds 512 bytes,
        // across a 100-tick Scenario C (n=50, 10 DamageEvent/tick) fixture.
        // -----------------------------------------------------------------------

        [Test]
        public void LoadFixture_ScenarioC50Clients100Ticks_NoSinglePacketExceedsMaxMessageBodyBytes()
        {
            // See LoadFixture_50Clients200TicksPeakCombat_... — same expected Position-overflow log
            // flood from this fixture's deliberately-oversized 49-entry position set.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                // Arrange & Act
                (_, int maxSinglePacketBytes) = RunLoadFixture(tickCount: 100);

                // Assert
                Assert.LessOrEqual(maxSinglePacketBytes, RUBatchWriter.MAX_MESSAGE_BODY_BYTES,
                    "No R-U batch, CycleBroadcast, or Position packet body may exceed MAX_MESSAGE_BODY_BYTES (512 bytes).");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        // ===========================================================================================
        // AC-BUF-1: buffer pool allocation beyond MAX_PLAYERS_PER_ZONE capacity fails and logs a
        // BufferPoolExhausted critical anomaly.
        // ===========================================================================================

        [Test]
        public void ZoneBufferPool_AllocateUpToCapacity_AllSucceedWithDistinctBuffers()
        {
            // Arrange
            var pool = new ZoneBufferPool(ZoneBufferPool.MAX_PLAYERS_PER_ZONE);
            var allocated = new List<ClientBufferSet>();

            // Act
            for (int i = 0; i < ZoneBufferPool.MAX_PLAYERS_PER_ZONE; i++)
            {
                bool ok = pool.TryAllocate(out int slotIndex, out ClientBufferSet bufferSet);
                Assert.IsTrue(ok, $"Allocation {i} must succeed within pre-allocated capacity.");
                Assert.AreEqual(i, slotIndex);
                Assert.IsNotNull(bufferSet);
                allocated.Add(bufferSet);
            }

            // Assert — every allocated ClientBufferSet is a distinct pre-allocated instance.
            Assert.AreEqual(ZoneBufferPool.MAX_PLAYERS_PER_ZONE, allocated.Count);
            for (int i = 0; i < allocated.Count; i++)
            {
                for (int j = i + 1; j < allocated.Count; j++)
                {
                    Assert.AreNotSame(allocated[i], allocated[j], "Every allocated ClientBufferSet must be a distinct instance.");
                }
            }
        }

        [Test]
        public void ZoneBufferPool_TryAllocateBeyondCapacity_FailsAndLogsBufferPoolExhausted()
        {
            // Arrange — a small pool, fully allocated.
            var pool = new ZoneBufferPool(capacity: 3);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(pool.TryAllocate(out _, out _));
            }

            LogAssert.Expect(LogType.Error, new Regex(@"\[ZoneBufferPool\] BufferPoolExhausted"));

            // Act
            bool ok = pool.TryAllocate(out int slotIndex, out ClientBufferSet bufferSet);

            // Assert
            Assert.IsFalse(ok, "Allocation beyond pre-allocated capacity must fail.");
            Assert.AreEqual(-1, slotIndex);
            Assert.IsNull(bufferSet);
        }

        [Test]
        public void ZoneBufferPool_ReleaseThenAllocate_ReusesTheReleasedSlot()
        {
            // Arrange — a small pool, fully allocated.
            var pool = new ZoneBufferPool(capacity: 2);
            Assert.IsTrue(pool.TryAllocate(out int firstSlot, out ClientBufferSet firstBuffers));
            Assert.IsTrue(pool.TryAllocate(out int secondSlot, out _));

            // Act — release the first slot, then allocate again.
            pool.Release(firstSlot);
            bool ok = pool.TryAllocate(out int reallocatedSlot, out ClientBufferSet reallocatedBuffers);

            // Assert
            Assert.IsTrue(ok, "Allocation must succeed after a slot is released.");
            Assert.AreEqual(firstSlot, reallocatedSlot, "The released slot must be reused.");
            Assert.AreSame(firstBuffers, reallocatedBuffers, "The same pre-allocated ClientBufferSet instance must be handed out again.");
            _ = secondSlot;
        }

        // ===========================================================================================
        // Proactive edge-case coverage beyond the 4 blocking ACs.
        // ===========================================================================================

        // -----------------------------------------------------------------------
        // DamageEvent intra-class overflow: 30 entries queued (3 above the 27-entry cap) — the
        // oldest 3 (by input/arrival order) are dropped, the newest 27 are kept, and a
        // DamageEventIntraclassOverflow anomaly is logged.
        // -----------------------------------------------------------------------

        [Test]
        public void RUBatchWriter_ThirtyDamageEventsQueued_HoldsNewest27DropsOldest3AndLogsIntraclassOverflow()
        {
            // Arrange — 30 DamageEvent entries, FinalDamage encodes arrival order (1 = oldest).
            var damageEvents = new DamageEvent[30];
            for (int i = 0; i < 30; i++)
            {
                damageEvents[i] = new DamageEvent(Eid(1), Eid(2), finalDamage: i + 1, isCrit: false, DamageType.Physical);
            }

            byte[] buffer = new byte[ClientBufferSet.BufferSize];
            LogAssert.Expect(LogType.Warning, new Regex(@"\[RUBatchWriter\] DamageEventIntraclassOverflow.*droppedCount=3"));

            // Act
            int bytesWritten = RUBatchWriter.Write(
                buffer, sequenceNumber: 1u, tickNumber: 500u,
                damageEvents: damageEvents, goldSyncEvents: Array.Empty<GoldSyncEvent>(),
                otherSubMessages: Array.Empty<PendingSubMessage>(), clientIdForLogging: 9u);

            // Assert — exactly the cap (27) survives.
            Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out _, out ushort subMessageCount));
            Assert.AreEqual(RUBatchWriter.DamageEventIntraclassCap, subMessageCount);
            Assert.AreEqual(27, subMessageCount);

            // Assert — the surviving entries are the newest 27 (FinalDamage 4..30), oldest-first order preserved.
            int offset = BatchHeaderCodec.HeaderSize;
            for (int i = 0; i < subMessageCount; i++)
            {
                Assert.IsTrue(BatchSubMessageCodec.TryReadDamageEvent(buffer.AsSpan(offset), out DamageEvent decoded, out int subBytesRead));
                Assert.AreEqual(i + 4, decoded.FinalDamage, $"Surviving entry {i} must be the (i+4)-th originally-queued event (oldest 3 dropped).");
                offset += subBytesRead;
            }

            Assert.AreEqual(bytesWritten, offset, "Total bytes written must match the sum of the header and all surviving sub-messages.");
        }

        // -----------------------------------------------------------------------
        // Category-level overflow: an oversized synthetic LootBidUpdate payload set forces
        // overflow; LootBidUpdate (lowest priority present) is dropped in its entirety while
        // higher-priority categories (SkillCastResult, ConnectionQualityUpdate) survive, in
        // canonical write order, and a BatchOverflow anomaly is logged.
        // -----------------------------------------------------------------------

        [Test]
        public void RUBatchWriter_OversizedLootBidUpdateSet_DropsLootBidCategoryAtomicallyKeepsOthersLogsBatchOverflow()
        {
            // Arrange — SkillCastResult(1) + ConnectionQualityUpdate(1) fit trivially (12 + 24 + 5 = 41
            // bytes), but 25 LootBidUpdate entries at 24 bytes each (600 bytes) push the batch to 641
            // bytes, forcing category-level eviction.
            const ushort skillCastResultTypeId = 0x0A10;
            const ushort connectionQualityTypeId = 0x0A20;
            const ushort lootBidTypeId = 0x0A30;

            var other = new List<PendingSubMessage>
            {
                new PendingSubMessage(RUBatchCategory.SkillCastResult, skillCastResultTypeId, new byte[20]),
                new PendingSubMessage(RUBatchCategory.ConnectionQualityUpdate, connectionQualityTypeId, new byte[1]),
            };
            for (int i = 0; i < 25; i++)
            {
                other.Add(new PendingSubMessage(RUBatchCategory.LootBidUpdate, lootBidTypeId, new byte[20]));
            }

            byte[] buffer = new byte[ClientBufferSet.BufferSize];
            LogAssert.Expect(LogType.Warning, new Regex(@"\[RUBatchWriter\] BatchOverflow"));

            // Act
            int bytesWritten = RUBatchWriter.Write(
                buffer, sequenceNumber: 1u, tickNumber: 700u,
                damageEvents: Array.Empty<DamageEvent>(), goldSyncEvents: Array.Empty<GoldSyncEvent>(),
                otherSubMessages: other, clientIdForLogging: 3u);

            // Assert — fits within budget; only the two non-LootBid sub-messages survive.
            Assert.LessOrEqual(bytesWritten, RUBatchWriter.MAX_MESSAGE_BODY_BYTES);
            Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out _, out ushort subMessageCount));
            Assert.AreEqual(2, subMessageCount, "LootBidUpdate must be dropped in its entirety; only SkillCastResult + ConnectionQualityUpdate survive.");

            // Assert — canonical write order preserved: SkillCastResult before ConnectionQualityUpdate.
            int offset = BatchHeaderCodec.HeaderSize;
            Assert.IsTrue(BatchSubMessageFraming.TryReadHeader(buffer.AsSpan(offset), out ushort firstBodySize, out ushort firstTypeId));
            Assert.AreEqual(skillCastResultTypeId, firstTypeId);
            offset += BatchSubMessageFraming.SubMessageHeaderSize + firstBodySize;

            Assert.IsTrue(BatchSubMessageFraming.TryReadHeader(buffer.AsSpan(offset), out ushort secondBodySize, out ushort secondTypeId));
            Assert.AreEqual(connectionQualityTypeId, secondTypeId);
            offset += BatchSubMessageFraming.SubMessageHeaderSize + secondBodySize;

            Assert.AreEqual(bytesWritten, offset);
        }

        [Test]
        public void RUBatchWriter_OtherSubMessagesContainsDamageEventOrGoldSyncEventCategory_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = new byte[ClientBufferSet.BufferSize];
            var misrouted = new List<PendingSubMessage>
            {
                new PendingSubMessage(RUBatchCategory.DamageEvent, 0x0301, new byte[14]),
            };

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                RUBatchWriter.Write(buffer, 1u, 1u, Array.Empty<DamageEvent>(), Array.Empty<GoldSyncEvent>(), misrouted, clientIdForLogging: 1u),
                "DamageEvent must be supplied via the typed damageEvents parameter, not otherSubMessages.");

            // Assert — the validation throw occurs in Step 2, strictly before any byte is written to
            // the destination (Step 6 writing begins only after all validation passes) — matches this
            // codebase's established "assert before writing, never a partial write" convention (see
            // WireIdCodec's zero-write guard).
            CollectionAssert.AreEqual(new byte[ClientBufferSet.BufferSize], buffer,
                "No byte may be written to the destination buffer before the misrouted-category validation throw.");
        }

        // -----------------------------------------------------------------------
        // Single opaque sub-message payload size boundary (MaxSingleOpaqueSubMessagePayloadBytes =
        // 496 bytes — CR-NET-7.7's "MessageTypeID + payload must never exceed MAX_MESSAGE_BODY_BYTES
        // - 14" guarantee that a single sub-message always fits in a fresh batch).
        // -----------------------------------------------------------------------

        [Test]
        public void RUBatchWriter_OpaqueSubMessagePayloadAtMaxBoundary_WritesSuccessfully()
        {
            // Arrange — exactly at the boundary: header(12) + subHeader(4) + payload(496) = 512.
            var other = new List<PendingSubMessage>
            {
                new PendingSubMessage(RUBatchCategory.SkillCastResult, 0x0A10, new byte[RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes]),
            };
            byte[] buffer = new byte[ClientBufferSet.BufferSize];

            // Act
            int bytesWritten = RUBatchWriter.Write(
                buffer, sequenceNumber: 1u, tickNumber: 1u,
                damageEvents: Array.Empty<DamageEvent>(), goldSyncEvents: Array.Empty<GoldSyncEvent>(),
                otherSubMessages: other, clientIdForLogging: 1u);

            // Assert — fits exactly at MAX_MESSAGE_BODY_BYTES, no exception.
            Assert.AreEqual(RUBatchWriter.MAX_MESSAGE_BODY_BYTES, bytesWritten);
            Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out _, out ushort subMessageCount));
            Assert.AreEqual(1, subMessageCount);
        }

        [Test]
        public void RUBatchWriter_OpaqueSubMessagePayloadOneByteOverBoundary_ThrowsInvalidOperationExceptionAndLogsOversizedSubMessage()
        {
            // Arrange — one byte over the boundary can never fit in a fresh batch, regardless of
            // what else is queued (CR-NET-7.7) — this is a caller sizing bug, not a network condition.
            var other = new List<PendingSubMessage>
            {
                new PendingSubMessage(RUBatchCategory.SkillCastResult, 0x0A10, new byte[RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes + 1]),
            };
            byte[] buffer = new byte[ClientBufferSet.BufferSize];
            LogAssert.Expect(LogType.Warning, new Regex(@"\[RUBatchWriter\] OversizedSubMessage"));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                RUBatchWriter.Write(buffer, 1u, 1u, Array.Empty<DamageEvent>(), Array.Empty<GoldSyncEvent>(), other, clientIdForLogging: 1u));
        }

        [Test]
        public void RUBatchWriter_EmptyInputs_WritesHeaderOnlyWithZeroSubMessageCount()
        {
            // Arrange
            byte[] buffer = new byte[ClientBufferSet.BufferSize];

            // Act
            int bytesWritten = RUBatchWriter.Write(
                buffer, sequenceNumber: 9u, tickNumber: 9u,
                damageEvents: Array.Empty<DamageEvent>(), goldSyncEvents: Array.Empty<GoldSyncEvent>(),
                otherSubMessages: Array.Empty<PendingSubMessage>(), clientIdForLogging: 1u);

            // Assert
            Assert.AreEqual(BatchHeaderCodec.HeaderSize, bytesWritten);
            Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out ServerMessageEnvelope envelope, out ushort subMessageCount));
            Assert.AreEqual(RUBatchWriter.TICK_BATCH_RU, envelope.MessageTypeId);
            Assert.AreEqual(0, subMessageCount);
        }

        // -----------------------------------------------------------------------
        // PositionPacketWriter: sorts ascending by EntityID, drops highest-EntityID-first on
        // overflow — the exact worked example from the GDD (49 entries -> 35 delivered, 14 dropped).
        // -----------------------------------------------------------------------

        [Test]
        public void PositionPacketWriter_49EntriesUnsorted_SortsAscendingAndDropsHighest14EntityIdsFirst()
        {
            // Arrange — entries supplied in descending order to prove sorting, not just pass-through.
            var entries = new EntityPositionUpdate[49];
            for (int i = 0; i < 49; i++)
            {
                uint entityId = 49u - (uint)i; // 49, 48, ..., 1
                entries[i] = new EntityPositionUpdate(Eid(entityId), 0, 0, 0);
            }

            byte[] buffer = new byte[ClientBufferSet.BufferSize];
            LogAssert.Expect(LogType.Warning, new Regex(@"\[PositionPacketWriter\] BatchOverflow.*droppedCount=14"));

            // Act
            int bytesWritten = PositionPacketWriter.Write(buffer, sequenceNumber: 1u, tickNumber: 1u, entries, clientIdForLogging: 4u);

            // Assert — exactly 35 delivered (⌊500/14⌋), fits within budget.
            Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out ServerMessageEnvelope envelope, out ushort subMessageCount));
            Assert.AreEqual(PositionPacketWriter.TICK_BATCH_UU, envelope.MessageTypeId);
            Assert.AreEqual(35, subMessageCount);
            Assert.LessOrEqual(bytesWritten, RUBatchWriter.MAX_MESSAGE_BODY_BYTES);

            // Assert — the surviving entries are entityId 1..35, ascending, in write order (lowest IDs kept).
            int offset = BatchHeaderCodec.HeaderSize;
            for (int i = 0; i < subMessageCount; i++)
            {
                Assert.IsTrue(BatchSubMessageCodec.TryReadEntityPositionUpdate(buffer.AsSpan(offset), out EntityPositionUpdate decoded, out int subBytesRead));
                Assert.AreEqual((uint)(i + 1), decoded.EntityId.RawValue, $"Surviving entry {i} must be entityId {i + 1} (ascending, highest IDs dropped).");
                offset += subBytesRead;
            }
        }

        // -----------------------------------------------------------------------
        // CycleBroadcastPacketWriter: never drops — an oversized entry set throws and logs a
        // critical anomaly instead of silently truncating.
        // -----------------------------------------------------------------------

        [Test]
        public void CycleBroadcastPacketWriter_EntriesExceedBudget_ThrowsAndLogsCriticalAnomalyRatherThanTruncating()
        {
            // Arrange — 60 entries: 12 + 60*10 = 612 bytes, exceeding MAX_MESSAGE_BODY_BYTES (512).
            var entries = new CycleTimerBroadcast[60];
            for (int i = 0; i < 60; i++)
            {
                entries[i] = new CycleTimerBroadcast(Eid((uint)(i + 1)), 0);
            }

            byte[] buffer = new byte[ClientBufferSet.BufferSize];
            LogAssert.Expect(LogType.Error, new Regex(@"\[CycleBroadcastPacketWriter\] CycleBroadcastNeverDroppedOverflow \(CRITICAL\)"));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                CycleBroadcastPacketWriter.Write(buffer, 1u, 1u, entries, clientIdForLogging: 4u),
                "CycleTimerBroadcast must never be dropped or silently truncated on overflow.");
        }

        // -----------------------------------------------------------------------
        // Sub-message codec round-trips (DamageEvent, GoldSyncEvent, CycleTimerBroadcast,
        // EntityPositionUpdate) and a decode-side too-short guard, matching this project's
        // established convention of testing codecs directly (see WireProtocol_Envelope_Serialization_tests.cs).
        // -----------------------------------------------------------------------

        [Test]
        public void BatchSubMessageCodec_DamageEvent_WriteThenTryRead_RoundTripsExactly()
        {
            var original = new DamageEvent(Eid(10), Eid(20), finalDamage: 123, isCrit: true, DamageType.Magical);
            Span<byte> buffer = new byte[DamageEvent.BatchSize];

            int written = BatchSubMessageCodec.WriteDamageEvent(buffer, in original);

            Assert.AreEqual(DamageEvent.BatchSize, written);
            Assert.IsTrue(BatchSubMessageCodec.TryReadDamageEvent(buffer, out DamageEvent decoded, out int bytesRead));
            Assert.AreEqual(original, decoded);
            Assert.AreEqual(DamageEvent.BatchSize, bytesRead);
        }

        [Test]
        public void BatchSubMessageCodec_GoldSyncEvent_WriteThenTryRead_RoundTripsExactly()
        {
            var original = new GoldSyncEvent(new CharacterID(99), newBalance: 4000u, version: 12u, GoldTransactionReason.CompensatingRefund);
            Span<byte> buffer = new byte[GoldSyncEvent.BatchSize];

            int written = BatchSubMessageCodec.WriteGoldSyncEvent(buffer, in original);

            Assert.AreEqual(GoldSyncEvent.BatchSize, written);
            Assert.IsTrue(BatchSubMessageCodec.TryReadGoldSyncEvent(buffer, out GoldSyncEvent decoded, out int bytesRead));
            Assert.AreEqual(original, decoded);
            Assert.AreEqual(GoldSyncEvent.BatchSize, bytesRead);
        }

        [Test]
        public void BatchSubMessageCodec_CycleTimerBroadcast_WriteThenTryRead_RoundTripsExactly()
        {
            var original = new CycleTimerBroadcast(Eid(5), 4200);
            Span<byte> buffer = new byte[CycleTimerBroadcast.BatchSize];

            int written = BatchSubMessageCodec.WriteCycleTimerBroadcast(buffer, in original);

            Assert.AreEqual(CycleTimerBroadcast.BatchSize, written);
            Assert.IsTrue(BatchSubMessageCodec.TryReadCycleTimerBroadcast(buffer, out CycleTimerBroadcast decoded, out int bytesRead));
            Assert.AreEqual(original, decoded);
            Assert.AreEqual(CycleTimerBroadcast.BatchSize, bytesRead);
        }

        [Test]
        public void BatchSubMessageCodec_EntityPositionUpdate_WriteThenTryRead_RoundTripsExactly()
        {
            var original = new EntityPositionUpdate(Eid(5), posX: 1234, posY: -1, posZ: -500);
            Span<byte> buffer = new byte[EntityPositionUpdate.BatchSize];

            int written = BatchSubMessageCodec.WriteEntityPositionUpdate(buffer, in original);

            Assert.AreEqual(EntityPositionUpdate.BatchSize, written);
            Assert.IsTrue(BatchSubMessageCodec.TryReadEntityPositionUpdate(buffer, out EntityPositionUpdate decoded, out int bytesRead));
            Assert.AreEqual(original, decoded);
            Assert.AreEqual(EntityPositionUpdate.BatchSize, bytesRead);
        }

        [Test]
        public void BatchSubMessageCodec_TryReadDamageEvent_TooShortSource_ReturnsFalse()
        {
            ReadOnlySpan<byte> tooShort = new byte[DamageEvent.BatchSize - 1];

            bool ok = BatchSubMessageCodec.TryReadDamageEvent(tooShort, out DamageEvent decoded, out int bytesRead);

            Assert.IsFalse(ok);
            Assert.AreEqual(default(DamageEvent), decoded);
            Assert.AreEqual(0, bytesRead);
        }

        [Test]
        public void BatchHeaderCodec_WriteThenTryRead_RoundTripsExactly()
        {
            var envelope = new ServerMessageEnvelope(RUBatchWriter.TICK_BATCH_RU, sequenceNumber: 42u, serverTickNumber: 1000u);
            Span<byte> buffer = new byte[BatchHeaderCodec.HeaderSize];

            BatchHeaderCodec.Write(buffer, in envelope, subMessageCount: 5);

            Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out ServerMessageEnvelope decodedEnvelope, out ushort decodedCount));
            Assert.AreEqual(envelope, decodedEnvelope);
            Assert.AreEqual(5, decodedCount);
        }

        [Test]
        public void BatchHeaderCodec_TryRead_TooShortSource_ReturnsFalse()
        {
            ReadOnlySpan<byte> tooShort = new byte[BatchHeaderCodec.HeaderSize - 1];

            bool ok = BatchHeaderCodec.TryRead(tooShort, out ServerMessageEnvelope envelope, out ushort subMessageCount);

            Assert.IsFalse(ok);
            Assert.AreEqual(default(ServerMessageEnvelope), envelope);
            Assert.AreEqual(0, subMessageCount);
        }

        // -----------------------------------------------------------------------
        // WireEnumCodec.DecodeGoldTransactionReason — the substitute-and-continue extension this
        // story adds for GoldSyncEvent.Reason decoding.
        // -----------------------------------------------------------------------

        [TestCase((byte)GoldTransactionReason.MonsterDrop, GoldTransactionReason.MonsterDrop)]
        [TestCase((byte)GoldTransactionReason.CompensatingRefund, GoldTransactionReason.CompensatingRefund)]
        [TestCase((byte)GoldTransactionReason.Other, GoldTransactionReason.Other)]
        public void WireEnumCodec_DecodeGoldTransactionReason_ValidByte_PassesThroughUnchanged(byte rawByte, GoldTransactionReason expected)
        {
            GoldTransactionReason decoded = WireEnumCodec.DecodeGoldTransactionReason(rawByte, messageTypeId: GoldSyncEvent.MessageTypeId);

            Assert.AreEqual(expected, decoded);
        }

        [Test]
        public void WireEnumCodec_DecodeGoldTransactionReason_OutOfRangeByte_SubstitutesOtherAndLogsAnomaly()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeGoldTransactionReason.*100"));

            GoldTransactionReason decoded = WireEnumCodec.DecodeGoldTransactionReason(100, messageTypeId: GoldSyncEvent.MessageTypeId);

            Assert.AreEqual(GoldTransactionReason.Other, decoded);
        }
    }
}
