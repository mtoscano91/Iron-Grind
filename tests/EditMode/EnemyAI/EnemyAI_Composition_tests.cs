using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Events;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.Navigation;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// Shared fixtures for composition rigs that need a mob registry (Enemy AI Story 013): one common mob type that never
    /// rolls Enraged, so seeded stats are deterministic.
    /// </summary>
    internal static class EnemyAICompositionFixtures
    {
        /// <summary>Raw id of the one registered mob type.</summary>
        public const uint MOB_TYPE_RAW = 1u;

        /// <summary>Base max HP of the fixture mob (never Enraged).</summary>
        public const int MOB_HP = 100;

        /// <summary>Kill XP of the fixture mob.</summary>
        public const int KILL_XP = 10;

        /// <summary>Move speed of the fixture mob in m/s.</summary>
        public const float MOVE_SPEED = 3.5f;

        /// <summary>The registered mob type.</summary>
        public static MobTypeID MobType => new MobTypeID(MOB_TYPE_RAW);

        /// <summary>A valid definition with Enraged chance 0.</summary>
        public static MobDefinition Def()
        {
            return new MobDefinition(
                mobTypeID: MobType,
                displayName: "Composition Mob",
                baseMaxHP: MOB_HP,
                baseAttackPower: 10,
                baseDefense: 5,
                attackRange: 2f,
                moveSpeed: MOVE_SPEED,
                aggroRange: 8f,
                leashRange: 20f,
                attackWindUpTicks: 12,
                attackRecoveryTicks: 6,
                attackCooldownTicks: 10,
                killXP: KILL_XP,
                mobTier: MobTier.Common,
                enragedChance: 0f,
                enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f,
                enragedXPMultiplier: 1.5f,
                enragedLootQualityBonusTier: 1);
        }

        /// <summary>A registry holding only <see cref="Def"/>.</summary>
        public static IMobDefinitionRegistry Registry() => MobDefinitionRegistry.Build(new[] { Def() });
    }

    /// <summary>
    /// Enemy AI Story 013: composition wiring of the registry, store, navigation seams, Enemy AI and the phase-3 order
    /// (ADR-014, CR-AI-2). Rigs: <see cref="NavCompositionRig"/> (composition-built navigation, so Enemy AI exists) and
    /// <see cref="WiRig"/> (a navigation seam, so no Enemy AI).
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_Composition_Tests
    {
        private const string LOOT_LOG = "[ZoneServer] loot table not implemented";

        private sealed class RegistryLoader : IZoneStaticDataLoader, IZoneMobData
        {
            public RegistryLoader(IMobDefinitionRegistry registry) { LoadedMobRegistry = registry; }

            public IMobDefinitionRegistry LoadedMobRegistry { get; }

            public bool LoadAndValidate() => true;
        }

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

        private NavCompositionRig StartedRig()
        {
            var rig = new NavCompositionRig();
            _rigs.Add(rig);
            rig.Start();
            return rig;
        }

        private static EntityID Spawn(ZoneServerComposition comp, Vector3 position)
        {
            ZoneInstancingWiring wiring = comp.ZoneInstancing;
            EntityID mob = wiring.MobRoster.TryAddMob(wiring.Pool.Zone, EnemyAICompositionFixtures.MobType, position);
            Assert.AreNotEqual(EntityID.Invalid, mob);
            comp.MobEvents.RaiseMobSpawned(new MobSpawnedArgs(comp.ZoneId, mob, EnemyAICompositionFixtures.MobType, position));
            return mob;
        }

        private static List<string> StepNames(IList<IZoneSimulationStep> steps)
        {
            var names = new List<string>();
            foreach (IZoneSimulationStep step in steps)
            {
                names.Add(step.GetType().Name);
            }

            return names;
        }

        // QA 1
        [Test]
        public void test_composition_phase_three_steps_are_listed_in_the_cr_ai_2_order()
        {
            NavCompositionRig rig = StartedRig();

            List<string> names = StepNames(rig.Comp.SimulationSteps);

            CollectionAssert.AreEqual(new[]
            {
                nameof(ZoneInstancingWiring), nameof(NavigationPhaseStep), nameof(MobSpawningPhaseStep),
                "StatusEffectsService", "MovementService",
                nameof(EnemyAISystem), "AutoAttackCombatSystem", "SkillCastStep", "GatewayLinkPump",
                "PartyTickStep", // Party System Story 011 AC-4: after the link pump, before phase 4
                "MobPositionIntegrationStep"
            }, names);
            Assert.AreSame(rig.Comp.EnemyAI.IntegrationStep, rig.Comp.SimulationSteps[names.Count - 1]);
        }

        // QA 2
        [Test]
        public void test_composition_without_a_mob_registry_fails_startup_with_the_error_and_builds_nothing()
        {
            var rig = new WiRig();
            rig.Comp.MobRegistryOverride = null;
            LogAssert.Expect(LogType.Error, ZoneServerComposition.NO_MOB_REGISTRY_ERROR);
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S5"));

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.IsFalse(rig.Comp.IsTickRunning);
            Assert.IsNull(rig.Comp.MobStore);
            Assert.IsNull(rig.Comp.EnemyAI);
        }

        // QA 3
        [Test]
        public void test_composition_registry_from_the_s2_loader_is_used_when_there_is_no_override()
        {
            IMobDefinitionRegistry registry = EnemyAICompositionFixtures.Registry();
            var rig = new WiRig(staticDataLoader: new RegistryLoader(registry));
            rig.Comp.MobRegistryOverride = null;

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.AreSame(registry, rig.Comp.MobRegistryInUse);
            Assert.IsNotNull(rig.Comp.MobStore);
            Assert.IsTrue(rig.Comp.IsTickRunning);
        }

        [Test]
        public void test_composition_registry_override_wins_over_the_s2_loader()
        {
            IMobDefinitionRegistry loaderRegistry = EnemyAICompositionFixtures.Registry();
            IMobDefinitionRegistry overrideRegistry = EnemyAICompositionFixtures.Registry();
            var rig = new WiRig(staticDataLoader: new RegistryLoader(loaderRegistry));
            rig.Comp.MobRegistryOverride = overrideRegistry;

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.AreSame(overrideRegistry, rig.Comp.MobRegistryInUse);
        }

        // QA 4
        [Test]
        public void test_composition_after_teardown_a_spawn_creates_no_store_entry_and_no_controller()
        {
            NavCompositionRig rig = StartedRig();
            MobEventBus bus = rig.Comp.MobEvents;
            MobRuntimeStore store = rig.Comp.MobStore;
            EnemyAISystem enemyAi = rig.Comp.EnemyAI;
            rig.Teardown();

            using (var capture = new NavProblemCapture())
            {
                Assert.DoesNotThrow(() => bus.RaiseMobSpawned(new MobSpawnedArgs(
                    rig.Comp.ZoneId, new EntityID(777u), EnemyAICompositionFixtures.MobType, Vector3.zero)));

                Assert.AreEqual(0, store.Count);
                Assert.AreEqual(0, enemyAi.Count);
                Assert.IsNull(rig.Comp.MobStore);
                Assert.IsNull(rig.Comp.EnemyAI);
                Assert.IsNull(rig.Comp.MobRegistryInUse);
                Assert.AreEqual(0, capture.Problems.Count, capture.Joined);
            }
        }

        // QA 5 and QA 9 (Dormant)
        [Test]
        public void test_composition_spawn_seeds_hp_and_creates_a_dormant_controller()
        {
            NavCompositionRig rig = StartedRig();

            EntityID mob = Spawn(rig.Comp, Vector3.zero);

            Assert.IsTrue(rig.Comp.MobStore.TryGet(mob, out MobRuntimeEntry entry));
            Assert.IsFalse(entry.Variant.IsEnraged);
            Assert.AreEqual(EnemyAICompositionFixtures.KILL_XP, entry.Variant.EffectiveKillXP);
            Assert.AreEqual((float)EnemyAICompositionFixtures.MOB_HP, rig.Comp.ZoneCharacterStats.GetCurrentHP(mob));
            Assert.AreEqual(1, rig.Comp.EnemyAI.Count);
            Assert.AreEqual(MobState.Dormant, rig.Comp.EnemyAI.Controllers[0].State);
        }

        // QA 8 (the step reads the store; position reaches navigation through the store's committed position)
        [Test]
        public void test_composition_navigation_step_runs_over_the_store_committed_position()
        {
            NavCompositionRig rig = StartedRig();
            var position = new Vector3(3f, 0f, 4f);
            EntityID mob = Spawn(rig.Comp, position);
            IZoneSimulationStep navigationStep = rig.Comp.SimulationSteps[1];

            Assert.DoesNotThrow(() => navigationStep.Run(1u, 0.05f));

            var committed = new Dictionary<EntityID, Vector3>();
            rig.Comp.MobStore.CopyCommittedPositions(committed);
            Assert.AreEqual(position, committed[mob]);
            Assert.AreEqual(1, rig.Comp.NavigationService.ActiveCount);
            FakeNavAgent claimed = rig.Factory.Created.Find(a => a.EnabledValue);
            Assert.IsNotNull(claimed);
            Assert.GreaterOrEqual(claimed.WarpCalls, 1);
            Assert.AreEqual(position, claimed.LastWarpPosition);
        }

        // Pursuing mobs read as in combat through the production entity-state source (Story 013 / TD-100): the Enemy AI
        // aggro scan finds a Movement-placed player, and the MobAware combat reader maps Pursuing to CombatActive.
        [Test]
        public void test_composition_pursuing_mob_reads_in_combat_through_the_entity_state_source()
        {
            NavCompositionRig rig = StartedRig();
            ZoneInstancingWiring wiring = rig.Comp.ZoneInstancing;
            Assert.IsTrue(wiring.Pool.TryAllocatePlayer(wiring.Pool.Zone, new CharacterID(11u), new AccountID(1011u), 11u,
                out var slot, out EntityID player));
            rig.Comp.Movement.SeedForTesting((int)slot, player, new Vector3(5f, 0f, 0f), 0);
            // The F-AI-2 scan skips players with HP 0 or less; this rig loads no character, so give the player HP.
            rig.Comp.ZoneCharacterStats.SetBaseStat(player, StatID.MaxHP, 100);
            rig.Comp.ZoneCharacterStats.SetCurrentHP(player, 100);
            EntityID near = Spawn(rig.Comp, Vector3.zero);
            EntityID far = Spawn(rig.Comp, new Vector3(100f, 0f, 0f));

            int ticks = EnemyAIConfigValues.AGGRO_SCAN_INTERVAL_TICKS_DEFAULT * 3;
            for (uint tick = 1u; tick <= (uint)ticks; tick++)
            {
                rig.Comp.EnemyAI.Run(tick, 0.05f);
            }

            Assert.IsTrue(rig.Comp.EnemyAI.TryGetMobState(near, out MobState nearState));
            Assert.AreEqual(MobState.Pursuing, nearState);
            Assert.IsTrue(rig.Comp.EntityStateSource.TryGetMobState(near, out ZoneMobStateView nearView));
            Assert.IsTrue(nearView.IsInCombat);
            Assert.IsTrue(rig.Comp.EnemyAI.TryGetMobState(far, out MobState farState));
            Assert.AreEqual(MobState.Dormant, farState);
            Assert.IsTrue(rig.Comp.EntityStateSource.TryGetMobState(far, out ZoneMobStateView farView));
            Assert.IsFalse(farView.IsInCombat);
        }

        [Test]
        public void test_composition_with_a_navigation_seam_has_bus_and_store_but_no_enemy_ai_or_integration_step()
        {
            var rig = new WiRig();

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.IsNotNull(rig.Comp.MobEvents);
            Assert.IsNotNull(rig.Comp.MobStore);
            Assert.IsNull(rig.Comp.EnemyAI);
            List<string> names = StepNames(rig.Comp.SimulationSteps);
            CollectionAssert.DoesNotContain(names, nameof(EnemyAISystem));
            CollectionAssert.DoesNotContain(names, nameof(NavigationPhaseStep));
            CollectionAssert.DoesNotContain(names, "MobPositionIntegrationStep");
        }

        // QA 10 (reduced): two kills reported through Combat reach Enemy AI, whose loot calls hit the logging stand-in; the
        // stand-in logs once across calls, so exactly one Expect covers both kills. A real auto-attack kill is not driven here:
        // Combat reads the player through the persistence-backed state source (no loaded, named player in this rig); the
        // end-to-end kill and its XP are covered by EnemyAI_CombatSeams_tests (test_endToEnd_*). The XP award is asserted
        // directly through the composition's runtime store.
        [Test]
        public void test_composition_reported_kills_reach_the_loot_stand_in_which_logs_once()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Random\] PRNG seed: ")); // S3 startup info line; NoUnexpectedReceived below would flag it
            LogAssert.Expect(LogType.Warning, new Regex(@"Death and Respawn not built")); // no town respawn provider here
            NavCompositionRig rig = StartedRig();
            ZoneInstancingWiring wiring = rig.Comp.ZoneInstancing;
            Assert.IsTrue(wiring.Pool.TryAllocatePlayer(wiring.Pool.Zone, new CharacterID(11u), new AccountID(1011u), 11u,
                out _, out EntityID player));
            EntityID first = Spawn(rig.Comp, Vector3.zero);
            EntityID second = Spawn(rig.Comp, new Vector3(1f, 0f, 1f));
            LogAssert.Expect(LogType.Log, LOOT_LOG);

            rig.Comp.AutoAttackCombat.ReportKill(player, first, Vector3.zero);
            rig.Comp.AutoAttackCombat.ReportKill(player, second, new Vector3(1f, 0f, 1f));

            var xp = new MobRuntimeStoreXpAward(rig.Comp.MobStore);
            Assert.AreEqual(EnemyAICompositionFixtures.KILL_XP, xp.GetXPAward(first));
            Assert.AreEqual(EnemyAICompositionFixtures.KILL_XP, xp.GetXPAward(second));
            Assert.IsTrue(rig.Comp.EnemyAI.TryGetMobState(first, out MobState firstState));
            Assert.AreEqual(MobState.Dead, firstState);
            Assert.IsTrue(rig.Comp.EnemyAI.TryGetMobState(second, out MobState secondState));
            Assert.AreEqual(MobState.Dead, secondState);
            LogAssert.NoUnexpectedReceived();
        }

        // Stand-in loot table
        [Test]
        public void test_stand_in_loot_table_logs_once_across_calls()
        {
            var loot = new StandInLootTableService();
            LogAssert.Expect(LogType.Log, LOOT_LOG);

            loot.ResolveMobDrop(new EntityID(5u), 0);
            loot.ResolveMobDrop(new EntityID(6u), 0);
            loot.ClearMob(new EntityID(5u));
            loot.Clear();

            LogAssert.NoUnexpectedReceived();
        }

        // QA 6
        [Test]
        public void test_nav_speed_source_known_type_returns_the_definition_move_speed()
        {
            var source = new MobDefinitionRegistryNavSpeedSource(EnemyAICompositionFixtures.Registry());

            bool found = source.TryGetMoveSpeed(EnemyAICompositionFixtures.MobType, out float speed);

            Assert.IsTrue(found);
            Assert.AreEqual(EnemyAICompositionFixtures.MOVE_SPEED, speed);
        }

        [Test]
        public void test_nav_speed_source_unknown_type_is_not_found()
        {
            var source = new MobDefinitionRegistryNavSpeedSource(EnemyAICompositionFixtures.Registry());

            bool found = source.TryGetMoveSpeed(new MobTypeID(999u), out float speed);

            Assert.IsFalse(found);
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void test_nav_speed_source_invalid_type_is_not_found()
        {
            var source = new MobDefinitionRegistryNavSpeedSource(EnemyAICompositionFixtures.Registry());

            Assert.IsFalse(source.TryGetMoveSpeed(MobTypeID.Invalid, out _));
        }

        [Test]
        public void test_nav_speed_source_null_registry_throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new MobDefinitionRegistryNavSpeedSource(null));
        }
    }
}
