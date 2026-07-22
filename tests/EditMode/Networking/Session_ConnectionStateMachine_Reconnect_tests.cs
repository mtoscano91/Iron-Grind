using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit/integration tests for Networking Core Story 013 — the
    /// <see cref="SessionState.Reconnecting"/> transitions added to <see cref="ConnectionStateMachine"/>
    /// (Story 012's registry, extended). Covers AC-NC-11 (reconnect handshake), AC-NC-13 (respec
    /// reservation resolution), AC-NC-CR64-RECONCILE (ADR-001 Decision 4 PendingPurchase
    /// reconciliation, crossing into the real Currency System — hence <c>Type: Integration</c>),
    /// AC-NC-37 (this story's Reconnecting-state session-stealing resolution — see
    /// <see cref="ConnectionStateMachine"/>'s class remarks for why this differs from Story 012's
    /// <c>AC-NC-39-SESSION</c>), and AC-NC-38-REAUTH (REAUTH_FAILURE_LIMIT exhaustion, EC-NET-7 TTL
    /// non-reset).
    /// </summary>
    /// <remarks>
    /// All timing is tick-based only — no <see cref="System.Threading.Thread.Sleep"/> anywhere in this
    /// file, matching <c>Session_ConnectionStateMachine_Core_tests.cs</c>'s own precedent. Gold
    /// reconciliation tests use a real <see cref="CurrencySystem"/> instance (not a mock) — Currency
    /// System is real, already-implemented production code (Currency System Stories 001–006), not a
    /// forward dependency this story needs to stub.
    /// </remarks>
    [TestFixture]
    internal sealed class Session_ConnectionStateMachine_Reconnect_Tests
    {
        private const uint AccountA = 7u;
        private const uint CharacterA = 555u;
        private const uint SessionExpiryTick = 6100u;

        /// <summary>Advances a fresh state machine's account to <see cref="SessionState.Disconnected_SessionActive"/> at tick 0.</summary>
        private static ConnectionStateMachine CreateDisconnectedSessionActiveStateMachine(NetworkTestObserver observer)
        {
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountA, CharacterA, currentTick: 0u, observer);
            // Heartbeat-timeout path (Story 012) is the only way to reach Disconnected_SessionActive.
            stateMachine.EvaluateTimeouts(currentTick: 1u, heartbeatTimeoutTicks: 1u, connectingTimeoutTicks: 1000u, observer);
            observer.Reset(); // isolate each test's assertions from the setup calls above
            return stateMachine;
        }

        /// <summary>Advances a fresh state machine's account to <see cref="SessionState.Reconnecting"/>.</summary>
        private static ConnectionStateMachine CreateReconnectingStateMachine(NetworkTestObserver observer, uint sessionExpiryTick = SessionExpiryTick)
        {
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);
            stateMachine.EnterReconnecting(AccountA, sessionExpiryTick, observer);
            observer.Reset();
            return stateMachine;
        }

        private static CurrencySystem CreateCurrencySystemWithBalance(uint balance)
        {
            var currencySystem = new CurrencySystem();
            currencySystem.RegisterCharacter(new CharacterID(CharacterA), balance);
            return currencySystem;
        }

        private static readonly SessionHandshakeData DefaultHandshakeData =
            new SessionHandshakeData(wasKilledWhileDisconnected: false, goldVersion: 3u, level: 15,
                currentHp: 80, currentMp: 40, heldFreePoints: 0, classType: 1);

        // =========================================================================================
        // EnterReconnecting: Disconnected_SessionActive -> Reconnecting.
        // =========================================================================================

        [Test]
        public void EnterReconnecting_DisconnectedSessionActiveAccount_TransitionsToReconnectingAndStoresExpiryTick()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);

            // Act
            stateMachine.EnterReconnecting(AccountA, SessionExpiryTick, observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountA, SessionState.Disconnected_SessionActive, SessionState.Reconnecting, "ReconnectAttempt"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.IsTrue(stateMachine.TryGetSessionState(AccountA, out SessionState state));
            Assert.AreEqual(SessionState.Reconnecting, state);
            Assert.IsTrue(stateMachine.TryGetSessionExpiryTick(AccountA, out uint expiryTick));
            Assert.AreEqual(SessionExpiryTick, expiryTick);
        }

        [Test]
        public void EnterReconnecting_AccountNotDisconnectedSessionActive_Throws()
        {
            // Arrange — a Connecting (not yet Disconnected_SessionActive) account.
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.EnterReconnecting(AccountA, SessionExpiryTick, observer));
        }

        // =========================================================================================
        // AC-NC-11: reconnect within TTL -> handshake fields match server state.
        // =========================================================================================

        [Test]
        public void CompleteReAuthSuccess_ReconnectingAccount_EmitsHandshakeWithFieldsMatchingAC_NC_11()
        {
            // Arrange — gold = 1,000g, Level = 15, per AC-NC-11's own scenario. currentTick =
            // sessionExpiryTick - 100 = 6000, confirming the scenario is inside the TTL window (the
            // caller's own precondition — see EnterReconnecting's remarks on why this class does not
            // itself gate on IsTickExpired).
            Assert.IsFalse(StaleDiscardComparer.IsTickExpired(currentTick: 6000u, expiryTick: SessionExpiryTick),
                "Sanity check: AC-NC-11's scenario places the reconnect inside the TTL window.");

            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(1000u);
            var stateMachine = CreateReconnectingStateMachine(observer);

            // Act
            stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                markPurchaseRefunded: _ => Assert.Fail("No pending purchases in this scenario."),
                queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                representRespecPhase2: _ => Assert.Fail("No active respec reservation in this scenario."),
                releaseRespecReservationAndNotify: _ => Assert.Fail("No active respec reservation in this scenario."),
                handshakeData: DefaultHandshakeData,
                observer);

            // Assert
            Assert.AreEqual(1, observer.SessionHandshakeEmittedCalls.Count);
            var call = observer.SessionHandshakeEmittedCalls[0];
            Assert.AreEqual(CharacterA, call.characterId);
            Assert.IsFalse(call.wasKilledWhileDisconnected);
            Assert.AreEqual(1000, call.goldBalance);
            Assert.AreEqual(DefaultHandshakeData.GoldVersion, call.goldVersion);
            Assert.AreEqual(15, call.level);
            Assert.AreEqual(DefaultHandshakeData.CurrentHp, call.currentHp);
            Assert.AreEqual(DefaultHandshakeData.CurrentMp, call.currentMp);
            Assert.AreEqual(DefaultHandshakeData.HeldFreePoints, call.heldFreePoints);
            Assert.AreEqual(DefaultHandshakeData.ClassType, call.classType);

            Assert.IsTrue(stateMachine.TryGetSessionState(AccountA, out SessionState state));
            Assert.AreEqual(SessionState.Connected, state);
        }

        // =========================================================================================
        // AC-NC-13: respec Phase 1 reservation resolution.
        // =========================================================================================

        [Test]
        public void CompleteReAuthSuccess_RespecReservationTtlValid_RepresentsPhase2()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(0u);
            var stateMachine = CreateReconnectingStateMachine(observer);
            uint phase2CalledFor = 0u;
            bool releaseCalled = false;

            // Act
            stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                markPurchaseRefunded: _ => { },
                queryRespecReservationStatus: _ => RespecReservationStatus.TtlValid,
                representRespecPhase2: charId => phase2CalledFor = charId,
                releaseRespecReservationAndNotify: _ => releaseCalled = true,
                handshakeData: DefaultHandshakeData,
                observer);

            // Assert
            Assert.AreEqual(CharacterA, phase2CalledFor, "AC-NC-13a: reconnect within the respec TTL re-presents Phase 2.");
            Assert.IsFalse(releaseCalled);
        }

        [Test]
        public void CompleteReAuthSuccess_RespecReservationTtlExpired_ReleasesAndNotifies()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(0u);
            var stateMachine = CreateReconnectingStateMachine(observer);
            uint releaseCalledFor = 0u;
            bool phase2Called = false;

            // Act
            stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                markPurchaseRefunded: _ => { },
                queryRespecReservationStatus: _ => RespecReservationStatus.TtlExpired,
                representRespecPhase2: _ => phase2Called = true,
                releaseRespecReservationAndNotify: charId => releaseCalledFor = charId,
                handshakeData: DefaultHandshakeData,
                observer);

            // Assert
            Assert.AreEqual(CharacterA, releaseCalledFor,
                "AC-NC-13b: reconnect after the respec TTL (but within session TTL) returns the scroll to inventory.");
            Assert.IsFalse(phase2Called);
        }

        [Test]
        public void CompleteReAuthSuccess_NoActiveRespecReservation_NeitherDelegateCalled()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(0u);
            var stateMachine = CreateReconnectingStateMachine(observer);
            bool phase2Called = false;
            bool releaseCalled = false;

            // Act
            stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                markPurchaseRefunded: _ => { },
                queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                representRespecPhase2: _ => phase2Called = true,
                releaseRespecReservationAndNotify: _ => releaseCalled = true,
                handshakeData: DefaultHandshakeData,
                observer);

            // Assert
            Assert.IsFalse(phase2Called);
            Assert.IsFalse(releaseCalled);
        }

        // =========================================================================================
        // AC-NC-CR64-RECONCILE: PendingPurchase reconciliation (ADR-001 Decision 4).
        // =========================================================================================

        [Test]
        public void CompleteReAuthSuccess_OnePendingGoldDebitedPurchase_ReconcilesBeforeHandshakeAndRefundsGold()
        {
            // Arrange — balance 500g pre-reconciliation; one GoldDebited record for 200g.
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(500u);
            var stateMachine = CreateReconnectingStateMachine(observer);
            var purchase = new PendingPurchaseRecord(new CharacterID(CharacterA), requestId: 42u, itemId: 99u,
                quantity: 1, totalCost: 200u);
            var refundedRecords = new List<PendingPurchaseRecord>();

            // Act
            stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                queryGoldDebitedPurchases: charId =>
                {
                    Assert.AreEqual(CharacterA, charId);
                    return new List<PendingPurchaseRecord> { purchase };
                },
                markPurchaseRefunded: record =>
                {
                    refundedRecords.Add(record);
                    Assert.IsEmpty(observer.SessionHandshakeEmittedCalls,
                        "AC-NC-CR64-RECONCILE: reconciliation must complete before the handshake is emitted.");
                },
                queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                representRespecPhase2: _ => { },
                releaseRespecReservationAndNotify: _ => { },
                handshakeData: DefaultHandshakeData,
                observer);

            // Assert
            Assert.AreEqual(700u, currencySystem.GetBalance(new CharacterID(CharacterA)),
                "AddGold(charId, totalCost, CompensatingRefund) must have credited the 200g refund.");
            Assert.AreEqual(1, refundedRecords.Count);
            Assert.AreEqual(purchase.RequestId, refundedRecords[0].RequestId);

            Assert.AreEqual(1, observer.SessionHandshakeEmittedCalls.Count);
            Assert.AreEqual(700, observer.SessionHandshakeEmittedCalls[0].goldBalance,
                "The handshake's gold balance must reflect the post-reconciliation value.");
        }

        [Test]
        public void CompleteReAuthSuccess_TwoPendingGoldDebitedPurchases_ReconcilesBothInOrderAndSumsRefund()
        {
            // Arrange — balance 500g pre-reconciliation; two GoldDebited records (200g, 100g), hardening
            // the reconciliation loop against an off-by-one/early-break regression that a single-record
            // test cannot catch.
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(500u);
            var stateMachine = CreateReconnectingStateMachine(observer);
            var purchase1 = new PendingPurchaseRecord(new CharacterID(CharacterA), requestId: 42u, itemId: 99u,
                quantity: 1, totalCost: 200u);
            var purchase2 = new PendingPurchaseRecord(new CharacterID(CharacterA), requestId: 43u, itemId: 77u,
                quantity: 2, totalCost: 100u);
            var refundedRecords = new List<PendingPurchaseRecord>();

            // Act
            stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                queryGoldDebitedPurchases: _ => new List<PendingPurchaseRecord> { purchase1, purchase2 },
                markPurchaseRefunded: record =>
                {
                    refundedRecords.Add(record);
                    Assert.IsEmpty(observer.SessionHandshakeEmittedCalls,
                        "AC-NC-CR64-RECONCILE: reconciliation must complete before the handshake is emitted.");
                },
                queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                representRespecPhase2: _ => { },
                releaseRespecReservationAndNotify: _ => { },
                handshakeData: DefaultHandshakeData,
                observer);

            // Assert
            Assert.AreEqual(800u, currencySystem.GetBalance(new CharacterID(CharacterA)),
                "Both AddGold(charId, totalCost, CompensatingRefund) calls must have credited: 500 + 200 + 100 = 800.");
            Assert.AreEqual(2, refundedRecords.Count);
            Assert.AreEqual(purchase1.RequestId, refundedRecords[0].RequestId, "Records must be refunded in the order returned by the query.");
            Assert.AreEqual(purchase2.RequestId, refundedRecords[1].RequestId);

            Assert.AreEqual(1, observer.SessionHandshakeEmittedCalls.Count);
            Assert.AreEqual(800, observer.SessionHandshakeEmittedCalls[0].goldBalance,
                "The handshake's gold balance must reflect the fully-reconciled post-loop value.");
        }

        [Test]
        public void CompleteReAuthSuccess_ReconciliationLogsEvent()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(500u);
            var stateMachine = CreateReconnectingStateMachine(observer);
            var purchase = new PendingPurchaseRecord(new CharacterID(CharacterA), requestId: 42u, itemId: 99u,
                quantity: 1, totalCost: 200u);

            LogAssert.Expect(LogType.Log, new Regex("PendingPurchaseReconciled.*requestId=42"));

            // Act
            stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                queryGoldDebitedPurchases: _ => new List<PendingPurchaseRecord> { purchase },
                markPurchaseRefunded: _ => { },
                queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                representRespecPhase2: _ => { },
                releaseRespecReservationAndNotify: _ => { },
                handshakeData: DefaultHandshakeData,
                observer);

            // Assert — LogAssert.Expect above already asserts the message was logged.
        }

        [Test]
        public void CompleteReAuthSuccess_NullCurrencyService_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteReAuthSuccess(AccountA, currencyService: null,
                    queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                    markPurchaseRefunded: _ => { },
                    queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                    representRespecPhase2: _ => { },
                    releaseRespecReservationAndNotify: _ => { },
                    handshakeData: DefaultHandshakeData,
                    observer));
        }

        [Test]
        public void CompleteReAuthSuccess_NullQueryGoldDebitedPurchases_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(0u);
            var stateMachine = CreateReconnectingStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                    queryGoldDebitedPurchases: null,
                    markPurchaseRefunded: _ => { },
                    queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                    representRespecPhase2: _ => { },
                    releaseRespecReservationAndNotify: _ => { },
                    handshakeData: DefaultHandshakeData,
                    observer));
        }

        [Test]
        public void CompleteReAuthSuccess_NullMarkPurchaseRefunded_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(0u);
            var stateMachine = CreateReconnectingStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                    queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                    markPurchaseRefunded: null,
                    queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                    representRespecPhase2: _ => { },
                    releaseRespecReservationAndNotify: _ => { },
                    handshakeData: DefaultHandshakeData,
                    observer));
        }

        [Test]
        public void CompleteReAuthSuccess_NullQueryRespecReservationStatus_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(0u);
            var stateMachine = CreateReconnectingStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                    queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                    markPurchaseRefunded: _ => { },
                    queryRespecReservationStatus: null,
                    representRespecPhase2: _ => { },
                    releaseRespecReservationAndNotify: _ => { },
                    handshakeData: DefaultHandshakeData,
                    observer));
        }

        [Test]
        public void CompleteReAuthSuccess_NullRepresentRespecPhase2_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(0u);
            var stateMachine = CreateReconnectingStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                    queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                    markPurchaseRefunded: _ => { },
                    queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                    representRespecPhase2: null,
                    releaseRespecReservationAndNotify: _ => { },
                    handshakeData: DefaultHandshakeData,
                    observer));
        }

        [Test]
        public void CompleteReAuthSuccess_NullReleaseRespecReservationAndNotify_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(0u);
            var stateMachine = CreateReconnectingStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                    queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                    markPurchaseRefunded: _ => { },
                    queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                    representRespecPhase2: _ => { },
                    releaseRespecReservationAndNotify: null,
                    handshakeData: DefaultHandshakeData,
                    observer));
        }

        [Test]
        public void CompleteReAuthSuccess_AccountNotReconnecting_Throws()
        {
            // Arrange — Disconnected_SessionActive, not yet Reconnecting.
            var observer = new NetworkTestObserver();
            var currencySystem = CreateCurrencySystemWithBalance(0u);
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);

            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteReAuthSuccess(AccountA, currencySystem,
                    queryGoldDebitedPurchases: _ => Array.Empty<PendingPurchaseRecord>(),
                    markPurchaseRefunded: _ => { },
                    queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                    representRespecPhase2: _ => { },
                    releaseRespecReservationAndNotify: _ => { },
                    handshakeData: DefaultHandshakeData,
                    observer));
        }

        // =========================================================================================
        // AC-NC-37 (this story's resolution): session-stealing while Reconnecting.
        // =========================================================================================

        [Test]
        public void HandleReconnectSessionSteal_ReconnectingAccount_RunsFullSequenceInOrderAndNewConnectionRegistersAsConnecting()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);
            var callOrder = new List<string>();

            // Act
            stateMachine.HandleReconnectSessionSteal(
                AccountA,
                newConnectionCurrentTick: 900u,
                invalidateSessionToken: accountId =>
                {
                    callOrder.Add("invalidateToken");
                    Assert.AreEqual(AccountA, accountId);
                    Assert.AreEqual(1, observer.SessionInvalidatedByStealCalls.Count,
                        "OnSessionInvalidatedBySteal must fire before token invalidation (step (a) before (b)).");
                    Assert.IsEmpty(observer.PersistenceWriteCompletedCalls);
                },
                persistFinalCharacterState: characterId =>
                {
                    callOrder.Add("persist");
                    Assert.AreEqual(CharacterA, characterId);
                    Assert.IsEmpty(observer.SessionStateTransitionedCalls,
                        "The SessionStealDuringReconnect transition must not have fired yet when persistence runs.");
                },
                closePriorTransportConnection: accountId =>
                {
                    callOrder.Add("closeTransport");
                    Assert.AreEqual(AccountA, accountId);
                    Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count,
                        "Persistence must complete before the prior transport connection closes.");
                    Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count,
                        "The prior session's SessionStealDuringReconnect transition must fire before the transport closes.");
                },
                observer);

            // Assert — full ordering: (a) invalidated -> (b) token invalidated -> persist -> (d)
            // persistence completed -> (e) prior transitioned -> (f) transport closed -> (g) new
            // connection's Connecting transition.
            CollectionAssert.AreEqual(new[] { "invalidateToken", "persist", "closeTransport" }, callOrder);

            Assert.AreEqual(1, observer.SessionInvalidatedByStealCalls.Count);
            Assert.AreEqual((AccountA, CharacterA), observer.SessionInvalidatedByStealCalls[0]);

            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((CharacterA, PersistenceWriteReason.SessionSteal), observer.PersistenceWriteCompletedCalls[0]);

            Assert.AreEqual(2, observer.SessionStateTransitionedCalls.Count,
                "Two transitions fire: the prior session's SessionStealDuringReconnect, then the new connection's NewConnection.");
            Assert.AreEqual((AccountA, SessionState.Reconnecting, SessionState.Disconnected_SessionExpired, "SessionStealDuringReconnect"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.AreEqual((AccountA, SessionState.Disconnected_SessionExpired, SessionState.Connecting, "NewConnection"),
                observer.SessionStateTransitionedCalls[1]);

            Assert.IsTrue(stateMachine.TryGetSessionState(AccountA, out SessionState finalState));
            Assert.AreEqual(SessionState.Connecting, finalState,
                "After session-stealing during reconnect, the account is registered fresh at Connecting for the new connection.");
        }

        [Test]
        public void HandleReconnectSessionSteal_AccountNotReconnecting_Throws()
        {
            // Arrange — no session registered for this account at all.
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();

            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.HandleReconnectSessionSteal(AccountA, newConnectionCurrentTick: 10u,
                    invalidateSessionToken: _ => { },
                    persistFinalCharacterState: _ => { },
                    closePriorTransportConnection: _ => { },
                    observer));
        }

        [Test]
        public void HandleReconnectSessionSteal_NullInvalidateSessionTokenDelegate_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.HandleReconnectSessionSteal(AccountA, newConnectionCurrentTick: 900u,
                    invalidateSessionToken: null,
                    persistFinalCharacterState: _ => { },
                    closePriorTransportConnection: _ => { },
                    observer));
        }

        [Test]
        public void HandleReconnectSessionSteal_NullPersistDelegate_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.HandleReconnectSessionSteal(AccountA, newConnectionCurrentTick: 900u,
                    invalidateSessionToken: _ => { },
                    persistFinalCharacterState: null,
                    closePriorTransportConnection: _ => { },
                    observer));
        }

        [Test]
        public void HandleReconnectSessionSteal_NullClosePriorTransportConnectionDelegate_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.HandleReconnectSessionSteal(AccountA, newConnectionCurrentTick: 900u,
                    invalidateSessionToken: _ => { },
                    persistFinalCharacterState: _ => { },
                    closePriorTransportConnection: null,
                    observer));
        }

        // =========================================================================================
        // AC-NC-38-REAUTH: REAUTH_FAILURE_LIMIT exhausted; EC-NET-7 TTL non-reset.
        // =========================================================================================

        [Test]
        public void RecordFailedReAuthAttempt_ThreeFailuresExhaustLimit_FiresInOrderAndTransitionsToExpired()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);
            const int reauthFailureLimit = 3;

            // Act — attempt 1: fails, bounces back to Disconnected_SessionActive.
            stateMachine.RecordFailedReAuthAttempt(AccountA, reauthFailureLimit,
                persistFinalCharacterState: _ => Assert.Fail("Must not persist before the limit is exhausted."),
                observer);

            // Assert — intermediate state after attempt 1.
            Assert.IsTrue(stateMachine.TryGetSessionState(AccountA, out SessionState stateAfter1));
            Assert.AreEqual(SessionState.Disconnected_SessionActive, stateAfter1);

            // Act — attempt 2: caller re-enters Reconnecting, fails again.
            stateMachine.EnterReconnecting(AccountA, SessionExpiryTick, observer);
            stateMachine.RecordFailedReAuthAttempt(AccountA, reauthFailureLimit,
                persistFinalCharacterState: _ => Assert.Fail("Must not persist before the limit is exhausted."),
                observer);

            // Act — attempt 3: caller re-enters Reconnecting; this failure exhausts the limit.
            stateMachine.EnterReconnecting(AccountA, SessionExpiryTick, observer);
            bool persistCalled = false;
            stateMachine.RecordFailedReAuthAttempt(AccountA, reauthFailureLimit,
                persistFinalCharacterState: characterId =>
                {
                    persistCalled = true;
                    Assert.AreEqual(CharacterA, characterId);
                },
                observer);

            // Assert — all 3 OnReAuthAttemptFailed calls, in order, with correct (attemptNumber, remainingAttempts).
            Assert.AreEqual(3, observer.ReAuthAttemptFailedCalls.Count);
            Assert.AreEqual((AccountA, 1, 2), observer.ReAuthAttemptFailedCalls[0]);
            Assert.AreEqual((AccountA, 2, 1), observer.ReAuthAttemptFailedCalls[1]);
            Assert.AreEqual((AccountA, 3, 0), observer.ReAuthAttemptFailedCalls[2]);

            Assert.IsTrue(persistCalled);
            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((CharacterA, PersistenceWriteReason.SessionExpiry), observer.PersistenceWriteCompletedCalls[0]);

            // Assert — the final transition in the overall call sequence is the ReauthLimitExceeded one.
            var lastTransition = observer.SessionStateTransitionedCalls[observer.SessionStateTransitionedCalls.Count - 1];
            Assert.AreEqual((AccountA, SessionState.Reconnecting, SessionState.Disconnected_SessionExpired, "ReauthLimitExceeded"),
                lastTransition);

            Assert.IsFalse(stateMachine.IsAccountRegistered(AccountA), "Session resources must be released once the limit is exhausted.");
        }

        [Test]
        public void RecordFailedReAuthAttempt_SessionExpiryTickUnchangedAcrossAllAttempts()
        {
            // Arrange — EC-NET-7: TTL must never reset across failed attempts.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer, SessionExpiryTick);

            // Act & Assert — attempt 1.
            stateMachine.RecordFailedReAuthAttempt(AccountA, reauthFailureLimit: 3,
                persistFinalCharacterState: _ => { }, observer);
            Assert.IsTrue(stateMachine.TryGetSessionExpiryTick(AccountA, out uint tickAfter1));
            Assert.AreEqual(SessionExpiryTick, tickAfter1, "sessionExpiryTick must be unchanged after attempt 1.");

            // Act & Assert — attempt 2 (re-enter Reconnecting with the SAME original value, per this
            // class's documented caller contract).
            stateMachine.EnterReconnecting(AccountA, SessionExpiryTick, observer);
            stateMachine.RecordFailedReAuthAttempt(AccountA, reauthFailureLimit: 3,
                persistFinalCharacterState: _ => { }, observer);
            Assert.IsTrue(stateMachine.TryGetSessionExpiryTick(AccountA, out uint tickAfter2));
            Assert.AreEqual(SessionExpiryTick, tickAfter2, "sessionExpiryTick must be unchanged after attempt 2.");
        }

        [Test]
        public void RecordFailedReAuthAttempt_SingleFailureNotExhausted_ReturnsToDisconnectedSessionActiveWithoutPersistenceWrite()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);

            // Act
            stateMachine.RecordFailedReAuthAttempt(AccountA, reauthFailureLimit: 3,
                persistFinalCharacterState: _ => Assert.Fail("Must not persist on a non-exhausting failure."),
                observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountA, SessionState.Reconnecting, SessionState.Disconnected_SessionActive, "ReAuthFailed"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.IsEmpty(observer.PersistenceWriteCompletedCalls);
            Assert.IsTrue(stateMachine.IsAccountRegistered(AccountA), "Session resources must be retained — the session is still recoverable.");
        }

        [Test]
        public void RecordFailedReAuthAttempt_AccountNotReconnecting_Throws()
        {
            // Arrange — Disconnected_SessionActive, not yet Reconnecting.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateDisconnectedSessionActiveStateMachine(observer);

            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.RecordFailedReAuthAttempt(AccountA, reauthFailureLimit: 3,
                    persistFinalCharacterState: _ => { }, observer));
        }

        [Test]
        public void RecordFailedReAuthAttempt_NullPersistDelegate_ThrowsArgumentNullException()
        {
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);

            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.RecordFailedReAuthAttempt(AccountA, reauthFailureLimit: 3,
                    persistFinalCharacterState: null, observer));
        }

        [Test]
        public void RecordFailedReAuthAttempt_CalledTwiceWithoutReEnteringReconnecting_SecondCallThrows()
        {
            // Arrange — a single failed attempt bounces the account back to Disconnected_SessionActive;
            // calling RecordFailedReAuthAttempt again without a fresh EnterReconnecting call must throw,
            // proving each attempt requires its own re-entry into Reconnecting (see class remarks).
            var observer = new NetworkTestObserver();
            var stateMachine = CreateReconnectingStateMachine(observer);
            stateMachine.RecordFailedReAuthAttempt(AccountA, reauthFailureLimit: 3,
                persistFinalCharacterState: _ => { }, observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.RecordFailedReAuthAttempt(AccountA, reauthFailureLimit: 3,
                    persistFinalCharacterState: _ => { }, observer));
        }
    }
}
