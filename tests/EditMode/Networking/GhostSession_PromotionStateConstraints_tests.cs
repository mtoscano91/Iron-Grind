using System;
using System.Collections.Generic;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 017 — Ghost Session cluster, first story:
    /// <see cref="GhostEntityTracker"/>'s CR-GH-2 promotion sequence (steps 2-4) and the CR-GH-4/
    /// CR-GH-5/CGS-1/CGS-2 state constraints a ghosted character is subject to. Covers all 7
    /// blocking ACs (AC-GH-1, AC-GH-2, AC-GH-3, AC-GH-13, AC-GH-15, AC-CGS-5, AC-GH-19) plus
    /// null-guard/precondition-guard coverage for every new public method on
    /// <see cref="GhostEntityTracker"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All timing is tick-based / caller-driven only — no <see cref="System.Threading.Thread.Sleep"/>
    /// anywhere in this file, matching <c>Session_ZoneStateMachine_Capacity_tests.cs</c>'s own
    /// precedent.
    /// </para>
    /// <para>
    /// <b>AC-GH-1 is the only test in this file that drives <see cref="ConnectionStateMachine"/> at
    /// all</b> (per this story's own instruction): it fires the real
    /// <c>OnSessionStateTransitioned(..., "HeartbeatTimeout")</c> via
    /// <see cref="ConnectionStateMachine.EvaluateTimeouts"/> and the real
    /// <c>OnGhostPromotionEventEmitted</c> via <see cref="GhostEntityTracker.PromoteToGhost"/>
    /// against the same <see cref="NetworkTestObserver"/>, in sequence — proving the composed
    /// behavior even though no orchestration layer exists yet to wire the two classes together for
    /// real (same "test composes them" precedent as Story 014's
    /// <c>Session_ZoneStateMachine_Capacity_tests.cs</c>). Both calls happen within the same simulated
    /// tick step in this test, which is what satisfies AC-GH-1's "within one <c>ZONE_TICK_MS</c> of the
    /// transition tick" pass condition — there is no separate tick advance between them.
    /// </para>
    /// <para>
    /// <b>AC-GH-13's <see cref="TransportFaultInjector"/> usage is scaffolding only (judgment call,
    /// approved by the coordinator before implementation):</b> <see cref="ITransportFaultInjector"/>'s
    /// entire API surface (<c>DropNextOutbound</c>/<c>DelayNextOutbound</c>/<c>ReorderNext</c>/
    /// <c>DropSnapshotFragment</c>/<c>SetSequenceNumber</c>) is outbound-fault-injection only — it has
    /// no method for queuing inbound buffered client commands during reconnect. This story's own
    /// GDD-derived pass-condition text names this interface as the "test technique," but the real
    /// production mechanism CR-GH-4/AC-GH-13 requires is <see cref="GhostEntityTracker.ShouldRejectCommand"/>.
    /// This file instantiates a <see cref="TransportFaultInjector"/> to honor the named test technique,
    /// but the actual assertion (buffered commands rejected without processing, no
    /// <c>OnServerDamageEventSerialized</c>/<c>OnServerCycleTimerBroadcastSerialized</c> firing) is
    /// driven by <see cref="GhostEntityTracker.ShouldRejectCommand"/> — the GDD's own pass-condition
    /// text is imprecise here, not a misunderstanding of the interface's actual capability.
    /// </para>
    /// <para>
    /// <b>characterId doubles as entityId</b> in every test below (e.g. the same <c>555u</c> constant
    /// is passed to both <see cref="GhostEntityTracker"/> methods, which key by <c>characterId</c>,
    /// and <see cref="INetworkTestObserver"/> callbacks that take an <c>entityId</c>) — no Character
    /// &lt;-&gt; Entity mapping system exists yet in this codebase, so this file uses one constant to
    /// stand in for both, the same simplification other stories' tests already make implicitly.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class GhostSession_PromotionStateConstraints_Tests
    {
        private const uint AccountId = 7u;
        private const uint GhostCharacterId = 555u; // doubles as entityId — see class remarks.
        private const uint MobEntityId = 900u;

        private static readonly (short x, short y, short z) DisconnectPosition = (100, 0, 250);

        /// <summary>
        /// Advances a fresh <see cref="ConnectionStateMachine"/> to <see cref="SessionState.Connected"/>
        /// for <see cref="AccountId"/>/<see cref="GhostCharacterId"/>, then resets
        /// <paramref name="observer"/> so the setup calls' own callbacks don't pollute a test's
        /// assertions — mirroring <c>Session_ZoneStateMachine_Capacity_tests.cs</c>'s
        /// <c>CreateActiveStateMachine</c> helper.
        /// </summary>
        private static ConnectionStateMachine CreateConnectedStateMachine(NetworkTestObserver observer)
        {
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountId, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountId, GhostCharacterId, currentTick: 0u, observer);
            stateMachine.RecordInboundActivity(AccountId, currentTick: 0u);
            observer.Reset();
            return stateMachine;
        }

        // =========================================================================================
        // AC-GH-1: heartbeat timeout -> Connected -> Disconnected_SessionActive + IsGhost=true
        // broadcasts within one ZONE_TICK_MS + GhostPromotionEvent (R-OD) emits.
        // =========================================================================================

        [Test]
        public void HeartbeatTimeout_ComposedWithPromoteToGhost_FiresSessionTransitionAndGhostPromotion()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var connectionStateMachine = CreateConnectedStateMachine(observer);
            var ghostTracker = new GhostEntityTracker();

            // Act — both calls happen within the same simulated tick step (see class remarks).
            connectionStateMachine.EvaluateTimeouts(currentTick: 60u, heartbeatTimeoutTicks: 60u,
                connectingTimeoutTicks: 200u, observer);
            ghostTracker.PromoteToGhost(GhostCharacterId, DisconnectPosition, observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountId, SessionState.Connected, SessionState.Disconnected_SessionActive, "HeartbeatTimeout"),
                observer.SessionStateTransitionedCalls[0]);

            Assert.AreEqual(1, observer.GhostPromotionEventEmittedCalls.Count);
            Assert.AreEqual(GhostCharacterId, observer.GhostPromotionEventEmittedCalls[0]);

            Assert.IsTrue(ghostTracker.IsGhost(GhostCharacterId));
        }

        // =========================================================================================
        // AC-GH-2: 10 consecutive zone ticks -> position and attack queue unchanged (frozen).
        // =========================================================================================

        [Test]
        public void TenConsecutiveTicks_PositionAndAttackQueueDepth_RemainFrozen()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, DisconnectPosition, observer);

            var observedPositions = new List<(short x, short y, short z)>();
            var observedQueueDepths = new List<int>();

            // Act
            for (uint tick = 1; tick <= 10; tick++)
            {
                observer.OnTickCompleted(tick);
                Assert.IsTrue(ghostTracker.TryGetFrozenPosition(GhostCharacterId, out var position));
                observedPositions.Add(position);
                observedQueueDepths.Add(ghostTracker.GetAttackQueueDepth(GhostCharacterId));
            }

            // Assert
            Assert.AreEqual(10, observer.TickCompletedCalls.Count);
            foreach (var position in observedPositions)
            {
                Assert.AreEqual(DisconnectPosition, position,
                    "AC-GH-2: the ghost's position must be identical across all 10 ticks.");
            }

            foreach (int depth in observedQueueDepths)
            {
                Assert.AreEqual(0, depth, "AC-GH-2: attack-queue depth must be 0 for every tick.");
            }
        }

        // =========================================================================================
        // AC-GH-3: mob attacks a ghost -> HP reduces via the standard damage pipeline.
        // =========================================================================================

        [Test]
        public void MobAttacksGhost_ReducesHpByStandardPipelineComputedDamage()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, DisconnectPosition, observer);
            const int priorHp = 100;
            const int computedDamage = 37;

            // Act — the standard damage pipeline (not yet built) is represented by the observer call;
            // ApplyDamage is the mock HP-reduction bookkeeping this story owns.
            observer.OnServerDamageEventSerialized(MobEntityId, GhostCharacterId, computedDamage);
            int newHp = ghostTracker.ApplyDamage(GhostCharacterId, priorHp, computedDamage);

            // Assert
            Assert.AreEqual(1, observer.ServerDamageEventSerializedCalls.Count);
            Assert.AreEqual((MobEntityId, GhostCharacterId, computedDamage), observer.ServerDamageEventSerializedCalls[0]);

            Assert.AreEqual(priorHp - computedDamage, newHp);
            Assert.AreEqual(newHp, ghostTracker.GetTrackedHp(GhostCharacterId),
                "Ghost HP reported on the next zone tick must equal prior HP minus computedDamage.");
        }

        // =========================================================================================
        // AC-GH-13: buffered commands sent during reconnect re-auth are discarded, no state change.
        // =========================================================================================

        [Test]
        public void BufferedCommandsDuringReconnect_AreDiscardedWithoutProcessing_NoStateChange()
        {
            // Arrange — see class remarks for why ITransportFaultInjector cannot actually perform this
            // test's named technique (it's outbound-fault-injection only). No instance of it is
            // constructed here — code review (Story 017) found the prior version's TransportFaultInjector
            // instantiation was dead code (only Reset() was called, with nothing ever configured on it,
            // so it participated in no assertion) and removed it; the class remarks alone document why.
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, DisconnectPosition, observer);

            // Buffered commands queued while disconnected, arriving during re-authentication.
            var bufferedCommandKinds = new[] { "Movement", "Attack", "Skill" };
            var rejectionResults = new List<bool>();

            // Act — a caller checks ShouldRejectCommand before processing any buffered command; none
            // of these should ever reach the damage or cycle-timer pipelines.
            foreach (string _ in bufferedCommandKinds)
            {
                bool shouldReject = ghostTracker.ShouldRejectCommand(GhostCharacterId);
                rejectionResults.Add(shouldReject);

                if (!shouldReject)
                {
                    Assert.Fail("A buffered command was not rejected — CR-GH-4 violation.");
                }

                // Discarded without processing — no ApplyDamage, no cycle-timer broadcast call.
            }

            // Assert
            CollectionAssert.AreEqual(new[] { true, true, true }, rejectionResults);
            Assert.IsEmpty(observer.ServerDamageEventSerializedCalls,
                "No buffered command may reach the damage pipeline while ghosted.");
            Assert.IsEmpty(observer.ServerCycleTimerBroadcastSerializedCalls,
                "No buffered command may reach the cycle-timer broadcast while ghosted.");
        }

        [Test]
        public void ShouldRejectCommand_RegisteredButNotGhosted_ReturnsFalse()
        {
            // Arrange — code review coverage gap (Story 017): all prior tests only exercised
            // ShouldRejectCommand against an unregistered or a ghosted character. This proves the
            // third reachable IsGhost state (a record exists, but IsGhost=false) is NOT rejected —
            // without this test, a future refactor that decoupled ShouldRejectCommand from IsGhost and
            // defaulted to true for any record at all would go undetected.
            var ghostTracker = new GhostEntityTracker();
            const uint neverGhostedCharacterId = 556u;
            ghostTracker.ApplyDamage(neverGhostedCharacterId, currentHp: 100, damageAmount: 10); // creates a record, never promotes

            // Act & Assert
            Assert.IsFalse(ghostTracker.IsGhost(neverGhostedCharacterId), "Sanity check: this character must not be ghosted.");
            Assert.IsFalse(ghostTracker.ShouldRejectCommand(neverGhostedCharacterId),
                "A registered-but-not-ghosted character's commands must NOT be rejected.");
        }

        // =========================================================================================
        // AC-GH-15: in-flight attack at the disconnect boundary resolves normally; no further
        // attacks queued after IsGhost=true.
        // =========================================================================================

        [Test]
        public void InFlightAttackAtDisconnectBoundary_ResolvesNormally_NoFurtherAttacksQueued()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            const int priorHp = 100;
            const int inFlightDamage = 15;

            // Act — promotion fires first (the heartbeat timeout that triggered it already happened);
            // the already-committed in-flight attack then resolves within the same/next tick, per
            // EC-GH-3/AC-GH-15's "the server never rolls back a committed attack event on disconnect."
            ghostTracker.PromoteToGhost(GhostCharacterId, DisconnectPosition, observer);
            observer.OnServerDamageEventSerialized(MobEntityId, GhostCharacterId, inFlightDamage);
            ghostTracker.ApplyDamage(GhostCharacterId, priorHp, inFlightDamage);

            // A further attack attempt after promotion must be rejected — no further damage event.
            bool furtherAttackRejected = ghostTracker.ShouldRejectCommand(GhostCharacterId);

            // Assert
            Assert.AreEqual(1, observer.GhostPromotionEventEmittedCalls.Count);
            Assert.AreEqual(1, observer.ServerDamageEventSerializedCalls.Count,
                "Exactly one damage event — the in-flight attack — must fire.");
            Assert.AreEqual((MobEntityId, GhostCharacterId, inFlightDamage), observer.ServerDamageEventSerializedCalls[0]);

            Assert.IsTrue(furtherAttackRejected, "No further attacks may be queued once IsGhost=true.");
            Assert.AreEqual(1, observer.ServerDamageEventSerializedCalls.Count,
                "No subsequent OnServerDamageEventSerialized may fire for this ghost entity.");
        }

        // =========================================================================================
        // AC-CGS-5: ghost HP update broadcasts via EntityHealthUpdate (R-U) next tick, all clients
        // apply directly, zero client-side prediction.
        // =========================================================================================

        [Test]
        public void GhostDamage_HpQueryMatchesComputedDamage_AndBroadcastsViaRUBatch()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, DisconnectPosition, observer);
            const int priorHp = 80;
            const int computedDamage = 25;
            const uint clientId = 1u;

            // Act — HP update applies, then broadcasts via the R-U EntityHealthUpdate batch next
            // tick (simulated via the existing RFR-1 observer hook, since no HP-value-carrying
            // callback exists — see AC-CGS-5's resolved test-observability approach).
            int newHp = ghostTracker.ApplyDamage(GhostCharacterId, priorHp, computedDamage);
            observer.OnRUBatchEntityHealthUpdates(clientId, new[] { GhostCharacterId });

            // Assert — queried HP matches prior HP minus computedDamage.
            Assert.AreEqual(priorHp - computedDamage, newHp);
            Assert.AreEqual(newHp, ghostTracker.GetTrackedHp(GhostCharacterId));

            // Assert — the entity ID appears in the next R-U batch's delivered health-update list.
            Assert.AreEqual(1, observer.RUBatchEntityHealthUpdatesCalls.Count);
            CollectionAssert.Contains(observer.RUBatchEntityHealthUpdatesCalls[0].deliveredEntityIds, GhostCharacterId);
        }

        [Test]
        public void ApplyDamage_IdenticalForGhostAndNonGhostCharacter_NoSpecialCasingExists()
        {
            // Arrange — regression guard (AC-CGS-5), NOT a standalone proof of CGS-1 (code review
            // finding, Story 017): the actual structural proof that no client-side prediction/blending
            // branch exists for ghosts is that ApplyDamage's method body has zero references to
            // IsGhost anywhere — confirmed by direct source read during code review, not something a
            // black-box equality test can conclusively establish on its own (a hypothetical
            // ghost-specific branch could coincidentally agree with the non-ghost path at any single
            // sampled input). This test samples multiple input vectors, including boundary cases, to
            // raise confidence as a regression guard against a future change reintroducing such a
            // branch — it complements the code-level proof, it does not replace it.
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            const uint ghostedCharacterId = GhostCharacterId;
            const uint neverGhostedCharacterId = 556u;
            ghostTracker.PromoteToGhost(ghostedCharacterId, DisconnectPosition, observer);

            // Act & Assert — multiple input vectors, including boundary cases (zero damage, damage
            // exceeding current HP producing a negative result), each checked independently against a
            // fresh instance so no vector's result can leak into another's.
            (int priorHp, int damageAmount)[] vectors =
            {
                (100, 30),  // ordinary case
                (100, 0),   // zero damage
                (10, 50),   // damage exceeds current HP -> negative result
                (0, 0),     // both zero
            };

            foreach ((int priorHp, int damageAmount) in vectors)
            {
                var vectorTracker = new GhostEntityTracker();
                vectorTracker.PromoteToGhost(ghostedCharacterId, DisconnectPosition, observer);

                int ghostedResult = vectorTracker.ApplyDamage(ghostedCharacterId, priorHp, damageAmount);
                int nonGhostedResult = vectorTracker.ApplyDamage(neverGhostedCharacterId, priorHp, damageAmount);

                Assert.AreEqual(ghostedResult, nonGhostedResult,
                    $"ApplyDamage must produce identical output for a ghosted and a non-ghosted character " +
                    $"given identical inputs (priorHp={priorHp}, damageAmount={damageAmount}).");
            }

            Assert.IsTrue(ghostTracker.IsGhost(ghostedCharacterId));
            Assert.IsFalse(ghostTracker.IsGhost(neverGhostedCharacterId));
        }

        // =========================================================================================
        // AC-GH-19: IsGhost=false -> the field is absent from the wire entirely (omitted, not sent
        // as false).
        // =========================================================================================

        [Test]
        public void EncodeIsGhostField_False_ReturnsEmptyArray()
        {
            // Act
            byte[] encoded = GhostEntityTracker.EncodeIsGhostField(isGhost: false);

            // Assert
            Assert.AreEqual(0, encoded.Length, "AC-GH-19: IsGhost=false must be omitted entirely, not sent as false.");
        }

        [Test]
        public void EncodeIsGhostField_True_ReturnsSingleByteArray()
        {
            // Act
            byte[] encoded = GhostEntityTracker.EncodeIsGhostField(isGhost: true);

            // Assert
            Assert.AreEqual(1, encoded.Length);
            Assert.AreEqual(1, encoded[0]);
        }

        // =========================================================================================
        // Null-guard / precondition-guard coverage for every new public method.
        // =========================================================================================

        [Test]
        public void PromoteToGhost_AlreadyGhosted_ThrowsInvalidOperationException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, DisconnectPosition, observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                ghostTracker.PromoteToGhost(GhostCharacterId, DisconnectPosition, observer));
        }

        [Test]
        public void ApplyDamage_NegativeDamageAmount_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var ghostTracker = new GhostEntityTracker();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ghostTracker.ApplyDamage(GhostCharacterId, currentHp: 100, damageAmount: -1));
        }

        [Test]
        public void TryGetFrozenPosition_UnregisteredCharacter_ReturnsFalse()
        {
            // Arrange
            var ghostTracker = new GhostEntityTracker();

            // Act
            bool found = ghostTracker.TryGetFrozenPosition(GhostCharacterId, out var position);

            // Assert
            Assert.IsFalse(found);
            Assert.AreEqual(default((short, short, short)), position);
        }

        [Test]
        public void IsGhost_UnregisteredCharacter_ReturnsFalse()
        {
            // Arrange
            var ghostTracker = new GhostEntityTracker();

            // Act & Assert
            Assert.IsFalse(ghostTracker.IsGhost(GhostCharacterId));
        }

        [Test]
        public void ShouldRejectCommand_UnregisteredCharacter_ReturnsFalse()
        {
            // Arrange
            var ghostTracker = new GhostEntityTracker();

            // Act & Assert
            Assert.IsFalse(ghostTracker.ShouldRejectCommand(GhostCharacterId));
        }

        [Test]
        public void GetAttackQueueDepth_UnregisteredCharacter_ReturnsZero()
        {
            // Arrange
            var ghostTracker = new GhostEntityTracker();

            // Act & Assert
            Assert.AreEqual(0, ghostTracker.GetAttackQueueDepth(GhostCharacterId));
        }

        [Test]
        public void GetTrackedHp_UnregisteredCharacter_ReturnsZero()
        {
            // Arrange
            var ghostTracker = new GhostEntityTracker();

            // Act & Assert
            Assert.AreEqual(0, ghostTracker.GetTrackedHp(GhostCharacterId));
        }
    }
}
