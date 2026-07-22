using System;
using System.Collections.Generic;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 018 — Ghost Session cluster, second story:
    /// <see cref="PreDisconnectSnapshotWal"/> (CGS-3's external WAL, EC-CGS-2 idempotency),
    /// <see cref="GhostCleanupSequencer"/> (CGS-4/CGS-5 write-ordering), and
    /// <see cref="ConnectionStateMachine.HandleGhostDeathWhileReconnecting"/> (AC-CGS-3, CGS-6's
    /// single-tick death-vs-reconnect priority rule). Covers all 4 blocking ACs (AC-CGS-1 through
    /// AC-CGS-4) plus null-guard/precondition-guard coverage for every new public method across all
    /// 3 new pieces, plus the EC-CGS-2 idempotent-write test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All timing is tick-based / caller-driven only — no <see cref="System.Threading.Thread.Sleep"/>
    /// anywhere in this file, matching <c>GhostSession_PromotionStateConstraints_tests.cs</c>'s own
    /// precedent.
    /// </para>
    /// <para>
    /// <b>Ordering proof idiom, matching every prior Networking Core ordering test in this
    /// codebase</b> (<c>ProcessExplicitDisconnect</c>, <c>CompleteSessionActiveTTLExpiry</c>,
    /// <c>CommitBeforeBroadcastSequencer.Execute</c>): a <c>callOrder</c> list captures the order the
    /// caller-supplied delegates themselves fire in, and — inside each delegate closure —
    /// <c>observer.&lt;X&gt;Calls.Count</c> is asserted at that exact point to prove an
    /// <see cref="INetworkTestObserver"/> callback has (or has not) already fired relative to that
    /// delegate call. Because the entire sequence is synchronous, a list's <c>Count</c> at any point
    /// during the call IS a sequence-index proof — exactly what AC-CGS-4's "verified by sequence-index
    /// ordering, not timestamps" pass condition asks for (events within the same 50ms tick have no
    /// meaningful timestamp ordering).
    /// </para>
    /// <para>
    /// <b>AC-CGS-4 additionally exercises the named "Automatable via
    /// <see cref="IServerCrashInjector.RegisterCrashAt"/>" technique</b> — a simulated crash immediately
    /// after the (mock) persistence write, proving the write is durable ("survives the crash") even
    /// though nothing further in the sequence (session transition, broadcast) executes. Mirrors
    /// <c>Session_TTLExpiry_ZoneCrash_tests.cs</c>'s own crash-after-persistence-write scenario shape.
    /// </para>
    /// <para>
    /// <b>characterId doubles as entityId/accountId's associated character</b> in every test below,
    /// the same simplification <c>GhostSession_PromotionStateConstraints_tests.cs</c> already makes
    /// (no Character &lt;-&gt; Entity mapping system exists yet in this codebase).
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class GhostSession_SnapshotWriteOrdering_Tests
    {
        private const uint AccountId = 7u;
        private const uint GhostCharacterId = 555u;
        private const uint DisconnectTickNumber = 1000u;

        private static readonly (short x, short y, short z) RespawnPosition = (0, 0, 10);

        /// <summary>
        /// Advances a fresh <see cref="ConnectionStateMachine"/> to <see cref="SessionState.Reconnecting"/>
        /// for <see cref="AccountId"/>/<see cref="GhostCharacterId"/> via the heartbeat-timeout path
        /// (Story 012) then <see cref="ConnectionStateMachine.EnterReconnecting"/> (Story 013), then
        /// resets <paramref name="observer"/> so the setup calls' own callbacks don't pollute a
        /// test's assertions — mirroring <c>Session_ConnectionStateMachine_Reconnect_tests.cs</c>'s
        /// own <c>CreateReconnectingStateMachine</c> helper.
        /// </summary>
        private static ConnectionStateMachine CreateReconnectingStateMachine(NetworkTestObserver observer)
        {
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountId, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountId, GhostCharacterId, currentTick: 0u, observer);
            stateMachine.EvaluateTimeouts(currentTick: 1u, heartbeatTimeoutTicks: 1u, connectingTimeoutTicks: 1000u, observer); // -> Disconnected_SessionActive
            stateMachine.EnterReconnecting(AccountId, sessionExpiryTick: 6100u, observer); // -> Reconnecting
            observer.Reset();
            return stateMachine;
        }

        // =========================================================================================
        // PreDisconnectSnapshotWal — EC-CGS-2 idempotency + precondition-guard coverage.
        // =========================================================================================

        [Test]
        public void TryWriteSnapshot_NoExistingEntry_WritesAndReturnsTrue()
        {
            // Arrange
            var wal = new PreDisconnectSnapshotWal();
            var snapshot = new PreDisconnectSnapshot(hp: 80, respawnPosition: RespawnPosition);

            // Act
            bool wrote = wal.TryWriteSnapshot(GhostCharacterId, DisconnectTickNumber, snapshot);

            // Assert
            Assert.IsTrue(wrote);
            Assert.IsTrue(wal.TryGetSnapshot(GhostCharacterId, out PreDisconnectSnapshot stored));
            Assert.AreEqual(80, stored.Hp);
            Assert.AreEqual(RespawnPosition, stored.RespawnPosition);
        }

        [Test]
        public void TryWriteSnapshot_IdenticalResubmission_IsNoOpAndEntryUntouched_EC_CGS_2()
        {
            // Arrange — models a re-submitted identical snapshot after a simulated crash mid-write
            // (EC-CGS-2's own named scenario).
            var wal = new PreDisconnectSnapshotWal();
            var originalSnapshot = new PreDisconnectSnapshot(hp: 80, respawnPosition: RespawnPosition);
            Assert.IsTrue(wal.TryWriteSnapshot(GhostCharacterId, DisconnectTickNumber, originalSnapshot));

            var resubmittedSnapshot = new PreDisconnectSnapshot(hp: 999, respawnPosition: (1, 2, 3));

            // Act — same characterId AND same disconnectTickNumber, but with different (bogus) HP/position
            // data, proving the resubmission is rejected as a no-op rather than blindly overwriting.
            bool wroteAgain = wal.TryWriteSnapshot(GhostCharacterId, DisconnectTickNumber, resubmittedSnapshot);

            // Assert
            Assert.IsFalse(wroteAgain, "EC-CGS-2: an identical re-submission (same characterId + same disconnectTickNumber) must be a no-op.");
            Assert.IsTrue(wal.TryGetSnapshot(GhostCharacterId, out PreDisconnectSnapshot stored));
            Assert.AreEqual(80, stored.Hp, "The original entry must be untouched by the no-op resubmission.");
            Assert.AreEqual(RespawnPosition, stored.RespawnPosition, "The original entry must be untouched by the no-op resubmission.");
        }

        [Test]
        public void TryWriteSnapshot_DifferentDisconnectTickNumber_OverwritesAndReturnsTrue()
        {
            // Arrange — a genuinely new ghost period for the same character (a different
            // disconnectTickNumber) must overwrite, not be treated as a duplicate.
            var wal = new PreDisconnectSnapshotWal();
            wal.TryWriteSnapshot(GhostCharacterId, DisconnectTickNumber, new PreDisconnectSnapshot(hp: 80, respawnPosition: RespawnPosition));

            var newSnapshot = new PreDisconnectSnapshot(hp: 55, respawnPosition: (9, 9, 9));

            // Act
            bool wroteAgain = wal.TryWriteSnapshot(GhostCharacterId, DisconnectTickNumber + 500u, newSnapshot);

            // Assert
            Assert.IsTrue(wroteAgain, "A different disconnectTickNumber for the same character is a new ghost period, not a duplicate.");
            Assert.IsTrue(wal.TryGetSnapshot(GhostCharacterId, out PreDisconnectSnapshot stored));
            Assert.AreEqual(55, stored.Hp);
            // RespawnPosition is (short, short, short); Assert.AreEqual's `object` parameters give the
            // (9, 9, 9) literal no target-typing context, so it defaults to (int, int, int) — a
            // different ValueTuple type with identical ToString() output, so the assertion failed with
            // "Expected: (9, 9, 9), But was: (9, 9, 9)" despite the values matching. Explicit casts
            // force the correct tuple type.
            Assert.AreEqual(((short)9, (short)9, (short)9), stored.RespawnPosition);
        }

        [Test]
        public void TryGetSnapshot_UnregisteredCharacter_ReturnsFalseAndDefault()
        {
            // Arrange
            var wal = new PreDisconnectSnapshotWal();

            // Act
            bool found = wal.TryGetSnapshot(GhostCharacterId, out PreDisconnectSnapshot snapshot);

            // Assert
            Assert.IsFalse(found);
            Assert.AreEqual(default(PreDisconnectSnapshot).Hp, snapshot.Hp);
            Assert.AreEqual(default(PreDisconnectSnapshot).RespawnPosition, snapshot.RespawnPosition);
        }

        [Test]
        public void TryWriteSnapshot_TwoDifferentCharacters_NoCrossCharacterInterference()
        {
            // Arrange — code review coverage gap (Story 018): every other test in this fixture used
            // only one character. This proves cross-character isolation, the same recurring gap class
            // code review caught in Stories 016/017 (per-character/per-account dedup scoping).
            const uint characterA = GhostCharacterId;
            const uint characterB = 556u;
            var wal = new PreDisconnectSnapshotWal();
            var snapshotA = new PreDisconnectSnapshot(hp: 80, respawnPosition: RespawnPosition);
            var snapshotB = new PreDisconnectSnapshot(hp: 30, respawnPosition: (5, 5, 5));

            // Act
            bool wroteA = wal.TryWriteSnapshot(characterA, DisconnectTickNumber, snapshotA);
            bool wroteB = wal.TryWriteSnapshot(characterB, DisconnectTickNumber, snapshotB);

            // Overwriting character B must not affect character A's entry.
            bool wroteB2 = wal.TryWriteSnapshot(characterB, DisconnectTickNumber + 500u, new PreDisconnectSnapshot(hp: 1, respawnPosition: (1, 1, 1)));

            // Assert
            Assert.IsTrue(wroteA);
            Assert.IsTrue(wroteB);
            Assert.IsTrue(wroteB2);

            Assert.IsTrue(wal.TryGetSnapshot(characterA, out PreDisconnectSnapshot storedA));
            Assert.AreEqual(80, storedA.Hp, "Character A's snapshot must be unaffected by any write to character B.");
            Assert.AreEqual(RespawnPosition, storedA.RespawnPosition);

            Assert.IsTrue(wal.TryGetSnapshot(characterB, out PreDisconnectSnapshot storedB));
            Assert.AreEqual(1, storedB.Hp, "Character B's entry reflects its own most recent write.");
        }

        // =========================================================================================
        // AC-CGS-1: ghost-period damage then TTL expiry -> persisted HP = pre-disconnect snapshot HP,
        // not the ghost-period-reduced HP.
        // =========================================================================================

        [Test]
        public void CompleteTTLExpiryCleanup_GhostPeriodDamageThenTTLExpiry_PersistsPreDisconnectSnapshotHp_AC_CGS_1()
        {
            // Arrange — N damage during the ghost period reduces the tracked HP; this must NOT be
            // what ultimately gets persisted.
            var observer = new NetworkTestObserver();
            var ghostTracker = new GhostEntityTracker();
            const int preDisconnectHp = 80;
            ghostTracker.PromoteToGhost(GhostCharacterId, frozenPosition: (100, 0, 250), observer);
            int ghostPeriodHp = ghostTracker.ApplyDamage(GhostCharacterId, preDisconnectHp, damageAmount: 35); // 45 — must not be persisted

            var wal = new PreDisconnectSnapshotWal();
            wal.TryWriteSnapshot(GhostCharacterId, DisconnectTickNumber, new PreDisconnectSnapshot(preDisconnectHp, RespawnPosition));
            observer.Reset(); // isolate this test's assertions from the setup calls above

            Assert.IsTrue(wal.TryGetSnapshot(GhostCharacterId, out PreDisconnectSnapshot snapshot));
            var callOrder = new List<string>();
            int? persistedHp = null;

            // Act
            GhostCleanupSequencer.CompleteTTLExpiryCleanup(AccountId, GhostCharacterId, snapshot.Hp,
                persistCharacterState: hp =>
                {
                    callOrder.Add("persist");
                    persistedHp = hp;
                    Assert.IsEmpty(observer.PersistenceWriteCompletedCalls,
                        "OnPersistenceWriteCompleted must not have fired yet when persistence runs.");
                },
                broadcastGhostExpiredEvent: (characterId, reason) =>
                {
                    callOrder.Add("broadcast");
                    Assert.AreEqual(GhostCharacterId, characterId);
                    Assert.AreEqual("GhostTtlExpired", reason);
                    Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count,
                        "AC-CGS-1: OnPersistenceWriteCompleted must fire before the GhostExpiredEvent broadcast.");
                    Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count,
                        "The session transition must fire before the GhostExpiredEvent broadcast.");
                },
                observer);

            // Assert — persisted HP is the pre-disconnect snapshot HP, never the ghost-period-reduced HP.
            Assert.AreEqual(preDisconnectHp, persistedHp,
                "AC-CGS-1: persisted HP must equal the pre-disconnect snapshot HP, not the ghost-period-reduced HP.");
            Assert.AreNotEqual(ghostPeriodHp, persistedHp);

            CollectionAssert.AreEqual(new[] { "persist", "broadcast" }, callOrder);

            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((GhostCharacterId, PersistenceWriteReason.GhostCombatTTLExpiry), observer.PersistenceWriteCompletedCalls[0]);

            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountId, SessionState.Disconnected_SessionActive, SessionState.Disconnected_SessionExpired, "TTLExpired"),
                observer.SessionStateTransitionedCalls[0]);
        }

        // =========================================================================================
        // AC-CGS-2: ghost death -> persisted HP = pre-disconnect snapshot HP (no penalty), persisted
        // position = zone-entry respawn position, wasKilledWhileDisconnected = true.
        // =========================================================================================

        [Test]
        public void CompleteGhostDeathCleanup_GhostDeath_PersistsSnapshotHpAndRespawnPosition_AC_CGS_2()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            const int snapshotHp = 42;
            var callOrder = new List<string>();
            int? persistedHp = null;
            (short x, short y, short z)? persistedPosition = null;

            // Act
            GhostCleanupSequencer.CompleteGhostDeathCleanup(GhostCharacterId, snapshotHp, RespawnPosition,
                persistCharacterState: (hp, position) =>
                {
                    callOrder.Add("persist");
                    persistedHp = hp;
                    persistedPosition = position;
                    Assert.IsEmpty(observer.PersistenceWriteCompletedCalls,
                        "OnPersistenceWriteCompleted must not have fired yet when persistence runs.");
                },
                broadcastGhostExpiredEvent: (characterId, reason) =>
                {
                    callOrder.Add("broadcast");
                    Assert.AreEqual(GhostCharacterId, characterId);
                    Assert.AreEqual("GhostDeath", reason);
                    Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count,
                        "AC-CGS-2: OnPersistenceWriteCompleted must fire before the GhostExpiredEvent broadcast.");
                },
                observer);

            // Assert
            CollectionAssert.AreEqual(new[] { "persist", "broadcast" }, callOrder);

            Assert.AreEqual(snapshotHp, persistedHp,
                "AC-CGS-2 / GD-CGS-2: ghost death applies no HP penalty — persisted HP must equal the pre-disconnect snapshot HP.");
            Assert.AreEqual(RespawnPosition, persistedPosition,
                "AC-CGS-2: persisted position must equal the zone-entry respawn position.");

            // NOTE (code review finding, Story 018): AC-CGS-2's "wasKilledWhileDisconnected = true in
            // the persisted record" clause is NOT independently verifiable by this test. No real
            // persisted-record type exists yet in this codebase — persistCharacterState's signature
            // (Action<int, (short,short,short)>) has no parameter carrying that flag at all, by design
            // (see GhostCleanupSequencer's own remarks: it's data the caller's own persist
            // implementation is responsible for including, not state this sequencer tracks). A prior
            // version of this test set a local bool unconditionally inside the lambda and asserted it
            // — that proved only that the lambda executed, not that the flag was ever set correctly.
            // Removed as misleading; this clause remains an enforced-by-convention documentation
            // requirement until a real persisted-record type exists for a future story to test against.

            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((GhostCharacterId, PersistenceWriteReason.GhostDeath), observer.PersistenceWriteCompletedCalls[0]);

            // AC-CGS-2's own pass condition does not test a session-state transition — see
            // GhostCleanupSequencer's class remarks for why CompleteGhostDeathCleanup deliberately
            // has no such step (AC-CGS-3 owns that scenario instead, via ConnectionStateMachine).
            Assert.IsEmpty(observer.SessionStateTransitionedCalls);
        }

        // =========================================================================================
        // AC-CGS-3 / CGS-6: ghost death races a same-tick reconnect ACK -> death wins.
        // =========================================================================================

        [Test]
        public void HandleGhostDeathWhileReconnecting_DeathRacesReconnectAck_DeathWinsAndReconnectAckRejected_AC_CGS_3()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);
            var callOrder = new List<string>();

            // Act — the death queue is processed first, within the same simulated tick (CGS-6).
            stateMachine.HandleGhostDeathWhileReconnecting(AccountId,
                persistFinalCharacterState: characterId =>
                {
                    callOrder.Add("persist");
                    Assert.AreEqual(GhostCharacterId, characterId);
                    Assert.IsEmpty(observer.PersistenceWriteCompletedCalls,
                        "OnPersistenceWriteCompleted must not have fired yet when persistence runs.");
                },
                sendZoneSessionEnded: (accountId, reason) =>
                {
                    callOrder.Add("zoneSessionEnded");
                    Assert.AreEqual(AccountId, accountId);
                    Assert.AreEqual(DisconnectReason.GhostDeath, reason);
                    Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count,
                        "OnPersistenceWriteCompleted must fire before ZoneSessionEnded is sent.");
                    Assert.IsEmpty(observer.SessionStateTransitionedCalls,
                        "The final Reconnecting -> Disconnected_SessionExpired transition must not have fired yet.");
                },
                observer);

            // Assert — full call order and observer results.
            CollectionAssert.AreEqual(new[] { "persist", "zoneSessionEnded" }, callOrder);

            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((GhostCharacterId, PersistenceWriteReason.GhostDeath), observer.PersistenceWriteCompletedCalls[0]);

            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            // AC-CGS-3: the fromState must be Reconnecting, not Disconnected_SessionActive — this is
            // a death racing a reconnect already in progress.
            Assert.AreEqual((AccountId, SessionState.Reconnecting, SessionState.Disconnected_SessionExpired, "GhostDeath"),
                observer.SessionStateTransitionedCalls[0]);

            Assert.IsFalse(stateMachine.IsAccountRegistered(AccountId), "Session resources must be released once the sequence completes.");

            // CGS-6 single-tick priority: the reconnect ACK, arriving the same tick, must now be
            // rejected — RequireState no longer finds the account Reconnecting, since death already
            // ran first (see ITransportFaultInjector's confirmed inability to inject an inbound
            // reconnect ACK — this story's own resolved test technique drives both code paths
            // directly instead).
            var currencySystem = new CurrencySystem();
            currencySystem.RegisterCharacter(new CharacterID(GhostCharacterId), 0u);

            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteReAuthSuccess(AccountId, currencySystem,
                    queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                    markPurchaseRefunded: _ => Assert.Fail("Must not reach reconciliation — the reconnect ACK must be rejected before this runs (CGS-6)."),
                    queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                    representRespecPhase2: _ => Assert.Fail("Must not reach respec resolution — the reconnect ACK must be rejected before this runs (CGS-6)."),
                    releaseRespecReservationAndNotify: _ => Assert.Fail("Must not reach respec resolution — the reconnect ACK must be rejected before this runs (CGS-6)."),
                    handshakeData: new SessionHandshakeData(wasKilledWhileDisconnected: true, goldVersion: 0u, level: 1,
                        currentHp: 0, currentMp: 0, heldFreePoints: 0, classType: 0),
                    observer));

            // NOTE (code review finding, Story 018): this test proves the registry correctly rejects a
            // reconnect ACK against an account that death-handling has already torn down — it proves
            // RequireState's generic missing-entry guard fires, not that the system arbitrates "which
            // of two same-tick events wins" independently of caller-chosen call order. No dispatcher
            // or per-tick event-ordering arbitration exists anywhere in this codebase yet (that is a
            // future orchestration story's job — see this class's own remarks on
            // HandleGhostDeathWhileReconnecting). What CGS-6's "single-tick priority" guarantee reduces
            // to, given the current architecture, is: a caller that happens to invoke
            // HandleGhostDeathWhileReconnecting before CompleteReAuthSuccess gets a correctly-rejected
            // second call. The companion test below proves the guard is symmetric in the reverse call
            // order too — together the two tests are the strongest proof obtainable without a real
            // per-tick dispatcher to test against.
        }

        [Test]
        public void CompleteReAuthSuccess_ThenHandleGhostDeathWhileReconnecting_SecondCallThrows()
        {
            // Arrange — code review coverage gap (Story 018): the reverse call order from the test
            // above. If the reconnect ACK is processed FIRST (succeeding into Connected), a
            // subsequently-arriving ghost-death event for the same account must also be correctly
            // rejected — the account is no longer Reconnecting, so HandleGhostDeathWhileReconnecting's
            // own RequireState guard must throw. This demonstrates the guard is symmetric/order-
            // sensitive in both directions — see the note on the companion AC-CGS-3 test above for what
            // this test class can and cannot prove about "priority" given no dispatcher exists yet.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);
            var currencySystem = new CurrencySystem();
            currencySystem.RegisterCharacter(new CharacterID(GhostCharacterId), 0u);

            // Act — the reconnect ACK is processed first and succeeds.
            stateMachine.CompleteReAuthSuccess(AccountId, currencySystem,
                queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                markPurchaseRefunded: _ => { },
                queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                representRespecPhase2: _ => { },
                releaseRespecReservationAndNotify: _ => { },
                handshakeData: new SessionHandshakeData(wasKilledWhileDisconnected: false, goldVersion: 0u, level: 1,
                    currentHp: 100, currentMp: 0, heldFreePoints: 0, classType: 0),
                observer);

            Assert.IsTrue(stateMachine.TryGetSessionState(AccountId, out SessionState state));
            Assert.AreEqual(SessionState.Connected, state, "Sanity check: the reconnect must have succeeded first.");

            // Assert — a subsequently-arriving ghost-death event now finds the account no longer
            // Reconnecting, and must be rejected.
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.HandleGhostDeathWhileReconnecting(AccountId,
                    persistFinalCharacterState: _ => Assert.Fail("Must not persist — the account is no longer Reconnecting."),
                    sendZoneSessionEnded: (_, _) => Assert.Fail("Must not send ZoneSessionEnded — the account is no longer Reconnecting."),
                    observer));
        }

        // =========================================================================================
        // AC-CGS-4: TTL expiry cleanup ordering, verified by sequence-index (not timestamps), plus
        // the named IServerCrashInjector.AfterGhostCleanupPersistenceWrite test technique.
        // =========================================================================================

        [Test]
        public void CompleteTTLExpiryCleanup_PersistenceWriteCompletedPrecedesTransitionAndBroadcast_AC_CGS_4()
        {
            // Arrange — reuses the ordering-proof idiom (see class remarks): each list's Count at the
            // point a subsequent delegate runs IS the sequence-index proof AC-CGS-4 requires.
            var observer = new NetworkTestObserver();
            var callOrder = new List<string>();
            const int snapshotHp = 60;

            // Act
            GhostCleanupSequencer.CompleteTTLExpiryCleanup(AccountId, GhostCharacterId, snapshotHp,
                persistCharacterState: hp =>
                {
                    callOrder.Add("persist");
                    Assert.IsEmpty(observer.PersistenceWriteCompletedCalls);
                    Assert.IsEmpty(observer.SessionStateTransitionedCalls);
                },
                broadcastGhostExpiredEvent: (characterId, reason) =>
                {
                    callOrder.Add("broadcast");
                    // AC-CGS-4: OnPersistenceWriteCompleted must precede BOTH the state transition and
                    // the GhostExpiredEvent broadcast — both already recorded by this point.
                    Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
                    Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
                },
                observer);

            // Assert
            CollectionAssert.AreEqual(new[] { "persist", "broadcast" }, callOrder);
            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
        }

        [Test]
        public void CompleteTTLExpiryCleanup_SimulatedCrashAfterPersistenceWrite_CharacterStateDurable_AC_CGS_4()
        {
            // Arrange — AC-CGS-4's own named test technique:
            // IServerCrashInjector.AfterGhostCleanupPersistenceWrite (an already-existing crash step,
            // pre-registered for exactly this scenario). The mock persistence write completes
            // durably; the simulated crash then aborts before the session transition or broadcast can
            // run — modeling "crash after step 1, character state must be durable on recovery."
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
                "AC-CGS-4: the persistence write must be durable even though the process 'crashed' immediately after.");
            // ...but nothing past the persistence write ran.
            Assert.IsFalse(broadcastFired, "GhostExpiredEvent must not fire once the simulated crash aborts the sequence.");
        }

        // =========================================================================================
        // Null-guard / precondition-guard coverage — GhostCleanupSequencer.
        // =========================================================================================

        [Test]
        public void CompleteTTLExpiryCleanup_NullPersistCharacterState_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                GhostCleanupSequencer.CompleteTTLExpiryCleanup(AccountId, GhostCharacterId, snapshotHp: 0,
                    persistCharacterState: null,
                    broadcastGhostExpiredEvent: (_, _) => { }));
        }

        [Test]
        public void CompleteTTLExpiryCleanup_NullBroadcastGhostExpiredEvent_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                GhostCleanupSequencer.CompleteTTLExpiryCleanup(AccountId, GhostCharacterId, snapshotHp: 0,
                    persistCharacterState: _ => { },
                    broadcastGhostExpiredEvent: null));
        }

        [Test]
        public void CompleteGhostDeathCleanup_NullPersistCharacterState_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                GhostCleanupSequencer.CompleteGhostDeathCleanup(GhostCharacterId, snapshotHp: 0, RespawnPosition,
                    persistCharacterState: null,
                    broadcastGhostExpiredEvent: (_, _) => { }));
        }

        [Test]
        public void CompleteGhostDeathCleanup_NullBroadcastGhostExpiredEvent_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                GhostCleanupSequencer.CompleteGhostDeathCleanup(GhostCharacterId, snapshotHp: 0, RespawnPosition,
                    persistCharacterState: (_, _) => { },
                    broadcastGhostExpiredEvent: null));
        }

        // =========================================================================================
        // Null-guard / precondition-guard coverage — ConnectionStateMachine.HandleGhostDeathWhileReconnecting.
        // =========================================================================================

        [Test]
        public void HandleGhostDeathWhileReconnecting_NullPersistFinalCharacterState_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.HandleGhostDeathWhileReconnecting(AccountId,
                    persistFinalCharacterState: null,
                    sendZoneSessionEnded: (_, _) => { },
                    observer));
        }

        [Test]
        public void HandleGhostDeathWhileReconnecting_NullSendZoneSessionEnded_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.HandleGhostDeathWhileReconnecting(AccountId,
                    persistFinalCharacterState: _ => { },
                    sendZoneSessionEnded: null,
                    observer));
        }

        [Test]
        public void HandleGhostDeathWhileReconnecting_AccountNotReconnecting_Throws()
        {
            // Arrange — a Connected (not yet Reconnecting) account.
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountId, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountId, GhostCharacterId, currentTick: 0u, observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.HandleGhostDeathWhileReconnecting(AccountId,
                    persistFinalCharacterState: _ => { },
                    sendZoneSessionEnded: (_, _) => { },
                    observer));
        }
    }
}
