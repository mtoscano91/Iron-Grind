using System;
using System.Collections.Generic;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 012 — the core (non-reconnect) transitions of
    /// the ST-NET-1 player connection state machine (<see cref="ConnectionStateMachine"/>). Covers
    /// AC-NC-10 (heartbeat timeout), AC-NC-26 (explicit disconnect), AC-NC-39-SESSION (session
    /// stealing), and AC-NC-39-CONNECTING (<c>Connecting</c> timeout), plus this story's own
    /// prior-state guard behavior for the caller-driven transition methods.
    /// </summary>
    /// <remarks>
    /// All timing is tick-based only — no <see cref="System.Threading.Thread.Sleep"/> anywhere in
    /// this file, and no <see cref="ServerTickLoop"/> instance is needed: every test drives raw tick
    /// numbers directly into <see cref="ConnectionStateMachine.EvaluateTimeouts"/>, matching
    /// <c>TickLoop_CrossCuttingGuards_tests.cs</c>'s own precedent for a Logic-story unit test.
    /// </remarks>
    [TestFixture]
    internal sealed class Session_ConnectionStateMachine_Core_Tests
    {
        private const uint AccountA = 7u;
        private const uint CharacterA = 555u;

        // Test-config values per the story's own AC text (production defaults are undetermined /
        // different — these are the values the ACs themselves specify).
        private const uint HeartbeatTimeoutTicks = 60u;   // HEARTBEAT_TIMEOUT_SECONDS=3 x TICK_RATE_HZ=20
        private const uint ConnectingTimeoutTicks = 200u; // CONNECTING_TIMEOUT_SECONDS=10 x TICK_RATE_HZ=20

        /// <summary>Advances a fresh state machine's account to <see cref="SessionState.Connected"/> at tick 0.</summary>
        private static ConnectionStateMachine CreateConnectedStateMachine(NetworkTestObserver observer)
        {
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(AccountA, CharacterA, currentTick: 0u, observer);
            observer.Reset(); // isolate each test's assertions from the setup calls above
            return stateMachine;
        }

        // =========================================================================================
        // AC-NC-10: heartbeat timeout (Connected -> Disconnected_SessionActive).
        // =========================================================================================

        [Test]
        public void EvaluateTimeouts_ConnectedSessionSilentSixtyOneTicks_TransitionsToDisconnectedSessionActive()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act — 61 ticks of silence (HeartbeatTimeout_ticks + 1), per AC-NC-10's own scenario.
            stateMachine.EvaluateTimeouts(currentTick: 61u, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountA, SessionState.Connected, SessionState.Disconnected_SessionActive, "HeartbeatTimeout"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.IsTrue(stateMachine.TryGetSessionState(AccountA, out SessionState state));
            Assert.AreEqual(SessionState.Disconnected_SessionActive, state);
        }

        [Test]
        public void EvaluateTimeouts_ConnectedSessionAtExactHeartbeatTimeoutBoundary_TransitionsInclusive()
        {
            // Arrange — StaleDiscardComparer.IsTickExpired is equality-inclusive: expiry at exactly
            // the boundary tick counts as expired, not one tick later.
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act
            stateMachine.EvaluateTimeouts(currentTick: HeartbeatTimeoutTicks, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count,
                "IsTickExpired's equality-inclusive semantics mean the transition fires at exactly the boundary tick.");
        }

        [Test]
        public void EvaluateTimeouts_ConnectedSessionOneTickBeforeHeartbeatTimeout_DoesNotTransition()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act
            stateMachine.EvaluateTimeouts(currentTick: HeartbeatTimeoutTicks - 1u, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert
            Assert.IsEmpty(observer.SessionStateTransitionedCalls,
                "One tick short of the heartbeat timeout boundary must not transition.");
            Assert.IsTrue(stateMachine.TryGetSessionState(AccountA, out SessionState state));
            Assert.AreEqual(SessionState.Connected, state);
        }

        [Test]
        public void EvaluateTimeouts_HeartbeatTimeout_EntityRemainsRegistered_NoSessionResourcesReleased()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act
            stateMachine.EvaluateTimeouts(currentTick: 61u, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert — AC-NC-10: "the entity remains in the zone; no session resources are released."
            Assert.IsTrue(stateMachine.IsAccountRegistered(AccountA),
                "The account's registry entry must be retained (not removed) on a heartbeat timeout — unlike ConnectingTimeout/ExplicitDisconnect/SessionSteal.");
            Assert.IsEmpty(observer.PersistenceWriteCompletedCalls, "No persistence write occurs on a heartbeat timeout.");
        }

        [Test]
        public void EvaluateTimeouts_HeartbeatTimeoutAlreadyFired_SecondSweepDoesNotDoubleFire()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);
            stateMachine.EvaluateTimeouts(currentTick: 61u, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Act — a later sweep; the account is now Disconnected_SessionActive, not Connected.
            stateMachine.EvaluateTimeouts(currentTick: 200u, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count,
                "Once an account has left Connected state, subsequent sweeps must not re-fire HeartbeatTimeout for it.");
        }

        [Test]
        public void RecordInboundActivity_ResetsHeartbeatWindow_NoTimeoutUntilNewBaselineExpires()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act — inbound activity at tick 50 resets the silence window; tick 109 is only 59 ticks
            // after that (one short of the 60-tick boundary), so no timeout should fire yet.
            stateMachine.RecordInboundActivity(AccountA, currentTick: 50u);
            stateMachine.EvaluateTimeouts(currentTick: 109u, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert
            Assert.IsEmpty(observer.SessionStateTransitionedCalls,
                "RecordInboundActivity must reset the heartbeat-silence baseline, extending the window.");

            // Act — tick 110 is exactly 60 ticks after the reset baseline (50 + 60 = 110): expired.
            stateMachine.EvaluateTimeouts(currentTick: 110u, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual("HeartbeatTimeout", observer.SessionStateTransitionedCalls[0].trigger);
        }

        [Test]
        public void RecordInboundActivity_OnConnectingAccount_IsNoOp_DoesNotResetConnectingTimeoutBaseline()
        {
            // Arrange — LastActivityTick is the same field EvaluateTimeouts uses as the
            // CONNECTING_TIMEOUT_TICKS baseline while an account is still Connecting. A regression
            // that let inbound packets reset it here would let a client indefinitely defer its own
            // ConnectingTimeout by sending packets before auth completes (AC-NC-39-CONNECTING).
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);
            observer.Reset();

            // Act — inbound activity mid-Connecting must be a no-op; the original tick-0 baseline
            // must still govern ConnectingTimeout.
            stateMachine.RecordInboundActivity(AccountA, currentTick: 100u);
            stateMachine.EvaluateTimeouts(currentTick: ConnectingTimeoutTicks, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count,
                "The ConnectingTimeout must still fire at the original tick-0 baseline + 200 ticks — RecordInboundActivity must not have reset it.");
            Assert.AreEqual((AccountA, SessionState.Connecting, SessionState.Disconnected_SessionExpired, "ConnectingTimeout"),
                observer.SessionStateTransitionedCalls[0]);
        }

        // =========================================================================================
        // AC-NC-26: explicit disconnect (Connected -> Disconnected_SessionExpired, TTL skipped).
        // =========================================================================================

        [Test]
        public void ProcessExplicitDisconnect_ConnectedSession_RunsFullSequenceInOrderAndTransitionsDirectlyToExpired()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);
            var callOrder = new List<string>();

            // Act
            stateMachine.ProcessExplicitDisconnect(
                AccountA,
                persistFinalCharacterState: characterId =>
                {
                    callOrder.Add("persist");
                    Assert.AreEqual(CharacterA, characterId);
                    Assert.IsEmpty(observer.SessionStateTransitionedCalls,
                        "The final state transition must not have fired yet when persistence runs.");
                },
                removeEntityFromZone: characterId =>
                {
                    callOrder.Add("removeFromZone");
                    Assert.AreEqual(CharacterA, characterId);
                    Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count,
                        "Persistence must complete (and be observed) before the entity is removed from the zone.");
                },
                broadcastPlayerLeftZone: (characterId, type) =>
                {
                    callOrder.Add("broadcast");
                    Assert.AreEqual(CharacterA, characterId);
                    Assert.AreEqual(DisconnectType.Graceful, type);
                    Assert.IsEmpty(observer.SessionStateTransitionedCalls,
                        "The final state transition must not have fired yet when the PlayerLeftZone broadcast runs.");
                },
                observer);

            // Assert
            CollectionAssert.AreEqual(new[] { "persist", "removeFromZone", "broadcast" }, callOrder,
                "AC-NC-26's sequence must be followed exactly: persist -> remove from zone -> broadcast.");
            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((CharacterA, PersistenceWriteReason.ExplicitDisconnect), observer.PersistenceWriteCompletedCalls[0]);
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountA, SessionState.Connected, SessionState.Disconnected_SessionExpired, "ExplicitDisconnect"),
                observer.SessionStateTransitionedCalls[0]);
        }

        [Test]
        public void ProcessExplicitDisconnect_NeverEntersDisconnectedSessionActiveAndReleasesRegistryEntry()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act
            stateMachine.ProcessExplicitDisconnect(AccountA,
                persistFinalCharacterState: characterId => { },
                removeEntityFromZone: characterId => { },
                broadcastPlayerLeftZone: (characterId, type) => { },
                observer);

            // Assert
            foreach (var call in observer.SessionStateTransitionedCalls)
            {
                Assert.AreNotEqual(SessionState.Disconnected_SessionActive, call.toState,
                    "AC-NC-26: no Disconnected_SessionActive state is entered on an explicit disconnect.");
            }

            Assert.IsFalse(stateMachine.IsAccountRegistered(AccountA),
                "AC-NC-26: TTL is skipped entirely and session resources are released immediately.");
        }

        [Test]
        public void ProcessExplicitDisconnect_AccountNotConnected_Throws()
        {
            // Arrange — a pending (Connecting) account, never completed auth.
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.ProcessExplicitDisconnect(AccountA,
                    persistFinalCharacterState: characterId => { },
                    removeEntityFromZone: characterId => { },
                    broadcastPlayerLeftZone: (characterId, type) => { },
                    observer));
        }

        [Test]
        public void ProcessExplicitDisconnect_NullPersistDelegate_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.ProcessExplicitDisconnect(AccountA,
                    persistFinalCharacterState: null,
                    removeEntityFromZone: characterId => { },
                    broadcastPlayerLeftZone: (characterId, type) => { },
                    observer));
        }

        [Test]
        public void ProcessExplicitDisconnect_NullRemoveEntityFromZoneDelegate_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.ProcessExplicitDisconnect(AccountA,
                    persistFinalCharacterState: characterId => { },
                    removeEntityFromZone: null,
                    broadcastPlayerLeftZone: (characterId, type) => { },
                    observer));
        }

        [Test]
        public void ProcessExplicitDisconnect_NullBroadcastPlayerLeftZoneDelegate_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.ProcessExplicitDisconnect(AccountA,
                    persistFinalCharacterState: characterId => { },
                    removeEntityFromZone: characterId => { },
                    broadcastPlayerLeftZone: null,
                    observer));
        }

        // =========================================================================================
        // AC-NC-39-SESSION: session-stealing (Connected -> Disconnected_SessionExpired, then the new
        // connection proceeds to Connecting; prior-session cleanup completes before that).
        // =========================================================================================

        [Test]
        public void HandleSessionSteal_ConnectedSession_RunsFullSequenceInOrderAndNewConnectionRegistersAsConnecting()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);
            var callOrder = new List<string>();

            // Act
            stateMachine.HandleSessionSteal(
                AccountA,
                newConnectionCurrentTick: 500u,
                persistFinalCharacterState: characterId =>
                {
                    callOrder.Add("persist");
                    Assert.AreEqual(CharacterA, characterId);
                    Assert.AreEqual(1, observer.SessionInvalidatedByStealCalls.Count,
                        "OnSessionInvalidatedBySteal must fire before persistence runs (AC-NC-39-SESSION step (a)).");
                    Assert.IsEmpty(observer.SessionStateTransitionedCalls,
                        "The SessionSteal transition (step (c)) must not have fired yet when persistence (step (b)) begins.");
                },
                closePriorTransportConnection: accountId =>
                {
                    callOrder.Add("closeTransport");
                    Assert.AreEqual(AccountA, accountId);
                    Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count,
                        "Persistence must complete before the prior transport connection closes.");
                    Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count,
                        "The prior session's SessionSteal transition must fire before the transport connection closes.");
                },
                observer);

            // Assert — full ordering: (a) invalidated -> persist -> (b) persistence completed -> (c) prior
            // transitioned -> (d) transport closed -> (e) new connection's Connecting transition.
            CollectionAssert.AreEqual(new[] { "persist", "closeTransport" }, callOrder);

            Assert.AreEqual(1, observer.SessionInvalidatedByStealCalls.Count);
            Assert.AreEqual((AccountA, CharacterA), observer.SessionInvalidatedByStealCalls[0]);

            Assert.AreEqual(1, observer.PersistenceWriteCompletedCalls.Count);
            Assert.AreEqual((CharacterA, PersistenceWriteReason.SessionSteal), observer.PersistenceWriteCompletedCalls[0]);

            Assert.AreEqual(2, observer.SessionStateTransitionedCalls.Count,
                "Two transitions fire: the prior session's SessionSteal, then the new connection's NewConnection.");
            Assert.AreEqual((AccountA, SessionState.Connected, SessionState.Disconnected_SessionExpired, "SessionSteal"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.AreEqual((AccountA, SessionState.Disconnected_SessionExpired, SessionState.Connecting, "NewConnection"),
                observer.SessionStateTransitionedCalls[1]);

            Assert.IsTrue(stateMachine.TryGetSessionState(AccountA, out SessionState finalState));
            Assert.AreEqual(SessionState.Connecting, finalState,
                "After session-stealing, the account is registered fresh at Connecting for the new connection.");
        }

        [Test]
        public void HandleSessionSteal_AccountNotConnected_Throws()
        {
            // Arrange — no session registered for this account at all.
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.HandleSessionSteal(AccountA, newConnectionCurrentTick: 10u,
                    persistFinalCharacterState: _ => { },
                    closePriorTransportConnection: _ => { },
                    observer));
        }

        [Test]
        public void HandleSessionSteal_NullPersistDelegate_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.HandleSessionSteal(AccountA, newConnectionCurrentTick: 500u,
                    persistFinalCharacterState: null,
                    closePriorTransportConnection: accountId => { },
                    observer));
        }

        [Test]
        public void HandleSessionSteal_NullClosePriorTransportConnectionDelegate_ThrowsArgumentNullException()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = CreateConnectedStateMachine(observer);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                stateMachine.HandleSessionSteal(AccountA, newConnectionCurrentTick: 500u,
                    persistFinalCharacterState: characterId => { },
                    closePriorTransportConnection: null,
                    observer));
        }

        // =========================================================================================
        // AC-NC-39-CONNECTING: Connecting timeout (Connecting -> Disconnected_SessionExpired).
        // =========================================================================================

        [Test]
        public void EvaluateTimeouts_ConnectingSessionSilentTwoHundredTicks_TransitionsToExpiredReleasesSlotNoPersistenceWrite()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);
            observer.Reset(); // isolate from the EnterConnecting setup call

            // Act — CONNECTING_TIMEOUT_TICKS = 10 x 20 = 200, per AC-NC-39-CONNECTING's own scenario.
            stateMachine.EvaluateTimeouts(currentTick: 200u, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountA, SessionState.Connecting, SessionState.Disconnected_SessionExpired, "ConnectingTimeout"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.IsFalse(stateMachine.IsAccountRegistered(AccountA), "The pending session slot must be released.");
            Assert.IsEmpty(observer.PersistenceWriteCompletedCalls,
                "No session was ever established, so no persistence write may occur.");
        }

        [Test]
        public void EvaluateTimeouts_ConnectingSessionOneTickBeforeConnectingTimeout_DoesNotTransition()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);
            observer.Reset();

            // Act
            stateMachine.EvaluateTimeouts(currentTick: ConnectingTimeoutTicks - 1u, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert
            Assert.IsEmpty(observer.SessionStateTransitionedCalls);
            Assert.IsTrue(stateMachine.IsAccountRegistered(AccountA));
        }

        [Test]
        public void EvaluateTimeouts_MixedConnectingAndConnectedAccounts_EachEvaluatedIndependently()
        {
            // Arrange — one account timing out its Connecting phase, another safely Connected and
            // recently active; a single EvaluateTimeouts sweep must handle both correctly and not
            // cross-contaminate state.
            const uint accountB = 8u;
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);
            stateMachine.EnterConnecting(accountB, currentTick: 0u, observer);
            stateMachine.CompleteAuthSuccess(accountB, characterId: 777u, currentTick: 195u, observer);
            observer.Reset();

            // Act
            stateMachine.EvaluateTimeouts(currentTick: 200u, HeartbeatTimeoutTicks, ConnectingTimeoutTicks, observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountA, SessionState.Connecting, SessionState.Disconnected_SessionExpired, "ConnectingTimeout"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.IsFalse(stateMachine.IsAccountRegistered(AccountA));

            Assert.IsTrue(stateMachine.TryGetSessionState(accountB, out SessionState stateB));
            Assert.AreEqual(SessionState.Connected, stateB,
                "accountB just authenticated at tick 195 (5 ticks ago) — well inside the 60-tick heartbeat window.");
        }

        // =========================================================================================
        // Scaffolding transitions this story's own table claims (Connecting -> Connected;
        // Connecting -> Disconnected_SessionExpired via non-timeout causes) — not covered by a
        // dedicated AC in this story, but required by the transition table and exercised here for
        // completeness and as the setup path every other test above depends on.
        // =========================================================================================

        [Test]
        public void CompleteAuthSuccess_ConnectingAccount_TransitionsToConnectedWithAuthSuccessTrigger()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);
            observer.Reset();

            // Act
            stateMachine.CompleteAuthSuccess(AccountA, CharacterA, currentTick: 10u, observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountA, SessionState.Connecting, SessionState.Connected, "AuthSuccess"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.IsTrue(stateMachine.TryGetSessionState(AccountA, out SessionState state));
            Assert.AreEqual(SessionState.Connected, state);
        }

        [Test]
        public void CompleteAuthSuccess_AccountNotConnecting_Throws()
        {
            // Arrange — no session registered at all.
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.CompleteAuthSuccess(AccountA, CharacterA, currentTick: 10u, observer));
        }

        [Test]
        public void FailConnecting_ConnectingAccount_ReleasesSlotAndTransitionsWithCallerSuppliedTrigger()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);
            observer.Reset();

            // Act
            stateMachine.FailConnecting(AccountA, trigger: "AuthFailed", observer);

            // Assert
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountA, SessionState.Connecting, SessionState.Disconnected_SessionExpired, "AuthFailed"),
                observer.SessionStateTransitionedCalls[0]);
            Assert.IsFalse(stateMachine.IsAccountRegistered(AccountA));
        }

        [Test]
        public void FailConnecting_AccountNotConnecting_Throws()
        {
            // Arrange — no session registered at all.
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                stateMachine.FailConnecting(AccountA, trigger: "AuthFailed", observer));
        }

        [Test]
        public void EnterConnecting_UsesDisconnectedSessionExpiredAsSyntheticFromStateForNewConnection()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            var stateMachine = new ConnectionStateMachine();

            // Act
            stateMachine.EnterConnecting(AccountA, currentTick: 0u, observer);

            // Assert — the approved judgment call: Disconnected_SessionExpired is the synthetic
            // fromState for a connection with no real prior session.
            Assert.AreEqual(1, observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual((AccountA, SessionState.Disconnected_SessionExpired, SessionState.Connecting, "NewConnection"),
                observer.SessionStateTransitionedCalls[0]);
        }
    }
}
