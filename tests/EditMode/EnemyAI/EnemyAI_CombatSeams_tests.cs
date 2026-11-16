using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.EnhancementSystem;
using IronGrind.Events;
using IronGrind.HitDetection;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.AutoAttackCombat;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.DamageCalculation;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode tests for Enemy AI Story 012: the combat and mob seams (target validity, damage applier, XP award, mob
    /// combat-state reader) and the end-to-end kill in a hand-built rig. The rig uses the real <see cref="ZoneMobRoster"/> with
    /// <see cref="ZoneMobRosterMobTypeSource"/> as the mob type source (no fake type source).
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_CombatSeams_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint TYPE_RAW = 1u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 8;
        private const int CAPACITY = 32;
        private const int LINGER = 10;
        private const int KILL_XP = 10;
        private const int ENRAGED_KILL_XP = 15;
        private const int MOB_HP = 100;
        private const int ENRAGED_MOB_HP = 150;
        private const int BASE_DAMAGE = 55;            // minus Defense 5 = 50 per hit
        private const int MAX_TICKS = 400;
        private const int ALLOC_CALLS = 1000;
        private const int ALLOC_HP = 5000;
        private const float DELTA = 0.05f;
        private const float RETURN_SPEED_CAP = 5f;
        private const double NORMAL_ROLL = 0.5;        // chance 0.5: 0.5 < 0.5 is false
        private const double ENRAGED_ROLL = 0.0;
        private const float NO_CRIT_ROLL = 0.99f;
        private const uint UNKNOWN_RAW = 900000u;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static MobTypeID MobType => new MobTypeID(TYPE_RAW);

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 1u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeAttackSpeed : IAttackSpeedSource
        {
            public float GetAttackSpeedMultiplier(EntityID entity) => 1.0f;
        }

        private sealed class FakeRegistry : IMobDefinitionRegistry
        {
            public readonly Dictionary<MobTypeID, MobDefinition> Defs = new Dictionary<MobTypeID, MobDefinition>();

            public MobDefinition GetDefinition(MobTypeID id) => Defs.TryGetValue(id, out MobDefinition d) ? d : null;
        }

        private sealed class QuietNav : INavigationProvider
        {
            public void SetDestination(EntityID agent, Vector3 target) { }

            public void SetSpeed(EntityID agent, float speed) { }

            public void Stop(EntityID agent) { }

            public void Resume(EntityID agent) { }

            public bool IsPathStale(EntityID agent) => false;

            public Vector3 GetCurrentVelocity(EntityID agent) => Vector3.zero;
        }

        private sealed class NoopResolver : IMobAttackResolver
        {
            public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange, Vector3 capturedForward) { }
        }

        private sealed class NoCandidates : IAggroCandidateSource
        {
            public void CopyPlayers(List<(EntityID Id, Vector3 Position)> into) { }

            public bool TryGetPlayerPosition(EntityID id, out Vector3 position)
            {
                position = Vector3.zero;
                return false;
            }
        }

        private sealed class CountingLoot : ILootTableService
        {
            public int Calls;

            public void RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage) { }

            public void ClearMob(EntityID mobEntityId) { }

            public void ResolveMobDrop(EntityID mobEntityID, int tierShift) => Calls++;

            public void Clear() { }
        }

        private sealed class CountingOutput : IAutoAttackDamageOutput
        {
            public int Count;

            public void Emit(EntityID attacker, EntityID target, in DamageResult result) => Count++;
        }

        private sealed class FixedCombatReader : IZoneEntityCombatReader
        {
            public const int STATE_CODE = 11;
            public const int TIMER = 22;
            public const int CYCLE = 33;

            public AutoAttackState GetState(EntityID entityId) => AutoAttackState.CombatActive;

            public int GetActionTimer(EntityID entityId) => TIMER;

            public int GetCycleTicks(EntityID entityId) => CYCLE;
        }

        private IronGrind.CharacterStats.CharacterStats _stats;
        private ZoneSlotPool _pool;
        private ZoneMobRoster _roster;
        private MobEventBus _bus;
        private FakeRegistry _registry;
        private ScriptedRandomProvider _rng;
        private MobRuntimeStore _store;
        private MobTargetValidity _validity;
        private MobRuntimeStoreXpAward _xp;
        private CharacterStatsAutoAttackDamageApplier _inner;
        private CountingOutput _output;
        private KillCallLog _kills;
        private FakeHitEmitter _hits;
        private FakeZoneState _zoneState;
        private AutoAttackCombatSystem _combat;
        private CountingLoot _loot;
        private EnemyAISystem _enemyAi;
        private int _combatDied;
        private int _busDied;
        private EntityID _player;
        private uint _tick;

        [SetUp]
        public void SetUp()
        {
            _stats = CharacterStatsFixture.Create();
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _roster = new ZoneMobRoster(_pool, new FakeTicks());
            _bus = new MobEventBus();
            _registry = new FakeRegistry();
            _registry.Defs[MobType] = Def();
            _rng = new ScriptedRandomProvider();
            for (int i = 0; i < MAX_TICKS; i++)
            {
                _rng.EnqueueFloat(NO_CRIT_ROLL);
            }

            _store = new MobRuntimeStore(Zone, _bus, _registry, _rng, new CharacterStatsMobStatWriter(_stats), CAPACITY);
            _validity = new MobTargetValidity(_pool, _store, _stats);
            _xp = new MobRuntimeStoreXpAward(_store);
            _inner = new CharacterStatsAutoAttackDamageApplier(_stats);
            _output = new CountingOutput();
            _kills = new KillCallLog();
            _hits = new FakeHitEmitter();
            _zoneState = new FakeZoneState();
            _loot = new CountingLoot();

            var calculator = new DamageCalculator(_stats, new FakeEquippedWeaponQuery(), new CountingItemDatabase(),
                new EnhancementBonusProvider(EnhancementConfig.Default), DamageCalculationConfig.Default, _rng);
            _combat = new AutoAttackCombatSystem(_pool, _validity, new FakeAttackSpeed(), _zoneState,
                new HitDetector(HitDetectionConfig.Default), _hits,
                new FakeStatSource { BaseDamage = BASE_DAMAGE, AttackRange = 3.0f },
                new DamageCalculatorAutoAttackAdapter(calculator), new DamageOutputApplier(_inner, _output), _xp,
                new FakeExperienceSink(_kills), new ZoneMobRosterMobTypeSource(_roster), Zone, new FakeFaceOutput());

            // Combat is built before Enemy AI (Story 011): Enemy AI subscribes to its OnMobDied.
            EnemyAIConfigValues config = EnemyAIConfigValues.Create(
                EnemyAIConfigValues.AGGRO_SCAN_INTERVAL_TICKS_DEFAULT,
                EnemyAIConfigValues.PURSUIT_REPOSITION_THRESHOLD_DEFAULT,
                EnemyAIConfigValues.RETURN_ARRIVAL_THRESHOLD_DEFAULT,
                LINGER);
            _enemyAi = new EnemyAISystem(Zone, _bus, _store, _registry, new QuietNav(),
                new CharacterStatsZoneEntityStatReader(_stats), new NoopResolver(), config, RETURN_SPEED_CAP,
                new NoCandidates(), _combat, _loot, CAPACITY);

            _tick = 0;
            _combatDied = 0;
            _busDied = 0;
            _combat.OnMobDied += HandleCombatDied;
            _bus.OnMobDied += HandleBusDied;

            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(11u), new AccountID(1011u), 11u, out _, out _player));
        }

        [TearDown]
        public void TearDown()
        {
            _combat.OnMobDied -= HandleCombatDied;
            _bus.OnMobDied -= HandleBusDied;
            _enemyAi.Dispose();
            _store.Dispose();
        }

        private void HandleCombatDied(MobDeathContext context) => _combatDied++;

        private void HandleBusDied(MobDiedArgs args) => _busDied++;

        // Enraged chance 0.5: a roll of 0.0 is Enraged, 0.5 is not.
        private static MobDefinition Def()
        {
            return new MobDefinition(
                mobTypeID: MobType,
                displayName: "Test Mob",
                baseMaxHP: MOB_HP,
                baseAttackPower: 10,
                baseDefense: 5,
                attackRange: 2f,
                moveSpeed: 3.5f,
                aggroRange: 8f,
                leashRange: 20f,
                attackWindUpTicks: 12,
                attackRecoveryTicks: 6,
                attackCooldownTicks: 10,
                killXP: KILL_XP,
                mobTier: MobTier.Common,
                enragedChance: 0.5f,
                enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f,
                enragedXPMultiplier: 1.5f,
                enragedLootQualityBonusTier: 1);
        }

        // A roster mob (so the type source knows it) spawned through the bus (so the store seeds it).
        private EntityID SpawnMob(bool enraged = false)
        {
            EntityID id = _roster.TryAddMob(Zone, MobType, Vector3.zero);
            Assert.AreNotEqual(EntityID.Invalid, id);
            _rng.EnqueueDouble(enraged ? ENRAGED_ROLL : NORMAL_ROLL);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, id, MobType, Vector3.zero));
            return id;
        }

        private static DamageResult Hit(int damage) => AutoAttackTestFakes.Damage(damage, false);

        private void RunUntilDied(int expectedDied)
        {
            for (int i = 0; i < MAX_TICKS && _combatDied < expectedDied; i++)
            {
                _tick++;
                _combat.Run(_tick, DELTA);
            }
        }

        private void Attack(EntityID mob)
        {
            _combat.SetAutoAttack(_player, true);
            _combat.SetTarget(_player, mob);
        }

        // QA 1
        [Test]
        public void test_validity_playerId_isInvalid()
        {
            Assert.IsFalse(_validity.IsValidTarget(_player));
        }

        // QA 2
        [Test]
        public void test_validity_liveMobWithStoreEntry_isValid()
        {
            EntityID mob = SpawnMob();

            Assert.IsTrue(_validity.IsValidTarget(mob));
        }

        // QA 3
        [Test]
        public void test_validity_mobSlotWithoutStoreEntry_isInvalid()
        {
            EntityID mob = _roster.TryAddMob(Zone, MobType, Vector3.zero);
            _stats.SetBaseStat(mob, StatID.MaxHP, MOB_HP);
            _stats.SetCurrentHP(mob, MOB_HP);

            Assert.IsTrue(_pool.TryGetMobSlot(mob, out ushort _));
            Assert.IsFalse(_store.TryGet(mob, out MobRuntimeEntry _));
            Assert.Greater(_stats.GetCurrentHP(mob), 0f);
            Assert.IsFalse(_validity.IsValidTarget(mob));
        }

        // Pool check. MobRuntimeStore seeds from any spawn event of its own zone and does not consult a pool, so a store
        // entry for an id minted by a SECOND pool (distinct first id, no overlap with the first pool) is made through the bus.
        [Test]
        public void test_validity_mobSlotInOtherPool_isInvalid()
        {
            var otherPool = new ZoneSlotPool(new ZoneID(ZONE_RAW + 1u), new FakeTicks(), new FakePoolListener(),
                MAX_PLAYERS, MAX_MOBS, 50000u);
            Assert.IsTrue(otherPool.TryAllocateMob(new ZoneID(ZONE_RAW + 1u), out ushort _, out EntityID foreign));
            _rng.EnqueueDouble(NORMAL_ROLL);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, foreign, MobType, Vector3.zero));

            Assert.IsTrue(otherPool.TryGetMobSlot(foreign, out ushort _));
            Assert.IsFalse(_pool.TryGetMobSlot(foreign, out ushort _));
            Assert.IsTrue(_store.TryGet(foreign, out MobRuntimeEntry _));
            Assert.Greater(_stats.GetCurrentHP(foreign), 0f);
            Assert.IsFalse(_validity.IsValidTarget(foreign));
        }

        // QA 4
        [Test]
        public void test_validity_unknownOrForeignId_isInvalid()
        {
            Assert.IsFalse(_validity.IsValidTarget(new EntityID(UNKNOWN_RAW)));
            Assert.IsFalse(_validity.IsValidTarget(EntityID.Invalid));
        }

        // QA 5
        [Test]
        public void test_validity_mobAtZeroHp_isInvalid()
        {
            EntityID mob = SpawnMob();
            _stats.ApplyDamage(mob, 1000f);

            Assert.AreEqual(0f, _stats.GetCurrentHP(mob));
            Assert.IsFalse(_validity.IsValidTarget(mob));
        }

        // QA 6
        [Test]
        public void test_applier_partialDamage_reducesHpOnly()
        {
            EntityID mob = SpawnMob();
            int maxHpBefore = _stats.GetEffectiveStat(mob, StatID.MaxHP);
            int defenseBefore = _stats.GetEffectiveStat(mob, StatID.Defense);
            DamageResult result = Hit(30);

            _inner.Apply(_player, mob, in result);

            Assert.AreEqual(70f, _stats.GetCurrentHP(mob));
            Assert.AreEqual(maxHpBefore, _stats.GetEffectiveStat(mob, StatID.MaxHP));
            Assert.AreEqual(defenseBefore, _stats.GetEffectiveStat(mob, StatID.Defense));
        }

        // QA 7
        [Test]
        public void test_applier_killingHit_hpReachesZero()
        {
            EntityID mob = SpawnMob();
            DamageResult result = Hit(MOB_HP + 50);

            _inner.Apply(_player, mob, in result);

            Assert.AreEqual(0f, _stats.GetCurrentHP(mob));
        }

        // QA 8
        [Test]
        public void test_xp_normalMob_returnsKillXp()
        {
            EntityID mob = SpawnMob();

            Assert.AreEqual(KILL_XP, _xp.GetXPAward(mob));
        }

        // QA 9
        [Test]
        public void test_xp_enragedMob_returnsEnragedXp()
        {
            EntityID mob = SpawnMob(enraged: true);

            Assert.IsTrue(_store.TryGet(mob, out MobRuntimeEntry entry));
            Assert.IsTrue(entry.Variant.IsEnraged);
            Assert.AreEqual(Mathf.RoundToInt(KILL_XP * 1.5f), _xp.GetXPAward(mob));
            Assert.AreEqual(ENRAGED_KILL_XP, entry.Variant.EffectiveKillXP);
        }

        // QA 10
        [Test]
        public void test_xp_noStoreEntry_returnsZeroAndOneWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[MobRuntimeStoreXpAward\]"));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[MobRuntimeStoreXpAward\]"));

            int first = _xp.GetXPAward(new EntityID(UNKNOWN_RAW));
            int second = _xp.GetXPAward(new EntityID(UNKNOWN_RAW));

            Assert.AreEqual(0, first);
            Assert.AreEqual(0, second);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_constructors_nullArguments_throw()
        {
            Assert.Throws<ArgumentNullException>(() => new MobTargetValidity(null, _store, _stats));
            Assert.Throws<ArgumentNullException>(() => new MobTargetValidity(_pool, null, _stats));
            Assert.Throws<ArgumentNullException>(() => new MobTargetValidity(_pool, _store, null));
            Assert.Throws<ArgumentNullException>(() => new MobRuntimeStoreXpAward(null));
            Assert.Throws<ArgumentNullException>(() => new CharacterStatsAutoAttackDamageApplier(null));
        }

        // QA 11
        [Test]
        public void test_mobTypeSource_rosterMobKnownPlayerNot()
        {
            var source = new ZoneMobRosterMobTypeSource(_roster);
            EntityID mob = SpawnMob();

            Assert.IsTrue(source.TryGetMobType(mob, out MobTypeID type));
            Assert.AreEqual(MobType, type);
            Assert.IsFalse(source.TryGetMobType(_player, out MobTypeID _));
        }

        // QA 12
        [TestCase(MobState.Dormant, AutoAttackState.Idle)]
        [TestCase(MobState.Pursuing, AutoAttackState.CombatActive)]
        [TestCase(MobState.WindingUp, AutoAttackState.CombatActive)]
        [TestCase(MobState.AttackExecution, AutoAttackState.CombatActive)]
        [TestCase(MobState.Recovering, AutoAttackState.CombatActive)]
        [TestCase(MobState.Returning, AutoAttackState.Idle)]
        [TestCase(MobState.Dead, AutoAttackState.Idle)]
        public void test_reader_mobState_mapsToAutoAttackStateWithZeroTimers(MobState mobState, AutoAttackState expected)
        {
            var reader = new MobAwareZoneEntityCombatReader(_enemyAi, new FixedCombatReader());
            EntityID mob = SpawnMob();
            Assert.IsTrue(_enemyAi.TryGetController(mob, out MobController controller));
            controller.ForceStateForTest(mobState);

            Assert.IsTrue(_enemyAi.TryGetMobState(mob, out MobState read));
            Assert.AreEqual(mobState, read);
            Assert.AreEqual(expected, reader.GetState(mob));
            Assert.AreEqual(0, reader.GetActionTimer(mob));
            Assert.AreEqual(0, reader.GetCycleTicks(mob));
        }

        // QA 13
        [Test]
        public void test_reader_playerAndUnknownIds_delegateToInner()
        {
            var reader = new MobAwareZoneEntityCombatReader(_enemyAi, new FixedCombatReader());

            foreach (EntityID id in new[] { _player, new EntityID(UNKNOWN_RAW) })
            {
                Assert.IsFalse(_enemyAi.TryGetMobState(id, out MobState state));
                Assert.AreEqual(MobState.Dormant, state);
                Assert.AreEqual(AutoAttackState.CombatActive, reader.GetState(id));
                Assert.AreEqual(FixedCombatReader.TIMER, reader.GetActionTimer(id));
                Assert.AreEqual(FixedCombatReader.CYCLE, reader.GetCycleTicks(id));
            }
        }

        [Test]
        public void test_reader_nullArguments_throw()
        {
            Assert.Throws<ArgumentNullException>(() => new MobAwareZoneEntityCombatReader(null, new FixedCombatReader()));
            Assert.Throws<ArgumentNullException>(() => new MobAwareZoneEntityCombatReader(_enemyAi, null));
        }

        // QA 14
        [Test]
        public void test_endToEnd_playerKillsMob_xpOnceDiedOnceRoutineOnceBusOnce()
        {
            EntityID mob = SpawnMob();
            Attack(mob);

            RunUntilDied(1);

            Assert.AreEqual(1, _combatDied);
            AssertKillBookkeeping(mob, KILL_XP);
            Assert.AreEqual(0f, _stats.GetCurrentHP(mob));
            Assert.IsTrue(_enemyAi.TryGetMobState(mob, out MobState state));
            Assert.AreEqual(MobState.Dead, state);
            Assert.AreEqual(2, _output.Count);   // 50 + 50 on 100 HP
        }

        // QA 15
        [Test]
        public void test_endToEnd_enragedMob_awardsEnragedXp()
        {
            EntityID mob = SpawnMob(enraged: true);
            Assert.AreEqual((float)ENRAGED_MOB_HP, _stats.GetCurrentHP(mob));
            Attack(mob);

            RunUntilDied(1);

            Assert.AreEqual(1, _combatDied);
            AssertKillBookkeeping(mob, ENRAGED_KILL_XP);
            Assert.AreEqual(3, _output.Count);   // 50 x 3 on 150 HP
        }

        // QA 16
        [Test]
        public void test_endToEnd_attackOnCorpse_noDamageNoXpNoSecondDeath()
        {
            EntityID mob = SpawnMob();
            Attack(mob);
            RunUntilDied(1);
            int outputBefore = _output.Count;
            int hitsBefore = _hits.Count;

            // Enemy AI is never ticked in this rig, so the corpse stays within the death linger: the store entry and the pool
            // slot are still held and HP 0 is the only reason for the rejection.
            Assert.IsTrue(_store.TryGet(mob, out MobRuntimeEntry _));
            Assert.IsTrue(_pool.TryGetMobSlot(mob, out ushort _));

            Attack(mob);
            for (int i = 0; i < 100; i++)
            {
                _tick++;
                _combat.Run(_tick, DELTA);
            }

            Assert.IsTrue(_store.TryGet(mob, out MobRuntimeEntry _));
            Assert.IsTrue(_pool.TryGetMobSlot(mob, out ushort _));
            Assert.IsFalse(_validity.IsValidTarget(mob));
            Assert.AreEqual(outputBefore, _output.Count);
            Assert.AreEqual(hitsBefore, _hits.Count);
            AssertKillBookkeeping(mob, KILL_XP);
            Assert.AreEqual(1, _combatDied);
        }

        private void AssertKillBookkeeping(EntityID mob, int expectedXp)
        {
            Assert.AreEqual(1, _kills.CountOf(KillCallKind.Add));
            Assert.AreEqual(expectedXp, _kills.Entries[0].Amount);
            Assert.AreEqual(_player, _kills.Entries[0].Attacker);
            Assert.AreEqual(1, _loot.Calls);
            Assert.AreEqual(1, _busDied);
            Assert.IsTrue(_enemyAi.TryGetMobState(mob, out MobState state));
            Assert.AreEqual(MobState.Dead, state);
            // Call order (AddExperience before ApplyDamage) is not observable here: KillCallLog only sees the XP sink, the
            // damage applier is the real CharacterStats one.
        }

        // QA 17
        [Test]
        public void test_seams_afterWarmUp_allocateNothing()
        {
            EntityID mob = SpawnMob();
            var reader = new MobAwareZoneEntityCombatReader(_enemyAi, new FixedCombatReader());
            EntityID unknown = new EntityID(UNKNOWN_RAW);
            DamageResult oneDamage = Hit(1);
            _stats.SetBaseStat(mob, StatID.MaxHP, ALLOC_HP);
            _stats.SetCurrentHP(mob, ALLOC_HP);

            // Warm-up.
            bool sink = _validity.IsValidTarget(mob) | _validity.IsValidTarget(_player) | _validity.IsValidTarget(unknown);
            _inner.Apply(_player, mob, in oneDamage);
            int total = _xp.GetXPAward(mob);
            sink |= _enemyAi.TryGetMobState(mob, out MobState state) | _enemyAi.TryGetMobState(unknown, out state);
            total += (int)reader.GetState(mob) + reader.GetActionTimer(mob) + reader.GetCycleTicks(mob);
            total += (int)reader.GetState(_player) + reader.GetActionTimer(_player) + reader.GetCycleTicks(_player);

            int validMob = 0;
            int validOther = 0;
            int xpTotal = 0;
            int stateHits = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < ALLOC_CALLS; i++)
            {
                validMob += _validity.IsValidTarget(mob) ? 1 : 0;
                validOther += _validity.IsValidTarget(_player) ? 1 : 0;
                validOther += _validity.IsValidTarget(unknown) ? 1 : 0;
                _inner.Apply(_player, mob, in oneDamage);
                xpTotal += _xp.GetXPAward(mob);
                stateHits += _enemyAi.TryGetMobState(mob, out state) && state == MobState.Dormant ? 1 : 0;
                total += (int)reader.GetState(mob) + reader.GetActionTimer(mob) + reader.GetCycleTicks(mob);
                total += (int)reader.GetState(_player) + reader.GetActionTimer(_player) + reader.GetCycleTicks(_player);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(0L, after - before);
            Assert.IsTrue(sink || total != 0);   // warm-up values stay observable
            Assert.AreEqual(ALLOC_CALLS, validMob);
            Assert.AreEqual(0, validOther);
            Assert.AreEqual(ALLOC_CALLS * KILL_XP, xpTotal);
            Assert.AreEqual(ALLOC_CALLS, stateHits);
            Assert.AreEqual((float)(ALLOC_HP - 1 - ALLOC_CALLS), _stats.GetCurrentHP(mob));
            Assert.IsTrue(_validity.IsValidTarget(mob));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
