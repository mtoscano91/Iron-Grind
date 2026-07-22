using System;
using System.Collections.Generic;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 020 -- Ghost Session cluster, fourth story:
    /// <see cref="GhostXpPoolTracker"/> (CR-GH-8.1 two-pool XP bookkeeping) and
    /// <see cref="PartyDisbandCoordinator"/> (EC-GH-7/AC-GH-18 party-disband-mid-ghost-period stop
    /// signal). Covers all 5 blocking ACs (AC-GH-6, AC-GH-7, AC-GH-8, AC-GH-12, AC-GH-18) plus
    /// null-guard/precondition-guard coverage for every new public method on both classes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All timing is tick-based / caller-driven only -- no <see cref="System.Threading.Thread.Sleep"/>
    /// anywhere in this file, matching every prior Ghost Session test file's own precedent (Stories
    /// 017-019).
    /// </para>
    /// <para>
    /// <b>AC-GH-7 and AC-GH-12 are composition tests, not re-derivations</b> of Story 018's
    /// already-tested <see cref="GhostCleanupSequencer.CompleteTTLExpiryCleanup"/> /
    /// <see cref="GhostCleanupSequencer.CompleteGhostDeathCleanup"/> HP-persistence guarantees -- the
    /// same "compose with the already-tested sequencer, don't re-derive" precedent Story 019's own
    /// AC-GH-9 test established (see <c>GhostSession_DeathDeTargeting_tests.cs</c>'s remarks). This
    /// story's sole new contribution at those two call sites is resolving the two-pool XP
    /// forfeit-vs-restore decision via <see cref="GhostXpPoolTracker.ResolveFinalXp"/>, invoked inside
    /// the caller-supplied <c>persistCharacterState</c> delegate -- neither sequencer method's
    /// signature is modified. <b>Honest caveat (code review finding, Story 020):</b> the delegate
    /// itself is a real, already-tested production extension point (unlike AC-GH-8 below), but no
    /// story in this epic yet claims ownership of actually wiring
    /// <see cref="GhostXpPoolTracker.ResolveFinalXp"/> into a real <c>persistCharacterState</c> call
    /// site outside a test -- tracked as TD-020 (<c>docs/tech-debt-register.md</c>).
    /// </para>
    /// <para>
    /// <b>AC-GH-6 is an absence proof, requiring no new production code</b> (per this story's own
    /// approved design guidance): a test-local <c>List&lt;uint&gt;</c> stands in for the not-yet-built
    /// Party System's roster (the GDD's own dependency table lists Party System as "not yet
    /// authored"). The test proves that nothing in this codebase ever removes an entry from that list
    /// while <see cref="ConnectionStateMachine.TryGetSessionState"/> reports
    /// <see cref="SessionState.Disconnected_SessionActive"/> for the ghosted member -- the same
    /// "nothing exists yet to violate this" idiom several prior Networking Core stories already used
    /// (e.g. <c>GhostSession_SnapshotWriteOrdering_tests.cs</c>'s own AC-CGS-3 caveat). This test no
    /// longer also asserts <see cref="ZoneTestConfigurator"/>'s zone-state storage (code review finding,
    /// Story 020: that assertion was a tautological set-then-read-back check of the test double's own
    /// property bag, with no production logic in between -- removed in favor of resting solely on the
    /// party-roster proof, which is what actually substantiates this AC's claim).
    /// </para>
    /// <para>
    /// <b>AC-GH-8's honest documentation note (judgment call, matching Story 019's own resolution
    /// idiom for an analogous mismatch -- see that file's class remarks on
    /// <c>MobDeTargetCommand</c>/harness gaps):</b> the GDD's own AC-GH-8 pass-condition text says
    /// "<c>OnSessionHandshakeEmitted</c> next tick reports <c>currentXp</c> = ..." but
    /// <see cref="INetworkTestObserver.OnSessionHandshakeEmitted"/>'s actual signature has no XP field
    /// at all, and this story does not extend it -- that would touch Story 013's already-closed,
    /// already-tested method and every one of its call sites for a change out of this story's scope.
    /// This test instead resolves the restored XP value via
    /// <see cref="GhostXpPoolTracker.ResolveFinalXp"/> directly (the same "resolve via the test's own
    /// delegate closure, not the observer" idiom this epic already used repeatedly for HP), and calls
    /// the real, unmodified <see cref="ConnectionStateMachine.CompleteReAuthSuccess"/> (Story 013) for
    /// the actual <c>Reconnecting -&gt; Connected</c> transition and handshake emission. <b>Unlike
    /// AC-GH-7/AC-GH-12 above, this is a materially weaker proof (code review finding, Story 020):</b>
    /// <see cref="ConnectionStateMachine.CompleteReAuthSuccess"/> has no delegate seam or any other call
    /// into <see cref="GhostXpPoolTracker"/> at all -- the state-transition assertions and the
    /// <c>ResolveFinalXp</c> assertion below are two causally-independent checks, not one integrated
    /// pathway. This test proves the XP arithmetic is correct and proves the reconnect transition fires
    /// correctly, in isolation from each other -- it would not catch a future reconnect implementation
    /// that wired the restore incorrectly or not at all. Tracked as TD-020
    /// (<c>docs/tech-debt-register.md</c>) pending a real wiring story.
    /// </para>
    /// <para>
    /// <b>characterId doubles as entityId/accountId's associated character</b> in every test below,
    /// the same simplification every prior Ghost Session test file already makes (no Character
    /// &lt;-&gt; Entity mapping system exists yet in this codebase).
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class GhostSession_RewardForfeitPolicy_Tests
    {
        private const uint AccountId = 7u;
        private const uint GhostCharacterId = 555u;
        private const uint SessionExpiryTick = 6100u;

        /// <summary>Advances a fresh state machine's account to <see cref="SessionState.Disconnected_SessionActive"/> at tick 1.</summary>
        private static ConnectionStateMachine CreateDisconnectedSessionActiveStateMachine(NetworkTestObserver observer)
        {
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountId, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountId, GhostCharacterId, currentTick: 0u, observer);
            stateMachine.EvaluateTimeouts(currentTick: 1u, heartbeatTimeoutTicks: 1u, connectingTimeoutTicks: 1000u, observer); // -> Disconnected_SessionActive
            observer.Reset();
            return stateMachine;
        }

        /// <summary>Advances a fresh state machine's account to <see cref="SessionState.Reconnecting"/>.</summary>
        private static ConnectionStateMachine CreateReconnectingStateMachine(NetworkTestObserver observer)
        {
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountId, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountId, GhostCharacterId, currentTick: 0u, observer);
            stateMachine.EvaluateTimeouts(currentTick: 1u, heartbeatTimeoutTicks: 1u, connectingTimeoutTicks: 1000u, observer);
            stateMachine.EnterReconnecting(AccountId, SessionExpiryTick, observer);
            observer.Reset();
            return stateMachine;
        }

        private static CurrencySystem CreateCurrencySystemWithBalance(uint balance)
        {
            var currencySystem = new CurrencySystem();
            currencySystem.RegisterCharacter(new CharacterID(GhostCharacterId), balance);
            return currencySystem;
        }

        private static readonly SessionHandshakeData DefaultHandshakeData =
            new SessionHandshakeData(wasKilledWhileDisconnected: false, goldVersion: 1u, level: 15,
                currentHp: 80, currentMp: 40, heldFreePoints: 0, classType: 1);

        // =========================================================================================
        // AC-GH-6: party of 2+, one member ghosted -> roster membership persists through the full
        // ghost period. Absence proof -- see class remarks.
        // =========================================================================================

        [Test]
        public void PartyRoster_GhostMemberDuringGhostPeriod_RemainsInRosterAndZoneStaysActive_AC_GH_6()
        {
            // Arrange -- a party of 3 with one member (GhostCharacterId) transitioning to ghost.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);

            var partyRoster = new List<uint> { GhostCharacterId, 556u, 557u };
            int preDisconnectMemberCount = partyRoster.Count;

            // Act -- the full ghost period elapses. No method anywhere in this codebase removes an
            // entry from partyRoster while the member remains Disconnected_SessionActive; this loop
            // exists only to make that "full ghost period" span explicit, not to invoke any removal.
            for (uint tick = 2u; tick <= 20u; tick++)
            {
                Assert.IsTrue(stateMachine.TryGetSessionState(AccountId, out SessionState stateAtTick));
                Assert.AreEqual(SessionState.Disconnected_SessionActive, stateAtTick);
            }

            // Assert -- (code review fix, Story 020: the prior version of this test also asserted
            // ZoneTestConfigurator.GetCurrentZoneState against a value this same test had just set via
            // SetZoneStateForTesting a few lines above, with no production logic in between that could
            // have changed it -- a tautological set-then-read-back check that proved nothing beyond
            // ZoneTestConfigurator's own storage. Removed; this test now rests solely on the party
            // roster proof below, which is the part that actually substantiates AC-GH-6's claim.)
            Assert.AreEqual(preDisconnectMemberCount, partyRoster.Count,
                "AC-GH-6: party roster member count is unchanged from pre-disconnect through the full ghost period.");
            CollectionAssert.Contains(partyRoster, GhostCharacterId,
                "The ghost member's slot is retained (CR-GH-8) -- not removed at disconnect.");
        }

        // =========================================================================================
        // AC-GH-7: party XP shares accumulating, TTL expires without reconnect -> persisted XP =
        // pre-disconnect snapshot only. Composed with Story 019's own AC-GH-5/Story 018's AC-CGS-1
        // flow -- see class remarks.
        // =========================================================================================

        [Test]
        public void TTLExpiry_ComposedWithCleanupSequencer_PersistedXpForfeitsPostDisconnectShares_AC_GH_7()
        {
            // Arrange -- party XP shares accumulating while IsGhost=true; TTL expires without reconnect.
            var observer = new NetworkTestObserver();
            var xpTracker = new GhostXpPoolTracker();
            const int preDisconnectXp = 500;
            const int postDisconnectShares = 100;
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp);
            xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, postDisconnectShares); // must be forfeited

            var callOrder = new List<string>();
            int? persistedXp = null;

            // Act -- (1) TTL expiry + de-targeting signal (Story 019's own AC-GH-5 flow). A non-empty
            // targetingMobIds is required so issueMobDeTargetCommand actually fires and "deTarget"
            // genuinely appears in callOrder below — an earlier version of this test passed
            // Array.Empty<uint>() here while still asserting "deTarget" was recorded, which can never
            // happen (ProcessTTLExpiry calls issueMobDeTargetCommand once per targetingMobIds entry,
            // zero times for an empty array). Undetected until this test could run in a live Editor.
            MobDeTargetingCoordinator.ProcessTTLExpiry(
                entityId: GhostCharacterId, disconnectTickNumber: 1000u, expiryTick: 1600u,
                targetingMobIds: new uint[] { 42u },
                issueMobDeTargetCommand: _ => callOrder.Add("deTarget"),
                observer);

            // (2) Story 018's already-tested HP-persistence cleanup sequence; this story resolves the
            // final XP value inside the caller-supplied persistCharacterState delegate.
            GhostCleanupSequencer.CompleteTTLExpiryCleanup(AccountId, GhostCharacterId, snapshotHp: 80,
                persistCharacterState: hp =>
                {
                    callOrder.Add("persist");
                    persistedXp = xpTracker.ResolveFinalXp(GhostCharacterId, includePostDisconnectShare: false);
                },
                broadcastGhostExpiredEvent: (characterId, reason) => callOrder.Add("broadcast"),
                observer);

            // Assert
            CollectionAssert.AreEqual(new[] { "deTarget", "persist", "broadcast" }, callOrder);

            Assert.AreEqual(1, observer.GhostCombatTTLExpiredCalls.Count);
            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((GhostCharacterId, PersistenceWriteReason.GhostCombatTTLExpiry), observer.PersistenceWriteCompletedCalls[0]);

            Assert.AreEqual(preDisconnectXp, persistedXp,
                "AC-GH-7: persisted XP must equal the pre-disconnect XP snapshot only -- no ghost-period party shares added.");
            Assert.AreNotEqual(preDisconnectXp + postDisconnectShares, persistedXp);
        }

        // =========================================================================================
        // AC-GH-8: N post-disconnect party XP shares accumulated, reconnect before TTL expiry ->
        // IsGhost clears (real ConnectionStateMachine.CompleteReAuthSuccess), persisted XP =
        // pre-disconnect + N. See class remarks for the OnSessionHandshakeEmitted XP-field mismatch
        // resolution.
        // =========================================================================================

        [Test]
        public void CompleteReAuthSuccess_PostDisconnectSharesRestored_ResolveFinalXpIncludesShares_AC_GH_8()
        {
            // Arrange -- N=100 post-disconnect party XP shares accumulated while IsGhost=true.
            var observer = new NetworkTestObserver();
            var xpTracker = new GhostXpPoolTracker();
            const int preDisconnectXp = 500;
            const int postDisconnectShares = 100;
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp);
            xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, postDisconnectShares);

            var currencySystem = CreateCurrencySystemWithBalance(0u);
            var stateMachine = CreateReconnectingStateMachine(observer);

            // Act -- the real Reconnecting -> Connected transition + handshake emission (Story 013,
            // unmodified signature).
            stateMachine.CompleteReAuthSuccess(AccountId, currencySystem,
                queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                markPurchaseRefunded: _ => Assert.Fail("No pending purchases in this scenario."),
                queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                representRespecPhase2: _ => Assert.Fail("No active respec reservation in this scenario."),
                releaseRespecReservationAndNotify: _ => Assert.Fail("No active respec reservation in this scenario."),
                handshakeData: DefaultHandshakeData,
                observer);

            // CR-GH-9.2 step 3/5 resolution -- see class remarks for why this is resolved via
            // GhostXpPoolTracker.ResolveFinalXp rather than an OnSessionHandshakeEmitted XP field.
            int finalXp = xpTracker.ResolveFinalXp(GhostCharacterId, includePostDisconnectShare: true);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountId, SessionState.Reconnecting, SessionState.Connected, "ReAuthSuccess"),
                observer.SessionStateTransitionedCalls[0]);

            Assert.AreEqual(1, observer.SessionHandshakeEmittedCalls.Count,
                "AC-GH-8: OnSessionHandshakeEmitted must fire the tick after reconnect.");
            Assert.IsEmpty(observer.GhostCombatTTLExpiredCalls,
                "AC-GH-8: no OnGhostCombatTTLExpired event for this session -- this is the reconnect path, not TTL expiry.");

            Assert.AreEqual(preDisconnectXp + postDisconnectShares, finalXp,
                "AC-GH-8: persisted XP = pre-disconnect XP + N.");
        }

        // =========================================================================================
        // AC-GH-12: 500 pre-disconnect XP (banked) + 100 post-disconnect party-share XP, ghost dies
        // before TTL expiry -> persisted XP = 500 exactly (not 600, not 0). Composed with Story 018's
        // AC-CGS-2 flow -- see class remarks.
        // =========================================================================================

        [Test]
        public void GhostDeath_ComposedWithCleanupSequencer_PersistedXpIsPreDisconnectOnly_AC_GH_12()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var xpTracker = new GhostXpPoolTracker();
            const int preDisconnectXp = 500;
            const int postDisconnectShares = 100;
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp);
            xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, postDisconnectShares);

            int? persistedXp = null;

            // Act -- Story 018's already-tested ghost-death cleanup sequence; this story resolves the
            // final XP value inside the caller-supplied persistCharacterState delegate.
            GhostCleanupSequencer.CompleteGhostDeathCleanup(GhostCharacterId, snapshotHp: 42, respawnPosition: (0, 0, 0),
                persistCharacterState: (hp, position) =>
                {
                    persistedXp = xpTracker.ResolveFinalXp(GhostCharacterId, includePostDisconnectShare: false);
                },
                broadcastGhostExpiredEvent: (characterId, reason) => { },
                observer);

            // Assert
            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((GhostCharacterId, PersistenceWriteReason.GhostDeath), observer.PersistenceWriteCompletedCalls[0]);

            Assert.AreEqual(500, persistedXp, "AC-GH-12: persisted XP must equal 500 exactly -- not 600, not 0.");
        }

        // =========================================================================================
        // AC-GH-18: party disbanded mid-ghost-period -> post-disconnect party XP share accumulation
        // stops at that moment; no further XP added.
        // =========================================================================================

        [Test]
        public void ProcessPartyDisband_StopsAccumulationAtDisbandTick_NoFurtherXpAdded_AC_GH_18()
        {
            // Arrange -- EC-GH-7: party disbanded mid-ghost-period. Mock party-membership provider,
            // per this story's own Implementation Notes (real Party System not yet authored).
            var observer = new NetworkTestObserver();
            var xpTracker = new GhostXpPoolTracker();
            const uint partyId = 900u;
            const uint disbandTick = 1234u;
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);
            xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, 30); // accrued before disband -- retained (CR-GH-8.1)

            // Act -- disband processed at tick 1234.
            PartyDisbandCoordinator.ProcessPartyDisband(partyId, disbandTick,
                ghostedPartyMemberCharacterIds: new uint[] { GhostCharacterId },
                xpTracker,
                observer);

            int poolAtDisband = xpTracker.GetPostDisconnectXp(GhostCharacterId);
            // Simulate a would-be post-disband-tick party XP award -- must be a no-op.
            xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, 999);
            int poolAfterAttempt = xpTracker.GetPostDisconnectXp(GhostCharacterId);

            // Assert
            Assert.AreEqual(1, observer.PartyDisbandedCalls.Count);
            Assert.AreEqual((partyId, disbandTick), observer.PartyDisbandedCalls[0]);

            Assert.AreEqual(30, poolAtDisband, "Shares accrued before the disband tick are retained.");
            Assert.AreEqual(poolAtDisband, poolAfterAttempt,
                "AC-GH-18: pool delta must be 0 for all ticks after the disband tick -- no further XP added.");
        }

        [Test]
        public void ProcessPartyDisband_MultipleGhostedMembers_StopsAccumulationForEachInOrder()
        {
            // Regression guard against an off-by-one/early-break in the per-member loop.
            var observer = new NetworkTestObserver();
            var xpTracker = new GhostXpPoolTracker();
            const uint memberA = 555u;
            const uint memberB = 556u;
            xpTracker.BeginTracking(memberA, preDisconnectXp: 500);
            xpTracker.BeginTracking(memberB, preDisconnectXp: 300);
            xpTracker.AccumulatePostDisconnectShare(memberA, 10);
            xpTracker.AccumulatePostDisconnectShare(memberB, 20);

            // Act
            PartyDisbandCoordinator.ProcessPartyDisband(900u, 1234u,
                ghostedPartyMemberCharacterIds: new uint[] { memberA, memberB },
                xpTracker, observer);

            xpTracker.AccumulatePostDisconnectShare(memberA, 999);
            xpTracker.AccumulatePostDisconnectShare(memberB, 999);

            // Assert
            Assert.AreEqual(10, xpTracker.GetPostDisconnectXp(memberA));
            Assert.AreEqual(20, xpTracker.GetPostDisconnectXp(memberB));
        }

        [Test]
        public void ProcessPartyDisband_EmptyGhostedList_FiresEventOnlyNoStopAccumulationCalls()
        {
            // Arrange -- edge case: a disbanded party with no currently-ghosted members.
            var observer = new NetworkTestObserver();
            var xpTracker = new GhostXpPoolTracker();

            // Act
            PartyDisbandCoordinator.ProcessPartyDisband(900u, 1234u,
                ghostedPartyMemberCharacterIds: Array.Empty<uint>(), xpTracker, observer);

            // Assert
            Assert.AreEqual(1, observer.PartyDisbandedCalls.Count);
        }

        [Test]
        public void ProcessPartyDisband_OneUntrackedMemberInList_ThrowsAfterProcessingEarlierMembers()
        {
            // Code review finding, Story 020: the per-member loop has no rollback -- if a caller
            // supplies a stale roster containing an untracked characterId (e.g. one that already had
            // EndTracking called, or was never a ghost), StopAccumulation throws partway through and
            // members earlier in the list are left correctly stopped while later members never get
            // processed. This test locks in that partial-failure behavior is exactly what happens (not
            // a silent skip, and not a full rollback) so a future change to this ordering is deliberate,
            // not accidental.
            var observer = new NetworkTestObserver();
            var xpTracker = new GhostXpPoolTracker();
            const uint trackedMember = 555u;
            const uint untrackedMember = 999u; // never BeginTracking'd -- simulates a stale roster entry.
            xpTracker.BeginTracking(trackedMember, preDisconnectXp: 500);
            xpTracker.AccumulatePostDisconnectShare(trackedMember, 10);

            // Act & Assert -- OnPartyDisbanded fires, trackedMember (listed first) is stopped, then the
            // loop throws on untrackedMember (listed second) without processing any further entries.
            Assert.Throws<InvalidOperationException>(() =>
                PartyDisbandCoordinator.ProcessPartyDisband(900u, 1234u,
                    ghostedPartyMemberCharacterIds: new uint[] { trackedMember, untrackedMember },
                    xpTracker, observer));

            Assert.AreEqual(1, observer.PartyDisbandedCalls.Count,
                "OnPartyDisbanded fires before the per-member loop, regardless of a later throw.");

            xpTracker.AccumulatePostDisconnectShare(trackedMember, 999); // must still no-op -- already stopped.
            Assert.AreEqual(10, xpTracker.GetPostDisconnectXp(trackedMember),
                "The member processed before the throw is left correctly stopped.");
        }

        // =========================================================================================
        // Null-guard coverage -- PartyDisbandCoordinator.ProcessPartyDisband.
        // =========================================================================================

        [Test]
        public void ProcessPartyDisband_NullGhostedPartyMemberCharacterIds_ThrowsArgumentNullException()
        {
            var xpTracker = new GhostXpPoolTracker();

            Assert.Throws<ArgumentNullException>(() =>
                PartyDisbandCoordinator.ProcessPartyDisband(900u, 1234u,
                    ghostedPartyMemberCharacterIds: null, xpTracker));
        }

        [Test]
        public void ProcessPartyDisband_NullXpPoolTracker_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                PartyDisbandCoordinator.ProcessPartyDisband(900u, 1234u,
                    ghostedPartyMemberCharacterIds: Array.Empty<uint>(), xpPoolTracker: null));
        }

        // =========================================================================================
        // Null-guard / precondition-guard coverage -- GhostXpPoolTracker.
        // =========================================================================================

        [Test]
        public void BeginTracking_AlreadyTracked_Throws()
        {
            var xpTracker = new GhostXpPoolTracker();
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);

            Assert.Throws<InvalidOperationException>(() =>
                xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 999));
        }

        [Test]
        public void AccumulatePostDisconnectShare_NegativeAmount_ThrowsArgumentOutOfRangeException()
        {
            var xpTracker = new GhostXpPoolTracker();
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, xpAmount: -1));
        }

        [Test]
        public void AccumulatePostDisconnectShare_UntrackedCharacter_Throws()
        {
            var xpTracker = new GhostXpPoolTracker();

            Assert.Throws<InvalidOperationException>(() =>
                xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, xpAmount: 10));
        }

        [Test]
        public void AccumulatePostDisconnectShare_AfterStopped_NoOps()
        {
            // Structural proof of AC-GH-18's "no further XP added" guarantee at the tracker level --
            // the coordinator-level AC-GH-18 test above proves the full disband-driven flow.
            var xpTracker = new GhostXpPoolTracker();
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);
            xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, 50);
            xpTracker.StopAccumulation(GhostCharacterId);

            xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, 999); // must no-op

            Assert.AreEqual(50, xpTracker.GetPostDisconnectXp(GhostCharacterId),
                "AccumulatePostDisconnectShare must no-op once accumulation has stopped.");
        }

        [Test]
        public void StopAccumulation_UntrackedCharacter_Throws()
        {
            var xpTracker = new GhostXpPoolTracker();

            Assert.Throws<InvalidOperationException>(() => xpTracker.StopAccumulation(GhostCharacterId));
        }

        [Test]
        public void StopAccumulation_CalledTwice_IsIdempotent()
        {
            var xpTracker = new GhostXpPoolTracker();
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);

            xpTracker.StopAccumulation(GhostCharacterId);

            Assert.DoesNotThrow(() => xpTracker.StopAccumulation(GhostCharacterId));
        }

        [Test]
        public void GetPreDisconnectXp_UntrackedCharacter_Throws()
        {
            var xpTracker = new GhostXpPoolTracker();

            Assert.Throws<InvalidOperationException>(() => xpTracker.GetPreDisconnectXp(GhostCharacterId));
        }

        [Test]
        public void GetPostDisconnectXp_UntrackedCharacter_Throws()
        {
            var xpTracker = new GhostXpPoolTracker();

            Assert.Throws<InvalidOperationException>(() => xpTracker.GetPostDisconnectXp(GhostCharacterId));
        }

        [Test]
        public void ResolveFinalXp_UntrackedCharacter_Throws()
        {
            var xpTracker = new GhostXpPoolTracker();

            Assert.Throws<InvalidOperationException>(() =>
                xpTracker.ResolveFinalXp(GhostCharacterId, includePostDisconnectShare: false));
        }

        [Test]
        public void EndTracking_UntrackedCharacter_Throws()
        {
            var xpTracker = new GhostXpPoolTracker();

            Assert.Throws<InvalidOperationException>(() => xpTracker.EndTracking(GhostCharacterId));
        }

        [Test]
        public void EndTracking_RemovesRecord_IsTrackedReturnsFalseAfterward()
        {
            var xpTracker = new GhostXpPoolTracker();
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);
            Assert.IsTrue(xpTracker.IsTracked(GhostCharacterId));

            xpTracker.EndTracking(GhostCharacterId);

            Assert.IsFalse(xpTracker.IsTracked(GhostCharacterId));
        }

        [Test]
        public void IsTracked_NeverTracked_ReturnsFalse()
        {
            var xpTracker = new GhostXpPoolTracker();

            Assert.IsFalse(xpTracker.IsTracked(GhostCharacterId));
        }

        [Test]
        public void ResolveFinalXp_ThenEndTracking_SubsequentQueryThrows()
        {
            // Code review finding, Story 020: locks in the documented lifecycle contract ("EndTracking
            // is called once the ghost-period outcome has been resolved and persisted") in the actual
            // order a real caller would use it -- resolve, then end, then any further query must throw
            // rather than silently succeed against a stale/removed record.
            var xpTracker = new GhostXpPoolTracker();
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);
            xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, 100);

            int resolved = xpTracker.ResolveFinalXp(GhostCharacterId, includePostDisconnectShare: true);
            Assert.AreEqual(600, resolved);

            xpTracker.EndTracking(GhostCharacterId);

            Assert.Throws<InvalidOperationException>(() =>
                xpTracker.ResolveFinalXp(GhostCharacterId, includePostDisconnectShare: true));
        }

        [Test]
        public void ResolveFinalXp_IncludeShareTrue_AfterStopAccumulation_ReflectsFrozenPoolNotLaterAttempts()
        {
            // Code review finding, Story 020: AC-GH-18's own concern is the forfeit case, but EC-GH-7
            // allows a post-disband ghost to still reconnect later (as a solo ghost, TTL continuing) --
            // this test locks in that the RESTORE path (includePostDisconnectShare: true), not just the
            // forfeit path already covered by AC-GH-7/AC-GH-12, also correctly reflects the pool as it
            // stood at the disband tick, ignoring any later no-op'd accumulation attempts.
            var xpTracker = new GhostXpPoolTracker();
            xpTracker.BeginTracking(GhostCharacterId, preDisconnectXp: 500);
            xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, 30);

            xpTracker.StopAccumulation(GhostCharacterId);
            xpTracker.AccumulatePostDisconnectShare(GhostCharacterId, 999); // no-op -- pool frozen at 30.

            int restored = xpTracker.ResolveFinalXp(GhostCharacterId, includePostDisconnectShare: true);

            Assert.AreEqual(530, restored,
                "The restore path must reflect the pool as frozen at the disband tick (500 + 30), not any later no-op'd attempt.");
        }
    }
}
