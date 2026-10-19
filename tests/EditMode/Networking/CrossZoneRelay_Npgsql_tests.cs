using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode tests for Networking Core Story 041 - the PostgreSQL transport of the cross-zone relay (ADR-015
    /// Decisions 2 and 3): <see cref="NpgsqlRelayPublisher"/> over a fake command seam and a real
    /// <see cref="TickCompletionQueue"/>, and <see cref="NpgsqlRelayListener"/> over a fake connection seam, with the
    /// loop run on the test thread. No database, no real timing: backoff delays go through a recording seam, and the
    /// cancellation sources are recording subclasses the test cancels and inspects itself. The few real-thread tests
    /// use a fake connection and join their thread; every blocked fake is released in TearDown.
    /// </summary>
    [TestFixture]
    internal sealed class CrossZoneRelay_Npgsql_Tests
    {
        private const ushort Broadcast9 = ServerBroadcast_Enhancement9.MessageTypeId;
        private const int MinBody = ServerBroadcast_Enhancement9.MinBodySize;
        private const int MaxBody = ServerBroadcast_Enhancement9.MaxBodySize;
        private const ulong OwnOrigin = 0x1111222233334444ul;
        private const ulong OtherOrigin = 0x5555666677778888ul;
        private const string PublisherPrefix = "[NpgsqlRelayPublisher]";
        private const string ListenerPrefix = "[NpgsqlRelayListener]";
        private const string Channel = NpgsqlRelayPublisher.RELAY_CHANNEL_NAME;
        private const string BaseConnectionString = "Host=127.0.0.1;Port=5432;Username=u;Password=p;Database=d";
        private const int JoinWaitMs = 5000;

        private readonly List<Action> _cleanup = new List<Action>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _cleanup.Count; i++)
            {
                _cleanup[i]();
            }

            _cleanup.Clear();
        }

        // ---- helpers ----------------------------------------------------------------------------

        private static byte[] Body(int length, int seed)
        {
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = (byte)(seed + i);
            }

            return bytes;
        }

        private static Regex LogLine(string text)
        {
            return new Regex(Regex.Escape(text));
        }

        private static string RepoRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static string RelaySource(string fileName)
        {
            return File.ReadAllText(Path.Combine(RepoRoot(), "src", "ServerLogic", "Networking", "CrossZoneRelay", fileName));
        }

        private static Exception MakeException(int kind)
        {
            switch (kind)
            {
                // ADR-012 Decision 1: only IronGrind.ServerLogic references Npgsql.dll, so the real type is made by name.
                case 0: return (Exception)Activator.CreateInstance(Type.GetType("Npgsql.NpgsqlException, Npgsql", true), "fake npgsql");
                case 1: return new IOException("fake io");
                case 2: return new ObjectDisposedException("fake disposed");
                default: return new InvalidOperationException("fake invalid");
            }
        }

        // ---- publisher doubles ------------------------------------------------------------------

        private sealed class RecordingCts : CancellationTokenSource
        {
            public int DisposeCount;

            protected override void Dispose(bool disposing)
            {
                DisposeCount++;
                base.Dispose(disposing);
            }
        }

        private sealed class StartCall
        {
            public string Channel;
            public string Payload;
            public int TimeoutMs;
            public CancellationToken Token;
            public TaskCompletionSource<int> Source;
        }

        private sealed class FakeStarter : IRelayNotifyCommandStarter
        {
            public readonly List<StartCall> Calls = new List<StartCall>();
            public Exception ThrowOnStart;

            public Task<int> Start(string channel, string payload, int commandTimeoutMs, CancellationToken cancellationToken)
            {
                var call = new StartCall { Channel = channel, Payload = payload, TimeoutMs = commandTimeoutMs, Token = cancellationToken };
                Calls.Add(call);
                if (ThrowOnStart != null)
                {
                    throw ThrowOnStart;
                }

                call.Source = new TaskCompletionSource<int>();
                cancellationToken.Register(CancelCall, call);
                return call.Source.Task;
            }

            private static void CancelCall(object state)
            {
                ((StartCall)state).Source.TrySetCanceled();
            }
        }

        private sealed class PublisherRig
        {
            public readonly FakeStarter Starter = new FakeStarter();
            public readonly TickCompletionQueue Queue;
            public readonly List<RecordingCts> Sources = new List<RecordingCts>();
            public readonly List<int> RequestedTimeouts = new List<int>();
            public readonly NpgsqlRelayPublisher Publisher;
            private uint _tick;

            public PublisherRig(int maxInFlight = NpgsqlRelayPublisher.RELAY_MAX_PUBLISHES_IN_FLIGHT, int watchdogTicks = 200)
            {
                Queue = new TickCompletionQueue(watchdogTicks);
                Publisher = new NpgsqlRelayPublisher(
                    Starter, Queue, new CrossZoneRelayOrigin(OwnOrigin), Channel, maxInFlight,
                    NpgsqlRelayPublisher.RELAY_PUBLISH_TIMEOUT_MS, CreateSource);
            }

            public void Publish(int seed = 1)
            {
                Publisher.Publish(Broadcast9, Body(MinBody, seed));
            }

            public void Drain(uint? tick = null)
            {
                _tick = tick ?? _tick + 1u;
                Queue.Drain(_tick);
            }

            private CancellationTokenSource CreateSource(int timeoutMs)
            {
                RequestedTimeouts.Add(timeoutMs);
                var source = new RecordingCts();
                Sources.Add(source);
                return source;
            }
        }

        // ---- publisher --------------------------------------------------------------------------

        [Test]
        public void test_publisher_publish_returns_at_once_for_a_task_that_never_completes_and_hands_it_to_the_queue()
        {
            var rig = new PublisherRig();

            rig.Publish();
            rig.Drain();

            Assert.AreEqual(1, rig.Starter.Calls.Count);
            Assert.AreEqual(1, rig.Publisher.InFlightCount);
            Assert.AreEqual(1, rig.Queue.InFlightCount);
            Assert.AreEqual(0, rig.Sources[0].DisposeCount, "Nothing ended; the source stays alive.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_timeout_cancel_runs_the_callback_on_the_next_drain_logs_one_warning_frees_the_slot_and_disposes_the_source()
        {
            var rig = new PublisherRig();
            rig.Publish();
            Assert.AreEqual(NpgsqlRelayPublisher.RELAY_PUBLISH_TIMEOUT_MS, rig.RequestedTimeouts[0]);
            Assert.AreEqual(NpgsqlRelayPublisher.RELAY_PUBLISH_TIMEOUT_MS, rig.Starter.Calls[0].TimeoutMs, "The command's own timeout is the same value.");
            Assert.AreEqual(rig.Sources[0].Token, rig.Starter.Calls[0].Token);
            rig.Drain();
            Assert.AreEqual(1, rig.Publisher.InFlightCount);

            rig.Sources[0].Cancel();
            Assert.IsTrue(rig.Starter.Calls[0].Source.Task.IsCanceled);
            Assert.AreEqual(1, rig.Publisher.InFlightCount, "The callback has not run before the next Drain.");
            Assert.AreEqual(0, rig.Sources[0].DisposeCount);
            LogAssert.NoUnexpectedReceived();
            LogAssert.Expect(LogType.Warning, LogLine($"{PublisherPrefix} PublishTimedOut: status=Canceled timeoutMs=2000"));
            rig.Drain();

            Assert.AreEqual(0, rig.Publisher.InFlightCount);
            Assert.AreEqual(1, rig.Sources[0].DisposeCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_queue_watchdog_timeout_logs_one_warning_frees_the_slot_and_disposes_the_source()
        {
            var rig = new PublisherRig(NpgsqlRelayPublisher.RELAY_MAX_PUBLISHES_IN_FLIGHT, 1);
            rig.Publish();

            LogAssert.Expect(LogType.Error, LogLine("[TickCompletionQueue] CRITICAL"));
            LogAssert.Expect(LogType.Warning, LogLine($"{PublisherPrefix} PublishTimedOut: status=TimedOut"));
            rig.Drain(5u);

            Assert.AreEqual(0, rig.Publisher.InFlightCount);
            Assert.AreEqual(1, rig.Sources[0].DisposeCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_success_frees_the_slot_disposes_the_source_and_logs_nothing()
        {
            var rig = new PublisherRig();
            rig.Publish();

            rig.Starter.Calls[0].Source.SetResult(1);
            rig.Drain();

            Assert.AreEqual(0, rig.Publisher.InFlightCount);
            Assert.AreEqual(1, rig.Sources[0].DisposeCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_failure_logs_one_warning_frees_the_slot_and_disposes_the_source()
        {
            var rig = new PublisherRig();
            rig.Publish();

            rig.Starter.Calls[0].Source.SetException(new InvalidOperationException("db down"));
            LogAssert.Expect(LogType.Warning, LogLine($"{PublisherPrefix} PublishFailed: InvalidOperationException: db down"));
            rig.Drain();

            Assert.AreEqual(0, rig.Publisher.InFlightCount);
            Assert.AreEqual(1, rig.Sources[0].DisposeCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_a_freed_slot_is_reused_with_a_new_source_each_time()
        {
            var rig = new PublisherRig(1);

            for (int i = 0; i < 3; i++)
            {
                rig.Publish(i);
                rig.Starter.Calls[i].Source.SetResult(1);
                rig.Drain();
            }

            Assert.AreEqual(3, rig.Starter.Calls.Count);
            Assert.AreEqual(3, rig.Sources.Count);
            foreach (RecordingCts source in rig.Sources)
            {
                Assert.AreEqual(1, source.DisposeCount);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_bound_ninth_publish_starts_nothing_logs_one_warning_and_a_completion_frees_room()
        {
            var rig = new PublisherRig();
            for (int i = 0; i < NpgsqlRelayPublisher.RELAY_MAX_PUBLISHES_IN_FLIGHT; i++)
            {
                rig.Publish(i);
            }

            LogAssert.Expect(LogType.Warning, LogLine($"{PublisherPrefix} PublishDropped: type=0xE103 - 8 publishes are already in flight"));
            Assert.DoesNotThrow(() => rig.Publish(99));

            Assert.AreEqual(8, rig.Starter.Calls.Count, "The ninth started nothing.");
            Assert.AreEqual(8, rig.Sources.Count, "The ninth created no source.");
            Assert.AreEqual(8, rig.Publisher.InFlightCount);

            rig.Starter.Calls[0].Source.SetResult(1);
            rig.Drain();
            Assert.AreEqual(7, rig.Publisher.InFlightCount);
            rig.Publish(100);

            Assert.AreEqual(9, rig.Starter.Calls.Count);
            Assert.AreEqual(8, rig.Publisher.InFlightCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_synchronous_throw_from_the_seam_frees_the_slot_disposes_the_source_logs_one_warning_and_returns()
        {
            var rig = new PublisherRig();
            rig.Starter.ThrowOnStart = new InvalidOperationException("starter fake");
            LogAssert.Expect(LogType.Warning, LogLine($"{PublisherPrefix} PublishStartFailed: type=0xE103 - InvalidOperationException: starter fake"));

            Assert.DoesNotThrow(() => rig.Publish());

            Assert.AreEqual(0, rig.Publisher.InFlightCount);
            Assert.AreEqual(0, rig.Queue.InFlightCount, "Nothing was handed to the queue.");
            Assert.AreEqual(1, rig.Sources.Count);
            Assert.AreEqual(1, rig.Sources[0].DisposeCount);

            rig.Starter.ThrowOnStart = null;
            rig.Publish();
            Assert.AreEqual(1, rig.Publisher.InFlightCount, "The slot is usable again.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_source_factory_throwing_frees_the_slot_starts_nothing_logs_one_warning_and_returns()
        {
            var starter = new FakeStarter();
            var queue = new TickCompletionQueue();
            var publisher = new NpgsqlRelayPublisher(
                starter, queue, new CrossZoneRelayOrigin(OwnOrigin), Channel, 1, NpgsqlRelayPublisher.RELAY_PUBLISH_TIMEOUT_MS, ThrowingSourceFactory);
            LogAssert.Expect(LogType.Warning, LogLine($"{PublisherPrefix} PublishStartFailed: type=0xE103 - InvalidOperationException: source fake"));

            Assert.DoesNotThrow(() => publisher.Publish(Broadcast9, Body(MinBody, 1)));

            Assert.AreEqual(0, publisher.InFlightCount);
            Assert.AreEqual(0, starter.Calls.Count, "Nothing was started.");
            Assert.AreEqual(0, queue.InFlightCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_queue_track_throwing_cancels_the_started_task_frees_the_slot_disposes_the_source_and_logs_one_warning()
        {
            var starter = new FakeStarter();
            var queue = new ThrowingCompletionQueue();
            var sources = new List<RecordingCts>();
            var publisher = new NpgsqlRelayPublisher(
                starter, queue, new CrossZoneRelayOrigin(OwnOrigin), Channel, 1, NpgsqlRelayPublisher.RELAY_PUBLISH_TIMEOUT_MS,
                ms => { var s = new RecordingCts(); sources.Add(s); return s; });
            LogAssert.Expect(LogType.Warning, LogLine($"{PublisherPrefix} PublishStartFailed: type=0xE103 - InvalidOperationException: track fake"));

            Assert.DoesNotThrow(() => publisher.Publish(Broadcast9, Body(MinBody, 1)));

            Assert.AreEqual(1, starter.Calls.Count);
            Assert.IsTrue(starter.Calls[0].Source.Task.IsCanceled, "The untracked command was cancelled.");
            Assert.AreEqual(0, publisher.InFlightCount, "The slot is free again.");
            Assert.AreEqual(1, sources[0].DisposeCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_default_timed_source_cancels_a_task_that_never_completes_and_the_next_drain_frees_the_slot()
        {
            var starter = new FakeStarter();
            var queue = new TickCompletionQueue();
            // sourceFactory null: the production timed CancellationTokenSource, here with a 1 ms timeout.
            var publisher = new NpgsqlRelayPublisher(starter, queue, new CrossZoneRelayOrigin(OwnOrigin), Channel, 1, 1, null);

            publisher.Publish(Broadcast9, Body(MinBody, 1));
            Task<int> task = starter.Calls[0].Source.Task;

            Assert.IsTrue(SpinWait.SpinUntil(() => task.IsCompleted, JoinWaitMs), "The real timer cancelled the task.");
            Assert.IsTrue(task.IsCanceled);
            LogAssert.Expect(LogType.Warning, LogLine($"{PublisherPrefix} PublishTimedOut: status=Canceled timeoutMs=1"));
            queue.Drain(1u);
            Assert.AreEqual(0, publisher.InFlightCount);
            LogAssert.NoUnexpectedReceived();
        }

        private static CancellationTokenSource ThrowingSourceFactory(int timeoutMs)
        {
            throw new InvalidOperationException("source fake");
        }

        private sealed class ThrowingCompletionQueue : ITickCompletionQueue
        {
            public int InFlightCount
            {
                get { return 0; }
            }

            public void Track<T>(Task<T> task, CancellationTokenSource cancellation, Action<TickTaskResult<T>> onCompletedOnTick)
            {
                throw new InvalidOperationException("track fake");
            }

            public void Drain(uint currentTick)
            {
            }

            public void DrainOnShutdown(TimeSpan timeout)
            {
            }
        }

        [Test]
        public void test_publisher_throwing_seam_leaves_the_broadcasters_local_delivery_and_result_unchanged()
        {
            var seam = new OutboxRecordingSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(1u);
            outbox.AddClient(2u);
            var rig = new PublisherRig();
            rig.Starter.ThrowOnStart = new InvalidOperationException("starter fake");
            var broadcaster = new ServerWideBroadcaster(outbox, rig.Publisher, CrossZoneRelayAllowlist.CreateProduction());
            LogAssert.Expect(LogType.Warning, LogLine($"{PublisherPrefix} PublishStartFailed: type=0xE103 - InvalidOperationException: starter fake"));

            bool accepted = false;
            Assert.DoesNotThrow(() => accepted = broadcaster.Broadcast(Broadcast9, Body(MinBody, 1)));
            outbox.Flush(1u);

            Assert.IsTrue(accepted);
            Assert.AreEqual(1, rig.Starter.Calls.Count);
            Assert.AreEqual(2, seam.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(MinBody)]
        [TestCase(MaxBody)]
        public void test_publisher_seam_gets_the_channel_and_an_envelope_the_codec_decodes_to_type_body_and_this_origin(int length)
        {
            var rig = new PublisherRig();
            byte[] body = Body(length, 40);

            rig.Publisher.Publish(Broadcast9, body);

            StartCall call = rig.Starter.Calls[0];
            var buffer = new byte[RelayEnvelopeCodec.DecodeBufferSize(MaxBody)];
            RelayEnvelopeDecodeResult result = RelayEnvelopeCodec.TryDecode(
                call.Payload, buffer, out ushort type, out ulong origin, out int decodedLength);
            Assert.AreEqual("irongrind_relay", call.Channel);
            Assert.AreEqual(Channel, call.Channel);
            Assert.AreEqual(RelayEnvelopeDecodeResult.Ok, result);
            Assert.AreEqual(Broadcast9, type);
            Assert.AreEqual(OwnOrigin, origin);
            Assert.AreEqual(length, decodedLength);
            CollectionAssert.AreEqual(body, new ArraySegment<byte>(buffer, 0, decodedLength).ToArray());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_constructor_argument_checks_name_the_argument()
        {
            var starter = new FakeStarter();
            var queue = new TickCompletionQueue();
            var origin = new CrossZoneRelayOrigin(1ul);

            Assert.AreEqual("dataSource", PublicPublisherConstructorWithNullDataSource(queue, origin).ParamName);
            Assert.AreEqual("starter", Assert.Throws<ArgumentNullException>(() => new NpgsqlRelayPublisher(null, queue, origin, Channel, 8, 2000, null)).ParamName);
            Assert.AreEqual("completionQueue", Assert.Throws<ArgumentNullException>(() => new NpgsqlRelayPublisher(starter, null, origin, Channel, 8, 2000, null)).ParamName);
            Assert.AreEqual("origin", Assert.Throws<ArgumentNullException>(() => new NpgsqlRelayPublisher(starter, queue, null, Channel, 8, 2000, null)).ParamName);
            Assert.AreEqual("channelName", Assert.Throws<ArgumentNullException>(() => new NpgsqlRelayPublisher(starter, queue, origin, null, 8, 2000, null)).ParamName);
            Assert.AreEqual("channelName", Assert.Throws<ArgumentException>(() => new NpgsqlRelayPublisher(starter, queue, origin, string.Empty, 8, 2000, null)).ParamName);
            Assert.AreEqual("maxPublishesInFlight", Assert.Throws<ArgumentOutOfRangeException>(() => new NpgsqlRelayPublisher(starter, queue, origin, Channel, 0, 2000, null)).ParamName);
            Assert.AreEqual("publishTimeoutMs", Assert.Throws<ArgumentOutOfRangeException>(() => new NpgsqlRelayPublisher(starter, queue, origin, Channel, 8, 0, null)).ParamName);
        }

        // ADR-012 Decision 1: the test assembly does not reference Npgsql.dll, so the public constructor (whose first
        // parameter is an NpgsqlDataSource) is reached by reflection, with a null data source.
        private static ArgumentNullException PublicPublisherConstructorWithNullDataSource(ITickCompletionQueue queue, CrossZoneRelayOrigin origin)
        {
            ConstructorInfo constructor = typeof(NpgsqlRelayPublisher).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Single(c => c.GetParameters()[0].ParameterType.FullName == "Npgsql.NpgsqlDataSource");
            var exception = Assert.Throws<TargetInvocationException>(() => constructor.Invoke(new object[]
            {
                null, queue, origin, NpgsqlRelayPublisher.RELAY_CHANNEL_NAME,
                NpgsqlRelayPublisher.RELAY_MAX_PUBLISHES_IN_FLIGHT, NpgsqlRelayPublisher.RELAY_PUBLISH_TIMEOUT_MS,
            }));
            Assert.IsInstanceOf<ArgumentNullException>(exception.InnerException);
            return (ArgumentNullException)exception.InnerException;
        }

        // ---- listener doubles -------------------------------------------------------------------

        private sealed class FakeConnection : IRelayListenerConnection
        {
            private readonly ListenerScript _script;
            private readonly object _lock = new object();
            private readonly List<string> _events = new List<string>();

            public Action OnOpen;
            public Action OnListen;
            public Func<int, bool> OnWait;
            public int WaitCount;
            public int DisposeCount;
            public int DisposeThreadId = -1;
            public bool HandlerSetAtOpen;
            public Action<string> Handler;

            public FakeConnection(ListenerScript script)
            {
                _script = script;
            }

            public Action<string> NotificationHandler
            {
                set { Handler = value; }
            }

            public List<string> Events
            {
                get
                {
                    lock (_lock)
                    {
                        return new List<string>(_events);
                    }
                }
            }

            public void StopOnWaitNumber(int number)
            {
                OnWait = ms =>
                {
                    if (WaitCount >= number)
                    {
                        _script.Listener.Stop();
                    }

                    return false;
                };
            }

            public void Raise(string payload)
            {
                Handler(payload);
            }

            public void Open()
            {
                Record("Open");
                HandlerSetAtOpen = Handler != null;
                OnOpen?.Invoke();
            }

            public void Listen(string channel)
            {
                Record("Listen:" + channel);
                OnListen?.Invoke();
            }

            public bool Wait(int timeoutMs)
            {
                Record("Wait:" + timeoutMs);
                WaitCount++;
                return OnWait != null && OnWait(timeoutMs);
            }

            public void Dispose()
            {
                Record("Dispose");
                DisposeCount++;
                DisposeThreadId = Thread.CurrentThread.ManagedThreadId;
            }

            private void Record(string text)
            {
                lock (_lock)
                {
                    _events.Add(text);
                }
            }
        }

        private sealed class ListenerScript
        {
            public readonly Queue<FakeConnection> Pending = new Queue<FakeConnection>();
            public readonly List<FakeConnection> Created = new List<FakeConnection>();
            public readonly List<int> Delays = new List<int>();
            public readonly CrossZoneRelayInbox Inbox;
            public readonly CrossZoneRelayReceiver Receiver;
            public NpgsqlRelayListener Listener;
            public Action<int> DelayHook;
            public bool Exhausted;

            public ListenerScript()
            {
                CrossZoneRelayAllowlist allowlist = CrossZoneRelayAllowlist.CreateProduction();
                Inbox = new CrossZoneRelayInbox(allowlist.LargestMaxBodyBytes);
                Receiver = new CrossZoneRelayReceiver(allowlist, Inbox, new CrossZoneRelayOrigin(OwnOrigin));
            }

            public FakeConnection Add()
            {
                var connection = new FakeConnection(this);
                Pending.Enqueue(connection);
                return connection;
            }

            public IRelayListenerConnection Create()
            {
                if (Pending.Count == 0)
                {
                    Exhausted = true;
                    Listener.Stop();
                    throw new InvalidOperationException("listener script exhausted");
                }

                FakeConnection connection = Pending.Dequeue();
                Created.Add(connection);
                return connection;
            }

            public bool Delay(int milliseconds, ManualResetEventSlim stopSignal)
            {
                Delays.Add(milliseconds);
                DelayHook?.Invoke(milliseconds);
                return stopSignal.IsSet;
            }
        }

        private static NpgsqlRelayListener NewListener(
            ListenerScript script,
            int waitMs = NpgsqlRelayListener.RELAY_LISTENER_WAIT_MS,
            int maxBackoffMs = NpgsqlRelayListener.RELAY_RECONNECT_MAX_BACKOFF_MS,
            int alertAfter = NpgsqlRelayListener.RELAY_RECONNECT_ALERT_AFTER,
            int stopTimeoutMs = NpgsqlRelayListener.RELAY_LISTENER_STOP_TIMEOUT_MS,
            string channel = Channel)
        {
            script.Listener = new NpgsqlRelayListener(
                script.Create, script.Receiver, channel, waitMs, maxBackoffMs, alertAfter, stopTimeoutMs, script.Delay);
            return script.Listener;
        }

        private static void ExpectLost(int failures, Exception exception)
        {
            LogAssert.Expect(LogType.Warning, LogLine($"{ListenerPrefix} ConnectionLost: consecutiveFailures={failures} - {exception.GetType().Name}"));
        }

        private static void FailOpen(FakeConnection connection, Exception exception)
        {
            connection.OnOpen = () => { throw exception; };
        }

        // ---- listener loop ----------------------------------------------------------------------

        [Test]
        public void test_listener_loop_opens_listens_on_the_channel_then_waits_repeatedly_until_stop_and_disposes()
        {
            var script = new ListenerScript();
            FakeConnection connection = script.Add();
            connection.StopOnWaitNumber(3);
            NewListener(script);

            script.Listener.RunLoop();

            CollectionAssert.AreEqual(
                new[] { "Open", "Listen:irongrind_relay", "Wait:500", "Wait:500", "Wait:500", "Dispose" }, connection.Events);
            Assert.IsTrue(connection.HandlerSetAtOpen, "The notification handler is set before the connection opens.");
            Assert.AreEqual(1, script.Created.Count);
            CollectionAssert.IsEmpty(script.Delays);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_loop_uses_the_channel_and_wait_values_it_was_given()
        {
            var script = new ListenerScript();
            FakeConnection connection = script.Add();
            connection.StopOnWaitNumber(1);
            NewListener(script, waitMs: 123, channel: "other_channel");

            script.Listener.RunLoop();

            CollectionAssert.AreEqual(new[] { "Open", "Listen:other_channel", "Wait:123", "Dispose" }, connection.Events);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_notification_payload_reaches_the_receiver_once_and_the_body_is_queued()
        {
            var script = new ListenerScript();
            FakeConnection connection = script.Add();
            byte[] body = Body(MaxBody, 9);
            string payload = RelayEnvelopeCodec.Encode(Broadcast9, OtherOrigin, body);
            connection.OnWait = ms =>
            {
                connection.Raise(payload);
                script.Listener.Stop();
                return true;
            };
            NewListener(script);

            script.Listener.RunLoop();
            var handler = new RelayRecordingHandler();
            script.Inbox.Drain(1u, handler);

            Assert.AreEqual(1, handler.Received.Count);
            Assert.AreEqual(Broadcast9, handler.Received[0].MessageTypeId);
            CollectionAssert.AreEqual(body, handler.Received[0].Body);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_notification_goes_to_the_receiver_which_refuses_a_hostile_payload_without_stopping_the_loop()
        {
            var script = new ListenerScript();
            FakeConnection connection = script.Add();
            connection.OnWait = ms =>
            {
                connection.Raise("v9:garbage");
                script.Listener.Stop();
                return true;
            };
            NewListener(script);
            LogAssert.Expect(LogType.Warning, LogLine("[CrossZoneRelayReceiver] PayloadDropped: reason=UnknownVersion"));

            script.Listener.RunLoop();

            Assert.AreEqual(1, script.Receiver.RefusedPayloadCount);
            Assert.AreEqual(0, script.Inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_reconnect_each_exception_type_at_open_listen_or_wait_disposes_logs_waits_500_and_listens_again(
            [Values(0, 1, 2, 3)] int kind, [Values(0, 1, 2)] int stage)
        {
            var script = new ListenerScript();
            Exception exception = MakeException(kind);
            FakeConnection first = script.Add();
            var expectedFirst = new List<string> { "Open" };
            if (stage == 0)
            {
                FailOpen(first, exception);
            }
            else
            {
                expectedFirst.Add("Listen:irongrind_relay");
                if (stage == 1)
                {
                    first.OnListen = () => { throw exception; };
                }
                else
                {
                    expectedFirst.Add("Wait:500");
                    first.OnWait = ms => { throw exception; };
                }
            }

            expectedFirst.Add("Dispose");
            FakeConnection second = script.Add();
            second.StopOnWaitNumber(1);
            NewListener(script);
            ExpectLost(1, exception);

            Assert.DoesNotThrow(() => script.Listener.RunLoop());

            CollectionAssert.AreEqual(expectedFirst, first.Events);
            Assert.AreEqual(1, first.DisposeCount);
            CollectionAssert.AreEqual(new[] { 500 }, script.Delays);
            CollectionAssert.AreEqual(new[] { "Open", "Listen:irongrind_relay", "Wait:500", "Dispose" }, second.Events);
            Assert.IsFalse(script.Exhausted);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_backoff_doubles_from_500_to_the_cap_and_stays_there()
        {
            var script = new ListenerScript();
            Exception exception = MakeException(3);
            for (int i = 0; i < 7; i++)
            {
                FailOpen(script.Add(), exception);
            }

            script.Add().StopOnWaitNumber(1);
            NewListener(script, alertAfter: 100);
            for (int i = 1; i <= 7; i++)
            {
                ExpectLost(i, exception);
            }

            script.Listener.RunLoop();

            CollectionAssert.AreEqual(new[] { 500, 1000, 2000, 4000, 8000, 10000, 10000 }, script.Delays);
            Assert.IsFalse(script.Exhausted);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_backoff_cap_is_the_constructor_value()
        {
            var script = new ListenerScript();
            Exception exception = MakeException(1);
            for (int i = 0; i < 6; i++)
            {
                FailOpen(script.Add(), exception);
            }

            script.Add().StopOnWaitNumber(1);
            NewListener(script, maxBackoffMs: 3000, alertAfter: 100);
            for (int i = 1; i <= 6; i++)
            {
                ExpectLost(i, exception);
            }

            script.Listener.RunLoop();

            CollectionAssert.AreEqual(new[] { 500, 1000, 2000, 3000, 3000, 3000 }, script.Delays);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_fifth_consecutive_failure_logs_an_error_and_the_others_a_warning()
        {
            var script = new ListenerScript();
            Exception exception = MakeException(0);
            for (int i = 0; i < 6; i++)
            {
                FailOpen(script.Add(), exception);
            }

            script.Add().StopOnWaitNumber(1);
            NewListener(script);
            for (int i = 1; i <= 4; i++)
            {
                ExpectLost(i, exception);
            }

            LogAssert.Expect(LogType.Error, LogLine($"{ListenerPrefix} ReconnectAlert: 5 consecutive failures - NpgsqlException"));
            ExpectLost(6, exception);

            script.Listener.RunLoop();

            CollectionAssert.AreEqual(new[] { 500, 1000, 2000, 4000, 8000, 10000 }, script.Delays);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_a_successful_listen_resets_the_failure_count_and_the_backoff()
        {
            var script = new ListenerScript();
            Exception exception = MakeException(3);
            FailOpen(script.Add(), exception);
            FailOpen(script.Add(), exception);
            FakeConnection healthy = script.Add();
            healthy.OnWait = ms => { throw exception; };
            script.Add().StopOnWaitNumber(1);
            NewListener(script);
            ExpectLost(1, exception);
            ExpectLost(2, exception);
            ExpectLost(1, exception);

            script.Listener.RunLoop();

            CollectionAssert.AreEqual(new[] { 500, 1000, 500 }, script.Delays);
            Assert.AreEqual(4, script.Created.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_a_fifth_failure_split_by_a_successful_listen_logs_no_error()
        {
            var script = new ListenerScript();
            Exception exception = MakeException(1);
            for (int i = 0; i < 3; i++)
            {
                FailOpen(script.Add(), exception);
            }

            FakeConnection healthy = script.Add();
            healthy.OnWait = ms => { throw exception; };
            for (int i = 0; i < 3; i++)
            {
                FailOpen(script.Add(), exception);
            }

            script.Add().StopOnWaitNumber(1);
            NewListener(script);
            foreach (int count in new[] { 1, 2, 3, 1, 2, 3, 4 })
            {
                ExpectLost(count, exception);
            }

            script.Listener.RunLoop();

            CollectionAssert.AreEqual(new[] { 500, 1000, 2000, 500, 1000, 2000, 4000 }, script.Delays);
            Assert.IsFalse(script.Exhausted);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_stop_called_from_the_listener_thread_returns_without_joining_itself_and_the_loop_ends()
        {
            var script = new ListenerScript();
            FakeConnection connection = script.Add();
            long innerStopMs = -1;
            connection.OnWait = ms =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                script.Listener.Stop();
                innerStopMs = watch.ElapsedMilliseconds;
                return false;
            };
            NpgsqlRelayListener listener = NewListener(script, stopTimeoutMs: JoinWaitMs);
            _cleanup.Add(listener.Stop);

            listener.Start();
            Thread thread = listener.WorkerThread;

            Assert.IsTrue(thread.Join(JoinWaitMs * 2), "The loop ended after the Stop from its own thread.");
            Assert.That(innerStopMs, Is.InRange(0L, JoinWaitMs / 5L), "Stop on the listener thread did not wait for its own join.");
            Assert.AreEqual(1, connection.WaitCount);
            Assert.AreEqual(1, connection.DisposeCount);
            Assert.AreEqual(thread.ManagedThreadId, connection.DisposeThreadId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_stop_during_wait_leaves_after_that_wait_with_no_backoff_and_a_disposed_connection()
        {
            var script = new ListenerScript();
            FakeConnection connection = script.Add();
            connection.StopOnWaitNumber(2);
            NewListener(script);

            script.Listener.RunLoop();

            CollectionAssert.AreEqual(new[] { "Open", "Listen:irongrind_relay", "Wait:500", "Wait:500", "Dispose" }, connection.Events);
            CollectionAssert.IsEmpty(script.Delays);
            Assert.AreEqual(1, script.Created.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_stop_during_the_backoff_delay_leaves_at_once_without_reconnecting()
        {
            var script = new ListenerScript();
            Exception exception = MakeException(1);
            FailOpen(script.Add(), exception);
            script.Add().StopOnWaitNumber(1);
            NewListener(script);
            script.DelayHook = ms => script.Listener.Stop();
            ExpectLost(1, exception);

            script.Listener.RunLoop();

            CollectionAssert.AreEqual(new[] { 500 }, script.Delays);
            Assert.AreEqual(1, script.Created.Count, "The loop left the delay and did not open another connection.");
            Assert.AreEqual(1, script.Created[0].DisposeCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_stop_requested_while_a_call_fails_leaves_with_one_debug_line_no_warning_and_no_delay()
        {
            var script = new ListenerScript();
            FakeConnection connection = script.Add();
            connection.OnOpen = () =>
            {
                script.Listener.Stop();
                throw new IOException("closed by the test");
            };
            NewListener(script);
            LogAssert.Expect(LogType.Log, LogLine($"{ListenerPrefix} FailureAfterStop: IOException: closed by the test"));

            script.Listener.RunLoop();

            CollectionAssert.IsEmpty(script.Delays);
            Assert.AreEqual(1, connection.DisposeCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_stop_before_the_loop_runs_makes_it_return_without_connecting()
        {
            var script = new ListenerScript();
            NewListener(script);

            script.Listener.Stop();
            script.Listener.RunLoop();

            Assert.AreEqual(0, script.Created.Count);
            Assert.IsFalse(script.Exhausted);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_the_loop_disposes_the_connection_and_stop_never_does()
        {
            var script = new ListenerScript();
            FakeConnection connection = script.Add();
            int disposeCountWhenStopReturned = -1;
            connection.OnWait = ms =>
            {
                script.Listener.Stop();
                disposeCountWhenStopReturned = connection.DisposeCount;
                return false;
            };
            NewListener(script);

            script.Listener.RunLoop();

            Assert.AreEqual(0, disposeCountWhenStopReturned, "Stop returned before the loop disposed anything.");
            Assert.AreEqual(1, connection.DisposeCount);
            Assert.AreEqual(Thread.CurrentThread.ManagedThreadId, connection.DisposeThreadId, "The loop thread disposed it.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_default_backoff_wait_returns_true_at_once_when_stop_is_signalled_and_false_on_timeout()
        {
            using (var signal = new ManualResetEventSlim(false))
            {
                Assert.IsFalse(NpgsqlRelayListener.WaitForStop(1, signal));
                signal.Set();
                Assert.IsTrue(NpgsqlRelayListener.WaitForStop(NpgsqlRelayListener.RELAY_RECONNECT_MAX_BACKOFF_MS, signal));
            }
        }

        // ---- listener lifecycle -----------------------------------------------------------------

        [Test]
        public void test_listener_stop_before_start_three_times_returns_at_once_and_a_later_start_throws()
        {
            var script = new ListenerScript();
            NpgsqlRelayListener listener = NewListener(script);

            Assert.DoesNotThrow(() => listener.Stop());
            Assert.DoesNotThrow(() => listener.Stop());
            Assert.DoesNotThrow(() => listener.Stop());

            Assert.IsFalse(listener.IsRunning);
            Assert.IsNull(listener.WorkerThread);
            Assert.Throws<InvalidOperationException>(() => listener.Start());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_stop_after_the_loop_has_ended_returns_at_once()
        {
            var script = new ListenerScript();
            script.Add().StopOnWaitNumber(1);
            NpgsqlRelayListener listener = NewListener(script);
            listener.RunLoop();

            Assert.DoesNotThrow(() => listener.Stop());
            Assert.DoesNotThrow(() => listener.Stop());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_real_thread_is_a_named_background_thread_start_twice_is_a_no_op_and_stop_ends_it_three_times_safely()
        {
            var script = new ListenerScript();
            var release = new ManualResetEventSlim(false);
            var entered = new ManualResetEventSlim(false);
            FakeConnection connection = script.Add();
            connection.OnWait = ms =>
            {
                entered.Set();
                release.Wait(20);
                return false;
            };
            NpgsqlRelayListener listener = NewListener(script, stopTimeoutMs: JoinWaitMs);
            _cleanup.Add(release.Set);
            _cleanup.Add(listener.Stop);

            listener.Start();
            Thread thread = listener.WorkerThread;
            listener.Start();

            Assert.IsNotNull(thread);
            Assert.AreSame(thread, listener.WorkerThread, "The second Start started no second thread.");
            Assert.IsTrue(thread.IsBackground);
            Assert.IsFalse(thread.IsThreadPoolThread);
            Assert.AreEqual("IronGrindRelayListener", thread.Name);
            Assert.IsTrue(entered.Wait(JoinWaitMs), "The listener thread reached Wait.");
            Assert.IsTrue(listener.IsRunning);

            listener.Stop();
            listener.Stop();
            listener.Stop();

            Assert.IsFalse(listener.IsRunning);
            Assert.AreEqual(1, connection.DisposeCount);
            Assert.AreEqual(thread.ManagedThreadId, connection.DisposeThreadId, "The listener thread disposed the connection, not the caller of Stop.");
            Assert.AreNotEqual(Thread.CurrentThread.ManagedThreadId, connection.DisposeThreadId);
            Assert.Throws<InvalidOperationException>(() => listener.Start());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_listener_stop_join_timeout_returns_after_the_timeout_logs_one_warning_and_a_repeat_logs_nothing()
        {
            var script = new ListenerScript();
            var release = new ManualResetEventSlim(false);
            var entered = new ManualResetEventSlim(false);
            FakeConnection connection = script.Add();
            connection.OnWait = ms =>
            {
                entered.Set();
                release.Wait();
                return false;
            };
            NpgsqlRelayListener listener = NewListener(script, stopTimeoutMs: 50);
            _cleanup.Add(release.Set);
            _cleanup.Add(() => listener.WorkerThread?.Join(JoinWaitMs));
            listener.Start();
            Assert.IsTrue(entered.Wait(JoinWaitMs), "The listener thread is blocked in Wait.");
            LogAssert.Expect(LogType.Warning, LogLine($"{ListenerPrefix} StopTimedOut: the listener thread did not end within 50 ms"));

            listener.Stop();
            listener.Stop();

            Assert.IsTrue(listener.IsRunning, "Stop gave up; the thread is still blocked.");
            Assert.AreEqual(0, connection.DisposeCount, "Stop did not touch the connection.");
            release.Set();
            Assert.IsTrue(listener.WorkerThread.Join(JoinWaitMs));
            Assert.AreEqual(1, connection.DisposeCount, "The thread disposed it on its way out.");
            LogAssert.NoUnexpectedReceived();
        }

        // ---- construction -----------------------------------------------------------------------

        [Test]
        public void test_listener_connection_string_has_pooling_off_multiplexing_off_tcp_keepalive_on_and_no_keepalive()
        {
            string built = NpgsqlRelayListener.BuildListenerConnectionString(
                BaseConnectionString + ";Pooling=true;Multiplexing=true;Keepalive=30;Tcp Keepalive=false");

            DbConnectionStringBuilder builder = ParseConnectionString(built);
            Assert.IsFalse(BoolKey(builder, "Pooling"));
            Assert.IsFalse(BoolKey(builder, "Multiplexing"));
            Assert.IsTrue(BoolKey(builder, "Tcp Keepalive"));
            Assert.IsFalse(builder.ContainsKey("Keepalive"), "No protocol-level Keepalive key.");
            Assert.AreEqual("127.0.0.1", builder["Host"]);
            Assert.AreEqual("d", builder["Database"]);
        }

        [Test]
        public void test_listener_connection_string_from_a_plain_base_string_gets_the_same_settings()
        {
            DbConnectionStringBuilder builder = ParseConnectionString(NpgsqlRelayListener.BuildListenerConnectionString(BaseConnectionString));

            Assert.IsFalse(BoolKey(builder, "Pooling"));
            Assert.IsFalse(BoolKey(builder, "Multiplexing"));
            Assert.IsTrue(BoolKey(builder, "Tcp Keepalive"));
            Assert.IsFalse(builder.ContainsKey("Keepalive"));
        }

        // ADR-012 Decision 1: the test assembly does not reference Npgsql.dll, so the string Npgsql's builder wrote is
        // read back with the generic parser (keys are case-insensitive). Each asserted key must be written explicitly.
        private static DbConnectionStringBuilder ParseConnectionString(string connectionString)
        {
            return new DbConnectionStringBuilder { ConnectionString = connectionString };
        }

        private static bool BoolKey(DbConnectionStringBuilder builder, string key)
        {
            Assert.IsTrue(builder.TryGetValue(key, out object value), "The key '" + key + "' is written explicitly.");
            return bool.Parse(Convert.ToString(value));
        }

        [Test]
        public void test_listener_constructor_argument_checks_name_the_argument()
        {
            var script = new ListenerScript();

            Assert.AreEqual("baseConnectionString", Assert.Throws<ArgumentNullException>(() => new NpgsqlRelayListener((string)null, script.Receiver)).ParamName);
            Assert.AreEqual("baseConnectionString", Assert.Throws<ArgumentException>(() => new NpgsqlRelayListener("  ", script.Receiver)).ParamName);
            Assert.AreEqual("receiver", Assert.Throws<ArgumentNullException>(() => new NpgsqlRelayListener(BaseConnectionString, null)).ParamName);
            Assert.AreEqual("connectionFactory", Assert.Throws<ArgumentNullException>(() => new NpgsqlRelayListener(null, script.Receiver, Channel, 500, 10000, 5, 2000, null)).ParamName);
            Assert.AreEqual("channelName", Assert.Throws<ArgumentNullException>(() => new NpgsqlRelayListener(script.Create, script.Receiver, null, 500, 10000, 5, 2000, null)).ParamName);
            Assert.AreEqual("channelName", Assert.Throws<ArgumentException>(() => new NpgsqlRelayListener(script.Create, script.Receiver, string.Empty, 500, 10000, 5, 2000, null)).ParamName);
            Assert.AreEqual("waitMs", Assert.Throws<ArgumentOutOfRangeException>(() => new NpgsqlRelayListener(script.Create, script.Receiver, Channel, 0, 10000, 5, 2000, null)).ParamName);
            Assert.AreEqual("reconnectMaxBackoffMs", Assert.Throws<ArgumentOutOfRangeException>(() => new NpgsqlRelayListener(script.Create, script.Receiver, Channel, 500, 0, 5, 2000, null)).ParamName);
            Assert.AreEqual("reconnectAlertAfter", Assert.Throws<ArgumentOutOfRangeException>(() => new NpgsqlRelayListener(script.Create, script.Receiver, Channel, 500, 10000, 0, 2000, null)).ParamName);
            Assert.AreEqual("stopTimeoutMs", Assert.Throws<ArgumentOutOfRangeException>(() => new NpgsqlRelayListener(script.Create, script.Receiver, Channel, 500, 10000, 5, 0, null)).ParamName);
        }

        [Test]
        public void test_tuning_knobs_are_public_consts_with_the_adr_015_values()
        {
            Assert.AreEqual("irongrind_relay", NpgsqlRelayPublisher.RELAY_CHANNEL_NAME);
            Assert.AreEqual(8, NpgsqlRelayPublisher.RELAY_MAX_PUBLISHES_IN_FLIGHT);
            Assert.AreEqual(2000, NpgsqlRelayPublisher.RELAY_PUBLISH_TIMEOUT_MS);
            Assert.AreEqual(500, NpgsqlRelayListener.RELAY_LISTENER_WAIT_MS);
            Assert.AreEqual(10000, NpgsqlRelayListener.RELAY_RECONNECT_MAX_BACKOFF_MS);
            Assert.AreEqual(5, NpgsqlRelayListener.RELAY_RECONNECT_ALERT_AFTER);
            Assert.AreEqual(2000, NpgsqlRelayListener.RELAY_LISTENER_STOP_TIMEOUT_MS);
        }

        // ---- source searches --------------------------------------------------------------------

        [Test]
        public void test_source_publisher_file_has_no_open_wait_result_await_or_lambda()
        {
            string source = RelaySource("NpgsqlRelayPublisher.cs");

            StringAssert.DoesNotContain("=>", source, "No lambda: the callbacks are method groups made once.");
            Assert.IsFalse(Regex.IsMatch(source, @"\bawait\b"), "await");
            Assert.IsFalse(Regex.IsMatch(source, @"\.Open\("), ".Open(");
            Assert.IsFalse(Regex.IsMatch(source, @"\.Wait\("), ".Wait(");
            Assert.IsFalse(Regex.IsMatch(source, @"\.Result\b"), ".Result");
            Assert.IsFalse(Regex.IsMatch(source, @"ExecuteNonQuery\("), "synchronous ExecuteNonQuery");
            Assert.IsFalse(Regex.IsMatch(source, @"NpgsqlDataSource\.Create\("), "the publisher never creates the data source");
            Assert.IsFalse(Regex.IsMatch(source, @"[dD]ataSource\.Dispose"), "the publisher never disposes the data source");
        }

        [Test]
        public void test_source_command_starter_never_opens_a_connection_or_runs_the_command_synchronously()
        {
            string source = RelaySource("NpgsqlRelayNotifyCommandStarter.cs");

            Assert.IsFalse(Regex.IsMatch(source, @"\bawait\b"));
            Assert.IsFalse(Regex.IsMatch(source, @"\.Open\("));
            Assert.IsFalse(Regex.IsMatch(source, @"\.Wait\("));
            Assert.IsFalse(Regex.IsMatch(source, @"\.Result\b"));
            Assert.IsFalse(Regex.IsMatch(source, @"ExecuteNonQuery\("));
            StringAssert.Contains("ExecuteNonQueryAsync(", source);
            StringAssert.Contains("CreateCommand(", source);
            Assert.IsFalse(Regex.IsMatch(source, @"NpgsqlDataSource\.Create\("));
            Assert.IsFalse(Regex.IsMatch(source, @"[dD]ataSource\.Dispose"), "the starter never disposes the data source");
        }

        [Test]
        public void test_source_listener_file_has_a_dedicated_background_thread_and_no_task_run_pool_async_or_lambda()
        {
            string source = RelaySource("NpgsqlRelayListener.cs");

            StringAssert.Contains("new Thread(", source);
            StringAssert.Contains("IsBackground = true", source);
            StringAssert.Contains("IronGrindRelayListener", source);
            StringAssert.DoesNotContain("Task.Run", source);
            StringAssert.DoesNotContain("ThreadPool", source);
            StringAssert.DoesNotContain("=>", source, "No lambda: the notification handler is a method.");
            Assert.IsFalse(Regex.IsMatch(source, @"\basync\b"), "async");
            Assert.IsFalse(Regex.IsMatch(source, @"\bawait\b"), "await");
        }

        [Test]
        public void test_source_each_knob_default_has_one_literal_and_the_channel_name_only_in_the_publisher()
        {
            string publisher = RelaySource("NpgsqlRelayPublisher.cs");
            string listener = RelaySource("NpgsqlRelayListener.cs");

            Assert.AreEqual(1, Regex.Matches(publisher, "\"irongrind_relay\"").Count);
            Assert.AreEqual(0, Regex.Matches(listener, "\"irongrind_relay\"").Count);
            Assert.AreEqual(1, Regex.Matches(publisher, @"\b2000\b").Count);
            Assert.AreEqual(1, Regex.Matches(listener, @"\b2000\b").Count);
            Assert.AreEqual(1, Regex.Matches(listener, @"\b10000\b").Count);
        }
    }
}
