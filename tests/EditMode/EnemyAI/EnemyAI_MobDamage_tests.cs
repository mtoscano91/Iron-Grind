using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.HitDetection;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.Randomness;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.DamageCalculation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode integration tests for Enemy AI Story 010: <see cref="MobAttackResolver"/> over the real hit detector,
    /// damage calculator, Character Stats and both emitters (AC-1 to AC-6; QA cases 1 to 14 of
    /// production/epics/enemy-ai/story-010-mob-to-player-damage.md). Crit chance is 0 throughout, so the roll never matters.
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_MobDamage_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint CLIENT_DEFENDER = 11u;
        private const uint CLIENT_BYSTANDER = 12u;
        private const uint TICK = 4242u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const int PLAYER_MAX_HP = 100;
        private const float RANGE = 2f;
        private const float FIXED_ROLL = 0.5f;
        private const int ATTACK_POWER = 10;
        private const int ALLOC_CALLS = 10;
        private const int ALLOC_MAX_HP = 1000;

        private static readonly Vector3 FORWARD = new Vector3(1f, 0f, 0f);
        private static readonly Vector3 NEAR = new Vector3(1.5f, 0f, 0f);
        private static readonly Vector3 FAR = new Vector3(5f, 0f, 0f);
        private static readonly Vector3 BEHIND = new Vector3(-1f, 0f, 0f);

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => TICK;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class Sent
        {
            public readonly uint ClientId;
            public readonly byte[] Data;

            public Sent(uint clientId, byte[] data)
            {
                ClientId = clientId;
                Data = data;
            }
        }

        private sealed class FakeSeam : IClientMessageSendSeam
        {
            public readonly List<Sent> Packets = new List<Sent>();

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                Packets.Add(new Sent(clientId, message.ToArray()));
            }
        }

        private sealed class NullCollector : IRUBatchCollector
        {
            public void Enqueue(uint clientId, RUBatchCategory category, ushort messageTypeId, ReadOnlySpan<byte> body) { }

            public void EnqueueDamageEvent(uint clientId, in DamageEvent damageEvent) { }
        }

        private sealed class FixedRandom : IRandomProvider
        {
            public float Value = FIXED_ROLL;

            public float NextFloat() => Value;

            public double NextDouble() => Value;

            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        }

        private sealed class RecordingCalculator : IAutoAttackDamageCalculator
        {
            private readonly IAutoAttackDamageCalculator _inner;
            public int Calls;

            public RecordingCalculator(IAutoAttackDamageCalculator inner)
            {
                _inner = inner;
            }

            public DamageResult Calculate(int baseDamage, EntityID attacker, EntityID target)
            {
                Calls++;
                return _inner.Calculate(baseDamage, attacker, target);
            }
        }

        private sealed class RecordingOutput : IAutoAttackDamageOutput
        {
            public int Calls;

            public void Emit(EntityID attacker, EntityID target, in DamageResult result)
            {
                Calls++;
            }
        }

        private sealed class StatsReader : IZoneEntityStatReader
        {
            private readonly IronGrind.CharacterStats.CharacterStats _stats;

            public StatsReader(IronGrind.CharacterStats.CharacterStats stats)
            {
                _stats = stats;
            }

            public float GetCurrentHP(EntityID entityId) => _stats.GetCurrentHP(entityId);

            public float GetCurrentMP(EntityID entityId) => 0f;

            public int GetEffectiveStat(EntityID entityId, StatID statId) => _stats.GetEffectiveStat(entityId, statId);
        }

        private sealed class NullNav : INavigationProvider
        {
            public void SetDestination(EntityID agent, Vector3 target) { }

            public void SetSpeed(EntityID agent, float speed) { }

            public void Stop(EntityID agent) { }

            public void Resume(EntityID agent) { }

            public bool IsPathStale(EntityID agent) => false;

            public Vector3 GetCurrentVelocity(EntityID agent) => Vector3.zero;
        }

        private sealed class OnePlayerCandidates : IAggroCandidateSource
        {
            public EntityID Player;
            public Vector3 Position;

            public void CopyPlayers(List<(EntityID Id, Vector3 Position)> into)
            {
                into.Add((Player, Position));
            }

            public bool TryGetPlayerPosition(EntityID id, out Vector3 position)
            {
                position = Position;
                return id.Equals(Player);
            }
        }

        private FakeTicks _ticks;
        private ZoneSlotPool _pool;
        private FakeSeam _seam;
        private ClientMessageOutbox _outbox;
        private RUBatchOutboundStep _step;
        private IronGrind.CharacterStats.CharacterStats _stats;
        private FakeEquippedWeaponQuery _weapons;
        private CountingItemDatabase _items;
        private RecordingBonusProvider _bonuses;
        private FixedRandom _random;
        private DamageCalculatorAutoAttackAdapter _adapter;
        private MobAttackResolver _resolver;
        private EntityID _defender;
        private EntityID _bystander;
        private EntityID _mob;
        private int _diedCount;
        private IronGrind.CharacterStats.CharacterStats.EntityDiedHandler _diedHandler;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks();
            _pool = new ZoneSlotPool(Zone, _ticks, new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _seam = new FakeSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _outbox.AddClient(CLIENT_DEFENDER);
            _outbox.AddClient(CLIENT_BYSTANDER);
            _step = new RUBatchOutboundStep(_seam, _outbox, MAX_PLAYERS);
            _defender = AddPlayer(CLIENT_DEFENDER);
            _bystander = AddPlayer(CLIENT_BYSTANDER);
            _mob = AddMob();

            _stats = CharacterStatsFixture.Create();
            _diedCount = 0;
            _diedHandler = OnEntityDied;
            _stats.Subscribe(_diedHandler);
            GivenPlayerHp(PLAYER_MAX_HP, 0);
            _stats.SetBaseStat(_mob, StatID.AttackPower, ATTACK_POWER);

            _weapons = new FakeEquippedWeaponQuery();
            _items = new CountingItemDatabase();
            _bonuses = new RecordingBonusProvider();
            _random = new FixedRandom();
            var calculator = new DamageCalculator(_stats, _weapons, _items, _bonuses, DamageCalculationConfig.Default, _random);
            _adapter = new DamageCalculatorAutoAttackAdapter(calculator);
            _resolver = BuildResolver(_adapter, _step);
        }

        [TearDown]
        public void TearDown()
        {
            _stats?.Unsubscribe(_diedHandler);
        }

        private void OnEntityDied(EntityID entityId)
        {
            _diedCount++;
        }

        private MobAttackResolver BuildResolver(IAutoAttackDamageCalculator calculator, IRUBatchCollector collector)
        {
            return new MobAttackResolver(
                new HitDetector(HitDetectionConfig.Default),
                new HitResultEmitter(_pool, collector, _ticks),
                calculator,
                _stats,
                new AutoAttackDamageOutputEmitter(_pool, Zone, _outbox, collector));
        }

        private EntityID AddPlayer(uint client)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u), client, out _, out EntityID id));
            return id;
        }

        private EntityID AddMob()
        {
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out EntityID id));
            return id;
        }

        private void GivenPlayerHp(float hp, int defense)
        {
            _stats.SetBaseStat(_defender, StatID.MaxHP, PLAYER_MAX_HP);
            _stats.SetCurrentHP(_defender, hp);
            _stats.SetBaseStat(_defender, StatID.Defense, defense);
        }

        private void Attack(Vector3 targetPos, Vector3 forward)
        {
            _resolver.ResolveAttack(_mob, _defender, Vector3.zero, targetPos, RANGE, forward);
        }

        private void FlushAll()
        {
            _outbox.Flush(TICK);
            _step.Flush(TICK);
        }

        // Decodes every HitCheckResult and DamageEvent in the client's batch packets; any other sub-message fails the test.
        private void Decode(uint client, List<HitCheckResult> hits, List<DamageEvent> damages)
        {
            foreach (Sent packet in _seam.Packets)
            {
                if (packet.ClientId != client || !MessageEnvelopeCodec.TryRead(packet.Data, out ServerMessageEnvelope e)
                    || e.MessageTypeId != RUBatchWriter.TICK_BATCH_RU)
                {
                    continue;
                }

                Assert.IsTrue(BatchHeaderCodec.TryRead(packet.Data, out _, out ushort count));
                int offset = BatchHeaderCodec.HeaderSize;
                for (int i = 0; i < count; i++)
                {
                    var rest = new ReadOnlySpan<byte>(packet.Data, offset, packet.Data.Length - offset);
                    Assert.IsTrue(BatchSubMessageFraming.TryReadHeader(rest, out _, out ushort type));
                    int read;
                    if (type == HitCheckResult.MessageTypeId)
                    {
                        Assert.IsTrue(BatchSubMessageCodec.TryReadHitCheckResult(rest, out HitCheckResult hit, out read));
                        hits.Add(hit);
                    }
                    else
                    {
                        Assert.AreEqual(DamageEvent.MessageTypeId, type, "unexpected sub-message type");
                        Assert.IsTrue(BatchSubMessageCodec.TryReadDamageEvent(rest, out DamageEvent damage, out read));
                        damages.Add(damage);
                    }

                    offset += read;
                }
            }
        }

        private List<HitCheckResult> HitsFor(uint client)
        {
            var hits = new List<HitCheckResult>();
            Decode(client, hits, new List<DamageEvent>());
            return hits;
        }

        private List<DamageEvent> DamagesFor(uint client)
        {
            var damages = new List<DamageEvent>();
            Decode(client, new List<HitCheckResult>(), damages);
            return damages;
        }

        private int SelfDamagePackets()
        {
            return _seam.Packets.FindAll(p => MessageEnvelopeCodec.TryRead(p.Data, out ServerMessageEnvelope e)
                && e.MessageTypeId == SelfDamageEvent.MessageTypeId).Count;
        }

        // QA 1
        [Test]
        public void test_mob_hit_applies_attack_power_minus_defense_and_delivers_both_messages()
        {
            GivenPlayerHp(PLAYER_MAX_HP, 3);

            Attack(NEAR, FORWARD);
            FlushAll();

            Assert.AreEqual(PLAYER_MAX_HP - 7f, _stats.GetCurrentHP(_defender));
            var expectedHit = new HitCheckResult(_mob, _defender, HitResult.Hit, TICK);
            CollectionAssert.AreEqual(new[] { expectedHit }, HitsFor(CLIENT_DEFENDER));
            Assert.AreEqual(0, HitsFor(CLIENT_BYSTANDER).Count, "HitCheckResult goes to the defender only");
            var expectedDamage = new DamageEvent(_mob, _defender, 7, false, DamageType.Physical);
            CollectionAssert.AreEqual(new[] { expectedDamage }, DamagesFor(CLIENT_DEFENDER));
            CollectionAssert.AreEqual(new[] { expectedDamage }, DamagesFor(CLIENT_BYSTANDER));
        }

        // QA 2
        [Test]
        public void test_mob_hit_defense_above_attack_power_deals_minimum_damage()
        {
            GivenPlayerHp(PLAYER_MAX_HP, 999);
            _stats.SetBaseStat(_mob, StatID.AttackPower, 100);
            int expected = Mathf.Max(1, Mathf.FloorToInt(100 * DamageCalculationConfig.Default.MinDamageFraction));

            Attack(NEAR, FORWARD);

            Assert.AreEqual(PLAYER_MAX_HP - (float)expected, _stats.GetCurrentHP(_defender));
            FlushAll();
            Assert.AreEqual(expected, DamagesFor(CLIENT_DEFENDER)[0].FinalDamage);
        }

        // QA 3
        [Test]
        public void test_mob_attack_out_of_range_emits_miss_and_applies_nothing()
        {
            var calculator = new RecordingCalculator(_adapter);
            _resolver = BuildResolver(calculator, _step);

            Attack(FAR, FORWARD);
            FlushAll();

            Assert.AreEqual(PLAYER_MAX_HP, _stats.GetCurrentHP(_defender));
            Assert.AreEqual(0, calculator.Calls);
            Assert.AreEqual(0, SelfDamagePackets());
            CollectionAssert.AreEqual(new[] { new HitCheckResult(_mob, _defender, HitResult.MissOutOfRange, TICK) }, HitsFor(CLIENT_DEFENDER));
            Assert.AreEqual(0, DamagesFor(CLIENT_DEFENDER).Count);
            Assert.AreEqual(0, DamagesFor(CLIENT_BYSTANDER).Count);
        }

        // QA 4
        [Test]
        public void test_mob_attack_target_behind_captured_forward_emits_miss_facing()
        {
            var calculator = new RecordingCalculator(_adapter);
            var output = new RecordingOutput();
            _resolver = new MobAttackResolver(new HitDetector(HitDetectionConfig.Default),
                new HitResultEmitter(_pool, _step, _ticks), calculator, _stats, output);

            Attack(BEHIND, FORWARD);
            FlushAll();

            Assert.AreEqual(PLAYER_MAX_HP, _stats.GetCurrentHP(_defender));
            Assert.AreEqual(0, calculator.Calls);
            Assert.AreEqual(0, output.Calls);
            CollectionAssert.AreEqual(new[] { new HitCheckResult(_mob, _defender, HitResult.MissFacing, TICK) }, HitsFor(CLIENT_DEFENDER));
            Assert.AreEqual(0, DamagesFor(CLIENT_DEFENDER).Count);
        }

        // QA 5
        [Test]
        public void test_mob_attack_uses_the_captured_forward_not_the_direction_to_the_target()
        {
            // The target is 90 degrees off a +Z forward (outside the 45 degree tolerance) but dead ahead of +X.
            Attack(NEAR, new Vector3(0f, 0f, 1f));
            Attack(NEAR, FORWARD);
            FlushAll();

            List<HitCheckResult> hits = HitsFor(CLIENT_DEFENDER);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(HitResult.MissFacing, hits[0].Result);
            Assert.AreEqual(HitResult.Hit, hits[1].Result);
        }

        // QA 6
        [Test]
        public void test_mob_attack_two_consecutive_misses_emit_two_hit_check_results()
        {
            Attack(FAR, FORWARD);
            Attack(FAR, FORWARD);
            FlushAll();

            List<HitCheckResult> hits = HitsFor(CLIENT_DEFENDER);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(HitResult.MissOutOfRange, hits[0].Result);
            Assert.AreEqual(HitResult.MissOutOfRange, hits[1].Result);
        }

        // QA 7
        [Test]
        public void test_mob_attack_with_zero_attack_power_hits_but_deals_no_damage()
        {
            _stats.SetBaseStat(_mob, StatID.AttackPower, 0);
            LogAssert.Expect(LogType.Error, new Regex("BaseDamage must be at least 1"));

            Attack(NEAR, FORWARD);
            FlushAll();

            Assert.AreEqual(PLAYER_MAX_HP, _stats.GetCurrentHP(_defender));
            CollectionAssert.AreEqual(new[] { new HitCheckResult(_mob, _defender, HitResult.Hit, TICK) }, HitsFor(CLIENT_DEFENDER));
            Assert.AreEqual(0, DamagesFor(CLIENT_DEFENDER).Count);
            Assert.AreEqual(0, DamagesFor(CLIENT_BYSTANDER).Count);
        }

        // QA 8
        [Test]
        public void test_mob_hit_uses_the_attack_power_stat_so_an_enraged_mob_deals_more()
        {
            EntityID enraged = AddMob();
            _stats.SetBaseStat(enraged, StatID.AttackPower, 13); // 10 x 1.3, the Story 007 Enraged scaling

            Attack(NEAR, FORWARD);
            float afterNormal = _stats.GetCurrentHP(_defender);
            _resolver.ResolveAttack(enraged, _defender, Vector3.zero, NEAR, RANGE, FORWARD);
            float afterEnraged = _stats.GetCurrentHP(_defender);

            Assert.AreEqual(PLAYER_MAX_HP - ATTACK_POWER, afterNormal);
            Assert.AreEqual(13f, afterNormal - afterEnraged);
        }

        // QA 9
        [Test]
        public void test_mob_hit_killing_the_player_raises_died_once_and_second_attack_changes_nothing()
        {
            GivenPlayerHp(5, 3);

            Attack(NEAR, FORWARD);
            Assert.AreEqual(0f, _stats.GetCurrentHP(_defender));
            Assert.AreEqual(1, _diedCount);

            // A direct second swing on the dead defender: the resolver guards on HP before Calculate, so no calculator
            // error is logged, ApplyDamage is not reached and no DamageEvent is sent.
            Attack(NEAR, FORWARD);
            FlushAll();

            Assert.AreEqual(0f, _stats.GetCurrentHP(_defender));
            Assert.AreEqual(1, _diedCount);
            List<HitCheckResult> hits = HitsFor(CLIENT_DEFENDER);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(HitResult.Hit, hits[1].Result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_swingOnDeadDefender_sendsNoDamageEvent()
        {
            GivenPlayerHp(5, 3);
            var calculator = new RecordingCalculator(_adapter);
            _resolver = BuildResolver(calculator, _step);
            Attack(NEAR, FORWARD);
            Assert.AreEqual(1, calculator.Calls, "precondition: the killing hit was calculated");

            Attack(NEAR, FORWARD);
            FlushAll();

            Assert.AreEqual(1, calculator.Calls, "the dead-defender swing never reaches the calculator");
            Assert.AreEqual(1, DamagesFor(CLIENT_DEFENDER).Count, "only the killing hit sends a DamageEvent");
            Assert.AreEqual(1, DamagesFor(CLIENT_BYSTANDER).Count);
            LogAssert.NoUnexpectedReceived();
        }

        // QA 10 (tick arithmetic: the aggro loop ends with both mobs Pursuing; the next tick enters WindingUp; the wind-up
        // ticks that follow end with both mobs reaching AttackExecution on the LAST iteration, first mob ticked first, so its
        // swing kills the player and the second mob's CheckTarget sees a dead target and skips the resolver.)
        [Test]
        public void test_two_mobs_attacking_one_player_at_hp_1_in_the_same_tick_only_the_first_deals_damage()
        {
            GivenPlayerHp(1, 0);
            EntityID mobB = AddMob();
            _stats.SetBaseStat(mobB, StatID.AttackPower, ATTACK_POWER);
            var candidates = new OnePlayerCandidates { Player = _defender, Position = FAR };
            var reader = new StatsReader(_stats);
            var definition = new MobDefinition(
                mobTypeID: new MobTypeID(1u), displayName: "Test Mob", baseMaxHP: 100, baseAttackPower: ATTACK_POWER,
                baseDefense: 5, attackRange: RANGE, moveSpeed: 3.5f, aggroRange: 8f, leashRange: 20f,
                attackWindUpTicks: 4, attackRecoveryTicks: 6, attackCooldownTicks: 10, killXP: 10,
                mobTier: MobTier.Common, enragedChance: 0.07f, enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f, enragedXPMultiplier: 1.5f, enragedLootQualityBonusTier: 1);
            MobController first = NewController(_mob, definition, reader, candidates);
            MobController second = NewController(mobB, definition, reader, candidates);
            // The mobs are never asked to hold HP of their own here: seed it so neither starts Dead.
            _stats.SetBaseStat(_mob, StatID.MaxHP, PLAYER_MAX_HP);
            _stats.SetCurrentHP(_mob, PLAYER_MAX_HP);
            _stats.SetBaseStat(mobB, StatID.MaxHP, PLAYER_MAX_HP);
            _stats.SetCurrentHP(mobB, PLAYER_MAX_HP);

            uint tick = 0;
            for (int i = 0; i < EnemyAIConfigValues.AGGRO_SCAN_INTERVAL_TICKS_DEFAULT; i++)
            {
                tick++;
                first.Tick(tick);
                second.Tick(tick);
            }

            candidates.Position = NEAR;
            for (int i = 0; i < 1 + definition.AttackWindUpTicks; i++)
            {
                tick++;
                first.Tick(tick);
                second.Tick(tick);
            }

            FlushAll();
            Assert.AreEqual(0f, _stats.GetCurrentHP(_defender));
            Assert.AreEqual(1, _diedCount);
            List<HitCheckResult> hits = HitsFor(CLIENT_DEFENDER);
            Assert.AreEqual(1, hits.Count, "the second mob skipped the resolver for the dead target");
            Assert.AreEqual(_mob, hits[0].AttackerEntityId);
            Assert.AreEqual(1, DamagesFor(CLIENT_BYSTANDER).Count);
        }

        private MobController NewController(EntityID id, MobDefinition definition, IZoneEntityStatReader reader, IAggroCandidateSource candidates)
        {
            return new MobController(id, definition, Vector3.zero, new NullNav(), reader, _resolver,
                EnemyAIConfigValues.Defaults, NavigationConfigValues.RETURN_SPEED_CAP_DEFAULT, candidates);
        }

        // QA 11
        [Test]
        public void test_mob_hit_enqueues_no_self_damage_event_and_defender_gets_the_broadcast()
        {
            Attack(NEAR, FORWARD);
            FlushAll();

            Assert.AreEqual(0, SelfDamagePackets());
            Assert.AreEqual(1, DamagesFor(CLIENT_DEFENDER).Count);
        }

        // QA 12 (counting fakes asserting zero calls are equivalent to fakes that throw when called)
        [Test]
        public void test_mob_hit_never_touches_the_item_database_or_enhancement_provider()
        {
            Attack(NEAR, FORWARD);

            Assert.Less(_stats.GetCurrentHP(_defender), (float)PLAYER_MAX_HP, "the hit still resolved");
            Assert.AreEqual(ItemID.Invalid, _weapons.WeaponId);
            Assert.AreEqual(0, _items.GetItemCallCount);
            Assert.AreEqual(0, _items.TryGetItemCallCount);
            Assert.AreEqual(0, _bonuses.ElementalCallCount);
        }

        // Review additions

        private sealed class HpProbeOutput : IAutoAttackDamageOutput
        {
            private readonly IronGrind.CharacterStats.CharacterStats _stats;
            private readonly EntityID _watched;
            public float HpAtEmit = -1f;
            public int Calls;

            public HpProbeOutput(IronGrind.CharacterStats.CharacterStats stats, EntityID watched)
            {
                _stats = stats;
                _watched = watched;
            }

            public void Emit(EntityID attacker, EntityID target, in DamageResult result)
            {
                Calls++;
                HpAtEmit = _stats.GetCurrentHP(_watched);
            }
        }

        [Test]
        public void test_hit_appliesDamageBeforeEmittingOutput()
        {
            GivenPlayerHp(PLAYER_MAX_HP, 3);
            var probe = new HpProbeOutput(_stats, _defender);
            _resolver = new MobAttackResolver(new HitDetector(HitDetectionConfig.Default),
                new HitResultEmitter(_pool, _step, _ticks), _adapter, _stats, probe);

            Attack(NEAR, FORWARD);

            Assert.AreEqual(1, probe.Calls);
            Assert.AreEqual(PLAYER_MAX_HP - 7f, probe.HpAtEmit);
        }

        [Test]
        public void test_attackPowerAboveMaxBaseDamage_hitSentNoDamageNoEvent()
        {
            const int MAX_BASE_DAMAGE = 50;
            var config = new DamageCalculationConfig(0.05f, 200f, 0.10f, MAX_BASE_DAMAGE);
            var calculator = new DamageCalculator(_stats, _weapons, _items, _bonuses, config, _random);
            _resolver = BuildResolver(new DamageCalculatorAutoAttackAdapter(calculator), _step);
            _stats.SetBaseStat(_mob, StatID.AttackPower, MAX_BASE_DAMAGE + 1);
            LogAssert.Expect(LogType.Error, new Regex("BaseDamage must be at most"));

            Attack(NEAR, FORWARD);
            FlushAll();

            Assert.AreEqual(PLAYER_MAX_HP, _stats.GetCurrentHP(_defender));
            CollectionAssert.AreEqual(new[] { new HitCheckResult(_mob, _defender, HitResult.Hit, TICK) }, HitsFor(CLIENT_DEFENDER));
            Assert.AreEqual(0, DamagesFor(CLIENT_DEFENDER).Count);
            Assert.AreEqual(0, DamagesFor(CLIENT_BYSTANDER).Count);
        }

        [Test]
        public void test_hit_usesEffectiveAttackPowerIncludingModifiers()
        {
            const int BUFF_FLAT = 5;
            _stats.AddBuffModifier(_mob, StatID.AttackPower, new BuffModifierEntry(BUFF_FLAT, 0f, 10, (BuffID)601u));
            Assert.AreEqual(ATTACK_POWER + BUFF_FLAT, _stats.GetEffectiveStat(_mob, StatID.AttackPower), "precondition: modifier applies");

            Attack(NEAR, FORWARD);

            Assert.AreEqual(PLAYER_MAX_HP - (float)(ATTACK_POWER + BUFF_FLAT), _stats.GetCurrentHP(_defender));
        }

        [Test]
        public void test_mobWithoutCritStats_neverCrits()
        {
            _random.Value = 0f; // the lowest possible roll; CritChance is never set, so 0 < 0 is false
            Attack(NEAR, FORWARD);
            FlushAll();

            List<DamageEvent> events = DamagesFor(CLIENT_BYSTANDER);
            Assert.AreEqual(1, events.Count);
            Assert.IsFalse(events[0].IsCrit);
            Assert.AreEqual(ATTACK_POWER, events[0].FinalDamage);
        }

        [Test]
        public void test_defenderWithoutClient_noThrowDamageStillApplied()
        {
            const uint CLIENT_GHOST = 13u;
            EntityID ghost = AddPlayer(CLIENT_GHOST); // pool slot, but the client is never added to the outbox
            _stats.SetBaseStat(ghost, StatID.MaxHP, PLAYER_MAX_HP);
            _stats.SetCurrentHP(ghost, PLAYER_MAX_HP);

            Assert.DoesNotThrow(() => _resolver.ResolveAttack(_mob, ghost, Vector3.zero, NEAR, RANGE, FORWARD));
            FlushAll();

            Assert.AreEqual(PLAYER_MAX_HP - (float)ATTACK_POWER, _stats.GetCurrentHP(ghost));
            Assert.AreEqual(1, DamagesFor(CLIENT_BYSTANDER).Count);
        }

        [Test]
        public void test_defenderNotInPool_noThrowDamageStillApplied()
        {
            var absent = new EntityID(900000u);
            _stats.SetBaseStat(absent, StatID.MaxHP, PLAYER_MAX_HP);
            _stats.SetCurrentHP(absent, PLAYER_MAX_HP);

            Assert.DoesNotThrow(() => _resolver.ResolveAttack(_mob, absent, Vector3.zero, NEAR, RANGE, FORWARD));
            FlushAll();

            Assert.AreEqual(PLAYER_MAX_HP - (float)ATTACK_POWER, _stats.GetCurrentHP(absent));
            Assert.AreEqual(1, DamagesFor(CLIENT_BYSTANDER).Count);
        }

        [TestCase(50, 49)] // 50 x 0.05 = 2.5 beats 50 - 49 = 1: floors to 2
        [TestCase(10, 10)] // 10 x 0.05 = 0.5 floors to 0: clamped up to 1
        public void test_minDamageFraction_flooredAndClampedToOne(int attackPower, int defense)
        {
            GivenPlayerHp(PLAYER_MAX_HP, defense);
            _stats.SetBaseStat(_mob, StatID.AttackPower, attackPower);
            float fraction = DamageCalculationConfig.Default.MinDamageFraction;
            int expected = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(attackPower * fraction, attackPower - defense)));

            Attack(NEAR, FORWARD);

            Assert.AreEqual(PLAYER_MAX_HP - (float)expected, _stats.GetCurrentHP(_defender));
            Assert.AreEqual(attackPower == 50 ? 2 : 1, expected, "the case exercises the intended branch");
        }

        [Test]
        public void test_hitPath_realBatchCollector_allocatesNothing()
        {
            _stats.SetBaseStat(_defender, StatID.MaxHP, ALLOC_MAX_HP);
            _stats.SetCurrentHP(_defender, ALLOC_MAX_HP);
            Attack(NEAR, FORWARD); // warm-up through the real batch step
            FlushAll();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < ALLOC_CALLS; i++)
            {
                Attack(NEAR, FORWARD);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
        }

        // QA 13
        [Test]
        public void test_resolver_constructor_null_argument_throws_argument_null_exception()
        {
            var detector = new HitDetector(HitDetectionConfig.Default);
            var hitEmitter = new HitResultEmitter(_pool, _step, _ticks);
            var output = new RecordingOutput();

            Assert.Throws<ArgumentNullException>(() => new MobAttackResolver(null, hitEmitter, _adapter, _stats, output));
            Assert.Throws<ArgumentNullException>(() => new MobAttackResolver(detector, null, _adapter, _stats, output));
            Assert.Throws<ArgumentNullException>(() => new MobAttackResolver(detector, hitEmitter, null, _stats, output));
            Assert.Throws<ArgumentNullException>(() => new MobAttackResolver(detector, hitEmitter, _adapter, null, output));
            Assert.Throws<ArgumentNullException>(() => new MobAttackResolver(detector, hitEmitter, _adapter, _stats, null));
        }

        // QA 14
        [Test]
        public void test_mob_hit_path_allocates_zero_bytes_after_warm_up()
        {
            _stats.SetBaseStat(_defender, StatID.MaxHP, ALLOC_MAX_HP);
            _stats.SetCurrentHP(_defender, ALLOC_MAX_HP);
            _resolver = BuildResolver(_adapter, new NullCollector());
            Attack(NEAR, FORWARD);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < ALLOC_CALLS; i++)
            {
                Attack(NEAR, FORWARD);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual(ALLOC_MAX_HP - ATTACK_POWER * (ALLOC_CALLS + 1), _stats.GetCurrentHP(_defender));
        }
    }
}
