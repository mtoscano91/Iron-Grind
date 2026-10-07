using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 031 — the irreversible-outcome coordinator
    /// (<see cref="IrreversibleOutcomeCoordinator"/>) and the shared failure protocol
    /// (<see cref="IrreversibleWriteFailureProtocol"/>), ADR-011 Decision 5. Uses the real
    /// <see cref="TickCompletionQueue"/> and <see cref="CharacterMutationGate"/>. Deterministic: no
    /// async tests, no sleeps, no real threads; every <see cref="TaskCompletionSource{TResult}"/> uses
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>.
    /// </summary>
    [TestFixture]
    internal sealed class TickLoop_IrreversibleOutcomeCoordinator_Tests
    {
        private const uint CLIENT_ID = 7u;
        private const uint OTHER_CLIENT_ID = 8u;
        private const uint CHAR_A_VALUE = 1u;
        private const uint CHAR_B_VALUE = 2u;
        private const int WATCHDOG_TICKS = 3;
        private const int LONG_WATCHDOG_TICKS = 1000;
        private const int SUCCESS_CODE = 0;
        private const int FAILURE_CODE = 1;
        private const int EXPECTED_TTL_SECONDS = 300;
        private const int DELEGATE_COUNT = 9;

        private const string STEP_VALIDATE = "validate";
        private const string STEP_ACK = "ack";
        private const string STEP_COMPUTE = "compute";
        private const string STEP_WRITE = "write";
        private const string STEP_DELIVER = "deliver";
        private const string STEP_REVERT = "revert";
        private const string STEP_DISCONNECT = "disconnect";
        private const string STEP_PRESERVE = "preserve";
        private const string STEP_GATE_OPENED = "gateOpened";

        private const string COORDINATOR_ALERT_PREFIX = @"\[IrreversibleOutcomeCoordinator\] PersistenceWriteFailed: clientId=7 charId=.* — ";
        private const string CAUSE_NON_SUCCESS = "write returned a non-success result";
        private const string CAUSE_FAULTED = "write faulted";
        private const string CAUSE_CANCELLED = "write was cancelled";
        private const string CAUSE_WATCHDOG = "watchdog timeout";
        private const string CAUSE_START_THREW = "write did not start \\(start delegate threw";
        private const string CAUSE_NO_TASK = "write did not start \\(start delegate returned no task";
        private const string CAUSE_NOT_TRACKED = "write started but could not be tracked";

        private const int TICK_THREAD_ID = 1;
        private const int OTHER_THREAD_ID = 2;
        private const int FAILURE_STEP_COUNT = 3;

        private static readonly Regex QUEUE_WATCHDOG_ALERT = new Regex("CRITICAL infrastructure alert");
        private static readonly Regex QUEUE_LATE_COMPLETION = new Regex("late completion of a timed-out task");
        private static readonly Regex QUEUE_CALLBACK_THREW = new Regex(@"\[TickCompletionQueue\] Completion callback threw");

        private static readonly CharacterID CHAR_A = new CharacterID(CHAR_A_VALUE);
        private static readonly CharacterID CHAR_B = new CharacterID(CHAR_B_VALUE);

        private TickCompletionQueue _queue;
        private CharacterMutationGate _gate;
        private NetworkTestObserver _observer;
        private IrreversibleOutcomeCoordinator _coordinator;
        private List<string> _steps;
        private List<CharacterID> _openedFor;
        private uint _tick;
        private int _threadId;

        private int ReadThreadId() => _threadId;

        [SetUp]
        public void SetUp()
        {
            _tick = 0u;
            _threadId = TICK_THREAD_ID;
            _steps = new List<string>();
            _openedFor = new List<CharacterID>();
            _observer = new NetworkTestObserver();
            _gate = new CharacterMutationGate();
            _gate.OnGateOpened += OnGateOpened;
            Rebuild(WATCHDOG_TICKS);
        }

        [TearDown]
        public void TearDown()
        {
            _gate.OnGateOpened -= OnGateOpened;
        }

        private void OnGateOpened(CharacterID charId)
        {
            _steps.Add(STEP_GATE_OPENED);
            _openedFor.Add(charId);
        }

        private void Rebuild(int watchdogTicks)
        {
            _queue = new TickCompletionQueue(watchdogTicks);
            _coordinator = new IrreversibleOutcomeCoordinator(_queue, _gate, _observer);
        }

        private void DrainNextTick()
        {
            _tick++;
            _queue.Drain(_tick);
        }

        private static TaskCompletionSource<int> NewSource() =>
            new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Expects the coordinator's critical log line naming the given cause (a regex fragment).</summary>
        private static void ExpectCoordinatorAlert(string cause) =>
            LogAssert.Expect(LogType.Error, new Regex(COORDINATOR_ALERT_PREFIX + cause));

        private sealed class Outcome
        {
        }

        /// <summary>One caller's set of delegates for a character; records every call.</summary>
        private sealed class Scenario
        {
            private readonly List<string> _steps;
            private readonly ICharacterMutationGate _gate;
            private readonly NetworkTestObserver _observer;
            private readonly uint _clientId;

            internal readonly CharacterID Char;
            internal readonly Outcome Computed = new Outcome();
            internal readonly TaskCompletionSource<int> Source = NewSource();
            internal readonly List<bool> HeldDuringSteps = new List<bool>();

            internal bool ValidateReturns = true;
            internal bool ThrowInAck;
            internal bool ThrowInCompute;
            internal bool ThrowInWrite;
            internal bool ReturnNullTask;
            internal bool ThrowInDeliver;
            internal bool ThrowInRevert;
            internal bool ThrowInIsSuccess;

            internal int WriteCalls;
            internal int DeliverCalls;
            internal Outcome SeenByWrite;
            internal Outcome Delivered;
            internal CancellationToken WriteToken;
            internal bool HeldWhenAckRan;
            internal uint DisconnectedClient;
            internal DisconnectReason DisconnectedReason;
            internal uint PreservedClient;
            internal int PreservedTtl;
            internal int AlertsAtDisconnect = -1;
            internal int AlertsAtPreserve = -1;

            internal Scenario(List<string> steps, ICharacterMutationGate gate, NetworkTestObserver observer, CharacterID charId, uint clientId)
            {
                _steps = steps;
                _gate = gate;
                _observer = observer;
                Char = charId;
                _clientId = clientId;
            }

            internal bool OnValidate()
            {
                _steps.Add(STEP_VALIDATE);
                return ValidateReturns;
            }

            internal void OnAck()
            {
                _steps.Add(STEP_ACK);
                HeldWhenAckRan = _gate.IsHeld(Char);
                if (ThrowInAck) { throw new InvalidOperationException("ack boom"); }
            }

            internal Outcome OnCompute()
            {
                _steps.Add(STEP_COMPUTE);
                if (ThrowInCompute) { throw new InvalidOperationException("compute boom"); }
                return Computed;
            }

            internal Task<int> OnWrite(Outcome outcome, CancellationToken token)
            {
                _steps.Add(STEP_WRITE);
                WriteCalls++;
                SeenByWrite = outcome;
                WriteToken = token;
                if (ThrowInWrite) { throw new InvalidOperationException("write start boom"); }
                return ReturnNullTask ? null : Source.Task;
            }

            internal bool OnIsSuccess(int value)
            {
                if (ThrowInIsSuccess) { throw new InvalidOperationException("isSuccess boom"); }
                return value == SUCCESS_CODE;
            }

            internal void OnDeliver(Outcome outcome)
            {
                _steps.Add(STEP_DELIVER);
                DeliverCalls++;
                Delivered = outcome;
                HeldDuringSteps.Add(_gate.IsHeld(Char));
                if (ThrowInDeliver) { throw new InvalidOperationException("deliver boom"); }
            }

            internal void OnRevert()
            {
                _steps.Add(STEP_REVERT);
                HeldDuringSteps.Add(_gate.IsHeld(Char));
                if (ThrowInRevert) { throw new InvalidOperationException("revert boom"); }
            }

            internal void OnDisconnect(uint clientId, DisconnectReason reason)
            {
                _steps.Add(STEP_DISCONNECT);
                HeldDuringSteps.Add(_gate.IsHeld(Char));
                DisconnectedClient = clientId;
                DisconnectedReason = reason;
                AlertsAtDisconnect = _observer.CriticalInfrastructureAlertFiredCalls.Count;
            }

            internal void OnPreserve(uint clientId, int ttlSeconds)
            {
                _steps.Add(STEP_PRESERVE);
                HeldDuringSteps.Add(_gate.IsHeld(Char));
                PreservedClient = clientId;
                PreservedTtl = ttlSeconds;
                AlertsAtPreserve = _observer.CriticalInfrastructureAlertFiredCalls.Count;
            }

            internal IrreversibleOutcomeBeginResult Begin(IIrreversibleOutcomeCoordinator coordinator) =>
                coordinator.Begin<Outcome, int>(
                    _clientId, Char, OnValidate, OnAck, OnCompute, OnWrite, OnIsSuccess, OnDeliver, OnRevert, OnDisconnect, OnPreserve);

            /// <summary>Calls Begin with the delegate at <paramref name="nullIndex"/> replaced by null.</summary>
            internal IrreversibleOutcomeBeginResult BeginWithNull(IIrreversibleOutcomeCoordinator coordinator, int nullIndex) =>
                coordinator.Begin<Outcome, int>(
                    _clientId,
                    Char,
                    nullIndex == 0 ? null : (Func<bool>)OnValidate,
                    nullIndex == 1 ? null : (Action)OnAck,
                    nullIndex == 2 ? null : (Func<Outcome>)OnCompute,
                    nullIndex == 3 ? null : (Func<Outcome, CancellationToken, Task<int>>)OnWrite,
                    nullIndex == 4 ? null : (Func<int, bool>)OnIsSuccess,
                    nullIndex == 5 ? null : (Action<Outcome>)OnDeliver,
                    nullIndex == 6 ? null : (Action)OnRevert,
                    nullIndex == 7 ? null : (Action<uint, DisconnectReason>)OnDisconnect,
                    nullIndex == 8 ? null : (Action<uint, int>)OnPreserve);
        }

        private Scenario NewScenario(CharacterID charId, uint clientId = CLIENT_ID) =>
            new Scenario(_steps, _gate, _observer, charId, clientId);

        private static readonly string[] FAILURE_STEPS = { STEP_REVERT, STEP_DISCONNECT, STEP_PRESERVE };

        private void AssertFailureProtocolRanOnce(Scenario s)
        {
            CollectionAssert.AreEqual(FAILURE_STEPS, _steps.FindAll(x => x == STEP_REVERT || x == STEP_DISCONNECT || x == STEP_PRESERVE),
                "The failure steps must run once each, in the order revert, disconnect, preserve.");
            Assert.AreEqual(CLIENT_ID, s.DisconnectedClient);
            Assert.AreEqual(DisconnectReason.Other, s.DisconnectedReason);
            Assert.AreEqual(CLIENT_ID, s.PreservedClient);
            Assert.AreEqual(EXPECTED_TTL_SECONDS, s.PreservedTtl);
            Assert.AreEqual(0, s.AlertsAtDisconnect, "The alert must not fire before the disconnect.");
            Assert.AreEqual(1, s.AlertsAtPreserve, "The alert must have fired exactly once before the session is preserved.");
            Assert.AreEqual(1, _observer.CriticalInfrastructureAlertFiredCalls.Count);
            Assert.AreEqual(CLIENT_ID, _observer.CriticalInfrastructureAlertFiredCalls[0].clientId);
            Assert.AreEqual(0, s.DeliverCalls, "Deliver must never run on the failure branch.");
            Assert.AreEqual(1, s.WriteCalls, "A failed irreversible write is never retried.");
            Assert.AreEqual(FAILURE_STEP_COUNT, s.HeldDuringSteps.Count);
            CollectionAssert.DoesNotContain(s.HeldDuringSteps, false, "The gate must be held inside every failure step.");
            Assert.IsFalse(_gate.IsHeld(s.Char), "The gate must be open afterwards.");
        }

        // =========================================================================================
        // Begin
        // =========================================================================================

        [Test]
        public void Begin_ValidRequest_RunsSequenceInOrderHoldsGateAndTracksWrite()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);

            // Act
            IrreversibleOutcomeBeginResult result = s.Begin(_coordinator);

            // Assert
            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result);
            CollectionAssert.AreEqual(new[] { STEP_VALIDATE, STEP_ACK, STEP_COMPUTE, STEP_WRITE }, _steps);
            Assert.IsTrue(s.HeldWhenAckRan, "The gate must already be closed when the acknowledgment runs.");
            Assert.IsTrue(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(1, _queue.InFlightCount);
            Assert.AreEqual(0, _openedFor.Count);
        }

        [Test]
        public void Begin_ValidationFails_ReturnsRejectedInvalidRequestWithoutClosingGate()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.ValidateReturns = false;

            // Act
            IrreversibleOutcomeBeginResult result = s.Begin(_coordinator);

            // Assert
            Assert.AreEqual(IrreversibleOutcomeBeginResult.RejectedInvalidRequest, result);
            CollectionAssert.AreEqual(new[] { STEP_VALIDATE }, _steps);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _queue.InFlightCount);
        }

        [Test]
        public void Begin_GateAlreadyClosed_ReturnsRejectedWriteInFlightAndCallsNothing()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            _gate.Close(CHAR_A);

            // Act
            IrreversibleOutcomeBeginResult result = s.Begin(_coordinator);

            // Assert
            Assert.AreEqual(IrreversibleOutcomeBeginResult.RejectedWriteInFlight, result);
            CollectionAssert.IsEmpty(_steps, "Not even validation may run.");
            Assert.IsTrue(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _openedFor.Count);
            Assert.AreEqual(0, _queue.InFlightCount);
        }

        [Test]
        public void Begin_SecondCallDuringWrite_IsRejectedAndFirstStillCompletes()
        {
            // Arrange
            Scenario first = NewScenario(CHAR_A);
            Scenario second = NewScenario(CHAR_A);
            first.Begin(_coordinator);
            _steps.Clear();

            // Act
            IrreversibleOutcomeBeginResult secondResult = second.Begin(_coordinator);
            first.Source.SetResult(SUCCESS_CODE);
            DrainNextTick();

            // Assert
            Assert.AreEqual(IrreversibleOutcomeBeginResult.RejectedWriteInFlight, secondResult);
            Assert.AreEqual(0, second.WriteCalls);
            Assert.AreEqual(1, first.DeliverCalls);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
        }

        [Test]
        public void Begin_Outcome_IsTheSameInstanceForWriteAndDeliver()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.Begin(_coordinator);

            // Act
            s.Source.SetResult(SUCCESS_CODE);
            DrainNextTick();

            // Assert
            Assert.AreSame(s.Computed, s.SeenByWrite);
            Assert.AreSame(s.Computed, s.Delivered);
        }

        // =========================================================================================
        // Completion
        // =========================================================================================

        [Test]
        public void Drain_WriteIncomplete_CallsNothingAndGateStaysHeld()
        {
            // Arrange
            Rebuild(LONG_WATCHDOG_TICKS);
            Scenario s = NewScenario(CHAR_A);
            s.Begin(_coordinator);
            _steps.Clear();

            // Act
            DrainNextTick();
            DrainNextTick();
            DrainNextTick();

            // Assert
            CollectionAssert.IsEmpty(_steps);
            Assert.IsTrue(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(1, _queue.InFlightCount);
        }

        [Test]
        public void Drain_WriteSucceeds_DeliversOnceAndOpensGateOnce()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.Begin(_coordinator);
            _steps.Clear();

            // Act
            s.Source.SetResult(SUCCESS_CODE);
            DrainNextTick();
            DrainNextTick();

            // Assert
            CollectionAssert.AreEqual(new[] { STEP_DELIVER, STEP_GATE_OPENED }, _steps);
            Assert.AreEqual(1, s.DeliverCalls);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            CollectionAssert.AreEqual(new[] { CHAR_A }, _openedFor);
            Assert.AreEqual(0, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        [Test]
        public void Drain_WriteReturnsNonSuccessCode_RunsFailureProtocolInOrderOnce()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.Begin(_coordinator);
            _steps.Clear();
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            // Act
            s.Source.SetResult(FAILURE_CODE);
            DrainNextTick();
            DrainNextTick();

            // Assert
            CollectionAssert.AreEqual(new[] { STEP_REVERT, STEP_DISCONNECT, STEP_PRESERVE, STEP_GATE_OPENED }, _steps);
            AssertFailureProtocolRanOnce(s);
        }

        [Test]
        public void Drain_WriteFaults_RunsFailureProtocolOnce()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.Begin(_coordinator);
            _steps.Clear();
            ExpectCoordinatorAlert(CAUSE_FAULTED);

            // Act
            s.Source.SetException(new InvalidOperationException("db down"));
            DrainNextTick();
            DrainNextTick();

            // Assert
            CollectionAssert.AreEqual(new[] { STEP_REVERT, STEP_DISCONNECT, STEP_PRESERVE, STEP_GATE_OPENED }, _steps);
            AssertFailureProtocolRanOnce(s);
        }

        [Test]
        public void Drain_WriteCancelled_RunsFailureProtocolOnce()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.Begin(_coordinator);
            _steps.Clear();
            ExpectCoordinatorAlert(CAUSE_CANCELLED);

            // Act
            s.Source.SetCanceled();
            DrainNextTick();
            DrainNextTick();

            // Assert
            CollectionAssert.AreEqual(new[] { STEP_REVERT, STEP_DISCONNECT, STEP_PRESERVE, STEP_GATE_OPENED }, _steps);
            AssertFailureProtocolRanOnce(s);
        }

        [Test]
        public void Drain_WatchdogExpires_RunsFailureProtocolOnceAndCancelsWriteToken()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.Begin(_coordinator);
            _steps.Clear();
            Assert.IsFalse(s.WriteToken.IsCancellationRequested);
            LogAssert.Expect(LogType.Error, QUEUE_WATCHDOG_ALERT);
            ExpectCoordinatorAlert(CAUSE_WATCHDOG);

            // Act
            _tick = WATCHDOG_TICKS - 1;
            DrainNextTick();

            // Assert
            CollectionAssert.AreEqual(new[] { STEP_REVERT, STEP_DISCONNECT, STEP_PRESERVE, STEP_GATE_OPENED }, _steps);
            AssertFailureProtocolRanOnce(s);
            Assert.IsTrue(s.WriteToken.IsCancellationRequested, "The watchdog must cancel the token given to the write.");
        }

        [Test]
        public void Drain_WriteCompletesAfterTimeout_CausesNoFurtherCall()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.Begin(_coordinator);
            LogAssert.Expect(LogType.Error, QUEUE_WATCHDOG_ALERT);
            ExpectCoordinatorAlert(CAUSE_WATCHDOG);
            _tick = WATCHDOG_TICKS - 1;
            DrainNextTick();
            _steps.Clear();
            LogAssert.Expect(LogType.Error, QUEUE_LATE_COMPLETION);

            // Act
            s.Source.SetResult(SUCCESS_CODE);
            DrainNextTick();

            // Assert
            CollectionAssert.IsEmpty(_steps, "A late completion must trigger neither deliver nor a second failure protocol.");
            Assert.AreEqual(0, s.DeliverCalls);
            Assert.AreEqual(1, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        [Test]
        public void Drain_FailureOrSuccess_GateIsHeldInsideEveryStepAndOpensAfterTheLast()
        {
            // Arrange
            Scenario success = NewScenario(CHAR_A);
            Scenario failure = NewScenario(CHAR_B);
            success.Begin(_coordinator);
            failure.Begin(_coordinator);
            _steps.Clear();
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            // Act
            success.Source.SetResult(SUCCESS_CODE);
            failure.Source.SetResult(FAILURE_CODE);
            DrainNextTick();

            // Assert
            CollectionAssert.AreEqual(
                new[] { STEP_DELIVER, STEP_GATE_OPENED, STEP_REVERT, STEP_DISCONNECT, STEP_PRESERVE, STEP_GATE_OPENED }, _steps);
            Assert.AreEqual(1, success.HeldDuringSteps.Count);
            Assert.AreEqual(3, failure.HeldDuringSteps.Count);
            CollectionAssert.DoesNotContain(success.HeldDuringSteps, false);
            CollectionAssert.DoesNotContain(failure.HeldDuringSteps, false);
        }

        [Test]
        public void Drain_DeliverThrows_QueueLogsAndGateIsOpen()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.ThrowInDeliver = true;
            s.Begin(_coordinator);
            s.Source.SetResult(SUCCESS_CODE);
            LogAssert.Expect(LogType.Error, QUEUE_CALLBACK_THREW);

            // Act
            TestDelegate drain = DrainNextTick;

            // Assert
            Assert.DoesNotThrow(drain);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(1, _openedFor.Count);
        }

        [Test]
        public void Drain_RevertThrows_QueueLogsGateIsOpenAndLaterStepsAreSkipped()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.ThrowInRevert = true;
            s.Begin(_coordinator);
            _steps.Clear();
            s.Source.SetResult(FAILURE_CODE);
            LogAssert.Expect(LogType.Error, QUEUE_CALLBACK_THREW);

            // Act
            TestDelegate drain = DrainNextTick;

            // Assert
            Assert.DoesNotThrow(drain);
            CollectionAssert.AreEqual(new[] { STEP_REVERT, STEP_GATE_OPENED }, _steps, "No catch between steps: disconnect and preserve are skipped.");
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        // =========================================================================================
        // Failures inside Begin
        // =========================================================================================

        [Test]
        public void Begin_WriteDelegateThrows_RunsFailureProtocolAtOnceAndOpensGate()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.ThrowInWrite = true;
            ExpectCoordinatorAlert(CAUSE_START_THREW);

            // Act
            IrreversibleOutcomeBeginResult result = s.Begin(_coordinator);

            // Assert
            Assert.AreEqual(IrreversibleOutcomeBeginResult.PersistenceFailed, result);
            CollectionAssert.AreEqual(
                new[] { STEP_VALIDATE, STEP_ACK, STEP_COMPUTE, STEP_WRITE, STEP_REVERT, STEP_DISCONNECT, STEP_PRESERVE, STEP_GATE_OPENED }, _steps);
            AssertFailureProtocolRanOnce(s);
            Assert.AreEqual(0, _queue.InFlightCount);
        }

        [Test]
        public void Begin_WriteDelegateReturnsNull_RunsFailureProtocolAtOnceAndOpensGate()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.ReturnNullTask = true;
            ExpectCoordinatorAlert(CAUSE_NO_TASK);

            // Act
            IrreversibleOutcomeBeginResult result = s.Begin(_coordinator);

            // Assert
            Assert.AreEqual(IrreversibleOutcomeBeginResult.PersistenceFailed, result);
            CollectionAssert.AreEqual(
                new[] { STEP_VALIDATE, STEP_ACK, STEP_COMPUTE, STEP_WRITE, STEP_REVERT, STEP_DISCONNECT, STEP_PRESERVE, STEP_GATE_OPENED }, _steps);
            AssertFailureProtocolRanOnce(s);
            Assert.AreEqual(0, _queue.InFlightCount);
        }

        [Test]
        public void Begin_ComputeThrows_ExceptionReachesCallerGateOpenNoWriteNoFailureStep()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.ThrowInCompute = true;

            // Act
            TestDelegate begin = () => s.Begin(_coordinator);

            // Assert
            Assert.Throws<InvalidOperationException>(begin);
            CollectionAssert.AreEqual(new[] { STEP_VALIDATE, STEP_ACK, STEP_COMPUTE, STEP_GATE_OPENED }, _steps);
            Assert.AreEqual(0, s.WriteCalls);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _queue.InFlightCount);
            Assert.AreEqual(0, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        [Test]
        public void Begin_AcknowledgeThrows_ExceptionReachesCallerGateOpenComputeNotCalled()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.ThrowInAck = true;

            // Act
            TestDelegate begin = () => s.Begin(_coordinator);

            // Assert
            Assert.Throws<InvalidOperationException>(begin);
            CollectionAssert.AreEqual(new[] { STEP_VALIDATE, STEP_ACK, STEP_GATE_OPENED }, _steps);
            Assert.AreEqual(0, s.WriteCalls);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        // =========================================================================================
        // Guards and sharing
        // =========================================================================================

        [Test]
        public void Drain_TwoCharactersOneCompletes_DeliversThatOneAndLeavesTheOtherHeld()
        {
            // Arrange
            Scenario a = NewScenario(CHAR_A);
            Scenario b = NewScenario(CHAR_B, OTHER_CLIENT_ID);
            IrreversibleOutcomeBeginResult resultA = a.Begin(_coordinator);
            IrreversibleOutcomeBeginResult resultB = b.Begin(_coordinator);

            // Act
            b.Source.SetResult(SUCCESS_CODE);
            DrainNextTick();

            // Assert
            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, resultA);
            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, resultB, "A write in flight for A must not affect Begin for B.");
            Assert.AreEqual(1, b.DeliverCalls);
            Assert.IsFalse(_gate.IsHeld(CHAR_B));
            Assert.AreEqual(0, a.DeliverCalls);
            Assert.IsTrue(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(1, _queue.InFlightCount);
        }

        [Test]
        public void Drain_TwoCharactersOtherCompletesLater_DeliversItOnItsOwnTick()
        {
            // Arrange
            Scenario a = NewScenario(CHAR_A);
            Scenario b = NewScenario(CHAR_B, OTHER_CLIENT_ID);
            a.Begin(_coordinator);
            b.Begin(_coordinator);
            b.Source.SetResult(SUCCESS_CODE);
            DrainNextTick();

            // Act
            a.Source.SetResult(SUCCESS_CODE);
            DrainNextTick();

            // Assert
            Assert.AreEqual(1, a.DeliverCalls);
            Assert.AreEqual(1, b.DeliverCalls);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _queue.InFlightCount);
            CollectionAssert.AreEqual(new[] { CHAR_B, CHAR_A }, _openedFor);
        }

        // =========================================================================================
        // Edge cases (code review 2026-10-07)
        // =========================================================================================

        [Test]
        public void Begin_WriteAlreadyCompleteWhenStarted_IsHandledByTheNextDrainNotInsideBegin()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.Source.SetResult(SUCCESS_CODE);

            // Act
            IrreversibleOutcomeBeginResult result = s.Begin(_coordinator);
            int deliverCallsAfterBegin = s.DeliverCalls;
            bool heldAfterBegin = _gate.IsHeld(CHAR_A);
            DrainNextTick();

            // Assert
            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result);
            Assert.AreEqual(0, deliverCallsAfterBegin, "Deliver must wait for the next Drain.");
            Assert.IsTrue(heldAfterBegin);
            Assert.AreEqual(1, s.DeliverCalls);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
        }

        [Test]
        public void Begin_SameCharacterAfterSuccess_StartsANewWrite()
        {
            // Arrange
            Scenario first = NewScenario(CHAR_A);
            Scenario second = NewScenario(CHAR_A);
            first.Begin(_coordinator);
            first.Source.SetResult(SUCCESS_CODE);
            DrainNextTick();

            // Act
            IrreversibleOutcomeBeginResult result = second.Begin(_coordinator);

            // Assert
            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result);
            Assert.AreEqual(1, second.WriteCalls);
            Assert.IsTrue(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(1, _queue.InFlightCount);
        }

        [Test]
        public void Begin_SameCharacterAfterFailure_StartsANewWrite()
        {
            // Arrange
            Scenario first = NewScenario(CHAR_A);
            Scenario second = NewScenario(CHAR_A);
            first.Begin(_coordinator);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);
            first.Source.SetResult(FAILURE_CODE);
            DrainNextTick();

            // Act
            IrreversibleOutcomeBeginResult result = second.Begin(_coordinator);

            // Assert
            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result);
            Assert.AreEqual(1, second.WriteCalls);
            Assert.IsTrue(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(1, _queue.InFlightCount);
        }

        [Test]
        public void Drain_IsSuccessThrows_QueueLogsGateIsOpenAndNoStepRuns()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.ThrowInIsSuccess = true;
            s.Begin(_coordinator);
            _steps.Clear();
            s.Source.SetResult(SUCCESS_CODE);
            LogAssert.Expect(LogType.Error, QUEUE_CALLBACK_THREW);

            // Act
            TestDelegate drain = DrainNextTick;

            // Assert
            Assert.DoesNotThrow(drain);
            CollectionAssert.AreEqual(new[] { STEP_GATE_OPENED }, _steps, "Neither deliver nor a failure step runs; the gate still opens.");
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        [Test]
        public void Begin_WriteDoesNotStartAndRevertThrows_ExceptionReachesCallerAndGateIsOpen()
        {
            // Arrange
            Scenario s = NewScenario(CHAR_A);
            s.ThrowInWrite = true;
            s.ThrowInRevert = true;

            // Act
            TestDelegate begin = () => s.Begin(_coordinator);

            // Assert
            Assert.Throws<InvalidOperationException>(begin);
            CollectionAssert.AreEqual(
                new[] { STEP_VALIDATE, STEP_ACK, STEP_COMPUTE, STEP_WRITE, STEP_REVERT, STEP_GATE_OPENED }, _steps,
                "No catch between steps: disconnect and preserve are skipped.");
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _queue.InFlightCount);
            Assert.AreEqual(0, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        [Test]
        public void Begin_TrackThrowsAfterWriteStarted_RunsFailureProtocolOpensGateAndRethrows()
        {
            // Arrange
            _queue = new TickCompletionQueue(WATCHDOG_TICKS, 0u, ReadThreadId);
            _coordinator = new IrreversibleOutcomeCoordinator(_queue, _gate, _observer);
            Scenario s = NewScenario(CHAR_A);
            _threadId = OTHER_THREAD_ID;
            ExpectCoordinatorAlert(CAUSE_NOT_TRACKED);

            // Act
            TestDelegate begin = () => s.Begin(_coordinator);

            // Assert
            Assert.Throws<InvalidOperationException>(begin);
            CollectionAssert.AreEqual(
                new[] { STEP_VALIDATE, STEP_ACK, STEP_COMPUTE, STEP_WRITE, STEP_REVERT, STEP_DISCONNECT, STEP_PRESERVE, STEP_GATE_OPENED }, _steps);
            AssertFailureProtocolRanOnce(s);
            Assert.AreEqual(0, _queue.InFlightCount);
            Assert.DoesNotThrow(() => { WaitHandle unused = s.WriteToken.WaitHandle; },
                "The token source must not be disposed: the write is still running with its token.");
        }

        [Test]
        public void Constructor_NullQueueOrGate_ThrowsArgumentNullException()
        {
            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => new IrreversibleOutcomeCoordinator(null, _gate));
            Assert.Throws<ArgumentNullException>(() => new IrreversibleOutcomeCoordinator(_queue, null));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void Begin_NullDelegate_ThrowsArgumentNullExceptionBeforeAnythingRuns(int nullIndex)
        {
            // Arrange
            Assert.Less(nullIndex, DELEGATE_COUNT);
            Scenario s = NewScenario(CHAR_A);

            // Act
            TestDelegate begin = () => s.BeginWithNull(_coordinator, nullIndex);

            // Assert
            Assert.Throws<ArgumentNullException>(begin);
            CollectionAssert.IsEmpty(_steps);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _queue.InFlightCount);
        }

        [Test]
        public void Run_FailureProtocol_CallsStepsInOrderWithReasonOtherAndTtl()
        {
            // Arrange
            var order = new List<string>();
            uint disconnected = 0u;
            DisconnectReason reason = DisconnectReason.Other;
            int ttl = 0;
            LogAssert.Expect(LogType.Error, new Regex("direct protocol call"));

            // Act
            IrreversibleWriteFailureProtocol.Run(
                CLIENT_ID,
                "direct protocol call",
                "reason",
                () => order.Add(STEP_REVERT),
                (id, r) => { order.Add(STEP_DISCONNECT); disconnected = id; reason = r; },
                (id, seconds) => { order.Add(STEP_PRESERVE); ttl = seconds; },
                _observer);

            // Assert
            CollectionAssert.AreEqual(FAILURE_STEPS, order);
            Assert.AreEqual(CLIENT_ID, disconnected);
            Assert.AreEqual(DisconnectReason.Other, reason);
            Assert.AreEqual(CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS, ttl);
            Assert.AreEqual(1, _observer.CriticalInfrastructureAlertFiredCalls.Count);
            Assert.AreEqual("reason", _observer.CriticalInfrastructureAlertFiredCalls[0].reason);
        }

        [Test]
        public void Execute_PersistenceReturnsFalse_StillLogsSequencerMessageThroughSharedProtocol()
        {
            // Arrange
            var order = new List<string>();
            LogAssert.Expect(LogType.Error, new Regex(@"\[CommitBeforeBroadcastSequencer\] PersistenceWriteFailed: clientId=7 — SaveIrreversibleOutcome failed\."));

            // Act
            CommitBeforeBroadcastResult result = CommitBeforeBroadcastSequencer.Execute<Outcome>(
                CLIENT_ID,
                () => true,
                () => { },
                () => new Outcome(),
                outcome => false,
                outcome => order.Add(STEP_DELIVER),
                () => order.Add(STEP_REVERT),
                (id, r) => order.Add(STEP_DISCONNECT),
                (id, seconds) => order.Add(STEP_PRESERVE),
                _observer);

            // Assert
            Assert.AreEqual(CommitBeforeBroadcastResult.PersistenceFailed, result);
            CollectionAssert.AreEqual(FAILURE_STEPS, order);
            Assert.AreEqual(1, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }
    }
}
