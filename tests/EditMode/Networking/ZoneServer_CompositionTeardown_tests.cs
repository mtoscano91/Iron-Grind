using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    internal sealed class ZsApp : IZoneApplication
    {
        private readonly List<string> _log;
        public bool Editor;
        public bool QuitThrows;
        public readonly List<int> QuitCodes = new List<int>();

        public ZsApp(List<string> log) { _log = log; }

        public bool IsEditor => Editor;

        public void Quit(int code)
        {
            _log.Add("Quit:" + code);
            QuitCodes.Add(code);
            if (QuitThrows)
            {
                throw new InvalidOperationException("secret");
            }
        }
        public void SubscribeWantsToQuit(Func<bool> handler) { }
        public void UnsubscribeWantsToQuit(Func<bool> handler) { }
        public void KeepAcrossSceneLoads() { }
    }

    internal sealed class ZsNetwork : IZoneNetwork
    {
        private readonly List<string> _log;
        private int _inProgressReads;
        public Action UnsubscribedHandler;
        public bool Alive = true;

        /// <summary>Number of ShutdownInProgress reads that return true; -1 = forever.</summary>
        public int InProgressReadsTrue;

        public ZsNetwork(List<string> log) { _log = log; }

        public void SubscribeTick(Action handler) { }
        public void UnsubscribeTick(Action handler) { _log.Add("TickUnsubscribe"); UnsubscribedHandler = handler; }
        public void Shutdown() { _log.Add("NgoShutdown"); }
        public bool IsAlive => Alive;
        public void PrepareObject() { }
        public void Configure(uint tickRate, int targetFrameRate, int vSyncCount) { }
        public bool StartServer() { return true; }

        public bool ShutdownInProgress
        {
            get
            {
                if (InProgressReadsTrue < 0)
                {
                    return true;
                }

                return _inProgressReads++ < InProgressReadsTrue;
            }
        }
    }

    internal sealed class ZsNav : IZoneNavigation
    {
        private readonly List<string> _log;
        public bool Throw;
        public ZsNav(List<string> log) { _log = log; }
        public void Initialize(ZoneID zone, NavMeshDataInstance instance) { }
        public void Teardown() { _log.Add("NavTeardown"); if (Throw) { throw new InvalidOperationException("secret"); } }
    }

    internal sealed class ZsClose : IZoneCloseSteps
    {
        private readonly List<string> _log;
        public bool Throw;
        public ZsClose(List<string> log) { _log = log; }
        public void RunCloseSteps() { _log.Add("CloseSteps"); if (Throw) { throw new InvalidOperationException("secret"); } }
    }

    internal sealed class ZsOutbound : IZoneOutboundStep
    {
        private readonly List<string> _log;
        public uint LastTick;
        public int Calls;
        public bool Throw;
        public ZsOutbound(List<string> log) { _log = log; }

        public void Flush(uint currentTick)
        {
            _log.Add("OutboundFlush");
            LastTick = currentTick;
            Calls++;
            if (Throw)
            {
                throw new InvalidOperationException("secret");
            }
        }
    }

    internal sealed class ZsQueue : ITickCompletionQueue
    {
        private readonly List<string> _log;
        public TimeSpan LastTimeout;
        public int Calls;
        public bool Throw;
        public ZsQueue(List<string> log) { _log = log; }

        public void Track<T>(Task<T> task, CancellationTokenSource cancellation, Action<TickTaskResult<T>> onCompletedOnTick) { }
        public void Drain(uint currentTick) { }
        public int InFlightCount => 0;

        public void DrainOnShutdown(TimeSpan timeout)
        {
            _log.Add("Drain");
            LastTimeout = timeout;
            Calls++;
            if (Throw)
            {
                throw new InvalidOperationException("secret-connection-detail");
            }
        }
    }

    internal sealed class ZsListener : IZoneRelayListener
    {
        private readonly List<string> _log;
        public bool Throw;
        public ZsListener(List<string> log) { _log = log; }

        public void Stop()
        {
            _log.Add("ListenerStop");
            if (Throw)
            {
                throw new InvalidOperationException("secret-connection-detail");
            }
        }
    }

    internal sealed class ZsTransport : IZoneInboundTransport
    {
        private readonly List<string> _log;
        public bool Throw;
        public ZsTransport(List<string> log) { _log = log; }
        public void Start() { _log.Add("ShellStart"); }
        public void Stop() { _log.Add("ShellStop"); if (Throw) { throw new InvalidOperationException("secret"); } }
    }

    internal sealed class ZsDisposable : IDisposable
    {
        private readonly List<string> _log;
        private readonly string _name;
        private readonly bool _throw;

        public ZsDisposable(List<string> log, string name, bool doThrow = false)
        {
            _log = log;
            _name = name;
            _throw = doThrow;
        }

        public void Dispose()
        {
            _log.Add("Dispose:" + _name);
            if (_throw)
            {
                throw new InvalidOperationException("secret");
            }
        }
    }

    /// <summary>Everything a composition test needs, all fakes appending to one shared call log.</summary>
    internal sealed class ZsRig
    {
        public readonly List<string> Log = new List<string>();
        public readonly ZsApp App;
        public readonly ZsNetwork Net;
        public readonly ZsNav Nav;
        public readonly ZsClose Close;
        public readonly ZsOutbound Outbound;
        public readonly ZsQueue Queue;
        public readonly ZsListener Listener;
        public readonly ZsTransport Transport;
        public readonly ZoneServerComposition Comp;
        public readonly Action TickHandler = () => { };
        public const uint LAST_TICK = 4242u;

        public ZsRig(int waitFrames = 100)
        {
            App = new ZsApp(Log);
            Net = new ZsNetwork(Log);
            Nav = new ZsNav(Log);
            Close = new ZsClose(Log);
            Outbound = new ZsOutbound(Log);
            Queue = new ZsQueue(Log);
            Listener = new ZsListener(Log);
            Transport = new ZsTransport(Log);
            Comp = new ZoneServerComposition(App, Net, Nav, Close, waitFrames);
            Comp.AttachOwnedObjects(Queue, Outbound, Listener, Transport, () => LAST_TICK, TickHandler);
            Comp.MarkNavigationInitialized();
            Comp.MarkZoneStateEntered();
            Comp.MarkTickStarted();
        }

        public static string[] Steps1To7()
        {
            return new[]
            {
                "TickUnsubscribe", "ListenerStop", "NavTeardown", "CloseSteps", "OutboundFlush", "Drain",
                "ShellStop", "NgoShutdown"
            };
        }
    }

    /// <summary>Networking Core Story 042: zone server composition lifecycle, teardown 1-8, quit interception.</summary>
    [TestFixture]
    public sealed class ZoneServerCompositionTeardownTests
    {
        private const string SOURCE_FOLDER = "src/ServerLogic/ZoneServer";

        /// <summary>Drives a coroutine with a MoveNext loop; returns the number of yields.</summary>
        private static int Drive(IEnumerator e)
        {
            int yields = 0;
            while (e.MoveNext())
            {
                yields++;
                Assert.Less(yields, 100000, "Runaway coroutine.");
            }

            return yields;
        }

        private static string[] Concat(string[] a, params string[] b)
        {
            var list = new List<string>(a);
            list.AddRange(b);
            return list.ToArray();
        }

        // ---------------------------------------------------------------- AC-1

        [Test]
        public void test_exit_code_has_exactly_three_members_with_fixed_values()
        {
            Assert.AreEqual(0, (int)ZoneExitCode.Clean);
            Assert.AreEqual(1, (int)ZoneExitCode.StartupFailed);
            Assert.AreEqual(2, (int)ZoneExitCode.Fatal);
            Assert.AreEqual(3, Enum.GetValues(typeof(ZoneExitCode)).Length);
        }

        // ---------------------------------------------------------------- AC-2

        [Test]
        public void test_request_teardown_first_code_wins_and_quit_uses_it()
        {
            var rig = new ZsRig();

            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);
            Assert.AreEqual(0, rig.Log.Count, "RequestTeardown must tear nothing down.");
            Drive(rig.Comp.RunTeardown());

            CollectionAssert.AreEqual(new[] { 2 }, rig.App.QuitCodes);
        }

        [Test]
        public void test_request_teardown_after_finish_has_no_effect_and_no_second_quit()
        {
            var rig = new ZsRig();
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);
            Drive(rig.Comp.RunTeardown());

            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);
            Drive(rig.Comp.RunTeardown());

            CollectionAssert.AreEqual(new[] { 0 }, rig.App.QuitCodes);
        }

        [Test]
        public void test_no_request_means_host_never_begins_teardown()
        {
            var rig = new ZsRig();

            Assert.IsFalse(rig.Comp.TryBeginTeardown());
            Assert.AreEqual(0, rig.Log.Count);
        }

        // ---------------------------------------------------------------- AC-3

        [Test]
        public void test_run_teardown_runs_steps_in_exact_order_with_dispose_before_quit()
        {
            var rig = new ZsRig();
            rig.Comp.RegisterDisposable(new ZsDisposable(rig.Log, "A"));
            rig.Comp.RegisterDisposable(new ZsDisposable(rig.Log, "B"));
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);

            Drive(rig.Comp.RunTeardown());

            CollectionAssert.AreEqual(
                Concat(ZsRig.Steps1To7(), "Dispose:B", "Dispose:A", "Quit:0"),
                rig.Log);
        }

        [Test]
        public void test_run_teardown_flush_gets_last_tick_and_unsubscribes_the_tick_handler()
        {
            var rig = new ZsRig();

            Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(ZsRig.LAST_TICK, rig.Outbound.LastTick);
            Assert.AreEqual(1, rig.Outbound.Calls);
            Assert.AreSame(rig.TickHandler, rig.Net.UnsubscribedHandler);
        }

        [Test]
        public void test_run_teardown_drain_gets_the_computed_watchdog_duration()
        {
            var rig = new ZsRig();

            Drive(rig.Comp.RunTeardown());

            TimeSpan expected = TimeSpan.FromSeconds(
                (double)TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS / ServerTickLoop.TICK_RATE_HZ);
            Assert.AreEqual(expected, rig.Queue.LastTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(10), rig.Queue.LastTimeout);
        }

        [Test]
        public void test_constructor_rejects_non_positive_wait_frames()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneServerComposition(null, null, null, null, 0));
        }

        // ---------------------------------------------------------------- AC-4

        [Test]
        public void test_ngo_wait_yields_once_per_frame_while_in_progress_without_warning()
        {
            var rig = new ZsRig();
            rig.Net.InProgressReadsTrue = 3;

            int yields = Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(3, yields);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ngo_wait_stops_at_the_cap_with_one_warning_and_step_7_still_runs()
        {
            var rig = new ZsRig(100);
            rig.Net.InProgressReadsTrue = -1;
            rig.Comp.RegisterDisposable(new ZsDisposable(rig.Log, "A"));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[ZoneServer\] .*100 frames"));

            int yields = Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(100, yields);
            CollectionAssert.Contains(rig.Log, "Dispose:A");
            Assert.AreEqual("Quit:0", rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_ngo_wait_with_dead_manager_yields_nothing()
        {
            var rig = new ZsRig();
            rig.Net.Alive = false;
            rig.Net.InProgressReadsTrue = -1;

            int yields = Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(0, yields);
            CollectionAssert.DoesNotContain(rig.Log, "NgoShutdown");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ngo_wait_ending_exactly_at_the_cap_without_shutdown_in_progress_has_no_warning()
        {
            var rig = new ZsRig(3);
            rig.Net.InProgressReadsTrue = 3;

            int yields = Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(3, yields);
            Assert.AreEqual("Quit:0", rig.Log[rig.Log.Count - 1]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ngo_wait_with_cap_of_one_yields_one_frame()
        {
            var rig = new ZsRig(1);
            rig.Net.InProgressReadsTrue = -1;
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[ZoneServer\] .*1 frames"));

            int yields = Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(1, yields);
        }

        // ---------------------------------------------------------------- AC-5

        [Test]
        public void test_dispose_pass_reverses_order_and_survives_a_throwing_dispose()
        {
            var rig = new ZsRig();
            rig.Comp.RegisterDisposable(new ZsDisposable(rig.Log, "A"));
            rig.Comp.RegisterDisposable(new ZsDisposable(rig.Log, "B", true));
            rig.Comp.RegisterDisposable(new ZsDisposable(rig.Log, "C"));
            LogAssert.Expect(LogType.Error, "[ZoneServer] teardown step 7 Dispose failed: InvalidOperationException");

            Drive(rig.Comp.RunTeardown());

            var disposes = rig.Log.FindAll(s => s.StartsWith("Dispose:", StringComparison.Ordinal));
            CollectionAssert.AreEqual(new[] { "Dispose:C", "Dispose:B", "Dispose:A" }, disposes);
        }

        // ---------------------------------------------------------------- AC-6

        [Test]
        public void test_nothing_built_teardown_only_quits_without_throwing()
        {
            var log = new List<string>();
            var app = new ZsApp(log);
            var comp = new ZoneServerComposition(app, null, null, null, 100);

            Assert.DoesNotThrow(() => Drive(comp.RunTeardown()));

            CollectionAssert.AreEqual(new[] { "Quit:0" }, log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_missing_listener_is_skipped_and_later_steps_run()
        {
            var rig = new ZsRig();
            rig.Comp.AttachOwnedObjects(rig.Queue, rig.Outbound, null, rig.Transport, () => 1u, rig.TickHandler);

            Drive(rig.Comp.RunTeardown());

            var expected = new List<string>(ZsRig.Steps1To7());
            expected.Remove("ListenerStop");
            expected.Add("Quit:0");
            CollectionAssert.AreEqual(expected, rig.Log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_dead_ngo_manager_skips_shutdown_and_tick_unsubscribe()
        {
            var rig = new ZsRig();
            rig.Net.Alive = false;

            Drive(rig.Comp.RunTeardown());

            CollectionAssert.AreEqual(
                new[] { "ListenerStop", "NavTeardown", "CloseSteps", "OutboundFlush", "Drain", "ShellStop", "Quit:0" },
                rig.Log);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-7

        [Test]
        public void test_step_exception_is_logged_by_type_and_teardown_continues_with_code_unchanged()
        {
            var rig = new ZsRig();
            rig.Listener.Throw = true;
            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);
            LogAssert.Expect(LogType.Error, "[ZoneServer] teardown step 2a failed: InvalidOperationException");

            Drive(rig.Comp.RunTeardown());

            CollectionAssert.AreEqual(
                new[] { "TickUnsubscribe", "ListenerStop", "NavTeardown", "CloseSteps", "OutboundFlush", "Drain", "ShellStop", "NgoShutdown", "Quit:2" },
                rig.Log);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase("3")]
        [TestCase("4")]
        [TestCase("4b")]
        [TestCase("5")]
        public void test_each_step_throwing_is_logged_by_its_name_and_teardown_goes_on_to_quit(string step)
        {
            var rig = new ZsRig();
            rig.Nav.Throw = step == "3";
            rig.Close.Throw = step == "4";
            rig.Outbound.Throw = step == "4b";
            rig.Queue.Throw = step == "5";
            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);
            LogAssert.Expect(LogType.Error, "[ZoneServer] teardown step " + step + " failed: InvalidOperationException");

            Drive(rig.Comp.RunTeardown());

            CollectionAssert.AreEqual(Concat(ZsRig.Steps1To7(), "Quit:2"), rig.Log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_transport_stop_throwing_still_calls_ngo_shutdown()
        {
            var rig = new ZsRig();
            rig.Transport.Throw = true;
            LogAssert.Expect(LogType.Error, "[ZoneServer] teardown step 6 failed: InvalidOperationException");

            Drive(rig.Comp.RunTeardown());

            CollectionAssert.AreEqual(Concat(ZsRig.Steps1To7(), "Quit:0"), rig.Log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_quit_throwing_is_logged_as_step_8_and_does_not_propagate()
        {
            var rig = new ZsRig();
            rig.App.QuitThrows = true;
            LogAssert.Expect(LogType.Error, "[ZoneServer] teardown step 8 failed: InvalidOperationException");

            Assert.DoesNotThrow(() => Drive(rig.Comp.RunTeardown()));

            Assert.IsTrue(rig.Comp.OnWantsToQuit(), "The quit flag is set before Quit is called.");
            LogAssert.NoUnexpectedReceived();
        }

        // Story 043 AC-7: before MarkTickStarted() (startup S9) the teardown has no flush and no drain.
        [Test]
        public void test_teardown_without_mark_tick_started_has_no_flush_and_no_drain()
        {
            var log = new List<string>();
            var comp = new ZoneServerComposition(new ZsApp(log), new ZsNetwork(log), new ZsNav(log), new ZsClose(log), 100);
            comp.AttachOwnedObjects(
                new ZsQueue(log), new ZsOutbound(log), new ZsListener(log), new ZsTransport(log), () => 1u, () => { });

            Drive(comp.RunTeardown());

            CollectionAssert.DoesNotContain(log, "OutboundFlush");
            CollectionAssert.DoesNotContain(log, "Drain");
            CollectionAssert.Contains(log, "ListenerStop");
            CollectionAssert.Contains(log, "ShellStop");
            Assert.AreEqual("Quit:0", log[log.Count - 1]);
        }

        [Test]
        public void test_teardown_without_navigation_and_zone_state_marks_skips_steps_3_and_4()
        {
            var log = new List<string>();
            var comp = new ZoneServerComposition(new ZsApp(log), new ZsNetwork(log), new ZsNav(log), new ZsClose(log), 100);

            Drive(comp.RunTeardown());

            CollectionAssert.DoesNotContain(log, "NavTeardown");
            CollectionAssert.DoesNotContain(log, "CloseSteps");
            CollectionAssert.AreEqual(new[] { "NgoShutdown", "Quit:0" }, log);
        }

        // ---------------------------------------------------------------- AC-8

        // Deviation note: the drain timeout is computed from constants (10 s) and is not injectable, so the
        // "tracked task that never completes" case with a real queue would wait 10 s. The queue's own alert is
        // covered by TickLoop_CompletionQueue_tests (DrainOnShutdown_OneTaskIncomplete_LogsItNoCallbackAndHandles-
        // TheFinishedOne, DrainOnShutdown_TimedOutTaskStillIncomplete_AlertsOnce). Here a fake checks the call and
        // a real, empty queue checks the wiring and the tick-thread assertion.
        [Test]
        public void test_real_tick_completion_queue_with_nothing_in_flight_drains_at_once_through_teardown()
        {
            var rig = new ZsRig();
            var queue = new TickCompletionQueue();
            rig.Comp.AttachOwnedObjects(queue, rig.Outbound, rig.Listener, rig.Transport, () => ZsRig.LAST_TICK, rig.TickHandler);

            Drive(rig.Comp.RunTeardown());

            CollectionAssert.AreEqual(new[] { 0 }, rig.App.QuitCodes);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_drain_is_called_once_and_leaves_the_fatal_code_alone()
        {
            var rig = new ZsRig();
            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);

            Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(1, rig.Queue.Calls);
            CollectionAssert.AreEqual(new[] { 2 }, rig.App.QuitCodes);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_drain_throwing_is_logged_as_step_5_and_teardown_goes_on_to_quit_clean()
        {
            var rig = new ZsRig();
            rig.Queue.Throw = true;
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);
            LogAssert.Expect(LogType.Error, "[ZoneServer] teardown step 5 failed: InvalidOperationException");

            Drive(rig.Comp.RunTeardown());

            int drain = rig.Log.IndexOf("Drain");
            Assert.Less(drain, rig.Log.IndexOf("ShellStop"));
            Assert.Less(drain, rig.Log.IndexOf("NgoShutdown"));
            CollectionAssert.AreEqual(new[] { 0 }, rig.App.QuitCodes);
        }

        // ---------------------------------------------------------------- AC-9

        [Test]
        public void test_try_begin_teardown_is_true_exactly_once_after_request()
        {
            var rig = new ZsRig();
            Assert.IsFalse(rig.Comp.TryBeginTeardown());

            rig.Comp.RequestTeardown(ZoneExitCode.Clean);

            Assert.IsTrue(rig.Comp.TryBeginTeardown());
            Assert.IsFalse(rig.Comp.TryBeginTeardown());
            Assert.IsFalse(rig.Comp.TryBeginTeardown());
        }

        [Test]
        public void test_second_run_teardown_yields_nothing_and_logs_nothing()
        {
            var rig = new ZsRig();
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);
            Drive(rig.Comp.RunTeardown());
            rig.Log.Clear();

            int yields = Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(0, yields);
            Assert.AreEqual(0, rig.Log.Count);
        }

        [Test]
        public void test_second_run_teardown_while_the_first_is_suspended_yields_nothing()
        {
            var rig = new ZsRig(2);
            rig.Net.InProgressReadsTrue = -1;
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[ZoneServer\] .*2 frames"));
            IEnumerator first = rig.Comp.RunTeardown();
            Assert.IsTrue(first.MoveNext());
            int logged = rig.Log.Count;

            Assert.AreEqual(0, Drive(rig.Comp.RunTeardown()));
            Assert.AreEqual(logged, rig.Log.Count);

            Drive(first);
            CollectionAssert.AreEqual(new[] { 0 }, rig.App.QuitCodes);
        }

        [Test]
        public void test_run_teardown_after_partial_steps_runs_the_remaining_steps_and_quits()
        {
            var rig = new ZsRig();
            rig.Comp.RunStepsForTest(5); // through 4b
            rig.Log.Clear();

            Drive(rig.Comp.RunTeardown());

            CollectionAssert.AreEqual(new[] { "Drain", "ShellStop", "NgoShutdown", "Quit:0" }, rig.Log);
        }

        [Test]
        public void test_request_teardown_after_teardown_now_then_run_teardown_does_nothing()
        {
            var rig = new ZsRig();
            rig.Comp.TeardownNow();
            rig.Log.Clear();

            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);

            Assert.IsTrue(rig.Comp.TryBeginTeardown());
            Assert.AreEqual(0, Drive(rig.Comp.RunTeardown()));
            Assert.AreEqual(0, rig.Log.Count);
            Assert.AreEqual(0, rig.App.QuitCodes.Count);
        }

        // ---------------------------------------------------------------- AC-10

        [Test]
        public void test_wants_to_quit_in_editor_is_true_and_requests_nothing()
        {
            var rig = new ZsRig();
            rig.App.Editor = true;

            Assert.IsTrue(rig.Comp.OnWantsToQuit());
            Assert.IsFalse(rig.Comp.TryBeginTeardown());
        }

        [Test]
        public void test_wants_to_quit_in_player_before_step_8_is_false_and_records_clean()
        {
            var rig = new ZsRig();

            Assert.IsFalse(rig.Comp.OnWantsToQuit());
            Assert.IsTrue(rig.Comp.TryBeginTeardown());
            Assert.AreEqual(0, rig.Log.Count, "No teardown work inside OnWantsToQuit.");
            Drive(rig.Comp.RunTeardown());
            CollectionAssert.AreEqual(new[] { 0 }, rig.App.QuitCodes);
        }

        [Test]
        public void test_wants_to_quit_after_step_8_is_true()
        {
            var rig = new ZsRig();
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);
            Drive(rig.Comp.RunTeardown());

            Assert.IsTrue(rig.Comp.OnWantsToQuit());
        }

        [Test]
        public void test_wants_to_quit_repeated_keeps_the_first_recorded_code()
        {
            var rig = new ZsRig();
            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);

            Assert.IsFalse(rig.Comp.OnWantsToQuit());
            Assert.IsFalse(rig.Comp.OnWantsToQuit());
            Drive(rig.Comp.RunTeardown());

            CollectionAssert.AreEqual(new[] { 2 }, rig.App.QuitCodes);
        }

        // ---------------------------------------------------------------- AC-11

        [Test]
        public void test_teardown_now_runs_steps_1_to_7_without_waiting_or_quit()
        {
            var rig = new ZsRig();
            rig.Net.InProgressReadsTrue = -1;

            rig.Comp.TeardownNow();

            CollectionAssert.AreEqual(ZsRig.Steps1To7(), rig.Log);
            Assert.AreEqual(0, rig.App.QuitCodes.Count);
        }

        [Test]
        public void test_teardown_now_after_partial_teardown_runs_only_the_remaining_steps()
        {
            var rig = new ZsRig();
            rig.Comp.RunStepsForTest(5); // through 4b
            rig.Log.Clear();

            rig.Comp.TeardownNow();

            CollectionAssert.AreEqual(new[] { "Drain", "ShellStop", "NgoShutdown" }, rig.Log);
        }

        [Test]
        public void test_teardown_now_after_finished_teardown_does_nothing()
        {
            var rig = new ZsRig();
            Drive(rig.Comp.RunTeardown());
            rig.Log.Clear();

            rig.Comp.TeardownNow();

            Assert.AreEqual(0, rig.Log.Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void test_teardown_now_while_suspended_in_ngo_wait_runs_dispose_pass_only_and_never_quits(bool viaDispose)
        {
            var rig = new ZsRig();
            rig.Net.InProgressReadsTrue = -1;
            rig.Comp.RegisterDisposable(new ZsDisposable(rig.Log, "A"));
            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);
            IEnumerator run = rig.Comp.RunTeardown();
            Assert.IsTrue(run.MoveNext(), "The coroutine must be suspended in the step-6 wait.");
            rig.Log.Clear();

            if (viaDispose)
            {
                rig.Comp.Dispose();
            }
            else
            {
                rig.Comp.TeardownNow();
            }

            CollectionAssert.AreEqual(new[] { "Dispose:A" }, rig.Log);
            Assert.IsFalse(run.MoveNext(), "The suspended coroutine ends without running step 8.");
            Assert.AreEqual(0, rig.App.QuitCodes.Count);
            Assert.IsFalse(rig.Comp.OnWantsToQuit(), "Current contract: quit stays blocked until step 8.");
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-12

        [Test]
        public void test_stand_in_navigation_logs_once_across_methods()
        {
            var nav = new StandInZoneNavigation();
            LogAssert.Expect(LogType.Log, "[ZoneServer] navigation not implemented");

            nav.Initialize(new ZoneID(1u), default(NavMeshDataInstance));
            nav.Teardown();
            nav.Teardown();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_stand_in_bounds_validator_logs_once_and_returns_true()
        {
            var validator = new StandInZoneBoundsValidator();
            LogAssert.Expect(LogType.Log, "[ZoneServer] bounds validation not implemented");

            Assert.IsTrue(validator.Validate(new ZoneID(1u)));
            Assert.IsTrue(validator.Validate(new ZoneID(1u)));
            Assert.IsTrue(validator.Validate(new ZoneID(1u)));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_stand_in_gateway_registration_logs_once()
        {
            var gateway = new StandInGatewayRegistration();
            LogAssert.Expect(LogType.Log, "[ZoneServer] gateway registration not implemented");

            gateway.RegisterZone(new ZoneID(1u), 7777);
            gateway.RegisterZone(new ZoneID(1u), 7777);
            gateway.RegisterZone(new ZoneID(1u), 7777);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_stand_in_close_steps_logs_once()
        {
            var close = new StandInZoneCloseSteps();
            LogAssert.Expect(LogType.Log, "[ZoneServer] zone close steps not implemented");

            close.RunCloseSteps();
            close.RunCloseSteps();
            close.RunCloseSteps();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_stand_in_static_data_loader_logs_once_and_returns_true()
        {
            var loader = new StandInZoneStaticDataLoader();
            LogAssert.Expect(LogType.Log, "[ZoneServer] static data loading not implemented");

            Assert.IsTrue(loader.LoadAndValidate());
            Assert.IsTrue(loader.LoadAndValidate());
            Assert.IsTrue(loader.LoadAndValidate());

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_stand_ins_log_once_per_instance_not_once_per_type()
        {
            LogAssert.Expect(LogType.Log, "[ZoneServer] zone close steps not implemented");
            LogAssert.Expect(LogType.Log, "[ZoneServer] zone close steps not implemented");

            new StandInZoneCloseSteps().RunCloseSteps();
            new StandInZoneCloseSteps().RunCloseSteps();

            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-13

        [Test]
        public void test_source_has_no_process_exit_environment_exit_or_on_application_quit()
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, SOURCE_FOLDER);
            string[] files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
            Assert.Greater(files.Length, 0, "No source files found; the search would pass vacuously.");

            foreach (string path in files)
            {
                string text = File.ReadAllText(path);
                text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                text = Regex.Replace(text, @"//[^\r\n]*", string.Empty);

                StringAssert.DoesNotContain("Process.Exit", text, path);
                StringAssert.DoesNotContain("Environment.Exit", text, path);
                StringAssert.DoesNotContain("OnApplicationQuit", text, path);
            }
        }

        // ---------------------------------------------------------------- AC-14

        [Test]
        public void test_dispose_twice_runs_steps_1_to_7_once_without_quit()
        {
            var rig = new ZsRig();
            rig.Comp.RegisterDisposable(new ZsDisposable(rig.Log, "A"));

            rig.Comp.Dispose();
            rig.Comp.Dispose();

            CollectionAssert.AreEqual(Concat(ZsRig.Steps1To7(), "Dispose:A"), rig.Log);
            Assert.AreEqual(0, rig.App.QuitCodes.Count);
        }

        [Test]
        public void test_dispose_after_teardown_now_adds_nothing()
        {
            var rig = new ZsRig();
            rig.Comp.TeardownNow();
            rig.Log.Clear();

            rig.Comp.Dispose();

            Assert.AreEqual(0, rig.Log.Count);
        }

        [Test]
        public void test_dispose_after_finished_run_teardown_adds_nothing()
        {
            var rig = new ZsRig();
            Drive(rig.Comp.RunTeardown());
            rig.Log.Clear();

            rig.Comp.Dispose();

            Assert.AreEqual(0, rig.Log.Count);
        }

        [Test]
        public void test_dispose_after_partial_teardown_runs_only_steps_5_6_7()
        {
            var rig = new ZsRig();
            rig.Comp.RunStepsForTest(5);
            rig.Log.Clear();

            rig.Comp.Dispose();

            CollectionAssert.AreEqual(new[] { "Drain", "ShellStop", "NgoShutdown" }, rig.Log);
        }
    }
}
