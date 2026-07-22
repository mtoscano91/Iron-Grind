using System;
using System.Collections.Generic;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit/integration tests for Networking Core Story 015 — the closing chapter of the
    /// Session Lifecycle cluster (012–015). Four independent concerns, each in its own
    /// <see cref="TestFixture"/> below: AC-NC-12 (<see cref="ConnectionStateMachine.CompleteSessionActiveTTLExpiry"/>,
    /// <see cref="Session_TTLExpiry_Tests"/>); AC-NC-16/AC-NC-27/AC-NC-34-CRASH
    /// (<see cref="EnhancementRequestDeduplicator"/>, <see cref="EnhancementRequestDeduplicator_Tests"/>);
    /// AC-NC-35/AC-NC-40 (<see cref="ZoneSnapshotReassemblyTracker"/>,
    /// <see cref="ZoneSnapshotReassemblyTracker_Tests"/>); AC-NC-42
    /// (<see cref="ConnectionStateMachine_InFlightRpcAuthorityBoundary_Tests"/>, which adds no new
    /// production code — see that fixture's own remarks for why).
    /// </summary>
    /// <remarks>
    /// All timing is tick-based / caller-driven only — no <see cref="System.Threading.Thread.Sleep"/>
    /// anywhere in this file, matching every prior Networking Core story's test-file precedent.
    /// </remarks>
    [TestFixture]
    internal sealed class Session_TTLExpiry_Tests
    {
        private const uint AccountA = 7u;
        private const uint CharacterA = 555u;
        private const uint SessionExpiryTick = 6100u;

        /// <summary>Advances a fresh state machine's account to <see cref="SessionState.Disconnected_SessionActive"/> at tick 1.</summary>
        private static ConnectionStateMachine CreateDisconnectedSessionActiveStateMachine(NetworkTestObserver observer)
        {
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountA, CharacterA, currentTick: 0u, observer);
            stateMachine.EvaluateTimeouts(currentTick: 1u, heartbeatTimeoutTicks: 1u, connectingTimeoutTicks: 1000u, observer);
            observer.Reset(); // isolate each test's assertions from the setup calls above
            return stateMachine;
        }

        // =========================================================================================
        // AC-NC-12: tick counter reaches sessionExpiryTick while Disconnected_SessionActive.
        // =========================================================================================

        [Test]
        public void CompleteSessionActiveTTLExpiry_TickReachesExpiry_RunsFullSequenceInOrderAndTransitionsToExpired()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);
            var callOrder = new List<string>();

            // Act
            stateMachine.CompleteSessionActiveTTLExpiry(AccountA, currentTick: SessionExpiryTick, sessionExpiryTick: SessionExpiryTick,
                persistFinalCharacterState: characterId =>
                {
                    callOrder.Add("persist");
                    Assert.AreEqual(CharacterA, characterId);
                    Assert.IsEmpty(observer.PersistenceWriteCompletedCalls,
                        "OnPersistenceWriteCompleted must not have fired yet when persistence runs.");
                },
                removeEntityFromZone: characterId =>
                {
                    callOrder.Add("removeEntity");
                    Assert.AreEqual(CharacterA, characterId);
                    Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count,
                        "AC-NC-12: OnPersistenceWriteCompleted must fire before any resource release, including entity removal.");
                },
                broadcastPlayerLeftZone: (characterId, type) =>
                {
                    callOrder.Add("broadcast");
                    Assert.AreEqual(CharacterA, characterId);
                    Assert.AreEqual(DisconnectType.Timeout, type);
                    Assert.IsTrue(stateMachine.IsAccountRegistered(AccountA),
                        "Registry release (the final session resource) must not happen before the broadcast.");
                },
                observer);

            // Assert — full ordering.
            CollectionAssert.AreEqual(new[] { "persist", "removeEntity", "broadcast" }, callOrder);

            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((CharacterA, PersistenceWriteReason.SessionExpiry), observer.PersistenceWriteCompletedCalls[0]);

            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountA, SessionState.Disconnected_SessionActive, SessionState.Disconnected_SessionExpired, "TTLExpired"),
                observer.SessionStateTransitionedCalls[0]);

            Assert.IsFalse(stateMachine.IsAccountRegistered(AccountA), "Session resources must be released once the sequence completes.");
        }

        [Test]
        public void CompleteSessionActiveTTLExpiry_SubsequentConnectionFromSameAccountStartsAtConnecting()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);
            stateMachine.CompleteSessionActiveTTLExpiry(AccountA, currentTick: SessionExpiryTick, sessionExpiryTick: SessionExpiryTick,
                persistFinalCharacterState: _ => { },
                removeEntityFromZone: _ => { },
                broadcastPlayerLeftZone: (_, _) => { },
                observer);
            observer.Reset();

            // Act — a subsequent connection from the same account.
            stateMachine.EnterConnecting(AccountA, currentTick: SessionExpiryTick + 1u, observer);

            // Assert
            Assert.IsTrue(stateMachine.TryGetSessionState(AccountA, out SessionState state));
            Assert.AreEqual(SessionState.Connecting, state);
        }

        [Test]
        public void CompleteSessionActiveTTLExpiry_TickNotYetExpired_NoOp()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);

            // Act
            stateMachine.CompleteSessionActiveTTLExpiry(AccountA, currentTick: SessionExpiryTick - 100u, sessionExpiryTick: SessionExpiryTick,
                persistFinalCharacterState: _ => Assert.Fail("Must not persist before expiry."),
                removeEntityFromZone: _ => Assert.Fail("Must not remove the entity before expiry."),
                broadcastPlayerLeftZone: (_, _) => Assert.Fail("Must not broadcast before expiry."),
                observer);

            // Assert
            Assert.IsEmpty(observer.PersistenceWriteCompletedCalls);
            Assert.IsEmpty(observer.SessionStateTransitionedCalls);
            Assert.IsTrue(stateMachine.IsAccountRegistered(AccountA));
            Assert.IsTrue(stateMachine.TryGetSessionState(AccountA, out SessionState state));
            Assert.AreEqual(SessionState.Disconnected_SessionActive, state);
        }

        [Test]
        public void CompleteSessionActiveTTLExpiry_TickExactlyAtExpiry_Expires()
        {
            // Equality = expired, per StaleDiscardComparer.IsTickExpired's own documented contract.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);

            stateMachine.CompleteSessionActiveTTLExpiry(AccountA, currentTick: SessionExpiryTick, sessionExpiryTick: SessionExpiryTick,
                persistFinalCharacterState: _ => { },
                removeEntityFromZone: _ => { },
                broadcastPlayerLeftZone: (_, _) => { },
                observer);

            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
        }

        [Test]
        public void CompleteSessionActiveTTLExpiry_AccountNotDisconnectedSessionActive_Throws()
        {
            // Arrange — a Connecting (not yet Disconnected_SessionActive) account.
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteSessionActiveTTLExpiry(AccountA, currentTick: SessionExpiryTick, sessionExpiryTick: SessionExpiryTick,
                    persistFinalCharacterState: _ => { },
                    removeEntityFromZone: _ => { },
                    broadcastPlayerLeftZone: (_, _) => { },
                    observer));
        }

        [Test]
        public void CompleteSessionActiveTTLExpiry_AccountReconnecting_Throws()
        {
            // Arrange — code review coverage gap (Story 015): only the Connecting wrong-state case was
            // previously tested. Reconnecting is named alongside Disconnected_SessionActive in this
            // story's own control-manifest rule ("no game-logic RPC processed in Disconnected_SessionActive,
            // Reconnecting, or Disconnected_SessionExpired"), and a caller could plausibly invoke this
            // method against an account that has since bounced into Reconnecting — this must throw, not
            // silently misbehave.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);
            stateMachine.EnterReconnecting(AccountA, SessionExpiryTick, observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteSessionActiveTTLExpiry(AccountA, currentTick: SessionExpiryTick, sessionExpiryTick: SessionExpiryTick,
                    persistFinalCharacterState: _ => { },
                    removeEntityFromZone: _ => { },
                    broadcastPlayerLeftZone: (_, _) => { },
                    observer));
        }

        [Test]
        public void CompleteSessionActiveTTLExpiry_NullPersistDelegate_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteSessionActiveTTLExpiry(AccountA, currentTick: SessionExpiryTick, sessionExpiryTick: SessionExpiryTick,
                    persistFinalCharacterState: null,
                    removeEntityFromZone: _ => { },
                    broadcastPlayerLeftZone: (_, _) => { },
                    observer));
        }

        [Test]
        public void CompleteSessionActiveTTLExpiry_NullRemoveEntityDelegate_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteSessionActiveTTLExpiry(AccountA, currentTick: SessionExpiryTick, sessionExpiryTick: SessionExpiryTick,
                    persistFinalCharacterState: _ => { },
                    removeEntityFromZone: null,
                    broadcastPlayerLeftZone: (_, _) => { },
                    observer));
        }

        [Test]
        public void CompleteSessionActiveTTLExpiry_NullBroadcastDelegate_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteSessionActiveTTLExpiry(AccountA, currentTick: SessionExpiryTick, sessionExpiryTick: SessionExpiryTick,
                    persistFinalCharacterState: _ => { },
                    removeEntityFromZone: _ => { },
                    broadcastPlayerLeftZone: null,
                    observer));
        }
    }

    /// <summary>
    /// EditMode tests for <see cref="EnhancementRequestDeduplicator"/> — AC-NC-16, AC-NC-27,
    /// AC-NC-34-CRASH.
    /// </summary>
    [TestFixture]
    internal sealed class EnhancementRequestDeduplicator_Tests
    {
        private sealed class MockOutcome
        {
            public uint ItemId;
            public bool Success;
            public byte NewEnhancementLevel;
        }

        // =========================================================================================
        // Fresh request.
        // =========================================================================================

        [Test]
        public void TryProcess_FreshRequest_Commits()
        {
            // Arrange
            var dedup = new EnhancementRequestDeduplicator();
            bool broadcastFired = false;

            // Act
            EnhancementRequestDedupResult result = dedup.TryProcess(requestId: 42u,
                computeOutcome: () => new MockOutcome { ItemId = 99u, Success = true, NewEnhancementLevel = 6 },
                persistOutcomeAndRequestId: (outcome, reqId) => true,
                emitOutcomeBroadcast: outcome => broadcastFired = true,
                outcome: out MockOutcome outcome);

            // Assert
            Assert.AreEqual(EnhancementRequestDedupResult.Committed, result);
            Assert.IsNotNull(outcome);
            Assert.IsTrue(broadcastFired);
            Assert.AreEqual(42u, dedup.LastEnhancementRequestId);
        }

        // =========================================================================================
        // EC-NET-9: dedup scope is per-character — two independent instances (characters) never
        // interfere with each other's LastEnhancementRequestID.
        // =========================================================================================

        [Test]
        public void TryProcess_TwoIndependentCharacterInstances_SameRequestIdOnBothCommitsIndependently()
        {
            // Arrange — code review coverage gap (Story 015): the class's own documented guarantee
            // ("dedup scope is per-character") had zero test evidence. No shared/static state exists in
            // the class, but this proves it, rather than relying on visual inspection alone.
            var characterADedup = new EnhancementRequestDeduplicator();
            var characterBDedup = new EnhancementRequestDeduplicator();
            bool characterABroadcastFired = false;
            bool characterBBroadcastFired = false;

            // Act — both characters submit the SAME requestId=42, independently.
            EnhancementRequestDedupResult resultA = characterADedup.TryProcess(requestId: 42u,
                computeOutcome: () => new MockOutcome { ItemId = 1u, Success = true, NewEnhancementLevel = 1 },
                persistOutcomeAndRequestId: (outcome, reqId) => true,
                emitOutcomeBroadcast: outcome => characterABroadcastFired = true,
                outcome: out MockOutcome _);

            EnhancementRequestDedupResult resultB = characterBDedup.TryProcess(requestId: 42u,
                computeOutcome: () => new MockOutcome { ItemId = 2u, Success = true, NewEnhancementLevel = 1 },
                persistOutcomeAndRequestId: (outcome, reqId) => true,
                emitOutcomeBroadcast: outcome => characterBBroadcastFired = true,
                outcome: out MockOutcome _);

            // Assert — both commit independently; neither sees the other's requestId as a duplicate.
            Assert.AreEqual(EnhancementRequestDedupResult.Committed, resultA);
            Assert.AreEqual(EnhancementRequestDedupResult.Committed, resultB);
            Assert.IsTrue(characterABroadcastFired);
            Assert.IsTrue(characterBBroadcastFired);
            Assert.AreEqual(42u, characterADedup.LastEnhancementRequestId);
            Assert.AreEqual(42u, characterBDedup.LastEnhancementRequestId);
        }

        // =========================================================================================
        // AC-NC-27: duplicate RequestID rejected unconditionally, no time window.
        // =========================================================================================

        [Test]
        public void TryProcess_DuplicateRequestIdEvenAfterLargeElapsedTickDelta_RejectedUnconditionally_AC_NC_27()
        {
            // Arrange — first request commits.
            var dedup = new EnhancementRequestDeduplicator();
            dedup.TryProcess(requestId: 42u,
                computeOutcome: () => new MockOutcome { ItemId = 99u, Success = true, NewEnhancementLevel = 6 },
                persistOutcomeAndRequestId: (outcome, reqId) => true,
                emitOutcomeBroadcast: outcome => { },
                outcome: out MockOutcome _);

            // Act — resubmit the same RequestID. AC-NC-27 requires rejection "even when >30 seconds
            // have elapsed": TryProcess has no tick/wall-clock parameter anywhere on its signature, so
            // there is structurally no argument a caller could pass to represent "30 seconds elapsed"
            // that would let the duplicate through — the delegates below throw if the dedup check ever
            // failed to short-circuit, proving no computation/persistence/broadcast occurs regardless.
            EnhancementRequestDedupResult result = dedup.TryProcess(requestId: 42u,
                computeOutcome: () => throw new InvalidOperationException("Must not compute for a duplicate."),
                persistOutcomeAndRequestId: (outcome, reqId) => throw new InvalidOperationException("Must not persist for a duplicate."),
                emitOutcomeBroadcast: outcome => throw new InvalidOperationException("Must not broadcast for a duplicate."),
                outcome: out MockOutcome outcome);

            // Assert
            Assert.AreEqual(EnhancementRequestDedupResult.RejectedDuplicate, result);
            Assert.IsNull(outcome);
        }

        // =========================================================================================
        // AC-NC-16 / AC-NC-34-CRASH: crash after persistence write, before broadcast.
        // =========================================================================================

        /// <summary>
        /// Shared crash scenario for both AC-NC-16 and AC-NC-34-CRASH: an enhancement request is
        /// processed, the mock persistence delegate durably records the outcome AND the new
        /// <c>LastEnhancementRequestID</c> in a test-owned mock store, then
        /// <see cref="IServerCrashInjector.RegisterCrashAt"/>'s registered <see cref="CrashStep.AfterPersistenceWrite"/>
        /// step fires synchronously — modeling the server process dying right after the durable write
        /// completes, before <see cref="EnhancementRequestDeduplicator.TryProcess{TOutcome}"/> can reach
        /// the broadcast step. "Reconnect" is modeled by constructing a fresh
        /// <see cref="EnhancementRequestDeduplicator"/> seeded from the mock store's now-durable value.
        /// </summary>
        private static (EnhancementRequestDeduplicator reconnectedDedup, MockOutcome mockStoreOutcome, bool originalBroadcastFired)
            RunCrashAfterPersistenceWriteBeforeBroadcastScenario(uint requestId)
        {
            var crashInjector = new ServerCrashInjector();
            crashInjector.RegisterCrashAt(CrashStep.AfterPersistenceWrite);

            var dedup = new EnhancementRequestDeduplicator();
            MockOutcome mockStoreOutcome = null;
            uint? mockStoreLastRequestId = null;
            bool broadcastFired = false;

            dedup.TryProcess(requestId: requestId,
                computeOutcome: () => new MockOutcome { ItemId = 99u, Success = true, NewEnhancementLevel = 6 },
                persistOutcomeAndRequestId: (outcome, reqId) =>
                {
                    // The durable write itself completes regardless of the crash simulated below —
                    // this is the whole point: the persistence layer's own write already succeeded.
                    mockStoreOutcome = outcome;
                    mockStoreLastRequestId = reqId;

                    bool crashed = false;
                    crashInjector.SignalStepReached(CrashStep.AfterPersistenceWrite, onCrash: () => crashed = true);

                    return !crashed; // the process died before TryProcess could proceed to broadcast
                },
                emitOutcomeBroadcast: outcome => broadcastFired = true,
                outcome: out MockOutcome _);

            var reconnectedDedup = new EnhancementRequestDeduplicator(persistedLastEnhancementRequestId: mockStoreLastRequestId);
            return (reconnectedDedup, mockStoreOutcome, broadcastFired);
        }

        [Test]
        public void TryProcess_CrashAfterPersistenceWriteBeforeBroadcast_ReconnectDeliversPostOutcomeStateWithoutAnimationReplay_AC_NC_16()
        {
            // Act
            (EnhancementRequestDeduplicator reconnectedDedup, MockOutcome mockStoreOutcome, bool broadcastFired) =
                RunCrashAfterPersistenceWriteBeforeBroadcastScenario(requestId: 42u);

            // Assert — the session handshake would deliver the post-outcome item state...
            Assert.IsNotNull(mockStoreOutcome);
            Assert.AreEqual(99u, mockStoreOutcome.ItemId);
            Assert.AreEqual(6, mockStoreOutcome.NewEnhancementLevel);

            // ...without replaying the animation: the original broadcast (the animation trigger) never fired.
            Assert.IsFalse(broadcastFired, "No outcome broadcast/animation may fire once the crash interrupts the sequence.");

            // A fresh instance (post-restart) reflects the durably-committed dedup state.
            Assert.AreEqual(42u, reconnectedDedup.LastEnhancementRequestId);
        }

        [Test]
        public void TryProcess_CrashAfterPersistenceWriteBeforeBroadcast_ResubmitRejectedAndItemStateUnchanged_AC_NC_34_CRASH()
        {
            // Arrange
            (EnhancementRequestDeduplicator reconnectedDedup, MockOutcome mockStoreOutcome, bool _) =
                RunCrashAfterPersistenceWriteBeforeBroadcastScenario(requestId: 42u);

            // Assert (a) — handshake delivers post-enhancement item state (proven via mockStoreOutcome).
            Assert.IsNotNull(mockStoreOutcome);

            // Act (b) — a re-submitted RequestID=X against the reconnected instance.
            EnhancementRequestDedupResult resubmitResult = reconnectedDedup.TryProcess(requestId: 42u,
                computeOutcome: () => throw new InvalidOperationException("Must not compute for a duplicate."),
                persistOutcomeAndRequestId: (outcome, reqId) => throw new InvalidOperationException("Must not persist for a duplicate."),
                emitOutcomeBroadcast: outcome => throw new InvalidOperationException("Must not broadcast for a duplicate."),
                outcome: out MockOutcome _);

            // Assert (b)
            Assert.AreEqual(EnhancementRequestDedupResult.RejectedDuplicate, resubmitResult);

            // Assert (c) — item state is unchanged by the rejected re-submit.
            Assert.AreEqual(99u, mockStoreOutcome.ItemId);
            Assert.AreEqual(6, mockStoreOutcome.NewEnhancementLevel);
        }

        // =========================================================================================
        // Null-guard tests.
        // =========================================================================================

        [Test]
        public void TryProcess_NullComputeOutcome_ThrowsArgumentNullException()
        {
            var dedup = new EnhancementRequestDeduplicator();

            Assert.Throws<ArgumentNullException>(() =>
                dedup.TryProcess<MockOutcome>(requestId: 1u,
                    computeOutcome: null,
                    persistOutcomeAndRequestId: (outcome, reqId) => true,
                    emitOutcomeBroadcast: outcome => { },
                    outcome: out _));
        }

        [Test]
        public void TryProcess_NullPersistDelegate_ThrowsArgumentNullException()
        {
            var dedup = new EnhancementRequestDeduplicator();

            Assert.Throws<ArgumentNullException>(() =>
                dedup.TryProcess(requestId: 1u,
                    computeOutcome: () => new MockOutcome(),
                    persistOutcomeAndRequestId: null,
                    emitOutcomeBroadcast: outcome => { },
                    outcome: out _));
        }

        [Test]
        public void TryProcess_NullBroadcastDelegate_ThrowsArgumentNullException()
        {
            var dedup = new EnhancementRequestDeduplicator();

            Assert.Throws<ArgumentNullException>(() =>
                dedup.TryProcess(requestId: 1u,
                    computeOutcome: () => new MockOutcome(),
                    persistOutcomeAndRequestId: (outcome, reqId) => true,
                    emitOutcomeBroadcast: null,
                    outcome: out _));
        }

        [Test]
        public void TryProcess_PersistenceWriteReturnsFalse_NoCrashScenario_ReturnsNotCommittedAndDoesNotBroadcast()
        {
            // A genuine (non-crash) persistence failure must behave identically to the crash path — no
            // broadcast, and this instance does not mirror the requestId as processed.
            var dedup = new EnhancementRequestDeduplicator();
            bool broadcastFired = false;

            EnhancementRequestDedupResult result = dedup.TryProcess(requestId: 1u,
                computeOutcome: () => new MockOutcome(),
                persistOutcomeAndRequestId: (outcome, reqId) => false,
                emitOutcomeBroadcast: outcome => broadcastFired = true,
                outcome: out MockOutcome _);

            Assert.AreEqual(EnhancementRequestDedupResult.NotCommitted, result);
            Assert.IsFalse(broadcastFired);
            Assert.IsNull(dedup.LastEnhancementRequestId);
        }
    }

    /// <summary>
    /// EditMode tests for <see cref="ZoneSnapshotReassemblyTracker"/> — AC-NC-35, AC-NC-40.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneSnapshotReassemblyTracker_Tests
    {
        private const uint ClientA = 7u;
        private const uint CharacterA = 555u;
        private const uint ReassemblyTimeoutTicks = 200u; // 10s @ 20Hz, matching FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS's default

        // =========================================================================================
        // AC-NC-35: last fragment dropped -> retransmit at timeout -> gate stays closed -> gate opens
        // within 100ms (same tick, no delay) of successful reassembly.
        // =========================================================================================

        [Test]
        public void EvaluateReassemblyTimeout_LastFragmentDropped_EmitsRetransmitAttemptAndGateStaysClosed_AC_NC_35a_AC_NC_35b()
        {
            // Arrange — simulate ITransportFaultInjector.DropSnapshotFragment(ushort.MaxValue) having
            // dropped the last fragment of the zone-entry snapshot. No real transport send path exists
            // yet in this codebase (see ZoneSnapshotReassemblyTracker remarks), so this sanity check
            // only grounds the scenario in the fault injector's own documented contract — it is not
            // wired to this tracker's production code.
            var faultInjector = new TransportFaultInjector();
            faultInjector.DropSnapshotFragment(ushort.MaxValue);
            Assert.IsTrue(faultInjector.TryConsumeSnapshotFragmentDrop(fragmentIndex: 4, isLastFragment: true),
                "Sanity check grounding this scenario in ITransportFaultInjector's own documented 'drop the last fragment' contract.");

            var observer = new NetworkTestObserver();
            var tracker = new ZoneSnapshotReassemblyTracker();
            tracker.BeginReassembly(ClientA, CharacterA, firstFragmentTick: 100u);

            // Act — FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS elapses after fragment 1 arrived.
            tracker.EvaluateReassemblyTimeout(ClientA, currentTick: 100u + ReassemblyTimeoutTicks, ReassemblyTimeoutTicks, observer);

            // Assert (a): the client emits a retransmit request.
            Assert.AreEqual(1, observer.SnapshotRetransmitAttemptCalls.Count);
            Assert.AreEqual((CharacterA, 1, ZoneSnapshotReassemblyTracker.MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS),
                observer.SnapshotRetransmitAttemptCalls[0]);

            // Assert (b): the zone-entry gate remains closed.
            Assert.IsFalse(tracker.IsGateOpen(ClientA));
            Assert.IsEmpty(observer.ZoneGateOpenedCalls);
        }

        [Test]
        public void CompleteReassembly_AfterServerResendsAndClientReassembles_OpensGateImmediately_AC_NC_35c()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var tracker = new ZoneSnapshotReassemblyTracker();
            tracker.BeginReassembly(ClientA, CharacterA, firstFragmentTick: 100u);
            tracker.EvaluateReassemblyTimeout(ClientA, currentTick: 100u + ReassemblyTimeoutTicks, ReassemblyTimeoutTicks, observer);
            observer.Reset();

            // Act — the server re-sends and the client reassembles successfully.
            tracker.CompleteReassembly(ClientA, observer);

            // Assert — gate opens with no simulated delay: the 100ms bound is satisfied by construction
            // (no wall-clock wait occurs between reassembly completion and this call).
            Assert.AreEqual(1, observer.ZoneGateOpenedCalls.Count);
            Assert.AreEqual(ClientA, observer.ZoneGateOpenedCalls[0]);
            Assert.IsTrue(tracker.IsGateOpen(ClientA));
        }

        // =========================================================================================
        // AC-NC-40: 3 consecutive failed reassembly cycles -> 3 OnSnapshotRetransmitAttempt calls in
        // order -> gate never opens -> client gives up (no further reassembly attempted).
        // =========================================================================================

        [Test]
        public void EvaluateReassemblyTimeout_ThreeConsecutiveDropCycles_FiresThreeAttemptsInOrderThenExhausts_AC_NC_40()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var tracker = new ZoneSnapshotReassemblyTracker();
            tracker.BeginReassembly(ClientA, CharacterA, firstFragmentTick: 0u);

            // Act — 3 consecutive timeout cycles, each representing a dropped retransmitted fragment
            // (ITransportFaultInjector.DropSnapshotFragment(N-1) on each of 3 consecutive cycles).
            uint tick = ReassemblyTimeoutTicks;
            for (int i = 0; i < 3; i++)
            {
                tracker.EvaluateReassemblyTimeout(ClientA, currentTick: tick, ReassemblyTimeoutTicks, observer);
                tick += ReassemblyTimeoutTicks;
            }

            // Assert — 3 calls, in order, with correct (attemptNumber, maxAttempts).
            Assert.AreEqual(3, observer.SnapshotRetransmitAttemptCalls.Count);
            Assert.AreEqual((CharacterA, 1, 3), observer.SnapshotRetransmitAttemptCalls[0]);
            Assert.AreEqual((CharacterA, 2, 3), observer.SnapshotRetransmitAttemptCalls[1]);
            Assert.AreEqual((CharacterA, 3, 3), observer.SnapshotRetransmitAttemptCalls[2]);

            // Assert — the gate never opened, and the client has given up.
            Assert.IsFalse(tracker.IsGateOpen(ClientA));
            Assert.IsEmpty(observer.ZoneGateOpenedCalls);
            Assert.IsTrue(tracker.HasExhaustedRetransmitAttempts(ClientA));
        }

        [Test]
        public void EvaluateReassemblyTimeout_AfterExhausted_FurtherCallsAreNoOp_NoFurtherReassemblyAttempted()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var tracker = new ZoneSnapshotReassemblyTracker();
            tracker.BeginReassembly(ClientA, CharacterA, firstFragmentTick: 0u);
            uint tick = ReassemblyTimeoutTicks;
            for (int i = 0; i < ZoneSnapshotReassemblyTracker.MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS; i++)
            {
                tracker.EvaluateReassemblyTimeout(ClientA, currentTick: tick, ReassemblyTimeoutTicks, observer);
                tick += ReassemblyTimeoutTicks;
            }
            observer.Reset();

            // Act — a further evaluation after exhaustion.
            tracker.EvaluateReassemblyTimeout(ClientA, currentTick: tick, ReassemblyTimeoutTicks, observer);

            // Assert — no further attempt recorded; the count stays pinned at the max.
            Assert.IsEmpty(observer.SnapshotRetransmitAttemptCalls);
            Assert.AreEqual(ZoneSnapshotReassemblyTracker.MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS, tracker.GetRetransmitAttemptCount(ClientA));
        }

        // =========================================================================================
        // Precondition/null-guard tests.
        // =========================================================================================

        [Test]
        public void EvaluateReassemblyTimeout_ClientNotTracked_Throws()
        {
            var tracker = new ZoneSnapshotReassemblyTracker();

            Assert.Throws<InvalidOperationException>(() =>
                tracker.EvaluateReassemblyTimeout(ClientA, currentTick: 100u, reassemblyTimeoutTicks: 10u));
        }

        [Test]
        public void EvaluateReassemblyTimeout_ZeroTimeoutTicks_ThrowsArgumentOutOfRangeException()
        {
            var tracker = new ZoneSnapshotReassemblyTracker();
            tracker.BeginReassembly(ClientA, CharacterA, firstFragmentTick: 0u);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                tracker.EvaluateReassemblyTimeout(ClientA, currentTick: 100u, reassemblyTimeoutTicks: 0u));
        }

        [Test]
        public void EvaluateReassemblyTimeout_GateAlreadyOpen_NoOp()
        {
            var observer = new NetworkTestObserver();
            var tracker = new ZoneSnapshotReassemblyTracker();
            tracker.BeginReassembly(ClientA, CharacterA, firstFragmentTick: 0u);
            tracker.CompleteReassembly(ClientA, observer);
            observer.Reset();

            tracker.EvaluateReassemblyTimeout(ClientA, currentTick: 10000u, reassemblyTimeoutTicks: 1u, observer);

            Assert.IsEmpty(observer.SnapshotRetransmitAttemptCalls);
        }

        [Test]
        public void CompleteReassembly_ClientNotTracked_Throws()
        {
            var tracker = new ZoneSnapshotReassemblyTracker();

            Assert.Throws<InvalidOperationException>(() => tracker.CompleteReassembly(ClientA));
        }

        [Test]
        public void CompleteReassembly_AfterExhausted_Throws()
        {
            var observer = new NetworkTestObserver();
            var tracker = new ZoneSnapshotReassemblyTracker();
            tracker.BeginReassembly(ClientA, CharacterA, firstFragmentTick: 0u);
            uint tick = ReassemblyTimeoutTicks;
            for (int i = 0; i < ZoneSnapshotReassemblyTracker.MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS; i++)
            {
                tracker.EvaluateReassemblyTimeout(ClientA, currentTick: tick, ReassemblyTimeoutTicks, observer);
                tick += ReassemblyTimeoutTicks;
            }

            Assert.Throws<InvalidOperationException>(() => tracker.CompleteReassembly(ClientA, observer));
        }

        [Test]
        public void BeginReassembly_CalledTwice_OverwritesPriorEntryWithCleanSlate()
        {
            var observer = new NetworkTestObserver();
            var tracker = new ZoneSnapshotReassemblyTracker();
            tracker.BeginReassembly(ClientA, CharacterA, firstFragmentTick: 0u);
            tracker.EvaluateReassemblyTimeout(ClientA, currentTick: ReassemblyTimeoutTicks, ReassemblyTimeoutTicks, observer);

            // Act — a fresh zone-entry attempt (e.g. after a reconnect) begins again.
            tracker.BeginReassembly(ClientA, CharacterA, firstFragmentTick: 5000u);

            // Assert — clean slate.
            Assert.AreEqual(0, tracker.GetRetransmitAttemptCount(ClientA));
            Assert.IsFalse(tracker.IsGateOpen(ClientA));
            Assert.IsFalse(tracker.HasExhaustedRetransmitAttempts(ClientA));
        }

        [Test]
        public void IsTracked_UnregisteredThenRegistered_ReflectsRegistryState()
        {
            var tracker = new ZoneSnapshotReassemblyTracker();

            Assert.IsFalse(tracker.IsTracked(ClientA));

            tracker.BeginReassembly(ClientA, CharacterA, firstFragmentTick: 0u);

            Assert.IsTrue(tracker.IsTracked(ClientA));
        }

        [Test]
        public void IsGateOpen_UnregisteredClient_ReturnsFalse()
        {
            var tracker = new ZoneSnapshotReassemblyTracker();

            Assert.IsFalse(tracker.IsGateOpen(ClientA));
        }

        [Test]
        public void HasExhaustedRetransmitAttempts_UnregisteredClient_ReturnsFalse()
        {
            var tracker = new ZoneSnapshotReassemblyTracker();

            Assert.IsFalse(tracker.HasExhaustedRetransmitAttempts(ClientA));
        }

        [Test]
        public void GetRetransmitAttemptCount_UnregisteredClient_ReturnsZero()
        {
            var tracker = new ZoneSnapshotReassemblyTracker();

            Assert.AreEqual(0, tracker.GetRetransmitAttemptCount(ClientA));
        }
    }

    /// <summary>
    /// EditMode tests for AC-NC-42 (in-flight RPC at the disconnect boundary, EC-NET-6). Both legal
    /// orderings are driven directly against <see cref="ConnectionStateMachine"/>'s existing public API
    /// (<see cref="ConnectionStateMachine.TryGetSessionState"/> + <see cref="ConnectionStateMachine.EvaluateTimeouts"/>)
    /// — no new production code was added for this AC.
    /// </summary>
    /// <remarks>
    /// <b>Judgment call (documented per the story's own request):</b>
    /// <see cref="ConnectionStateMachine"/>'s state-machine methods are already synchronous and its
    /// registry is a simple in-process <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>
    /// with no concurrency, so "the transition IS the authority boundary" (EC-NET-6) is already true by
    /// construction as long as a caller checks current state before applying an RPC's effect — exactly
    /// what <see cref="ConnectionStateMachine.TryGetSessionState"/> already allows a caller to do. Adding
    /// a thin <c>TryApplyIfConnected(accountId, Action)</c> wrapper around that existing check would not
    /// prove anything <see cref="ConnectionStateMachine.TryGetSessionState"/> doesn't already prove; it
    /// would only move the same one-line guard into production code that has no other caller yet (no
    /// real RPC-dispatch layer exists anywhere in this codebase). Per the story's own guidance ("this may
    /// need little or no new production code... do not invent new production infrastructure beyond what's
    /// needed"), this fixture proves the guarantee without adding that wrapper.
    /// </remarks>
    [TestFixture]
    internal sealed class ConnectionStateMachine_InFlightRpcAuthorityBoundary_Tests
    {
        private const uint AccountA = 7u;
        private const uint CharacterA = 555u;

        /// <summary>
        /// A minimal mock of "persisted character state" — just enough to prove no partial-application
        /// state is ever observable (AC-NC-42 / EC-NET-6). Not a real persistence layer.
        /// </summary>
        private sealed class MockPersistedCharacterState
        {
            public int HeldFreePoints = 1;
            public bool StatApplied;
        }

        [TestCase(true, TestName = "EitherOrdering_NeverProducesPartialApplicationState(RpcProcessedFirst)")]
        [TestCase(false, TestName = "EitherOrdering_NeverProducesPartialApplicationState(HeartbeatTimeoutEvaluatedFirst)")]
        public void EitherOrdering_NeverProducesPartialApplicationState(bool rpcProcessedFirst)
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountA, CharacterA, currentTick: 0u, observer);
            var persisted = new MockPersistedCharacterState(); // heldFreePoints = 1, statApplied = false

            void ProcessRpcIfConnected()
            {
                // The authority-boundary guard every real RPC handler must apply (EC-NET-6): check
                // current state before applying any effect. TryGetSessionState is the existing public
                // API this guard is built on — see fixture remarks for why no new production method
                // was added for this AC.
                if (stateMachine.TryGetSessionState(AccountA, out SessionState state) && state == SessionState.Connected)
                {
                    persisted.HeldFreePoints = 0;
                    persisted.StatApplied = true;
                }
                // else: dropped — persisted is left untouched.
            }

            // Act
            if (rpcProcessedFirst)
            {
                // Ordering (a): the AllocateFreePointRequest RPC is dequeued and processed at tick T-1,
                // strictly before the heartbeat timeout is evaluated at tick T.
                ProcessRpcIfConnected();
                stateMachine.EvaluateTimeouts(currentTick: 100u, heartbeatTimeoutTicks: 1u, connectingTimeoutTicks: 1000u, observer);
            }
            else
            {
                // Ordering (b): the state machine advances to Disconnected_SessionActive before the RPC
                // (still sitting in the input queue) is dequeued and processed.
                stateMachine.EvaluateTimeouts(currentTick: 100u, heartbeatTimeoutTicks: 1u, connectingTimeoutTicks: 1000u, observer);
                ProcessRpcIfConnected();
            }

            // Assert — EC-NET-6: no partial-application state is ever observable. Exactly one of the two
            // fully-applied/fully-dropped shapes holds; the forbidden shape (heldFreePoints decremented
            // without the stat write, or vice versa) never occurs.
            bool fullyProcessed = persisted.HeldFreePoints == 0 && persisted.StatApplied;
            bool fullyDropped = persisted.HeldFreePoints == 1 && !persisted.StatApplied;
            Assert.IsTrue(fullyProcessed || fullyDropped,
                $"Partial-application state detected: heldFreePoints={persisted.HeldFreePoints}, statApplied={persisted.StatApplied}.");

            if (rpcProcessedFirst)
            {
                Assert.IsTrue(fullyProcessed, "Ordering (a): RPC processed before the transition advanced -> must be fully applied.");
            }
            else
            {
                Assert.IsTrue(fullyDropped, "Ordering (b): transition advanced before the RPC was dequeued -> must be fully dropped.");
            }
        }
    }
}
