#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
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
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZoneStats = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode tests for Enemy AI Story 011: the death bridge (Combat OnMobDied or CR-AI-13, then loot, then
    /// MobEventBus.MobDied, then the linger despawn). QA 16 (no XP API) is covered by code review only.
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_DeathBridge_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint OTHER_ZONE_RAW = 8u;
        private const uint TYPE_RAW = 1u;
        private const uint MOB_RAW_ID = 3001u;
        private const uint PLAYER_RAW_ID = 101u;
        private const float RETURN_SPEED_CAP = 5f;
        private const float DELTA = 0.05f;
        private const int LINGER = 10;
        private const int ENRAGED_TIER = 1;
        private const int CAPACITY = 32;
        private const double NON_ENRAGED_ROLL = 0.5;

        private static readonly EntityID PLAYER = new EntityID(PLAYER_RAW_ID);

        private sealed class FakeRegistry : IMobDefinitionRegistry
        {
            public readonly Dictionary<MobTypeID, MobDefinition> Defs = new Dictionary<MobTypeID, MobDefinition>();

            public MobDefinition GetDefinition(MobTypeID id) => Defs.TryGetValue(id, out MobDefinition? d) ? d : null!;
        }

        private sealed class QuietNav : INavigationProvider
        {
            public void SetDestination(EntityID agent, Vector3 target) { }

            public void SetSpeed(EntityID agent, float speed) { }

            public void Stop(EntityID agent) { }

            public void Resume(EntityID agent) { }

            public bool IsPathStale(EntityID agent) => false;

            public Vector3 Velocity;

            public Vector3 GetCurrentVelocity(EntityID agent) => Velocity;
        }

        private sealed class HpStats : IZoneEntityStatReader
        {
            public readonly Dictionary<EntityID, float> Hp = new Dictionary<EntityID, float>();

            public float GetCurrentHP(EntityID entityId) => Hp.TryGetValue(entityId, out float hp) ? hp : 100f;

            public float GetCurrentMP(EntityID entityId) => 0f;

            public int GetEffectiveStat(EntityID entityId, StatID statId) => 0;
        }

        private sealed class CountingResolver : IMobAttackResolver
        {
            public int Calls;

            public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange, Vector3 capturedForward)
            {
                Calls++;
            }
        }

        private sealed class FakeCandidates : IAggroCandidateSource
        {
            public readonly List<(EntityID Id, Vector3 Position)> Players = new List<(EntityID Id, Vector3 Position)>();

            public void CopyPlayers(List<(EntityID Id, Vector3 Position)> into) => into.AddRange(Players);

            public bool TryGetPlayerPosition(EntityID id, out Vector3 position)
            {
                for (int i = 0; i < Players.Count; i++)
                {
                    if (Players[i].Id.Equals(id))
                    {
                        position = Players[i].Position;
                        return true;
                    }
                }

                position = Vector3.zero;
                return false;
            }
        }

        private sealed class FakeCombat : ICombatService
        {
            public event Action<MobDeathContext>? OnMobDied;

            public bool HasSubscribers => OnMobDied != null;

            public void Raise(MobDeathContext context) => OnMobDied?.Invoke(context);
        }

        private sealed class FakeLoot : ILootTableService
        {
            public List<string>? Log;
            public int Calls;
            public bool Throw;
            public EntityID? ThrowFor;

            public void RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage) { }

            public void ClearMob(EntityID mobEntityId) { }

            public void ResolveMobDrop(EntityID mobEntityID, int tierShift)
            {
                Calls++;
                Log?.Add($"loot {mobEntityID} {tierShift}");
                if (Throw || (ThrowFor.HasValue && ThrowFor.Value.Equals(mobEntityID)))
                {
                    throw new InvalidOperationException("loot failure");
                }
            }

            public void Clear() { }
        }

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static MobDefinition Def()
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(TYPE_RAW),
                displayName: "Test Mob",
                baseMaxHP: 100,
                baseAttackPower: 10,
                baseDefense: 5,
                attackRange: 2f,
                moveSpeed: 3.5f,
                aggroRange: 8f,
                leashRange: 20f,
                attackWindUpTicks: 12,
                attackRecoveryTicks: 6,
                attackCooldownTicks: 10,
                killXP: 10,
                mobTier: MobTier.Common,
                enragedChance: 0f,
                enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f,
                enragedXPMultiplier: 1.5f,
                enragedLootQualityBonusTier: ENRAGED_TIER);
        }

        private MobEventBus _bus = null!;
        private FakeRegistry _registry = null!;
        private ScriptedRandomProvider _rng = null!;
        private MobRuntimeStore _store = null!;
        private HpStats _stats = null!;
        private CountingResolver _resolver = null!;
        private FakeCandidates _candidates = null!;
        private FakeCombat _combat = null!;
        private FakeLoot _loot = null!;
        private EnemyAISystem _system = null!;
        private List<string> _log = null!;
        private List<MobDiedArgs> _died = null!;
        private QuietNav _nav = null!;
        private int _warnings;

        [SetUp]
        public void SetUp()
        {
            ZoneStats zoneStats = CharacterStatsFixture.Create();
            _bus = new MobEventBus();
            _registry = new FakeRegistry();
            _registry.Defs[new MobTypeID(TYPE_RAW)] = Def();
            _rng = new ScriptedRandomProvider();
            _store = new MobRuntimeStore(Zone, _bus, _registry, _rng, new CharacterStatsMobStatWriter(zoneStats), CAPACITY);
            _stats = new HpStats();
            _resolver = new CountingResolver();
            _candidates = new FakeCandidates();
            _combat = new FakeCombat();
            _log = new List<string>();
            _died = new List<MobDiedArgs>();
            _loot = new FakeLoot { Log = _log };
            _nav = new QuietNav();
            _warnings = 0;
            Application.logMessageReceived += HandleLog;
            _bus.OnMobDied += HandleBusMobDied;
            _system = NewSystem();
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= HandleLog;
            _bus.OnMobDied -= HandleBusMobDied;
            _system.Dispose();
            _store.Dispose();
        }

        private void HandleBusMobDied(MobDiedArgs args)
        {
            _died.Add(args);
            _log.Add($"died {args.Entity}");
        }

        // Counts [EnemyAISystem] warnings (including ones LogAssert.Expect swallows). Runs only when something is logged.
        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning && condition.StartsWith("[EnemyAISystem]", StringComparison.Ordinal))
            {
                _warnings++;
            }
        }

        private EnemyAISystem NewSystem()
        {
            EnemyAIConfigValues config = EnemyAIConfigValues.Create(
                EnemyAIConfigValues.AGGRO_SCAN_INTERVAL_TICKS_DEFAULT,
                EnemyAIConfigValues.PURSUIT_REPOSITION_THRESHOLD_DEFAULT,
                EnemyAIConfigValues.RETURN_ARRIVAL_THRESHOLD_DEFAULT,
                LINGER);
            return new EnemyAISystem(Zone, _bus, _store, _registry, _nav, _stats, _resolver, config,
                RETURN_SPEED_CAP, _candidates, _combat, _loot, CAPACITY);
        }

        private static EntityID MobId(uint offset) => new EntityID(MOB_RAW_ID + offset);

        private MobController Spawn(EntityID id, bool enraged = false)
        {
            _rng.EnqueueDouble(NON_ENRAGED_ROLL);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, id, new MobTypeID(TYPE_RAW), Vector3.zero));
            Assert.IsTrue(_system.TryGetController(id, out MobController c));
            c.IsEnraged = enraged;
            return c;
        }

        private static MobDeathContext Death(EntityID id, ZoneID zone)
        {
            return new MobDeathContext(zone, id, new MobTypeID(TYPE_RAW), Vector3.zero, PLAYER);
        }

        private void Kill(EntityID id) => _combat.Raise(Death(id, Zone));

        private void Run(uint tick) => _system.Run(tick, DELTA);

        // A Pursuing mob with a target 1.5 m away (inside attack range 2): reaches WindingUp on its first ticks.
        private MobController SpawnAttacker(EntityID id)
        {
            _candidates.Players.Add((PLAYER, new Vector3(1.5f, 0f, 0f)));
            MobController c = Spawn(id);
            c.ForceStateForTest(MobState.Pursuing);
            c.TargetEntityID = PLAYER;
            return c;
        }

        // Ticks until the mob is WindingUp with the given counter; returns the next free tick.
        private uint TickUntilWindUp(MobController c, int remaining)
        {
            uint t = 1u;
            while (t < 60u && !(c.State == MobState.WindingUp && c.WindUpTicksRemaining == remaining))
            {
                Run(t);
                t++;
            }

            Assert.AreEqual(MobState.WindingUp, c.State);
            Assert.AreEqual(remaining, c.WindUpTicksRemaining);
            Assert.AreEqual(0, _resolver.Calls);
            return t;
        }

        private string Loot(EntityID id, int tier) => $"loot {id} {tier}";

        private string Died(EntityID id) => $"died {id}";

        // QA 1
        [Test]
        public void test_onMobDied_liveMob_lootThenMobDiedAndDead()
        {
            MobController c = Spawn(MobId(0));

            Kill(MobId(0));

            CollectionAssert.AreEqual(new[] { Loot(MobId(0), 0), Died(MobId(0)) }, _log);
            Assert.AreEqual(MobState.Dead, c.State);
            Assert.AreEqual(1, c.DeadEntryCount);
            Assert.AreEqual(0, _warnings);
        }

        // QA 2
        [Test]
        public void test_hpCheck_hpZero_sameRoutineAndDeadEnteredAtCurrentTick()
        {
            MobController c = Spawn(MobId(0));
            _stats.Hp[MobId(0)] = 0f;

            Run(5u);

            CollectionAssert.AreEqual(new[] { Loot(MobId(0), 0), Died(MobId(0)) }, _log);
            Assert.AreEqual(MobState.Dead, c.State);
            Assert.AreEqual(5L, c.DeadEnteredTick);
        }

        // QA 3
        [Test]
        public void test_orderA_onMobDiedThenHpCheck_routineRunsOnce()
        {
            MobController c = Spawn(MobId(0));
            Kill(MobId(0));
            _stats.Hp[MobId(0)] = 0f;

            Run(1u);

            CollectionAssert.AreEqual(new[] { Loot(MobId(0), 0), Died(MobId(0)) }, _log);
            Assert.AreEqual(1, c.DeadEntryCount);
            Assert.AreEqual(0L, c.DeadEnteredTick);
            Assert.AreEqual(1, _loot.Calls);
            Assert.AreEqual(1, _died.Count);
        }

        // QA 4
        [Test]
        public void test_orderB_hpCheckThenOnMobDied_routineRunsOnceNoWarning()
        {
            MobController c = Spawn(MobId(0));
            _stats.Hp[MobId(0)] = 0f;
            Run(1u);

            Kill(MobId(0));

            CollectionAssert.AreEqual(new[] { Loot(MobId(0), 0), Died(MobId(0)) }, _log);
            Assert.AreEqual(1, c.DeadEntryCount);
            Assert.AreEqual(1L, c.DeadEnteredTick);
            Assert.AreEqual(0, _warnings);
        }

        // QA 5
        [Test]
        public void test_onMobDied_foreignZoneOrUnknownMob_oneWarningEachNothingElse()
        {
            MobController c = Spawn(MobId(0));
            var rx = new Regex(@"^\[EnemyAISystem\]");

            LogAssert.Expect(LogType.Warning, rx);
            _combat.Raise(Death(MobId(0), new ZoneID(OTHER_ZONE_RAW)));
            LogAssert.Expect(LogType.Warning, rx);
            Kill(MobId(99));

            Assert.AreEqual(0, _loot.Calls);
            Assert.AreEqual(0, _died.Count);
            Assert.AreEqual(MobState.Dormant, c.State);
            Assert.AreEqual(2, _warnings);
        }

        // QA 6
        [Test]
        public void test_linger_boundaries_despawnsAtExactlyLingerTicks()
        {
            Spawn(MobId(0));
            Run(100u);
            Kill(MobId(0));

            Run(109u);
            Assert.IsTrue(_system.TryGetController(MobId(0), out _));
            Assert.IsTrue(_store.TryGet(MobId(0), out _));

            Run(110u);
            Assert.IsFalse(_system.TryGetController(MobId(0), out _));
            Assert.IsFalse(_store.TryGet(MobId(0), out _));
            Assert.AreEqual(0, _system.Count);
        }

        // QA 7
        [Test]
        public void test_onMobDied_beforeFirstRun_deadEnteredTickIsZero()
        {
            MobController c = Spawn(MobId(0));

            Kill(MobId(0));

            Assert.AreEqual(0L, c.DeadEnteredTick);
        }

        // QA 8
        [Test]
        public void test_lootThrows_errorWithMobIdMobDiedStillRaisedAndMobDespawns()
        {
            _loot.Throw = true;
            Spawn(MobId(0));

            LogAssert.Expect(LogType.Error, new Regex(@"^\[EnemyAISystem\].*" + Regex.Escape(MobId(0).ToString())));
            Kill(MobId(0));

            Assert.AreEqual(1, _died.Count);
            Assert.AreEqual(MobId(0), _died[0].Entity);
            CollectionAssert.AreEqual(new[] { Loot(MobId(0), 0), Died(MobId(0)) }, _log);
            Run(9u);
            Assert.IsTrue(_system.TryGetController(MobId(0), out _));
            Run(10u);
            Assert.IsFalse(_system.TryGetController(MobId(0), out _));
            Assert.AreEqual(0, _warnings);
        }

        // QA 9
        [Test]
        public void test_mobEventBus_subscriberStandingInForNavigation_calledOnceWithZoneAndMob()
        {
            Spawn(MobId(0));

            Kill(MobId(0));
            Run(1u);

            Assert.AreEqual(1, _died.Count);
            Assert.AreEqual(Zone, _died[0].Zone);
            Assert.AreEqual(MobId(0), _died[0].Entity);
        }

        // QA 10
        [Test]
        public void test_windingUpWithHpZero_nextRunIsDeadAndNoAttack()
        {
            MobController c = SpawnAttacker(MobId(0));
            uint t = TickUntilWindUp(c, 3);
            _stats.Hp[MobId(0)] = 0f;

            Run(t);

            Assert.AreEqual(MobState.Dead, c.State);
            Assert.AreEqual(0, _resolver.Calls);
            Assert.AreEqual(1, c.DeadEntryCount);
            CollectionAssert.AreEqual(new[] { Loot(MobId(0), 0), Died(MobId(0)) }, _log);
        }

        // QA 11
        [Test]
        public void test_statusEffectStepThenEnemyAiStep_windUpOneMobDiesWithoutAttacking()
        {
            MobController c = SpawnAttacker(MobId(0));
            uint t = TickUntilWindUp(c, 1);

            // Status Effects stand-in: a damage-over-time tick drops the mob to 0 HP before Enemy AI runs this tick.
            _stats.Hp[MobId(0)] = 0f;
            Run(t);

            Assert.AreEqual(MobState.Dead, c.State);
            Assert.AreEqual(0, _resolver.Calls);
            Assert.AreEqual(t, (uint)c.DeadEnteredTick);
        }

        // QA 12
        [Test]
        public void test_cr_ai_13_enragedMobGetsBonusTierNonEnragedGetsZero()
        {
            Spawn(MobId(0), enraged: true);
            Spawn(MobId(1), enraged: false);
            _stats.Hp[MobId(0)] = 0f;
            _stats.Hp[MobId(1)] = 0f;

            Run(1u);

            CollectionAssert.AreEqual(
                new[] { Loot(MobId(0), ENRAGED_TIER), Died(MobId(0)), Loot(MobId(1), 0), Died(MobId(1)) },
                _log);
        }

        // QA 13
        [Test]
        public void test_twoMobsDieSameRun_twoRoutinesOrderKeptBothDespawnSameTick()
        {
            Spawn(MobId(0));
            Spawn(MobId(1));
            Spawn(MobId(2));
            _stats.Hp[MobId(0)] = 0f;
            _stats.Hp[MobId(2)] = 0f;

            Run(1u);

            CollectionAssert.AreEqual(
                new[] { Loot(MobId(0), 0), Died(MobId(0)), Loot(MobId(2), 0), Died(MobId(2)) },
                _log);
            Assert.AreEqual(3, _system.Count);
            Assert.AreEqual(MobId(0), _system.Controllers[0].EntityID);
            Assert.AreEqual(MobId(1), _system.Controllers[1].EntityID);
            Assert.AreEqual(MobId(2), _system.Controllers[2].EntityID);

            Run(1u + LINGER);

            Assert.AreEqual(1, _system.Count);
            Assert.IsTrue(_system.TryGetController(MobId(1), out _));
            Assert.IsFalse(_system.TryGetController(MobId(0), out _));
            Assert.IsFalse(_system.TryGetController(MobId(2), out _));
        }

        // QA 14
        [Test]
        public void test_dispose_onMobDiedReachesNoHandler()
        {
            MobController c = Spawn(MobId(0));

            _system.Dispose();
            Kill(MobId(0));

            Assert.IsFalse(_combat.HasSubscribers);

            // Spawn handling is unsubscribed too: the store still seeds the mob, the system adds no controller.
            _rng.EnqueueDouble(NON_ENRAGED_ROLL);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, MobId(1), new MobTypeID(TYPE_RAW), Vector3.zero));
            Assert.IsTrue(_store.TryGet(MobId(1), out _));
            Assert.AreEqual(1, _system.Count);
            Assert.AreEqual(0, _loot.Calls);
            Assert.AreEqual(0, _died.Count);
            Assert.AreEqual(MobState.Dormant, c.State);
        }

        [Test]
        public void test_onMobDied_pursuingMobWithCachedVelocity_velocityClearedAndCorpseDoesNotMove()
        {
            // Arrange: a Pursuing mob whose velocity was cached by Run.
            _nav.Velocity = new Vector3(2f, 0f, 1f);
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            MobController c = Spawn(MobId(0));
            c.ForceStateForTest(MobState.Pursuing);
            c.TargetEntityID = PLAYER;
            Run(1u);
            Assert.IsTrue(_system.HasVelocity[0]);
            Assert.IsTrue(_store.TryGet(MobId(0), out MobRuntimeEntry before));

            // Act: Combat kills it, then the phase 4 step runs.
            Kill(MobId(0));
            bool cleared = !_system.HasVelocity[0];
            _system.IntegrationStep.Run(1u, DELTA);

            // Assert
            Assert.IsTrue(cleared);
            Assert.AreEqual(MobState.Dead, c.State);
            Assert.AreEqual(Vector3.zero, c.Position);
            Assert.IsTrue(_store.TryGet(MobId(0), out MobRuntimeEntry after));
            Assert.AreEqual(before.CommittedPosition, after.CommittedPosition);
        }

        [Test]
        public void test_hpCheck_lingerBoundaries_presentAtLingerMinusOneGoneAtLinger()
        {
            // Arrange
            Spawn(MobId(0));
            _stats.Hp[MobId(0)] = 0f;

            // Act / Assert
            Run(5u);
            Run((uint)(5 + LINGER - 1));
            Assert.IsTrue(_system.TryGetController(MobId(0), out _));
            Assert.IsTrue(_store.TryGet(MobId(0), out _));

            Run((uint)(5 + LINGER));
            Assert.IsFalse(_system.TryGetController(MobId(0), out _));
            Assert.IsFalse(_store.TryGet(MobId(0), out _));
        }

        [Test]
        public void test_onMobDied_enragedMob_lootGetsEnragedTier()
        {
            // Arrange
            Spawn(MobId(0), enraged: true);

            // Act
            Kill(MobId(0));

            // Assert
            CollectionAssert.AreEqual(new[] { Loot(MobId(0), ENRAGED_TIER), Died(MobId(0)) }, _log);
        }

        [Test]
        public void test_onMobDied_afterDespawn_oneWarningNoLootNoMobDied()
        {
            // Arrange: the mob dies and despawns.
            Spawn(MobId(0));
            Kill(MobId(0));
            Run((uint)LINGER);
            Assert.IsFalse(_system.TryGetController(MobId(0), out _));
            Assert.AreEqual(0, _warnings);

            // Act: a late kill for the despawned mob.
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[EnemyAISystem\]"));
            Kill(MobId(0));

            // Assert
            Assert.AreEqual(1, _warnings);
            Assert.AreEqual(1, _loot.Calls);
            Assert.AreEqual(1, _died.Count);
        }

        [Test]
        public void test_twoMobsDie_lootThrowsForOne_bothRaiseMobDiedOneErrorBothDespawn()
        {
            // Arrange
            _loot.ThrowFor = MobId(0);
            Spawn(MobId(0));
            Spawn(MobId(1));
            _stats.Hp[MobId(0)] = 0f;
            _stats.Hp[MobId(1)] = 0f;

            // Act
            LogAssert.Expect(LogType.Error, new Regex(@"^\[EnemyAISystem\].*" + Regex.Escape(MobId(0).ToString())));
            Run(1u);

            // Assert
            CollectionAssert.AreEqual(
                new[] { Loot(MobId(0), 0), Died(MobId(0)), Loot(MobId(1), 0), Died(MobId(1)) },
                _log);
            Run((uint)(1 + LINGER));
            Assert.AreEqual(0, _system.Count);
            Assert.IsFalse(_store.TryGet(MobId(0), out _));
            Assert.IsFalse(_store.TryGet(MobId(1), out _));
            Assert.AreEqual(0, _warnings);
        }

        // QA 15
        [Test]
        public void test_run_deathTickAndDespawnTick_allocateNothingAfterWarmUp()
        {
            const int MOBS = 20;
            for (uint i = 0; i < MOBS; i++)
            {
                Spawn(MobId(i));
            }

            // Pre-create every dictionary key the measured region writes.
            for (uint i = 0; i < 4; i++)
            {
                _stats.Hp[MobId(i)] = 100f;
            }

            _bus.OnMobDied -= HandleBusMobDied;
            _loot.Log = null;
            var deathCtx0 = Death(MobId(0), Zone);
            var deathCtx2 = Death(MobId(2), Zone);

            uint t = 1u;
            for (; t <= 12u; t++)
            {
                Run(t);
            }

            // Warm-up death and despawn (both triggers) on mobs 2 and 3.
            _stats.Hp[MobId(3)] = 0f;
            _combat.Raise(deathCtx2);
            for (; t <= 12u + LINGER + 2u; t++)
            {
                Run(t);
            }

            Assert.AreEqual(MOBS - 2, _system.Count);

            // Measured: mob 0 dies through Combat, mob 1 through the HP check, both despawn within the window.
            _stats.Hp[MobId(1)] = 0f;
            long before = GC.GetAllocatedBytesForCurrentThread();
            _combat.Raise(deathCtx0);
            uint end = t + LINGER + 1u;
            for (; t <= end; t++)
            {
                Run(t);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(0L, after - before);
            Assert.AreEqual(MOBS - 4, _system.Count);
            Assert.AreEqual(4, _loot.Calls);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
