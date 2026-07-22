using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 028 — the RFR-2 relevance filter algorithm
    /// (<see cref="RelevanceFilter"/>) and its two new concrete R-U batch sub-message schemas
    /// (<see cref="EntityHealthUpdate"/>, <see cref="PartyMemberHealthUpdate"/> via
    /// <see cref="BatchSubMessageCodec"/>). Covers all 4 blocking ACs from
    /// <c>story-028-relevance-filter-algorithm.md</c>: AC-RFR-01 (solo/no-target exclusivity),
    /// AC-RFR-02 (full-party exclusivity), AC-RFR-04 (Scenario C byte budget composed through the
    /// real, unmodified <see cref="RUBatchWriter"/>), AC-RFR-07 (self-slot HP tracking over ticks).
    /// </summary>
    [TestFixture]
    internal sealed class RelevanceFilter_HealthUpdateSets_Tests
    {
        private static EntityID Eid(uint value) => new EntityID(value);

        private static readonly EntityHealthUpdate NoTarget = default; // EntityId == EntityID.Invalid (0)

        // ===========================================================================================
        // AC-RFR-01: solo player, no target, across 50 ticks — each R-U batch contains exactly 1
        // EntityHealthUpdate (self-slot) and 0 PartyMemberHealthUpdate.
        // ===========================================================================================

        [Test]
        public void AC_RFR_01_SoloNoTarget_50Ticks_EachBatchExactlyOneEHUZeroPMHU()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var selfSlot = new EntityHealthUpdate(Eid(1), currentHP: 1000, maxHP: 1000);
            byte[] buffer = new byte[ClientBufferSet.BufferSize];

            for (uint tick = 1; tick <= 50; tick++)
            {
                // Act
                List<PendingSubMessage> healthUpdates = RelevanceFilter.BuildHealthUpdateSubMessages(
                    clientId: 1u, in selfSlot, in NoTarget, partyMembers: null, observer);

                int bytesWritten = RUBatchWriter.Write(
                    buffer, sequenceNumber: tick, tickNumber: tick,
                    damageEvents: Array.Empty<DamageEvent>(), goldSyncEvents: Array.Empty<GoldSyncEvent>(),
                    otherSubMessages: healthUpdates, clientIdForLogging: 1u, observer: observer);

                // Assert — exactly 1 sub-message in the batch (the self-slot EHU), nothing else.
                Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out _, out ushort subMessageCount));
                Assert.AreEqual(1, subMessageCount, $"Tick {tick}: solo/no-target batch must contain exactly 1 sub-message.");
                Assert.AreEqual(BatchHeaderCodec.HeaderSize + EntityHealthUpdate.BatchSize, bytesWritten);

                Assert.IsTrue(BatchSubMessageCodec.TryReadEntityHealthUpdate(buffer.AsSpan(BatchHeaderCodec.HeaderSize), out EntityHealthUpdate decoded, out int subBytesRead));
                Assert.AreEqual(selfSlot, decoded, $"Tick {tick}: the single sub-message must be the self-slot EHU.");
                Assert.AreEqual(bytesWritten, BatchHeaderCodec.HeaderSize + subBytesRead);
            }

            // Assert — the observer hook fired once per tick, always with exactly [self], never a party member.
            Assert.AreEqual(50, observer.RUBatchEntityHealthUpdatesCalls.Count);
            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual(1u, observer.RUBatchEntityHealthUpdatesCalls[i].clientId);
                CollectionAssert.AreEqual(new uint[] { 1u }, observer.RUBatchEntityHealthUpdatesCalls[i].deliveredEntityIds,
                    $"Tick {i + 1}: deliveredEntityIds must be exactly [self], zero PartyMemberHealthUpdate.");
            }
        }

        // ===========================================================================================
        // AC-RFR-02: client A in a full 4-person party (B, C, D), no target — exactly 3
        // PartyMemberHealthUpdate + exactly 1 EntityHealthUpdate (self-slot only, never for B/C/D).
        // ===========================================================================================

        [Test]
        public void AC_RFR_02_FullPartyNoTarget_OneTick_ExactlyThreePMHUAndOneSelfEHU_NoneForPartyMembersViaEHU()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var selfSlot = new EntityHealthUpdate(Eid(1), currentHP: 900, maxHP: 1000);
            var partyMembers = new[]
            {
                new PartyMemberHealthUpdate(Eid(2), currentHP: 500, maxHP: 500, currentMP: 100, maxMP: 100), // B
                new PartyMemberHealthUpdate(Eid(3), currentHP: 400, maxHP: 500, currentMP: 80, maxMP: 100),  // C
                new PartyMemberHealthUpdate(Eid(4), currentHP: 500, maxHP: 500, currentMP: 100, maxMP: 100), // D
            };
            byte[] buffer = new byte[ClientBufferSet.BufferSize];

            // Act
            List<PendingSubMessage> healthUpdates = RelevanceFilter.BuildHealthUpdateSubMessages(
                clientId: 1u, in selfSlot, in NoTarget, partyMembers, observer);

            int bytesWritten = RUBatchWriter.Write(
                buffer, sequenceNumber: 1u, tickNumber: 1u,
                damageEvents: Array.Empty<DamageEvent>(), goldSyncEvents: Array.Empty<GoldSyncEvent>(),
                otherSubMessages: healthUpdates, clientIdForLogging: 1u, observer: observer);

            // Assert — exactly 4 sub-messages: 1 EHU (self) + 3 PMHU (B, C, D).
            Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out _, out ushort subMessageCount));
            Assert.AreEqual(4, subMessageCount);
            Assert.AreEqual(BatchHeaderCodec.HeaderSize + EntityHealthUpdate.BatchSize + (3 * PartyMemberHealthUpdate.BatchSize), bytesWritten);

            // Assert — canonical write order: EntityHealthUpdate category before PartyMemberHealthUpdate (RUBatchCategory declaration order).
            int offset = BatchHeaderCodec.HeaderSize;
            Assert.IsTrue(BatchSubMessageCodec.TryReadEntityHealthUpdate(buffer.AsSpan(offset), out EntityHealthUpdate decodedSelf, out int selfBytesRead));
            Assert.AreEqual(selfSlot, decodedSelf, "The lone EntityHealthUpdate must be the self-slot — never a party member.");
            offset += selfBytesRead;

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(BatchSubMessageCodec.TryReadPartyMemberHealthUpdate(buffer.AsSpan(offset), out PartyMemberHealthUpdate decodedMember, out int memberBytesRead));
                Assert.AreEqual(partyMembers[i], decodedMember, $"PartyMemberHealthUpdate {i} must match input order (B, C, D).");
                offset += memberBytesRead;
            }

            Assert.AreEqual(bytesWritten, offset);

            // Assert — the observer's EHU-only hook reports exactly [self], never B/C/D.
            Assert.AreEqual(1, observer.RUBatchEntityHealthUpdatesCalls.Count);
            CollectionAssert.AreEqual(new uint[] { 1u }, observer.RUBatchEntityHealthUpdatesCalls[0].deliveredEntityIds,
                "EntityHealthUpdate delivery must be exactly [self] — B, C, D receive PartyMemberHealthUpdate only, never EntityHealthUpdate.");
        }

        // ===========================================================================================
        // AC-RFR-04: client A in a full 4-person party, targeting a non-party entity, at n=50
        // Scenario C density (10 DamageEvent/tick) — total R-U batch <= 400 bytes, nothing dropped.
        // Mirrors F-RFR-2's worked example exactly (12 + 180 + 32 + 72 + 17 + 5 = 318 bytes) —
        // recomputed independently here, not assumed equal to AC-RFR-04's separate <=400 bound.
        // ===========================================================================================

        [Test]
        public void AC_RFR_04_FullPartyNonPartyTarget_ScenarioCDensity_TotalBatchAtMost400BytesNothingDropped()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var selfSlot = new EntityHealthUpdate(Eid(1), currentHP: 900, maxHP: 1000);
            var target = new EntityHealthUpdate(Eid(99), currentHP: 300, maxHP: 300); // non-party entity
            var partyMembers = new[]
            {
                new PartyMemberHealthUpdate(Eid(2), 500, 500, 100, 100),
                new PartyMemberHealthUpdate(Eid(3), 400, 500, 80, 100),
                new PartyMemberHealthUpdate(Eid(4), 500, 500, 100, 100),
            };

            var damageEvents = new DamageEvent[10];
            for (int i = 0; i < 10; i++)
            {
                damageEvents[i] = new DamageEvent(Eid(1), Eid(99), finalDamage: 50, isCrit: false, DamageType.Physical);
            }

            var goldSyncEvents = new[] { new GoldSyncEvent(new CharacterID(1), newBalance: 100u, version: 1u, GoldTransactionReason.MonsterDrop) };

            byte[] buffer = new byte[ClientBufferSet.BufferSize];

            // Act
            List<PendingSubMessage> healthUpdates = RelevanceFilter.BuildHealthUpdateSubMessages(
                clientId: 1u, in selfSlot, in target, partyMembers, observer);

            // ConnectionQualityUpdate has no concrete schema yet (Story 028 does not own it) — carried
            // as an opaque 1-byte payload via PendingSubMessage, matching this codebase's established
            // precedent for the remaining not-yet-implemented categories (see WireProtocol_BatchFraming_tests.cs).
            var otherSubMessages = new List<PendingSubMessage>(healthUpdates)
            {
                new PendingSubMessage(RUBatchCategory.ConnectionQualityUpdate, messageTypeId: 0x0A20, new byte[1]),
            };

            int bytesWritten = RUBatchWriter.Write(
                buffer, sequenceNumber: 1u, tickNumber: 1u,
                damageEvents: damageEvents, goldSyncEvents: goldSyncEvents,
                otherSubMessages: otherSubMessages, clientIdForLogging: 1u, observer: observer);

            // Assert — matches F-RFR-2's worked example exactly: 12(header) + 180(10 DamageEvent) +
            // 32(2 EntityHealthUpdate) + 72(3 PartyMemberHealthUpdate) + 17(GoldSyncEvent) + 5(ConnectionQualityUpdate) = 318.
            const int expectedBytes = 12 + (10 * DamageEvent.BatchSize) + (2 * EntityHealthUpdate.BatchSize)
                + (3 * PartyMemberHealthUpdate.BatchSize) + GoldSyncEvent.BatchSize + 5;
            Assert.AreEqual(318, expectedBytes, "Sanity-check against F-RFR-2's own stated total.");
            Assert.AreEqual(expectedBytes, bytesWritten);

            // Assert — AC-RFR-04's actual numeric bound: <=400 bytes, well below the 512-byte cap.
            Assert.LessOrEqual(bytesWritten, 400);
            Assert.LessOrEqual(bytesWritten, RUBatchWriter.MAX_MESSAGE_BODY_BYTES);

            // Assert — nothing dropped: all 17 sub-messages present (10 DamageEvent + 2 EHU + 3 PMHU + 1 GoldSyncEvent + 1 ConnectionQualityUpdate).
            Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out _, out ushort subMessageCount));
            Assert.AreEqual(17, subMessageCount);
        }

        // ===========================================================================================
        // AC-RFR-07: client A, HP=750 at zone entry, 10 ticks of active combat with decrementing HP —
        // every batch's self-slot EntityHealthUpdate reflects the current authoritative HP.
        // ===========================================================================================

        [Test]
        public void AC_RFR_07_TenTicksHPDecrementing_SelfSlotEHUTracksAuthoritativeHPEveryTick()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            byte[] buffer = new byte[ClientBufferSet.BufferSize];
            const int startingHP = 750;
            const int maxHP = 1000;
            const int damagePerTick = 25;

            for (uint tick = 1; tick <= 10; tick++)
            {
                int expectedHP = startingHP - ((int)(tick - 1) * damagePerTick);
                var selfSlot = new EntityHealthUpdate(Eid(1), currentHP: expectedHP, maxHP: maxHP);

                // Act
                List<PendingSubMessage> healthUpdates = RelevanceFilter.BuildHealthUpdateSubMessages(
                    clientId: 1u, in selfSlot, in NoTarget, partyMembers: null, observer);

                int bytesWritten = RUBatchWriter.Write(
                    buffer, sequenceNumber: tick, tickNumber: tick,
                    damageEvents: Array.Empty<DamageEvent>(), goldSyncEvents: Array.Empty<GoldSyncEvent>(),
                    otherSubMessages: healthUpdates, clientIdForLogging: 1u, observer: observer);

                // Assert — the decoded self-slot EHU matches the server's authoritative HP for this tick.
                Assert.IsTrue(BatchSubMessageCodec.TryReadEntityHealthUpdate(buffer.AsSpan(BatchHeaderCodec.HeaderSize), out EntityHealthUpdate decoded, out _));
                Assert.AreEqual(expectedHP, decoded.CurrentHP, $"Tick {tick}: self-slot EHU must reflect the authoritative HP.");
                Assert.AreEqual(maxHP, decoded.MaxHP);
                _ = bytesWritten;
            }

            // Assert — the observer hook fired every tick with the self-slot present.
            Assert.AreEqual(10, observer.RUBatchEntityHealthUpdatesCalls.Count);
            for (int i = 0; i < 10; i++)
            {
                CollectionAssert.AreEqual(new uint[] { 1u }, observer.RUBatchEntityHealthUpdatesCalls[i].deliveredEntityIds);
            }
        }

        // ===========================================================================================
        // Proactive edge-case coverage beyond the 4 blocking ACs.
        // ===========================================================================================

        // -----------------------------------------------------------------------
        // RFR-1 separation invariant / EC-RFR-5: target IS a party member — the target-slot EHU is
        // suppressed; that member still receives its PartyMemberHealthUpdate.
        // -----------------------------------------------------------------------

        [Test]
        public void SeparationInvariant_TargetIsPartyMember_TargetSlotEHUSuppressedMemberStillGetsPMHU()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var selfSlot = new EntityHealthUpdate(Eid(1), currentHP: 900, maxHP: 1000);
            var partyMembers = new[]
            {
                new PartyMemberHealthUpdate(Eid(2), 500, 500, 100, 100), // B — this is also the target
                new PartyMemberHealthUpdate(Eid(3), 400, 500, 80, 100),  // C
                new PartyMemberHealthUpdate(Eid(4), 500, 500, 100, 100), // D
            };
            var target = new EntityHealthUpdate(Eid(2), currentHP: 500, maxHP: 500); // targeting B, a party member

            // Act
            List<PendingSubMessage> healthUpdates = RelevanceFilter.BuildHealthUpdateSubMessages(
                clientId: 1u, in selfSlot, in target, partyMembers, observer);

            // Assert — 1 EHU (self only) + 3 PMHU (B, C, D) = 4 sub-messages, never 2 EHU.
            Assert.AreEqual(4, healthUpdates.Count);
            int ehuCount = 0, pmhuCount = 0;
            foreach (PendingSubMessage msg in healthUpdates)
            {
                if (msg.Category == RUBatchCategory.EntityHealthUpdate) ehuCount++;
                if (msg.Category == RUBatchCategory.PartyMemberHealthUpdate) pmhuCount++;
            }
            Assert.AreEqual(1, ehuCount, "Only the self-slot EHU may be present — the target-slot EHU for B must be suppressed.");
            Assert.AreEqual(3, pmhuCount);

            // Assert — the observer's EHU-only hook reports exactly [self], not B (B is party-set only).
            Assert.AreEqual(1, observer.RUBatchEntityHealthUpdatesCalls.Count);
            CollectionAssert.AreEqual(new uint[] { 1u }, observer.RUBatchEntityHealthUpdatesCalls[0].deliveredEntityIds);
        }

        // -----------------------------------------------------------------------
        // MaxPartyMembersExcludingSelf guard: more than 3 party members throws ArgumentException.
        // -----------------------------------------------------------------------

        [Test]
        public void BuildHealthUpdateSubMessages_FourPartyMembers_ThrowsArgumentException()
        {
            // Arrange
            var selfSlot = new EntityHealthUpdate(Eid(1), currentHP: 900, maxHP: 1000);
            var partyMembers = new[]
            {
                new PartyMemberHealthUpdate(Eid(2), 500, 500, 100, 100),
                new PartyMemberHealthUpdate(Eid(3), 500, 500, 100, 100),
                new PartyMemberHealthUpdate(Eid(4), 500, 500, 100, 100),
                new PartyMemberHealthUpdate(Eid(5), 500, 500, 100, 100), // one too many
            };

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                RelevanceFilter.BuildHealthUpdateSubMessages(clientId: 1u, in selfSlot, in NoTarget, partyMembers),
                $"partyMembers.Count (4) exceeds MaxPartyMembersExcludingSelf ({RelevanceFilter.MaxPartyMembersExcludingSelf}).");
        }

        [Test]
        public void MaxPartyMembersExcludingSelf_EqualsThree()
        {
            Assert.AreEqual(3, RelevanceFilter.MaxPartyMembersExcludingSelf);
            Assert.AreEqual(4, RelevanceFilter.MAX_PARTY_SIZE);
        }

        // -----------------------------------------------------------------------
        // BatchSubMessageCodec round-trips for the two new schemas.
        // -----------------------------------------------------------------------

        [Test]
        public void BatchSubMessageCodec_EntityHealthUpdate_WriteThenTryRead_RoundTripsExactly()
        {
            var original = new EntityHealthUpdate(Eid(10), currentHP: 750, maxHP: 1000);
            Span<byte> buffer = new byte[EntityHealthUpdate.BatchSize];

            int written = BatchSubMessageCodec.WriteEntityHealthUpdate(buffer, in original);

            Assert.AreEqual(EntityHealthUpdate.BatchSize, written);
            Assert.AreEqual(16, written);
            Assert.IsTrue(BatchSubMessageCodec.TryReadEntityHealthUpdate(buffer, out EntityHealthUpdate decoded, out int bytesRead));
            Assert.AreEqual(original, decoded);
            Assert.AreEqual(EntityHealthUpdate.BatchSize, bytesRead);
        }

        [Test]
        public void BatchSubMessageCodec_PartyMemberHealthUpdate_WriteThenTryRead_RoundTripsExactly()
        {
            var original = new PartyMemberHealthUpdate(Eid(20), currentHP: 400, maxHP: 500, currentMP: 80, maxMP: 100);
            Span<byte> buffer = new byte[PartyMemberHealthUpdate.BatchSize];

            int written = BatchSubMessageCodec.WritePartyMemberHealthUpdate(buffer, in original);

            Assert.AreEqual(PartyMemberHealthUpdate.BatchSize, written);
            Assert.AreEqual(24, written);
            Assert.IsTrue(BatchSubMessageCodec.TryReadPartyMemberHealthUpdate(buffer, out PartyMemberHealthUpdate decoded, out int bytesRead));
            Assert.AreEqual(original, decoded);
            Assert.AreEqual(PartyMemberHealthUpdate.BatchSize, bytesRead);
        }

        [Test]
        public void BatchSubMessageCodec_TryReadEntityHealthUpdate_TooShortSource_ReturnsFalse()
        {
            ReadOnlySpan<byte> tooShort = new byte[EntityHealthUpdate.BatchSize - 1];

            bool ok = BatchSubMessageCodec.TryReadEntityHealthUpdate(tooShort, out EntityHealthUpdate decoded, out int bytesRead);

            Assert.IsFalse(ok);
            Assert.AreEqual(default(EntityHealthUpdate), decoded);
            Assert.AreEqual(0, bytesRead);
        }

        [Test]
        public void BatchSubMessageCodec_TryReadPartyMemberHealthUpdate_TooShortSource_ReturnsFalse()
        {
            ReadOnlySpan<byte> tooShort = new byte[PartyMemberHealthUpdate.BatchSize - 1];

            bool ok = BatchSubMessageCodec.TryReadPartyMemberHealthUpdate(tooShort, out PartyMemberHealthUpdate decoded, out int bytesRead);

            Assert.IsFalse(ok);
            Assert.AreEqual(default(PartyMemberHealthUpdate), decoded);
            Assert.AreEqual(0, bytesRead);
        }
    }
}
