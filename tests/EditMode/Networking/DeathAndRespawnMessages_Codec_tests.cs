using System;
using System.Text.RegularExpressions;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Death and Respawn Story 005's wire types: <see cref="EntityDied"/>,
    /// <see cref="DeathStateEntered"/>, <see cref="EntityRespawned"/> and <see cref="RespawnConfirmed"/> (codecs and
    /// routing rows; networking-wire-protocol.md Death and Respawn).
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawnMessages_Codec_Tests
    {
        private const uint ENTITY = 0x01020304u;
        private static readonly Vector3 Position = new Vector3(12.75f, 0f, -85.23f);

        // ---- AC-1a: EntityDied ----

        [Test]
        public void test_wire_messages_entity_died_round_trip_returns_input_and_body_size()
        {
            var body = new byte[EntityDied.BodySize];

            int written = EntityDiedCodec.WriteBody(body, ENTITY);
            bool read = EntityDiedCodec.TryReadBody(body, out uint decoded);

            Assert.AreEqual(4, written);
            Assert.IsTrue(read);
            Assert.AreEqual(ENTITY, decoded);
        }

        [Test]
        public void test_wire_messages_entity_died_bytes_are_little_endian()
        {
            var body = new byte[EntityDied.BodySize];

            EntityDiedCodec.WriteBody(body, ENTITY);

            CollectionAssert.AreEqual(new byte[] { 0x04, 0x03, 0x02, 0x01 }, body);
        }

        [TestCase(3)]
        [TestCase(5)]
        [TestCase(0)]
        public void test_wire_messages_entity_died_wrong_length_is_rejected(int length)
        {
            bool read = EntityDiedCodec.TryReadBody(new byte[length], out uint decoded);

            Assert.IsFalse(read);
            Assert.AreEqual(0u, decoded);
        }

        // ---- AC-1a: DeathStateEntered ----

        [Test]
        public void test_wire_messages_death_state_entered_round_trip_returns_input_and_body_size()
        {
            var body = new byte[DeathStateEntered.BodySize];

            int written = DeathStateEnteredCodec.WriteBody(body, ENTITY, 7);
            bool read = DeathStateEnteredCodec.TryReadBody(body, out uint decoded, out byte seconds);

            Assert.AreEqual(5, written);
            Assert.IsTrue(read);
            Assert.AreEqual(ENTITY, decoded);
            Assert.AreEqual((byte)7, seconds);
        }

        [Test]
        public void test_wire_messages_death_state_entered_bytes_put_timer_at_offset_four()
        {
            var body = new byte[DeathStateEntered.BodySize];

            DeathStateEnteredCodec.WriteBody(body, ENTITY, 7);

            CollectionAssert.AreEqual(new byte[] { 0x04, 0x03, 0x02, 0x01, 0x07 }, body);
        }

        [TestCase(4)]
        [TestCase(6)]
        [TestCase(0)]
        public void test_wire_messages_death_state_entered_wrong_length_is_rejected(int length)
        {
            bool read = DeathStateEnteredCodec.TryReadBody(new byte[length], out uint decoded, out byte seconds);

            Assert.IsFalse(read);
            Assert.AreEqual(0u, decoded);
            Assert.AreEqual((byte)0, seconds);
        }

        // ---- AC-1a: EntityRespawned ----

        [Test]
        public void test_wire_messages_entity_respawned_round_trip_returns_input_and_body_size()
        {
            var body = new byte[EntityRespawned.BodySize];

            int written = EntityRespawnedCodec.WriteBody(body, ENTITY, Position);
            bool read = EntityRespawnedCodec.TryReadBody(body, out uint decoded, out Vector3 position);

            Assert.AreEqual(10, written);
            Assert.IsTrue(read);
            Assert.AreEqual(ENTITY, decoded);
            Assert.AreEqual(Position.x, position.x, 0.005f);
            Assert.AreEqual(Position.y, position.y, 0.005f);
            Assert.AreEqual(Position.z, position.z, 0.005f);
        }

        [Test]
        public void test_wire_messages_entity_respawned_bytes_hold_position_at_offsets_four_six_eight()
        {
            var body = new byte[EntityRespawned.BodySize];

            EntityRespawnedCodec.WriteBody(body, ENTITY, Position);

            // posX 1275 = 0x04FB, posY 0, posZ -8523 = 0xDEB5 (little-endian).
            CollectionAssert.AreEqual(new byte[] { 0x04, 0x03, 0x02, 0x01, 0xFB, 0x04, 0x00, 0x00, 0xB5, 0xDE }, body);
            Assert.AreEqual(1275, (short)(body[4] | (body[5] << 8)));
            Assert.AreEqual(0, (short)(body[6] | (body[7] << 8)));
            Assert.AreEqual(-8523, (short)(body[8] | (body[9] << 8)));
        }

        [TestCase(9)]
        [TestCase(11)]
        [TestCase(0)]
        public void test_wire_messages_entity_respawned_wrong_length_is_rejected(int length)
        {
            bool read = EntityRespawnedCodec.TryReadBody(new byte[length], out uint decoded, out Vector3 position);

            Assert.IsFalse(read);
            Assert.AreEqual(0u, decoded);
            Assert.AreEqual(Vector3.zero, position);
        }

        // ---- AC-9 ----

        [Test]
        public void test_wire_messages_entity_respawned_position_over_range_is_clamped_with_one_warning()
        {
            var body = new byte[EntityRespawned.BodySize];

            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireFixedPointCodec\] EncodePosition"));
            EntityRespawnedCodec.WriteBody(body, ENTITY, new Vector3(400f, 0f, 0f));

            Assert.AreEqual(32767, (short)(body[4] | (body[5] << 8)));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-1a: RespawnConfirmed ----

        [Test]
        public void test_wire_messages_respawn_confirmed_round_trip_returns_input_and_body_size()
        {
            var body = new byte[RespawnConfirmed.BodySize];

            int written = RespawnConfirmedCodec.WriteBody(body, ENTITY);
            bool read = RespawnConfirmedCodec.TryReadBody(body, out uint decoded);

            Assert.AreEqual(4, written);
            Assert.IsTrue(read);
            Assert.AreEqual(ENTITY, decoded);
            CollectionAssert.AreEqual(new byte[] { 0x04, 0x03, 0x02, 0x01 }, body);
        }

        [TestCase(3)]
        [TestCase(5)]
        [TestCase(0)]
        public void test_wire_messages_respawn_confirmed_wrong_length_is_rejected(int length)
        {
            bool read = RespawnConfirmedCodec.TryReadBody(new byte[length], out uint decoded);

            Assert.IsFalse(read);
            Assert.AreEqual(0u, decoded);
        }

        [Test]
        public void test_wire_messages_write_body_into_short_buffer_throws_out_of_range_for_all_four()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EntityDiedCodec.WriteBody(new byte[EntityDied.BodySize - 1], ENTITY));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeathStateEnteredCodec.WriteBody(new byte[DeathStateEntered.BodySize - 1], ENTITY, 7));
            Assert.Throws<ArgumentOutOfRangeException>(() => EntityRespawnedCodec.WriteBody(new byte[EntityRespawned.BodySize - 1], ENTITY, Position));
            Assert.Throws<ArgumentOutOfRangeException>(() => RespawnConfirmedCodec.WriteBody(new byte[RespawnConfirmed.BodySize - 1], ENTITY));
        }

        // ---- AC-1b ----

        [Test]
        public void test_wire_messages_ids_are_e160_to_e163_and_distinct()
        {
            Assert.AreEqual(0xE160, EntityDied.MessageTypeId);
            Assert.AreEqual(0xE161, DeathStateEntered.MessageTypeId);
            Assert.AreEqual(0xE162, EntityRespawned.MessageTypeId);
            Assert.AreEqual(0xE163, RespawnConfirmed.MessageTypeId);
        }

        [TestCase((ushort)0xE160, nameof(EntityDied), MessageDirection.ServerToAllZoneClients)]
        [TestCase((ushort)0xE161, nameof(DeathStateEntered), MessageDirection.ServerToOwningClient)]
        [TestCase((ushort)0xE162, nameof(EntityRespawned), MessageDirection.ServerToAllZoneClients)]
        [TestCase((ushort)0xE163, nameof(RespawnConfirmed), MessageDirection.ServerToOwningClient)]
        public void test_wire_messages_routing_is_priority_path_with_expected_direction(
            ushort id, string name, MessageDirection direction)
        {
            bool has = MessageRoutingRegistry.TryGetEntry(id, out MessageRoutingEntry entry);

            Assert.IsTrue(has);
            Assert.AreEqual(name, entry.MessageName);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, entry.Channel);
            Assert.AreEqual(MessageDeliveryContext.PriorityPath, entry.DeliveryContext);
            Assert.AreEqual(direction, entry.Direction);
        }
    }
}
