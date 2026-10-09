using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// EditMode unit tests for Networking Core Story 037 — <see cref="ZoneTickPipeline"/> (ADR-014
    /// Decision 6): construction, the fixed order of the four steps, the single tick number, the
    /// isolation of a throwing step, and one end-to-end tick with the real
    /// <see cref="TickCompletionQueue"/> and the real <see cref="InboundRequestDispatcher"/>.
    /// Deterministic: no sleeps, no threads, no random; a completed write is <c>Task.FromResult</c>.
    /// </summary>
    /// <remarks>
    /// Every recording fake appends one entry to the fixture's shared log, so the order is asserted on
    /// that one list. A fake logs its entry first and then runs its behaviour and its failure.
    /// </remarks>
    [TestFixture]
    internal sealed class TickLoop_ZoneTickPipeline_Tests
    {
        private const uint TICK_SEVEN = 7u;
        private const uint TICK_EIGHT = 8u;
        private const uint TICK_CHANGED_BY_STEP = 99u;
        private const float DELTA_TIME = 0.05f;
        private const float OTHER_DELTA_TIME = 0.123f;
        private const string BOOM = "boom";
        private const string OTHER_MESSAGE = "bad";
        private const string INVALID_OPERATION_TYPE_NAME = nameof(InvalidOperationException);
        private const int STEP_COUNT = 4;
        private const int STEP_COMPLETION = 0;
        private const int STEP_REQUESTS = 1;
        private const int STEP_SIMULATION = 2;
        private const int STEP_OUTBOUND = 3;
        private const uint E2E_HELD_TICK = 20u;
        private const uint E2E_RELEASE_TICK = 21u;

        private static readonly string[] StepNames = { "Completion", "Requests", "Simulation", "Outbound" };
        private static readonly string[] ParameterNames =
            { "completionQueue", "dispatcher", "simulation", "outbound", "tickSource" };

        private List<string> _log;
        private InboundSettableTickSource _tickSource;
        private ZoneTickPipelineRecordingCompletionQueue _completion;
        private ZoneTickPipelineRecordingDispatcher _dispatcher;
        private ZoneTickPipelineRecordingSimulation _simulation;
        private ZoneTickPipelineRecordingOutbound _outbound;

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
            _tickSource = new InboundSettableTickSource { Tick = TICK_SEVEN };
            _completion = new ZoneTickPipelineRecordingCompletionQueue(_log);
            _dispatcher = new ZoneTickPipelineRecordingDispatcher(_log);
            _simulation = new ZoneTickPipelineRecordingSimulation(_log);
            _outbound = new ZoneTickPipelineRecordingOutbound(_log);
        }

        private ZoneTickPipeline CreatePipeline() =>
            new ZoneTickPipeline(_completion, _dispatcher, _simulation, _outbound, _tickSource);

        private static string DrainEntry(uint tick) => $"Drain({tick})";

        private static string DispatchEntry(uint tick) => $"DispatchTick({tick})";

        private static string RunEntry(uint tick, float deltaTime) =>
            $"Run({tick}, {deltaTime.ToString("R", CultureInfo.InvariantCulture)})";

        private static string FlushEntry(uint tick) => $"Flush({tick})";

        private static string[] ExpectedTick(uint tick, float deltaTime) =>
            new[] { DrainEntry(tick), DispatchEntry(tick), RunEntry(tick, deltaTime), FlushEntry(tick) };

        private static Regex StepFailedRegex(string stepName, uint tick,
            string exceptionTypeName = INVALID_OPERATION_TYPE_NAME, string message = BOOM) =>
            new Regex("^" + Regex.Escape(
                $"[ZoneTickPipeline] StepFailed: step={stepName} tick={tick} - {exceptionTypeName}: {message}") + "$");

        private void ThrowInStep(int stepIndex, Func<uint, bool> throwWhen)
        {
            switch (stepIndex)
            {
                case STEP_COMPLETION: _completion.ThrowWhen = throwWhen; break;
                case STEP_REQUESTS: _dispatcher.ThrowWhen = throwWhen; break;
                case STEP_SIMULATION: _simulation.ThrowWhen = throwWhen; break;
                case STEP_OUTBOUND: _outbound.ThrowWhen = throwWhen; break;
                default: throw new ArgumentOutOfRangeException(nameof(stepIndex));
            }
        }

        // =========================================================================================
        // Construction
        // =========================================================================================

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void Constructor_OneArgumentNull_ThrowsArgumentNullExceptionNamingIt(int nullIndex)
        {
            // Arrange
            object[] arguments = { _completion, _dispatcher, _simulation, _outbound, _tickSource };
            arguments[nullIndex] = null;

            // Act
            ArgumentNullException thrown = Assert.Throws<ArgumentNullException>(() => new ZoneTickPipeline(
                (ITickCompletionQueue)arguments[0],
                (IInboundRequestDispatcher)arguments[1],
                (IZoneSimulationStep)arguments[2],
                (IZoneOutboundStep)arguments[3],
                (IServerTickSource)arguments[4]));

            // Assert
            Assert.AreEqual(ParameterNames[nullIndex], thrown.ParamName);
        }

        [Test]
        public void Tick_RegisteredOnServerTickLoopWithoutLambda_EachStepRunsOnceOnAdvanceTick()
        {
            // Arrange
            var tickLoop = new ServerTickLoop();
            var pipeline = new ZoneTickPipeline(_completion, _dispatcher, _simulation, _outbound, tickLoop);
            tickLoop.RegisterTickDriven(pipeline.Tick);
            uint expectedTick = tickLoop.ServerTickNumber + 1u;

            // Act
            tickLoop.AdvanceTick();

            // Assert
            CollectionAssert.AreEqual(ExpectedTick(expectedTick, ServerTickLoop.FIXED_DELTA_TIME), _log);
            LogAssert.NoUnexpectedReceived();
        }

        // =========================================================================================
        // Order
        // =========================================================================================

        [Test]
        public void Tick_NothingThrows_RunsFourStepsInOrderWithTheTick()
        {
            // Arrange
            ZoneTickPipeline pipeline = CreatePipeline();

            // Act
            pipeline.Tick(DELTA_TIME);

            // Assert
            CollectionAssert.AreEqual(ExpectedTick(TICK_SEVEN, DELTA_TIME), _log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Tick_CompletionStepChangesTickSource_LaterStepsStillReceiveTheTickReadFirst()
        {
            // Arrange
            _completion.Behaviour = () => _tickSource.Tick = TICK_CHANGED_BY_STEP;
            ZoneTickPipeline pipeline = CreatePipeline();

            // Act
            pipeline.Tick(DELTA_TIME);

            // Assert
            CollectionAssert.AreEqual(ExpectedTick(TICK_SEVEN, DELTA_TIME), _log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Tick_CalledOnTwoTicks_FirstFourEntriesCarrySevenAndLastFourCarryEight()
        {
            // Arrange
            ZoneTickPipeline pipeline = CreatePipeline();

            // Act
            pipeline.Tick(DELTA_TIME);
            _tickSource.Tick = TICK_EIGHT;
            pipeline.Tick(DELTA_TIME);

            // Assert
            var expected = new List<string>(ExpectedTick(TICK_SEVEN, DELTA_TIME));
            expected.AddRange(ExpectedTick(TICK_EIGHT, DELTA_TIME));
            CollectionAssert.AreEqual(expected, _log);
            Assert.AreEqual(STEP_COUNT * 2, _log.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Tick_DeltaTime_ReachesTheSimulationStepUnchangedAndNoOtherStep()
        {
            // Arrange
            ZoneTickPipeline pipeline = CreatePipeline();

            // Act
            pipeline.Tick(OTHER_DELTA_TIME);

            // Assert
            Assert.AreEqual(OTHER_DELTA_TIME, _simulation.LastDeltaTime);
            CollectionAssert.AreEqual(ExpectedTick(TICK_SEVEN, OTHER_DELTA_TIME), _log,
                "Only the Run entry carries a delta time.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Tick_NoStepThrows_LogsNothing()
        {
            // Arrange
            ZoneTickPipeline pipeline = CreatePipeline();

            // Act
            pipeline.Tick(DELTA_TIME);

            // Assert
            Assert.AreEqual(STEP_COUNT, _log.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // =========================================================================================
        // Isolation
        // =========================================================================================

        [TestCase(STEP_COMPLETION)]
        [TestCase(STEP_REQUESTS)]
        [TestCase(STEP_SIMULATION)]
        [TestCase(STEP_OUTBOUND)]
        public void Tick_OneStepThrows_OtherStepsStillRunOnceInOrderAndOneErrorIsLogged(int throwingStep)
        {
            // Arrange
            ThrowInStep(throwingStep, tick => true);
            ZoneTickPipeline pipeline = CreatePipeline();
            LogAssert.Expect(LogType.Error, StepFailedRegex(StepNames[throwingStep], TICK_SEVEN));

            // Act
            Assert.DoesNotThrow(() => pipeline.Tick(DELTA_TIME));

            // Assert
            CollectionAssert.AreEqual(ExpectedTick(TICK_SEVEN, DELTA_TIME), _log,
                "Every step is called exactly once, with the same arguments as when nothing throws.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Tick_AllFourStepsThrow_ReturnsAndLogsFourErrorsInStepOrder()
        {
            // Arrange
            for (int step = 0; step < STEP_COUNT; step++)
            {
                ThrowInStep(step, tick => true);
                LogAssert.Expect(LogType.Error, StepFailedRegex(StepNames[step], TICK_SEVEN));
            }
            ZoneTickPipeline pipeline = CreatePipeline();

            // Act
            Assert.DoesNotThrow(() => pipeline.Tick(DELTA_TIME));

            // Assert
            CollectionAssert.AreEqual(ExpectedTick(TICK_SEVEN, DELTA_TIME), _log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Tick_StepThrowsAnotherExceptionType_LogsTheRuntimeTypeName()
        {
            // Arrange
            _simulation.ThrowWhen = tick => true;
            _simulation.ExceptionToThrow = new ZoneTickPipelineFakeStepException(OTHER_MESSAGE);
            ZoneTickPipeline pipeline = CreatePipeline();
            LogAssert.Expect(LogType.Error, StepFailedRegex(StepNames[STEP_SIMULATION], TICK_SEVEN,
                nameof(ZoneTickPipelineFakeStepException), OTHER_MESSAGE));

            // Act
            Assert.DoesNotThrow(() => pipeline.Tick(DELTA_TIME));

            // Assert
            CollectionAssert.AreEqual(ExpectedTick(TICK_SEVEN, DELTA_TIME), _log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Tick_ExceptionMessageGetterThrows_ReturnsWithoutLoggingAndLaterStepsStillRun()
        {
            // Arrange
            _simulation.ThrowWhen = tick => true;
            _simulation.ExceptionToThrow = new ZoneTickPipelineThrowingMessageException();
            ZoneTickPipeline pipeline = CreatePipeline();

            // Act
            Assert.DoesNotThrow(() => pipeline.Tick(DELTA_TIME));

            // Assert
            CollectionAssert.AreEqual(ExpectedTick(TICK_SEVEN, DELTA_TIME), _log,
                "The outbound step must still run when the failed step's log call throws.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Tick_SimulationFailedOnPreviousTick_NextTickRunsAllStepsAndLogsNothing()
        {
            // Arrange
            _simulation.ThrowWhen = tick => tick == TICK_SEVEN;
            ZoneTickPipeline pipeline = CreatePipeline();
            LogAssert.Expect(LogType.Error, StepFailedRegex(StepNames[STEP_SIMULATION], TICK_SEVEN));
            pipeline.Tick(DELTA_TIME);
            _log.Clear();
            _tickSource.Tick = TICK_EIGHT;

            // Act
            pipeline.Tick(DELTA_TIME);

            // Assert
            CollectionAssert.AreEqual(ExpectedTick(TICK_EIGHT, DELTA_TIME), _log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AdvanceTick_OutboundStepThrows_TtlTimerFiresAndObserverIsStillCalled()
        {
            // Arrange
            var tickLoop = new ServerTickLoop();
            var observer = new NetworkTestObserver();
            var pipeline = new ZoneTickPipeline(_completion, _dispatcher, _simulation, _outbound, tickLoop);
            tickLoop.RegisterTickDriven(pipeline.Tick);
            int ttlCalls = 0;
            tickLoop.RegisterTtlTimer(tickLoop.ServerTickNumber + 1u, () => ttlCalls++);
            _outbound.ThrowWhen = tick => true;
            uint expectedTick = tickLoop.ServerTickNumber + 1u;
            LogAssert.Expect(LogType.Error, StepFailedRegex(StepNames[STEP_OUTBOUND], expectedTick));

            // Act
            Assert.DoesNotThrow(() => tickLoop.AdvanceTick(observer: observer));

            // Assert
            Assert.AreEqual(1, ttlCalls, "The TTL timer due on that tick must fire once.");
            CollectionAssert.AreEqual(new[] { tickLoop.ServerTickNumber }, observer.TickCompletedCalls);
            Assert.AreEqual(expectedTick, tickLoop.ServerTickNumber);
            LogAssert.NoUnexpectedReceived();
        }

        // =========================================================================================
        // End to end with the real completion queue and the real dispatcher
        // =========================================================================================

        [Test]
        public void Tick_RealQueueAndDispatcher_RunsCompletionThenHeldRequestThenNewRequestThenSimulationThenOutbound()
        {
            // Arrange — a closed gate holds the HeldRod request on the first pipeline tick.
            var harness = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            CharacterID character = InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne);
            var queue = new TickCompletionQueue();
            var pipeline = new ZoneTickPipeline(queue, harness.Dispatcher, _simulation, _outbound, harness.TickSource);
            harness.Handler.Behaviour = context => _log.Add(HandlerEntry(context.MessageTypeId, context.WasHeld));
            harness.Gate.Close(character);
            harness.TickSource.Tick = E2E_HELD_TICK;
            Assert.IsTrue(harness.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeHeldRod, 1u,
                InboundMessageBuilder.Body(InboundTestIds.HeldRodBodySize, InboundTestIds.SeedOne)));
            pipeline.Tick(DELTA_TIME);
            Assert.AreEqual(0, harness.Handler.Calls.Count, "The held request's handler must not run while the gate is closed.");
            Assert.AreEqual(1, harness.Dispatcher.HeldCount(character));
            _log.Clear();

            // Arrange — a completion that opens the gate, and a new plain request.
            queue.Track(Task.FromResult(true), null, result =>
            {
                _log.Add("completion");
                harness.Gate.Open(character);
            });
            harness.TickSource.Tick = E2E_RELEASE_TICK;
            Assert.IsTrue(harness.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, 2u,
                InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, InboundTestIds.SeedTwo)));

            // Act
            pipeline.Tick(DELTA_TIME);

            // Assert
            var expected = new[]
            {
                "completion",
                HandlerEntry(InboundTestIds.TypeHeldRod, true),
                HandlerEntry(InboundTestIds.TypePlainRod, false),
                RunEntry(E2E_RELEASE_TICK, DELTA_TIME),
                FlushEntry(E2E_RELEASE_TICK),
            };
            CollectionAssert.AreEqual(expected, _log);
            Assert.AreEqual(0, harness.Dispatcher.HeldCount(character));
            LogAssert.NoUnexpectedReceived();
        }

        private static string HandlerEntry(ushort messageTypeId, bool wasHeld) =>
            $"Handler(0x{messageTypeId:X4}, WasHeld={wasHeld})";

        // =========================================================================================
        // Recording fakes. Prefixed ZoneTickPipeline so they cannot collide with other doubles.
        // =========================================================================================

        private static void ThrowIfDue(Func<uint, bool> throwWhen, uint tick, Exception exceptionToThrow = null)
        {
            if (throwWhen != null && throwWhen(tick))
            {
                throw exceptionToThrow ?? new InvalidOperationException(BOOM);
            }
        }

        /// <summary>An exception type the pipeline cannot know, so the logged name is the runtime type's.</summary>
        private sealed class ZoneTickPipelineFakeStepException : Exception
        {
            public ZoneTickPipelineFakeStepException(string message) : base(message)
            {
            }
        }

        /// <summary>An exception whose <c>Message</c> getter throws, so the pipeline's log call fails.</summary>
        private sealed class ZoneTickPipelineThrowingMessageException : Exception
        {
            public override string Message => throw new InvalidOperationException(BOOM);
        }

        private sealed class ZoneTickPipelineRecordingCompletionQueue : ITickCompletionQueue
        {
            private readonly List<string> _entries;

            public Action Behaviour;
            public Func<uint, bool> ThrowWhen;

            public ZoneTickPipelineRecordingCompletionQueue(List<string> entries)
            {
                _entries = entries;
            }

            public int InFlightCount => 0;

            public void Drain(uint currentTick)
            {
                _entries.Add(DrainEntry(currentTick));
                Behaviour?.Invoke();
                ThrowIfDue(ThrowWhen, currentTick);
            }

            public void Track<T>(Task<T> task, CancellationTokenSource cancellation, Action<TickTaskResult<T>> onCompletedOnTick)
            {
                throw new NotSupportedException();
            }

            public void DrainOnShutdown(TimeSpan timeout)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class ZoneTickPipelineRecordingDispatcher : IInboundRequestDispatcher
        {
            private readonly List<string> _entries;

            public Func<uint, bool> ThrowWhen;

            public ZoneTickPipelineRecordingDispatcher(List<string> entries)
            {
                _entries = entries;
            }

            public void DispatchTick(uint currentTick)
            {
                _entries.Add(DispatchEntry(currentTick));
                ThrowIfDue(ThrowWhen, currentTick);
            }

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler)
            {
                throw new NotSupportedException();
            }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink)
            {
                throw new NotSupportedException();
            }

            public void Seal()
            {
                throw new NotSupportedException();
            }

            public bool AddConnection(uint clientId)
            {
                throw new NotSupportedException();
            }

            public void RemoveConnection(uint clientId)
            {
                throw new NotSupportedException();
            }

            public int HeldCount(CharacterID charId)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class ZoneTickPipelineRecordingSimulation : IZoneSimulationStep
        {
            private readonly List<string> _entries;

            public Func<uint, bool> ThrowWhen;
            public Exception ExceptionToThrow;
            public float LastDeltaTime;

            public ZoneTickPipelineRecordingSimulation(List<string> entries)
            {
                _entries = entries;
            }

            public void Run(uint currentTick, float deltaTime)
            {
                LastDeltaTime = deltaTime;
                _entries.Add(RunEntry(currentTick, deltaTime));
                ThrowIfDue(ThrowWhen, currentTick, ExceptionToThrow);
            }
        }

        private sealed class ZoneTickPipelineRecordingOutbound : IZoneOutboundStep
        {
            private readonly List<string> _entries;

            public Func<uint, bool> ThrowWhen;

            public ZoneTickPipelineRecordingOutbound(List<string> entries)
            {
                _entries = entries;
            }

            public void Flush(uint currentTick)
            {
                _entries.Add(FlushEntry(currentTick));
                ThrowIfDue(ThrowWhen, currentTick);
            }
        }
    }
}
