using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 011 — the generic commit-before-broadcast
    /// ordering helper (<see cref="CommitBeforeBroadcastSequencer"/>). Covers AC-NC-09 (200ms
    /// simulated persistence delay), AC-NC-15 (500ms simulated persistence delay), AC-CBB-1 (the
    /// CR-NET-5.5 / CR-CP-5 write-failure protocol), and AC-CBB-2 (acknowledgment vs. outcome
    /// distinction), plus the sequencer's own validation-reject and null-argument-guard behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No real Enhancement/Respec/NPC Shop outcome type exists in this codebase yet (all three are
    /// out of scope for this story) — every test exercises
    /// <see cref="CommitBeforeBroadcastSequencer.Execute{TOutcome}"/> directly against the private
    /// <see cref="MockOutcome"/> type, per the story's own generic-seam scoping.
    /// </para>
    /// <para>
    /// <b>AC-NC-09/AC-NC-15 do not use <see cref="IServerCrashInjector"/></b> — see
    /// <see cref="CommitBeforeBroadcastSequencer"/>'s class remarks for why (no delay variant exists
    /// on that interface, and its one operation simulates a process crash, not a live server's slow
    /// write). Both delays are simulated with a real, short, blocking delay inside the test's own
    /// mock <c>persistOutcome</c> delegate, and asserted via <see cref="Stopwatch"/> timestamp
    /// comparison — no injectable time source is needed because
    /// <see cref="CommitBeforeBroadcastSequencer.Execute{TOutcome}"/> is fully synchronous: the
    /// ordering guarantee is structural, not timing-dependent.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class TickLoop_CommitBeforeBroadcast_Tests
    {
        /// <summary>A minimal mock irreversible-outcome payload, per this story's generic-seam scoping.</summary>
        private readonly struct MockOutcome
        {
            internal readonly int Value;
            internal MockOutcome(int value) => Value = value;
        }

        // =========================================================================================
        // Happy path — full sequence and ordering (covers AC-CBB-2: acknowledgment fires before
        // outcome computation begins, and is structurally distinguishable from the outcome message).
        // =========================================================================================

        [Test]
        public void Execute_ValidRequest_RunsFullSequenceInOrderAndCommits()
        {
            // Arrange
            var callOrder = new List<string>();
            MockOutcome? broadcastReceivedOutcome = null;

            // Act
            CommitBeforeBroadcastResult result = CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 7,
                validateRequest: () => { callOrder.Add("validate"); return true; },
                emitAcknowledgment: () => callOrder.Add("ack"),
                computeOutcome: () => { callOrder.Add("compute"); return new MockOutcome(99); },
                persistOutcome: outcome => { callOrder.Add("persist"); return true; },
                broadcastOutcome: outcome => { callOrder.Add("broadcast"); broadcastReceivedOutcome = outcome; },
                revertOnFailure: () => Assert.Fail("revertOnFailure must not fire on a successful commit."),
                disconnectClient: (id, reason) => Assert.Fail("disconnectClient must not fire on a successful commit."),
                preserveSessionForTtl: (id, ttl) => Assert.Fail("preserveSessionForTtl must not fire on a successful commit."));

            // Assert
            Assert.AreEqual(CommitBeforeBroadcastResult.Committed, result,
                "A valid request whose persistence write succeeds must report Committed.");
            CollectionAssert.AreEqual(
                new[] { "validate", "ack", "compute", "persist", "broadcast" },
                callOrder,
                "CR-NET-5.3's sequence must be followed exactly: validate -> acknowledge (before compute) -> compute -> persist -> broadcast (only after persist confirms).");
            Assert.IsTrue(broadcastReceivedOutcome.HasValue, "broadcastOutcome must receive the computed outcome.");
            Assert.AreEqual(99, broadcastReceivedOutcome.Value.Value, "broadcastOutcome must receive the exact outcome computeOutcome produced.");
        }

        [Test]
        public void Execute_InvalidRequest_RejectsImmediatelyWithoutFiringAnyOtherCallback()
        {
            // Arrange & Act — validateRequest returns false; every other delegate must be unreachable.
            CommitBeforeBroadcastResult result = CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 7,
                validateRequest: () => false,
                emitAcknowledgment: () => Assert.Fail("emitAcknowledgment must not fire for an invalid request."),
                computeOutcome: () => { Assert.Fail("computeOutcome must not fire for an invalid request."); return default; },
                persistOutcome: outcome => { Assert.Fail("persistOutcome must not fire for an invalid request."); return false; },
                broadcastOutcome: outcome => Assert.Fail("broadcastOutcome must not fire for an invalid request."),
                revertOnFailure: () => Assert.Fail("revertOnFailure must not fire for an invalid request."),
                disconnectClient: (id, reason) => Assert.Fail("disconnectClient must not fire for an invalid request."),
                preserveSessionForTtl: (id, ttl) => Assert.Fail("preserveSessionForTtl must not fire for an invalid request."));

            // Assert
            Assert.AreEqual(CommitBeforeBroadcastResult.RejectedInvalidRequest, result,
                "An invalid request must be rejected immediately (CR-NET-5.3 step 2), before any other step runs.");
        }

        // =========================================================================================
        // AC-NC-09 / AC-NC-15: the outcome is never broadcast before the (simulated) persistence
        // write has actually taken at least as long as it took.
        // =========================================================================================

        [Test]
        public void Execute_PersistenceWriteTakes200ms_BroadcastNotInvokedBeforeAtLeast200msElapsed()
        {
            // Arrange
            const int simulatedWriteDelayMs = 200;
            var stopwatch = Stopwatch.StartNew();
            long broadcastElapsedMs = -1;

            // Act
            CommitBeforeBroadcastResult result = CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 7,
                validateRequest: () => true,
                emitAcknowledgment: () => { },
                computeOutcome: () => new MockOutcome(1),
                persistOutcome: outcome =>
                {
                    Thread.Sleep(simulatedWriteDelayMs); // simulated persistence write duration (AC-NC-09)
                    return true;
                },
                broadcastOutcome: outcome => broadcastElapsedMs = stopwatch.ElapsedMilliseconds,
                revertOnFailure: () => Assert.Fail("revertOnFailure must not fire on a successful commit."),
                disconnectClient: (id, reason) => Assert.Fail("disconnectClient must not fire on a successful commit."),
                preserveSessionForTtl: (id, ttl) => Assert.Fail("preserveSessionForTtl must not fire on a successful commit."));

            // Assert
            Assert.AreEqual(CommitBeforeBroadcastResult.Committed, result);
            Assert.GreaterOrEqual(broadcastElapsedMs, simulatedWriteDelayMs,
                "The outcome must not be observed (broadcast) until at least as long as the simulated persistence " +
                "write took, verified by comparing timestamps (AC-NC-09).");
        }

        [Test]
        public void Execute_PersistenceWriteTakes500ms_BroadcastNotInvokedBeforeAtLeast500msElapsed()
        {
            // Arrange
            const int simulatedWriteDelayMs = 500;
            var stopwatch = Stopwatch.StartNew();
            long broadcastElapsedMs = -1;

            // Act
            CommitBeforeBroadcastResult result = CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 7,
                validateRequest: () => true,
                emitAcknowledgment: () => { },
                computeOutcome: () => new MockOutcome(1),
                persistOutcome: outcome =>
                {
                    Thread.Sleep(simulatedWriteDelayMs); // simulated persistence write duration (AC-NC-15)
                    return true;
                },
                broadcastOutcome: outcome => broadcastElapsedMs = stopwatch.ElapsedMilliseconds,
                revertOnFailure: () => Assert.Fail("revertOnFailure must not fire on a successful commit."),
                disconnectClient: (id, reason) => Assert.Fail("disconnectClient must not fire on a successful commit."),
                preserveSessionForTtl: (id, ttl) => Assert.Fail("preserveSessionForTtl must not fire on a successful commit."));

            // Assert
            Assert.AreEqual(CommitBeforeBroadcastResult.Committed, result);
            Assert.GreaterOrEqual(broadcastElapsedMs, simulatedWriteDelayMs,
                "The outcome must not be observed (broadcast) until at least as long as the simulated persistence " +
                "write took, verified by comparing timestamps (AC-NC-15).");
        }

        // =========================================================================================
        // AC-CBB-1: the CR-NET-5.5 / CR-CP-5 write-failure protocol, in CR-CP-5's numbered order —
        // revert -> disconnect -> critical alert -> preserve session.
        // =========================================================================================

        [Test]
        public void Execute_PersistenceWriteFails_RunsFullWriteFailureProtocolInCRCP5Order()
        {
            // Arrange
            var callOrder = new List<string>();
            var observer = new NetworkTestObserver();
            bool broadcastFired = false;
            LogAssert.Expect(LogType.Error, new Regex(@"\[CommitBeforeBroadcastSequencer\] PersistenceWriteFailed"));

            // Act
            CommitBeforeBroadcastResult result = CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 42,
                validateRequest: () => true,
                emitAcknowledgment: () => callOrder.Add("ack"),
                computeOutcome: () => { callOrder.Add("compute"); return new MockOutcome(7); },
                persistOutcome: outcome => { callOrder.Add("persist"); return false; }, // simulated write failure
                broadcastOutcome: outcome => { broadcastFired = true; callOrder.Add("broadcast"); },
                revertOnFailure: () => callOrder.Add("revert"),
                disconnectClient: (id, reason) =>
                {
                    callOrder.Add("disconnect");
                    Assert.AreEqual(42u, id, "The disconnected client must be the request's own clientId.");
                    Assert.AreEqual(DisconnectReason.Other, reason, "CR-NET-5.5 specifies DisconnectReason.Other.");
                    Assert.AreEqual(0, observer.CriticalInfrastructureAlertFiredCalls.Count,
                        "Disconnect (CR-CP-5 step 3) must happen before the critical alert (step 4).");
                },
                preserveSessionForTtl: (id, ttl) =>
                {
                    callOrder.Add("preserveSession");
                    Assert.AreEqual(42u, id, "The preserved session must belong to the request's own clientId.");
                    Assert.AreEqual(CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS, ttl,
                        "The session must be preserved for exactly SESSION_TTL_SECONDS.");
                    Assert.AreEqual(300, ttl,
                        "SESSION_TTL_SECONDS' tuned default is 300 (character-persistence.md) — this catches a " +
                        "regression to the literal value, not just self-consistency against the constant.");
                    Assert.AreEqual(1, observer.CriticalInfrastructureAlertFiredCalls.Count,
                        "The critical alert (CR-CP-5 step 4) must fire before session preservation (step 5).");
                },
                observer: observer);

            // Assert
            Assert.AreEqual(CommitBeforeBroadcastResult.PersistenceFailed, result);
            Assert.IsFalse(broadcastFired, "No outcome message may be emitted after a persistence write failure (CR-NET-5.1, CR-NET-5.5).");
            CollectionAssert.AreEqual(
                new[] { "ack", "compute", "persist", "revert", "disconnect", "preserveSession" },
                callOrder,
                "The write-failure protocol must run as: acknowledge -> compute -> persist(fails) -> revert -> disconnect -> preserveSession, " +
                "with the critical alert (asserted separately via the observer count checks above) firing between disconnect and preserveSession per CR-CP-5's numbered order.");
            Assert.AreEqual(1, observer.CriticalInfrastructureAlertFiredCalls.Count,
                "A critical infrastructure alert must fire exactly once (CR-CP-5 step 4).");
            Assert.AreEqual(42u, observer.CriticalInfrastructureAlertFiredCalls[0].clientId,
                "The critical alert must report the affected clientId.");
        }

        [Test]
        public void Execute_PersistOutcomeThrows_RunsFullWriteFailureProtocolInCRCP5Order()
        {
            // Arrange — code-review follow-up (qa-tester gap): persistOutcome throwing instead of
            // returning false must not let the exception propagate past Execute, silently bypassing
            // the entire CR-NET-5.5 write-failure protocol. Mirrors
            // Execute_PersistenceWriteFails_RunsFullWriteFailureProtocolInCRCP5Order exactly, except
            // the persistence failure is signaled via a thrown exception instead of a false return.
            var callOrder = new List<string>();
            var observer = new NetworkTestObserver();
            bool broadcastFired = false;
            LogAssert.Expect(LogType.Error, new Regex(@"\[CommitBeforeBroadcastSequencer\] PersistenceWriteFailed"));

            // Act
            CommitBeforeBroadcastResult result = CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 42,
                validateRequest: () => true,
                emitAcknowledgment: () => callOrder.Add("ack"),
                computeOutcome: () => { callOrder.Add("compute"); return new MockOutcome(7); },
                persistOutcome: outcome =>
                {
                    callOrder.Add("persist");
                    throw new InvalidOperationException("simulated write exception"); // simulated write failure via exception, not a false return
                },
                broadcastOutcome: outcome => { broadcastFired = true; callOrder.Add("broadcast"); },
                revertOnFailure: () => callOrder.Add("revert"),
                disconnectClient: (id, reason) =>
                {
                    callOrder.Add("disconnect");
                    Assert.AreEqual(42u, id, "The disconnected client must be the request's own clientId.");
                    Assert.AreEqual(DisconnectReason.Other, reason, "CR-NET-5.5 specifies DisconnectReason.Other.");
                    Assert.AreEqual(0, observer.CriticalInfrastructureAlertFiredCalls.Count,
                        "Disconnect (CR-CP-5 step 3) must happen before the critical alert (step 4).");
                },
                preserveSessionForTtl: (id, ttl) =>
                {
                    callOrder.Add("preserveSession");
                    Assert.AreEqual(42u, id, "The preserved session must belong to the request's own clientId.");
                    Assert.AreEqual(300, ttl, "SESSION_TTL_SECONDS' tuned default is 300 (character-persistence.md).");
                    Assert.AreEqual(1, observer.CriticalInfrastructureAlertFiredCalls.Count,
                        "The critical alert (CR-CP-5 step 4) must fire before session preservation (step 5).");
                },
                observer: observer);

            // Assert
            Assert.AreEqual(CommitBeforeBroadcastResult.PersistenceFailed, result,
                "A thrown exception from persistOutcome must be caught and treated identically to a false return.");
            Assert.IsFalse(broadcastFired, "No outcome message may be emitted after a persistence write failure, even when signaled via exception.");
            CollectionAssert.AreEqual(
                new[] { "ack", "compute", "persist", "revert", "disconnect", "preserveSession" },
                callOrder,
                "A thrown persistOutcome exception must route into the exact same write-failure protocol and " +
                "ordering as a false return: acknowledge -> compute -> persist(throws) -> revert -> disconnect -> preserveSession.");
            Assert.AreEqual(1, observer.CriticalInfrastructureAlertFiredCalls.Count,
                "A critical infrastructure alert must fire exactly once, even when the failure was signaled via exception.");
            Assert.AreEqual(42u, observer.CriticalInfrastructureAlertFiredCalls[0].clientId,
                "The critical alert must report the affected clientId.");
        }

        // =========================================================================================
        // Null-argument guards — every required delegate must be validated up front.
        // =========================================================================================

        [Test]
        public void Execute_ValidateRequestNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 1, validateRequest: null, emitAcknowledgment: () => { }, computeOutcome: () => default,
                persistOutcome: o => true, broadcastOutcome: o => { }, revertOnFailure: () => { },
                disconnectClient: (id, reason) => { }, preserveSessionForTtl: (id, ttl) => { }));
        }

        [Test]
        public void Execute_EmitAcknowledgmentNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 1, validateRequest: () => true, emitAcknowledgment: null, computeOutcome: () => default,
                persistOutcome: o => true, broadcastOutcome: o => { }, revertOnFailure: () => { },
                disconnectClient: (id, reason) => { }, preserveSessionForTtl: (id, ttl) => { }));
        }

        [Test]
        public void Execute_ComputeOutcomeNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 1, validateRequest: () => true, emitAcknowledgment: () => { }, computeOutcome: null,
                persistOutcome: o => true, broadcastOutcome: o => { }, revertOnFailure: () => { },
                disconnectClient: (id, reason) => { }, preserveSessionForTtl: (id, ttl) => { }));
        }

        [Test]
        public void Execute_PersistOutcomeNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 1, validateRequest: () => true, emitAcknowledgment: () => { }, computeOutcome: () => default,
                persistOutcome: null, broadcastOutcome: o => { }, revertOnFailure: () => { },
                disconnectClient: (id, reason) => { }, preserveSessionForTtl: (id, ttl) => { }));
        }

        [Test]
        public void Execute_BroadcastOutcomeNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 1, validateRequest: () => true, emitAcknowledgment: () => { }, computeOutcome: () => default,
                persistOutcome: o => true, broadcastOutcome: null, revertOnFailure: () => { },
                disconnectClient: (id, reason) => { }, preserveSessionForTtl: (id, ttl) => { }));
        }

        [Test]
        public void Execute_RevertOnFailureNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 1, validateRequest: () => true, emitAcknowledgment: () => { }, computeOutcome: () => default,
                persistOutcome: o => true, broadcastOutcome: o => { }, revertOnFailure: null,
                disconnectClient: (id, reason) => { }, preserveSessionForTtl: (id, ttl) => { }));
        }

        [Test]
        public void Execute_DisconnectClientNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 1, validateRequest: () => true, emitAcknowledgment: () => { }, computeOutcome: () => default,
                persistOutcome: o => true, broadcastOutcome: o => { }, revertOnFailure: () => { },
                disconnectClient: null, preserveSessionForTtl: (id, ttl) => { }));
        }

        [Test]
        public void Execute_PreserveSessionForTtlNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CommitBeforeBroadcastSequencer.Execute<MockOutcome>(
                clientId: 1, validateRequest: () => true, emitAcknowledgment: () => { }, computeOutcome: () => default,
                persistOutcome: o => true, broadcastOutcome: o => { }, revertOnFailure: () => { },
                disconnectClient: (id, reason) => { }, preserveSessionForTtl: null));
        }
    }
}
