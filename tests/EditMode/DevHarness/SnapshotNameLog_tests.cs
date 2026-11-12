using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.DevHarness.Session;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.DevHarness
{
    /// <summary>EditMode tests for <see cref="SnapshotNameLog"/> (Authentication Story 017: harness logs player names).</summary>
    [TestFixture]
    internal sealed class SnapshotNameLog_Tests
    {
        private static EntityState Player(uint id, string name)
        {
            return EntityState.CreatePlayer(new EntityID(id), 100, 0, 200, 0, 0, 0, 32767, 50, 100, false, false, 0, 0, name, 3, 10, 20, 0, false, 0u);
        }

        private static EntityState Mob(uint id)
        {
            return EntityState.CreateMob(new EntityID(id), 5, 0, 6, 0, 0, 0, 32767, 30, 30, false, 0, 0, 1);
        }

        private static byte[] Pack(params EntityState[] entries)
        {
            var buffer = new byte[entries.Length * EntityState.PlayerMaxBodySize];
            int offset = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                offset += EntityStateCodec.WriteBody(buffer.AsSpan(offset), entries[i]);
            }

            var exact = new byte[offset];
            Array.Copy(buffer, exact, offset);
            return exact;
        }

        [Test]
        public void test_snapshot_name_log_lists_players_in_order_and_summary()
        {
            byte[] payload = Pack(Player(1u, "Brannoc"), Player(2u, "Ysolde"), Mob(3u));
            var output = new List<string>();

            SnapshotNameLog.FormatPlayerNames(payload, output);

            CollectionAssert.AreEqual(
                new[] { "snapshot player name=Brannoc", "snapshot player name=Ysolde", "snapshot entities=3 players=2" },
                output);
        }

        [Test]
        public void test_snapshot_name_log_empty_payload_reports_zero_entities()
        {
            var output = new List<string>();

            SnapshotNameLog.FormatPlayerNames(ReadOnlySpan<byte>.Empty, output);

            CollectionAssert.AreEqual(new[] { "snapshot entities=0 players=0" }, output);
        }

        [Test]
        public void test_snapshot_name_log_truncated_payload_reports_decode_failure_offset()
        {
            byte[] full = Pack(Player(1u, "Brannoc"), Mob(2u));
            int firstLength = EntityState.PlayerMinBodySize + "Brannoc".Length;
            var truncated = new byte[firstLength + 5];
            Array.Copy(full, truncated, truncated.Length);
            var output = new List<string>();

            SnapshotNameLog.FormatPlayerNames(truncated, output);

            CollectionAssert.AreEqual(
                new[] { "snapshot player name=Brannoc", "snapshot decode failed at offset=" + firstLength },
                output);
        }
    }
}
