using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LevelingSystem;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode tests for Skill System Story 009 - cast output: <see cref="SkillCastOutputEmitter"/> recipients, the
    /// cooldown update rules and <see cref="ZoneSkillCastPartySource"/> (CR-SK-9, CR-SK-12, CR-SK-23, AC-SK-42, AC-SK-43;
    /// TR-sk-009, TR-sk-010). A recording <see cref="IRUBatchCollector"/> captures every enqueued sub-message.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_CastOutput_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 8;
        private const int MAX_MOBS = 4;
        private const uint CLIENT_CASTER = 11u;
        private const uint CLIENT_A = 12u;
        private const uint CLIENT_B = 13u;
        private const uint CLIENT_C = 14u;
        private const uint CLIENT_D = 15u;
        private const uint CLIENT_E = 16u;
        private const uint SKILL_MAIN = 101u;
        private const uint SKILL_ZERO = 102u;
        private const uint UNKNOWN_SKILL = 4242u;
        private const uint TICK = 100u;
        private const uint EXPIRY = 130u;
        private const int COOLDOWN_TICKS = 20;
        private const int DAMAGE = 321;
        private const int HEAL = 77;
        private const int TOLERANCE = 5;
        private const uint NON_PLAYER_RAW = 9999u;
        private const uint SPOOFED_CASTER_RAW = 5555u;
        private const uint PARTY_RAW = 5u;
        private const uint OTHER_PARTY_RAW = 6u;
        private const int PAIRS_COUNT = 5;
        private const int ACCEPTED_AND_REJECTED = 10;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Tick = 1u;
            public uint ServerTickNumber => Tick;
        }

        private sealed class NullPoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class Rec
        {
            public uint ClientId;
            public RUBatchCategory Category;
            public ushort TypeId;
            public byte[] Body;
        }

        private sealed class RecCollector : IRUBatchCollector
        {
            public readonly List<Rec> Items = new List<Rec>(64);
            public bool CountOnly;
            public int Count;

            public void Enqueue(uint clientId, RUBatchCategory category, ushort messageTypeId, ReadOnlySpan<byte> body)
            {
                Count++;
                if (CountOnly)
                {
                    return;
                }

                Items.Add(new Rec { ClientId = clientId, Category = category, TypeId = messageTypeId, Body = body.ToArray() });
            }

            public void EnqueueDamageEvent(uint clientId, in DamageEvent damageEvent) { }
        }

        private sealed class FakeParty : ISkillCastPartySource
        {
            private readonly HashSet<ulong> _pairs = new HashSet<ulong>();

            public void Add(EntityID a, EntityID b)
            {
                _pairs.Add(Key(a, b));
                _pairs.Add(Key(b, a));
            }

            public bool AreInSameParty(EntityID a, EntityID b) => _pairs.Contains(Key(a, b));

            private static ulong Key(EntityID a, EntityID b) => ((ulong)a.RawValue << 32) | b.RawValue;
        }

        private sealed class ThrowingParty : ISkillCastPartySource
        {
            public bool AreInSameParty(EntityID a, EntityID b) => throw new InvalidOperationException("party source fault");
        }

        private sealed class FakePresence : IPartyZonePresence
        {
            public readonly Dictionary<uint, PartyID> Parties = new Dictionary<uint, PartyID>();

            public bool TryGetFormalParty(CharacterID character, out PartyID party)
            {
                if (Parties.TryGetValue(character.RawValue, out party))
                {
                    return true;
                }

                party = PartyID.Uninitialized;
                return false;
            }

            public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone) { }

            public void NotifyZoneExit(PartyID party, EntityID entityId) { }
        }

        private sealed class FakeLevels : ISkillLevelSource
        {
            public int GetLevel(EntityID entity) => 1;
        }

        private sealed class NullUnlockOutput : ISkillUnlockOutput
        {
            public void SendUnlock(EntityID owner, SkillID skillId) { }
        }

        private sealed class FakeConfig : ISkillSystemConfigSource
        {
            public SkillSystemConfigValues Values { get; set; } =
                SkillSystemConfigValues.Create(1.0f, SkillSystemConfigValues.CAST_STALE_TICK_TOLERANCE_DEFAULT);
        }

        private sealed class FakeMana : ISkillManaSink
        {
            public bool ConsumeMana(EntityID entity, int cost) => true;
        }

        private sealed class AllAlive : ISkillCasterAliveSource
        {
            public bool IsAlive(EntityID entity) => true;
        }

        private sealed class NoSilence : ISkillSilenceSource
        {
            public bool IsSilenced(EntityID entity) => false;
        }

        private sealed class NoTargets : ISkillTargetSource
        {
            public bool TryGetTarget(EntityID target, out SkillTargetKind kind, out bool isAlive)
            {
                kind = default;
                isAlive = false;
                return false;
            }
        }

        private sealed class NoPositions : ISkillPositionSource
        {
            public bool TryGetPositionXZ(EntityID entity, out float x, out float z)
            {
                x = 0f;
                z = 0f;
                return false;
            }
        }

        private sealed class NullSink : ISkillCastValidatedSink
        {
            public int Count;

            public void Accept(in ValidatedSkillCast cast) { Count++; }
        }

        private FakeTicks _ticks;
        private ZoneSlotPool _pool;
        private TargetSlotTracker _tracker;
        private FakeParty _party;
        private RecCollector _collector;
        private SkillDatabase _database;
        private SkillSystemService _service;
        private SkillCastOutputEmitter _emitter;
        private readonly List<EntityID> _valid = new List<EntityID>();
        private EntityID _caster;
        private EntityID _a;
        private EntityID _b;
        private EntityID _c;
        private EntityID _d;
        private EntityID _e;
        private EntityID _target;
        private SkillDefinition _zeroDef;
        private SkillCastRequest _request;
        private SkillExecutionOutcome _outcome;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks { Tick = TICK };
            _pool = new ZoneSlotPool(Zone, _ticks, new NullPoolListener(), MAX_PLAYERS, MAX_MOBS);
            _tracker = new TargetSlotTracker();
            _party = new FakeParty();
            _collector = new RecCollector();
            _valid.Clear();

            _caster = AddPlayer(CLIENT_CASTER);
            _a = AddPlayer(CLIENT_A);
            _b = AddPlayer(CLIENT_B);
            _c = AddPlayer(CLIENT_C);
            _d = AddPlayer(CLIENT_D);
            _e = AddPlayer(CLIENT_E);
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out ushort _, out _target));
            _valid.Add(_target);

            var mainDef = Def(SKILL_MAIN, COOLDOWN_TICKS);
            _zeroDef = Def(SKILL_ZERO, 0);
            var registry = new ClassRegistry();
            registry.RegisterClass(0, new ClassDefinition(0, 0, 0, 0, 0, new[] { SKILL_MAIN, SKILL_ZERO }));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(new[] { mainDef, _zeroDef });
            _service = new SkillSystemService(registry, _database, new FakeLevels(), new ZoneSlotPoolEntityIndex(_pool),
                new NullUnlockOutput(), MAX_PLAYERS);
            _service.OnEntitySpawned(_caster, ClassType.Warrior);

            _emitter = new SkillCastOutputEmitter(_pool, Zone, _tracker, _party, _collector, _service);
            _request = new SkillCastRequest(_caster.RawValue, SKILL_MAIN, _target.RawValue, 0u);
            _outcome = default;
        }

        [TearDown]
        public void TearDown()
        {
            // Every known warning/error must be expected explicitly by the test that triggers it.
            LogAssert.NoUnexpectedReceived();
            if (_database != null)
            {
                UnityEngine.Object.DestroyImmediate(_database);
                _database = null;
            }
        }

        private EntityID AddPlayer(uint client)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u), client, out byte _, out EntityID id));
            _valid.Add(id);
            return id;
        }

        private static SkillDefinition Def(uint id, int cooldownTicks)
        {
            return new SkillDefinition
            {
                ID = id,
                UnlockLevel = 1,
                RangeUnits = 10f,
                CooldownTicks = cooldownTicks,
                TargetConstraint = TargetConstraint.SingleHostile,
            };
        }

        private void SetTarget(uint client, EntityID own, EntityID target)
        {
            Assert.That(_tracker.ProcessSetTarget(client, own, target, _valid), Is.EqualTo(SetTargetOutcome.Accepted));
        }

        private List<SkillCastResult> Results(uint client)
        {
            var list = new List<SkillCastResult>();
            foreach (Rec r in _collector.Items)
            {
                if (r.ClientId == client && r.Category == RUBatchCategory.SkillCastResult)
                {
                    Assert.That(r.TypeId, Is.EqualTo(SkillCastResult.MessageTypeId));
                    Assert.That(SkillCastResultCodec.TryReadBody(r.Body, out SkillCastResult m), Is.True);
                    list.Add(m);
                }
            }

            return list;
        }

        private List<SkillCooldownUpdate> Updates(uint client)
        {
            var list = new List<SkillCooldownUpdate>();
            foreach (Rec r in _collector.Items)
            {
                if (r.ClientId == client && r.Category == RUBatchCategory.SkillCooldownUpdate)
                {
                    Assert.That(r.TypeId, Is.EqualTo(SkillCooldownUpdate.MessageTypeId));
                    Assert.That(SkillCooldownUpdateCodec.TryReadBody(r.Body, out SkillCooldownUpdate m), Is.True);
                    list.Add(m);
                }
            }

            return list;
        }

        private int Total(RUBatchCategory category)
        {
            int n = 0;
            foreach (Rec r in _collector.Items)
            {
                if (r.Category == category)
                {
                    n++;
                }
            }

            return n;
        }

        private void Accept(int cooldownTicks = COOLDOWN_TICKS)
        {
            _emitter.SendAccepted(_caster, in _request, cooldownTicks, in _outcome);
        }

        private void Reject(SkillCastRejectionCode code)
        {
            _emitter.SendRejection(_caster, in _request, code);
        }

        // ---- AC-SK-42 ----

        [Test]
        public void test_castoutput_five_accepted_and_five_rejected_caster_gets_ten_results()
        {
            for (int i = 0; i < PAIRS_COUNT; i++)
            {
                Accept();
            }

            Reject(SkillCastRejectionCode.InvalidSkill);
            Reject(SkillCastRejectionCode.SkillLocked);
            Reject(SkillCastRejectionCode.OnCooldown);
            Reject(SkillCastRejectionCode.InvalidTarget);
            Reject(SkillCastRejectionCode.InsufficientMana);

            Assert.That(Results(CLIENT_CASTER).Count, Is.EqualTo(ACCEPTED_AND_REJECTED));
        }

        // ---- CR-SK-9 rejected ----

        [Test]
        public void test_castoutput_rejection_goes_to_caster_only_with_zeroed_payload()
        {
            SetTarget(CLIENT_A, _a, _caster);
            SetTarget(CLIENT_B, _b, _target);

            Reject(SkillCastRejectionCode.InvalidTarget);

            Assert.That(Total(RUBatchCategory.SkillCastResult), Is.EqualTo(1));
            List<SkillCastResult> got = Results(CLIENT_CASTER);
            Assert.That(got.Count, Is.EqualTo(1));
            SkillCastResult r = got[0];
            Assert.That(r.CastAccepted, Is.False);
            Assert.That(r.RejectionCode, Is.EqualTo((byte)SkillCastRejectionCode.InvalidTarget));
            Assert.That(r.SkillId, Is.EqualTo(SKILL_MAIN));
            Assert.That(r.CasterId, Is.EqualTo(_caster.RawValue));
            Assert.That(r.DamageDealt, Is.EqualTo(0));
            Assert.That(r.HealAmount, Is.EqualTo(0));
            Assert.That(r.IsCrit, Is.False);
            Assert.That(r.IsKill, Is.False);
            Assert.That(r.NewCooldownTicks, Is.EqualTo(0));
            Assert.That(Results(CLIENT_A).Count, Is.EqualTo(0));
            Assert.That(Results(CLIENT_B).Count, Is.EqualTo(0));
            Assert.That(Results(CLIENT_C).Count, Is.EqualTo(0));
        }

        // ---- CR-SK-9 accepted ----

        [Test]
        public void test_castoutput_accepted_reaches_caster_party_targeters_and_target_party_once_each()
        {
            _party.Add(_a, _caster);                // A: caster's party
            SetTarget(CLIENT_B, _b, _target);       // B: targets the target
            SetTarget(CLIENT_D, _d, _caster);       // D: targets the caster ...
            _party.Add(_d, _caster);                // ... and is in the caster's party
            _party.Add(_e, _target);                // E: in the target's party only

            Accept();

            Assert.That(Results(CLIENT_CASTER).Count, Is.EqualTo(1));
            Assert.That(Results(CLIENT_A).Count, Is.EqualTo(1));
            Assert.That(Results(CLIENT_B).Count, Is.EqualTo(1));
            Assert.That(Results(CLIENT_D).Count, Is.EqualTo(1));
            Assert.That(Results(CLIENT_E).Count, Is.EqualTo(1));
            Assert.That(Results(CLIENT_C).Count, Is.EqualTo(0));
            Assert.That(Total(RUBatchCategory.SkillCastResult), Is.EqualTo(5));
        }

        [Test]
        public void test_castoutput_accepted_reaches_a_player_target_through_its_self_slot()
        {
            // RFR-1: C has no target and no party, but C is the target of the cast (e.g. a heal on another player).
            _request = new SkillCastRequest(_caster.RawValue, SKILL_MAIN, _c.RawValue, 0u);

            Accept();

            Assert.That(Results(CLIENT_C).Count, Is.EqualTo(1));
            Assert.That(Results(CLIENT_C)[0].TargetId, Is.EqualTo(_c.RawValue));
            Assert.That(Total(RUBatchCategory.SkillCastResult), Is.EqualTo(2), "caster and the target player only");
        }

        [Test]
        public void test_castoutput_accepted_with_no_target_does_not_match_idle_clients()
        {
            _request = new SkillCastRequest(_caster.RawValue, SKILL_MAIN, 0u, 0u);

            Accept();

            Assert.That(Total(RUBatchCategory.SkillCastResult), Is.EqualTo(1));
            Assert.That(Results(CLIENT_CASTER)[0].TargetId, Is.EqualTo(0u));
        }

        [Test]
        public void test_castoutput_accepted_payload_carries_outcome_and_cooldown_ticks()
        {
            _outcome = new SkillExecutionOutcome { DamageDealt = DAMAGE, HealAmount = HEAL, IsCrit = true, IsKill = true };

            Accept();

            SkillCastResult r = Results(CLIENT_CASTER)[0];
            Assert.That(r.CastAccepted, Is.True);
            Assert.That(r.RejectionCode, Is.EqualTo((byte)0));
            Assert.That(r.SkillId, Is.EqualTo(SKILL_MAIN));
            Assert.That(r.CasterId, Is.EqualTo(_caster.RawValue));
            Assert.That(r.TargetId, Is.EqualTo(_target.RawValue));
            Assert.That(r.DamageDealt, Is.EqualTo(DAMAGE));
            Assert.That(r.HealAmount, Is.EqualTo(HEAL));
            Assert.That(r.IsCrit, Is.True);
            Assert.That(r.IsKill, Is.True);
            Assert.That(r.NewCooldownTicks, Is.EqualTo(COOLDOWN_TICKS));
        }

        // ---- CR-SK-23 ----

        [Test]
        public void test_castoutput_accepted_sends_one_cooldown_update_to_caster_after_the_result()
        {
            SetTarget(CLIENT_B, _b, _target);
            _service.SetCooldownForTesting(_caster, 0, EXPIRY);

            Accept();

            List<SkillCooldownUpdate> updates = Updates(CLIENT_CASTER);
            Assert.That(updates.Count, Is.EqualTo(1));
            Assert.That(updates[0].SkillId, Is.EqualTo(SKILL_MAIN));
            Assert.That(updates[0].CooldownExpiryTick, Is.EqualTo(EXPIRY));
            Assert.That(Total(RUBatchCategory.SkillCooldownUpdate), Is.EqualTo(1), "caster only");
            // Enqueued right after the caster's result (before the roster walk); wire order is set by the batch writer.
            Assert.That(_collector.Items[0].Category, Is.EqualTo(RUBatchCategory.SkillCastResult));
            Assert.That(_collector.Items[1].Category, Is.EqualTo(RUBatchCategory.SkillCooldownUpdate));
            Assert.That(_collector.Items[1].ClientId, Is.EqualTo(CLIENT_CASTER));
        }

        [Test]
        public void test_castoutput_try_get_cooldown_expiry_unknown_entity_is_false_and_zero()
        {
            bool found = _service.TryGetCooldownExpiry(new EntityID(NON_PLAYER_RAW), SKILL_MAIN, out uint expiry);

            Assert.That(found, Is.False);
            Assert.That(expiry, Is.EqualTo(0u));
        }

        [Test]
        public void test_castoutput_try_get_cooldown_expiry_unknown_skill_is_false_and_zero()
        {
            _service.SetCooldownForTesting(_caster, 0, EXPIRY);

            bool found = _service.TryGetCooldownExpiry(_caster, UNKNOWN_SKILL, out uint expiry);

            Assert.That(found, Is.False);
            Assert.That(expiry, Is.EqualTo(0u));
        }

        [Test]
        public void test_castoutput_try_get_cooldown_expiry_known_skill_returns_the_written_expiry()
        {
            _service.SetCooldownForTesting(_caster, 0, EXPIRY);
            _service.SetCooldownForTesting(_caster, 1, EXPIRY + 5u);

            Assert.That(_service.TryGetCooldownExpiry(_caster, SKILL_MAIN, out uint main), Is.True);
            Assert.That(main, Is.EqualTo(EXPIRY));
            Assert.That(_service.TryGetCooldownExpiry(_caster, SKILL_ZERO, out uint zero), Is.True);
            Assert.That(zero, Is.EqualTo(EXPIRY + 5u));
        }

        [Test]
        public void test_castoutput_on_cooldown_rejection_with_failed_lookup_sends_result_and_no_update()
        {
            _request = new SkillCastRequest(_caster.RawValue, UNKNOWN_SKILL, _target.RawValue, 0u);

            Reject(SkillCastRejectionCode.OnCooldown);

            Assert.That(Results(CLIENT_CASTER).Count, Is.EqualTo(1));
            Assert.That(Total(RUBatchCategory.SkillCooldownUpdate), Is.EqualTo(0));
        }

        [Test]
        public void test_castoutput_on_cooldown_rejection_sends_update_with_current_expiry()
        {
            _service.SetCooldownForTesting(_caster, 0, EXPIRY);

            Reject(SkillCastRejectionCode.OnCooldown);

            Assert.That(Results(CLIENT_CASTER).Count, Is.EqualTo(1));
            List<SkillCooldownUpdate> updates = Updates(CLIENT_CASTER);
            Assert.That(updates.Count, Is.EqualTo(1));
            Assert.That(updates[0].SkillId, Is.EqualTo(SKILL_MAIN));
            Assert.That(updates[0].CooldownExpiryTick, Is.EqualTo(EXPIRY));
        }

        [TestCase(SkillCastRejectionCode.InvalidSkill)]
        [TestCase(SkillCastRejectionCode.SkillLocked)]
        [TestCase(SkillCastRejectionCode.InvalidTarget)]
        [TestCase(SkillCastRejectionCode.InsufficientMana)]
        [TestCase(SkillCastRejectionCode.Silenced)]
        [TestCase(SkillCastRejectionCode.OutOfRange)]
        [TestCase(SkillCastRejectionCode.CasterNotAlive)]
        [TestCase(SkillCastRejectionCode.ServerError)]
        public void test_castoutput_other_rejection_codes_send_no_cooldown_update(SkillCastRejectionCode code)
        {
            _service.SetCooldownForTesting(_caster, 0, EXPIRY);

            Reject(code);

            Assert.That(Results(CLIENT_CASTER).Count, Is.EqualTo(1));
            Assert.That(Results(CLIENT_CASTER)[0].RejectionCode, Is.EqualTo((byte)code));
            Assert.That(Total(RUBatchCategory.SkillCooldownUpdate), Is.EqualTo(0));
        }

        [Test]
        public void test_castoutput_zero_cooldown_cast_through_executor_sends_update_with_cast_tick()
        {
            var executor = new SkillCastExecutor(new FakeMana(), new FakeConfig(), _service, _emitter);
            var request = new SkillCastRequest(_caster.RawValue, SKILL_ZERO, _target.RawValue, 0u);
            var cast = new ValidatedSkillCast(_caster, _target, in _zeroDef, 1, TICK, in request);

            executor.Accept(in cast);

            List<SkillCooldownUpdate> updates = Updates(CLIENT_CASTER);
            Assert.That(updates.Count, Is.EqualTo(1));
            Assert.That(updates[0].SkillId, Is.EqualTo(SKILL_ZERO));
            Assert.That(updates[0].CooldownExpiryTick, Is.EqualTo(TICK));
            Assert.That(Results(CLIENT_CASTER)[0].NewCooldownTicks, Is.EqualTo(0));
        }

        [Test]
        public void test_castoutput_emitter_has_no_tick_driven_sends()
        {
            // SkillSystemService has no per-tick path (cooldowns are stored as absolute expiry ticks and never swept),
            // so the only per-tick code that can run is SkillCastStep with an empty intake. Nothing may be sent when
            // the expiry passes (CR-SK-23), and the stored expiry must stay untouched.
            _service.SetCooldownForTesting(_caster, 0, TICK + 2u);
            var slots = new ZoneSlotPoolEntityIndex(_pool);
            var intake = new SkillCastIntake(slots, MAX_PLAYERS);
            var step = new SkillCastStep(intake, _service, _database, new AllAlive(), new NoSilence(), new NoTargets(),
                new NoPositions(), _emitter, new NullSink());

            for (uint t = TICK; t < TICK + 10u; t++)
            {
                step.Run(t, 0.05f);
            }

            Assert.That(_collector.Count, Is.EqualTo(0));
            Assert.That(_service.TryGetCooldownExpiry(_caster, SKILL_MAIN, out uint expiry), Is.True);
            Assert.That(expiry, Is.EqualTo(TICK + 2u));
        }

        // ---- AC-SK-43 ----

        [Test]
        public void test_castoutput_spoofed_and_stale_requests_produce_no_result_to_any_client()
        {
            var slots = new ZoneSlotPoolEntityIndex(_pool);
            var intake = new SkillCastIntake(slots, MAX_PLAYERS);
            var handler = new SkillCastRequestHandler(intake, _ticks, TOLERANCE);
            var step = new SkillCastStep(intake, _service, _database, new AllAlive(), new NoSilence(), new NoTargets(),
                new NoPositions(), _emitter, new NullSink());

            _service.OnEntitySpawned(_a, ClassType.Warrior);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[SkillCastRequestHandler\] SECURITY V-1 caster mismatch"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[SkillCastRequestHandler\] SECURITY V-2 stale cast"));

            // V-1: the claimed caster is not the sender.
            var spoofed = new SkillCastRequest(SPOOFED_CASTER_RAW, SKILL_MAIN, _target.RawValue, TICK);
            Send(handler, _caster, in spoofed);
            // V-2: client tick far behind the server tick.
            var stale = new SkillCastRequest(_caster.RawValue, SKILL_MAIN, _target.RawValue, TICK - TOLERANCE - 5u);
            Send(handler, _caster, in stale);
            // Control: a valid request from another player in the same tick proves the pipeline is wired.
            var valid = new SkillCastRequest(_a.RawValue, SKILL_MAIN, _target.RawValue, TICK);
            Send(handler, _a, in valid);

            Assert.That(PendingCount(intake), Is.EqualTo(1), "only the valid request was queued");

            step.Run(TICK, 0.05f);

            Assert.That(Results(CLIENT_CASTER).Count, Is.EqualTo(0));
            Assert.That(Results(CLIENT_A).Count, Is.EqualTo(1), "the control request produced its one result");
            Assert.That(Total(RUBatchCategory.SkillCastResult), Is.EqualTo(1), "nothing else was sent");
        }

        private static int PendingCount(SkillCastIntake intake)
        {
            int n = 0;
            for (int i = 0; i < intake.Capacity; i++)
            {
                if (intake.TryGetPending(i, out _))
                {
                    n++;
                }
            }

            return n;
        }

        // ---- fault handling ----

        [Test]
        public void test_castoutput_faulting_party_source_still_delivers_caster_result_and_update_and_logs_once()
        {
            var emitter = new SkillCastOutputEmitter(_pool, Zone, _tracker, new ThrowingParty(), _collector, _service);
            _service.SetCooldownForTesting(_caster, 0, EXPIRY);
            LogAssert.Expect(LogType.Error, new Regex(@"\[SkillCastOutputEmitter\] SendAcceptedThrew: InvalidOperationException: party source fault"));

            Assert.DoesNotThrow(() => emitter.SendAccepted(_caster, in _request, COOLDOWN_TICKS, in _outcome));

            Assert.That(Results(CLIENT_CASTER).Count, Is.EqualTo(1));
            Assert.That(Updates(CLIENT_CASTER).Count, Is.EqualTo(1));
        }

        [Test]
        public void test_castoutput_repeated_faults_log_only_once()
        {
            var emitter = new SkillCastOutputEmitter(_pool, Zone, _tracker, new ThrowingParty(), _collector, _service);
            LogAssert.Expect(LogType.Error, new Regex(@"SendAcceptedThrew"));

            emitter.SendAccepted(_caster, in _request, COOLDOWN_TICKS, in _outcome);
            emitter.SendAccepted(_caster, in _request, COOLDOWN_TICKS, in _outcome);

            // The second fault adds no log: TearDown's NoUnexpectedReceived fails the test if it did.
            Assert.That(Results(CLIENT_CASTER).Count, Is.EqualTo(2));
        }

        // ---- recipient edge cases ----

        [Test]
        public void test_castoutput_party_member_target_without_a_target_set_receives_exactly_one()
        {
            _party.Add(_a, _caster);
            _request = new SkillCastRequest(_caster.RawValue, SKILL_MAIN, _a.RawValue, 0u);

            Accept();

            Assert.That(Results(CLIENT_A).Count, Is.EqualTo(1));
            Assert.That(Total(RUBatchCategory.SkillCastResult), Is.EqualTo(2));
        }

        [Test]
        public void test_castoutput_target_equal_to_caster_sends_exactly_one_result()
        {
            _request = new SkillCastRequest(_caster.RawValue, SKILL_MAIN, _caster.RawValue, 0u);

            Accept();

            Assert.That(Results(CLIENT_CASTER).Count, Is.EqualTo(1));
            Assert.That(Total(RUBatchCategory.SkillCastResult), Is.EqualTo(1));
        }

        [Test]
        public void test_castoutput_client_that_is_the_target_and_targets_the_caster_receives_exactly_one()
        {
            SetTarget(CLIENT_A, _a, _caster);
            _request = new SkillCastRequest(_caster.RawValue, SKILL_MAIN, _a.RawValue, 0u);

            Accept();

            Assert.That(Results(CLIENT_A).Count, Is.EqualTo(1));
            Assert.That(Total(RUBatchCategory.SkillCastResult), Is.EqualTo(2));
        }

        private static void Send(SkillCastRequestHandler handler, EntityID sender, in SkillCastRequest request)
        {
            var bytes = new byte[SkillCastRequest.BodySize];
            SkillCastRequestCodec.WriteBody(bytes, in request);
            var context = new InboundRequestContext(CLIENT_CASTER, sender, default, SkillCastRequest.MessageTypeId, TICK, TICK, false);
            handler.Handle(in context, bytes);
        }

        // ---- non-player caster ----

        [Test]
        public void test_castoutput_non_player_caster_sends_nothing_and_does_not_throw()
        {
            var mob = new EntityID(NON_PLAYER_RAW);
            var request = new SkillCastRequest(NON_PLAYER_RAW, SKILL_MAIN, _target.RawValue, 0u);

            Assert.DoesNotThrow(() =>
            {
                _emitter.SendAccepted(mob, in request, COOLDOWN_TICKS, in _outcome);
                _emitter.SendRejection(mob, in request, SkillCastRejectionCode.OnCooldown);
            });

            Assert.That(_collector.Count, Is.EqualTo(0));
        }

        // ---- party adapter ----

        [Test]
        public void test_castoutput_party_adapter_over_no_party_presence_is_always_false()
        {
            LogAssert.Expect(LogType.Warning, NoPartyZonePresence.NOT_IMPLEMENTED_MESSAGE);
            var adapter = new ZoneSkillCastPartySource(_pool, new NoPartyZonePresence());

            Assert.That(adapter.AreInSameParty(_caster, _a), Is.False);
            Assert.That(adapter.AreInSameParty(_caster, _caster), Is.False);
        }

        [Test]
        public void test_castoutput_party_adapter_same_formal_party_is_true_and_different_or_missing_is_false()
        {
            var presence = new FakePresence();
            presence.Parties[CLIENT_CASTER] = new PartyID(PARTY_RAW);
            presence.Parties[CLIENT_A] = new PartyID(PARTY_RAW);
            presence.Parties[CLIENT_B] = new PartyID(OTHER_PARTY_RAW);
            var adapter = new ZoneSkillCastPartySource(_pool, presence);

            Assert.That(adapter.AreInSameParty(_caster, _a), Is.True);
            Assert.That(adapter.AreInSameParty(_a, _caster), Is.True);
            Assert.That(adapter.AreInSameParty(_caster, _b), Is.False, "different party");
            Assert.That(adapter.AreInSameParty(_caster, _c), Is.False, "one side has no party");
            Assert.That(adapter.AreInSameParty(_c, _d), Is.False, "neither has a party");
        }

        [Test]
        public void test_castoutput_party_adapter_non_player_entity_is_false()
        {
            var presence = new FakePresence();
            presence.Parties[CLIENT_CASTER] = new PartyID(PARTY_RAW);
            var adapter = new ZoneSkillCastPartySource(_pool, presence);

            Assert.That(adapter.AreInSameParty(_caster, _target), Is.False);
            Assert.That(adapter.AreInSameParty(new EntityID(NON_PLAYER_RAW), _caster), Is.False);
        }

        // ---- constructors ----

        [Test]
        public void test_castoutput_emitter_null_constructor_arguments_throw()
        {
            Assert.Throws<ArgumentNullException>(() => new SkillCastOutputEmitter(null, Zone, _tracker, _party, _collector, _service));
            Assert.Throws<ArgumentNullException>(() => new SkillCastOutputEmitter(_pool, Zone, null, _party, _collector, _service));
            Assert.Throws<ArgumentNullException>(() => new SkillCastOutputEmitter(_pool, Zone, _tracker, null, _collector, _service));
            Assert.Throws<ArgumentNullException>(() => new SkillCastOutputEmitter(_pool, Zone, _tracker, _party, null, _service));
            Assert.Throws<ArgumentNullException>(() => new SkillCastOutputEmitter(_pool, Zone, _tracker, _party, _collector, null));
        }

        [Test]
        public void test_castoutput_party_adapter_null_constructor_arguments_throw()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneSkillCastPartySource(null, new NoPartyZonePresence()));
            Assert.Throws<ArgumentNullException>(() => new ZoneSkillCastPartySource(_pool, null));
        }

        // ---- allocation ----

        [Test]
        public void test_castoutput_accepted_cast_with_three_recipients_allocates_nothing()
        {
            SetTarget(CLIENT_A, _a, _target);
            _party.Add(_b, _caster);
            _service.SetCooldownForTesting(_caster, 0, EXPIRY);
            _collector.CountOnly = true;
            Accept();   // warm-up
            Assert.That(_collector.Count, Is.EqualTo(4), "caster + A + B results and one update");

            Assert.That(() => _emitter.SendAccepted(_caster, in _request, COOLDOWN_TICKS, in _outcome),
                Is.Not.AllocatingGCMemory());
        }
    }
}
