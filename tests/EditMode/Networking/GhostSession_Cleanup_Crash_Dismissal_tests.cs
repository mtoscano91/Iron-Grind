using System;
using System.Collections.Generic;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 021 — Ghost Session cluster, fifth and final
    /// story: <see cref="GhostEntityTracker.RemoveGhost"/> (CR-GH-10 step 4),
    /// <see cref="GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup"/> (CR-GH-12/12.1),
    /// <see cref="GhostDismissalCoordinator"/> (CR-GH-12), and <see cref="ZoneCrashCleanupHandler"/>
    /// (CR-GH-11). Covers all 5 blocking ACs (AC-GH-10, AC-GH-11, AC-GH-14, AC-GH-16, AC-GH-20) plus
    /// null-guard/precondition-guard coverage for every new public method across all 4 new/extended
    /// pieces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All timing is tick-based / caller-driven only — no <see cref="System.Threading.Thread.Sleep"/>
    /// anywhere in this file, matching every prior Ghost Session test file's own precedent (Stories
    /// 017-020).
    /// </para>
    /// <para>
    /// <b>AC-GH-14 and AC-GH-16 are composition tests requiring no new production code</b> (per this
    /// story's own approved design guidance — the same "compose with already-tested methods, don't
    /// re-derive" precedent Story 019's AC-GH-9 and Story 020's AC-GH-7/AC-GH-12/AC-GH-6 tests already
    /// established). AC-GH-14 composes <see cref="ConnectionStateMachine.EnterReconnecting"/> and
    /// <see cref="ConnectionStateMachine.RecordFailedReAuthAttempt"/> (Story 013, already tested — see
    /// that method's own remarks: "<c>sessionExpiryTick</c> is never written by this method, on either
    /// branch") with <see cref="MobDeTargetingCoordinator.ProcessTTLExpiry"/> (Story 019, already
    /// tested), reusing the identical <c>originalSessionExpiryTick</c> constant across both
    /// <see cref="ConnectionStateMachine.EnterReconnecting"/> calls to prove the TTL is never re-armed
    /// by the intervening failed reconnect. AC-GH-16 composes
    /// <see cref="GhostEntityTracker.PromoteToGhost"/> and a real
    /// <see cref="ConnectionStateMachine"/> heartbeat-timeout ghost (proving a genuine ghost session,
    /// not just an abstract slot count) with
    /// <see cref="ZoneSessionStateMachine.EvaluateJoinAttempt"/> — the exact method
    /// <c>Session_ZoneStateMachine_Capacity_tests.cs</c>'s own
    /// <c>EvaluateJoinAttempt_FortyNineConnectedPlusOneGhost_GhostCountsTowardCap_RejectsNewJoin</c>
    /// (Story 014) already tests for the identical underlying capacity arithmetic; that method's own
    /// doc comment and EC-NET-3 already state ghost sessions count toward zone capacity, so this test
    /// proves this story's own scenario composes correctly with that already-tested guarantee, not a
    /// new one.
    /// </para>
    /// <para>
    /// <b>AC-GH-20 does not use <see cref="IServerCrashInjector"/></b> (mechanism clarification, per
    /// this story's own story-readiness fix): unlike AC-GH-10 (a single crash injected mid-sequence
    /// via <see cref="IServerCrashInjector.RegisterCrashAt"/>), AC-GH-20 exercises
    /// <see cref="ZoneCrashCleanupHandler.ProcessZoneCrash"/>'s own enumeration logic directly, against
    /// a test-constructed list of two <see cref="GhostZoneCrashSession"/> entries in different
    /// <see cref="SessionState"/> values — proving the per-session loop handles both
    /// <see cref="SessionState.Disconnected_SessionActive"/> and <see cref="SessionState.Reconnecting"/>
    /// <c>fromState</c> values correctly.
    /// </para>
    /// <para>
    /// <b><see cref="PersistenceWriteReason"/> enum decision (judgment call, this story):</b> two new
    /// values were added — <see cref="PersistenceWriteReason.GhostDismissed"/> (CR-GH-12, distinct
    /// from <see cref="PersistenceWriteReason.GhostCombatTTLExpiry"/> — the cleanup write's actual
    /// cause is a voluntary dismissal, not TTL elapsing) and
    /// <see cref="PersistenceWriteReason.GhostZoneCrash"/> (CR-GH-11, distinct from
    /// <see cref="PersistenceWriteReason.ZoneClose"/> — that value is Story 014's unrelated,
    /// already-owned non-ghost <c>Draining → Closed</c> teardown reason). Both mirror Story 018's own
    /// precedent of adding <see cref="PersistenceWriteReason.GhostCombatTTLExpiry"/>/
    /// <see cref="PersistenceWriteReason.GhostDeath"/> when no existing value fit.
    /// </para>
    /// <para>
    /// <b>Mislabeling avoidance (judgment call, this story):</b>
    /// <see cref="GhostDismissalCoordinator.ProcessDismissalRequest"/> does not call
    /// <see cref="MobDeTargetingCoordinator.ProcessTTLExpiry"/> for its de-target loop — that method
    /// unconditionally fires <c>OnGhostCombatTTLExpired</c>, an observer event name that is
    /// TTL-expiry-specific. Firing it during a voluntary dismissal would mislabel the event (the
    /// ghost was not TTL-expired; it was dismissed). <see cref="GhostDismissalCoordinator"/> instead
    /// inlines the de-target loop itself, issuing <c>issueMobDeTargetCommand</c> directly with no
    /// TTL-specific observer call at all — see that class's own remarks.
    /// </para>
    /// <para>
    /// <b>characterId doubles as entityId/accountId's associated character</b> in every test below,
    /// the same simplification every prior Ghost Session test file already makes (no Character
    /// &lt;-&gt; Entity mapping system exists yet in this codebase).
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class GhostSession_Cleanup_Crash_Dismissal_Tests
    {
        private const uint AccountId = 7u;
        private const uint GhostCharacterId = 555u;

        /// <summary>Advances a fresh <see cref="ConnectionStateMachine"/>'s account to <see cref="SessionState.Disconnected_SessionActive"/> at tick 1.</summary>
        private static ConnectionStateMachine CreateDisconnectedSessionActiveStateMachine(NetworkTestObserver observer)
        {
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountId, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountId, GhostCharacterId, currentTick: 0u, observer);
            stateMachine.EvaluateTimeouts(currentTick: 1u, heartbeatTimeoutTicks: 1u, connectingTimeoutTicks: 1000u, observer); // -> Disconnected_SessionActive
            observer.Reset();
            return stateMachine;
        }

        // =========================================================================================
        // AC-GH-10: zone crash after CR-GH-10 step 3 (ghost cleanup persistence write) does not lose
        // pre-disconnect character state. First, a happy-path composition proving the full CR-GH-10
        // sequence (steps 1-2, 3, 6-7, 4-5) is correctly ordered; then the crash variant.
        // =========================================================================================

        [Test]
        public void FullCrGh10CleanupSequence_DeTargetPersistTransitionBroadcastRemoveEndTracking_OrderedCorrectly_AC_GH_10()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            var xpPoolTracker = new GhostXpPoolTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (10, 0, 20), observer);
            xpPoolTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);
            observer.Reset();

            var callOrder = new List<string>();
            const int snapshotHp = 60;
            const uint disconnectTickNumber = 1000u;
            const uint expiryTick = 1600u;

            // Act — CR-GH-10 steps 1-2: de-target.
            MobDeTargetingCoordinator.ProcessTTLExpiry(
                entityId: GhostCharacterId, disconnectTickNumber: disconnectTickNumber, expiryTick: expiryTick,
                targetingMobIds: new uint[] { 42u, 43u },
                issueMobDeTargetCommand: mobId => callOrder.Add($"detarget:{mobId}"),
                observer);

            // CR-GH-10 steps 3, 6, 7.
            GhostCleanupSequencer.CompleteTTLExpiryCleanup(AccountId, GhostCharacterId, snapshotHp,
                persistCharacterState: hp =>
                {
                    // Assert before appending "persist" — this proves de-target already ran by the
                    // moment persist is invoked. Asserting after the Add would trivially include
                    // "persist" itself in callOrder, which is what the original (buggy, undetected
                    // until this test could run in a live Editor) version of this test did.
                    CollectionAssert.AreEqual(new[] { "detarget:42", "detarget:43" }, callOrder,
                        "CR-GH-10 steps 1-2 (de-target) must complete before step 3 (persist).");
                    callOrder.Add("persist");
                },
                broadcastGhostExpiredEvent: (characterId, reason) => callOrder.Add("broadcast"),
                observer);

            // CR-GH-10 steps 4-5.
            ghostTracker.RemoveGhost(GhostCharacterId);
            xpPoolTracker.EndTracking(GhostCharacterId);
            callOrder.Add("removeAndEndTracking");

            // Assert
            CollectionAssert.AreEqual(
                new[] { "detarget:42", "detarget:43", "persist", "broadcast", "removeAndEndTracking" }, callOrder);
            Assert.IsFalse(ghostTracker.IsGhost(GhostCharacterId), "CR-GH-10 step 4: ghost entity removed from zone instance state.");
            Assert.IsFalse(xpPoolTracker.IsTracked(GhostCharacterId), "CR-GH-10 step 5: party XP-pool tracking released.");
        }

        [Test]
        public void CompleteTTLExpiryCleanup_SimulatedCrashAfterGhostCleanupPersistenceWrite_CharacterStateDurableStepsFourFiveNeverRun_AC_GH_10()
        {
            // Arrange — AC-GH-10's own named test technique: IServerCrashInjector.AfterGhostCleanupPersistenceWrite
            // (an already-existing crash step — see IServerCrashInjector's own doc comment naming this
            // exact scenario). The mock persistence write completes durably; the simulated crash then
            // aborts before steps 4-7 of CR-GH-10 can run.
            var ghostTracker = new GhostEntityTracker();
            var xpPoolTracker = new GhostXpPoolTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (10, 0, 20));
            xpPoolTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);

            var crashInjector = new ServerCrashInjector();
            crashInjector.RegisterCrashAt(CrashStep.AfterGhostCleanupPersistenceWrite);
            int? mockStorePersistedHp = null;
            bool broadcastFired = false;
            const int snapshotHp = 42;

            // Act
            Assert.Throws<InvalidOperationException>(() =>
                GhostCleanupSequencer.CompleteTTLExpiryCleanup(AccountId, GhostCharacterId, snapshotHp,
                    persistCharacterState: hp =>
                    {
                        // The durable write itself always completes, regardless of the crash below.
                        mockStorePersistedHp = hp;

                        crashInjector.SignalStepReached(CrashStep.AfterGhostCleanupPersistenceWrite,
                            onCrash: () => throw new InvalidOperationException(
                                "Simulated server crash after ghost cleanup persistence write."));
                    },
                    broadcastGhostExpiredEvent: (characterId, reason) => broadcastFired = true));

            // Assert — character state survived the crash (recoverable on restart)...
            Assert.AreEqual(snapshotHp, mockStorePersistedHp,
                "AC-GH-10: the persistence write must be durable even though the process 'crashed' immediately after.");
            // ...but nothing past the persistence write ran — steps 4-7 never execute, since the
            // crash exception propagates out of CompleteTTLExpiryCleanup before this test's own call
            // site could ever reach RemoveGhost/EndTracking.
            Assert.IsFalse(broadcastFired, "GhostExpiredEvent must not fire once the simulated crash aborts the sequence.");
            Assert.IsTrue(ghostTracker.IsGhost(GhostCharacterId),
                "CR-GH-10 step 4 (remove ghost) must never run once the crash aborts the sequence at step 3.");
            Assert.IsTrue(xpPoolTracker.IsTracked(GhostCharacterId),
                "CR-GH-10 step 5 (release XP tracking) must never run once the crash aborts the sequence at step 3.");
        }

        // =========================================================================================
        // AC-GH-11: voluntary dismissal — TTL cancelled (structurally), ghost removed, party slot
        // released, GhostExpiredEvent(GHOST_DISMISSED) emitted, banked XP only, session ->
        // Disconnected_SessionExpired reason GhostDismissed.
        // =========================================================================================

        [Test]
        public void ProcessDismissalRequest_ValidRequest_ExecutesFullCrGh10SequenceInOrderAndReturnsTrue_AC_GH_11()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            var xpPoolTracker = new GhostXpPoolTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (10, 0, 20), observer);
            xpPoolTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);
            observer.Reset();

            var callOrder = new List<string>();
            const int snapshotHp = 42;

            // Act
            bool accepted = GhostDismissalCoordinator.ProcessDismissalRequest(
                AccountId, GhostCharacterId, snapshotHp,
                requesterIsValidPartyMember: true,
                currentState: SessionState.Disconnected_SessionActive,
                targetingMobIds: new uint[] { 42u, 43u },
                issueMobDeTargetCommand: mobId => callOrder.Add($"detarget:{mobId}"),
                persistCharacterState: hp =>
                {
                    // Assert before appending "persist" — see the identical fix and rationale in
                    // FullCrGh10CleanupSequence_..._AC_GH_10's test above.
                    CollectionAssert.AreEqual(new[] { "detarget:42", "detarget:43" }, callOrder,
                        "CR-GH-10 steps 1-2 (de-target) must complete before step 3 (persist).");
                    Assert.IsEmpty(observer.PersistenceWriteCompletedCalls);
                    Assert.IsEmpty(observer.SessionStateTransitionedCalls);
                    callOrder.Add("persist");
                },
                broadcastGhostExpiredEvent: (characterId, reason) =>
                {
                    callOrder.Add("broadcast");
                    Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
                    Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
                    Assert.AreEqual(GhostCharacterId, characterId);
                    Assert.AreEqual("GhostDismissed", reason);
                },
                ghostTracker, xpPoolTracker, observer);

            // Assert
            Assert.IsTrue(accepted);
            CollectionAssert.AreEqual(new[] { "detarget:42", "detarget:43", "persist", "broadcast" }, callOrder);

            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((GhostCharacterId, PersistenceWriteReason.GhostDismissed), observer.PersistenceWriteCompletedCalls[0]);

            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual(
                (AccountId, SessionState.Disconnected_SessionActive, SessionState.Disconnected_SessionExpired, "GhostDismissed"),
                observer.SessionStateTransitionedCalls[0]);

            Assert.IsFalse(ghostTracker.IsGhost(GhostCharacterId), "CR-GH-10 step 4: ghost removed.");
            Assert.IsFalse(xpPoolTracker.IsTracked(GhostCharacterId), "CR-GH-10 step 5: XP-pool tracking released.");
        }

        [Test]
        public void ProcessDismissalRequest_ValidRequest_PersistsPreDisconnectXpOnlyForfeitingPostDisconnectShare_AC_GH_11()
        {
            // Arrange — CR-GH-12.1: identical forfeit policy as TTL expiry (CR-GH-9/9.1) — only
            // pre-disconnect XP persists; the post-disconnect party-share pool is forfeited.
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            var xpPoolTracker = new GhostXpPoolTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (10, 0, 20));
            xpPoolTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);
            xpPoolTracker.AccumulatePostDisconnectShare(GhostCharacterId, xpAmount: 100); // forfeitable pool

            int? persistedXp = null;

            // Act — resolved inside the persist delegate closure, the same idiom Story 020's own
            // AC-GH-7/AC-GH-12 tests established (see class remarks' TD-020 caveat).
            bool accepted = GhostDismissalCoordinator.ProcessDismissalRequest(
                AccountId, GhostCharacterId, snapshotHp: 42,
                requesterIsValidPartyMember: true,
                currentState: SessionState.Disconnected_SessionActive,
                targetingMobIds: Array.Empty<uint>(),
                issueMobDeTargetCommand: _ => { },
                persistCharacterState: _ =>
                {
                    persistedXp = xpPoolTracker.ResolveFinalXp(GhostCharacterId, includePostDisconnectShare: false);
                },
                broadcastGhostExpiredEvent: (_, _) => { },
                ghostTracker, xpPoolTracker, observer);

            // Assert
            Assert.IsTrue(accepted);
            Assert.AreEqual(500, persistedXp,
                "CR-GH-12.1: only pre-disconnect XP (banked) persists — the 100 post-disconnect party-share XP is forfeited.");
        }

        [Test]
        public void ProcessDismissalRequest_RequesterNotValidPartyMember_RejectsWithNoSideEffects_AC_GH_11()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            var xpPoolTracker = new GhostXpPoolTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (10, 0, 20));
            xpPoolTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);

            bool deTargetCalled = false;
            bool persistCalled = false;
            bool broadcastCalled = false;

            // Act — CR-GH-12 step 1: not a valid party member.
            bool accepted = GhostDismissalCoordinator.ProcessDismissalRequest(
                AccountId, GhostCharacterId, snapshotHp: 42,
                requesterIsValidPartyMember: false,
                currentState: SessionState.Disconnected_SessionActive,
                targetingMobIds: new uint[] { 42u },
                issueMobDeTargetCommand: _ => deTargetCalled = true,
                persistCharacterState: _ => persistCalled = true,
                broadcastGhostExpiredEvent: (_, _) => broadcastCalled = true,
                ghostTracker, xpPoolTracker, observer);

            // Assert — rejected, no side effects at all.
            Assert.IsFalse(accepted);
            Assert.IsFalse(deTargetCalled);
            Assert.IsFalse(persistCalled);
            Assert.IsFalse(broadcastCalled);
            Assert.IsTrue(ghostTracker.IsGhost(GhostCharacterId), "Rejected request must not remove the ghost entity.");
            Assert.IsTrue(xpPoolTracker.IsTracked(GhostCharacterId), "Rejected request must not release XP-pool tracking.");
            Assert.IsEmpty(observer.PersistenceWriteCompletedCalls);
            Assert.IsEmpty(observer.SessionStateTransitionedCalls);
        }

        [Test]
        public void ProcessDismissalRequest_GhostNotInDisconnectedSessionActive_RejectsWithNoSideEffects_AC_GH_11()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            var xpPoolTracker = new GhostXpPoolTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (10, 0, 20));
            xpPoolTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);

            bool persistCalled = false;

            // Act — CR-GH-12 step 1: session is Reconnecting, not Disconnected_SessionActive.
            bool accepted = GhostDismissalCoordinator.ProcessDismissalRequest(
                AccountId, GhostCharacterId, snapshotHp: 42,
                requesterIsValidPartyMember: true,
                currentState: SessionState.Reconnecting,
                targetingMobIds: Array.Empty<uint>(),
                issueMobDeTargetCommand: _ => { },
                persistCharacterState: _ => persistCalled = true,
                broadcastGhostExpiredEvent: (_, _) => { },
                ghostTracker, xpPoolTracker, observer);

            // Assert
            Assert.IsFalse(accepted);
            Assert.IsFalse(persistCalled);
            Assert.IsTrue(ghostTracker.IsGhost(GhostCharacterId));
            Assert.IsTrue(xpPoolTracker.IsTracked(GhostCharacterId));
        }

        // =========================================================================================
        // AC-GH-14: double disconnect (failed reconnect mid-TTL) does not reset the TTL — composition
        // test, no new production code (see class remarks).
        // =========================================================================================

        [Test]
        public void DoubleDisconnect_FailedReconnectMidTTL_TTLNeverResetsExpiryTickUnchanged_AC_GH_14()
        {
            // Arrange — ghost enters Disconnected_SessionActive at tick 0 (T=0s); the TTL window is
            // computed once, by the caller, from the ORIGINAL disconnect tick.
            var observer = new NetworkTestObserver();
            const uint disconnectTickNumber = 0u;
            const uint ghostCombatTtlTicks = 600u; // arbitrary tick-based TTL width, caller-computed
            const uint originalSessionExpiryTick = disconnectTickNumber + ghostCombatTtlTicks; // T=0 + TTL

            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountId, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountId, GhostCharacterId, currentTick: 0u, observer);
            stateMachine.EvaluateTimeouts(currentTick: disconnectTickNumber + 1u, heartbeatTimeoutTicks: 1u,
                connectingTimeoutTicks: 1000u, observer); // -> Disconnected_SessionActive
            observer.Reset();

            // Act 1 — a reconnect attempt at T=15s fails re-auth (non-exhausting: limit 3).
            stateMachine.EnterReconnecting(AccountId, originalSessionExpiryTick, observer);
            stateMachine.RecordFailedReAuthAttempt(AccountId, reauthFailureLimit: 3,
                persistFinalCharacterState: _ => Assert.Fail("Must not persist — this failure does not exhaust the limit."),
                observer); // -> back to Disconnected_SessionActive; EC-NET-7: SessionExpiryTick untouched

            // Two transitions fire here, not one: EnterReconnecting itself fires
            // Disconnected_SessionActive -> Reconnecting ("ReconnectAttempt") before
            // RecordFailedReAuthAttempt's own Reconnecting -> Disconnected_SessionActive
            // ("ReAuthFailed"). An earlier version of this test only anticipated the second call —
            // undetected until this test could actually run in a live Editor for the first time.
            Assert.AreEqual(2, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountId, SessionState.Disconnected_SessionActive, SessionState.Reconnecting, "ReconnectAttempt"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.AreEqual((AccountId, SessionState.Reconnecting, SessionState.Disconnected_SessionActive, "ReAuthFailed"),
                observer.SessionStateTransitionedCalls[1]);

            // Act 2 — the session returns to Disconnected_SessionActive at T=20s; a second reconnect
            // attempt re-enters Reconnecting, reusing the SAME originalSessionExpiryTick (EC-NET-7:
            // the caller must pass the same original value on every re-entry — see
            // ConnectionStateMachine.EnterReconnecting's own remarks).
            stateMachine.EnterReconnecting(AccountId, originalSessionExpiryTick, observer);

            // Code review finding, Story 021: read the frozen value back OUT of the state machine
            // rather than reusing the test's own local constant — closes the gap where this test would
            // otherwise pass even if a bug re-armed the timer, since `TryGetSessionExpiryTick` is the
            // one accessor whose own doc comment exists specifically to prove "the frozen value stored
            // on the record," not merely "the same literal the test author happened to pass twice."
            Assert.IsTrue(stateMachine.TryGetSessionExpiryTick(AccountId, out uint actualExpiryTick));
            Assert.AreEqual(originalSessionExpiryTick, actualExpiryTick,
                "AC-GH-14: the session record's own stored SessionExpiryTick must still equal the original value after the failed reconnect round-trip.");

            var mobDeTargetCalls = new List<uint>();

            // Assert — the TTL expiry, when finally evaluated, is computed from the value actually
            // stored on the session record (read back above), never re-armed by the failed reconnect.
            MobDeTargetingCoordinator.ProcessTTLExpiry(
                entityId: GhostCharacterId,
                disconnectTickNumber: disconnectTickNumber,
                expiryTick: actualExpiryTick,
                targetingMobIds: Array.Empty<uint>(),
                issueMobDeTargetCommand: mobId => mobDeTargetCalls.Add(mobId),
                observer);

            Assert.AreEqual(1, observer.GhostCombatTTLExpiredCalls.Count);
            Assert.AreEqual((GhostCharacterId, disconnectTickNumber, originalSessionExpiryTick),
                observer.GhostCombatTTLExpiredCalls[0],
                "AC-GH-14: expiryTick must equal the ORIGINAL disconnect tick + TTL width — never re-armed by the failed reconnect.");
        }

        // =========================================================================================
        // AC-GH-16: ghost counts against zone capacity — composition test, no new production code
        // (see class remarks).
        // =========================================================================================

        [Test]
        public void EvaluateJoinAttempt_GhostOccupiesFinalCapacitySlot_NewJoinRejected_AC_GH_16()
        {
            // Arrange — a zone at capacity N=10 with 9 live Connected players (abstracted as a slot
            // count) plus 1 real ghost, tracked by both GhostEntityTracker and ConnectionStateMachine
            // (not just an abstract integer) — proving the ghost occupying the final slot is a
            // genuine ghost session.
            const int zoneCapacity = 10;
            const int otherConnectedPlayers = 9;

            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer); // ghost via heartbeat timeout
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (10, 0, 20), observer);

            Assert.IsTrue(stateMachine.TryGetSessionState(AccountId, out SessionState ghostState));
            Assert.AreEqual(SessionState.Disconnected_SessionActive, ghostState);
            Assert.IsTrue(ghostTracker.IsGhost(GhostCharacterId));

            var zoneStateMachine = new ZoneSessionStateMachine();

            // Act — EC-NET-3: the ghost session counts toward the cap alongside the 9 live players.
            bool accepted = zoneStateMachine.EvaluateJoinAttempt(
                currentOccupiedSlots: otherConnectedPlayers + 1, capacity: zoneCapacity);

            // Assert
            Assert.IsFalse(accepted,
                "AC-GH-16: the ghost session's slot is not relinquished until TTL expiry — it still occupies the final slot.");
        }

        // =========================================================================================
        // AC-GH-20: zone crash persists all ghost sessions in Disconnected_SessionActive or
        // Reconnecting. Direct invocation of ZoneCrashCleanupHandler.ProcessZoneCrash — not
        // IServerCrashInjector (see class remarks' mechanism clarification).
        // =========================================================================================

        [Test]
        public void ProcessZoneCrash_TwoSessionsActiveAndReconnecting_PersistsBothBeforeTerminationWithCorrectFromStates_AC_GH_20()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var persistedCalls = new List<(uint characterId, int hp)>();

            var affectedSessions = new[]
            {
                new GhostZoneCrashSession(accountId: 7u, characterId: 555u, snapshotHp: 42, SessionState.Disconnected_SessionActive),
                new GhostZoneCrashSession(accountId: 8u, characterId: 556u, snapshotHp: 90, SessionState.Reconnecting),
            };

            // Act
            ZoneCrashCleanupHandler.ProcessZoneCrash(affectedSessions,
                persistCharacterState: (characterId, hp) => persistedCalls.Add((characterId, hp)),
                observer);

            // Assert
            CollectionAssert.AreEqual(new (uint, int)[] { (555u, 42), (556u, 90) }, persistedCalls,
                "AC-GH-20: both characters' pre-disconnect snapshots must be persisted, in order.");

            Assert.AreEqual(2, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((555u, PersistenceWriteReason.GhostZoneCrash), observer.PersistenceWriteCompletedCalls[0]);
            Assert.AreEqual((556u, PersistenceWriteReason.GhostZoneCrash), observer.PersistenceWriteCompletedCalls[1]);

            Assert.AreEqual(2, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((7u, SessionState.Disconnected_SessionActive, SessionState.Disconnected_SessionExpired, "ZoneCrash"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.AreEqual((8u, SessionState.Reconnecting, SessionState.Disconnected_SessionExpired, "ZoneCrash"),
                observer.SessionStateTransitionedCalls[1]);
        }

        [Test]
        public void ProcessZoneCrash_EmptyAffectedSessions_NoOpNoExceptionNoCalls_AC_GH_20()
        {
            var observer = new NetworkTestObserver();
            bool persistCalled = false;

            ZoneCrashCleanupHandler.ProcessZoneCrash(Array.Empty<GhostZoneCrashSession>(),
                persistCharacterState: (_, _) => persistCalled = true,
                observer);

            Assert.IsFalse(persistCalled);
            Assert.IsEmpty(observer.PersistenceWriteCompletedCalls);
            Assert.IsEmpty(observer.SessionStateTransitionedCalls);
        }

        [Test]
        public void ProcessZoneCrash_SecondSessionPersistThrows_FirstSessionAlreadyTransitionedRemainingSessionsNeverProcessed()
        {
            // Code review finding, Story 021: the per-session loop has no rollback -- matches the same
            // partial-failure/atomicity guard Story 020 added for PartyDisbandCoordinator's equivalent
            // loop. Locks in exactly what happens if a caller-supplied persistCharacterState throws
            // partway through a multi-session crash: the entry before the throw is left fully
            // transitioned, and no entry after it is ever touched.
            var observer = new NetworkTestObserver();
            var affectedSessions = new[]
            {
                new GhostZoneCrashSession(accountId: 7u, characterId: 555u, snapshotHp: 42, SessionState.Disconnected_SessionActive),
                new GhostZoneCrashSession(accountId: 8u, characterId: 556u, snapshotHp: 90, SessionState.Reconnecting),
                new GhostZoneCrashSession(accountId: 9u, characterId: 557u, snapshotHp: 10, SessionState.Disconnected_SessionActive),
            };

            Assert.Throws<InvalidOperationException>(() =>
                ZoneCrashCleanupHandler.ProcessZoneCrash(affectedSessions,
                    persistCharacterState: (characterId, hp) =>
                    {
                        if (characterId == 556u)
                        {
                            throw new InvalidOperationException("Simulated persistence failure for the second session.");
                        }
                    },
                    observer));

            // The first entry (555u) completed fully before the throw.
            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((555u, PersistenceWriteReason.GhostZoneCrash), observer.PersistenceWriteCompletedCalls[0]);
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((7u, SessionState.Disconnected_SessionActive, SessionState.Disconnected_SessionExpired, "ZoneCrash"),
                observer.SessionStateTransitionedCalls[0]);

            // The third entry (557u), listed after the failing second entry, is never reached.
            Assert.IsFalse(observer.PersistenceWriteCompletedCalls.Exists(call => call.characterId == 557u));
            Assert.IsFalse(observer.SessionStateTransitionedCalls.Exists(call => call.accountId == 9u));
        }

        // =========================================================================================
        // Null-guard / precondition-guard coverage — GhostEntityTracker.RemoveGhost.
        // =========================================================================================

        [Test]
        public void RemoveGhost_CharacterNeverPromoted_ThrowsInvalidOperationException()
        {
            var ghostTracker = new GhostEntityTracker();
            Assert.Throws<InvalidOperationException>(() => ghostTracker.RemoveGhost(GhostCharacterId));
        }

        [Test]
        public void RemoveGhost_AlreadyRemoved_ThrowsInvalidOperationException()
        {
            var ghostTracker = new GhostEntityTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (0, 0, 0));
            ghostTracker.RemoveGhost(GhostCharacterId);

            Assert.Throws<InvalidOperationException>(() => ghostTracker.RemoveGhost(GhostCharacterId));
        }

        [Test]
        public void RemoveGhost_RecordExistsButNeverPromoted_ThrowsInvalidOperationException()
        {
            // Code review finding, Story 021 (coverage-completeness note): the third reachable branch
            // of RemoveGhost's guard -- a record exists (created via ApplyDamage, which never sets
            // IsGhost) but the character was never promoted. Already correct by the same `||` guard
            // condition; this test locks it in explicitly rather than leaving it implicit.
            var ghostTracker = new GhostEntityTracker();
            ghostTracker.ApplyDamage(GhostCharacterId, currentHp: 100, damageAmount: 10); // creates a record, IsGhost stays false

            Assert.Throws<InvalidOperationException>(() => ghostTracker.RemoveGhost(GhostCharacterId));
        }

        // =========================================================================================
        // Null-guard coverage — GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup.
        // =========================================================================================

        [Test]
        public void CompleteVoluntaryDismissalCleanup_NullPersistCharacterState_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup(AccountId, GhostCharacterId, snapshotHp: 0,
                    persistCharacterState: null,
                    broadcastGhostExpiredEvent: (_, _) => { }));
        }

        [Test]
        public void CompleteVoluntaryDismissalCleanup_NullBroadcastGhostExpiredEvent_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup(AccountId, GhostCharacterId, snapshotHp: 0,
                    persistCharacterState: _ => { },
                    broadcastGhostExpiredEvent: null));
        }

        // =========================================================================================
        // Null-guard coverage — GhostDismissalCoordinator.ProcessDismissalRequest.
        // =========================================================================================

        [Test]
        public void ProcessDismissalRequest_NullTargetingMobIds_ThrowsArgumentNullException()
        {
            var ghostTracker = new GhostEntityTracker();
            var xpPoolTracker = new GhostXpPoolTracker();
            Assert.Throws<ArgumentNullException>(() =>
                GhostDismissalCoordinator.ProcessDismissalRequest(AccountId, GhostCharacterId, snapshotHp: 0,
                    requesterIsValidPartyMember: true, currentState: SessionState.Disconnected_SessionActive,
                    targetingMobIds: null,
                    issueMobDeTargetCommand: _ => { },
                    persistCharacterState: _ => { },
                    broadcastGhostExpiredEvent: (_, _) => { },
                    ghostTracker, xpPoolTracker));
        }

        [Test]
        public void ProcessDismissalRequest_NullIssueMobDeTargetCommand_ThrowsArgumentNullException()
        {
            var ghostTracker = new GhostEntityTracker();
            var xpPoolTracker = new GhostXpPoolTracker();
            Assert.Throws<ArgumentNullException>(() =>
                GhostDismissalCoordinator.ProcessDismissalRequest(AccountId, GhostCharacterId, snapshotHp: 0,
                    requesterIsValidPartyMember: true, currentState: SessionState.Disconnected_SessionActive,
                    targetingMobIds: Array.Empty<uint>(),
                    issueMobDeTargetCommand: null,
                    persistCharacterState: _ => { },
                    broadcastGhostExpiredEvent: (_, _) => { },
                    ghostTracker, xpPoolTracker));
        }

        [Test]
        public void ProcessDismissalRequest_NullPersistCharacterState_ThrowsArgumentNullException()
        {
            var ghostTracker = new GhostEntityTracker();
            var xpPoolTracker = new GhostXpPoolTracker();
            Assert.Throws<ArgumentNullException>(() =>
                GhostDismissalCoordinator.ProcessDismissalRequest(AccountId, GhostCharacterId, snapshotHp: 0,
                    requesterIsValidPartyMember: true, currentState: SessionState.Disconnected_SessionActive,
                    targetingMobIds: Array.Empty<uint>(),
                    issueMobDeTargetCommand: _ => { },
                    persistCharacterState: null,
                    broadcastGhostExpiredEvent: (_, _) => { },
                    ghostTracker, xpPoolTracker));
        }

        [Test]
        public void ProcessDismissalRequest_NullBroadcastGhostExpiredEvent_ThrowsArgumentNullException()
        {
            var ghostTracker = new GhostEntityTracker();
            var xpPoolTracker = new GhostXpPoolTracker();
            Assert.Throws<ArgumentNullException>(() =>
                GhostDismissalCoordinator.ProcessDismissalRequest(AccountId, GhostCharacterId, snapshotHp: 0,
                    requesterIsValidPartyMember: true, currentState: SessionState.Disconnected_SessionActive,
                    targetingMobIds: Array.Empty<uint>(),
                    issueMobDeTargetCommand: _ => { },
                    persistCharacterState: _ => { },
                    broadcastGhostExpiredEvent: null,
                    ghostTracker, xpPoolTracker));
        }

        [Test]
        public void ProcessDismissalRequest_NullGhostEntityTracker_ThrowsArgumentNullException()
        {
            var xpPoolTracker = new GhostXpPoolTracker();
            Assert.Throws<ArgumentNullException>(() =>
                GhostDismissalCoordinator.ProcessDismissalRequest(AccountId, GhostCharacterId, snapshotHp: 0,
                    requesterIsValidPartyMember: true, currentState: SessionState.Disconnected_SessionActive,
                    targetingMobIds: Array.Empty<uint>(),
                    issueMobDeTargetCommand: _ => { },
                    persistCharacterState: _ => { },
                    broadcastGhostExpiredEvent: (_, _) => { },
                    ghostEntityTracker: null, xpPoolTracker));
        }

        [Test]
        public void ProcessDismissalRequest_NullXpPoolTracker_ThrowsArgumentNullException()
        {
            var ghostTracker = new GhostEntityTracker();
            Assert.Throws<ArgumentNullException>(() =>
                GhostDismissalCoordinator.ProcessDismissalRequest(AccountId, GhostCharacterId, snapshotHp: 0,
                    requesterIsValidPartyMember: true, currentState: SessionState.Disconnected_SessionActive,
                    targetingMobIds: Array.Empty<uint>(),
                    issueMobDeTargetCommand: _ => { },
                    persistCharacterState: _ => { },
                    broadcastGhostExpiredEvent: (_, _) => { },
                    ghostTracker, xpPoolTracker: null));
        }

        // =========================================================================================
        // Null-guard coverage — ZoneCrashCleanupHandler.ProcessZoneCrash.
        // =========================================================================================

        [Test]
        public void ProcessZoneCrash_NullAffectedSessions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ZoneCrashCleanupHandler.ProcessZoneCrash(null, persistCharacterState: (_, _) => { }));
        }

        [Test]
        public void ProcessZoneCrash_NullPersistCharacterState_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ZoneCrashCleanupHandler.ProcessZoneCrash(Array.Empty<GhostZoneCrashSession>(), persistCharacterState: null));
        }
    }
}
