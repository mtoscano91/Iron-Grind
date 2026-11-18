using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.HitDetection;
using IronGrind.LevelingSystem;
using IronGrind.LootTableSystem;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.AutoAttackCombat;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// EditMode tests for Death and Respawn Story 006: dead player permissions (AC-1 to AC-5; TR-dr-006, CR-DR-15 to
    /// CR-DR-17, AC-DR-4, AC-DR-17, AC-DR-NEW-5). Real Character Stats, ZoneSlotPool, DeathAndRespawnService,
    /// AutoAttackCombatSystem, MovementService, MobController, MobAttackResolver and the request handlers; only seams
    /// without a death-relevant behaviour are faked. AC-DR-NEW-6 (voluntary zone change) is out of scope: no request exists.
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_DeadPermissions_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const uint START_TICK = 1000u;
        private const float DELTA = 0.05f;
        private const uint CLIENT = 11u;
        private const float PLAYER_MAX_HP = 100f;
        private const float MOB_MAX_HP = 100f;
        private const float OVERKILL = 9999f;
        private const float AGGRO_RANGE = 8f;
        private const short FULL = 32767;
        private const uint SKILL_HOSTILE = 101u;
        private const int SKILL_SLOT_COUNT = 50;
        private const int FLOOD_TICKS_MIN = 10;
        private const ushort DR_RANGE_FIRST = 0xE160;
        private const ushort DR_RANGE_LAST = 0xE16F;
        private const float PLAYER_START_X = 3f;
        private const int MOVE_TICKS = 10;
        private const int LOOP_CAP_MARGIN = 5;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static DeathAndRespawnConfigValues Config => new DeathAndRespawnConfigValues(
            DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_DEFAULT,
            DeathAndRespawnConfigValues.RESPAWN_ANCHOR_RETRY_TICKS_DEFAULT);

        // ---- fakes ----

        private sealed class FakeServerTicks : IServerTickSource
        {
            public uint Value;
            public uint ServerTickNumber => Value;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }
            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeDeathTicks : IDeathTickSource
        {
            public long Value;
            public long CurrentTick => Value;
        }

        private sealed class NotGhost : IGhostStatusSource
        {
            public bool IsGhost(EntityID id) { return false; }
        }

        private sealed class NoGhostSink : IGhostDeathSink
        {
            public void OnGhostDied(EntityID entity) { }
        }

        private sealed class NoParty : IPartyMemberStatusSink
        {
            public void SetMemberStatus(EntityID entity, PartyMemberDeathStatus status) { }
        }

        private sealed class SlotIndex : IEntitySlotIndex
        {
            private readonly Dictionary<uint, int> _map = new Dictionary<uint, int>();
            public void Map(EntityID id, int slot) { _map[id.RawValue] = slot; }
            public bool TryGetSlotIndex(EntityID id, out int index) { return _map.TryGetValue(id.RawValue, out index); }
        }

        private sealed class StickyIntents : IMovementIntentSource
        {
            public bool Has;
            public MovementIntentMessage Message;

            public bool TryTakeIntent(int playerSlot, out MovementIntentMessage intent)
            {
                intent = Message;
                return Has && playerSlot == 0;
            }
        }

        private sealed class FixedSpeed : IMovementSpeedSource
        {
            public bool TryGetMovementSpeed(EntityID id, out float speed) { speed = 5f; return true; }
        }

        private sealed class PassSampler : INavMeshSampler
        {
            public bool TrySample(Vector3 candidate, float radius, out Vector3 hit) { hit = candidate; return true; }
        }

        private sealed class UnitAttackSpeed : IAttackSpeedSource
        {
            public float GetAttackSpeedMultiplier(EntityID entity) { return 1.0f; }
        }

        private sealed class MobOnlyValidity : IAutoAttackTargetValidity
        {
            private readonly EntityID _mob;
            public MobOnlyValidity(EntityID mob) { _mob = mob; }
            public bool IsValidTarget(EntityID target) { return target == _mob; }
        }

        private sealed class NullNav : INavigationProvider
        {
            public void SetDestination(EntityID agent, Vector3 target) { }
            public void SetSpeed(EntityID agent, float speed) { }
            public void Stop(EntityID agent) { }
            public void Resume(EntityID agent) { }
            public bool IsPathStale(EntityID agent) { return false; }
            public Vector3 GetCurrentVelocity(EntityID agent) { return Vector3.zero; }
        }

        private sealed class CountingMobResolver : IMobAttackResolver
        {
            public int Calls;

            public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange,
                Vector3 capturedForward)
            {
                Calls++;
            }
        }

        private sealed class CountingDamageOutput : IAutoAttackDamageOutput
        {
            public int Calls;
            public void Emit(EntityID attacker, EntityID target, in DamageResult result) { Calls++; }
        }

        private sealed class NullUnlockOutput : ISkillUnlockOutput
        {
            public void SendUnlock(EntityID owner, SkillID skillId) { }
        }

        private sealed class AllLevels : ISkillLevelSource
        {
            public int GetLevel(EntityID entity) { return 1; }
        }

        private sealed class NoSilence : ISkillSilenceSource
        {
            public bool IsSilenced(EntityID entity) { return false; }
        }

        private sealed class MobTargets : ISkillTargetSource
        {
            private readonly EntityID _mob;
            public MobTargets(EntityID mob) { _mob = mob; }

            public bool TryGetTarget(EntityID target, out SkillTargetKind kind, out bool isAlive)
            {
                kind = SkillTargetKind.Mob;
                isAlive = true;
                return target == _mob;
            }
        }

        private sealed class FixedPositions : ISkillPositionSource
        {
            public bool TryGetPositionXZ(EntityID entity, out float x, out float z)
            {
                x = 0f;
                z = 0f;
                return true;
            }
        }

        private sealed class CountingCastOutput : ISkillCastOutput
        {
            public int Rejections;
            public SkillCastRejectionCode LastCode;
            public int Accepted;

            public void SendRejection(EntityID caster, in SkillCastRequest request, SkillCastRejectionCode code)
            {
                Rejections++;
                LastCode = code;
            }

            public void SendAccepted(EntityID caster, in SkillCastRequest request, int newCooldownTicks,
                in SkillExecutionOutcome outcome)
            {
                Accepted++;
            }
        }

        private sealed class CountingCastSink : ISkillCastValidatedSink
        {
            public int Count;
            public void Accept(in ValidatedSkillCast cast) { Count++; }
        }

        // ---- rig ----

        private IronGrind.CharacterStats.CharacterStats _stats;
        private ZoneSlotPool _pool;
        private FakeDeathTicks _deathTicks;
        private FakeServerTicks _serverTicks;
        private StickyIntents _intents;
        private MovementService _movement;
        private AutoAttackCombatSystem _combat;
        private FakeDamageApplier _applier;
        private FakeHitEmitter _hitEmitter;
        private DeathAndRespawnService _service;
        private TargetSlotTracker _tracker;
        private List<EntityID> _validIds;
        private SetTargetRequestHandler _setTarget;
        private SetAutoAttackRequestHandler _setAuto;
        private EntityID _player;
        private EntityID _mob;
        private MobController _mobController;
        private CountingMobResolver _mobResolver;
        private uint _tick;

        private SkillDatabase _database;
        private SkillCastIntake _intake;
        private SkillCastStep _castStep;
        private SkillCastRequestHandler _castHandler;
        private CountingCastOutput _castOutput;
        private CountingCastSink _castSink;

        private static int ScanInterval => EnemyAIConfigValues.AGGRO_SCAN_INTERVAL_TICKS_DEFAULT;

        [SetUp]
        public void SetUp()
        {
            _tick = START_TICK;
            _serverTicks = new FakeServerTicks { Value = _tick };
            _deathTicks = new FakeDeathTicks { Value = _tick };
            _pool = new ZoneSlotPool(Zone, _serverTicks, new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(CLIENT), new AccountID(1000u + CLIENT), CLIENT,
                out _, out _player));
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out _mob));

            _stats = CharacterStatsFixture.Create();
            _stats.SetBaseStat(_player, StatID.MaxHP, (int)PLAYER_MAX_HP);
            _stats.SetCurrentHP(_player, PLAYER_MAX_HP);
            _stats.SetBaseStat(_mob, StatID.MaxHP, (int)MOB_MAX_HP);
            _stats.SetCurrentHP(_mob, MOB_MAX_HP);

            var index = new SlotIndex();
            index.Map(_player, 0);
            _intents = new StickyIntents();
            _movement = new MovementService(_intents, new FixedSpeed(), new PassSampler(), index);
            _movement.SeedForTesting(0, _player, new Vector3(PLAYER_START_X, 0f, 0f), 0);

            _applier = new FakeDamageApplier();
            _hitEmitter = new FakeHitEmitter();
            _combat = new AutoAttackCombatSystem(_pool, new MobOnlyValidity(_mob), new UnitAttackSpeed(), new FakeZoneState(),
                new HitDetector(HitDetectionConfig.Default), _hitEmitter, new FakeStatSource(), new FakeDamageCalc(), _applier,
                new FakeXpAward(), new FakeExperienceSink(), new FakeMobTypeSource(), default(ZoneID), new FakeFaceOutput());

            _service = new DeathAndRespawnService(Config, _pool, new CharacterStatsDeathSource(_stats), _deathTicks, _combat,
                _movement, new NoParty(), new NotGhost(), new NoGhostSink(), new RespawnStubs.Points(),
                new CharacterStatsRespawnVitalsWriter(_stats), _movement, new RespawnStubs.Status(), new RespawnStubs.Output());

            _tracker = new TargetSlotTracker();
            _validIds = new List<EntityID> { _mob };
            _setTarget = new SetTargetRequestHandler(_tracker, _validIds, _combat);
            _setAuto = new SetAutoAttackRequestHandler(_combat);

            _mobResolver = new CountingMobResolver();
            var candidates = new ZoneAggroCandidateSource(Zone, _pool, new MovementPlayerPositionReader(_movement),
                _service);
            _mobController = new MobController(_mob, MobDef(), Vector3.zero, new NullNav(),
                new CharacterStatsZoneEntityStatReader(_stats), _mobResolver, EnemyAIConfigValues.Defaults,
                NavigationConfigValues.RETURN_SPEED_CAP_DEFAULT, candidates);

            BuildSkillRig();
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            if (_database != null)
            {
                UnityEngine.Object.DestroyImmediate(_database);
                _database = null;
            }
        }

        private static MobDefinition MobDef()
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(1u), displayName: "Test Mob", baseMaxHP: 100, baseAttackPower: 10,
                baseDefense: 5, attackRange: 2f, moveSpeed: 3.5f, aggroRange: AGGRO_RANGE, leashRange: 20f,
                attackWindUpTicks: 12, attackRecoveryTicks: 6, attackCooldownTicks: 10, killXP: 10,
                mobTier: MobTier.Common, enragedChance: 0.07f, enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f, enragedXPMultiplier: 1.5f, enragedLootQualityBonusTier: 1);
        }

        private void BuildSkillRig()
        {
            var slots = new SlotIndex();
            slots.Map(_player, 0);
            var registry = new ClassRegistry();
            registry.RegisterClass(0, new ClassDefinition(0, 0, 0, 0, 0, new[] { SKILL_HOSTILE }));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(new[]
            {
                new SkillDefinition
                {
                    ID = SKILL_HOSTILE, UnlockLevel = 1, RangeUnits = 10f,
                    TargetConstraint = TargetConstraint.SingleHostile, CooldownTicks = 20,
                },
            });
            var service = new SkillSystemService(registry, _database, new AllLevels(), slots, new NullUnlockOutput(),
                SKILL_SLOT_COUNT);
            service.OnEntitySpawned(_player, ClassType.Warrior);
            _intake = new SkillCastIntake(slots, SKILL_SLOT_COUNT);
            _castOutput = new CountingCastOutput();
            _castSink = new CountingCastSink();
            _castStep = new SkillCastStep(_intake, service, _database, new CharacterStatsSkillCasterAliveSource(_stats),
                new NoSilence(), new MobTargets(_mob), new FixedPositions(), _castOutput, _castSink);
            _castHandler = new SkillCastRequestHandler(_intake, _serverTicks, 5);
        }

        // ---- helpers ----

        // One zone tick: shared tick counter, then movement, combat, mob, skills, death service.
        private void Step(int count, Action perTick = null)
        {
            for (int i = 0; i < count; i++)
            {
                _tick++;
                _serverTicks.Value = _tick;
                _deathTicks.Value = _tick;
                perTick?.Invoke();
                _movement.Run(_tick, DELTA);
                _combat.Run(_tick, DELTA);
                _mobController.Tick(_tick);
                _castStep.Run(_tick, DELTA);
                _service.Tick(_tick);
            }
        }

        // Steps until the target tick, failing instead of hanging if it is never reached.
        private void StepUntil(long target)
        {
            long cap = (long)_tick + Config.RespawnDelayTicks + LOOP_CAP_MARGIN;
            while (_tick < target)
            {
                Assert.Less((long)_tick, cap, "the target tick is reachable");
                Step(1);
            }
        }

        // More than two full attack cycles, and at least FLOOD_TICKS_MIN ticks.
        private int AutoAttackWindow()
        {
            int cycle = _combat.GetCycleTicks(_player);
            Assert.Greater(cycle, 0);
            return Math.Max(FLOOD_TICKS_MIN, (cycle * 2) + 1);
        }

        private void Kill()
        {
            _stats.ApplyDamage(_player, OVERKILL);
            Assert.AreEqual(0f, _stats.GetCurrentHP(_player));
            Assert.IsTrue(_service.IsDead(_player));
        }

        private DeathState StateOf(EntityID id)
        {
            Assert.IsTrue(_service.TryGetDeathState(id, out DeathState state));
            return state;
        }

        private ZoneSessionState SlotState()
        {
            Assert.IsTrue(_pool.TryGetPlayer(_player, out ZonePlayerSlot slot));
            return slot.State;
        }

        private InboundRequestContext Context(ushort typeId)
        {
            return new InboundRequestContext(CLIENT, _player, default, typeId, _tick, _tick, false);
        }

        private void SendSetTarget(EntityID target)
        {
            InboundRequestContext c = Context(SetTarget.MessageTypeId);
            _setTarget.Handle(in c, BitConverter.GetBytes(target.RawValue));
        }

        private void SendSetAutoAttack(bool enabled)
        {
            InboundRequestContext c = Context(SetAutoAttack.MessageTypeId);
            _setAuto.Handle(in c, new[] { (byte)(enabled ? 1 : 0) });
        }

        private void SendSkillCast()
        {
            var request = new SkillCastRequest(_player.RawValue, SKILL_HOSTILE, _mob.RawValue, _tick);
            var body = new byte[SkillCastRequest.BodySize];
            SkillCastRequestCodec.WriteBody(body, in request);
            InboundRequestContext c = Context(SkillCastRequest.MessageTypeId);
            _castHandler.Handle(in c, body);
        }

        private void ActivateAutoAttack()
        {
            SendSetTarget(_mob);
            SendSetAutoAttack(true);
        }

        // ---- AC-1 mob targeting ----

        [Test]
        public void test_dead_permissions_mob_with_only_dead_player_in_range_does_not_select_player()
        {
            Kill();
            Assert.AreEqual(ZoneSessionState.Connected, SlotState()); // Story 007: slot state is connection only
            Assert.IsTrue(_service.IsDead(_player));

            Step(ScanInterval * 3);

            Assert.IsNull(_mobController.TargetEntityID);
            Assert.AreEqual(MobState.Dormant, _mobController.State);
        }

        [Test]
        public void test_dead_permissions_control_mob_selects_alive_player_in_range()
        {
            Step(ScanInterval * 3);

            Assert.AreEqual(_player, _mobController.TargetEntityID);
        }

        [Test]
        public void test_dead_permissions_mob_chasing_player_drops_player_when_player_dies()
        {
            Step(ScanInterval * 3);
            Assert.AreEqual(_player, _mobController.TargetEntityID);
            Assert.AreEqual(MobState.Pursuing, _mobController.State);
            Step(ScanInterval);
            Assert.AreEqual(MobState.Pursuing, _mobController.State, "control: the chase holds while P is alive");

            Kill();
            Step(ScanInterval * 3);

            Assert.IsNull(_mobController.TargetEntityID);
            Assert.That(_mobController.State, Is.EqualTo(MobState.Dormant).Or.EqualTo(MobState.Returning));
        }

        [Test]
        public void test_dead_permissions_mob_may_select_player_again_after_respawn_in_range()
        {
            Kill();
            Step(ScanInterval * 3);
            Assert.IsFalse(_mobController.TargetEntityID.HasValue);
            DeathState dead = StateOf(_player);

            StepUntil(dead.RespawnTick);

            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            Assert.AreEqual(ZoneSessionState.Connected, SlotState());
            Assert.Greater(_stats.GetCurrentHP(_player), 0f);

            Step(ScanInterval * 3);

            Assert.AreEqual(_player, _mobController.TargetEntityID);
        }

        // ---- AC-2 player targeting / damage ----

        [Test]
        public void test_dead_permissions_control_alive_player_auto_attack_applies_damage()
        {
            ActivateAutoAttack();
            Step(1);

            Step(AutoAttackWindow());

            Assert.Greater(_applier.Count, 0, "control: the same rig deals damage for an alive player");
        }

        [Test]
        public void test_dead_permissions_dead_player_target_and_auto_attack_deal_no_damage_over_a_full_cycle_window()
        {
            Kill();
            ActivateAutoAttack();
            Step(1);
            Assert.AreEqual(AutoAttackState.Prohibited, _combat.GetState(_player), "the prohibition is why nothing lands");
            int ticks = AutoAttackWindow();
            Assert.Less(ticks, (int)Config.RespawnDelayTicks - 2, "the window ends before the respawn");

            Step(ticks);

            // FakeDamageApplier never writes Character Stats, so mob HP cannot change here: the counters are the proof.
            Assert.AreEqual(0, _applier.Count, "no damage event");
            Assert.AreEqual(0, _hitEmitter.Count);
            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
        }

        [Test]
        public void test_dead_permissions_mob_attack_resolved_against_dead_player_applies_no_damage_and_no_event()
        {
            Kill();
            var calc = new FakeDamageCalc();
            var emitter = new FakeHitEmitter();
            var output = new CountingDamageOutput();
            var resolver = new MobAttackResolver(new HitDetector(HitDetectionConfig.Default), emitter, calc, _stats, output);

            resolver.ResolveAttack(_mob, _player, Vector3.zero, new Vector3(1f, 0f, 0f), 2f, new Vector3(1f, 0f, 0f));

            Assert.AreEqual(1, emitter.Count);
            Assert.AreEqual(HitResult.Hit, emitter.Results[0], "the swing reached the dead-target guard");
            Assert.AreEqual(0, calc.Calls);
            Assert.AreEqual(0, output.Calls);
        }

        [Test]
        public void test_dead_permissions_control_mob_attack_resolved_against_alive_player_applies_damage()
        {
            var calc = new FakeDamageCalc();
            var output = new CountingDamageOutput();
            var resolver = new MobAttackResolver(new HitDetector(HitDetectionConfig.Default), new FakeHitEmitter(), calc,
                _stats, output);

            resolver.ResolveAttack(_mob, _player, Vector3.zero, new Vector3(1f, 0f, 0f), 2f, new Vector3(1f, 0f, 0f));

            Assert.AreEqual(1, calc.Calls);
            Assert.AreEqual(1, output.Calls);
            Assert.Less(_stats.GetCurrentHP(_player), PLAYER_MAX_HP);
        }

        // ---- AC-3 skills ----

        [Test]
        public void test_dead_permissions_control_alive_player_skill_cast_is_accepted()
        {
            SendSkillCast();
            Step(1);

            Assert.AreEqual(0, _castOutput.Rejections);
            Assert.AreEqual(1, _castSink.Count);
        }

        [Test]
        public void test_dead_permissions_dead_player_skill_cast_is_rejected_with_no_effect()
        {
            Kill();
            ActivateAutoAttack();

            SendSkillCast();
            Step(1);

            Assert.AreEqual(1, _castOutput.Rejections);
            Assert.AreEqual(SkillCastRejectionCode.CasterNotAlive, _castOutput.LastCode);
            // The validated sink is where execution (damage, status effects) starts: zero calls is the AC-3 proof.
            Assert.AreEqual(0, _castSink.Count, "nothing reaches execution, so no status effect or damage");
            Assert.AreEqual(AutoAttackState.Prohibited, _combat.GetState(_player));
        }

        // ---- AC-4 movement ----

        [Test]
        public void test_dead_permissions_dead_player_movement_intents_leave_position_at_death_position()
        {
            _intents.Has = true;
            _intents.Message = new MovementIntentMessage(FULL, 0, 0, 0u);
            Step(3);
            Assert.IsTrue(_movement.TryGetPosition(_player, out Vector3 alivePosition));
            Assert.AreNotEqual(PLAYER_START_X, alivePosition.x, "control: an alive player moves");

            Kill();
            Assert.IsTrue(_movement.TryGetPosition(_player, out Vector3 deathPosition));
            Step(MOVE_TICKS);

            Assert.IsTrue(_movement.TryGetPosition(_player, out Vector3 after));
            Assert.AreEqual(deathPosition, after);
        }

        // ---- AC-5 no early respawn ----

        [Test]
        public void test_dead_permissions_service_public_instance_methods_are_exactly_the_reviewed_set()
        {
            string[] names = typeof(DeathAndRespawnService)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            // Story 007 added BeforeRelease (pool release only; the player leaves the zone) and TryGetRespawnTick
            // (read-only).
            CollectionAssert.AreEqual(
                new[] { "BeforeRelease", "Dispose", "IsDead", "Tick", "TryGetDeathState", "TryGetRespawnTick" },
                names,
                "a new public method must be reviewed against CR-DR-17 (no way to respawn before RespawnTick)");
        }

        [Test]
        public void test_dead_permissions_routing_registry_has_no_client_to_server_type_in_the_death_respawn_range()
        {
            var offenders = new List<string>();
            var inRange = new List<string>();
            foreach (MessageRoutingEntry entry in MessageRoutingRegistry.AllEntries)
            {
                if (entry.MessageTypeId < DR_RANGE_FIRST || entry.MessageTypeId > DR_RANGE_LAST)
                {
                    continue;
                }

                inRange.Add(entry.MessageName);
                if (entry.Direction == MessageDirection.ClientToServer)
                {
                    offenders.Add(entry.MessageName);
                }
            }

            // Control: the scan sees the Story 005 server-to-client messages in this range.
            CollectionAssert.Contains(inRange, nameof(EntityDied));
            CollectionAssert.Contains(inRange, nameof(RespawnConfirmed));
            CollectionAssert.IsEmpty(offenders);
        }

        [Test]
        public void test_dead_permissions_request_flood_does_not_change_death_state_and_respawn_fires_on_respawn_tick()
        {
            Kill();
            DeathState dead = StateOf(_player);
            long respawnTick = dead.RespawnTick;
            int flooded = 0;
            Action flood = () =>
            {
                SendSetTarget(_mob);
                SendSetAutoAttack(true);
                SendSkillCast();
                flooded++;
            };
            long cap = (long)_tick + Config.RespawnDelayTicks + LOOP_CAP_MARGIN;

            while (_tick < respawnTick - 1)
            {
                Assert.Less((long)_tick, cap, "the respawn tick is reachable");
                Step(1, flood);
                DeathState now = StateOf(_player);
                Assert.AreEqual(DeathStateKind.Dead, now.CurrentState, "tick " + _tick);
                Assert.AreEqual(respawnTick, now.RespawnTick);
                Assert.AreEqual(dead.DeathTick, now.DeathTick);
            }

            Assert.AreEqual(respawnTick - 1, (long)_tick);
            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
            Assert.AreEqual(AutoAttackState.Prohibited, _combat.GetState(_player));

            Step(1, flood);

            Assert.AreEqual(respawnTick, (long)_tick);
            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            // Every flooded cast reached the cast step and was rejected there (the cast step runs before T-3).
            Assert.AreEqual(flooded, _castOutput.Rejections, "one rejection per flooded tick");
            Assert.AreEqual(SkillCastRejectionCode.CasterNotAlive, _castOutput.LastCode);
            Assert.AreEqual(0, _castSink.Count, "no flooded cast was accepted while dead");
        }
    }
}
