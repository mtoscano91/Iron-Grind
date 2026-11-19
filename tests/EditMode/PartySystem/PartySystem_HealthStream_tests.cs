using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.PartySystem;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.PartySystem;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.PartySystem
{
    /// <summary>
    /// EditMode tests for Party System Story 010: the party health stream (AC-1 recipients, AC-2 values, AC-3 send
    /// rule, AC-4 full suppression, AC-5 record lifetime, AC-6 batch, AC-7 robustness and cost). Time is explicit ticks.
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_HealthStream_Tests
    {
        private const uint ENTITY_BASE = 1000u;
        private const uint CLIENT_BASE = 100u;
        private const uint A = 1u;
        private const uint B = 2u;
        private const uint C = 3u;
        private const uint D = 4u;
        private const float FULL_HP = 100f;
        private const float FULL_MP = 50f;
        private const int MAX_HP = 100;
        private const int MAX_MP = 50;
        private const uint START_TICK = 1000u;
        private const int FULL_TICKS = 10;
        private const int GC_WARMUP_TICKS = 3;
        private const int GC_TICKS = 20;
        private const int ZONE_CAPACITY = 32;
        private const uint ZONE_RAW = 7u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const int POOL_PLAYERS = 8;
        private const int POOL_MOBS = 4;

        private struct Stat
        {
            public float Hp;
            public float Mp;
            public int MaxHp;
            public int MaxMp;
        }

        private sealed class FakeStats : IZoneEntityStatReader
        {
            private readonly Dictionary<EntityID, Stat> _stats = new Dictionary<EntityID, Stat>();
            private readonly HashSet<EntityID> _throwing = new HashSet<EntityID>();

            public void Set(EntityID entity, float hp, int maxHp, float mp, int maxMp)
                => _stats[entity] = new Stat { Hp = hp, Mp = mp, MaxHp = maxHp, MaxMp = maxMp };

            public void ThrowFor(EntityID entity) => _throwing.Add(entity);

            public void StopThrowing() => _throwing.Clear();

            private Stat Read(EntityID entity)
            {
                if (_throwing.Contains(entity)) throw new InvalidOperationException("stat read failed");
                return _stats.TryGetValue(entity, out Stat s)
                    ? s
                    : new Stat { Hp = FULL_HP, Mp = FULL_MP, MaxHp = MAX_HP, MaxMp = MAX_MP };
            }

            public float GetCurrentHP(EntityID entityId) => Read(entityId).Hp;

            public float GetCurrentMP(EntityID entityId) => Read(entityId).Mp;

            public int GetEffectiveStat(EntityID entityId, StatID statId)
            {
                Stat s = Read(entityId);
                return statId == StatID.MaxHP ? s.MaxHp : statId == StatID.MaxMP ? s.MaxMp : 0;
            }
        }

        private sealed class FakeClients : IPartyClientResolver
        {
            private readonly Dictionary<CharacterID, uint> _clients = new Dictionary<CharacterID, uint>();
            private readonly HashSet<CharacterID> _throwing = new HashSet<CharacterID>();

            public void Map(CharacterID character, uint client) => _clients[character] = client;

            public void Unmap(CharacterID character) => _clients.Remove(character);

            public void ThrowFor(CharacterID character) => _throwing.Add(character);

            public bool TryGetClient(CharacterID character, out uint clientId)
            {
                if (_throwing.Contains(character)) throw new InvalidOperationException("client lookup failed");
                return _clients.TryGetValue(character, out clientId);
            }
        }

        private sealed class Sent
        {
            public uint Client;
            public RUBatchCategory Category;
            public ushort Type;
            public PartyMemberHealthUpdate Update;
        }

        internal static PartyMemberHealthUpdate Decode(ushort type, ReadOnlySpan<byte> body)
        {
            byte[] buffer = new byte[PartyMemberHealthUpdate.BatchSize];
            BatchSubMessageFraming.WriteHeader(buffer, (ushort)body.Length, type);
            body.CopyTo(buffer.AsSpan(BatchSubMessageFraming.SubMessageHeaderSize));
            Assert.IsTrue(BatchSubMessageCodec.TryReadPartyMemberHealthUpdate(buffer, out PartyMemberHealthUpdate update, out _));
            return update;
        }

        private sealed class RecordingCollector : IRUBatchCollector
        {
            public readonly List<Sent> Messages = new List<Sent>();

            public void Enqueue(uint clientId, RUBatchCategory category, ushort messageTypeId, ReadOnlySpan<byte> body)
                => Messages.Add(new Sent
                {
                    Client = clientId,
                    Category = category,
                    Type = messageTypeId,
                    Update = Decode(messageTypeId, body),
                });

            public void EnqueueDamageEvent(uint clientId, in DamageEvent damageEvent) { }

            public int CountAbout(uint recipient, uint member)
            {
                int n = 0;
                foreach (Sent s in Messages)
                {
                    if (s.Client == Client(recipient) && s.Update.EntityId == En(member)) n++;
                }

                return n;
            }

            public Sent Last(uint recipient, uint member)
            {
                for (int i = Messages.Count - 1; i >= 0; i--)
                {
                    if (Messages[i].Client == Client(recipient) && Messages[i].Update.EntityId == En(member)) return Messages[i];
                }

                throw new InvalidOperationException("no update to " + recipient + " about " + member);
            }
        }

        private sealed class CountingCollector : IRUBatchCollector
        {
            public int Count;

            public void Enqueue(uint clientId, RUBatchCategory category, ushort messageTypeId, ReadOnlySpan<byte> body)
                => Count++;

            public void EnqueueDamageEvent(uint clientId, in DamageEvent damageEvent) { }
        }

        private PartyRegistry _registry;
        private FakeStats _stats;
        private FakeClients _clients;
        private RecordingCollector _collector;
        private PartyHealthStreamStep _step;
        private uint _tick;

        private static CharacterID Ch(uint n) => new CharacterID(n);

        private static EntityID En(uint n) => new EntityID(ENTITY_BASE + n);

        private static uint Client(uint n) => CLIENT_BASE + n;

        private static PartySystemConfigValues Config(float threshold = PartySystemConfigValues.HP_DELTA_THRESHOLD_FRACTION_DEFAULT)
        {
            return new PartySystemConfigValues(
                PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT,
                PartySystemConfigValues.XP_PARTY_DEDUCTION_RATE_DEFAULT,
                PartySystemConfigValues.LEVEL_RANGE_GATE_DEFAULT,
                PartySystemConfigValues.INVITE_TIMEOUT_SECONDS_DEFAULT,
                PartySystemConfigValues.INVITE_RETRY_SECONDS_DEFAULT,
                PartySystemConfigValues.RECONNECT_GRACE_SECONDS_DEFAULT,
                threshold,
                PartySystemConfigValues.DISBAND_TIMEOUT_SECONDS_DEFAULT);
        }

        [SetUp]
        public void SetUp()
        {
            _registry = new PartyRegistry(Config());
            _stats = new FakeStats();
            _clients = new FakeClients();
            _collector = new RecordingCollector();
            _step = new PartyHealthStreamStep(_registry, _stats, _clients, _collector, Config(), ZONE_CAPACITY);
            _tick = START_TICK;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.NoUnexpectedReceived();
        }

        private void Reg(uint n)
        {
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.RegisterPlayer(Ch(n), En(n), out _));
            _clients.Map(Ch(n), Client(n));
        }

        private PartyID MakeParty(uint leader, params uint[] members)
        {
            Reg(leader);
            foreach (uint member in members)
            {
                Reg(member);
            }

            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryCreateParty(Ch(leader), out PartyID id));
            foreach (uint member in members)
            {
                Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(id, Ch(member)));
            }

            return id;
        }

        private void RunTick()
        {
            _tick++;
            _step.Run(_tick);
        }

        private void RunAndClear()
        {
            RunTick();
            _collector.Messages.Clear();
        }

        private void SetStat(uint n, float hp, int maxHp = MAX_HP, float mp = FULL_MP, int maxMp = MAX_MP)
            => _stats.Set(En(n), hp, maxHp, mp, maxMp);

        // ── AC-1 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_health_stream_only_online_and_dead_members_exchange_updates()
        {
            MakeParty(A, B, C, D);
            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Dead);
            _registry.SetMemberStatus(Ch(C), PartyMemberStatus.Ghost);
            _registry.SetMemberStatus(Ch(D), PartyMemberStatus.OutOfZone);

            RunTick();

            Assert.AreEqual(2, _collector.Messages.Count);
            Assert.AreEqual(1, _collector.CountAbout(A, B));
            Assert.AreEqual(1, _collector.CountAbout(B, A));
            foreach (Sent s in _collector.Messages)
            {
                Assert.AreNotEqual(En(C), s.Update.EntityId);
                Assert.AreNotEqual(En(D), s.Update.EntityId);
                Assert.AreNotEqual(Client(C), s.Client);
                Assert.AreNotEqual(Client(D), s.Client);
            }
        }

        [Test]
        public void test_health_stream_party_of_three_sends_every_ordered_pair_once()
        {
            MakeParty(A, B, C);

            RunTick();

            Assert.AreEqual(6, _collector.Messages.Count);
            foreach (uint r in new[] { A, B, C })
            {
                foreach (uint m in new[] { A, B, C })
                {
                    Assert.AreEqual(r == m ? 0 : 1, _collector.CountAbout(r, m), r + " about " + m);
                }
            }
        }

        [Test]
        public void test_health_stream_solo_player_sends_nothing()
        {
            Reg(A);

            RunTick();

            Assert.AreEqual(0, _collector.Messages.Count);
        }

        [Test]
        public void test_health_stream_disbanding_party_sends_nothing()
        {
            PartyID party = MakeParty(A, B);
            _registry.SetStateForTests(party, PartyLifecycleState.Disbanding);

            RunTick();

            Assert.AreEqual(0, _collector.Messages.Count);
        }

        [Test]
        public void test_health_stream_recipient_without_a_client_is_skipped_silently()
        {
            MakeParty(A, B);
            _clients.Unmap(Ch(A));

            RunTick();

            Assert.AreEqual(1, _collector.Messages.Count);
            Assert.AreEqual(1, _collector.CountAbout(B, A));
        }

        // ── AC-2 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_health_stream_values_are_floored_absolute_numbers_from_the_stat_reader()
        {
            MakeParty(A, B);
            SetStat(B, 99.7f, 120, 25.9f, 60);

            RunTick();

            Sent sent = _collector.Last(A, B);
            Assert.AreEqual(RUBatchCategory.PartyMemberHealthUpdate, sent.Category);
            Assert.AreEqual(PartyMemberHealthUpdate.MessageTypeId, sent.Type);
            Assert.AreEqual(En(B), sent.Update.EntityId);
            Assert.AreEqual(99, sent.Update.CurrentHP);
            Assert.AreEqual(120, sent.Update.MaxHP);
            Assert.AreEqual(25, sent.Update.CurrentMP);
            Assert.AreEqual(60, sent.Update.MaxMP);
        }

        [Test]
        public void test_health_stream_dead_member_is_sent_with_zero_hp_whatever_the_stat_reads()
        {
            MakeParty(A, B);
            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Dead);
            SetStat(B, 12f);

            RunTick();

            Sent sent = _collector.Last(A, B);
            Assert.AreEqual(0, sent.Update.CurrentHP);
            Assert.AreEqual(MAX_HP, sent.Update.MaxHP);
            Assert.AreEqual(1, _collector.CountAbout(B, A), "a Dead recipient still receives");
        }

        // ── AC-3 ───────────────────────────────────────────────────────────────────────────

        private int ChangeAndCount(float baseHp, float baseMp, float newHp, float newMp, int maxHp = MAX_HP,
            int maxMp = MAX_MP, int baseMaxHp = MAX_HP, int baseMaxMp = MAX_MP)
        {
            MakeParty(A, B);
            SetStat(B, baseHp, baseMaxHp, baseMp, baseMaxMp);
            RunAndClear();
            SetStat(B, newHp, maxHp, newMp, maxMp);
            RunTick();
            return _collector.CountAbout(A, B);
        }

        [TestCase(96f, 0)]
        [TestCase(95f, 1)]
        public void test_health_stream_hp_change_sends_at_the_threshold_only(float newHp, int expected)
        {
            Assert.AreEqual(expected, ChangeAndCount(FULL_HP, FULL_MP, newHp, FULL_MP));
        }

        [TestCase(48f, 0)]
        [TestCase(47f, 1)]
        public void test_health_stream_mp_change_sends_at_the_threshold_only(float newMp, int expected)
        {
            Assert.AreEqual(expected, ChangeAndCount(FULL_HP, FULL_MP, FULL_HP, newMp));
        }

        [Test]
        public void test_health_stream_max_hp_change_sends_at_the_same_hp()
        {
            Assert.AreEqual(1, ChangeAndCount(100f, FULL_MP, 100f, FULL_MP, 110));
        }

        [Test]
        public void test_health_stream_max_mp_change_sends_at_the_same_mp()
        {
            Assert.AreEqual(1, ChangeAndCount(100f, FULL_MP, 100f, FULL_MP, MAX_HP, 60));
        }

        [Test]
        public void test_health_stream_reaching_full_sends_even_below_the_threshold()
        {
            MakeParty(A, B);
            SetStat(B, 96f);
            RunAndClear();
            SetStat(B, 100f);

            RunTick();

            Assert.AreEqual(1, _collector.CountAbout(A, B));
            Assert.AreEqual(100, _collector.Last(A, B).Update.CurrentHP);
        }

        [Test]
        public void test_health_stream_hp_reaching_zero_sends_below_the_threshold()
        {
            Assert.AreEqual(1, ChangeAndCount(3f, FULL_MP, 0f, FULL_MP));
        }

        [Test]
        public void test_health_stream_hp_leaving_zero_sends_below_the_threshold()
        {
            Assert.AreEqual(1, ChangeAndCount(0f, FULL_MP, 2f, FULL_MP));
        }

        [Test]
        public void test_health_stream_unchanged_values_send_nothing_after_the_first_send()
        {
            MakeParty(A, B);
            SetStat(B, 60f, MAX_HP, 20f, MAX_MP);
            RunAndClear();

            RunTick();
            RunTick();

            Assert.AreEqual(0, _collector.Messages.Count);
        }

        [Test]
        public void test_health_stream_small_changes_are_measured_against_the_last_sent_value()
        {
            MakeParty(A, B);
            SetStat(B, 90f);
            RunAndClear();
            SetStat(B, 94f);
            RunTick();
            SetStat(B, 86f);
            RunTick();

            Assert.AreEqual(0, _collector.CountAbout(A, B), "94 and 86 are both under 5 from the recorded 90");
            SetStat(B, 85f);
            RunTick();
            Assert.AreEqual(1, _collector.CountAbout(A, B));
            Assert.AreEqual(85, _collector.Last(A, B).Update.CurrentHP);
        }

        [Test]
        public void test_health_stream_zero_threshold_sends_any_change_but_not_equal_values()
        {
            _step = new PartyHealthStreamStep(_registry, _stats, _clients, _collector, Config(0f), ZONE_CAPACITY);
            MakeParty(A, B);
            SetStat(B, 80f);
            RunAndClear();

            RunTick();
            Assert.AreEqual(0, _collector.CountAbout(A, B), "equal values never send");
            SetStat(B, 79f);
            RunTick();

            Assert.AreEqual(1, _collector.CountAbout(A, B));
        }

        [Test]
        public void test_health_stream_non_positive_max_makes_any_change_of_that_value_send()
        {
            MakeParty(A, B);
            SetStat(B, 5f, 0, FULL_MP, MAX_MP);
            RunAndClear();
            SetStat(B, 6f, 0, FULL_MP, MAX_MP);

            RunTick();

            Assert.AreEqual(1, _collector.CountAbout(A, B));
        }

        [Test]
        public void test_health_stream_dying_sends_zero_and_respawn_sends_the_new_hp()
        {
            MakeParty(A, B);
            SetStat(B, 3f);
            RunAndClear();

            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Dead);
            RunTick();
            Assert.AreEqual(0, _collector.Last(A, B).Update.CurrentHP);
            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Online);
            SetStat(B, 50f);
            RunTick();

            Assert.AreEqual(2, _collector.CountAbout(A, B));
            Assert.AreEqual(50, _collector.Last(A, B).Update.CurrentHP);
        }

        // ── AC-4 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_health_stream_full_and_recorded_full_sends_nothing_for_ten_ticks()
        {
            MakeParty(A, B);
            RunAndClear();

            for (int i = 0; i < FULL_TICKS; i++)
            {
                RunTick();
            }

            Assert.AreEqual(0, _collector.Messages.Count);
        }

        [Test]
        public void test_health_stream_drop_and_return_to_full_send_once_each_then_nothing()
        {
            MakeParty(A, B);
            RunAndClear();

            SetStat(B, 90f);
            RunTick();
            Assert.AreEqual(1, _collector.CountAbout(A, B));
            Assert.AreEqual(90, _collector.Last(A, B).Update.CurrentHP);
            SetStat(B, 100f);
            RunTick();
            Assert.AreEqual(2, _collector.CountAbout(A, B));
            Assert.AreEqual(100, _collector.Last(A, B).Update.CurrentHP);
            for (int i = 0; i < FULL_TICKS; i++)
            {
                RunTick();
            }

            Assert.AreEqual(2, _collector.CountAbout(A, B));
        }

        // ── AC-5 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_health_stream_first_tick_sends_even_at_full_health()
        {
            MakeParty(A, B);
            SetStat(A, 70f, 90, 20f, 40);

            RunTick();

            Assert.AreEqual(2, _collector.Messages.Count);
            Assert.AreEqual(2, _step.RecordCount);
            Assert.AreEqual(1, _collector.CountAbout(A, B));
            Assert.AreEqual(1, _collector.CountAbout(B, A));
            PartyMemberHealthUpdate aboutB = _collector.Last(A, B).Update;
            Assert.AreEqual(En(B), aboutB.EntityId);
            Assert.AreEqual(MAX_HP, aboutB.CurrentHP);
            Assert.AreEqual(MAX_HP, aboutB.MaxHP);
            Assert.AreEqual(MAX_MP, aboutB.CurrentMP);
            Assert.AreEqual(MAX_MP, aboutB.MaxMP);
            PartyMemberHealthUpdate aboutA = _collector.Last(B, A).Update;
            Assert.AreEqual(En(A), aboutA.EntityId);
            Assert.AreEqual(70, aboutA.CurrentHP);
            Assert.AreEqual(90, aboutA.MaxHP);
            Assert.AreEqual(20, aboutA.CurrentMP);
            Assert.AreEqual(40, aboutA.MaxMP);
        }

        [Test]
        public void test_health_stream_member_going_ghost_and_back_is_sent_afresh_on_return()
        {
            MakeParty(A, B, C);
            RunAndClear();
            Assert.AreEqual(6, _step.RecordCount);

            _registry.SetMemberStatus(Ch(C), PartyMemberStatus.Ghost);
            RunTick();
            Assert.AreEqual(0, _collector.Messages.Count);
            Assert.AreEqual(2, _step.RecordCount);

            _registry.SetMemberStatus(Ch(C), PartyMemberStatus.Online);
            RunTick();

            Assert.AreEqual(4, _collector.Messages.Count);
            Assert.AreEqual(1, _collector.CountAbout(A, C));
            Assert.AreEqual(1, _collector.CountAbout(B, C));
            Assert.AreEqual(1, _collector.CountAbout(C, A));
            Assert.AreEqual(1, _collector.CountAbout(C, B));
            Assert.AreEqual(6, _step.RecordCount);
        }

        [Test]
        public void test_health_stream_member_leaving_and_rejoining_is_sent_afresh()
        {
            PartyID party = MakeParty(A, B, C);
            RunAndClear();

            Assert.AreEqual(PartyRegistryResult.Ok, _registry.RemoveMember(party, Ch(C), out _));
            RunTick();
            Assert.AreEqual(0, _collector.Messages.Count);
            Assert.AreEqual(2, _step.RecordCount);

            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(party, Ch(C)));
            RunTick();

            Assert.AreEqual(4, _collector.Messages.Count);
            Assert.AreEqual(6, _step.RecordCount);
        }

        [Test]
        public void test_health_stream_records_of_a_disbanded_party_are_dropped()
        {
            PartyID party = MakeParty(A, B, C);
            RunAndClear();
            Assert.AreEqual(6, _step.RecordCount);

            Assert.AreEqual(PartyRegistryResult.Ok, _registry.DisbandParty(party));
            RunTick();

            Assert.AreEqual(0, _collector.Messages.Count);
            Assert.AreEqual(0, _step.RecordCount);
        }

        [Test]
        public void test_health_stream_records_of_a_disbanding_party_are_dropped()
        {
            PartyID party = MakeParty(A, B);
            RunAndClear();

            _registry.SetStateForTests(party, PartyLifecycleState.Disbanding);
            RunTick();

            Assert.AreEqual(0, _step.RecordCount);
        }

        [Test]
        public void test_health_stream_capacity_is_zone_capacity_times_max_party_size_minus_one()
        {
            Assert.AreEqual(ZONE_CAPACITY * (PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT - 1), _step.RecordCapacity);
        }

        [Test]
        public void test_health_stream_storage_overflow_still_sends_unremembered_pairs_with_one_warning_per_run()
        {
            _step = new PartyHealthStreamStep(_registry, _stats, _clients, _collector, Config(), 1);
            MakeParty(A, B, C);
            Assert.AreEqual(3, _step.RecordCapacity);
            LogAssert.Expect(LogType.Warning, new Regex("record storage full"));

            RunTick();

            Assert.AreEqual(6, _collector.Messages.Count);
            Assert.AreEqual(3, _step.RecordCount);
            _collector.Messages.Clear();
            LogAssert.Expect(LogType.Warning, new Regex("record storage full"));

            RunTick();

            Assert.AreEqual(3, _collector.Messages.Count, "the three unremembered pairs are sent again");
            Assert.AreEqual(3, _step.RecordCount);
        }

        // ── AC-6 ───────────────────────────────────────────────────────────────────────────

        private sealed class FakeSeam : IClientMessageSendSeam
        {
            public readonly List<(uint Client, byte[] Data)> Packets = new List<(uint, byte[])>();

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
                => Packets.Add((clientId, message.ToArray()));
        }

        [Test]
        public void test_health_stream_through_the_real_batch_step_arrives_as_0x0305_after_the_entity_health_update()
        {
            var seam = new FakeSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(Client(A));
            outbox.AddClient(Client(B));
            var batch = new RUBatchOutboundStep(seam, outbox, 4);
            _step = new PartyHealthStreamStep(_registry, _stats, _clients, batch, Config(), ZONE_CAPACITY);
            MakeParty(A, B);
            SetStat(B, 77f, 100, 30f, 50);
            SetStat(A, 60f, 90, 20f, 40);

            RunTick();
            Span<byte> body = stackalloc byte[EntityHealthUpdate.BodySize];
            BatchSubMessageCodec.WriteEntityHealthUpdateBody(body, new EntityHealthUpdate(En(B), 5, 10));
            batch.Enqueue(Client(A), RUBatchCategory.EntityHealthUpdate, EntityHealthUpdate.MessageTypeId, body);
            batch.Flush(_tick);

            Assert.AreEqual(2, seam.Packets.Count);
            byte[] packet = null;
            foreach ((uint client, byte[] data) in seam.Packets)
            {
                if (client == Client(A)) packet = data;
            }

            Assert.IsNotNull(packet);
            Assert.IsTrue(BatchHeaderCodec.TryRead(packet, out _, out ushort count));
            Assert.AreEqual(2, count);
            var types = new List<ushort>();
            PartyMemberHealthUpdate decoded = default;
            int offset = BatchHeaderCodec.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> rest = packet.AsSpan(offset);
                Assert.IsTrue(BatchSubMessageFraming.TryReadHeader(rest, out ushort bodySize, out ushort type));
                types.Add(type);
                if (type == PartyMemberHealthUpdate.MessageTypeId)
                {
                    Assert.IsTrue(BatchSubMessageCodec.TryReadPartyMemberHealthUpdate(rest, out decoded, out _));
                }

                offset += BatchSubMessageFraming.SubMessageHeaderSize + bodySize;
            }

            Assert.AreEqual(EntityHealthUpdate.MessageTypeId, types[0]);
            Assert.AreEqual(PartyMemberHealthUpdate.MessageTypeId, types[1]);
            Assert.AreEqual(0x0305, types[1]);
            Assert.AreEqual(En(B), decoded.EntityId);
            Assert.AreEqual(77, decoded.CurrentHP);
            Assert.AreEqual(100, decoded.MaxHP);
            Assert.AreEqual(30, decoded.CurrentMP);
            Assert.AreEqual(50, decoded.MaxMP);

            byte[] packetB = null;
            foreach ((uint clientB, byte[] dataB) in seam.Packets)
            {
                if (clientB == Client(B)) packetB = dataB;
            }

            Assert.IsNotNull(packetB);
            Assert.IsTrue(BatchHeaderCodec.TryRead(packetB, out _, out ushort countB));
            Assert.AreEqual(1, countB);
            ReadOnlySpan<byte> restB = packetB.AsSpan(BatchHeaderCodec.HeaderSize);
            Assert.IsTrue(BatchSubMessageFraming.TryReadHeader(restB, out _, out ushort typeB));
            Assert.AreEqual(PartyMemberHealthUpdate.MessageTypeId, typeB);
            Assert.IsTrue(BatchSubMessageCodec.TryReadPartyMemberHealthUpdate(restB, out PartyMemberHealthUpdate decodedB, out _));
            Assert.AreEqual(En(A), decodedB.EntityId);
            Assert.AreEqual(60, decodedB.CurrentHP);
            Assert.AreEqual(90, decodedB.MaxHP);
            Assert.AreEqual(20, decodedB.CurrentMP);
            Assert.AreEqual(40, decodedB.MaxMP);
        }

        // ── AC-7 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_health_stream_a_throwing_stat_read_is_logged_once_and_other_pairs_still_send()
        {
            MakeParty(A, B, C);
            _stats.ThrowFor(En(B));
            LogAssert.Expect(LogType.Error, new Regex("read threw for"));

            RunTick();

            Assert.AreEqual(0, _collector.CountAbout(A, B));
            Assert.AreEqual(0, _collector.CountAbout(C, B));
            Assert.AreEqual(1, _collector.CountAbout(A, C));
            Assert.AreEqual(1, _collector.CountAbout(B, A));
            Assert.AreEqual(1, _collector.CountAbout(B, C));
            Assert.AreEqual(1, _collector.CountAbout(C, A));
            Assert.AreEqual(4, _collector.Messages.Count);
        }

        [Test]
        public void test_health_stream_the_record_sweep_still_runs_after_a_throwing_read()
        {
            MakeParty(A, B, C);
            RunAndClear();
            Assert.AreEqual(6, _step.RecordCount);
            _stats.ThrowFor(En(B));
            LogAssert.Expect(LogType.Error, new Regex("read threw for"));

            RunTick();

            Assert.AreEqual(4, _step.RecordCount, "the two pairs about B were not stamped and are dropped");
        }

        [Test]
        public void test_health_stream_a_failing_run_logs_again_on_the_next_run_but_once_per_run()
        {
            MakeParty(A, B, C);
            _stats.ThrowFor(En(B));
            LogAssert.Expect(LogType.Error, new Regex("read threw for"));
            RunTick();
            LogAssert.Expect(LogType.Error, new Regex("read threw for"));

            RunTick();

            Assert.AreEqual(4, _step.RecordCount);
        }

        [Test]
        public void test_health_stream_recovery_after_a_throwing_read_sends_the_member_afresh()
        {
            MakeParty(A, B);
            _stats.ThrowFor(En(B));
            LogAssert.Expect(LogType.Error, new Regex("read threw for"));
            RunTick();
            Assert.AreEqual(0, _collector.CountAbout(A, B));
            _stats.StopThrowing();

            RunTick();

            Assert.AreEqual(1, _collector.CountAbout(A, B));
        }

        [Test]
        public void test_health_stream_constructor_rejects_null_arguments_bad_capacity_and_invalid_config()
        {
            Assert.Throws<ArgumentNullException>(() => new PartyHealthStreamStep(null, _stats, _clients, _collector, Config(), 1));
            Assert.Throws<ArgumentNullException>(() => new PartyHealthStreamStep(_registry, null, _clients, _collector, Config(), 1));
            Assert.Throws<ArgumentNullException>(() => new PartyHealthStreamStep(_registry, _stats, null, _collector, Config(), 1));
            Assert.Throws<ArgumentNullException>(() => new PartyHealthStreamStep(_registry, _stats, _clients, null, Config(), 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PartyHealthStreamStep(_registry, _stats, _clients, _collector, Config(), 0));
            LogAssert.Expect(LogType.Error, new Regex("HP_DELTA_THRESHOLD_FRACTION"));

            Assert.Throws<ArgumentException>(() => new PartyHealthStreamStep(_registry, _stats, _clients, _collector, Config(0.5f), 1));
        }

        [Test]
        public void test_health_stream_run_allocates_nothing_for_a_party_of_four_with_changing_hp()
        {
            var counting = new CountingCollector();
            _step = new PartyHealthStreamStep(_registry, _stats, _clients, counting, Config(), ZONE_CAPACITY);
            MakeParty(A, B, C, D);
            uint[] members = { A, B, C, D };
            for (int t = 0; t < GC_WARMUP_TICKS; t++)
            {
                ChangeHp(members, t);
                RunTick();
            }

            int sentBefore = counting.Count;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = GC_WARMUP_TICKS; t < GC_WARMUP_TICKS + GC_TICKS; t++)
            {
                ChangeHp(members, t);
                RunTick();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
            Assert.Greater(counting.Count, sentBefore, "the HP changes did cause sends");
        }

        private void ChangeHp(uint[] members, int t)
        {
            foreach (uint n in members)
            {
                SetStat(n, 100f - 10f * ((t + (int)n) % 5));
            }
        }

        // ── Additional coverage ────────────────────────────────────────────────────────────

        [Test]
        public void test_health_stream_recipient_losing_and_regaining_a_client_is_sent_afresh()
        {
            MakeParty(A, B);
            SetStat(B, 80f);
            RunAndClear();
            Assert.AreEqual(2, _step.RecordCount);

            _clients.Unmap(Ch(A));
            RunTick();
            Assert.AreEqual(0, _collector.Messages.Count);
            Assert.AreEqual(1, _step.RecordCount, "A's record as recipient is dropped");

            _clients.Map(Ch(A), Client(A));
            RunTick();

            Assert.AreEqual(1, _collector.Messages.Count);
            Sent sent = _collector.Last(A, B);
            Assert.AreEqual(Client(A), sent.Client);
            Assert.AreEqual(80, sent.Update.CurrentHP);
            Assert.AreEqual(MAX_HP, sent.Update.MaxHP);
            Assert.AreEqual(2, _step.RecordCount);
        }

        [Test]
        public void test_health_stream_records_are_independent_per_recipient()
        {
            PartyID party = MakeParty(A, B);
            RunAndClear();
            SetStat(B, 96f);
            RunTick();
            Assert.AreEqual(0, _collector.CountAbout(A, B), "96 is under the threshold from the recorded 100");

            Reg(C);
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(party, Ch(C)));
            RunTick();
            Assert.AreEqual(1, _collector.CountAbout(C, B), "C has a fresh record about B");
            Assert.AreEqual(96, _collector.Last(C, B).Update.CurrentHP);
            Assert.AreEqual(0, _collector.CountAbout(A, B));
            _collector.Messages.Clear();

            SetStat(B, 95f);
            RunTick();

            Assert.AreEqual(1, _collector.CountAbout(A, B));
            Assert.AreEqual(95, _collector.Last(A, B).Update.CurrentHP);
            Assert.AreEqual(0, _collector.CountAbout(C, B), "C's record is 96, one away");
        }

        [Test]
        public void test_health_stream_a_throwing_resolver_logs_once_per_run_and_drops_that_recipients_records()
        {
            MakeParty(A, B, C);
            RunAndClear();
            Assert.AreEqual(6, _step.RecordCount);
            _clients.ThrowFor(Ch(A));
            SetStat(B, 50f);
            LogAssert.Expect(LogType.Error, new Regex("read threw for"));

            RunTick();

            Assert.AreEqual(1, _collector.Messages.Count);
            Assert.AreEqual(1, _collector.CountAbout(C, B));
            Assert.AreEqual(50, _collector.Last(C, B).Update.CurrentHP);
            Assert.AreEqual(Client(C), _collector.Last(C, B).Client);
            Assert.AreEqual(0, _collector.CountAbout(A, B));
            Assert.AreEqual(0, _collector.CountAbout(A, C));
            Assert.AreEqual(4, _step.RecordCount, "A's two records as recipient are dropped");
        }

        [TestCase(float.NaN, 100, 50f, 50, 0, 100, 50, 50)]
        [TestCase(-5f, 100, 50f, 50, 0, 100, 50, 50)]
        [TestCase(40f, 100, -3f, 50, 40, 100, 0, 50)]
        [TestCase(40f, -10, 20f, 50, 40, 0, 20, 50)]
        public void test_health_stream_negative_or_nan_stats_are_clamped_to_zero(float hp, int maxHp, float mp, int maxMp,
            int expHp, int expMaxHp, int expMp, int expMaxMp)
        {
            MakeParty(A, B);
            SetStat(B, hp, maxHp, mp, maxMp);

            RunTick();

            PartyMemberHealthUpdate u = _collector.Last(A, B).Update;
            Assert.AreEqual(En(B), u.EntityId);
            Assert.AreEqual(expHp, u.CurrentHP);
            Assert.AreEqual(expMaxHp, u.MaxHP);
            Assert.AreEqual(expMp, u.CurrentMP);
            Assert.AreEqual(expMaxMp, u.MaxMP);
        }

        [Test]
        public void test_health_stream_reaching_full_through_mp_only_sends_below_the_threshold()
        {
            MakeParty(A, B);
            SetStat(B, FULL_HP, MAX_HP, 48f, MAX_MP);
            RunAndClear();
            SetStat(B, FULL_HP, MAX_HP, 50f, MAX_MP);

            RunTick();

            Assert.AreEqual(1, _collector.CountAbout(A, B));
            Assert.AreEqual(50, _collector.Last(A, B).Update.CurrentMP);
            Assert.AreEqual(100, _collector.Last(A, B).Update.CurrentHP);
        }

        [Test]
        public void test_health_stream_dead_member_with_stat_hp_is_sent_zero_then_online_hp_three_is_sent()
        {
            MakeParty(A, B);
            SetStat(B, 50f);
            RunAndClear();

            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Dead);
            RunTick();
            Assert.AreEqual(1, _collector.CountAbout(A, B));
            Assert.AreEqual(0, _collector.Last(A, B).Update.CurrentHP);
            _collector.Messages.Clear();

            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Online);
            SetStat(B, 3f);
            RunTick();

            Assert.AreEqual(1, _collector.CountAbout(A, B));
            Assert.AreEqual(3, _collector.Last(A, B).Update.CurrentHP);
        }

        [TestCase(0.05f, 90f, 94f, 0)]
        [TestCase(0.05f, 90f, 95f, 1)]
        [TestCase(0.10f, 100f, 91f, 0)]
        [TestCase(0.10f, 100f, 90f, 1)]
        public void test_health_stream_threshold_applies_in_both_directions_and_for_other_fractions(float fraction,
            float baseHp, float newHp, int expected)
        {
            _step = new PartyHealthStreamStep(_registry, _stats, _clients, _collector, Config(fraction), ZONE_CAPACITY);
            MakeParty(A, B);
            SetStat(B, baseHp);
            RunAndClear();
            SetStat(B, newHp);

            RunTick();

            Assert.AreEqual(expected, _collector.CountAbout(A, B));
            if (expected == 1) Assert.AreEqual((int)newHp, _collector.Last(A, B).Update.CurrentHP);
        }

        [Test]
        public void test_health_stream_surviving_records_still_work_after_other_records_are_swap_removed()
        {
            MakeParty(A, B, C);
            RunAndClear();
            Assert.AreEqual(6, _step.RecordCount);

            _clients.Unmap(Ch(A));
            RunTick();
            Assert.AreEqual(0, _collector.Messages.Count);
            Assert.AreEqual(4, _step.RecordCount);

            RunTick();
            Assert.AreEqual(0, _collector.Messages.Count, "equal values stay suppressed after the swap-remove");
            SetStat(C, 90f);
            RunTick();

            Assert.AreEqual(1, _collector.Messages.Count);
            Sent toB = _collector.Last(B, C);
            Assert.AreEqual(Client(B), toB.Client);
            Assert.AreEqual(En(C), toB.Update.EntityId);
            Assert.AreEqual(90, toB.Update.CurrentHP);
            _collector.Messages.Clear();

            SetStat(B, 80f);
            RunTick();

            Assert.AreEqual(1, _collector.Messages.Count);
            Sent toC = _collector.Last(C, B);
            Assert.AreEqual(Client(C), toC.Client);
            Assert.AreEqual(En(B), toC.Update.EntityId);
            Assert.AreEqual(80, toC.Update.CurrentHP);
            Assert.AreEqual(4, _step.RecordCount);
        }

        [Test]
        public void test_health_stream_tick_number_wrap_keeps_records_and_suppresses_unchanged_values()
        {
            MakeParty(A, B);

            _step.Run(uint.MaxValue - 1u);
            Assert.AreEqual(2, _collector.Messages.Count);
            _collector.Messages.Clear();
            _step.Run(uint.MaxValue);
            _step.Run(0u);
            _step.Run(1u);

            Assert.AreEqual(0, _collector.Messages.Count);
            Assert.AreEqual(2, _step.RecordCount);
        }

        [Test]
        public void test_health_stream_run_allocates_nothing_with_status_toggles_and_leave_rejoin()
        {
            const int WARMUP = 6;
            var counting = new CountingCollector();
            _step = new PartyHealthStreamStep(_registry, _stats, _clients, counting, Config(), ZONE_CAPACITY);
            PartyID party = MakeParty(A, B, C, D);
            uint[] members = { A, B, C, D };
            for (int t = 0; t < WARMUP; t++)
            {
                ChurnTick(party, members, t);
            }

            int sentBefore = counting.Count;
            long allocated = 0L;
            for (int t = WARMUP; t < WARMUP + GC_TICKS; t++)
            {
                allocated += ChurnTick(party, members, t);
            }

            Assert.AreEqual(0L, allocated);
            Assert.Greater(counting.Count, sentBefore, "the churn did cause sends");
        }

        // Toggles C Ghost/Online and D leave/rejoin on alternate ticks; returns the bytes the Run call allocated.
        private long ChurnTick(PartyID party, uint[] members, int t)
        {
            ChangeHp(members, t);
            if (t % 2 == 0)
            {
                _registry.SetMemberStatus(Ch(C), PartyMemberStatus.Ghost);
                Assert.AreEqual(PartyRegistryResult.Ok, _registry.RemoveMember(party, Ch(D), out _));
            }
            else
            {
                _registry.SetMemberStatus(Ch(C), PartyMemberStatus.Online);
                Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(party, Ch(D)));
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunTick();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        // ── Production resolver ────────────────────────────────────────────────────────────

        [Test]
        public void test_health_stream_pool_resolver_registered_entity_without_a_pool_slot_is_false_and_slot_client_wins()
        {
            const uint SLOT_CLIENT = 777u;
            var zone = new ZoneID(ZONE_RAW);
            var pool = new ZoneSlotPool(zone, new FakeTicks(), new FakePoolListener(), POOL_PLAYERS, POOL_MOBS);
            var outbox = new ClientMessageOutbox(new FakeSeam());
            outbox.AddClient(SLOT_CLIENT);
            outbox.AddClient(Client(C));
            Assert.IsTrue(pool.TryAllocatePlayer(zone, Ch(A), new AccountID(A + ACCOUNT_OFFSET), SLOT_CLIENT, out _, out EntityID entityA));
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.RegisterPlayer(Ch(A), entityA, out _));
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.RegisterPlayer(Ch(C), En(C), out _));
            var resolver = new PoolPartyClientResolver(_registry, pool, outbox);

            Assert.IsFalse(resolver.TryGetClient(Ch(C), out _), "registered entity, no pool slot");
            Assert.IsTrue(resolver.TryGetClient(Ch(A), out uint client));
            Assert.AreEqual(SLOT_CLIENT, client);
            Assert.AreNotEqual(Client(A), client);
        }

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => START_TICK;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        [Test]
        public void test_health_stream_pool_resolver_finds_pooled_clients_in_the_outbox_only()
        {
            var zone = new ZoneID(ZONE_RAW);
            var pool = new ZoneSlotPool(zone, new FakeTicks(), new FakePoolListener(), POOL_PLAYERS, POOL_MOBS);
            var outbox = new ClientMessageOutbox(new FakeSeam());
            outbox.AddClient(Client(A));
            Assert.IsTrue(pool.TryAllocatePlayer(zone, Ch(A), new AccountID(A + ACCOUNT_OFFSET), Client(A), out _, out EntityID entityA));
            Assert.IsTrue(pool.TryAllocatePlayer(zone, Ch(B), new AccountID(B + ACCOUNT_OFFSET), Client(B), out _, out EntityID entityB));
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.RegisterPlayer(Ch(A), entityA, out _));
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.RegisterPlayer(Ch(B), entityB, out _));
            var resolver = new PoolPartyClientResolver(_registry, pool, outbox);

            Assert.IsTrue(resolver.TryGetClient(Ch(A), out uint client));
            Assert.AreEqual(Client(A), client);
            Assert.IsFalse(resolver.TryGetClient(Ch(B), out _), "no outbox connection");
            Assert.IsFalse(resolver.TryGetClient(Ch(C), out _), "not registered");
        }
    }
}
