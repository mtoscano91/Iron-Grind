using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Navigation
{
    /// <summary>
    /// Composition rig for Navigation Story 010: the real backend over the Networking test fakes, with the navigation
    /// built by the composition (null seam) over the Navigation fakes through the three override hooks.
    /// </summary>
    internal sealed class NavCompositionRig
    {
        public readonly List<string> Log = new List<string>();
        public readonly List<string> Observed = new List<string>();
        public readonly SuPlan Plan = new SuPlan();
        public readonly WiNetwork Net;
        public readonly FakeNavAgentFactory Factory;
        public readonly FakeNavMeshInstanceOps Ops;
        public readonly ZoneServerComposition Comp;
        public readonly NavigationConfig Config;

        /// <param name="validInstance">Whether the fake instance ops call the (default) instance valid.</param>
        /// <param name="factoryOverride">Whether the fake factory is injected; false keeps the real Unity factory.</param>
        /// <param name="ownConfig">True: no config override, the composition creates (and destroys) its own.</param>
        /// <param name="seam">A navigation seam to pass instead of null.</param>
        public NavCompositionRig(bool validInstance = true, bool factoryOverride = true, bool ownConfig = false,
            IZoneNavigation seam = null)
        {
            Factory = new FakeNavAgentFactory(Log);
            Ops = new FakeNavMeshInstanceOps(Log);
            if (validInstance)
            {
                Ops.Add(default(UnityEngine.AI.NavMeshDataInstance));
            }

            var env = new WiEnvironment();
            env.Variables["IRONGRIND_PG_PASSWORD"] = WiRig.PASSWORD;
            Comp = ZoneServerComposition.CreateWithRealBackend(
                new SuApp(Log, Plan), Net = new WiNetwork(Log), seam, new ZsClose(Log), new SuScene(Log, Plan),
                new SuBounds(Log, Plan), new SuStatic(Log, Plan), new SuStateOwner(Log), new SuGateway(Log, Plan),
                new SuConfigSource(Log, Plan) { Json = WiRig.DEFAULT_CONFIG }, env, null);
            Comp.ConstructionObserver = Observed.Add;
            Comp.ItemDatabaseOverride = WiRig.EmptyItemDatabase();
            Comp.WarmUpOverride = ct => Task.CompletedTask;
            Comp.ListenerFactoryOverride = (connectionString, receiver) => new WiListener(Log);
            if (!ownConfig)
            {
                Config = ScriptableObject.CreateInstance<NavigationConfig>();
                Comp.NavigationConfigOverride = Config;
            }

            if (factoryOverride)
            {
                Comp.NavAgentFactoryOverride = () => Factory;
            }

            Comp.NavMeshInstanceOpsOverride = () => Ops;
        }

        public void Start()
        {
            SuRig.Drive(Comp.Start(SuRig.ARGS));
        }

        /// <summary>Requests a Clean teardown and runs it the way the host's Update would.</summary>
        public void Teardown()
        {
            Comp.RequestTeardown(ZoneExitCode.Clean);
            if (Comp.TryBeginTeardown())
            {
                SuRig.Drive(Comp.RunTeardown());
            }
        }

        public void Destroy()
        {
            if (Config != null)
            {
                UnityEngine.Object.DestroyImmediate(Config);
            }
        }
    }

    /// <summary>
    /// Records warnings and errors (not info lines such as the startup PRNG seed) while alive, so a test can assert
    /// "exactly one warning". <c>LogAssert.NoUnexpectedReceived</c> also fails on info lines, so it cannot be used here.
    /// </summary>
    internal sealed class NavProblemCapture : IDisposable
    {
        public readonly List<string> Problems = new List<string>();

        public NavProblemCapture() { Application.logMessageReceived += OnLog; }

        public void Dispose() { Application.logMessageReceived -= OnLog; }

        public string Joined => string.Join(" | ", Problems);

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Log)
            {
                Problems.Add(type + ": " + condition);
            }
        }
    }

    /// <summary>Navigation Story 010: composition wiring, S6, teardown step 3 and the frame-rate probe.</summary>
    [TestFixture]
    public sealed class NavigationCompositionTests
    {
        private const string POOL_ROOT_NAME = "NavCompositionTestRoot";

        private readonly List<NavCompositionRig> _rigs = new List<NavCompositionRig>();

        [TearDown]
        public void DestroyRigs()
        {
            foreach (NavCompositionRig rig in _rigs)
            {
                rig.Destroy();
            }

            _rigs.Clear();
        }

        private NavCompositionRig NewRig(bool validInstance = true, bool factoryOverride = true, bool ownConfig = false,
            IZoneNavigation seam = null)
        {
            var rig = new NavCompositionRig(validInstance, factoryOverride, ownConfig, seam);
            _rigs.Add(rig);
            return rig;
        }

        private static int IndexOfStep(IList<IZoneSimulationStep> steps, string typeName)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].GetType().Name == typeName)
                {
                    return i;
                }
            }

            return -1;
        }

        private static MobSpawnedArgs SpawnArgs(ZoneServerComposition comp, uint entity)
        {
            return new MobSpawnedArgs(comp.ZoneId, new EntityID(entity), new MobTypeID(1u), Vector3.zero);
        }

        // ---------------------------------------------------------------- host type and seams

        [Test]
        public void test_composition_null_navigation_builds_real_service_with_override_factory_and_stand_in_speeds()
        {
            NavCompositionRig rig = NewRig();

            rig.Start();

            Assert.IsInstanceOf<ZoneNavigationService>(rig.Comp.NavigationService);
            Assert.AreEqual(NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT, rig.Factory.CreateCalls);
            Assert.IsInstanceOf<StandInNavMobSpeedSource>(((ZoneNavigationService)rig.Comp.NavigationService).SpeedSource);

            rig.Comp.MobEvents.RaiseMobSpawned(SpawnArgs(rig.Comp, 900u));
            FakeNavAgent claimed = rig.Factory.Created.Find(a => a.EnabledValue);
            Assert.IsNotNull(claimed);
            Assert.AreEqual(NavigationConfigValues.STAND_IN_MOB_MOVE_SPEED_DEFAULT, claimed.SpeedValue);
        }

        [Test]
        public void test_composition_passed_seam_builds_no_bus_no_step_and_no_provider()
        {
            var seamLog = new List<string>();
            NavCompositionRig withSeam = NewRig(seam: new SuNav(seamLog, new SuPlan()));

            withSeam.Start();

            Assert.IsNull(withSeam.Comp.MobEvents);
            Assert.IsNull(withSeam.Comp.NavigationProvider);
            Assert.IsNull(withSeam.Comp.NavigationService);
            Assert.AreEqual(-1, IndexOfStep(withSeam.Comp.SimulationSteps, nameof(NavigationPhaseStep)));
            CollectionAssert.DoesNotContain(withSeam.Observed, nameof(ZoneNavigationService));
            CollectionAssert.Contains(seamLog, "S6:NavInit");
            Assert.AreEqual(0, withSeam.Factory.CreateCalls);
        }

        [Test]
        public void test_composition_navigation_provider_is_the_same_instance_as_the_service()
        {
            NavCompositionRig rig = NewRig();

            rig.Start();

            Assert.IsNotNull(rig.Comp.NavigationProvider);
            Assert.AreSame(rig.Comp.NavigationService, rig.Comp.NavigationProvider);
        }

        // ---------------------------------------------------------------- bus

        [Test]
        public void test_composition_has_one_bus_and_a_spawn_raised_after_s6_reaches_the_service()
        {
            NavCompositionRig rig = NewRig();
            Assert.IsNull(rig.Comp.MobEvents, "the bus is built by Start, not before");

            rig.Start();
            MobEventBus bus = rig.Comp.MobEvents;
            Assert.IsNotNull(bus);
            bus.RaiseMobSpawned(SpawnArgs(rig.Comp, 901u));

            Assert.AreEqual(1, rig.Comp.NavigationService.ActiveCount);
        }

        [Test]
        public void test_composition_bus_raised_after_teardown_reaches_nothing_and_the_composition_drops_it()
        {
            NavCompositionRig rig = NewRig();
            rig.Start();
            var service = (ZoneNavigationService)rig.Comp.NavigationService;
            MobEventBus bus = rig.Comp.MobEvents;
            int createdBefore = rig.Factory.CreateCalls;
            rig.Teardown();

            using (var capture = new NavProblemCapture())
            {
                bus.RaiseMobSpawned(SpawnArgs(rig.Comp, 902u));

                Assert.AreEqual(0, service.ActiveCount);
                Assert.AreEqual(createdBefore, rig.Factory.CreateCalls);
                Assert.IsNull(rig.Comp.MobEvents, "the Dispose pass drops the bus reference");
                Assert.AreEqual(0, capture.Problems.Count, capture.Joined);
            }
        }

        // ---------------------------------------------------------------- step order

        [Test]
        public void test_composition_navigation_step_precedes_every_phase_three_system()
        {
            NavCompositionRig rig = NewRig();

            rig.Start();

            IList<IZoneSimulationStep> steps = rig.Comp.SimulationSteps;
            int nav = IndexOfStep(steps, nameof(NavigationPhaseStep));
            Assert.GreaterOrEqual(nav, 0);
            foreach (string later in new[] { "StatusEffectsService", "MovementService", "AutoAttackCombatSystem", "SkillCastStep" })
            {
                int index = IndexOfStep(steps, later);
                Assert.GreaterOrEqual(index, 0, later + " is not in the step list");
                Assert.Less(nav, index, "navigation step must precede " + later);
            }

            int wiringIndex = rig.Observed.IndexOf(nameof(ZoneInstancingWiring));
            Assert.AreEqual(wiringIndex + 1, rig.Observed.IndexOf(nameof(ZoneNavigationService)));
            Assert.Less(rig.Observed.IndexOf(nameof(ZoneNavigationService)), rig.Observed.IndexOf("StatusEffectsService"));
        }

        // ---------------------------------------------------------------- S6

        [Test]
        public void test_s6_valid_instance_creates_the_pool_and_startup_continues()
        {
            NavCompositionRig rig = NewRig();

            rig.Start();

            Assert.AreEqual(NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT, rig.Factory.CreateCalls);
            Assert.AreEqual(NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT, rig.Comp.NavigationService.AvailableCount);
            // The real backend routes S9 MarkActive to the Zone Instancing wiring, not the injected state owner; IsTickRunning is S9 done.
            Assert.IsTrue(rig.Comp.IsTickRunning);
        }

        [Test]
        public void test_s6_without_a_config_override_the_composition_config_gives_the_default_pool_and_is_destroyed()
        {
            int before = Resources.FindObjectsOfTypeAll<NavigationConfig>().Length;
            NavCompositionRig rig = NewRig(ownConfig: true);

            rig.Start();
            Assert.AreEqual(NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT, rig.Factory.CreateCalls);
            rig.Teardown();

            Assert.AreEqual(before, Resources.FindObjectsOfTypeAll<NavigationConfig>().Length);
        }

        [Test]
        public void test_s6_invalid_instance_fails_startup_and_teardown_runs_the_navigation_teardown()
        {
            NavCompositionRig rig = NewRig(validInstance: false);
            LogAssert.Expect(LogType.Error, new Regex("invalid NavMesh instance"));
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S6"));

            rig.Start();
            Assert.AreEqual(0, rig.Factory.CreateCalls);
            Assert.IsFalse(rig.Comp.IsTickRunning);
            var service = (ZoneNavigationService)rig.Comp.NavigationService;
            Assert.AreEqual(0, service.TeardownCallCount);
            Assert.DoesNotThrow(rig.Teardown);

            Assert.AreEqual(1, service.TeardownCallCount, "step 3 ran the navigation teardown exactly once");
            Assert.IsTrue(service.IsTornDown);
            CollectionAssert.Contains(rig.Log, "Quit:1", "S6 failure exits with StartupFailed (1)");

            // A second teardown request is harmless and does not reach the navigation again.
            Assert.DoesNotThrow(rig.Teardown);
            Assert.AreEqual(1, service.TeardownCallCount);
        }

        // ---------------------------------------------------------------- teardown

        [Test]
        public void test_teardown_halts_the_tick_before_the_navigation_teardown_removes_the_mesh()
        {
            NavCompositionRig rig = NewRig();
            rig.Start();

            rig.Teardown();

            int tickUnsubscribe = rig.Log.IndexOf("TickUnsubscribe");
            int removed = rig.Log.IndexOf("Remove");
            Assert.GreaterOrEqual(tickUnsubscribe, 0);
            Assert.Greater(removed, tickUnsubscribe, "NavMesh removal must follow the tick unsubscribe");
            Assert.AreEqual(1, rig.Ops.RemoveCalls);

            // Pool creation may also log a disable; only entries after the unsubscribe belong to the teardown.
            int firstTeardownDisable = rig.Log.FindIndex(tickUnsubscribe + 1, e => e.EndsWith(".Enabled=False", StringComparison.Ordinal));
            Assert.Greater(firstTeardownDisable, tickUnsubscribe, "agents are disabled after the tick unsubscribe");
            Assert.Less(firstTeardownDisable, removed, "agents are disabled before the NavMesh removal");
        }

        [Test]
        public void test_teardown_step_three_failure_is_logged_and_the_pool_roots_are_still_destroyed()
        {
            NavCompositionRig rig = NewRig(validInstance: false, factoryOverride: false);
            LogAssert.Expect(LogType.Error, new Regex("invalid NavMesh instance"));
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S6"));
            rig.Start();
            var service = (ZoneNavigationService)rig.Comp.NavigationService;
            rig.Comp.UnityNavFactory.Create(POOL_ROOT_NAME);
            GameObject root = GameObject.Find(POOL_ROOT_NAME);
            Assert.IsNotNull(root);
            rig.Ops.Add(default(UnityEngine.AI.NavMeshDataInstance));
            rig.Ops.ThrowOnRemove = true;
            LogAssert.Expect(LogType.Error, new Regex("teardown step 3 failed: InvalidOperationException"));

            try
            {
                Assert.DoesNotThrow(rig.Teardown);

                Assert.AreEqual(1, service.TeardownCallCount, "the navigation teardown ran (and threw) before the roots were destroyed");
                Assert.IsTrue(GameObject.Find(POOL_ROOT_NAME) == null, "the pool root is destroyed even though Teardown threw");
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        public void test_teardown_step_three_destroys_the_pool_roots_of_the_real_factory()
        {
            NavCompositionRig rig = NewRig(validInstance: false, factoryOverride: false);
            LogAssert.Expect(LogType.Error, new Regex("invalid NavMesh instance"));
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S6"));
            rig.Start();
            Assert.IsNotNull(rig.Comp.UnityNavFactory, "no override: the composition keeps the concrete factory");
            rig.Comp.UnityNavFactory.Create(POOL_ROOT_NAME);
            GameObject root = GameObject.Find(POOL_ROOT_NAME);
            Assert.IsNotNull(root);

            try
            {
                rig.Teardown();

                Assert.IsTrue(GameObject.Find(POOL_ROOT_NAME) == null, "step 3 must destroy the pool root");
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        public void test_teardown_with_a_factory_override_has_no_concrete_factory_to_destroy()
        {
            NavCompositionRig rig = NewRig();
            rig.Start();

            Assert.IsNull(rig.Comp.UnityNavFactory);
            Assert.DoesNotThrow(rig.Teardown);
        }

        // ---------------------------------------------------------------- probe

        [Test]
        public void test_probe_flag_absent_requests_nothing()
        {
            Assert.IsFalse(NavFrameRateProbe.IsRequested(new[] { "-batchmode", "-zoneTemplate", "StartingZone" }));
            Assert.IsFalse(NavFrameRateProbe.IsRequested(Array.Empty<string>()));
            Assert.IsFalse(NavFrameRateProbe.IsRequested(null));
            Assert.IsTrue(NavFrameRateProbe.IsRequested(new[] { "-batchmode", NavFrameRateProbe.FLAG }));
        }

        [Test]
        public void test_probe_waits_until_the_zone_is_active()
        {
            NavCompositionRig rig = NewRig();
            var probe = new NavFrameRateProbe(rig.Comp, () => true);

            probe.Tick(0f);
            probe.Tick(1f);

            Assert.IsFalse(probe.IsRunning);
            Assert.IsFalse(probe.IsFinished);
        }

        [Test]
        public void test_probe_in_a_non_debug_build_warns_once_and_spawns_nothing()
        {
            NavCompositionRig rig = NewRig();
            rig.Start();
            var probe = new NavFrameRateProbe(rig.Comp, () => false);
            LogAssert.Expect(LogType.Warning, new Regex("not a debug build"));

            using (var capture = new NavProblemCapture())
            {
                probe.Tick(0f);
                probe.Tick(0.05f);
                probe.Tick(1f);

                probe.Tick(1.05f);

                Assert.IsTrue(probe.IsFinished);
                Assert.AreEqual(0, rig.Comp.NavigationService.ActiveCount);
                Assert.AreEqual(1, capture.Problems.Count, capture.Joined);
            }
        }

        [Test]
        public void test_probe_without_composition_navigation_warns_once_and_does_nothing()
        {
            NavCompositionRig rig = NewRig(seam: new SuNav(new List<string>(), new SuPlan()));
            rig.Start();
            var probe = new NavFrameRateProbe(rig.Comp, () => true);
            LogAssert.Expect(LogType.Warning, new Regex("built no navigation"));

            using (var capture = new NavProblemCapture())
            {
                probe.Tick(0f);
                probe.Tick(1f);
                probe.Tick(1.05f);

                Assert.IsTrue(probe.IsFinished);
                Assert.IsNull(rig.Comp.NavigationService);
                Assert.AreEqual(1, capture.Problems.Count, capture.Joined);
            }
        }

        [Test]
        public void test_probe_spawns_one_mob_requests_the_destination_logs_and_kills_it_after_ten_seconds()
        {
            NavCompositionRig rig = NewRig();
            rig.Start();
            var probe = new NavFrameRateProbe(rig.Comp, () => true);
            using (var capture = new WiLogCapture())
            {
                probe.Tick(0f);

                Assert.IsTrue(probe.IsRunning);
                Assert.AreEqual(1, rig.Comp.NavigationService.ActiveCount);
                var entity = new EntityID(NavFrameRateProbe.PROBE_ENTITY_RAW);
                Assert.IsTrue(rig.Comp.NavigationService.HasLatestRequest(entity, out Vector3 target));
                Assert.AreEqual(new Vector3(NavFrameRateProbe.DESTINATION_X, 0f, 0f), target);

                FakeNavAgent agent = rig.Factory.Created.Find(a => a.EnabledValue);
                Assert.IsNotNull(agent);

                // 20 Hz with integer frames: frame 10 (t = 0.5 s) ends the warm-up and is the uncounted baseline; frames
                // 11..30 are the first interval (20 frames), and so on; frame 210 closes the tenth interval and the run.
                const float STEP_METRES = 0.175f;
                const int LAST_FRAME = 230;
                for (int i = 1; i <= LAST_FRAME; i++)
                {
                    agent.NextPosition = new Vector3(STEP_METRES * i, 0f, 0f);
                    probe.Tick(i / 20f);
                }

                Assert.IsTrue(probe.IsFinished);
                Assert.AreEqual(0, rig.Comp.NavigationService.ActiveCount, "OnMobDied releases the probe agent");
                string tPrefix = NavFrameRateProbe.LOG_PREFIX + "t=";
                List<string> intervals = capture.Lines.FindAll(l => l.StartsWith(tPrefix, StringComparison.Ordinal));
                Assert.AreEqual(10, intervals.Count);
                foreach (string line in intervals)
                {
                    StringAssert.Contains(" frames=20 movingFrames=20 meanStep=0.1750 ", line);
                    StringAssert.Contains(" totalAdvance=", line);
                }

                Assert.AreEqual(NavFrameRateProbe.DONE_LINE, capture.Lines[capture.Lines.Count - 1]);
                Assert.AreEqual(1, capture.Lines.FindAll(l => l == NavFrameRateProbe.DONE_LINE).Count);
            }
        }

        [Test]
        public void test_probe_teardown_mid_run_warns_zone_left_active_and_releases_the_mob()
        {
            NavCompositionRig rig = NewRig();
            rig.Start();
            var service = (ZoneNavigationService)rig.Comp.NavigationService;
            var probe = new NavFrameRateProbe(rig.Comp, () => true);
            using (var capture = new WiLogCapture())
            {
                probe.Tick(0f);
                probe.Tick(0.05f);
                Assert.IsTrue(probe.IsRunning);
                rig.Teardown();
                LogAssert.Expect(LogType.Warning, new Regex("zone left Active"));

                probe.Tick(0.1f);
                probe.Tick(0.15f);

                Assert.IsTrue(probe.IsFinished);
                Assert.AreEqual(0, service.ActiveCount);
                CollectionAssert.DoesNotContain(capture.Lines, NavFrameRateProbe.DONE_LINE);
            }
        }

        [Test]
        public void test_probe_agent_not_claimed_warns_and_finishes()
        {
            NavCompositionRig rig = NewRig();
            rig.Start();
            int pool = NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT;
            for (uint i = 0; i < (uint)pool; i++)
            {
                rig.Comp.MobEvents.RaiseMobSpawned(SpawnArgs(rig.Comp, 1000u + i));
            }

            Assert.AreEqual(0, rig.Comp.NavigationService.AvailableCount);
            var probe = new NavFrameRateProbe(rig.Comp, () => true);
            LogAssert.Expect(LogType.Error, new Regex("Agent pool exhausted"));
            LogAssert.Expect(LogType.Warning, new Regex("not claimed"));

            probe.Tick(0f);
            probe.Tick(0.05f);

            Assert.IsTrue(probe.IsFinished);
        }

        [Test]
        public void test_probe_agent_unregistered_mid_run_warns_and_finishes()
        {
            NavCompositionRig rig = NewRig();
            rig.Start();
            var probe = new NavFrameRateProbe(rig.Comp, () => true);
            probe.Tick(0f);
            Assert.IsTrue(probe.IsRunning);
            rig.Comp.MobEvents.RaiseMobDied(new MobDiedArgs(rig.Comp.ZoneId, new EntityID(NavFrameRateProbe.PROBE_ENTITY_RAW)));
            LogAssert.Expect(LogType.Warning, new Regex("no longer registered"));

            using (var capture = new NavProblemCapture())
            {
                probe.Tick(0.05f);
                probe.Tick(0.1f);

                Assert.IsTrue(probe.IsFinished);
                Assert.AreEqual(1, capture.Problems.Count, capture.Joined);
            }
        }
    }
}
