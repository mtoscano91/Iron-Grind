using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 029 — the <c>SetTarget</c> RPC wire codec
    /// (<see cref="SetTargetCodec"/>) and per-client target-slot state (<see cref="TargetSlotTracker"/>),
    /// composed with Story 028's <see cref="RelevanceFilter"/> where the AC text requires proving the
    /// resulting <c>EntityHealthUpdate</c> set. Covers all 3 blocking ACs from
    /// <c>story-029-settarget-rpc-target-slot-management.md</c>: AC-RFR-03 (before-flush/after-flush
    /// timing), AC-RFR-05 (self-target rejection), AC-RFR-06 (target-change to a party member).
    /// </summary>
    [TestFixture]
    internal sealed class RelevanceFilter_SetTargetRpc_Tests
    {
        private static EntityID Eid(uint value) => new EntityID(value);

        // ===========================================================================================
        // AC-RFR-03: before-flush vs. after-flush target-change timing, via call-ordering composition
        // of TargetSlotTracker + RelevanceFilter.
        // ===========================================================================================

        [Test]
        public void AC_RFR_03_SetTargetBeforeFlush_TickTBatchReflectsNewTargetNotOld()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var tracker = new TargetSlotTracker();
            EntityID clientEntityId = Eid(1);
            EntityID entityE = Eid(50); // prior target (non-party)
            EntityID entityF = Eid(60); // new target (non-party)
            var validZoneEntityIds = new[] { entityE, entityF };

            Assert.AreEqual(SetTargetOutcome.Accepted,
                tracker.ProcessSetTarget(clientId: 7u, ownEntityId: clientEntityId, targetEntityId: entityE, validZoneEntityIds, observer));

            // Act — SetTarget(F) arrives before tick T's batch flush.
            SetTargetOutcome outcome = tracker.ProcessSetTarget(
                clientId: 7u, ownEntityId: clientEntityId, targetEntityId: entityF, validZoneEntityIds, observer);

            var selfSlot = new EntityHealthUpdate(clientEntityId, currentHP: 900, maxHP: 1000);
            EntityID targetAtFlush = tracker.GetTarget(7u);
            var targetSlot = new EntityHealthUpdate(targetAtFlush, currentHP: 300, maxHP: 300);
            RelevanceFilter.BuildHealthUpdateSubMessages(clientId: 7u, in selfSlot, in targetSlot, partyMembers: null, observer);

            // Assert
            Assert.AreEqual(SetTargetOutcome.Accepted, outcome);
            Assert.AreEqual(entityF, targetAtFlush);
            Assert.AreEqual(1, observer.RUBatchEntityHealthUpdatesCalls.Count);
            CollectionAssert.AreEqual(new uint[] { clientEntityId.RawValue, entityF.RawValue },
                observer.RUBatchEntityHealthUpdatesCalls[0].deliveredEntityIds,
                "Tick T's batch must contain F (and self), not E, when SetTarget arrives before the flush.");
        }

        [Test]
        public void AC_RFR_03_SetTargetAfterFlush_TickTKeepsOldTargetTickTPlus1ReflectsNew()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var tracker = new TargetSlotTracker();
            EntityID clientEntityId = Eid(1);
            EntityID entityE = Eid(50);
            EntityID entityF = Eid(60);
            var validZoneEntityIds = new[] { entityE, entityF };

            Assert.AreEqual(SetTargetOutcome.Accepted,
                tracker.ProcessSetTarget(clientId: 7u, ownEntityId: clientEntityId, targetEntityId: entityE, validZoneEntityIds, observer));

            var selfSlot = new EntityHealthUpdate(clientEntityId, currentHP: 900, maxHP: 1000);

            // Act — tick T's batch is built BEFORE SetTarget(F) arrives.
            EntityID targetAtTickT = tracker.GetTarget(7u);
            var targetSlotTickT = new EntityHealthUpdate(targetAtTickT, currentHP: 300, maxHP: 300);
            RelevanceFilter.BuildHealthUpdateSubMessages(clientId: 7u, in selfSlot, in targetSlotTickT, partyMembers: null, observer);

            // SetTarget(F) arrives after tick T's flush.
            SetTargetOutcome outcome = tracker.ProcessSetTarget(
                clientId: 7u, ownEntityId: clientEntityId, targetEntityId: entityF, validZoneEntityIds, observer);

            // Tick T+1's batch is built after the RPC was processed.
            EntityID targetAtTickTPlus1 = tracker.GetTarget(7u);
            var targetSlotTickTPlus1 = new EntityHealthUpdate(targetAtTickTPlus1, currentHP: 250, maxHP: 300);
            RelevanceFilter.BuildHealthUpdateSubMessages(clientId: 7u, in selfSlot, in targetSlotTickTPlus1, partyMembers: null, observer);

            // Assert
            Assert.AreEqual(SetTargetOutcome.Accepted, outcome);
            Assert.AreEqual(2, observer.RUBatchEntityHealthUpdatesCalls.Count);
            CollectionAssert.AreEqual(new uint[] { clientEntityId.RawValue, entityE.RawValue },
                observer.RUBatchEntityHealthUpdatesCalls[0].deliveredEntityIds,
                "Tick T's batch must still contain E — SetTarget arrived after the flush.");
            CollectionAssert.AreEqual(new uint[] { clientEntityId.RawValue, entityF.RawValue },
                observer.RUBatchEntityHealthUpdatesCalls[1].deliveredEntityIds,
                "Tick T+1's batch must contain F.");
        }

        // ===========================================================================================
        // AC-RFR-05: self-target attempt — target slot unchanged, RejectedSelfTarget returned,
        // OnSelfTargetAttemptLogged fired exactly once, self EHU appears exactly once (never twice).
        // ===========================================================================================

        [Test]
        public void AC_RFR_05_SelfTargetAttempt_SlotUnchangedRejectedAnomalyLoggedOnce_SelfEHUAppearsExactlyOnce()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var tracker = new TargetSlotTracker();
            EntityID clientEntityId = Eid(1);
            EntityID priorTarget = Eid(50); // non-party
            var validZoneEntityIds = new[] { priorTarget, clientEntityId };

            Assert.AreEqual(SetTargetOutcome.Accepted,
                tracker.ProcessSetTarget(clientId: 7u, ownEntityId: clientEntityId, targetEntityId: priorTarget, validZoneEntityIds, observer));

            // Act — client A attempts to target its own EntityID.
            SetTargetOutcome outcome = tracker.ProcessSetTarget(
                clientId: 7u, ownEntityId: clientEntityId, targetEntityId: clientEntityId, validZoneEntityIds, observer);

            // Assert — rejected, slot unchanged, anomaly logged exactly once.
            Assert.AreEqual(SetTargetOutcome.RejectedSelfTarget, outcome);
            Assert.AreEqual(priorTarget, tracker.GetTarget(7u), "Self-target attempt must not mutate the target slot.");
            Assert.AreEqual(1, observer.SelfTargetAttemptLoggedCalls.Count);
            Assert.AreEqual(clientEntityId.RawValue, observer.SelfTargetAttemptLoggedCalls[0]);

            // Assert — composing with RelevanceFilter using the (unchanged) tracked target: client A's
            // own EntityID appears exactly once in the delivered set (self-slot), never a second time
            // via the target slot — proving the self-target attempt never reached the target slot.
            var selfSlot = new EntityHealthUpdate(clientEntityId, currentHP: 900, maxHP: 1000);
            var targetSlot = new EntityHealthUpdate(tracker.GetTarget(7u), currentHP: 300, maxHP: 300);
            RelevanceFilter.BuildHealthUpdateSubMessages(clientId: 7u, in selfSlot, in targetSlot, partyMembers: null, observer);

            Assert.AreEqual(1, observer.RUBatchEntityHealthUpdatesCalls.Count);
            IReadOnlyList<uint> delivered = observer.RUBatchEntityHealthUpdatesCalls[0].deliveredEntityIds;
            int selfOccurrences = 0;
            foreach (uint id in delivered)
            {
                if (id == clientEntityId.RawValue)
                {
                    selfOccurrences++;
                }
            }
            Assert.AreEqual(1, selfOccurrences, "Client A's EntityHealthUpdate must appear exactly once — never twice.");
        }

        // ===========================================================================================
        // AC-RFR-06: target-change to an existing party member — slot updates to the party member's
        // EntityID, but RelevanceFilter suppresses the target-slot EHU (separation invariant).
        // ===========================================================================================

        [Test]
        public void AC_RFR_06_TargetChangeToPartyMember_SlotUpdatesButEHUSuppressed_OneSelfPlusThreePMHU()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var tracker = new TargetSlotTracker();
            EntityID clientEntityId = Eid(1);
            EntityID memberB = Eid(2);
            EntityID memberC = Eid(3);
            EntityID memberD = Eid(4);
            EntityID entityE = Eid(50); // prior non-party target
            var validZoneEntityIds = new[] { entityE, memberB, memberC, memberD };

            Assert.AreEqual(SetTargetOutcome.Accepted,
                tracker.ProcessSetTarget(clientId: 7u, ownEntityId: clientEntityId, targetEntityId: entityE, validZoneEntityIds, observer));

            // Act — client A retargets to B, a party member.
            SetTargetOutcome outcome = tracker.ProcessSetTarget(
                clientId: 7u, ownEntityId: clientEntityId, targetEntityId: memberB, validZoneEntityIds, observer);

            // Assert — slot updated to B.
            Assert.AreEqual(SetTargetOutcome.Accepted, outcome);
            Assert.AreEqual(memberB, tracker.GetTarget(7u));

            // Compose with RelevanceFilter: separation invariant suppresses B's target-slot EHU.
            var selfSlot = new EntityHealthUpdate(clientEntityId, currentHP: 900, maxHP: 1000);
            var targetSlot = new EntityHealthUpdate(tracker.GetTarget(7u), currentHP: 500, maxHP: 500);
            var partyMembers = new[]
            {
                new PartyMemberHealthUpdate(memberB, 500, 500, 100, 100),
                new PartyMemberHealthUpdate(memberC, 400, 500, 80, 100),
                new PartyMemberHealthUpdate(memberD, 500, 500, 100, 100),
            };

            List<PendingSubMessage> healthUpdates = RelevanceFilter.BuildHealthUpdateSubMessages(
                clientId: 7u, in selfSlot, in targetSlot, partyMembers, observer);

            // Assert — 1 EHU (self only — E's EHU is replaced, B's EHU is suppressed) + 3 PMHU.
            int ehuCount = 0, pmhuCount = 0;
            foreach (PendingSubMessage msg in healthUpdates)
            {
                if (msg.Category == RUBatchCategory.EntityHealthUpdate) ehuCount++;
                if (msg.Category == RUBatchCategory.PartyMemberHealthUpdate) pmhuCount++;
            }
            Assert.AreEqual(1, ehuCount, "Only the self-slot EHU may be present — E's EHU is replaced, B's EHU is suppressed.");
            Assert.AreEqual(3, pmhuCount);

            Assert.AreEqual(1, observer.RUBatchEntityHealthUpdatesCalls.Count);
            CollectionAssert.AreEqual(new uint[] { clientEntityId.RawValue },
                observer.RUBatchEntityHealthUpdatesCalls[0].deliveredEntityIds,
                "Delivered EntityHealthUpdate set must be exactly [self] — E is gone (replaced) and B is party-set only.");
        }

        // ===========================================================================================
        // Proactive edge-case coverage beyond the 3 blocking ACs (directly specified in the story's
        // Implementation Notes).
        // ===========================================================================================

        [Test]
        public void ProcessSetTarget_InvalidTargetEntityId_NotInValidZoneEntityIds_RejectedSlotUnchanged()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var tracker = new TargetSlotTracker();
            EntityID clientEntityId = Eid(1);
            EntityID priorTarget = Eid(50);
            var validZoneEntityIds = new[] { priorTarget };
            EntityID bogusTarget = Eid(999); // not present in validZoneEntityIds

            Assert.AreEqual(SetTargetOutcome.Accepted,
                tracker.ProcessSetTarget(clientId: 7u, ownEntityId: clientEntityId, targetEntityId: priorTarget, validZoneEntityIds, observer));

            // Act
            SetTargetOutcome outcome = tracker.ProcessSetTarget(
                clientId: 7u, ownEntityId: clientEntityId, targetEntityId: bogusTarget, validZoneEntityIds, observer);

            // Assert
            Assert.AreEqual(SetTargetOutcome.RejectedInvalidTarget, outcome);
            Assert.AreEqual(priorTarget, tracker.GetTarget(7u), "Invalid target must not mutate the target slot.");
            Assert.AreEqual(1, observer.InvalidTargetEntityIdLoggedCalls.Count);
            Assert.AreEqual((7u, bogusTarget.RawValue), observer.InvalidTargetEntityIdLoggedCalls[0]);
        }

        [Test]
        public void ProcessSetTarget_Deselect_AlwaysSucceedsClearsSlot_EvenWithNullValidZoneEntityIds()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var tracker = new TargetSlotTracker();
            EntityID clientEntityId = Eid(1);
            EntityID priorTarget = Eid(50);
            var validZoneEntityIds = new[] { priorTarget };

            Assert.AreEqual(SetTargetOutcome.Accepted,
                tracker.ProcessSetTarget(clientId: 7u, ownEntityId: clientEntityId, targetEntityId: priorTarget, validZoneEntityIds, observer));
            Assert.AreEqual(priorTarget, tracker.GetTarget(7u));

            // Act — deselect (targetEntityId = EntityID.Invalid), with a null validZoneEntityIds to
            // prove the zone-validity check is skipped entirely for a deselect.
            SetTargetOutcome outcome = tracker.ProcessSetTarget(
                clientId: 7u, ownEntityId: clientEntityId, targetEntityId: EntityID.Invalid, validZoneEntityIds: null, observer);

            // Assert
            Assert.AreEqual(SetTargetOutcome.Accepted, outcome);
            Assert.AreEqual(EntityID.Invalid, tracker.GetTarget(7u));
            Assert.AreEqual(0, observer.SelfTargetAttemptLoggedCalls.Count);
            Assert.AreEqual(0, observer.InvalidTargetEntityIdLoggedCalls.Count);
        }

        [Test]
        public void GetTarget_UnknownClientId_ReturnsInvalid()
        {
            var tracker = new TargetSlotTracker();
            Assert.AreEqual(EntityID.Invalid, tracker.GetTarget(clientId: 999u));
        }

        [Test]
        public void ProcessSetTarget_SelfTargetAndNotInValidZone_RejectedAsSelfTarget_NotAsInvalidTarget()
        {
            // Pins check order (RFR-5 before RFR-3a): the client's own EntityID is deliberately
            // absent from validZoneEntityIds, so a self-target attempt can only be rejected as
            // RejectedSelfTarget if the self-target guard runs BEFORE the zone-validity guard. If a
            // future refactor swapped the order, this would incorrectly become RejectedInvalidTarget.
            var observer = new NetworkTestObserver();
            var tracker = new TargetSlotTracker();
            EntityID clientEntityId = Eid(1);
            var validZoneEntityIds = Array.Empty<EntityID>(); // self deliberately not present

            SetTargetOutcome outcome = tracker.ProcessSetTarget(
                clientId: 7u, ownEntityId: clientEntityId, targetEntityId: clientEntityId, validZoneEntityIds, observer);

            Assert.AreEqual(SetTargetOutcome.RejectedSelfTarget, outcome,
                "Self-target check must run before zone-validity — self is absent from validZoneEntityIds here.");
            Assert.AreEqual(1, observer.SelfTargetAttemptLoggedCalls.Count);
            Assert.AreEqual(0, observer.InvalidTargetEntityIdLoggedCalls.Count);
        }

        // ===========================================================================================
        // SetTargetCodec round-trips.
        // ===========================================================================================

        [Test]
        public void SetTargetCodec_WriteThenTryRead_RoundTripsExactly()
        {
            Span<byte> buffer = new byte[SetTarget.WireSize];

            int written = SetTargetCodec.Write(buffer, sequenceNumber: 7u, tickNumber: 1000u, senderEntityId: 501u, targetEntityId: 42u);

            Assert.AreEqual(SetTarget.WireSize, written);
            Assert.AreEqual(18, written);
            Assert.IsTrue(SetTargetCodec.TryRead(buffer, out ClientEntityMessageEnvelope envelope, out uint targetEntityId));
            Assert.AreEqual(SetTarget.MessageTypeId, envelope.MessageTypeId);
            Assert.AreEqual(7u, envelope.SequenceNumber);
            Assert.AreEqual(1000u, envelope.ServerTickNumber);
            Assert.AreEqual(501u, envelope.SenderEntityId);
            Assert.AreEqual(42u, targetEntityId);
        }

        [Test]
        public void SetTargetCodec_TargetEntityIdZero_RoundTripsAsZero_NoExceptionThrown()
        {
            // The one field in the wire protocol where 0 is a legitimate value (deselect, RFR-3a) —
            // must NOT throw, unlike WireIdCodec.SerializeEntityId's zero-write guard.
            Span<byte> buffer = new byte[SetTarget.WireSize];

            Assert.DoesNotThrow(() => SetTargetCodec.Write(buffer, sequenceNumber: 1u, tickNumber: 1u, senderEntityId: 501u, targetEntityId: 0u));
            Assert.IsTrue(SetTargetCodec.TryRead(buffer, out _, out uint targetEntityId));
            Assert.AreEqual(0u, targetEntityId);
        }

        // ===========================================================================================
        // CrossCuttingRpcGuardChain composition: RpcTypeTag.SetTarget has no rate limit (gap=0).
        // ===========================================================================================

        [Test]
        public void CrossCuttingRpcGuardChain_SetTarget_NoRateLimit_ConsecutiveSameTickCallsBothAccepted()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            EntityID entityId = Eid(501);
            guardChain.RegisterEntityOwnership(clientId: 7u, entityId);
            guardChain.MarkSessionReady(clientId: 7u);

            var descriptorA = new InboundRpcDescriptor(clientId: 7u, senderEntityId: entityId, rpcTypeTag: RpcTypeTag.SetTarget, currentTick: 100u);
            var descriptorB = new InboundRpcDescriptor(clientId: 7u, senderEntityId: entityId, rpcTypeTag: RpcTypeTag.SetTarget, currentTick: 100u);

            // Act & Assert — two SetTarget RPCs on the exact same tick both accepted (no rate limit).
            Assert.AreEqual(RpcGuardResult.Accepted, guardChain.Evaluate(descriptorA));
            Assert.AreEqual(RpcGuardResult.Accepted, guardChain.Evaluate(descriptorB));
        }
    }
}
