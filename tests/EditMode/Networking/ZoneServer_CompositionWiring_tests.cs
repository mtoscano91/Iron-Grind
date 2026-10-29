using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace IronGrind.Tests.EditMode.Networking
{
    internal sealed class WiEnvironment : IZoneEnvironment
    {
        public readonly Dictionary<string, string> Variables = new Dictionary<string, string>();

        public string GetVariable(string name)
        {
            string value;
            return Variables.TryGetValue(name, out value) ? value : null;
        }
    }

    internal sealed class WiSendSeam : IClientMessageSendSeam
    {
        public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel) { }
    }

    internal sealed class WiTransport : IZoneInboundTransport
    {
        private readonly List<string> _log;
        public WiTransport(List<string> log) { _log = log; }
        public void Start() { _log.Add("S8:ShellStart"); }
        public void Stop() { _log.Add("ShellStop"); }
    }

    internal sealed class WiNetwork : IZoneNetwork
    {
        private readonly List<string> _log;
        public Action SubscribedHandler;
        public bool ThrowOnSendSeam;
        public WiTransport Transport;
        public IInboundMessageIntake Intake;
        public IInboundRequestDispatcher Dispatcher;
        public IClientConnectionListener ClientListener;

        public WiNetwork(List<string> log) { _log = log; }

        public void PrepareObject() { }
        public void Configure(uint tickRate, int targetFrameRate, int vSyncCount, ushort port) { }
        public bool StartServer() { return true; }
        public void SubscribeTick(Action handler) { SubscribedHandler = handler; }
        public void UnsubscribeTick(Action handler) { _log.Add("TickUnsubscribe"); }
        public void Shutdown() { _log.Add("NgoShutdown"); }
        public bool IsAlive => true;
        public bool ShutdownInProgress => false;

        public IClientMessageSendSeam CreateSendSeam()
        {
            _log.Add("CreateSendSeam");
            if (ThrowOnSendSeam)
            {
                throw new InvalidOperationException("Sup3r;Secret!");
            }

            return new WiSendSeam();
        }

        public IZoneInboundTransport CreateInboundTransport(
            IInboundMessageIntake intake, IInboundRequestDispatcher dispatcher, IClientConnectionListener clientListener)
        {
            Intake = intake;
            Dispatcher = dispatcher;
            ClientListener = clientListener;
            Transport = new WiTransport(_log);
            return Transport;
        }

        public void FireTick() { SubscribedHandler(); }
    }

    internal sealed class WiListener : IStartableZoneRelayListener
    {
        private readonly List<string> _log;
        public bool ThrowOnStart;
        public int StartCalls;
        public int StopCalls;
        public string ConnectionString;

        public WiListener(List<string> log) { _log = log; }

        public void Start()
        {
            StartCalls++;
            _log.Add("ListenerStart");
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("Sup3r;Secret!");
            }
        }

        public void Stop()
        {
            StopCalls++;
            _log.Add("ListenerStop");
        }
    }

    internal sealed class WiSimStep : IZoneSimulationStep
    {
        private readonly List<string> _log;
        private readonly string _name;
        public WiSimStep(List<string> log, string name) { _log = log; _name = name; }
        public void Run(uint currentTick, float deltaTime) { _log.Add(_name + ":" + currentTick); }
    }

    /// <summary>Collects every log line (all types) while alive.</summary>
    internal sealed class WiLogCapture : IDisposable
    {
        public readonly List<string> Lines = new List<string>();

        public WiLogCapture() { Application.logMessageReceived += OnLog; }

        public void Dispose() { Application.logMessageReceived -= OnLog; }

        private void OnLog(string condition, string stackTrace, LogType type) { Lines.Add(condition); }
    }

    /// <summary>The composition with the REAL backend over fakes for everything outside the process.</summary>
    internal sealed class WiRig
    {
        public const string PASSWORD = "Sup3r;Secret!";

        public const string DEFAULT_CONFIG =
            "{\"Host\":\"db.invalid\",\"Port\":5432,\"Database\":\"d\",\"Username\":\"u\","
            + "\"DbWarmupTimeoutMs\":2000,\"NgoShutdownWaitFrames\":2}";

        public readonly List<string> Log = new List<string>();
        public readonly List<string> Observed = new List<string>();
        public readonly SuPlan Plan = new SuPlan();
        public readonly SuApp App;
        public readonly WiNetwork Net;
        public readonly SuConfigSource Config;
        public readonly WiEnvironment Env = new WiEnvironment();
        public readonly ZoneServerComposition Comp;
        public WiListener Listener;
        public bool ListenerThrowsOnStart;
        public string ListenerConnectionString;
        public double Now;

        public WiRig(string configJson = DEFAULT_CONFIG, bool realWarmUpAndListener = false)
        {
            App = new SuApp(Log, Plan);
            Net = new WiNetwork(Log);
            Config = new SuConfigSource(Log, Plan) { Json = configJson };
            Env.Variables["IRONGRIND_PG_PASSWORD"] = PASSWORD;
            Comp = ZoneServerComposition.CreateWithRealBackend(
                App, Net, new SuNav(Log, Plan), new ZsClose(Log), new SuScene(Log, Plan), new SuBounds(Log, Plan),
                new SuStatic(Log, Plan), new SuStateOwner(Log), new SuGateway(Log, Plan), Config, Env, () => Now);
            Comp.ConstructionObserver = Observed.Add;
            if (realWarmUpAndListener)
            {
                return;
            }

            Comp.WarmUpOverride = ct => Task.CompletedTask;
            Comp.ListenerFactoryOverride = (connectionString, receiver) =>
            {
                ListenerConnectionString = connectionString;
                Listener = new WiListener(Log) { ThrowOnStart = ListenerThrowsOnStart };
                return Listener;
            };
        }

        /// <summary>Runs Start to the end, then the teardown (requested clean when startup succeeded).</summary>
        public void RunAll()
        {
            SuRig.Drive(Comp.Start(SuRig.ARGS));
            Comp.RequestTeardown(ZoneExitCode.Clean);
            if (Comp.TryBeginTeardown())
            {
                SuRig.Drive(Comp.RunTeardown());
            }
        }
    }

    /// <summary>
    /// Networking Core Story 044 (no database): the real backend S3, S4, S5, S7 over fakes. The data source is real
    /// (<c>NpgsqlDataSource.Create</c> does no I/O); only the warm-up and the listener creation are replaced.
    /// </summary>
    [TestFixture]
    public sealed class ZoneServerCompositionWiringTests
    {
        private static readonly string[] CONSTRUCTION_ORDER =
        {
            "TickCompletionQueue", "ServerTickLoop", "NpgsqlCharacterRecordStore", "CharacterPersistenceService",
            "CrossZoneRelayAllowlist", "CrossZoneRelayOrigin", "CrossZoneRelayInbox", "CrossZoneRelayReceiver",
            "NpgsqlRelayPublisher", "CrossCuttingRpcGuardChain", "CharacterMutationGate", "InboundRequestDispatcher",
            "ClientMessageOutbox", "ZoneInstancingWiring", "CharacterStats", "StatusEffectsService", "ServerWideBroadcaster", "ZoneClientRelayHandler", "ZoneSimulationStep",
            "ZoneTickPipeline", "Seal", "RegisterTickDriven", "CreateInboundTransport"
        };

        private static string ConfigWithWarmup(int warmupMs)
        {
            return "{\"Host\":\"db.invalid\",\"Database\":\"d\",\"Username\":\"u\",\"DbWarmupTimeoutMs\":" + warmupMs
                + ",\"NgoShutdownWaitFrames\":2}";
        }

        private static void AssertNoSecrets(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                StringAssert.DoesNotContain("Sup3r", line);
                StringAssert.DoesNotContain("Password=", line);
                StringAssert.DoesNotContain("Host=", line);
                StringAssert.DoesNotContain("Username=", line);
                StringAssert.DoesNotContain("db.invalid", line);
            }
        }

        // ---------------------------------------------------------------- AC-1

        [Test]
        public void test_s3_creates_one_random_provider_and_s5_keeps_the_same_reference()
        {
            var rig = new WiRig();
            Assert.IsNull(rig.Comp.RandomProvider);

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.IsNotNull(rig.Comp.RandomProvider);
            object afterStartup = rig.Comp.RandomProvider;
            rig.Net.FireTick();
            Assert.AreSame(afterStartup, rig.Comp.RandomProvider);
        }

        [Test]
        public void test_seed_is_logged_on_exactly_one_line_during_a_full_startup()
        {
            using (var capture = new WiLogCapture())
            {
                var rig = new WiRig();

                rig.RunAll();

                int seedLines = capture.Lines.FindAll(l => l.Contains("seed")).Count;
                Assert.AreEqual(1, seedLines);
            }
        }

        // ---------------------------------------------------------------- AC-2

        [Test]
        public void test_connection_string_carries_every_config_field_and_the_escaped_password()
        {
            var config = new ZoneServerConfig
            {
                Host = "db.example", Port = 6543, Database = "irongrind", Username = "zone_user",
                Timeout = 7, CommandTimeout = 9, MinPoolSize = 3, MaxPoolSize = 12, ConnectionIdleLifetime = 33
            };

            ZoneConnectionFields f = ZoneCompositionDatabaseTestAccess.BuildFields(config, "p@ss;word", 7u);

            Assert.AreEqual("db.example", f.Host);
            Assert.AreEqual(6543, f.Port);
            Assert.AreEqual("irongrind", f.Database);
            Assert.AreEqual("zone_user", f.Username);
            Assert.AreEqual("p@ss;word", f.Password);
            Assert.AreEqual(7, f.Timeout);
            Assert.AreEqual(9, f.CommandTimeout);
            Assert.IsTrue(f.Pooling);
            Assert.AreEqual(3, f.MinPoolSize);
            Assert.AreEqual(12, f.MaxPoolSize);
            Assert.AreEqual(33, f.ConnectionIdleLifetime);
            Assert.AreEqual("irongrind-zone-7", f.ApplicationName);
            Assert.AreEqual("irongrind-zone-7", ZoneCompositionDatabaseTestAccess.ApplicationNameOf(7u));
        }

        [Test]
        public void test_s7_hands_the_listener_the_built_string_with_the_password_from_the_environment()
        {
            var rig = new WiRig();

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            ZoneConnectionFields f = ZoneCompositionDatabaseTestAccess.Parse(rig.ListenerConnectionString);
            Assert.AreEqual(WiRig.PASSWORD, f.Password);
            Assert.AreEqual("db.invalid", f.Host);
            Assert.AreEqual("irongrind-zone-7", f.ApplicationName);
        }

        // ---------------------------------------------------------------- AC-3

        [Test]
        public void test_unset_password_is_s_fail_at_s4_naming_the_variable_and_quits_1()
        {
            var rig = new WiRig();
            rig.Env.Variables.Remove("IRONGRIND_PG_PASSWORD");
            LogAssert.Expect(LogType.Error, "[ZoneServer] S4: environment variable IRONGRIND_PG_PASSWORD is not set or empty");
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S4: StepReturnedFalse");

            rig.RunAll();

            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
            Assert.IsFalse(ZoneCompositionDatabaseTestAccess.DataSourceCreated(rig.Comp));
            CollectionAssert.DoesNotContain(rig.Observed, "TickCompletionQueue");
        }

        [Test]
        public void test_empty_password_is_s_fail_at_s4_naming_the_variable_and_quits_1()
        {
            var rig = new WiRig();
            rig.Env.Variables["IRONGRIND_PG_PASSWORD"] = string.Empty;
            LogAssert.Expect(LogType.Error, "[ZoneServer] S4: environment variable IRONGRIND_PG_PASSWORD is not set or empty");
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S4: StepReturnedFalse");

            rig.RunAll();

            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
            Assert.IsFalse(ZoneCompositionDatabaseTestAccess.DataSourceCreated(rig.Comp));
        }

        // ---------------------------------------------------------------- AC-4

        [Test]
        public void test_warm_up_timeout_is_s_fail_at_s4_cancels_the_token_and_disposes_the_data_source()
        {
            var rig = new WiRig(ConfigWithWarmup(50));
            CancellationToken seen = default(CancellationToken);
            var started = new ManualResetEventSlim(false);
            rig.Comp.WarmUpOverride = ct =>
            {
                seen = ct;
                started.Set();
                var never = new TaskCompletionSource<bool>();
                ct.Register(() => never.TrySetCanceled());
                return never.Task;
            };
            LogAssert.Expect(LogType.Error, "[ZoneServer] S4: database warm-up timed out");
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S4: StepReturnedFalse");
            var watch = Stopwatch.StartNew();

            rig.RunAll();

            watch.Stop();
            Assert.Less(watch.ElapsedMilliseconds, 1000L, "upper bound only");
            // The warm-up runs on the thread pool and may start after the 50 ms wait expired; wait for it before
            // reading the token it saw (the token is already cancelled either way).
            Assert.IsTrue(started.Wait(1000), "the warm-up task ran");
            Assert.IsTrue(seen.IsCancellationRequested);
            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
            Assert.AreEqual(1, ZoneCompositionDatabaseTestAccess.DataSourceDisposeCount(rig.Comp));
            CollectionAssert.DoesNotContain(rig.Observed, "TickCompletionQueue");
        }

        [Test]
        public void test_warm_up_fault_is_s_fail_at_s4_logging_the_exception_type_only()
        {
            var rig = new WiRig();
            rig.Comp.WarmUpOverride = ct => Task.FromException(new InvalidOperationException(WiRig.PASSWORD));
            LogAssert.Expect(LogType.Error, "[ZoneServer] S4: database warm-up failed: InvalidOperationException");
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S4: StepReturnedFalse");

            rig.RunAll();

            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
            Assert.AreEqual(1, ZoneCompositionDatabaseTestAccess.DataSourceDisposeCount(rig.Comp));
        }

        [Test]
        public void test_warm_up_that_throws_synchronously_is_s_fail_at_s4()
        {
            var rig = new WiRig();
            rig.Comp.WarmUpOverride = ct => { throw new TimeoutException(WiRig.PASSWORD); };
            LogAssert.Expect(LogType.Error, "[ZoneServer] S4: database warm-up failed: TimeoutException");
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S4: StepReturnedFalse");

            rig.RunAll();

            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
        }

        // ---------------------------------------------------------------- AC-5

        [Test]
        public void test_no_log_line_holds_a_secret_across_startup_and_teardown()
        {
            using (var capture = new WiLogCapture())
            {
                var rig = new WiRig();

                rig.RunAll();

                Assert.IsNotEmpty(capture.Lines);
                AssertNoSecrets(capture.Lines);
            }
        }

        [Test]
        public void test_no_log_line_holds_a_secret_when_the_warm_up_fails_with_a_secret_in_its_message()
        {
            using (var capture = new WiLogCapture())
            {
                var rig = new WiRig();
                rig.Comp.WarmUpOverride = ct => Task.FromException(new InvalidOperationException(WiRig.PASSWORD));
                LogAssert.Expect(LogType.Error, "[ZoneServer] S4: database warm-up failed: InvalidOperationException");
                LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S4: StepReturnedFalse");

                rig.RunAll();

                AssertNoSecrets(capture.Lines);
            }
        }

        [Test]
        public void test_no_log_line_holds_a_secret_when_s5_throws_with_a_secret_in_its_message()
        {
            using (var capture = new WiLogCapture())
            {
                var rig = new WiRig();
                rig.Net.ThrowOnSendSeam = true;
                LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S5: InvalidOperationException");

                rig.RunAll();

                AssertNoSecrets(capture.Lines);
            }
        }

        // ---------------------------------------------------------------- AC-6

        [Test]
        public void test_s5_constructs_in_the_documented_order_and_seals_before_registering_the_tick()
        {
            var rig = new WiRig();

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            CollectionAssert.AreEqual(CONSTRUCTION_ORDER, rig.Observed);
            Assert.AreEqual(1, rig.Observed.FindAll(s => s == "RegisterTickDriven").Count);
            Assert.Less(rig.Observed.IndexOf("Seal"), rig.Observed.IndexOf("RegisterTickDriven"));
            Assert.Less(rig.Observed.IndexOf("RegisterTickDriven"), rig.Observed.IndexOf("CreateInboundTransport"));
        }

        [Test]
        public void test_s5_passes_the_dispatcher_as_intake_and_dispatcher_and_the_outbox_as_listener()
        {
            var rig = new WiRig();

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.IsNotNull(rig.Net.Intake);
            Assert.AreSame(rig.Net.Intake, rig.Net.Dispatcher);
            Assert.IsInstanceOf<InboundRequestDispatcher>(rig.Net.Dispatcher);
            Assert.IsInstanceOf<ClientMessageOutbox>(rig.Net.ClientListener);
            CollectionAssert.Contains(rig.Log, "S8:ShellStart");
            CollectionAssert.DoesNotContain(rig.Log, "S8:OutboundStart");
        }

        [Test]
        public void test_one_network_tick_advances_the_attached_tick_loop_and_runs_the_pipeline_without_error()
        {
            var rig = new WiRig();
            rig.Now = 10.0;
            // S3: RandomProviderFactory writes the one seed line itself.
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"^\[Random\] PRNG seed: -?\d+$"));
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            rig.Now = 10.05;
            // The Zone Instancing wiring's tick step reads the stand-in ghost source (Story 014), which logs once.
            LogAssert.Expect(LogType.Log, "[ZoneServer] ghost session TTL enumeration not implemented");
            rig.Net.FireTick();

            Assert.AreEqual(1u, rig.Comp.TickLoop.ServerTickNumber);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_the_zone_instancing_wiring_is_a_simulation_step_and_a_quit_is_applied_by_the_next_real_tick()
        {
            var rig = new WiRig();
            rig.Now = 10.0;
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"^\[Random\] PRNG seed: -?\d+$"));
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            ZoneInstancingWiring wiring = rig.Comp.ZoneInstancing;

            Assert.IsNotNull(wiring);
            CollectionAssert.Contains(rig.Comp.SimulationSteps, wiring, "the wiring is a phase 3 step");
            Assert.AreEqual(1, rig.Observed.FindAll(s => s == "RegisterTickDriven").Count, "only the pipeline is tick-driven");
            Assert.AreEqual(ZoneState.Active, wiring.Lifecycle.State);

            rig.Comp.RequestQuit();
            Assert.AreEqual(1, wiring.PendingQuitCount);
            rig.Now = 10.05;
            LogAssert.Expect(LogType.Log, "[ZoneServer] ghost session TTL enumeration not implemented");
            rig.Net.FireTick();

            Assert.AreEqual(0, wiring.PendingQuitCount, "the pipeline ran the wiring, which applied the quit");
            Assert.IsTrue(wiring.Lifecycle.State == ZoneState.Draining || wiring.Lifecycle.State == ZoneState.Closed, "lifecycle after the quit tick was " + wiring.Lifecycle.State);
        }

        [Test]
        public void test_the_simulation_steps_the_composition_lists_run_in_order_on_the_tick()
        {
            var rig = new WiRig();
            rig.Comp.SimulationSteps.Add(new WiSimStep(rig.Log, "A"));
            rig.Comp.SimulationSteps.Add(new WiSimStep(rig.Log, "B"));
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            rig.Net.FireTick();

            int a = rig.Log.IndexOf("A:1");
            int b = rig.Log.IndexOf("B:1");
            Assert.GreaterOrEqual(a, 0);
            Assert.Less(a, b);
        }

        [Test]
        public void test_zone_simulation_step_with_an_empty_list_runs_nothing_and_a_null_list_throws()
        {
            var steps = new List<IZoneSimulationStep>();
            var simulation = new ZoneSimulationStep(steps);

            Assert.DoesNotThrow(() => simulation.Run(1u, 0.05f));
            Assert.AreEqual(0, simulation.Count);
            Assert.Throws<ArgumentNullException>(() => new ZoneSimulationStep(null));
        }

        // ---------------------------------------------------------------- Status Effects Story 009: per-entry isolation

        private sealed class WiThrowingStep : IZoneSimulationStep
        {
            public int Runs;
            public void Run(uint currentTick, float deltaTime) { Runs++; throw new InvalidOperationException("boom"); }
        }

        private sealed class WiCountingStep : IZoneSimulationStep
        {
            public int Runs;
            public uint LastTick;
            public void Run(uint currentTick, float deltaTime) { Runs++; LastTick = currentTick; }
        }

        [Test]
        public void test_zone_simulation_step_a_throwing_entry_logs_entry_failed_and_the_next_entry_still_runs()
        {
            var thrower = new WiThrowingStep();
            var after = new WiCountingStep();
            var simulation = new ZoneSimulationStep(new List<IZoneSimulationStep> { thrower, after });
            LogAssert.Expect(LogType.Error,
                "[ZoneSimulationStep] EntryFailed: index=0 type=WiThrowingStep tick=9 - InvalidOperationException: boom");

            Assert.DoesNotThrow(() => simulation.Run(9u, 0.05f));

            Assert.AreEqual(1, thrower.Runs);
            Assert.AreEqual(1, after.Runs);
            Assert.AreEqual(9u, after.LastTick);
        }

        // An exception whose Message getter throws: the EntryFailed log cannot be built.
        private sealed class WiUnprintableException : Exception
        {
            public override string Message => throw new InvalidOperationException("Message getter failed");
        }

        private sealed class WiUnprintableThrowingStep : IZoneSimulationStep
        {
            public void Run(uint currentTick, float deltaTime) => throw new WiUnprintableException();
        }

        [Test]
        public void test_zone_simulation_step_an_entry_whose_exception_message_throws_is_skipped_silently_and_the_next_entry_runs()
        {
            var after = new WiCountingStep();
            var simulation = new ZoneSimulationStep(new List<IZoneSimulationStep> { new WiUnprintableThrowingStep(), after });

            Assert.DoesNotThrow(() => simulation.Run(4u, 0.05f));

            Assert.AreEqual(1, after.Runs, "the guarded log did not let the second exception escape Run");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_zone_simulation_step_a_null_entry_logs_entry_failed_with_an_empty_type_and_the_next_entry_runs()
        {
            var after = new WiCountingStep();
            var simulation = new ZoneSimulationStep(new List<IZoneSimulationStep> { null, after });
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"^\[ZoneSimulationStep\] EntryFailed: index=0 type= tick=5 - NullReferenceException: "));

            simulation.Run(5u, 0.05f);

            Assert.AreEqual(1, after.Runs);
        }

        [Test]
        public void test_zone_simulation_step_every_failing_entry_logs_its_own_line_with_its_index()
        {
            var simulation = new ZoneSimulationStep(new List<IZoneSimulationStep>
                { new WiCountingStep(), new WiThrowingStep(), new WiThrowingStep() });
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"EntryFailed: index=1 type=WiThrowingStep tick=3 "));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"EntryFailed: index=2 type=WiThrowingStep tick=3 "));

            simulation.Run(3u, 0.05f);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_zone_simulation_step_success_path_allocates_nothing()
        {
            var simulation = new ZoneSimulationStep(new List<IZoneSimulationStep> { new WiCountingStep(), new WiCountingStep() });
            simulation.Run(1u, 0.05f); // warm-up

            Assert.That(() => simulation.Run(2u, 0.05f), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        // ---------------------------------------------------------------- AC-7

        [Test]
        public void test_character_directory_stand_in_finds_no_character_and_logs_once()
        {
            var directory = new StandInConnectionCharacterDirectory();
            LogAssert.Expect(LogType.Log, "[ZoneServer] connection character directory not implemented");

            bool first = directory.TryGetCharacterId(5u, out CharacterID id);
            bool second = directory.TryGetCharacterId(6u, out CharacterID id2);

            Assert.IsFalse(first);
            Assert.IsFalse(second);
            Assert.AreEqual(CharacterID.Invalid, id);
            Assert.AreEqual(CharacterID.Invalid, id2);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_activity_sink_stand_in_records_nothing_and_logs_once()
        {
            var sink = new StandInConnectionActivitySink();
            LogAssert.Expect(LogType.Log, "[ZoneServer] connection activity sink not implemented");

            sink.OnInboundActivity(5u, 1u);
            sink.OnInboundActivity(5u, 2u);

            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-8

        [Test]
        public void test_the_npgsql_listener_is_assignable_to_the_stop_interface()
        {
            Assert.IsTrue(typeof(IZoneRelayListener).IsAssignableFrom(typeof(NpgsqlRelayListener)));
        }

        [Test]
        public void test_s7_starts_the_listener_once_and_teardown_step_2a_stops_it()
        {
            var rig = new WiRig();

            rig.RunAll();

            Assert.AreEqual(1, rig.Listener.StartCalls);
            Assert.AreEqual(1, rig.Listener.StopCalls);
            Assert.Less(rig.Log.IndexOf("ListenerStart"), rig.Log.IndexOf("ListenerStop"));
        }

        [Test]
        public void test_a_listener_start_failure_is_s_fail_at_s7_and_teardown_still_stops_it()
        {
            var rig = new WiRig();
            rig.ListenerThrowsOnStart = true;
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S7: InvalidOperationException");

            rig.RunAll();

            Assert.AreEqual(1, rig.Listener.StartCalls);
            Assert.AreEqual(1, rig.Listener.StopCalls, "attached before Start(), so step 2a stops it");
            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
        }

        // ---------------------------------------------------------------- AC-9

        [Test]
        public void test_dispose_pass_disposes_the_data_source_once_after_a_clean_run()
        {
            var rig = new WiRig();

            rig.RunAll();
            rig.Comp.TeardownNow();
            rig.Comp.Dispose();

            Assert.IsTrue(ZoneCompositionDatabaseTestAccess.DataSourceCreated(rig.Comp));
            Assert.AreEqual(1, ZoneCompositionDatabaseTestAccess.DataSourceDisposeCount(rig.Comp));
        }

        [Test]
        public void test_dispose_pass_disposes_the_data_source_once_after_a_warm_up_failure()
        {
            var rig = new WiRig();
            rig.Comp.WarmUpOverride = ct => Task.FromException(new InvalidOperationException("x"));
            LogAssert.Expect(LogType.Error, "[ZoneServer] S4: database warm-up failed: InvalidOperationException");
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S4: StepReturnedFalse");

            rig.RunAll();
            rig.Comp.TeardownNow();

            Assert.AreEqual(1, ZoneCompositionDatabaseTestAccess.DataSourceDisposeCount(rig.Comp));
        }

        [Test]
        public void test_dispose_pass_disposes_the_data_source_once_after_an_s5_failure()
        {
            var rig = new WiRig();
            rig.Net.ThrowOnSendSeam = true;
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S5: InvalidOperationException");

            rig.RunAll();
            rig.Comp.TeardownNow();

            Assert.AreEqual(1, ZoneCompositionDatabaseTestAccess.DataSourceDisposeCount(rig.Comp));
            CollectionAssert.DoesNotContain(rig.Observed, "Seal");
        }

        [Test]
        public void test_connection_string_is_held_only_until_s7_or_the_dispose_pass()
        {
            var ok = new WiRig();
            SuRig.Drive(ok.Comp.Start(SuRig.ARGS));
            Assert.IsFalse(ok.Comp.HoldsConnectionString, "cleared at the end of S7");

            var failed = new WiRig();
            failed.Net.ThrowOnSendSeam = true;
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S5: InvalidOperationException");
            SuRig.Drive(failed.Comp.Start(SuRig.ARGS));
            Assert.IsTrue(failed.Comp.HoldsConnectionString, "S5 failed before S7 could clear it");

            Assert.IsTrue(failed.Comp.TryBeginTeardown());
            SuRig.Drive(failed.Comp.RunTeardown());

            Assert.IsFalse(failed.Comp.HoldsConnectionString, "cleared by the Dispose pass");
        }

        [Test]
        public void test_dispose_pass_disposes_the_data_source_once_after_an_s7_failure()
        {
            var rig = new WiRig();
            rig.ListenerThrowsOnStart = true;
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S7: InvalidOperationException");

            rig.RunAll();
            rig.Comp.TeardownNow();

            Assert.AreEqual(1, ZoneCompositionDatabaseTestAccess.DataSourceDisposeCount(rig.Comp));
        }

        [Test]
        public void test_a_failure_before_s3_creates_and_disposes_no_data_source()
        {
            var rig = new WiRig();
            rig.Plan.FailAt = "S2:StaticData";
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S2: StepReturnedFalse");

            rig.RunAll();

            Assert.IsFalse(ZoneCompositionDatabaseTestAccess.DataSourceCreated(rig.Comp));
            Assert.AreEqual(0, ZoneCompositionDatabaseTestAccess.DataSourceDisposeCount(rig.Comp));
            Assert.IsNull(rig.Comp.RandomProvider);
        }

        // ---------------------------------------------------------------- AC-10

        private const string COMPOSITION_FILE = "ZoneServerComposition.cs";

        // The four construction patterns only the composition root may use (AC-10).
        private static readonly string[] CONSTRUCTION_PATTERNS =
        {
            "RegisterTickDriven(", "NpgsqlDataSource.Create(", "CreateSeededFromEntropy(", "new NpgsqlRelayListener("
        };

        // Definitions, removed before counting: they contain the pattern text but are not call sites.
        private static readonly string[][] DEFINITIONS =
        {
            new[] { "ServerTickLoop.cs", "public void RegisterTickDriven(" },
            new[] { "RandomProviderFactory.cs", "public static IRandomProvider CreateSeededFromEntropy(" }
        };

        // Exact call counts allowed per (file, pattern). The composition makes one call of each pattern; the other
        // rows are the named exemptions, each with its reason. A new call anywhere, including a second call in an
        // exempt file or in the composition, changes a count and fails.
        private static readonly object[][] ALLOWED_CALLS =
        {
            new object[] { COMPOSITION_FILE, "RegisterTickDriven(", 1 },
            new object[] { COMPOSITION_FILE, "NpgsqlDataSource.Create(", 1 },
            new object[] { COMPOSITION_FILE, "CreateSeededFromEntropy(", 1 },
            new object[] { COMPOSITION_FILE, "new NpgsqlRelayListener(", 1 },
            // Story 041's two-process verification entry point (its own data source and listener).
            new object[] { "RelayVerificationHost.cs", "NpgsqlDataSource.Create(", 1 },
            new object[] { "RelayVerificationHost.cs", "new NpgsqlRelayListener(", 1 },
            // Character Persistence Story 012's dev-time Npgsql/Dapper round trip, run from a test only.
            new object[] { "NpgsqlRoundTripProbe.cs", "NpgsqlDataSource.Create(", 1 },
            // Character Persistence Story 014's test access (data sources the Database tests own).
            new object[] { "NpgsqlRecordStoreTestAccess.cs", "NpgsqlDataSource.Create(", 2 }
        };

        [Test]
        public void test_the_construction_patterns_appear_in_code_only_in_the_composition_and_the_named_exemptions()
        {
            string src = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "src");
            string[] files = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories);
            Assert.IsNotEmpty(files);
            var actual = new Dictionary<string, int>();
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                string code = StripComments(File.ReadAllText(file));
                foreach (string[] definition in DEFINITIONS)
                {
                    if (definition[0] == name)
                    {
                        code = code.Replace(definition[1], string.Empty);
                    }
                }

                foreach (string pattern in CONSTRUCTION_PATTERNS)
                {
                    int count = CountOccurrences(code, pattern);
                    if (count > 0)
                    {
                        actual[name + "|" + pattern] = count;
                    }
                }
            }

            var expected = new Dictionary<string, int>();
            foreach (object[] row in ALLOWED_CALLS)
            {
                expected[(string)row[0] + "|" + (string)row[1]] = (int)row[2];
            }

            CollectionAssert.AreEquivalent(expected, actual,
                "Call sites of the construction patterns differ from the composition and the named exemptions");
        }

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int index = text.IndexOf(pattern, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(pattern, index + pattern.Length, StringComparison.Ordinal);
            }

            return count;
        }

        // The file's text with every // and /* */ comment removed (a "//" inside a string or char literal is not a comment).
        private static string StripComments(string text)
        {
            var code = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                    }
                }
                else if (c == '/' && next == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 2;
                    code.Append(' ');
                }
                else if (c == '@' && next == '"')
                {
                    code.Append("@\"");
                    i += 2;
                    while (i < text.Length)
                    {
                        code.Append(text[i]);
                        if (text[i] == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"')
                            {
                                code.Append('"');
                                i++;
                            }
                            else
                            {
                                i++;
                                break;
                            }
                        }

                        i++;
                    }
                }
                else if (c == '"' || c == '\'')
                {
                    code.Append(c);
                    i++;
                    while (i < text.Length)
                    {
                        char ch = text[i];
                        code.Append(ch);
                        i++;
                        if (ch == '\\' && i < text.Length)
                        {
                            code.Append(text[i]);
                            i++;
                        }
                        else if (ch == c || ch == '\n')
                        {
                            break;
                        }
                    }
                }
                else
                {
                    code.Append(c);
                    i++;
                }
            }

            return code.ToString();
        }
    }
}
