using System;
using System.Collections.Generic;
using IronGrind.Networking;
using IronGrind.PartySystem;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.PartySystem
{
    /// <summary>
    /// EditMode tests for Party System Story 006: the nine party wire codecs, golden bytes, decode safety and registry rows.
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_WireMessages_Tests
    {
        private static readonly ushort[] AllIds =
        {
            PartyStateUpdate.MessageTypeId, PartyDisbanded.MessageTypeId, PartyInviteRequest.MessageTypeId,
            PartyInviteReceived.MessageTypeId, PartyInviteResponse.MessageTypeId, PartyLeaveRequest.MessageTypeId,
            PartyDisbandRequest.MessageTypeId, PartyInviteResult.MessageTypeId, PartyXpIneligibleNotice.MessageTypeId
        };

        private static PartyStateUpdate TwoMemberUpdate()
        {
            return new PartyStateUpdate(0x01020304u, 7u, false, 2, 0, 1,
                new PartyMemberSlot(0x0A0B0C0Du, 0x101u, 12, PartyMemberStatus.Online, true),
                new PartyMemberSlot(0x20u, 0x202u, 9, PartyMemberStatus.Dead, false),
                default, default);
        }

        private static byte[] TwoMemberGolden()
        {
            var bytes = new byte[PartyStateUpdate.BodySize];
            byte[] head =
            {
                0x04, 0x03, 0x02, 0x01, 0x07, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01,
                0x0D, 0x0C, 0x0B, 0x0A, 0x01, 0x01, 0x00, 0x00, 0x0C, 0x00, 0x01,
                0x20, 0x00, 0x00, 0x00, 0x02, 0x02, 0x00, 0x00, 0x09, 0x03, 0x00
            };
            head.CopyTo(bytes, 0);
            return bytes;
        }

        // ---- AC-1 / AC-2 round trips ----

        [Test]
        public void test_wire_messages_party_state_update_round_trips()
        {
            var buffer = new byte[PartyStateUpdate.BodySize];
            PartyStateUpdate source = TwoMemberUpdate();

            Assert.IsTrue(source.Write(buffer));
            Assert.IsTrue(PartyStateUpdate.TryRead(buffer, out PartyStateUpdate read, out bool unknown));

            Assert.IsFalse(unknown);
            Assert.AreEqual(0x01020304u, read.PartyId);
            Assert.AreEqual(7u, read.SequenceId);
            Assert.IsFalse(read.IsSoloParty);
            Assert.AreEqual((byte)2, read.MemberCount);
            Assert.AreEqual((byte)0, read.LeaderIndex);
            Assert.AreEqual((byte)1, read.RrNextIndex);
            Assert.AreEqual(0x0A0B0C0Du, read.GetMember(0).CharacterId);
            Assert.AreEqual(0x101u, read.GetMember(0).EntityId);
            Assert.AreEqual((byte)12, read.GetMember(0).Level);
            Assert.AreEqual(PartyMemberStatus.Online, read.GetMember(0).Status);
            Assert.IsTrue(read.GetMember(0).XpEligible);
            Assert.AreEqual(PartyMemberStatus.Dead, read.GetMember(1).Status);
            Assert.IsFalse(read.GetMember(1).XpEligible);
            Assert.AreEqual(0u, read.GetMember(3).CharacterId);
            Assert.Throws<ArgumentOutOfRangeException>(() => read.GetMember(4));
        }

        [Test]
        public void test_wire_messages_party_state_update_golden_bytes_match()
        {
            var buffer = new byte[PartyStateUpdate.BodySize];

            Assert.IsTrue(TwoMemberUpdate().Write(buffer));

            CollectionAssert.AreEqual(TwoMemberGolden(), buffer);
            Assert.AreEqual(56, PartyStateUpdate.BodySize);
        }

        [Test]
        public void test_wire_messages_party_disbanded_round_trips_with_golden_bytes()
        {
            var buffer = new byte[PartyDisbanded.BodySize];

            Assert.IsTrue(new PartyDisbanded(0x0A0B0C0Du).Write(buffer));
            CollectionAssert.AreEqual(new byte[] { 0x0D, 0x0C, 0x0B, 0x0A }, buffer);
            Assert.IsTrue(PartyDisbanded.TryRead(buffer, out PartyDisbanded read));
            Assert.AreEqual(0x0A0B0C0Du, read.PartyId);
        }

        [Test]
        public void test_wire_messages_party_invite_request_round_trips()
        {
            var buffer = new byte[PartyInviteRequest.BodySize];

            Assert.IsTrue(new PartyInviteRequest(0x01020304u).Write(buffer));
            Assert.IsTrue(PartyInviteRequest.TryRead(buffer, out PartyInviteRequest read));
            Assert.AreEqual(0x01020304u, read.TargetCharacterId);
            Assert.AreEqual(4, PartyInviteRequest.BodySize);
        }

        [Test]
        public void test_wire_messages_party_invite_response_round_trips_with_golden_bytes()
        {
            var buffer = new byte[PartyInviteResponse.BodySize];

            Assert.IsTrue(new PartyInviteResponse(0x01020304u, true).Write(buffer));
            CollectionAssert.AreEqual(new byte[] { 0x04, 0x03, 0x02, 0x01, 0x01 }, buffer);
            Assert.IsTrue(PartyInviteResponse.TryRead(buffer, out PartyInviteResponse read));
            Assert.AreEqual(0x01020304u, read.InviterCharacterId);
            Assert.IsTrue(read.Accepted);

            Assert.IsTrue(new PartyInviteResponse(5u, false).Write(buffer));
            Assert.IsTrue(PartyInviteResponse.TryRead(buffer, out read));
            Assert.IsFalse(read.Accepted);
        }

        [Test]
        public void test_wire_messages_party_invite_result_round_trips_with_golden_bytes()
        {
            var buffer = new byte[PartyInviteResult.BodySize];

            Assert.IsTrue(new PartyInviteResult(PartyInviteResultCode.LevelRange, 0x01020304u).Write(buffer));
            CollectionAssert.AreEqual(new byte[] { 0x03, 0x04, 0x03, 0x02, 0x01 }, buffer);
            Assert.IsTrue(PartyInviteResult.TryRead(buffer, out PartyInviteResult read));
            Assert.AreEqual(PartyInviteResultCode.LevelRange, read.ResultCode);
            Assert.AreEqual(0x01020304u, read.TargetCharacterId);
        }

        [Test]
        public void test_wire_messages_party_invite_result_code_values_are_fixed()
        {
            Assert.AreEqual((byte)0, (byte)PartyInviteResultCode.Sent);
            Assert.AreEqual((byte)1, (byte)PartyInviteResultCode.Queued);
            Assert.AreEqual((byte)2, (byte)PartyInviteResultCode.PartyFull);
            Assert.AreEqual((byte)3, (byte)PartyInviteResultCode.LevelRange);
            Assert.AreEqual((byte)4, (byte)PartyInviteResultCode.AlreadyInParty);
            Assert.AreEqual((byte)5, (byte)PartyInviteResultCode.TargetNotFound);
            Assert.AreEqual((byte)6, (byte)PartyInviteResultCode.NotLeader);
            Assert.AreEqual((byte)7, (byte)PartyInviteResultCode.Expired);
            Assert.AreEqual((byte)8, (byte)PartyInviteResultCode.Declined);
        }

        [Test]
        public void test_wire_messages_party_xp_ineligible_notice_round_trips_with_golden_bytes()
        {
            var buffer = new byte[PartyXpIneligibleNotice.BodySize];

            Assert.IsTrue(new PartyXpIneligibleNotice(0x0A0B0C0Du).Write(buffer));
            CollectionAssert.AreEqual(new byte[] { 0x0D, 0x0C, 0x0B, 0x0A }, buffer);
            Assert.IsTrue(PartyXpIneligibleNotice.TryRead(buffer, out PartyXpIneligibleNotice read));
            Assert.AreEqual(0x0A0B0C0Du, read.PartyId);
        }

        [Test]
        public void test_wire_messages_empty_body_codecs_read_an_empty_span_and_write_nothing()
        {
            Assert.AreEqual(0, PartyLeaveRequest.BodySize);
            Assert.AreEqual(0, PartyDisbandRequest.BodySize);
            Assert.IsTrue(PartyLeaveRequest.TryRead(ReadOnlySpan<byte>.Empty, out _));
            Assert.IsTrue(PartyDisbandRequest.TryRead(ReadOnlySpan<byte>.Empty, out _));
            Assert.IsTrue(new PartyLeaveRequest().Write(Span<byte>.Empty));
            Assert.IsTrue(new PartyDisbandRequest().Write(Span<byte>.Empty));
        }

        // ---- PartyInviteReceived golden bytes ----

        [Test]
        public void test_wire_messages_party_invite_received_empty_name_is_15_bytes_golden()
        {
            var buffer = new byte[PartyInviteReceived.MaxBodySize];
            var message = new PartyInviteReceived(0x11223344u, 5u, string.Empty, 3, 1000u);

            Assert.IsTrue(message.Write(buffer, out int written));

            Assert.AreEqual(15, written);
            Assert.AreEqual(15, message.GetBodySize());
            CollectionAssert.AreEqual(
                new byte[] { 0x44, 0x33, 0x22, 0x11, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0xE8, 0x03, 0x00, 0x00 },
                buffer.AsSpan(0, written).ToArray());
            Assert.IsTrue(PartyInviteReceived.TryRead(buffer.AsSpan(0, written), out PartyInviteReceived read));
            Assert.AreEqual(string.Empty, read.InviterName);
            Assert.AreEqual((byte)3, read.InviterLevel);
            Assert.AreEqual(1000u, read.InviteExpiryTick);
        }

        [Test]
        public void test_wire_messages_party_invite_received_24_byte_name_is_39_bytes_golden()
        {
            var buffer = new byte[PartyInviteReceived.MaxBodySize];
            string name = new string('A', 24);
            var message = new PartyInviteReceived(1u, 2u, name, 50, 9u);

            Assert.IsTrue(message.Write(buffer, out int written));

            Assert.AreEqual(39, written);
            byte[] golden =
            {
                0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x18, 0x00,
                0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                0x32, 0x09, 0x00, 0x00, 0x00
            };
            CollectionAssert.AreEqual(golden, buffer);
            Assert.IsTrue(PartyInviteReceived.TryRead(buffer, out PartyInviteReceived read));
            Assert.AreEqual(name, read.InviterName);
            Assert.AreEqual(9u, read.InviteExpiryTick);
        }

        [Test]
        public void test_wire_messages_party_invite_received_multi_byte_name_golden_and_round_trip()
        {
            var buffer = new byte[PartyInviteReceived.MaxBodySize];
            var message = new PartyInviteReceived(1u, 2u, "Zoë", 8, 77u);

            Assert.IsTrue(message.Write(buffer, out int written));

            Assert.AreEqual(19, written);
            CollectionAssert.AreEqual(new byte[] { 0x04, 0x00, 0x5A, 0x6F, 0xC3, 0xAB }, buffer.AsSpan(8, 6).ToArray());
            CollectionAssert.AreEqual(new byte[] { 0x08, 0x4D, 0x00, 0x00, 0x00 }, buffer.AsSpan(14, 5).ToArray());
            Assert.IsTrue(PartyInviteReceived.TryRead(buffer.AsSpan(0, written), out PartyInviteReceived read));
            Assert.AreEqual("Zoë", read.InviterName);
        }

        [Test]
        public void test_wire_messages_party_invite_received_long_name_is_cut_at_a_character_boundary()
        {
            var buffer = new byte[PartyInviteReceived.MaxBodySize];
            string name = new string('日', 9);
            var message = new PartyInviteReceived(1u, 2u, name, 8, 77u);

            Assert.IsTrue(message.Write(buffer, out int written));

            Assert.AreEqual(39, written);
            Assert.IsTrue(PartyInviteReceived.TryRead(buffer.AsSpan(0, written), out PartyInviteReceived read));
            Assert.AreEqual(new string('日', 8), read.InviterName);
        }

        [Test]
        public void test_wire_messages_party_invite_received_cut_mid_character_drops_the_whole_character()
        {
            var buffer = new byte[PartyInviteReceived.MaxBodySize];
            string name = new string('A', 23) + "ë";
            var message = new PartyInviteReceived(1u, 2u, name, 8, 77u);

            Assert.AreEqual(38, message.GetBodySize());
            Assert.IsTrue(message.Write(buffer, out int written));

            Assert.AreEqual(38, written);
            Assert.AreEqual((byte)23, buffer[8]);
            Assert.IsTrue(PartyInviteReceived.TryRead(buffer.AsSpan(0, written), out PartyInviteReceived read));
            Assert.AreEqual(new string('A', 23), read.InviterName);
        }

        [Test]
        public void test_wire_messages_party_invite_received_null_name_encodes_as_empty_15_bytes()
        {
            var buffer = new byte[PartyInviteReceived.MaxBodySize];
            var message = new PartyInviteReceived(1u, 2u, null, 8, 77u);

            Assert.AreEqual(15, message.GetBodySize());
            Assert.IsTrue(message.Write(buffer, out int written));

            Assert.AreEqual(15, written);
            Assert.IsTrue(PartyInviteReceived.TryRead(buffer.AsSpan(0, written), out PartyInviteReceived read));
            Assert.AreEqual(string.Empty, read.InviterName);
        }

        [Test]
        public void test_wire_messages_party_invite_received_empty_name_with_short_tail_returns_false()
        {
            var buffer = new byte[PartyInviteReceived.MaxBodySize];
            Assert.IsTrue(new PartyInviteReceived(1u, 2u, string.Empty, 8, 77u).Write(buffer, out int written));
            Assert.AreEqual(15, written);

            // 14 bytes: below the minimum body, and an empty name with only 4 trailing bytes (level + 3 tick bytes).
            Assert.IsFalse(PartyInviteReceived.TryRead(buffer.AsSpan(0, 14), out _));
            Assert.IsTrue(PartyInviteReceived.TryRead(buffer.AsSpan(0, 15), out _));
        }

        [Test]
        public void test_wire_messages_party_invite_request_golden_bytes()
        {
            var buffer = new byte[PartyInviteRequest.BodySize];

            Assert.IsTrue(new PartyInviteRequest(0x01020304u).Write(buffer));

            CollectionAssert.AreEqual(new byte[] { 0x04, 0x03, 0x02, 0x01 }, buffer);
        }

        [Test]
        public void test_wire_messages_party_state_update_member_count_0_and_4_are_accepted()
        {
            byte[] bytes = TwoMemberGolden();

            bytes[9] = 0;
            Assert.IsTrue(PartyStateUpdate.TryRead(bytes, out PartyStateUpdate zero, out _));
            Assert.AreEqual((byte)0, zero.MemberCount);

            bytes[9] = 4;
            Assert.IsTrue(PartyStateUpdate.TryRead(bytes, out PartyStateUpdate four, out _));
            Assert.AreEqual((byte)4, four.MemberCount);
        }

        [Test]
        public void test_wire_messages_party_state_update_non_zero_flag_bytes_decode_as_true()
        {
            byte[] bytes = TwoMemberGolden();
            bytes[8] = 2;
            bytes[PartyStateUpdate.HeaderSize + 10] = 2;

            Assert.IsTrue(PartyStateUpdate.TryRead(bytes, out PartyStateUpdate read, out _));

            Assert.IsTrue(read.IsSoloParty);
            Assert.IsTrue(read.GetMember(0).XpEligible);
        }

        [Test]
        public void test_wire_messages_party_state_update_indices_beyond_member_count_are_not_validated()
        {
            byte[] bytes = TwoMemberGolden();
            bytes[10] = 200;
            bytes[11] = 255;

            Assert.IsTrue(PartyStateUpdate.TryRead(bytes, out PartyStateUpdate read, out _));

            Assert.AreEqual((byte)200, read.LeaderIndex);
            Assert.AreEqual((byte)255, read.RrNextIndex);
        }

        [Test]
        public void test_wire_messages_party_invite_result_code_255_returns_false()
        {
            var bytes = new byte[PartyInviteResult.BodySize];
            bytes[0] = 255;

            Assert.IsFalse(PartyInviteResult.TryRead(bytes, out _));
        }

        [Test]
        public void test_wire_messages_party_member_slot_write_to_10_bytes_returns_false()
        {
            var slot = new PartyMemberSlot(1u, 2u, 3, PartyMemberStatus.Dead, true);
            var small = new byte[10];

            Assert.IsFalse(slot.Write(small));
            CollectionAssert.AreEqual(new byte[10], small);
        }

        // ---- AC-3 registry ----

        [TestCase((ushort)0xE170, MessageDirection.ServerToParty)]
        [TestCase((ushort)0xE171, MessageDirection.ServerToParty)]
        [TestCase((ushort)0xE172, MessageDirection.ClientToServer)]
        [TestCase((ushort)0xE173, MessageDirection.ServerToOwningClient)]
        [TestCase((ushort)0xE174, MessageDirection.ClientToServer)]
        [TestCase((ushort)0xE175, MessageDirection.ClientToServer)]
        [TestCase((ushort)0xE176, MessageDirection.ClientToServer)]
        [TestCase((ushort)0xE177, MessageDirection.ServerToOwningClient)]
        [TestCase((ushort)0xE178, MessageDirection.ServerToOwningClient)]
        public void test_wire_messages_registry_lookup_returns_direction_and_priority_path(ushort id, MessageDirection direction)
        {
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(id, out MessageRoutingEntry entry));

            Assert.AreEqual(direction, entry.Direction);
            Assert.AreEqual(MessageDeliveryContext.PriorityPath, entry.DeliveryContext);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, entry.Channel);
            Assert.IsFalse(entry.IsMcr3Exception);
        }

        [Test]
        public void test_wire_messages_party_ids_are_distinct_in_range_and_unique_in_the_registry()
        {
            var seen = new HashSet<ushort>();
            foreach (ushort id in AllIds)
            {
                Assert.IsTrue(seen.Add(id), "duplicate id " + id.ToString("X4"));
                Assert.GreaterOrEqual(id, (ushort)0xE170);
                Assert.LessOrEqual(id, (ushort)0xE17F);
            }

            Assert.AreEqual(9, seen.Count);
            var counts = new Dictionary<ushort, int>();
            foreach (MessageRoutingEntry row in MessageRoutingRegistry.AllEntries)
            {
                counts.TryGetValue(row.MessageTypeId, out int n);
                counts[row.MessageTypeId] = n + 1;
            }

            foreach (ushort id in AllIds)
            {
                Assert.AreEqual(1, counts[id]);
            }
        }

        [Test]
        public void test_wire_messages_client_request_body_sizes_match_adr_014_table()
        {
            Assert.AreEqual(4, PartyInviteRequest.BodySize);
            Assert.AreEqual(5, PartyInviteResponse.BodySize);
            Assert.AreEqual(0, PartyLeaveRequest.BodySize);
            Assert.AreEqual(0, PartyDisbandRequest.BodySize);
        }

        // ---- AC-4 decode safety ----

        [TestCase("PartyStateUpdate")]
        [TestCase("PartyDisbanded")]
        [TestCase("PartyInviteRequest")]
        [TestCase("PartyInviteResponse")]
        [TestCase("PartyInviteResult")]
        [TestCase("PartyXpIneligibleNotice")]
        [TestCase("PartyMemberSlot")]
        public void test_wire_messages_fixed_body_one_byte_short_returns_false(string codec)
        {
            bool result;
            switch (codec)
            {
                case "PartyStateUpdate":
                    result = PartyStateUpdate.TryRead(new byte[PartyStateUpdate.BodySize - 1], out _, out _);
                    break;
                case "PartyDisbanded":
                    result = PartyDisbanded.TryRead(new byte[PartyDisbanded.BodySize - 1], out _);
                    break;
                case "PartyInviteRequest":
                    result = PartyInviteRequest.TryRead(new byte[PartyInviteRequest.BodySize - 1], out _);
                    break;
                case "PartyInviteResponse":
                    result = PartyInviteResponse.TryRead(new byte[PartyInviteResponse.BodySize - 1], out _);
                    break;
                case "PartyInviteResult":
                    result = PartyInviteResult.TryRead(new byte[PartyInviteResult.BodySize - 1], out _);
                    break;
                case "PartyXpIneligibleNotice":
                    result = PartyXpIneligibleNotice.TryRead(new byte[PartyXpIneligibleNotice.BodySize - 1], out _);
                    break;
                default:
                    result = PartyMemberSlot.TryRead(new byte[PartyMemberSlot.WireSize - 1], out _, out _);
                    break;
            }

            Assert.IsFalse(result);
        }

        [Test]
        public void test_wire_messages_party_invite_received_truncated_cases_return_false()
        {
            var buffer = new byte[PartyInviteReceived.MaxBodySize];
            var message = new PartyInviteReceived(1u, 2u, "Zoë", 8, 77u);
            Assert.IsTrue(message.Write(buffer, out int written));

            // Cut inside the name, and short of the trailing level and tick.
            Assert.IsFalse(PartyInviteReceived.TryRead(buffer.AsSpan(0, 11), out _));
            Assert.IsFalse(PartyInviteReceived.TryRead(buffer.AsSpan(0, written - 1), out _));
            Assert.IsFalse(PartyInviteReceived.TryRead(buffer.AsSpan(0, 14), out _));
            Assert.IsFalse(PartyInviteReceived.TryRead(ReadOnlySpan<byte>.Empty, out _));
        }

        [Test]
        public void test_wire_messages_party_invite_received_name_length_above_24_returns_false()
        {
            var buffer = new byte[10 + 25 + 5];
            buffer[8] = 25;

            Assert.IsFalse(PartyInviteReceived.TryRead(buffer, out _));
        }

        [Test]
        public void test_wire_messages_party_state_update_member_count_above_4_returns_false()
        {
            byte[] bytes = TwoMemberGolden();
            bytes[9] = 5;

            Assert.IsFalse(PartyStateUpdate.TryRead(bytes, out _, out _));
        }

        [Test]
        public void test_wire_messages_party_invite_result_code_above_8_returns_false()
        {
            var bytes = new byte[PartyInviteResult.BodySize];
            bytes[0] = 9;

            Assert.IsFalse(PartyInviteResult.TryRead(bytes, out _));
            bytes[0] = 8;
            Assert.IsTrue(PartyInviteResult.TryRead(bytes, out _));
        }

        [Test]
        public void test_wire_messages_party_invite_response_accepted_byte_other_than_0_or_1_returns_false()
        {
            var bytes = new byte[PartyInviteResponse.BodySize];
            bytes[4] = 2;

            Assert.IsFalse(PartyInviteResponse.TryRead(bytes, out _));
        }

        [Test]
        public void test_wire_messages_party_state_update_unknown_status_reads_online_and_sets_flag()
        {
            byte[] bytes = TwoMemberGolden();
            bytes[PartyStateUpdate.HeaderSize + PartyMemberSlot.WireSize + 9] = 7;

            Assert.IsTrue(PartyStateUpdate.TryRead(bytes, out PartyStateUpdate read, out bool unknown));

            Assert.IsTrue(unknown);
            Assert.AreEqual(PartyMemberStatus.Online, read.GetMember(1).Status);
            Assert.AreEqual(0x20u, read.GetMember(1).CharacterId);
        }

        [Test]
        public void test_wire_messages_party_state_update_all_valid_statuses_clear_the_flag()
        {
            byte[] bytes = TwoMemberGolden();
            for (byte status = 0; status <= 3; status++)
            {
                bytes[PartyStateUpdate.HeaderSize + 9] = status;

                Assert.IsTrue(PartyStateUpdate.TryRead(bytes, out PartyStateUpdate read, out bool unknown));
                Assert.IsFalse(unknown);
                Assert.AreEqual((PartyMemberStatus)status, read.GetMember(0).Status);
            }
        }

        [Test]
        public void test_wire_messages_short_destination_write_returns_false_and_writes_nothing()
        {
            var small = new byte[3];
            var invite = new PartyInviteReceived(1u, 2u, "Zoë", 8, 77u);

            Assert.IsFalse(TwoMemberUpdate().Write(new byte[PartyStateUpdate.BodySize - 1]));
            Assert.IsFalse(new PartyDisbanded(1u).Write(small));
            Assert.IsFalse(new PartyInviteRequest(1u).Write(small));
            Assert.IsFalse(new PartyInviteResponse(1u, true).Write(new byte[4]));
            Assert.IsFalse(new PartyInviteResult(PartyInviteResultCode.Sent, 1u).Write(new byte[4]));
            Assert.IsFalse(new PartyXpIneligibleNotice(1u).Write(small));
            Assert.IsFalse(invite.Write(new byte[invite.GetBodySize() - 1], out int written));
            Assert.AreEqual(0, written);
            CollectionAssert.AreEqual(new byte[3], small);
        }
    }
}
