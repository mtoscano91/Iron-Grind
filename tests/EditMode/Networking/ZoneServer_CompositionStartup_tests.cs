using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>Fault plan shared by the startup fakes: a key may throw or report failure.</summary>
    internal sealed class SuPlan
    {
        public string ThrowAt;
        public string FailAt;

        public bool Step(List<string> log, string key)
        {
            log.Add(key);
            return Probe(key);
        }

        public bool Probe(string key)
        {
            if (ThrowAt == key)
            {
                throw new InvalidOperationException("secret-connection-detail");
            }

            return FailAt != key;
        }
    }

    internal sealed class SuApp : IZoneApplication
    {
        private readonly List<string> _log;
        private readonly SuPlan _plan;
        public SuApp(List<string> log, SuPlan plan) { _log = log; _plan = plan; }
        public bool IsEditor => false;
        public void Quit(int code) { _log.Add("Quit:" + code); }
        public void KeepAcrossSceneLoads() { _plan.Step(_log, "S1:KeepAcrossSceneLoads"); }
        public void SubscribeWantsToQuit(Func<bool> handler) { _plan.Step(_log, "S1:SubscribeQuit"); }
        public void UnsubscribeWantsToQuit(Func<bool> handler) { _log.Add("QuitUnsubscribe"); }
    }

    internal sealed class SuNetwork : IZoneNetwork
    {
        private readonly List<string> _log;
        private readonly SuPlan _plan;
        public Action SubscribedHandler;
        public Action UnsubscribedHandler;
        public uint ConfiguredTickRate;
        public int ConfiguredFrameRate = -1;
        public int ConfiguredVSync = -1;
        public bool ShutdownForever;

        public SuNetwork(List<string> log, SuPlan plan) { _log = log; _plan = plan; }

        public void PrepareObject() { _plan.Step(_log, "S1:PrepareObject"); }

        public void Configure(uint tickRate, int targetFrameRate, int vSyncCount)
        {
            _log.Add("S8:Configure");
            ConfiguredTickRate = tickRate;
            ConfiguredFrameRate = targetFrameRate;
            ConfiguredVSync = vSyncCount;
        }

        public bool StartServer() { return _plan.Step(_log, "S8:StartServer"); }

        public void SubscribeTick(Action handler)
        {
            _plan.Step(_log, "S9:Subscribe");
            SubscribedHandler = handler;
        }

        public void UnsubscribeTick(Action handler) { _log.Add("TickUnsubscribe"); UnsubscribedHandler = handler; }
        public void Shutdown() { _log.Add("NgoShutdown"); }
        public bool IsAlive => true;
        public bool ShutdownInProgress => ShutdownForever;

        public IClientMessageSendSeam CreateSendSeam() { return null; }

        public IZoneInboundTransport CreateInboundTransport(
            IInboundMessageIntake intake, IInboundRequestDispatcher dispatcher, IClientConnectionListener clientListener)
        {
            return null;
        }

        public void FireTick() { SubscribedHandler(); }
    }

    internal sealed class SuNav : IZoneNavigation
    {
        private readonly List<string> _log;
        private readonly SuPlan _plan;
        public SuNav(List<string> log, SuPlan plan) { _log = log; _plan = plan; }
        public void Initialize(ZoneID zone, NavMeshDataInstance instance) { _plan.Step(_log, "S6:NavInit"); }
        public void Teardown() { _log.Add("NavTeardown"); }
    }

    internal sealed class SuScene : IZoneScene
    {
        private readonly List<string> _log;
        private readonly SuPlan _plan;
        private int _polls;
        public int NotDonePolls;
        public Action OnPoll;
        public string LoadedTemplate;

        public SuScene(List<string> log, SuPlan plan) { _log = log; _plan = plan; }

        public void BeginLoad(string zoneTemplate)
        {
            LoadedTemplate = zoneTemplate;
            _plan.Step(_log, "S6:BeginLoad");
        }

        public bool IsDone
        {
            get
            {
                _plan.Probe("S6:IsDone");
                OnPoll?.Invoke();
                return _polls++ >= NotDonePolls;
            }
        }

        public bool Succeeded => _plan.Probe("S6:Succeeded");
        public NavMeshDataInstance AddNavMeshData() { _plan.Step(_log, "S6:AddNavMeshData"); return default(NavMeshDataInstance); }
    }

    internal sealed class SuBounds : IZoneBoundsValidator
    {
        private readonly List<string> _log;
        private readonly SuPlan _plan;
        public SuBounds(List<string> log, SuPlan plan) { _log = log; _plan = plan; }
        public bool Validate(ZoneID zone) { return _plan.Step(_log, "S6:Bounds"); }
    }

    internal sealed class SuStateOwner : IZoneStateOwner
    {
        private readonly List<string> _log;
        public SuStateOwner(List<string> log) { _log = log; }
        public void MarkActive(ZoneID zone) { _log.Add("S9:MarkActive"); }
        public void MarkClosedInvalidBounds(ZoneID zone) { _log.Add("S6:MarkClosedInvalidBounds"); }
    }

    internal sealed class SuGateway : IGatewayRegistration
    {
        private readonly List<string> _log;
        private readonly SuPlan _plan;
        public ZoneID Zone;
        public ushort Port;
        public SuGateway(List<string> log, SuPlan plan) { _log = log; _plan = plan; }

        public void RegisterZone(ZoneID zone, ushort port)
        {
            Zone = zone;
            Port = port;
            _plan.Step(_log, "S10:Register");
        }
    }

    internal sealed class SuStatic : IZoneStaticDataLoader
    {
        private readonly List<string> _log;
        private readonly SuPlan _plan;
        public SuStatic(List<string> log, SuPlan plan) { _log = log; _plan = plan; }
        public bool LoadAndValidate() { return _plan.Step(_log, "S2:StaticData"); }
    }

    internal sealed class SuConfigSource : IZoneConfigSource
    {
        private readonly List<string> _log;
        private readonly SuPlan _plan;
        public string Json = "{\"Host\":\"h\",\"Database\":\"d\",\"Username\":\"u\",\"NgoShutdownWaitFrames\":2}";
        public string PathRead;
        public SuConfigSource(List<string> log, SuPlan plan) { _log = log; _plan = plan; }

        public string ReadAllText(string path)
        {
            PathRead = path;
            return _plan.Step(_log, "S0:ReadConfig") ? Json : "{}";
        }
    }

    internal sealed class SuTransport : IZoneInboundTransport
    {
        private readonly List<string> _log;
        public SuTransport(List<string> log) { _log = log; }
        public void Start() { _log.Add("S8:ShellStart"); }
        public void Stop() { _log.Add("ShellStop"); }
    }

    internal sealed class SuSender : IZoneOutboundSender
    {
        private readonly List<string> _log;
        public SuSender(List<string> log) { _log = log; }
        public void Start() { _log.Add("S8:OutboundStart"); }
    }

    /// <summary>Fake backend modelling Story 044: S5 builds the graph and attaches it to the composition.</summary>
    internal sealed class SuBackend : IZoneBackendSteps
    {
        private readonly List<string> _log;
        private readonly SuPlan _plan;
        public ZoneServerComposition Comp;
        public Action OnOpenDatabase;
        public bool AttachTickLoop = true;
        public readonly ServerTickLoop TickLoop = new ServerTickLoop();

        public SuBackend(List<string> log, SuPlan plan) { _log = log; _plan = plan; }

        public bool InitializeRandom() { return _plan.Step(_log, "S3:Random"); }

        public bool OpenDatabase()
        {
            bool ok = _plan.Step(_log, "S4:Database");
            OnOpenDatabase?.Invoke();
            return ok;
        }

        public bool ConstructObjects()
        {
            if (!_plan.Step(_log, "S5:Construct"))
            {
                return false;
            }

            Comp.AttachOwnedObjects(
                new ZsQueue(_log), new ZsOutbound(_log), new ZsListener(_log), new SuTransport(_log),
                () => TickLoop.ServerTickNumber, null);
            Comp.AttachOutboundSender(new SuSender(_log));
            if (AttachTickLoop)
            {
                Comp.AttachTickLoop(TickLoop);
            }

            return true;
        }

        public bool StartListener() { return _plan.Step(_log, "S7:Listener"); }
    }

    /// <summary>Everything a startup test needs, all fakes appending to one shared call log.</summary>
    internal sealed class SuRig
    {
        public static readonly string[] ARGS =
        {
            "-zoneTemplate", "Forest", "-zoneId", "7", "-port", "7777", "-serverConfig", "cfg.json"
        };

        public readonly List<string> Log = new List<string>();
        public readonly SuPlan Plan = new SuPlan();
        public readonly SuApp App;
        public readonly SuNetwork Net;
        public readonly SuScene Scene;
        public readonly SuGateway Gateway;
        public readonly SuConfigSource Config;
        public readonly SuBackend Backend;
        public readonly ZoneServerComposition Comp;
        public double Now;

        public SuRig()
        {
            App = new SuApp(Log, Plan);
            Net = new SuNetwork(Log, Plan);
            Scene = new SuScene(Log, Plan);
            Gateway = new SuGateway(Log, Plan);
            Config = new SuConfigSource(Log, Plan);
            Backend = new SuBackend(Log, Plan);
            Comp = new ZoneServerComposition(
                App, Net, new SuNav(Log, Plan), new ZsClose(Log), Scene, new SuBounds(Log, Plan),
                new SuStatic(Log, Plan), new SuStateOwner(Log), Gateway, Config, Backend, () => Now);
            Backend.Comp = Comp;
        }

        public static int Drive(IEnumerator e)
        {
            int yields = 0;
            while (e.MoveNext())
            {
                yields++;
                Assert.Less(yields, 100000, "Runaway coroutine.");
            }

            return yields;
        }

        /// <summary>Runs Start to the end, then the teardown the way the host's Update would.</summary>
        public int RunAll()
        {
            int yields = Drive(Comp.Start(ARGS));
            if (Comp.TryBeginTeardown())
            {
                Drive(Comp.RunTeardown());
            }

            return yields;
        }

        /// <summary>Highest startup step number that appears in the log, or -1.</summary>
        public int MaxStep()
        {
            int max = -1;
            foreach (string entry in Log)
            {
                Match m = Regex.Match(entry, @"^S(\d+):");
                if (m.Success)
                {
                    max = Math.Max(max, int.Parse(m.Groups[1].Value));
                }
            }

            return max;
        }
    }

    /// <summary>Networking Core Story 043: zone startup S0-S10, S-fail, quit during startup, tick handler.</summary>
    [TestFixture]
    public sealed class ZoneServerCompositionStartupTests
    {
        private static readonly string[] FULL_STARTUP =
        {
            "S0:ReadConfig", "S1:SubscribeQuit", "S1:KeepAcrossSceneLoads", "S1:PrepareObject", "S2:StaticData",
            "S3:Random", "S4:Database", "S5:Construct", "S6:BeginLoad", "S6:AddNavMeshData", "S6:NavInit", "S6:Bounds",
            "S7:Listener", "S8:Configure", "S8:StartServer", "S8:ShellStart", "S8:OutboundStart",
            "S9:MarkActive", "S9:Subscribe", "S10:Register"
        };

        // ---------------------------------------------------------------- AC-4

        [Test]
        public void test_start_runs_s0_to_s10_in_exact_order_with_s8_sub_order()
        {
            var rig = new SuRig();

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            CollectionAssert.AreEqual(FULL_STARTUP, rig.Log);
            Assert.IsFalse(rig.Comp.TryBeginTeardown(), "A successful startup requests no teardown.");
            Assert.AreEqual("cfg.json", rig.Config.PathRead);
            Assert.AreEqual("Forest", rig.Scene.LoadedTemplate);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_start_registers_the_zone_with_the_parsed_id_and_port()
        {
            var rig = new SuRig();

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.AreEqual(new ZoneID(7u), rig.Gateway.Zone);
            Assert.AreEqual((ushort)7777, rig.Gateway.Port);
        }

        [Test]
        public void test_start_is_single_use_second_call_yields_nothing_and_runs_no_step()
        {
            var rig = new SuRig();
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            int logged = rig.Log.Count;

            Assert.AreEqual(0, SuRig.Drive(rig.Comp.Start(SuRig.ARGS)));
            Assert.AreEqual(logged, rig.Log.Count);
        }

        // ---------------------------------------------------------------- AC-5

        [Test]
        public void test_scene_not_done_for_three_polls_yields_exactly_three_times_all_inside_s6()
        {
            var rig = new SuRig();
            rig.Scene.NotDonePolls = 3;
            var seenAtYield = new List<string>();

            IEnumerator e = rig.Comp.Start(SuRig.ARGS);
            while (e.MoveNext())
            {
                seenAtYield.Add(rig.Log[rig.Log.Count - 1]);
            }

            Assert.AreEqual(3, seenAtYield.Count);
            foreach (string last in seenAtYield)
            {
                Assert.AreEqual("S6:BeginLoad", last);
            }

            CollectionAssert.AreEqual(FULL_STARTUP, rig.Log);
        }

        [Test]
        public void test_instant_scene_yields_zero_times()
        {
            var rig = new SuRig();

            Assert.AreEqual(0, SuRig.Drive(rig.Comp.Start(SuRig.ARGS)));
        }

        // ---------------------------------------------------------------- AC-6 / AC-7 / AC-9

        [TestCase("S0:ReadConfig", true, 0)]
        [TestCase("S0:ReadConfig", false, 0)]
        [TestCase("S1:PrepareObject", true, 1)]
        [TestCase("S1:KeepAcrossSceneLoads", true, 1)]
        [TestCase("S1:SubscribeQuit", true, 1)]
        [TestCase("S2:StaticData", true, 2)]
        [TestCase("S2:StaticData", false, 2)]
        [TestCase("S3:Random", true, 3)]
        [TestCase("S3:Random", false, 3)]
        [TestCase("S4:Database", true, 4)]
        [TestCase("S4:Database", false, 4)]
        [TestCase("S5:Construct", true, 5)]
        [TestCase("S5:Construct", false, 5)]
        [TestCase("S6:BeginLoad", true, 6)]
        [TestCase("S6:IsDone", true, 6)]
        [TestCase("S6:Succeeded", true, 6)]
        [TestCase("S6:Succeeded", false, 6)]
        [TestCase("S6:AddNavMeshData", true, 6)]
        [TestCase("S6:NavInit", true, 6)]
        [TestCase("S6:Bounds", true, 6)]
        [TestCase("S6:Bounds", false, 6)]
        [TestCase("S7:Listener", true, 7)]
        [TestCase("S7:Listener", false, 7)]
        [TestCase("S8:StartServer", true, 8)]
        [TestCase("S8:StartServer", false, 8)]
        [TestCase("S9:Subscribe", true, 9)]
        [TestCase("S10:Register", true, 10)]
        public void test_fault_at_step_ends_startup_with_one_log_no_later_step_and_quit_1(string key, bool doThrow, int step)
        {
            var rig = new SuRig();
            if (doThrow) { rig.Plan.ThrowAt = key; } else { rig.Plan.FailAt = key; }
            string typeName = doThrow ? "InvalidOperationException" : "StepReturnedFalse";
            if (key == "S0:ReadConfig" && !doThrow)
            {
                // The fake config source returns "{}": the S0 reason names the first missing field.
                LogAssert.Expect(LogType.Error, "[ZoneServer] S0: Host must not be empty");
            }

            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S" + step + ": " + typeName);

            rig.RunAll();

            Assert.LessOrEqual(rig.MaxStep(), step, "A later step ran.");
            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
            Assert.AreEqual(1, rig.Log.FindAll(s => s == "Quit:1").Count);

            // Only built objects are torn down: the object graph exists from S5 on (S5 itself builds nothing on failure).
            bool graphBuilt = step > 5;
            Assert.AreEqual(graphBuilt, rig.Log.Contains("ListenerStop"));
            Assert.AreEqual(graphBuilt, rig.Log.Contains("ShellStop"));

            // Navigation is torn down once S6 called Initialize (also when it threw); CR-ZI-12 once the zone state
            // owner was told of T-3 (S6 invalid bounds) or T-2 (S9).
            bool navInitialized = step > 6 || key == "S6:NavInit" || key == "S6:Bounds";
            bool zoneStateEntered = step >= 9 || (key == "S6:Bounds" && !doThrow);
            Assert.AreEqual(navInitialized, rig.Log.Contains("NavTeardown"));
            Assert.AreEqual(zoneStateEntered, rig.Log.Contains("CloseSteps"));

            // AC-7: flush and drain only once S9 completed the tick subscription.
            bool tickStarted = step == 10;
            Assert.AreEqual(tickStarted, rig.Log.Contains("OutboundFlush"));
            Assert.AreEqual(tickStarted, rig.Log.Contains("Drain"));
            Assert.AreEqual(tickStarted, rig.Log.Contains("TickUnsubscribe"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_s0_command_line_failure_is_s_fail_at_s0_before_the_config_is_read()
        {
            var rig = new SuRig();
            LogAssert.Expect(LogType.Error, "[ZoneServer] S0: -zoneId is missing");
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S0: StepReturnedFalse");

            Drive(rig.Comp.Start(new[] { "-zoneTemplate", "Forest" }));

            CollectionAssert.AreEqual(new string[0], rig.Log);
            Assert.IsTrue(rig.Comp.TryBeginTeardown());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_s0_invalid_config_text_logs_the_field_name_and_no_config_values()
        {
            var rig = new SuRig();
            rig.Config.Json = "{\"Host\":\"secret-host\",\"Database\":\"\",\"Username\":\"u\"}";
            LogAssert.Expect(LogType.Error, "[ZoneServer] S0: Database must not be empty");
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S0: StepReturnedFalse");

            rig.RunAll();

            CollectionAssert.DoesNotContain(rig.Log, "S1:SubscribeQuit");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_missing_required_seam_is_s_fail_at_its_step_not_an_exception()
        {
            var comp = new ZoneServerComposition(null, null, null, null, null, null, null, null, null, null, null, null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S0: NullReferenceException");

            Assert.DoesNotThrow(() => Drive(comp.Start(SuRig.ARGS)));

            Assert.IsTrue(comp.TryBeginTeardown());
            Assert.DoesNotThrow(() => Drive(comp.RunTeardown()));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_s6_invalid_bounds_marks_the_zone_closed_t3_and_fails_at_s6()
        {
            var rig = new SuRig();
            rig.Plan.FailAt = "S6:Bounds";
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S6: StepReturnedFalse");

            rig.RunAll();

            CollectionAssert.Contains(rig.Log, "S6:MarkClosedInvalidBounds");
            CollectionAssert.DoesNotContain(rig.Log, "S9:MarkActive");
            CollectionAssert.DoesNotContain(rig.Log, "S10:Register");
        }

        [Test]
        public void test_s9_without_an_attached_tick_loop_fails_at_s9_and_never_marks_active()
        {
            var rig = new SuRig();
            rig.Backend.AttachTickLoop = false;
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S9: InvalidOperationException");

            rig.RunAll();

            CollectionAssert.DoesNotContain(rig.Log, "S9:MarkActive");
            CollectionAssert.DoesNotContain(rig.Log, "S9:Subscribe");
            CollectionAssert.DoesNotContain(rig.Log, "S10:Register");
            CollectionAssert.DoesNotContain(rig.Log, "Drain");
        }

        [Test]
        public void test_fault_at_s7_has_no_flush_and_no_drain_but_stops_the_built_objects()
        {
            var rig = new SuRig();
            rig.Plan.ThrowAt = "S7:Listener";
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S7: InvalidOperationException");

            rig.RunAll();

            CollectionAssert.DoesNotContain(rig.Log, "OutboundFlush");
            CollectionAssert.DoesNotContain(rig.Log, "Drain");
            CollectionAssert.Contains(rig.Log, "ListenerStop");
            CollectionAssert.Contains(rig.Log, "NgoShutdown");
        }

        [Test]
        public void test_fault_at_s10_runs_flush_and_drain_and_unsubscribes_the_tick_handler()
        {
            var rig = new SuRig();
            rig.Plan.ThrowAt = "S10:Register";
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S10: InvalidOperationException");

            rig.RunAll();

            CollectionAssert.Contains(rig.Log, "OutboundFlush");
            CollectionAssert.Contains(rig.Log, "Drain");
            Assert.AreSame(rig.Net.SubscribedHandler, rig.Net.UnsubscribedHandler);
            Assert.IsTrue(rig.Log.IndexOf("TickUnsubscribe") < rig.Log.IndexOf("ListenerStop"));
        }

        [Test]
        public void test_no_step_is_retried_after_a_failure()
        {
            var rig = new SuRig();
            rig.Plan.FailAt = "S4:Database";
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S4: StepReturnedFalse");

            rig.RunAll();
            Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.AreEqual(1, rig.Log.FindAll(s => s == "S4:Database").Count);
            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
        }

        // ---------------------------------------------------------------- AC-8

        [Test]
        public void test_quit_during_s4_lets_s4_finish_runs_no_s5_and_quits_clean()
        {
            var rig = new SuRig();
            rig.Backend.OnOpenDatabase = () => Assert.IsFalse(rig.Comp.OnWantsToQuit());

            rig.RunAll();

            CollectionAssert.Contains(rig.Log, "S4:Database");
            CollectionAssert.DoesNotContain(rig.Log, "S5:Construct");
            Assert.AreEqual(4, rig.MaxStep());
            Assert.AreEqual("Quit:0", rig.Log[rig.Log.Count - 1]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_quit_during_the_s6_scene_wait_runs_no_s7_and_quits_clean()
        {
            var rig = new SuRig();
            rig.Scene.NotDonePolls = 5;
            rig.Scene.OnPoll = () => rig.Comp.OnWantsToQuit();

            rig.RunAll();

            Assert.AreEqual(6, rig.MaxStep());
            CollectionAssert.DoesNotContain(rig.Log, "S6:NavInit");
            Assert.AreEqual("Quit:0", rig.Log[rig.Log.Count - 1]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_quit_then_a_failure_in_the_same_step_still_quits_clean()
        {
            var rig = new SuRig();
            rig.Plan.FailAt = "S4:Database";
            rig.Backend.OnOpenDatabase = () => rig.Comp.OnWantsToQuit();
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S4: StepReturnedFalse");

            rig.RunAll();

            Assert.AreEqual("Quit:0", rig.Log[rig.Log.Count - 1]);
            Assert.AreEqual(4, rig.MaxStep());
        }

        [Test]
        public void test_request_teardown_clean_before_start_runs_no_step()
        {
            var rig = new SuRig();
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);

            rig.RunAll();

            Assert.AreEqual(-1, rig.MaxStep());
            Assert.AreEqual("Quit:0", rig.Log[rig.Log.Count - 1]);
        }

        // ---------------------------------------------------------------- AC-10

        [Test]
        public void test_s8_configures_20_20_0_before_start_server()
        {
            var rig = new SuRig();

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.AreEqual(20u, rig.Net.ConfiguredTickRate);
            Assert.AreEqual(20, rig.Net.ConfiguredFrameRate);
            Assert.AreEqual(0, rig.Net.ConfiguredVSync);
            Assert.Less(rig.Log.IndexOf("S8:Configure"), rig.Log.IndexOf("S8:StartServer"));
        }

        [Test]
        public void test_three_network_tick_events_advance_the_tick_loop_three_times_with_positive_durations()
        {
            var rig = new SuRig();
            rig.Now = 100.0;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            for (int i = 1; i <= 3; i++)
            {
                rig.Now += 0.05 * i;
                rig.Net.FireTick();

                Assert.AreEqual((uint)i, rig.Backend.TickLoop.ServerTickNumber);
                Assert.AreEqual(0.05f * i, rig.Comp.LastTickDurationSeconds, 1e-4f);
                Assert.Greater(rig.Comp.LastTickDurationSeconds, 0f);
            }
        }

        [Test]
        public void test_s9_subscribes_one_named_handler_that_teardown_unsubscribes()
        {
            var rig = new SuRig();
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);

            Assert.IsTrue(rig.Comp.TryBeginTeardown());
            Drive(rig.Comp.RunTeardown());

            Assert.IsNotNull(rig.Net.SubscribedHandler);
            Assert.AreSame(rig.Net.SubscribedHandler, rig.Net.UnsubscribedHandler);
            CollectionAssert.Contains(rig.Log, "OutboundFlush");
            CollectionAssert.Contains(rig.Log, "Drain");
        }

        // ---------------------------------------------------------------- AC-11

        [Test]
        public void test_nav_initialize_throwing_means_no_tick_subscription_and_no_gateway_registration()
        {
            var rig = new SuRig();
            rig.Plan.ThrowAt = "S6:NavInit";
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S6: InvalidOperationException");

            rig.RunAll();

            CollectionAssert.DoesNotContain(rig.Log, "S9:Subscribe");
            CollectionAssert.DoesNotContain(rig.Log, "S10:Register");
            Assert.IsNull(rig.Net.SubscribedHandler);

            // The partly initialised navigation (and its NavMesh data) is still released, but no CR-ZI-12 runs.
            CollectionAssert.Contains(rig.Log, "NavTeardown");
            CollectionAssert.DoesNotContain(rig.Log, "CloseSteps");
        }

        // ---------------------------------------------------------------- wantsToQuit handler lifetime

        [Test]
        public void test_teardown_now_unsubscribes_the_quit_handler_once_after_startup_subscribed_it()
        {
            var rig = new SuRig();
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            rig.Comp.TeardownNow();
            rig.Comp.TeardownNow();
            rig.Comp.Dispose();

            Assert.AreEqual(1, rig.Log.FindAll(s => s == "QuitUnsubscribe").Count);
            Assert.Greater(rig.Log.IndexOf("QuitUnsubscribe"), rig.Log.IndexOf("NgoShutdown"));
            CollectionAssert.DoesNotContain(rig.Log, "Quit:0");
        }

        [Test]
        public void test_teardown_now_after_a_failure_before_s1_does_not_unsubscribe()
        {
            var rig = new SuRig();
            rig.Plan.ThrowAt = "S0:ReadConfig";
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S0: InvalidOperationException");
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            rig.Comp.TeardownNow();

            CollectionAssert.DoesNotContain(rig.Log, "QuitUnsubscribe");
        }

        [Test]
        public void test_run_teardown_never_unsubscribes_the_quit_handler()
        {
            var rig = new SuRig();
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);

            Assert.IsTrue(rig.Comp.TryBeginTeardown());
            Drive(rig.Comp.RunTeardown());

            CollectionAssert.DoesNotContain(rig.Log, "QuitUnsubscribe");
            Assert.AreEqual("Quit:0", rig.Log[rig.Log.Count - 1]);
        }

        // ---------------------------------------------------------------- AC-12

        [Test]
        public void test_backend_steps_are_reached_at_s3_s4_s5_s7_in_that_order()
        {
            var rig = new SuRig();

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            var backend = rig.Log.FindAll(s => s == "S3:Random" || s == "S4:Database" || s == "S5:Construct" || s == "S7:Listener");
            CollectionAssert.AreEqual(new[] { "S3:Random", "S4:Database", "S5:Construct", "S7:Listener" }, backend);
        }

        // ---------------------------------------------------------------- S0 config use

        [Test]
        public void test_s0_applies_the_config_ngo_shutdown_wait_frames_to_the_teardown_wait()
        {
            var rig = new SuRig(); // the config text sets NgoShutdownWaitFrames to 2
            rig.Net.ShutdownForever = true;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[ZoneServer\] .*2 frames"));

            Assert.IsTrue(rig.Comp.TryBeginTeardown());
            int yields = Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(2, yields);
        }

        // ---------------------------------------------------------------- stand-in

        [Test]
        public void test_stand_in_zone_state_owner_logs_once_across_both_methods()
        {
            var owner = new StandInZoneStateOwner();
            LogAssert.Expect(LogType.Log, "[ZoneServer] zone state not implemented");

            owner.MarkActive(new ZoneID(1u));
            owner.MarkClosedInvalidBounds(new ZoneID(1u));
            owner.MarkActive(new ZoneID(1u));

            LogAssert.NoUnexpectedReceived();
        }

        private static int Drive(IEnumerator e)
        {
            return SuRig.Drive(e);
        }
    }
}
