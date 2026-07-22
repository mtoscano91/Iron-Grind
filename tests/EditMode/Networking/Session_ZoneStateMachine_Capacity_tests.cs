using System;
using System.Collections.Generic;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 014 — the ST-NET-2 zone instance lifecycle state
    /// machine (<see cref="ZoneSessionStateMachine"/>) and its 10–50-player zone capacity gate. Covers
    /// AC-NC-14 (last connected player becomes a ghost -&gt; zone Draining), AC-NC-22 (zone at capacity
    /// rejects the 51st join), AC-NC-24 (a ghost session counts toward the cap), and AC-NC-41 (last
    /// session's explicit disconnect -&gt; zone Closed directly, no intermediate Draining) — using the
    /// fuller AC-NC-41 text from <c>networking-session.md</c> (the story file's own copy omits the
    /// <see cref="INetworkTestObserver.OnPersistenceWriteCompleted"/> and
    /// <see cref="IZoneTestConfigurator"/> clauses; this file tests against the fuller GDD text). Also
    /// exercises the remaining ST-NET-2 transition-table rows this story's own Implementation Notes
    /// require (<c>Draining -&gt; Active</c>, <c>Draining -&gt; Draining</c>, <c>Draining -&gt; Closed</c>,
    /// <c>Closed -&gt; Empty</c>) plus every null-guard and precondition-guard on this story's new
    /// public methods.
    /// </summary>
    /// <remarks>
    /// All timing is tick-based / caller-driven only — no <see cref="System.Threading.Thread.Sleep"/>
    /// anywhere in this file, matching <c>Session_ConnectionStateMachine_Core_tests.cs</c>'s own
    /// precedent. This story deliberately does not query <see cref="ConnectionStateMachine"/> at all
    /// (see <see cref="ZoneSessionStateMachine"/>'s own class remarks) — every test drives
    /// <see cref="ZoneSessionStateMachine"/> directly with caller-supplied counts, standing in for the
    /// not-yet-built orchestration layer that would compose it with <see cref="ConnectionStateMachine"/>
    /// in production.
    /// </remarks>
    [TestFixture]
    internal sealed class Session_ZoneStateMachine_Capacity_Tests
    {
        private const uint ZoneA = 7u;
        private const uint CharacterA = 555u;

        /// <summary>Advances a fresh state machine's zone to <see cref="ZoneState.Active"/> with the given capacity.</summary>
        private static ZoneSessionStateMachine CreateActiveStateMachine(NetworkTestObserver observer, int capacity = 50)
        {
            var stateMachine = new ZoneSessionStateMachine();
            stateMachine.EnterActive(ZoneA, capacity, observer);
            observer.Reset(); // isolate each test's assertions from the setup call above
            return stateMachine;
        }

        /// <summary>Advances a fresh state machine's zone to <see cref="ZoneState.Draining"/> (one ghost remaining).</summary>
        private static ZoneSessionStateMachine CreateDrainingStateMachine(NetworkTestObserver observer)
        {
            var stateMachine = CreateActiveStateMachine(observer);
            stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: false, totalOccupiedSlots: 1, observer);
            observer.Reset();
            return stateMachine;
        }

        // =========================================================================================
        // AC-NC-14: last remaining player becomes a ghost -> zone Active -> Draining.
        // =========================================================================================

        [Test]
        public void EvaluatePlayerCountChange_LastConnectedPlayerBecomesGhost_TransitionsActiveToDraining()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);

            // Act
            stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: false, totalOccupiedSlots: 1, observer);

            // Assert
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZoneA, ZoneState.Active, ZoneState.Draining), observer.ZoneStateTransitionedCalls[0]);
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState state));
            Assert.AreEqual(ZoneState.Draining, state);
        }

        [Test]
        public void EvaluatePlayerCountChange_ZeroOccupiedSlotsWithNoConnectedFromActive_Throws()
        {
            // Arrange — this scenario (zero sessions remain, none connected) must route through
            // CompleteExplicitDisconnectTeardown instead — see class remarks.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: false, totalOccupiedSlots: 0, observer));
        }

        [Test]
        public void EvaluatePlayerCountChange_ZeroOccupiedSlotsWithNoConnectedFromDraining_Throws()
        {
            // Arrange — code review finding (Story 014): the zero-remaining-sessions guard must be
            // symmetric across Active and Draining, not only guard the Active case. The real
            // Draining -> Closed path is CompleteTTLExpiryTeardown's exclusive responsibility; a
            // caller reporting zero occupied slots through this method instead is always a contract
            // violation, regardless of which of the two states the zone started in.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDrainingStateMachine(observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: false, totalOccupiedSlots: 0, observer));
        }

        [Test]
        public void EvaluatePlayerCountChange_NegativeTotalOccupiedSlots_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: false, totalOccupiedSlots: -1, observer));
        }

        [Test]
        public void EvaluatePlayerCountChange_ZoneNotRegistered_Throws()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = new ZoneSessionStateMachine();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: true, totalOccupiedSlots: 1, observer));
        }

        [Test]
        public void EvaluatePlayerCountChange_ZoneClosed_Throws()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);
            stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA, characterId => { }, observer);
            observer.Reset();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: true, totalOccupiedSlots: 1, observer));
        }

        // =========================================================================================
        // Draining -> Active: a player reconnects (reaches Connected) or a new player enters.
        // =========================================================================================

        [Test]
        public void EvaluatePlayerCountChange_PlayerReconnectsWhileDraining_TransitionsDrainingToActive()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDrainingStateMachine(observer);

            // Act
            stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: true, totalOccupiedSlots: 1, observer);

            // Assert
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZoneA, ZoneState.Draining, ZoneState.Active), observer.ZoneStateTransitionedCalls[0]);
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState state));
            Assert.AreEqual(ZoneState.Active, state);
        }

        [Test]
        public void EvaluatePlayerCountChange_TwoFullDrainingCycles_TransitionsCorrectlyEachTimeWithNoStateLeakage()
        {
            // Arrange — code review coverage gap (Story 014): a second Active<->Draining cycle,
            // hardening against any future regression that leaks bookkeeping across cycles.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);

            // Act & Assert — cycle 1: Active -> Draining -> Active.
            stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: false, totalOccupiedSlots: 1, observer);
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState afterCycle1Drain));
            Assert.AreEqual(ZoneState.Draining, afterCycle1Drain);

            stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: true, totalOccupiedSlots: 1, observer);
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState afterCycle1Active));
            Assert.AreEqual(ZoneState.Active, afterCycle1Active);

            // Act & Assert — cycle 2: Active -> Draining -> Active again.
            stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: false, totalOccupiedSlots: 2, observer);
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState afterCycle2Drain));
            Assert.AreEqual(ZoneState.Draining, afterCycle2Drain);

            stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: true, totalOccupiedSlots: 2, observer);
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState afterCycle2Active));
            Assert.AreEqual(ZoneState.Active, afterCycle2Active);

            // Assert — all 4 transitions fired, in order, no extras and no drops.
            Assert.AreEqual(4, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZoneA, ZoneState.Active, ZoneState.Draining), observer.ZoneStateTransitionedCalls[0]);
            Assert.AreEqual((ZoneA, ZoneState.Draining, ZoneState.Active), observer.ZoneStateTransitionedCalls[1]);
            Assert.AreEqual((ZoneA, ZoneState.Active, ZoneState.Draining), observer.ZoneStateTransitionedCalls[2]);
            Assert.AreEqual((ZoneA, ZoneState.Draining, ZoneState.Active), observer.ZoneStateTransitionedCalls[3]);
        }

        // =========================================================================================
        // Draining -> Draining: re-authentication fails for a Reconnecting session, returns to ghost
        // -- no zone-level change.
        // =========================================================================================

        [Test]
        public void EvaluatePlayerCountChange_ReAuthFailsWhileDraining_StaysDraining_NoCallbackFired()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDrainingStateMachine(observer);

            // Act — the Reconnecting session's re-auth attempt failed and it returned to being a ghost;
            // still zero Connected sessions, one occupied slot.
            stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: false, totalOccupiedSlots: 1, observer);

            // Assert
            Assert.IsEmpty(observer.ZoneStateTransitionedCalls,
                "Draining -> Draining is a no-op per the GDD's own transition table — no callback fires.");
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState state));
            Assert.AreEqual(ZoneState.Draining, state);
        }

        // =========================================================================================
        // Active -> Active: any connect/reconnect/disconnect while others remain -- no zone-level change.
        // =========================================================================================

        [Test]
        public void EvaluatePlayerCountChange_StillHasConnectedSessionsWhileActive_NoCallbackFired()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);

            // Act
            stateMachine.EvaluatePlayerCountChange(ZoneA, hasAnyConnectedSession: true, totalOccupiedSlots: 3, observer);

            // Assert
            Assert.IsEmpty(observer.ZoneStateTransitionedCalls,
                "Active -> Active is a no-op per the GDD's own transition table — no callback fires.");
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState state));
            Assert.AreEqual(ZoneState.Active, state);
        }

        // =========================================================================================
        // AC-NC-22: zone at capacity (50) rejects the 51st join.
        // =========================================================================================

        [Test]
        public void EvaluateJoinAttempt_AtCapacity_RejectsFiftyFirstJoin()
        {
            // Arrange
            var stateMachine = new ZoneSessionStateMachine();

            // Act
            bool accepted = stateMachine.EvaluateJoinAttempt(currentOccupiedSlots: 50, capacity: 50);

            // Assert
            Assert.IsFalse(accepted, "AC-NC-22: the 51st join against a 50-capacity zone must be rejected.");
        }

        [Test]
        public void EvaluateJoinAttempt_BelowCapacity_AcceptsJoin()
        {
            // Arrange
            var stateMachine = new ZoneSessionStateMachine();

            // Act
            bool accepted = stateMachine.EvaluateJoinAttempt(currentOccupiedSlots: 49, capacity: 50);

            // Assert
            Assert.IsTrue(accepted, "The 50th join against a 50-capacity zone (49 already occupied) must be accepted.");
        }

        [Test]
        public void EvaluateJoinAttempt_ZeroCapacity_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var stateMachine = new ZoneSessionStateMachine();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                stateMachine.EvaluateJoinAttempt(currentOccupiedSlots: 0, capacity: 0));
        }

        [Test]
        public void EvaluateJoinAttempt_NegativeOccupiedSlots_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var stateMachine = new ZoneSessionStateMachine();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                stateMachine.EvaluateJoinAttempt(currentOccupiedSlots: -1, capacity: 50));
        }

        // =========================================================================================
        // AC-NC-24: 49 connected + 1 ghost -- the ghost counts toward the 50-player cap, rejecting a
        // new join.
        // =========================================================================================

        [Test]
        public void EvaluateJoinAttempt_FortyNineConnectedPlusOneGhost_GhostCountsTowardCap_RejectsNewJoin()
        {
            // Arrange — 49 Connected + 1 Disconnected_SessionActive = 50 occupied slots (EC-NET-3: the
            // ghost session counts toward the cap).
            var stateMachine = new ZoneSessionStateMachine();

            // Act
            bool accepted = stateMachine.EvaluateJoinAttempt(currentOccupiedSlots: 50, capacity: 50);

            // Assert
            Assert.IsFalse(accepted,
                "AC-NC-24: the ghost session's slot is not relinquished until TTL expiry — it still counts toward the cap.");
        }

        // =========================================================================================
        // AC-NC-41: last session's explicit disconnect -> zone Active -> Closed directly (no
        // intermediate Draining). Fuller GDD text: also fires OnPersistenceWriteCompleted and is
        // readable via IZoneTestConfigurator on the same tick boundary.
        // =========================================================================================

        [Test]
        public void CompleteExplicitDisconnectTeardown_LastSessionDisconnects_TransitionsActiveDirectlyToClosed_NoIntermediateDraining()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);

            // Act
            stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA,
                persistFinalCharacterState: characterId => { },
                observer);

            // Assert
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZoneA, ZoneState.Active, ZoneState.Closed), observer.ZoneStateTransitionedCalls[0]);
            foreach (var call in observer.ZoneStateTransitionedCalls)
            {
                Assert.AreNotEqual(ZoneState.Draining, call.toState,
                    "AC-NC-41: no intermediate Draining state is entered on the Active -> Closed direct path.");
            }

            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((CharacterA, PersistenceWriteReason.ExplicitDisconnect), observer.PersistenceWriteCompletedCalls[0]);

            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState state));
            Assert.AreEqual(ZoneState.Closed, state);
        }

        [Test]
        public void CompleteExplicitDisconnectTeardown_FiresPersistenceThenZoneTransition_InOrder()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);
            var callOrder = new List<string>();

            // Act
            stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA,
                persistFinalCharacterState: characterId =>
                {
                    callOrder.Add("persist");
                    Assert.AreEqual(CharacterA, characterId);
                    Assert.IsEmpty(observer.ZoneStateTransitionedCalls,
                        "The zone transition must not have fired yet when persistence runs.");
                },
                observer);

            // Assert
            CollectionAssert.AreEqual(new[] { "persist" }, callOrder);
            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
        }

        [Test]
        public void CompleteExplicitDisconnectTeardown_RealStateMirroredIntoZoneTestConfiguratorSeam_ReadsBackAsClosed()
        {
            // Arrange — code review finding (Story 014): the original version of this test hardcoded
            // ZoneState.Closed directly into SetZoneStateForTesting, independent of the state machine's
            // actual result — a bug that produced Draining instead of Closed would not have been caught,
            // making the test tautological. This version drives SetZoneStateForTesting from the real
            // TryGetZoneState result, so a regression in CompleteExplicitDisconnectTeardown's transition
            // logic is what this test would actually detect.
            //
            // NOTE ON WHAT THIS DOES AND DOES NOT PROVE: IZoneTestConfigurator/ZoneTestConfigurator
            // (Story 001) are test-only, #if-guarded types with zero production wiring to any real
            // zone-lifecycle system yet — ZoneSessionStateMachine deliberately never touches them (see
            // class remarks). This test documents the INTENDED seam contract a future orchestration
            // layer (composing ZoneSessionStateMachine with IZoneTestConfigurator) would need to satisfy
            // — it does not and cannot prove that orchestration layer's correctness, since it doesn't
            // exist yet. It is a regression guard on THIS test's own mirroring logic and on
            // TryGetZoneState's return value, not a production-integration proof.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);
            var zoneTestConfigurator = new ZoneTestConfigurator();

            // Act
            stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA,
                persistFinalCharacterState: characterId => { },
                observer);
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState realState));
            zoneTestConfigurator.SetZoneStateForTesting(ZoneA, realState);

            // Assert
            Assert.AreEqual(ZoneState.Closed, realState,
                "AC-NC-41: the real state machine must have actually computed Closed.");
            Assert.AreEqual(realState, zoneTestConfigurator.GetCurrentZoneState(ZoneA),
                "The test-configurator seam, when mirrored from the real result, reads back the same value.");
        }

        [Test]
        public void CompleteExplicitDisconnectTeardown_ZoneNotActive_Throws()
        {
            // Arrange — a zone that was never activated.
            var observer = new NetworkTestObserver();
            var stateMachine = new ZoneSessionStateMachine();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA,
                    persistFinalCharacterState: characterId => { },
                    observer));
        }

        [Test]
        public void CompleteExplicitDisconnectTeardown_ZoneDraining_Throws()
        {
            // Arrange — code review coverage gap (Story 014): a registered-but-wrong-state zone,
            // distinct from the unregistered case above.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDrainingStateMachine(observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA,
                    persistFinalCharacterState: characterId => { },
                    observer));
        }

        [Test]
        public void CompleteExplicitDisconnectTeardown_ZoneClosed_Throws()
        {
            // Arrange — code review coverage gap (Story 014): a registered-but-wrong-state zone,
            // distinct from the unregistered case above.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);
            stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA, characterId => { }, observer);
            observer.Reset();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA,
                    persistFinalCharacterState: characterId => { },
                    observer));
        }

        [Test]
        public void CompleteExplicitDisconnectTeardown_NullPersistDelegate_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA,
                    persistFinalCharacterState: null,
                    observer));
        }

        // =========================================================================================
        // Draining -> Closed: all remaining session TTLs expire (caller-driven; no real timer in this
        // class — Story 015 owns the real TTL-tick sweep).
        // =========================================================================================

        [Test]
        public void CompleteTTLExpiryTeardown_DrainingWithRemainingGhosts_TransitionsToClosedAndPersistsEachWithZoneCloseReason()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDrainingStateMachine(observer);
            uint[] remaining = { 555u, 556u };

            // Act
            stateMachine.CompleteTTLExpiryTeardown(ZoneA, remaining,
                persistFinalCharacterState: characterId => { },
                observer);

            // Assert
            Assert.AreEqual(2, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((555u, PersistenceWriteReason.ZoneClose), observer.PersistenceWriteCompletedCalls[0]);
            Assert.AreEqual((556u, PersistenceWriteReason.ZoneClose), observer.PersistenceWriteCompletedCalls[1]);

            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZoneA, ZoneState.Draining, ZoneState.Closed), observer.ZoneStateTransitionedCalls[0]);

            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState state));
            Assert.AreEqual(ZoneState.Closed, state);
        }

        [Test]
        public void CompleteTTLExpiryTeardown_PersistsAllRemainingSessionsBeforeZoneTransitionFires()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDrainingStateMachine(observer);
            uint[] remaining = { 555u, 556u };
            var persistedBeforeTransition = new List<bool>();

            // Act
            stateMachine.CompleteTTLExpiryTeardown(ZoneA, remaining,
                persistFinalCharacterState: characterId =>
                {
                    persistedBeforeTransition.Add(observer.ZoneStateTransitionedCalls.Count == 0);
                },
                observer);

            // Assert
            CollectionAssert.AreEqual(new[] { true, true }, persistedBeforeTransition,
                "Every remaining session's persistence write must complete before the zone transition fires.");
        }

        [Test]
        public void CompleteTTLExpiryTeardown_NoRemainingCharacterIds_TransitionsToClosedWithNoPersistenceWrites()
        {
            // Arrange — code review coverage gap (Story 014): the success path with zero remaining
            // ghosts was previously only exercised indirectly via the wrong-state-guard test's use of
            // Array.Empty<uint>(), which never reaches the loop body at all. This test exercises the
            // real Draining -> Closed transition with an empty remainingCharacterIds list.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDrainingStateMachine(observer);

            // Act
            stateMachine.CompleteTTLExpiryTeardown(ZoneA, Array.Empty<uint>(),
                persistFinalCharacterState: characterId => Assert.Fail("No remaining sessions — must not persist."),
                observer);

            // Assert
            Assert.IsEmpty(observer.PersistenceWriteCompletedCalls);
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZoneA, ZoneState.Draining, ZoneState.Closed), observer.ZoneStateTransitionedCalls[0]);
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState state));
            Assert.AreEqual(ZoneState.Closed, state);
        }

        [Test]
        public void CompleteTTLExpiryTeardown_ZoneNotDraining_Throws()
        {
            // Arrange — a zone still fully Active (no ghosts) is not eligible for this path.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteTTLExpiryTeardown(ZoneA, Array.Empty<uint>(),
                    persistFinalCharacterState: characterId => { },
                    observer));
        }

        [Test]
        public void CompleteTTLExpiryTeardown_NullRemainingCharacterIds_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDrainingStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteTTLExpiryTeardown(ZoneA, null,
                    persistFinalCharacterState: characterId => { },
                    observer));
        }

        [Test]
        public void CompleteTTLExpiryTeardown_NullPersistDelegate_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDrainingStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteTTLExpiryTeardown(ZoneA, new[] { 555u },
                    persistFinalCharacterState: null,
                    observer));
        }

        // =========================================================================================
        // Empty -> Active: first player enters Connected.
        // =========================================================================================

        [Test]
        public void EnterActive_FirstPlayerConnects_TransitionsEmptyToActive()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = new ZoneSessionStateMachine();

            // Act
            stateMachine.EnterActive(ZoneA, capacity: 50, observer);

            // Assert
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZoneA, ZoneState.Empty, ZoneState.Active), observer.ZoneStateTransitionedCalls[0]);
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState state));
            Assert.AreEqual(ZoneState.Active, state);
        }

        [Test]
        public void EnterActive_ZeroCapacity_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = new ZoneSessionStateMachine();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                stateMachine.EnterActive(ZoneA, capacity: 0, observer));
        }

        [Test]
        public void EnterActive_AlreadyActive_Throws()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.EnterActive(ZoneA, capacity: 50, observer));
        }

        [Test]
        public void EnterActive_AfterReinitializeFromClosed_SucceedsAsFreshActiveZone()
        {
            // Arrange — full Empty -> Active -> Closed -> Empty -> Active cycle.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);
            stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA, characterId => { }, observer);
            stateMachine.ReinitializeFromClosed(ZoneA, observer);
            observer.Reset();

            // Act
            stateMachine.EnterActive(ZoneA, capacity: 50, observer);

            // Assert
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZoneA, ZoneState.Empty, ZoneState.Active), observer.ZoneStateTransitionedCalls[0]);
        }

        // =========================================================================================
        // Closed -> Empty: zone re-allocated for a new session.
        // =========================================================================================

        [Test]
        public void ReinitializeFromClosed_ClosedZone_TransitionsToEmpty()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);
            stateMachine.CompleteExplicitDisconnectTeardown(ZoneA, CharacterA, characterId => { }, observer);
            observer.Reset();

            // Act
            stateMachine.ReinitializeFromClosed(ZoneA, observer);

            // Assert
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZoneA, ZoneState.Closed, ZoneState.Empty), observer.ZoneStateTransitionedCalls[0]);
            Assert.IsTrue(stateMachine.TryGetZoneState(ZoneA, out ZoneState state));
            Assert.AreEqual(ZoneState.Empty, state);
        }

        [Test]
        public void ReinitializeFromClosed_ZoneNotClosed_Throws()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateActiveStateMachine(observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.ReinitializeFromClosed(ZoneA, observer));
        }

        // =========================================================================================
        // Query methods.
        // =========================================================================================

        [Test]
        public void TryGetZoneState_UnregisteredZone_ReturnsFalse()
        {
            // Arrange
            var stateMachine = new ZoneSessionStateMachine();

            // Act
            bool found = stateMachine.TryGetZoneState(ZoneA, out ZoneState state);

            // Assert
            Assert.IsFalse(found);
            Assert.AreEqual(default(ZoneState), state);
        }

        [Test]
        public void IsZoneRegistered_UnregisteredThenRegistered_ReflectsRegistryState()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = new ZoneSessionStateMachine();

            // Act & Assert — before registration.
            Assert.IsFalse(stateMachine.IsZoneRegistered(ZoneA));

            // Act
            stateMachine.EnterActive(ZoneA, capacity: 50, observer);

            // Assert — after registration.
            Assert.IsTrue(stateMachine.IsZoneRegistered(ZoneA));
        }
    }
}
