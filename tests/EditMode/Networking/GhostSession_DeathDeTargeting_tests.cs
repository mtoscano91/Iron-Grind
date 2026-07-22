using System;
using System.Collections.Generic;
using System.Linq;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 019 — Ghost Session cluster, third story:
    /// <see cref="ConnectionStateMachine.CompleteGhostDeathFromDisconnected"/> (AC-GH-4, the
    /// normal/non-racing ghost-death session transition) and
    /// <see cref="MobDeTargetingCoordinator"/> (AC-GH-5, CR-GH-6/CR-GH-7 mob de-targeting). Covers
    /// all 4 blocking ACs (AC-GH-4, AC-GH-5, AC-GH-9, AC-GH-17) plus null-guard/precondition-guard
    /// coverage for every new public method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All timing is tick-based / caller-driven only — no <see cref="System.Threading.Thread.Sleep"/>
    /// anywhere in this file, matching <c>GhostSession_SnapshotWriteOrdering_tests.cs</c>'s (Story
    /// 018) own precedent.
    /// </para>
    /// <para>
    /// <b>Ordering proof idiom, matching every prior Networking Core ordering test in this
    /// codebase</b>: a <c>callOrder</c> list captures the order the caller-supplied delegates
    /// themselves fire in, and — inside each delegate closure — <c>observer.&lt;X&gt;Calls.Count</c>
    /// is asserted at that exact point to prove an <see cref="INetworkTestObserver"/> callback has
    /// (or has not) already fired relative to that delegate call.
    /// </para>
    /// <para>
    /// <b>AC-GH-9 is a composition test, not a re-derivation</b> of Story 018's AC-CGS-1
    /// HP-persistence guarantee — it calls <see cref="MobDeTargetingCoordinator.ProcessTTLExpiry"/>
    /// followed by <see cref="GhostCleanupSequencer.CompleteTTLExpiryCleanup"/> (the exact method
    /// AC-CGS-1 already tests) and asserts the same guarantee, proving this story's flow correctly
    /// composes with the already-tested sequencer.
    /// </para>
    /// <para>
    /// <b>AC-GH-17's ordering proof uses a test-constructed <c>List&lt;uint&gt;</c> standing in for
    /// "the zone tick's entity list"</b> — no <see cref="INetworkTestObserver"/> hook exists for
    /// "zone tick delivered" (confirmed; none should be added, per the story's own resolved scope
    /// note). The test proves structurally that sequencing
    /// <see cref="MobDeTargetingCoordinator.ProcessTTLExpiry"/> and
    /// <see cref="GhostCleanupSequencer.CompleteTTLExpiryCleanup"/> BEFORE building that tick's
    /// entity-ID list naturally excludes the already-cleaned-up entity.
    /// <b>Honest caveat (code review finding, Story 019 — the same class of limitation qa-tester
    /// found in Story 018's AC-CGS-3 test):</b> this proves "a correctly-ordered test constructs the
    /// correct result," not that any real production code enforces EC-GH-6's ordering requirement
    /// independently of the test author's chosen call order — no real zone-tick serializer exists
    /// yet to test against. It is a regression guard on the filter arithmetic and a documentation of
    /// the required sequencing contract, not a system-arbitrated proof. Re-verify once a real
    /// zone-tick serialization story exists to compose against.
    /// </para>
    /// <para>
    /// <b>characterId doubles as entityId/accountId's associated character</b> in every test below,
    /// the same simplification <c>GhostSession_SnapshotWriteOrdering_tests.cs</c> already makes (no
    /// Character &lt;-&gt; Entity mapping system exists yet in this codebase).
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class GhostSession_DeathDeTargeting_Tests
    {
        private const uint AccountId = 7u;
        private const uint GhostCharacterId = 555u;

        /// <summary>
        /// Advances a fresh <see cref="ConnectionStateMachine"/> to
        /// <see cref="SessionState.Disconnected_SessionActive"/> for <see cref="AccountId"/>/
        /// <see cref="GhostCharacterId"/> via the heartbeat-timeout path (Story 012), then resets
        /// <paramref name="observer"/> so the setup calls' own callbacks don't pollute a test's
        /// assertions — mirrors <c>GhostSession_SnapshotWriteOrdering_tests.cs</c>'s own
        /// <c>CreateReconnectingStateMachine</c> helper, stopping one state earlier (no
        /// <see cref="ConnectionStateMachine.EnterReconnecting"/> call).
        /// </summary>
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
        // AC-GH-4: ghost HP=1 taking >=1 damage -> death processing begins, session transitions
        // Disconnected_SessionActive -> Disconnected_SessionExpired ("GhostDeath"), persistence write
        // confirmed.
        // =========================================================================================

        [Test]
        public void CompleteGhostDeathFromDisconnected_GhostHpReachesZero_TransitionsAndPersists_AC_GH_4()
        {
            // Arrange — ghost HP=1 taking >=1 damage reaches zero (death processing begins).
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (10, 0, 20), observer);
            int newHp = ghostTracker.ApplyDamage(GhostCharacterId, currentHp: 1, damageAmount: 1);
            Assert.AreEqual(0, newHp, "Death processing begins once HP reaches zero.");

            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);
            var callOrder = new List<string>();

            // Act
            stateMachine.CompleteGhostDeathFromDisconnected(AccountId,
                persistFinalCharacterState: characterId =>
                {
                    callOrder.Add("persist");
                    Assert.AreEqual(GhostCharacterId, characterId);
                    Assert.IsEmpty(observer.PersistenceWriteCompletedCalls,
                        "OnPersistenceWriteCompleted must not have fired yet when persistence runs.");
                },
                observer);

            // Assert
            CollectionAssert.AreEqual(new[] { "persist" }, callOrder);

            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((GhostCharacterId, PersistenceWriteReason.GhostDeath), observer.PersistenceWriteCompletedCalls[0]);

            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountId, SessionState.Disconnected_SessionActive, SessionState.Disconnected_SessionExpired, "GhostDeath"),
                observer.SessionStateTransitionedCalls[0]);

            Assert.IsFalse(stateMachine.IsAccountRegistered(AccountId), "Session resources must be released once the sequence completes.");

            // NOTE (code review finding, Story 019): AC-GH-4's own pass condition also mentions
            // "post-disconnect party XP shares=0 are written" — this is correctly and explicitly out
            // of scope for this test (see the story's Out of Scope section: "XP forfeit policy
            // detail — Story 020"). No XP system exists yet in this codebase to model or assert
            // against; this is a deliberate scope boundary, not a silently skipped assertion.
        }

        // =========================================================================================
        // Null-guard / precondition-guard coverage — ConnectionStateMachine.CompleteGhostDeathFromDisconnected.
        // =========================================================================================

        [Test]
        public void CompleteGhostDeathFromDisconnected_NullPersistFinalCharacterState_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteGhostDeathFromDisconnected(AccountId, persistFinalCharacterState: null, observer));
        }

        [Test]
        public void CompleteGhostDeathFromDisconnected_AccountConnected_Throws()
        {
            // Arrange — a Connected (not yet Disconnected_SessionActive) account.
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountId, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountId, GhostCharacterId, currentTick: 0u, observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteGhostDeathFromDisconnected(AccountId,
                    persistFinalCharacterState: _ => Assert.Fail("Must not persist — the account is Connected, not Disconnected_SessionActive."),
                    observer));
        }

        [Test]
        public void CompleteGhostDeathFromDisconnected_AccountReconnecting_Throws()
        {
            // Arrange — distinguishes this method's precondition from
            // HandleGhostDeathWhileReconnecting's (Story 018): a Reconnecting account must NOT be
            // accepted here — that racing case is the other method's job.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);
            stateMachine.EnterReconnecting(AccountId, sessionExpiryTick: 6100u, observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteGhostDeathFromDisconnected(AccountId,
                    persistFinalCharacterState: _ => Assert.Fail("Must not persist — the account is Reconnecting, not Disconnected_SessionActive."),
                    observer));
        }

        // =========================================================================================
        // AC-GH-5: mob targeting an IsGhost=true character, TTL expires -> mob de-targets within
        // DE_TARGET_DEADLINE_MS (250ms default), proven structurally (no simulated delay).
        // =========================================================================================

        [Test]
        public void ProcessTTLExpiry_MobsTargetingGhost_FiresEventThenDeTargetsEachMobInOrder_AC_GH_5()
        {
            // Arrange — a test-local mob-target map standing in for the not-yet-built AI subsystem.
            var observer = new NetworkTestObserver();
            const uint mobA = 42u;
            const uint mobB = 43u;
            var mobTargets = new Dictionary<uint, uint?> { [mobA] = GhostCharacterId, [mobB] = GhostCharacterId };
            var callOrder = new List<string>();

            // Act
            MobDeTargetingCoordinator.ProcessTTLExpiry(
                entityId: GhostCharacterId,
                disconnectTickNumber: 1000u,
                expiryTick: 1600u,
                targetingMobIds: new uint[] { mobA, mobB },
                issueMobDeTargetCommand: mobId =>
                {
                    callOrder.Add($"deTarget:{mobId}");
                    Assert.AreEqual(1, observer.GhostCombatTTLExpiredCalls.Count,
                        "AC-GH-5: OnGhostCombatTTLExpired must fire before any MobDeTargetCommand is issued.");
                    mobTargets[mobId] = null;
                },
                observer);

            // Assert
            Assert.AreEqual(1, observer.GhostCombatTTLExpiredCalls.Count);
            Assert.AreEqual((GhostCharacterId, 1000u, 1600u), observer.GhostCombatTTLExpiredCalls[0]);

            CollectionAssert.AreEqual(new[] { $"deTarget:{mobA}", $"deTarget:{mobB}" }, callOrder);

            // Structural proof of the DE_TARGET_DEADLINE_MS (250ms) budget: both mobs are de-targeted
            // synchronously within this single call — no simulated delay anywhere in the sequence
            // (see MobDeTargetingCoordinator's class remarks).
            Assert.IsNull(mobTargets[mobA], "The mob's target field must be null once ProcessTTLExpiry returns.");
            Assert.IsNull(mobTargets[mobB], "The mob's target field must be null once ProcessTTLExpiry returns.");
        }

        [Test]
        public void ProcessTTLExpiry_NoMobsTargetingGhost_FiresEventOnlyNoDeTargetCommandsIssued()
        {
            // Arrange — edge case: an expired ghost with no mobs currently targeting it.
            var observer = new NetworkTestObserver();
            bool anyDeTargetIssued = false;

            // Act
            MobDeTargetingCoordinator.ProcessTTLExpiry(
                entityId: GhostCharacterId, disconnectTickNumber: 1000u, expiryTick: 1600u,
                targetingMobIds: Array.Empty<uint>(),
                issueMobDeTargetCommand: _ => anyDeTargetIssued = true,
                observer);

            // Assert
            Assert.AreEqual(1, observer.GhostCombatTTLExpiredCalls.Count);
            Assert.IsFalse(anyDeTargetIssued);
        }

        // =========================================================================================
        // Null-guard coverage — MobDeTargetingCoordinator.ProcessTTLExpiry.
        // =========================================================================================

        [Test]
        public void ProcessTTLExpiry_NullTargetingMobIds_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                MobDeTargetingCoordinator.ProcessTTLExpiry(
                    entityId: GhostCharacterId, disconnectTickNumber: 1000u, expiryTick: 1600u,
                    targetingMobIds: null,
                    issueMobDeTargetCommand: _ => { }));
        }

        [Test]
        public void ProcessTTLExpiry_NullIssueMobDeTargetCommand_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                MobDeTargetingCoordinator.ProcessTTLExpiry(
                    entityId: GhostCharacterId, disconnectTickNumber: 1000u, expiryTick: 1600u,
                    targetingMobIds: Array.Empty<uint>(),
                    issueMobDeTargetCommand: null));
        }

        // =========================================================================================
        // AC-GH-9: D ghost-period damage then TTL expiry without reconnect -> persisted HP =
        // disconnect-moment HP. Composition test — proves this story's de-targeting flow correctly
        // calls into Story 018's already-tested GhostCleanupSequencer.CompleteTTLExpiryCleanup, not a
        // re-derivation of the HP-persistence guarantee itself (see AC-CGS-1 in
        // GhostSession_SnapshotWriteOrdering_tests.cs for the original derivation).
        // =========================================================================================

        [Test]
        public void CompleteTTLExpiryCleanup_ComposedWithMobDeTargeting_PersistsDisconnectMomentHp_AC_GH_9()
        {
            // Arrange — N damage during the ghost period reduces the tracked HP; this must NOT be
            // what ultimately gets persisted.
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            const int disconnectMomentHp = 80;
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (100, 0, 250), observer);
            int ghostPeriodHp = ghostTracker.ApplyDamage(GhostCharacterId, disconnectMomentHp, damageAmount: 35); // 45 — must not be persisted
            observer.Reset(); // isolate this test's assertions from the setup calls above

            const uint mobId = 42u;
            var callOrder = new List<string>();
            int? persistedHp = null;

            // Act — this story's own de-targeting step first...
            MobDeTargetingCoordinator.ProcessTTLExpiry(
                entityId: GhostCharacterId, disconnectTickNumber: 1000u, expiryTick: 1600u,
                targetingMobIds: new uint[] { mobId },
                issueMobDeTargetCommand: _ => callOrder.Add("deTarget"),
                observer);

            // ...then Story 018's already-tested HP-persistence guarantee.
            GhostCleanupSequencer.CompleteTTLExpiryCleanup(AccountId, GhostCharacterId, disconnectMomentHp,
                persistCharacterState: hp =>
                {
                    callOrder.Add("persist");
                    persistedHp = hp;
                },
                broadcastGhostExpiredEvent: (characterId, reason) => callOrder.Add("broadcast"),
                observer);

            // Assert
            CollectionAssert.AreEqual(new[] { "deTarget", "persist", "broadcast" }, callOrder);

            Assert.AreEqual(disconnectMomentHp, persistedHp,
                "AC-GH-9: persisted HP must equal the disconnect-moment HP, not disconnectHP minus ghost-period damage.");
            Assert.AreNotEqual(ghostPeriodHp, persistedHp);

            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((GhostCharacterId, PersistenceWriteReason.GhostCombatTTLExpiry), observer.PersistenceWriteCompletedCalls[0]);
        }

        // =========================================================================================
        // AC-GH-17: ghost TTL expiry in the same server frame as a zone sync tick -> the tick
        // delivered does NOT contain the expired ghost entity; GhostExpiredEvent precedes/accompanies
        // it. Structural filter-based ordering proof (no new production code, no observer hook for
        // "zone tick delivered" — see story's own resolved scope note and this file's class remarks).
        // =========================================================================================

        [Test]
        public void SimultaneousTTLExpiryAndZoneTick_ZoneTickExcludesExpiredGhost_AC_GH_17()
        {
            // Arrange — EC-GH-6: ghost cleanup must be processed BEFORE that tick's zone-state
            // message is generated. "The zone tick's entity list" is modeled as a test-constructed
            // List<uint>; "already-expired" tracking is modeled as a HashSet<uint> populated by the
            // GhostExpiredEvent broadcast delegate.
            var observer = new NetworkTestObserver();
            const uint expiryTick = 1600u;
            var zoneEntityCandidates = new List<uint> { GhostCharacterId, 200u, 201u }; // entities alive going into tick T
            var expiredEntityIds = new HashSet<uint>();

            // Act — (1) TTL expiry signal + de-targeting (AC-GH-5's own flow).
            MobDeTargetingCoordinator.ProcessTTLExpiry(
                entityId: GhostCharacterId, disconnectTickNumber: 1000u, expiryTick: expiryTick,
                targetingMobIds: Array.Empty<uint>(),
                issueMobDeTargetCommand: _ => { },
                observer);

            Assert.AreEqual(1, observer.GhostCombatTTLExpiredCalls.Count);
            Assert.AreEqual(expiryTick, observer.GhostCombatTTLExpiredCalls[0].expiryTick,
                "OnGhostCombatTTLExpired must fire at the expiry tick.");

            // (2) Ghost cleanup (persistence + GhostExpiredEvent broadcast) — completes BEFORE this
            // tick's zone-state message is built (EC-GH-6).
            GhostCleanupSequencer.CompleteTTLExpiryCleanup(AccountId, GhostCharacterId, snapshotHp: 42,
                persistCharacterState: _ => { },
                broadcastGhostExpiredEvent: (characterId, reason) => expiredEntityIds.Add(characterId),
                observer);

            // (3) The zone tick's entity list is built only now, after cleanup — filtering out any
            // entity cleanup has already marked expired. This ordering (filter step written strictly
            // after both cleanup calls above) is the structural proof itself.
            List<uint> deliveredTickEntityIds = zoneEntityCandidates.Where(id => !expiredEntityIds.Contains(id)).ToList();

            // Assert — the tick delivered at T contains no entry for the expired ghost entity.
            CollectionAssert.DoesNotContain(deliveredTickEntityIds, GhostCharacterId);
            CollectionAssert.Contains(deliveredTickEntityIds, 200u);
            CollectionAssert.Contains(deliveredTickEntityIds, 201u);
        }
    }
}
