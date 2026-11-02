using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.SkillSystem;
using NUnit.Framework;
using UnityEngine.TestTools;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode tests for <see cref="SkillUnlockOutputEmitter"/> (Skill System Story 003, CR-SK-21), over the real
    /// <see cref="ZoneSlotPool"/> and <see cref="ClientMessageOutbox"/> with a recording send seam.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_UnlockOutputEmitter_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint CLIENT_A = 11u;
        private const uint CLIENT_B = 12u;
        private const uint CLIENT_NOT_IN_OUTBOX = 13u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const uint TICK = 4242u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const uint SKILL_VALUE = 0x00A1B2C3u;
        private const uint UNMAPPED_ENTITY_RAW = 999999u;

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

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private ZoneSlotPool _pool;
        private FakeSeam _seam;
        private ClientMessageOutbox _outbox;
        private SkillUnlockOutputEmitter _emitter;

        [SetUp]
        public void SetUp()
        {
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _seam = new FakeSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _outbox.AddClient(CLIENT_A);
            _outbox.AddClient(CLIENT_B);
            _emitter = new SkillUnlockOutputEmitter(_pool, _outbox);
        }

        private EntityID AddPlayer(uint client)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + ACCOUNT_OFFSET), client, out _, out EntityID id));
            return id;
        }

        private void Flush()
        {
            _outbox.Flush(TICK);
        }

        private static SkillUnlockNotification Decode(Sent packet)
        {
            Assert.That(MessageEnvelopeCodec.TryRead(packet.Data, out ServerMessageEnvelope envelope), Is.True);
            Assert.That(envelope.MessageTypeId, Is.EqualTo(SkillUnlockNotification.MessageTypeId));
            ReadOnlySpan<byte> body = packet.Data.AsSpan(ServerMessageEnvelope.WireSize);
            Assert.That(SkillUnlockNotificationCodec.TryReadBody(body, out SkillUnlockNotification message), Is.True);
            return message;
        }

        [Test]
        public void test_unlock_emitter_send_enqueues_notification_to_the_owners_client()
        {
            EntityID owner = AddPlayer(CLIENT_A);

            _emitter.SendUnlock(owner, new SkillID(SKILL_VALUE));
            Flush();

            Assert.That(_seam.Packets.Count, Is.EqualTo(1));
            Assert.That(_seam.Packets[0].ClientId, Is.EqualTo(CLIENT_A));
            Assert.That(Decode(_seam.Packets[0]).SkillId, Is.EqualTo(new SkillID(SKILL_VALUE)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_unlock_emitter_two_players_each_receive_only_their_own_notice()
        {
            EntityID a = AddPlayer(CLIENT_A);
            EntityID b = AddPlayer(CLIENT_B);

            _emitter.SendUnlock(a, new SkillID(SKILL_VALUE));
            _emitter.SendUnlock(b, new SkillID(SKILL_VALUE + 1));
            Flush();

            Assert.That(_seam.Packets.Count, Is.EqualTo(2));
            foreach (Sent packet in _seam.Packets)
            {
                uint expected = packet.ClientId == CLIENT_A ? SKILL_VALUE : SKILL_VALUE + 1;
                Assert.That(Decode(packet).SkillId.Value, Is.EqualTo(expected), "client " + packet.ClientId);
            }

            Assert.That(_seam.Packets[0].ClientId, Is.Not.EqualTo(_seam.Packets[1].ClientId));
        }

        [Test]
        public void test_unlock_emitter_owner_not_in_pool_sends_nothing_and_logs_nothing()
        {
            Assert.DoesNotThrow(() => _emitter.SendUnlock(new EntityID(UNMAPPED_ENTITY_RAW), new SkillID(SKILL_VALUE)));
            Flush();

            Assert.That(_seam.Packets, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_unlock_emitter_pool_slot_without_outbox_client_sends_nothing_and_logs_nothing()
        {
            EntityID ghost = AddPlayer(CLIENT_NOT_IN_OUTBOX);

            Assert.DoesNotThrow(() => _emitter.SendUnlock(ghost, new SkillID(SKILL_VALUE)));
            Flush();

            Assert.That(_seam.Packets, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void test_unlock_emitter_constructor_null_argument_throws(bool nullPool)
        {
            ZoneSlotPool pool = nullPool ? null : _pool;
            ClientMessageOutbox outbox = nullPool ? _outbox : null;

            Assert.Throws<ArgumentNullException>(() => new SkillUnlockOutputEmitter(pool, outbox));
        }
    }
}
