using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.MobSpawning;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.EnemyAI;
using IronGrind.Tests.EditMode.Navigation;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.MobSpawning
{
    /// <summary>
    /// Mob Spawning Story 006: zone composition wiring. Build at S6 over the definition slot (AC-1), the teardown
    /// forwarder (AC-2), the T-2 sweep at S9 (AC-3), the phase 3 position (AC-4), same-tick corpse removal with Enemy AI
    /// (AC-5), respawn (AC-6) and close (AC-7). Rig: <see cref="NavCompositionRig"/> (composition-built navigation, so
    /// Enemy AI exists) with a definition slot holding a test table and a spawn point validator over a
    /// <see cref="FakeMeshQuery"/> that always hits (EditMode has no NavMesh).
    /// </summary>
    [TestFixture]
    internal sealed class MobSpawning_Composition_Tests
    {
        private static readonly Vector3 BOUNDS_MIN = new Vector3(-1000f, -100f, -1000f);
        private static readonly Vector3 BOUNDS_MAX = new Vector3(1000f, 100f, 1000f);
        private static readonly EntityID KILLER = new EntityID(999_999u);

        private readonly List<NavCompositionRig> _rigs = new List<NavCompositionRig>();
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (NavCompositionRig rig in _rigs)
            {
                rig.Destroy();
            }

            _rigs.Clear();
            foreach (Object asset in _assets)
            {
                Object.DestroyImmediate(asset);
            }

            _assets.Clear();
        }

        // Entry i has SpawnPointID i + 1, the fixture mob type (Common) and position (i, 0, 0).
        private static SpawnPointEntry[] Entries(int n)
        {
            var list = new SpawnPointEntry[n];
            for (int i = 0; i < n; i++)
            {
                list[i] = new SpawnPointEntry((uint)(i + 1), EnemyAICompositionFixtures.MobType,
                    new Vector3(i, 0f, 0f), 0);
            }

            return list;
        }

        private ZoneDefinitionSlot Slot(SpawnPointEntry[] entries)
        {
            var definition = ScriptableObject.CreateInstance<ZoneDefinition>();
            _assets.Add(definition);
            definition.Initialize(ZoneTemplateID.StartingZone, BOUNDS_MIN, BOUNDS_MAX, Vector3.zero, false, null);
            var table = ScriptableObject.CreateInstance<ZoneSpawnTable>();
            _assets.Add(table);
            table.SetForTests(ZoneTemplateID.StartingZone, entries);
            definition.SetSpawnTableForTests(table);
            var slot = new ZoneDefinitionSlot();
            slot.Set(definition);
            return slot;
        }

        // A rig with a definition slot holding `points` entries and a validator that accepts every point.
        private NavCompositionRig NewRig(int points)
        {
            var rig = new NavCompositionRig(definitionSlot: Slot(Entries(points)));
            _rigs.Add(rig);
            rig.Comp.SpawnPointValidatorOverride = new SpawnPointValidator(new FakeMeshQuery(), rig.Config);
            return rig;
        }

        private NavCompositionRig StartedRig(int points)
        {
            NavCompositionRig rig = NewRig(points);
            rig.Start();
            Assert.IsTrue(rig.Comp.IsTickRunning, "startup must reach the tick");
            return rig;
        }

        private static void Ticks(NavCompositionRig rig, int count)
        {
            for (int i = 0; i < count; i++)
            {
                rig.Net.FireTick();
            }
        }

        private static int Linger(NavCompositionRig rig) => rig.Comp.EnemyAIConfigInUse.DeathLingerTicks;

        private static int CommonDelay() => MobSpawningConfigValues.Defaults.RespawnDelayCommonTicks;

        // ---- AC-1 / QA 1: build at S6 ----

        [Test]
        public void test_mobSpawning_composition_s6_builds_the_spawner_and_binds_the_forwarder_and_the_step()
        {
            NavCompositionRig rig = StartedRig(3);

            MobSpawningService spawner = rig.Comp.MobSpawner;
            Assert.IsNotNull(spawner);
            Assert.AreEqual(3, spawner.PointCount);
            Assert.AreSame(spawner, rig.Comp.MobSpawnTimers.Spawner);
            Assert.AreSame(spawner, rig.Comp.MobSpawningStep.Spawner);
        }

        [Test]
        public void test_mobSpawning_composition_151_entries_fail_s6_and_build_no_spawner()
        {
            NavCompositionRig rig = NewRig(ZoneSlotPool.MAX_MOBS_PER_ZONE + 1);
            LogAssert.Expect(LogType.Error, new Regex(ZoneSpawnTableValidator.OVER_CAPACITY_KEY));
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S6"));

            rig.Start();

            Assert.IsFalse(rig.Comp.IsTickRunning);
            Assert.IsNull(rig.Comp.MobSpawner);
            Assert.IsNull(rig.Comp.MobSpawnTimers.Spawner);
            Assert.IsNull(rig.Comp.MobSpawningStep.Spawner);
            Assert.AreEqual(0, rig.Comp.ZoneInstancing.MobRoster.GetMobCount(rig.Comp.ZoneId), "no roster spawns");
        }

        [Test]
        public void test_mobSpawning_composition_without_a_definition_slot_builds_no_spawner_and_logs_nothing()
        {
            var rig = new NavCompositionRig();
            _rigs.Add(rig);
            using (var capture = new NavProblemCapture())
            {
                rig.Start();

                Assert.IsTrue(rig.Comp.IsTickRunning);
                Assert.IsNull(rig.Comp.MobSpawner);
                Assert.IsNotNull(rig.Comp.MobSpawningStep, "the step is listed anyway");
                Assert.IsFalse(capture.Joined.Contains("[MobSpawning]"), capture.Joined);
            }
        }

        [Test]
        public void test_mobSpawning_composition_spawner_linger_is_the_enemy_ai_linger()
        {
            NavCompositionRig rig = StartedRig(1);
            EntityID mob = rig.Comp.MobSpawner.GetPoint(0).CurrentEntityID;
            rig.Comp.AutoAttackCombat.ReportKill(KILLER, mob, Vector3.zero);

            Assert.AreEqual(Linger(rig), rig.Comp.MobSpawner.GetPoint(0).TimerTick,
                "the spawner's linger countdown starts at Enemy AI's DEATH_LINGER_TICKS");
        }

        // ---- AC-2 / QA 2: teardown seam ----

        [Test]
        public void test_mobSpawning_composition_wiring_timers_are_the_forwarder_bound_to_the_spawner()
        {
            NavCompositionRig rig = StartedRig(2);

            IZoneMobSpawnTimers timers = rig.Comp.ZoneInstancing.MobSpawnTimers;

            Assert.AreSame(rig.Comp.MobSpawnTimers, timers);
            Assert.IsNotInstanceOf<LoggingZoneMobSpawnTimers>(timers);
            Assert.AreSame(rig.Comp.MobSpawner, rig.Comp.MobSpawnTimers.Spawner);
        }

        [Test]
        public void test_mobSpawning_composition_close_step_1_cancels_the_spawner()
        {
            NavCompositionRig rig = StartedRig(2);
            MobSpawningService spawner = rig.Comp.MobSpawner;

            rig.Teardown();

            Assert.IsTrue(spawner.IsCancelled);
            Assert.IsNull(rig.Comp.MobSpawner, "the Dispose pass drops the spawner");
        }

        [Test]
        public void test_mobSpawning_composition_close_after_a_failed_s6_does_nothing_and_logs_nothing()
        {
            NavCompositionRig rig = NewRig(ZoneSlotPool.MAX_MOBS_PER_ZONE + 1);
            LogAssert.Expect(LogType.Error, new Regex(ZoneSpawnTableValidator.OVER_CAPACITY_KEY));
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S6"));
            rig.Start();

            using (var capture = new NavProblemCapture())
            {
                rig.Teardown();

                Assert.IsNull(rig.Comp.MobSpawnTimers.Spawner);
                Assert.IsFalse(capture.Joined.Contains("[MobSpawning]"), capture.Joined);
            }
        }

        // ---- AC-3 / QA 3: T-2 ----

        [Test]
        public void test_mobSpawning_composition_t2_populates_every_point_with_store_entry_and_dormant_controller()
        {
            NavCompositionRig rig = StartedRig(5);
            MobSpawningService spawner = rig.Comp.MobSpawner;

            Assert.AreEqual(5, rig.Comp.ZoneInstancing.MobRoster.GetMobCount(rig.Comp.ZoneId));
            Assert.AreEqual(5, rig.Comp.MobStore.Count);
            Assert.AreEqual(5, rig.Comp.EnemyAI.Count);
            for (int i = 0; i < 5; i++)
            {
                SpawnPoint point = spawner.GetPoint(i);
                Assert.AreEqual(SpawnPointState.Live, point.State, "point " + i);
                Assert.IsTrue(rig.Comp.MobStore.TryGet(point.CurrentEntityID, out _), "store entry " + i);
                Assert.IsTrue(rig.Comp.EnemyAI.TryGetMobState(point.CurrentEntityID, out MobState state), "controller " + i);
                Assert.AreEqual(MobState.Dormant, state, "point " + i);
            }
        }

        // Story 007 AC-6 / QA 5: the T-2 info line, the smoke check's observable.
        [Test]
        public void test_mobSpawning_composition_t2_logs_the_live_and_deferred_counts_once()
        {
            NavCompositionRig rig = NewRig(5);
            var lines = new List<string>();
            Application.LogCallback record = (condition, stack, type) =>
            {
                if (condition.StartsWith(ZoneServerComposition.MOB_SPAWNING_POPULATED_LOG_PREFIX))
                {
                    lines.Add(type + " " + condition);
                }
            };

            Application.logMessageReceived += record;
            try
            {
                rig.Start();
                Ticks(rig, 3);
            }
            finally
            {
                Application.logMessageReceived -= record;
            }

            Assert.AreEqual(1, lines.Count, string.Join(" | ", lines));
            StringAssert.IsMatch(@"^Log \[MobSpawning\] zone .+: 5 of 5 points Live, 0 Deferred$", lines[0]);
        }

        // ---- AC-4 / QA 4: phase order ----

        [Test]
        public void test_mobSpawning_composition_step_follows_navigation_and_precedes_every_game_system()
        {
            NavCompositionRig rig = StartedRig(1);
            IList<IZoneSimulationStep> steps = rig.Comp.SimulationSteps;

            int spawning = steps.IndexOf(rig.Comp.MobSpawningStep);
            int navigation = -1;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] is NavigationPhaseStep)
                {
                    navigation = i;
                }
            }

            Assert.AreEqual(navigation + 1, spawning, "right after the navigation step");
            Assert.Less(spawning, steps.IndexOf(rig.Comp.StatusEffects));
            Assert.Less(spawning, steps.IndexOf(rig.Comp.EnemyAI));
            Assert.Less(spawning, steps.IndexOf(rig.Comp.AutoAttackCombat));
        }

        // ---- AC-5 / QA 5: same-tick corpse removal ----

        [Test]
        public void test_mobSpawning_composition_killed_mob_slot_and_controller_go_on_the_same_tick()
        {
            NavCompositionRig rig = StartedRig(1);
            ZoneMobRoster roster = rig.Comp.ZoneInstancing.MobRoster;
            EntityID mob = rig.Comp.MobSpawner.GetPoint(0).CurrentEntityID;
            Ticks(rig, 1);                                                   // tick T: Enemy AI ran
            rig.Comp.AutoAttackCombat.ReportKill(KILLER, mob, Vector3.zero); // Combat's kill on T

            Ticks(rig, Linger(rig) - 1);                                     // up to T + linger - 1

            Assert.IsTrue(roster.TryGetMob(mob, out _, out _), "slot still held on T + linger - 1");
            Assert.IsTrue(rig.Comp.EnemyAI.TryGetMobState(mob, out MobState state), "controller still there");
            Assert.AreEqual(MobState.Dead, state);
            Assert.IsTrue(rig.Comp.MobStore.TryGet(mob, out _), "store entry still there");

            Ticks(rig, 1);                                                   // T + linger

            Assert.IsFalse(roster.TryGetMob(mob, out _, out _), "slot released on T + linger");
            Assert.IsFalse(rig.Comp.EnemyAI.TryGetMobState(mob, out _), "controller removed on T + linger");
            Assert.IsFalse(rig.Comp.MobStore.TryGet(mob, out _), "store entry removed on T + linger");
            Assert.AreEqual(SpawnPointState.Respawning, rig.Comp.MobSpawner.GetPoint(0).State);
        }

        // ---- AC-6 / QA 6: respawn ----

        [Test]
        public void test_mobSpawning_composition_point_respawns_after_the_common_delay_with_a_new_id()
        {
            NavCompositionRig rig = StartedRig(1);
            MobSpawningService spawner = rig.Comp.MobSpawner;
            EntityID first = spawner.GetPoint(0).CurrentEntityID;
            Ticks(rig, 1);
            rig.Comp.AutoAttackCombat.ReportKill(KILLER, first, Vector3.zero);
            Ticks(rig, Linger(rig));                                         // corpse removed

            Ticks(rig, CommonDelay() - 1);

            Assert.AreEqual(SpawnPointState.Respawning, spawner.GetPoint(0).State, "not yet on delay - 1");

            Ticks(rig, 1);

            SpawnPoint point = spawner.GetPoint(0);
            Assert.AreEqual(SpawnPointState.Live, point.State);
            Assert.AreNotEqual(first, point.CurrentEntityID);
            Assert.AreNotEqual(EntityID.Invalid, point.CurrentEntityID);
            Assert.IsTrue(rig.Comp.MobStore.TryGet(point.CurrentEntityID, out MobRuntimeEntry entry));
            Assert.AreEqual(EnemyAICompositionFixtures.KILL_XP, entry.Variant.EffectiveKillXP, "store seeded from the definition");
            Assert.IsTrue(rig.Comp.EnemyAI.TryGetMobState(point.CurrentEntityID, out MobState state));
            Assert.AreEqual(MobState.Dormant, state);
        }

        // ---- AC-7 / QA 7: close ----

        // Teardown has no ticks of its own, so the only paths to the roster after step 1 are the spawner's MobSpawned
        // raise (TryAddMob) and its Dying countdown (RemoveMob, which Tick only reaches through a point's EntityID).
        // The test arms both (a Dying point mid-countdown, the deferred retry) and checks that the close raises no bus
        // event, cancels every point with no entity left to remove, and that later ticks of the spawner stay silent.
        [Test]
        public void test_mobSpawning_composition_after_close_no_spawn_no_death_event_and_no_entity_left()
        {
            NavCompositionRig rig = StartedRig(3);
            MobSpawningService spawner = rig.Comp.MobSpawner;
            MobEventBus bus = rig.Comp.MobEvents;
            ZoneMobRoster roster = rig.Comp.ZoneInstancing.MobRoster;
            EntityID dying = spawner.GetPoint(1).CurrentEntityID;
            Ticks(rig, 1);
            rig.Comp.AutoAttackCombat.ReportKill(KILLER, dying, Vector3.zero);
            Ticks(rig, 2);
            Assert.AreEqual(SpawnPointState.Dying, spawner.GetPoint(1).State, "armed: a RemoveMob is pending");
            Assert.Greater(spawner.GetPoint(1).TimerTick, 0);
            Assert.AreEqual(3, roster.GetMobCount(rig.Comp.ZoneId), "armed: the corpse still holds its slot");
            var events = new List<string>();
            bus.OnMobSpawned += args => events.Add("MobSpawned " + args.Entity);
            bus.OnMobDied += args => events.Add("MobDied " + args.Entity);

            rig.Teardown();

            Assert.IsTrue(spawner.IsCancelled, "step 1 reached the spawner");
            CollectionAssert.IsEmpty(events, "no bus event during the close");
            for (int i = 0; i < spawner.PointCount; i++)
            {
                SpawnPoint point = spawner.GetPoint(i);
                Assert.AreEqual(SpawnPointState.Cancelled, point.State, "point " + i);
                Assert.AreEqual(EntityID.Invalid, point.CurrentEntityID, "point " + i + " has no entity to remove");
            }

            for (int i = 0; i < Linger(rig) + CommonDelay() + 10; i++)
            {
                spawner.Tick();
            }

            spawner.PopulateOnActive();
            CollectionAssert.IsEmpty(events, "no MobSpawned or MobDied after the close");
        }

        // ---- Code review additions (2026-11-16) ----

        // G1: during the S6 scene wait S5 has run (forwarder and step exist) but the spawner is not built yet.
        [Test]
        public void test_mobSpawning_composition_spawner_is_null_until_s6_builds_it()
        {
            NavCompositionRig rig = NewRig(2);
            int polls = 0;
            rig.Scene.OnPoll = () =>
            {
                polls++;
                Assert.IsNotNull(rig.Comp.MobSpawnTimers, "forwarder built at S5");
                Assert.IsNotNull(rig.Comp.MobSpawningStep, "step built at S5");
                Assert.IsNull(rig.Comp.MobSpawner, "no spawner before S6 part 2");
                Assert.IsNull(rig.Comp.MobSpawnTimers.Spawner);
                Assert.IsNull(rig.Comp.MobSpawningStep.Spawner);
            };

            rig.Start();

            Assert.Greater(polls, 0, "the S6 wait was observed");
            Assert.IsNotNull(rig.Comp.MobSpawner);
        }

        // G3 / G7: T-2 raises exactly one MobSpawned per point, all before the tick is subscribed; a tick adds none.
        [Test]
        public void test_mobSpawning_composition_t2_spawns_once_per_point_before_the_tick_subscription()
        {
            NavCompositionRig rig = NewRig(5);
            int spawned = 0;
            int spawnedAfterSubscribe = 0;
            bool subscribed = false;
            rig.Scene.OnPoll = () =>
            {
                if (subscribed)
                {
                    return;
                }

                subscribed = true;
                rig.Comp.MobEvents.OnMobSpawned += _ =>
                {
                    spawned++;
                    if (rig.Net.SubscribedHandler != null)
                    {
                        spawnedAfterSubscribe++;
                    }
                };
            };

            rig.Start();
            Ticks(rig, 1);

            Assert.AreEqual(5, spawned, "one MobSpawned per point; no second sweep");
            Assert.AreEqual(0, spawnedAfterSubscribe, "T-2 ran before SubscribeTick");
            Assert.AreEqual(5, rig.Comp.MobStore.Count);
            Assert.AreEqual(5, rig.Comp.ZoneInstancing.MobRoster.GetMobCount(rig.Comp.ZoneId));
        }

        // G5 / G6: after a failed S6, navigation teardown step 3 still runs, and the forwarder logs nothing at all.
        [Test]
        public void test_mobSpawning_composition_failed_s6_still_runs_navigation_teardown_and_forwarder_is_silent()
        {
            NavCompositionRig rig = NewRig(ZoneSlotPool.MAX_MOBS_PER_ZONE + 1);
            LogAssert.Expect(LogType.Error, new Regex(ZoneSpawnTableValidator.OVER_CAPACITY_KEY));
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S6"));
            rig.Start();
            int logs = 0;
            Application.LogCallback count = (condition, stack, type) => logs++;

            Application.logMessageReceived += count;
            try
            {
                rig.Comp.MobSpawnTimers.CancelAllSpawnTimers(rig.Comp.ZoneId);
            }
            finally
            {
                Application.logMessageReceived -= count;
            }

            rig.Teardown();

            Assert.AreEqual(0, logs, "the forwarder with no spawner logs nothing, not even an info line");
            Assert.AreEqual(1, rig.Ops.RemoveCalls, "navigation teardown (step 3) removed the NavMesh instance");
        }

        // G8: a navigation seam with the validator override still builds the spawner (no Enemy AI there).
        [Test]
        public void test_mobSpawning_composition_navigation_seam_with_validator_override_builds_the_spawner()
        {
            var rig = new NavCompositionRig(seam: new SuNav(new List<string>(), new SuPlan()),
                definitionSlot: Slot(Entries(2)));
            _rigs.Add(rig);
            rig.Comp.SpawnPointValidatorOverride = new SpawnPointValidator(new FakeMeshQuery(), rig.Config);

            rig.Start();

            Assert.IsTrue(rig.Comp.IsTickRunning);
            Assert.IsNotNull(rig.Comp.MobSpawner);
            Assert.IsNull(rig.Comp.EnemyAI);
            Assert.AreEqual(2, rig.Comp.MobStore.Count);
            Assert.AreEqual(2, rig.Comp.ZoneInstancing.MobRoster.GetMobCount(rig.Comp.ZoneId));
        }

        // G8: a navigation seam without the override has no validator: no spawner, no log, the zone still starts.
        [Test]
        public void test_mobSpawning_composition_navigation_seam_without_override_builds_no_spawner_and_logs_nothing()
        {
            var rig = new NavCompositionRig(seam: new SuNav(new List<string>(), new SuPlan()),
                definitionSlot: Slot(Entries(2)));
            _rigs.Add(rig);
            using (var capture = new NavProblemCapture())
            {
                rig.Start();

                Assert.IsTrue(rig.Comp.IsTickRunning);
                Assert.IsNull(rig.Comp.MobSpawner);
                Assert.AreEqual(0, rig.Comp.ZoneInstancing.MobRoster.GetMobCount(rig.Comp.ZoneId));
                Assert.IsFalse(capture.Joined.Contains("[MobSpawning]"), capture.Joined);
            }
        }

        // G8: every point off the NavMesh aborts the load (CR-NAV-13): S-fail at S6, no spawner, no roster call.
        [Test]
        public void test_mobSpawning_composition_all_points_off_the_navmesh_fail_s6()
        {
            var rig = new NavCompositionRig(definitionSlot: Slot(Entries(2)));
            _rigs.Add(rig);
            rig.Comp.SpawnPointValidatorOverride =
                new SpawnPointValidator(new FakeMeshQuery { Hits = _ => false }, rig.Config);
            LogAssert.Expect(LogType.Error, new Regex("Spawn point off the NavMesh"));
            LogAssert.Expect(LogType.Error, new Regex("Spawn point off the NavMesh"));
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL"));
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S6"));

            rig.Start();

            Assert.IsFalse(rig.Comp.IsTickRunning);
            Assert.IsNull(rig.Comp.MobSpawner);
            Assert.AreEqual(0, rig.Comp.ZoneInstancing.MobRoster.GetMobCount(rig.Comp.ZoneId));
        }

        // G8: a slot the scene loader never filled is the loader's missing-definition failure, not an exception.
        [Test]
        public void test_mobSpawning_composition_empty_definition_slot_fails_s6_with_the_loader_error()
        {
            var rig = new NavCompositionRig(definitionSlot: new ZoneDefinitionSlot());
            _rigs.Add(rig);
            rig.Comp.SpawnPointValidatorOverride = new SpawnPointValidator(new FakeMeshQuery(), rig.Config);
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(MobSpawningZoneLoader.NO_DEFINITION_ERROR)));
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S6: StepReturnedFalse"));

            rig.Start();

            Assert.IsFalse(rig.Comp.IsTickRunning);
            Assert.IsNull(rig.Comp.MobSpawner);
        }

        // G8: MobSpawningConfigOverride is what the spawner uses (a Common delay 5 ticks above the default).
        [Test]
        public void test_mobSpawning_composition_config_override_sets_the_respawn_delay()
        {
            MobSpawningConfigValues d = MobSpawningConfigValues.Defaults;
            int delay = d.RespawnDelayCommonTicks + 5;
            NavCompositionRig rig = NewRig(1);
            rig.Comp.MobSpawningConfigOverride = new MobSpawningConfigValues(delay, d.RespawnDelayUncommonTicks,
                d.RespawnDelayRareTicks, d.RespawnDelayEliteTicks, d.SpawnDensityRetryIntervalTicks);
            rig.Start();
            MobSpawningService spawner = rig.Comp.MobSpawner;
            Ticks(rig, 1);
            rig.Comp.AutoAttackCombat.ReportKill(KILLER, spawner.GetPoint(0).CurrentEntityID, Vector3.zero);
            Ticks(rig, Linger(rig));

            Ticks(rig, delay - 1);
            Assert.AreEqual(SpawnPointState.Respawning, spawner.GetPoint(0).State, "not at the override delay - 1");

            Ticks(rig, 1);
            Assert.AreEqual(SpawnPointState.Live, spawner.GetPoint(0).State, "Live at the override delay");
        }
    }
}
