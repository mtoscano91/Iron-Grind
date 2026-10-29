using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>
    /// EditMode tests for Status Effects Story 009 - tick pipeline wiring, mob targets and the allocation budget
    /// (CR-SE-5, CR-SE-12, CR-SE-15; AC-SE-20, AC-SE-24, AC-SE-25). The composition tests use <see cref="WiRig"/>
    /// (real backend over fakes, no database I/O); the allocation and isolation tests build the pieces directly.
    /// </summary>
    [TestFixture]
    internal sealed class StatusEffects_PipelineWiring_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int BASE_ATTACK = 20;
        private const int BASE_DEFENSE = 10;
        private const int BASE_MAX_HP = 400;
        private const int BASE_MAX_MP = 80;
        private const float START_HP = 100f;
        private const float START_MP = 40f;
        private const float KILL_HP = 1f;
        private const float HEAL_AMOUNT = 50f;
        private const float OVERKILL = 1000f;
        private const float FLAT_AP_SMALL = 10f;
        private const float FLAT_AP_BIG = 100f;
        private const float PCT_NONE = 0f;
        private const uint LONG_DURATION = 100u;
        private const uint SHORT_DURATION = 2u;
        private const int ENTITY_COUNT = 200;
        private const int EFFECTS_PER_ENTITY = 8;
        private const int EXPIRING_PER_ENTITY = 3;
        private const uint SOME_TICK = 5u;
        private const float DELTA = 0.05f;

        private static readonly System.Text.RegularExpressions.Regex PRNG_LINE =
            new System.Text.RegularExpressions.Regex(@"^\[Random\] PRNG seed: -?\d+$");

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 0u;
        }

        private sealed class NullPoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        /// <summary>Stand-in for the Auto-Attack Combat entry: records the entity's AttackPower each time it runs.</summary>
        private sealed class AttackPowerRecorder : IZoneSimulationStep
        {
            private readonly IronGrind.CharacterStats.CharacterStats _stats;
            private readonly EntityID _entity;
            public readonly List<int> Seen = new List<int>();

            public AttackPowerRecorder(IronGrind.CharacterStats.CharacterStats stats, EntityID entity)
            {
                _stats = stats;
                _entity = entity;
            }

            public void Run(uint currentTick, float deltaTime)
            {
                Seen.Add(_stats.GetEffectiveStat(_entity, StatID.AttackPower));
            }
        }

        private sealed class CountingStep : IZoneSimulationStep
        {
            public int Runs;
            public void Run(uint currentTick, float deltaTime) { Runs++; }
        }

        /// <summary>Resource sink that deliberately allocates on every regen call (proves the measurement works).</summary>
        private sealed class AllocatingResourceSink : IStatResourceSink
        {
            private readonly IStatResourceSink _inner;
            public object Sink;

            public AllocatingResourceSink(IStatResourceSink inner) { _inner = inner; }

            public void ApplyRegen(EntityID entityId, float amount)
            {
                Sink = new object[16];
                _inner.ApplyRegen(entityId, amount);
            }

            public void ApplyManaRegen(EntityID entityId, float amount) { _inner.ApplyManaRegen(entityId, amount); }

            public void ClampHPToMax(EntityID entityId) { _inner.ClampHPToMax(entityId); }

            public void ClampMPToMax(EntityID entityId) { _inner.ClampMPToMax(entityId); }
        }

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static WiRig StartRig()
        {
            var rig = new WiRig();
            LogAssert.Expect(UnityEngine.LogType.Log, PRNG_LINE);
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            return rig;
        }

        private static ZoneSlotPool NewPool()
        {
            return new ZoneSlotPool(Zone, new FakeTicks(), new NullPoolListener());
        }

        private static EntityID AllocPlayer(ZoneSlotPool pool, uint n, out byte slot)
        {
            Assert.IsTrue(pool.TryAllocatePlayer(pool.Zone, new CharacterID(n), new AccountID(n + 5000u), n + 9000u,
                out slot, out EntityID id));
            return id;
        }

        private static EntityID AllocMob(ZoneSlotPool pool, out ushort mobSlot)
        {
            Assert.IsTrue(pool.TryAllocateMob(pool.Zone, out mobSlot, out EntityID id));
            return id;
        }

        private static void Populate(IronGrind.CharacterStats.CharacterStats stats, EntityID id)
        {
            stats.SetBaseStat(id, StatID.AttackPower, BASE_ATTACK);
            stats.SetBaseStat(id, StatID.Defense, BASE_DEFENSE);
            stats.SetBaseStat(id, StatID.MaxHP, BASE_MAX_HP);
            stats.SetBaseStat(id, StatID.MaxMP, BASE_MAX_MP);
            stats.SetCurrentHP(id, START_HP);
            stats.SetCurrentMP(id, START_MP);
        }

        private static BuffDefinition ApBuff(uint buffId, float flat, uint duration)
        {
            return new BuffDefinition((BuffID)buffId,
                new[] { new StatModifier(StatID.AttackPower, flat, PCT_NONE) }, duration, 0f, 0f);
        }

        private static StatusEffectsService BuildService(IronGrind.CharacterStats.CharacterStats stats, ZoneSlotPool pool)
        {
            return new StatusEffectsService(new CharacterStatsBuffModifierLayer(stats),
                new CharacterStatsResourceSink(stats), new ZoneSlotPoolEntityIndex(pool), new CharacterStatsDeathSource(stats));
        }

        // ------------------------------------------------------------ Character Stats construction

        [Test]
        public void test_composition_builds_exactly_one_character_stats_and_the_service_adapters_use_it()
        {
            WiRig rig = StartRig();
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            EntityID player = AllocPlayer(pool, 1u, out _);
            Populate(stats, player);

            ApplyResult result = rig.Comp.StatusEffects.ApplyEffect(player, player, ApBuff(1u, FLAT_AP_SMALL, LONG_DURATION));
            ApplyResult heal = rig.Comp.StatusEffects.ApplyInstantHeal(player, HEAL_AMOUNT);
            stats.ApplyDamage(player, OVERKILL);

            Assert.IsNotNull(stats);
            Assert.AreEqual(1, rig.Observed.FindAll(s => s == "CharacterStats").Count);
            // Each adapter reaches the composition's instance: the modifier layer (effective stat), the resource sink
            // (heal raised its CurrentHP) and the death source (a death on it reached the service).
            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_SMALL, stats.GetEffectiveStat(player, StatID.AttackPower));
            Assert.AreEqual(ApplyResult.Success, heal);
            Assert.IsTrue(rig.Comp.StatusEffects.IsEntityDead(player));
        }

        [Test]
        public void test_composition_resource_sink_heal_lands_on_the_zone_character_stats()
        {
            WiRig rig = StartRig();
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;
            EntityID player = AllocPlayer(rig.Comp.ZoneInstancing.Pool, 1u, out _);
            Populate(stats, player);

            rig.Comp.StatusEffects.ApplyInstantHeal(player, HEAL_AMOUNT);

            Assert.AreEqual(START_HP + HEAL_AMOUNT, stats.GetCurrentHP(player), 0.001f);
        }

        [Test]
        public void test_entity_index_pool_larger_than_the_effect_table_throws()
        {
            var pool = new ZoneSlotPool(Zone, new FakeTicks(), new NullPoolListener(),
                ZoneSlotPool.MAX_PLAYERS_PER_ZONE, ZoneSlotPool.MAX_MOBS_PER_ZONE + 1);

            Assert.Throws<ArgumentException>(() => new ZoneSlotPoolEntityIndex(pool));
        }

        // ------------------------------------------------------------ ZoneSlotPoolEntityIndex

        [Test]
        public void test_entity_index_null_pool_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneSlotPoolEntityIndex(null));
        }

        [Test]
        public void test_entity_index_player_maps_to_its_player_slot()
        {
            ZoneSlotPool pool = NewPool();
            EntityID player = AllocPlayer(pool, 1u, out byte slot);
            var index = new ZoneSlotPoolEntityIndex(pool);

            bool found = index.TryGetSlotIndex(player, out int result);

            Assert.IsTrue(found);
            Assert.AreEqual((int)slot, result);
        }

        [Test]
        public void test_entity_index_mob_maps_to_its_owl_index()
        {
            ZoneSlotPool pool = NewPool();
            EntityID mob = AllocMob(pool, out ushort mobSlot);
            var index = new ZoneSlotPoolEntityIndex(pool);

            bool found = index.TryGetSlotIndex(mob, out int result);

            Assert.IsTrue(found);
            Assert.AreEqual(pool.MaxPlayers + mobSlot, result);
            Assert.GreaterOrEqual(result, pool.MaxPlayers);
            Assert.Less(result, StatusEffectsConstants.MAX_ENTITY_SLOTS);
        }

        [Test]
        public void test_entity_index_unknown_id_returns_false()
        {
            var index = new ZoneSlotPoolEntityIndex(NewPool());

            Assert.IsFalse(index.TryGetSlotIndex(new EntityID(9999u), out _));
        }

        [Test]
        public void test_entity_index_released_player_returns_false()
        {
            ZoneSlotPool pool = NewPool();
            EntityID player = AllocPlayer(pool, 1u, out _);
            var index = new ZoneSlotPoolEntityIndex(pool);
            Assert.IsTrue(pool.ReleasePlayer(player));

            Assert.IsFalse(index.TryGetSlotIndex(player, out _));
        }

        [Test]
        public void test_entity_index_released_mob_returns_false()
        {
            ZoneSlotPool pool = NewPool();
            EntityID mob = AllocMob(pool, out _);
            var index = new ZoneSlotPoolEntityIndex(pool);
            Assert.IsTrue(pool.ReleaseMob(mob));

            Assert.IsFalse(index.TryGetSlotIndex(mob, out _));
        }

        [Test]
        public void test_entity_index_lookup_allocates_nothing()
        {
            ZoneSlotPool pool = NewPool();
            EntityID player = AllocPlayer(pool, 1u, out _);
            EntityID mob = AllocMob(pool, out _);
            var index = new ZoneSlotPoolEntityIndex(pool);
            index.TryGetSlotIndex(player, out _);
            index.TryGetSlotIndex(mob, out _);

            Assert.That(() =>
            {
                index.TryGetSlotIndex(player, out _);
                index.TryGetSlotIndex(mob, out _);
                index.TryGetSlotIndex(new EntityID(9999u), out _);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Wiring

        [Test]
        public void test_simulation_steps_are_exactly_zone_instancing_then_status_effects_then_movement_after_start()
        {
            WiRig rig = StartRig();

            IList<IZoneSimulationStep> steps = rig.Comp.SimulationSteps;

            Assert.AreEqual(3, steps.Count);
            Assert.AreSame(rig.Comp.Movement, steps[2]);
            Assert.AreSame(rig.Comp.ZoneInstancing, steps[0]);
            Assert.IsInstanceOf<ZoneInstancingWiring>(steps[0]);
            Assert.AreSame(rig.Comp.StatusEffects, steps[1]);
            Assert.IsInstanceOf<StatusEffectsService>(steps[1]);
        }

        [Test]
        public void test_pool_release_of_a_mob_clears_its_effects_through_the_release_listener()
        {
            WiRig rig = StartRig();
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;
            EntityID mob = AllocMob(pool, out _);
            Populate(stats, mob);
            Assert.AreEqual(ApplyResult.Success, rig.Comp.StatusEffects.ApplyEffect(mob, mob, ApBuff(1u, FLAT_AP_SMALL, LONG_DURATION)));
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_SMALL, stats.GetEffectiveStat(mob, StatID.AttackPower));

            Assert.IsTrue(pool.ReleaseMob(mob));

            // The slot is gone, so the service cannot be asked; the modifier removal in Character Stats proves ClearEntity ran.
            Assert.AreEqual(BASE_ATTACK, stats.GetEffectiveStat(mob, StatID.AttackPower));
        }

        [Test]
        public void test_pool_release_of_a_player_clears_its_effects_through_the_release_listener()
        {
            WiRig rig = StartRig();
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;
            EntityID player = AllocPlayer(pool, 1u, out _);
            Populate(stats, player);
            Assert.AreEqual(ApplyResult.Success, rig.Comp.StatusEffects.ApplyEffect(player, player, ApBuff(1u, FLAT_AP_SMALL, LONG_DURATION)));

            Assert.IsTrue(pool.ReleasePlayer(player));

            Assert.AreEqual(BASE_ATTACK, stats.GetEffectiveStat(player, StatID.AttackPower));
        }

        [Test]
        public void test_after_the_dispose_pass_a_death_event_no_longer_reaches_the_service()
        {
            WiRig rig = StartRig();
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;
            StatusEffectsService service = rig.Comp.StatusEffects;
            EntityID controlMob = AllocMob(pool, out _);
            Populate(stats, controlMob);
            stats.SetCurrentHP(controlMob, KILL_HP);
            stats.ApplyDamage(controlMob, OVERKILL);
            Assert.IsTrue(service.IsEntityDead(controlMob), "control: the subscription works before teardown");

            // The rig is already started; run the teardown (which ends with the Dispose pass) without a second Start.
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);
            Assert.IsTrue(rig.Comp.TryBeginTeardown());
            SuRig.Drive(rig.Comp.RunTeardown());
            rig.Comp.TeardownNow();
            // The close steps release every slot, so the probe is a mob allocated after the teardown.
            EntityID mob = AllocMob(pool, out _);
            Populate(stats, mob);
            Assert.IsTrue(pool.TryGetMobSlot(mob, out _), "precondition: the mob holds a slot, so the check below is not vacuous");
            stats.SetCurrentHP(mob, KILL_HP);
            stats.ApplyDamage(mob, OVERKILL);

            Assert.IsFalse(service.IsEntityDead(mob));
        }

        // ------------------------------------------------------------ AC-SE-24

        [Test]
        public void test_ac_se_24_stand_in_after_status_effects_sees_the_post_expiry_attack_power_on_the_expiry_tick()
        {
            const uint DURATION = 3u;
            WiRig rig = StartRig();
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;
            EntityID mob = AllocMob(pool, out _);
            Populate(stats, mob);
            var standIn = new AttackPowerRecorder(stats, mob);
            rig.Comp.SimulationSteps.Add(standIn); // after Status Effects, as Auto-Attack Combat will be
            Assert.AreEqual(ApplyResult.Success, rig.Comp.StatusEffects.ApplyEffect(mob, mob, ApBuff(1u, FLAT_AP_BIG, DURATION)));

            for (int i = 0; i < DURATION; i++)
                rig.Net.FireTick();

            Assert.AreEqual((int)DURATION, standIn.Seen.Count, "the stand-in runs once per network tick");
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_BIG, standIn.Seen[0]);
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_BIG, standIn.Seen[1]);
            Assert.AreEqual(BASE_ATTACK, standIn.Seen[2], "on the expiry tick the stand-in already sees the base value");
        }

        // ------------------------------------------------------------ AC-SE-20

        [Test]
        public void test_ac_se_20_mob_buff_is_applied_reflected_and_expires()
        {
            const uint DURATION = 2u;
            WiRig rig = StartRig();
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;
            StatusEffectsService service = rig.Comp.StatusEffects;
            EntityID mob = AllocMob(pool, out _);
            Populate(stats, mob);

            ApplyResult result = service.ApplyEffect(mob, mob, ApBuff(1u, FLAT_AP_SMALL, DURATION));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_SMALL, stats.GetEffectiveStat(mob, StatID.AttackPower));
            Assert.AreEqual(1, service.GetActiveEffectCount(mob));

            service.OnServerTick();
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_SMALL, stats.GetEffectiveStat(mob, StatID.AttackPower));
            service.OnServerTick();

            Assert.AreEqual(BASE_ATTACK, stats.GetEffectiveStat(mob, StatID.AttackPower));
            Assert.AreEqual(0, service.GetActiveEffectCount(mob));
        }

        // ------------------------------------------------------------ AC-SE-25

        private static BuffDefinition[] BuildLoadDefinitions()
        {
            var none = new StatModifier[0];
            return new[]
            {
                // expiring on the measured tick (duration 2, one warm-up tick first): ids 1, 3 and 6
                new BuffDefinition((BuffID)1u, new[] { new StatModifier(StatID.AttackPower, 5f, PCT_NONE) }, SHORT_DURATION, 0f, 0f),
                new BuffDefinition((BuffID)2u, new[] { new StatModifier(StatID.Defense, 3f, PCT_NONE) }, LONG_DURATION, 0f, 0f),
                new BuffDefinition((BuffID)3u, new[] { new StatModifier(StatID.MaxHP, 50f, PCT_NONE) }, SHORT_DURATION, 0f, 0f),
                new BuffDefinition((BuffID)4u, new[] { new StatModifier(StatID.MaxMP, 5f, PCT_NONE) }, LONG_DURATION, 0f, 0f),
                new BuffDefinition((BuffID)5u, none, LONG_DURATION, 1f, 0f),
                new BuffDefinition((BuffID)6u, none, SHORT_DURATION, 0f, 1f),
                new BuffDefinition((BuffID)7u, new[] { new StatModifier(StatID.Defense, 0f, 0.1f) }, LONG_DURATION, 0.5f, 0f),
                new BuffDefinition((BuffID)8u,
                    new[] { new StatModifier(StatID.MaxMP, 2f, PCT_NONE), new StatModifier(StatID.AttackPower, 2f, PCT_NONE) },
                    LONG_DURATION, 0f, 0.5f)
            };
        }

        [Test]
        public void test_ac_se_25_one_tick_over_200_entities_with_8_effects_each_allocates_zero_bytes()
        {
            ZoneSlotPool pool = NewPool();
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.Create();
            StatusEffectsService service = BuildService(stats, pool);
            AllocateFullZone(pool, stats, out List<EntityID> entities);
            Assert.AreEqual(ENTITY_COUNT, entities.Count);
            BuffDefinition[] definitions = BuildLoadDefinitions();
            Assert.AreEqual(EFFECTS_PER_ENTITY, definitions.Length);
            for (int e = 0; e < entities.Count; e++)
            {
                for (int d = 0; d < definitions.Length; d++)
                    Assert.AreEqual(ApplyResult.Success, service.ApplyEffect(entities[e], entities[e], definitions[d]), "entity " + e + " effect " + d);
            }

            // Warm-up: one full cycle at the measured load. Character Stats buffers the deferred OnStatChanged events in
            // arrays that double and a mask Dictionary that grows to the largest batch seen; a warm-up tick with no
            // expiry stays below the expiry tick's high-water mark, so the first expiry tick grows them once. Run that
            // expiry tick here, re-apply the short effects, then measure the next identical expiry tick.
            service.OnServerTick();
            service.OnServerTick();
            Assert.AreEqual(EFFECTS_PER_ENTITY - EXPIRING_PER_ENTITY, service.GetActiveEffectCount(entities[0]),
                "the warm-up cycle expired the short effects");
            for (int e = 0; e < entities.Count; e++)
            {
                for (int d = 0; d < definitions.Length; d++)
                {
                    if (definitions[d].DurationTicks == SHORT_DURATION)
                        Assert.AreEqual(ApplyResult.Success, service.ApplyEffect(entities[e], entities[e], definitions[d]));
                }
            }

            service.OnServerTick();
            for (int e = 0; e < entities.Count; e++)
                Assert.AreEqual(EFFECTS_PER_ENTITY, service.GetActiveEffectCount(entities[e]), "load before the measured tick, entity " + e);

            // Measured with the GC.Alloc profiler recorder (Unity Test Framework constraint, as in the Equipment and
            // Outbox tests). GC.GetAllocatedBytesForCurrentThread always reads 0 under the Editor's Mono (the positive
            // control below caught it), and the GDD's GC.GetTotalMemory(false) delta is not stable under that GC.
            Assert.That(service.OnServerTick, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "one OnServerTick over 200 x 8 effects allocated");
            Assert.AreEqual(EFFECTS_PER_ENTITY - EXPIRING_PER_ENTITY, service.GetActiveEffectCount(entities[0]),
                "the measured tick expired the short effects");
            Assert.AreEqual(EFFECTS_PER_ENTITY - EXPIRING_PER_ENTITY, service.GetActiveEffectCount(entities[ENTITY_COUNT - 1]));
            service.Dispose();
        }

        private static void AllocateFullZone(ZoneSlotPool pool, IronGrind.CharacterStats.CharacterStats stats, out List<EntityID> entities)
        {
            entities = new List<EntityID>(ENTITY_COUNT);
            for (int i = 0; i < pool.MaxPlayers; i++)
            {
                EntityID id = AllocPlayer(pool, (uint)(i + 1), out _);
                Populate(stats, id);
                entities.Add(id);
            }

            for (int i = 0; i < pool.MaxMobs; i++)
            {
                EntityID id = AllocMob(pool, out _);
                Populate(stats, id);
                entities.Add(id);
            }
        }

        [Test]
        public void test_ac_se_25_the_measurement_helper_detects_a_deliberately_allocating_test_double()
        {
            ZoneSlotPool pool = NewPool();
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.Create();
            var allocating = new AllocatingResourceSink(new CharacterStatsResourceSink(stats));
            var service = new StatusEffectsService(new CharacterStatsBuffModifierLayer(stats), allocating,
                new ZoneSlotPoolEntityIndex(pool), new CharacterStatsDeathSource(stats));
            EntityID mob = AllocMob(pool, out _);
            Populate(stats, mob);
            var regen = new BuffDefinition((BuffID)1u, new StatModifier[0], LONG_DURATION, 1f, 0f);
            Assert.AreEqual(ApplyResult.Success, service.ApplyEffect(mob, mob, regen));
            service.OnServerTick(); // warm-up

            Assert.That(service.OnServerTick, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "the GC.Alloc measure did not register the double's allocation");
            service.Dispose();
        }

        // ------------------------------------------------------------ Per-entry isolation

        [Test]
        public void test_isolation_a_throwing_layer_logs_entry_failed_the_stand_in_still_runs_and_the_next_tick_is_clean()
        {
            ZoneSlotPool pool = NewPool();
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.Create();
            var recorder = new RecordingBuffModifierLayer(
                new CharacterStatsBuffModifierLayer(stats), new CharacterStatsResourceSink(stats))
            {
                ThrowOnRemoveNumber = 1
            };
            var service = new StatusEffectsService(recorder, recorder, new ZoneSlotPoolEntityIndex(pool), new CharacterStatsDeathSource(stats));
            EntityID mob = AllocMob(pool, out _);
            Populate(stats, mob);
            Assert.AreEqual(ApplyResult.Success, service.ApplyEffect(mob, mob, ApBuff(1u, FLAT_AP_SMALL, 1u)));
            var standIn = new CountingStep();
            var simulation = new ZoneSimulationStep(new List<IZoneSimulationStep> { service, standIn });
            LogAssert.Expect(UnityEngine.LogType.Error,
                "[ZoneSimulationStep] EntryFailed: index=0 type=StatusEffectsService tick=" + SOME_TICK
                + " - InvalidOperationException: RecordingBuffModifierLayer: injected remove failure");

            simulation.Run(SOME_TICK, DELTA);

            Assert.AreEqual(1, standIn.Runs, "the entry after the failed one still ran this tick");
            Assert.AreEqual(recorder.BeginCalls, recorder.EndCalls, "the stat batch was closed");

            simulation.Run(SOME_TICK + 1u, DELTA);

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(2, standIn.Runs);
            Assert.AreEqual(2, recorder.BeginCalls);
            Assert.AreEqual(2, recorder.EndCalls);
            Assert.AreEqual(0, service.GetActiveEffectCount(mob), "the retried expiry completed");
            Assert.AreEqual(BASE_ATTACK, stats.GetEffectiveStat(mob, StatID.AttackPower));
            service.Dispose();
        }

        [Test]
        public void test_isolation_success_path_of_run_over_the_service_and_a_stand_in_allocates_zero_bytes()
        {
            ZoneSlotPool pool = NewPool();
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.Create();
            StatusEffectsService service = BuildService(stats, pool);
            EntityID mob = AllocMob(pool, out _);
            Populate(stats, mob);
            Assert.AreEqual(ApplyResult.Success, service.ApplyEffect(mob, mob, ApBuff(1u, FLAT_AP_SMALL, LONG_DURATION)));
            var standIn = new CountingStep();
            var simulation = new ZoneSimulationStep(new List<IZoneSimulationStep> { service, standIn });
            simulation.Run(1u, DELTA); // warm-up

            Assert.That(() => simulation.Run(2u, DELTA), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.AreEqual(2, standIn.Runs);
            service.Dispose();
        }

        [Test]
        public void test_service_run_calls_on_server_tick_and_ignores_its_arguments()
        {
            ZoneSlotPool pool = NewPool();
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.Create();
            StatusEffectsService service = BuildService(stats, pool);
            EntityID mob = AllocMob(pool, out _);
            Populate(stats, mob);
            Assert.AreEqual(ApplyResult.Success, service.ApplyEffect(mob, mob, ApBuff(1u, FLAT_AP_SMALL, 1u)));

            ((IZoneSimulationStep)service).Run(0u, 0f);

            Assert.AreEqual(0, service.GetActiveEffectCount(mob));
            service.Dispose();
        }
    }
}
