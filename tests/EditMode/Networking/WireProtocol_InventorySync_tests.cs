using System;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.InventorySystem;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for the owner inventory sync codecs (Inventory Story 012, TD-046):
    /// AC-NC-41 (slot-update half), AC-NC-40 (<c>InventoryFullSync</c> size), AC-NC-42 (<c>count</c> half).
    /// </summary>
    [TestFixture]
    internal sealed class WireProtocol_InventorySync_Tests
    {
        private const int BufferSize = 256;
        private const uint SwordItemRaw = 1001u;
        private const uint PotionItemRaw = 2002u;
        private const uint ScrollItemRaw = uint.MaxValue;
        private const int PotionStackQuantity = 37;
        private const byte SwordLevel = 7;
        private const byte WrongFullSyncCount = 19;
        private const string WRONG_COUNT_WARNING = "[InventoryFullSyncCodec] TryReadBody: received count byte 19";

        private static InventorySlotEntry[] BuildEntries(int count)
        {
            var entries = new InventorySlotEntry[count];
            for (int i = 0; i < count; i++)
            {
                entries[i] = new InventorySlotEntry((byte)i, new ItemID(SwordItemRaw + (uint)i), i + 1, (byte)(i % 10));
            }

            return entries;
        }

        private static void AssertEntryEqual(InventorySlotEntry expected, InventorySlotEntry actual, int index)
        {
            Assert.AreEqual(expected.SlotIndex, actual.SlotIndex, $"SlotIndex[{index}]");
            Assert.AreEqual(expected.ItemId, actual.ItemId, $"ItemId[{index}]");
            Assert.AreEqual(expected.Quantity, actual.Quantity, $"Quantity[{index}]");
            Assert.AreEqual(expected.EnhancementLevel, actual.EnhancementLevel, $"EnhancementLevel[{index}]");
        }

        // -----------------------------------------------------------------------
        // AC-NC-41 (slot-update half): body size and round trip.
        // -----------------------------------------------------------------------

        [TestCase(1, 11)]
        [TestCase(2, 21)]
        [TestCase(InventoryConstants.INVENTORY_SLOT_COUNT, 201)]
        public void InventorySlotUpdate_WriteThenRead_BodySizeMatchesAndEveryEntryRoundTrips(int count, int expectedSize)
        {
            // Arrange
            InventorySlotEntry[] entries = BuildEntries(count);
            byte[] buffer = new byte[BufferSize];
            var decoded = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];

            // Act
            int written = InventorySlotUpdateCodec.WriteBody(buffer, entries);
            bool ok = InventorySlotUpdateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), decoded, out int readCount);

            // Assert
            Assert.AreEqual(expectedSize, written);
            Assert.AreEqual(expectedSize, InventorySlotUpdate.GetBodySize(count));
            Assert.IsTrue(ok);
            Assert.AreEqual(count, readCount);
            Assert.AreEqual((byte)count, buffer[0]);
            for (int i = 0; i < count; i++)
            {
                AssertEntryEqual(entries[i], decoded[i], i);
            }
        }

        [Test]
        public void InventorySlotUpdate_SizeConstants_MatchSchema()
        {
            Assert.AreEqual(11, InventorySlotUpdate.MinBodySize);
            Assert.AreEqual(201, InventorySlotUpdate.MaxBodySize);
            Assert.AreEqual(10, InventorySlotEntry.WireSize);
        }

        [Test]
        public void InventorySlotUpdate_EmptiedSlot_EncodesWithoutAssertionAndRoundTrips()
        {
            // Arrange
            var emptied = new InventorySlotEntry(3, ItemID.Invalid, 0, 0);
            byte[] buffer = new byte[BufferSize];
            var decoded = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
            int written = 0;

            // Act
            Assert.DoesNotThrow(() => written = InventorySlotUpdateCodec.WriteBody(buffer, new[] { emptied }));
            bool ok = InventorySlotUpdateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), decoded, out int count);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual(1, count);
            AssertEntryEqual(emptied, decoded[0], 0);
            Assert.AreEqual(ItemID.Invalid, decoded[0].ItemId);
        }

        [Test]
        public void InventorySlotUpdate_ItemIdIsLittleEndianAtOffsetTwo()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            InventorySlotUpdateCodec.WriteBody(buffer, new[] { new InventorySlotEntry(5, new ItemID(0x01020304u), 0, 0) });

            // Assert — body: count, slotIndex, then itemId bytes.
            Assert.AreEqual(5, buffer[1]);
            Assert.AreEqual(0x04, buffer[2]);
            Assert.AreEqual(0x01, buffer[5]);
        }

        // -----------------------------------------------------------------------
        // InventorySlotUpdate decode bounds.
        // -----------------------------------------------------------------------

        [Test]
        public void InventorySlotUpdate_TryReadBody_CountZero_IsRejected()
        {
            byte[] body = new byte[InventorySlotUpdate.MaxBodySize];
            body[0] = 0;
            var decoded = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];

            Assert.IsFalse(InventorySlotUpdateCodec.TryReadBody(body, decoded, out int count));
            Assert.AreEqual(0, count);
        }

        [Test]
        public void InventorySlotUpdate_TryReadBody_CountAboveSlotCount_IsRejected()
        {
            byte[] body = new byte[InventorySlotUpdate.GetBodySize(InventoryConstants.INVENTORY_SLOT_COUNT + 1)];
            body[0] = (byte)(InventoryConstants.INVENTORY_SLOT_COUNT + 1);
            var decoded = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT + 1];

            Assert.IsFalse(InventorySlotUpdateCodec.TryReadBody(body, decoded, out int count));
            Assert.AreEqual(0, count);
        }

        [Test]
        public void InventorySlotUpdate_TryReadBody_BodyShorterThanCountClaims_IsRejected()
        {
            // Arrange — count says 3 entries (31 bytes) but only 30 bytes are present.
            byte[] body = new byte[InventorySlotUpdate.GetBodySize(3) - 1];
            body[0] = 3;
            var decoded = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];

            // Act & Assert
            Assert.IsFalse(InventorySlotUpdateCodec.TryReadBody(body, decoded, out int count));
            Assert.AreEqual(0, count);
        }

        [Test]
        public void InventorySlotUpdate_TryReadBody_EmptyBody_IsRejected()
        {
            var decoded = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];

            Assert.IsFalse(InventorySlotUpdateCodec.TryReadBody(ReadOnlySpan<byte>.Empty, decoded, out int count));
            Assert.AreEqual(0, count);
        }

        [Test]
        public void InventorySlotUpdate_WriteBody_NoEntriesOrTooMany_Throws()
        {
            byte[] buffer = new byte[BufferSize];

            Assert.Throws<ArgumentOutOfRangeException>(() => InventorySlotUpdateCodec.WriteBody(buffer, Array.Empty<InventorySlotEntry>()));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => InventorySlotUpdateCodec.WriteBody(buffer, BuildEntries(InventoryConstants.INVENTORY_SLOT_COUNT + 1)));
        }

        // -----------------------------------------------------------------------
        // AC-NC-40 / AC-NC-42: InventoryFullSync.
        // -----------------------------------------------------------------------

        private static InventorySlotEntry[] BuildMixedBag()
        {
            var bag = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < bag.Length; i++)
            {
                bag[i] = new InventorySlotEntry((byte)i, ItemID.Invalid, 0, 0);
            }

            bag[0] = new InventorySlotEntry(0, new ItemID(SwordItemRaw), 1, SwordLevel);
            bag[1] = new InventorySlotEntry(1, new ItemID(PotionItemRaw), PotionStackQuantity, 0);
            bag[4] = new InventorySlotEntry(4, new ItemID(ScrollItemRaw), int.MaxValue, 0);
            bag[InventoryConstants.INVENTORY_SLOT_COUNT - 1] = new InventorySlotEntry(
                (byte)(InventoryConstants.INVENTORY_SLOT_COUNT - 1), new ItemID(SwordItemRaw), 1, 1);
            return bag;
        }

        [Test]
        public void InventoryFullSync_WriteThenRead_Body201BytesAscendingAndRoundTrips()
        {
            // Arrange
            InventorySlotEntry[] bag = BuildMixedBag();
            byte[] buffer = new byte[BufferSize];
            var decoded = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];

            // Act
            int written = InventoryFullSyncCodec.WriteBody(buffer, bag);
            bool ok = InventoryFullSyncCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), decoded);

            // Assert
            Assert.AreEqual(201, written);
            Assert.AreEqual(201, InventoryFullSync.BodySize);
            Assert.AreEqual((byte)InventoryConstants.INVENTORY_SLOT_COUNT, buffer[0]);
            Assert.IsTrue(ok);
            for (int i = 0; i < bag.Length; i++)
            {
                AssertEntryEqual(bag[i], decoded[i], i);
                Assert.AreEqual(i, buffer[1 + i * InventorySlotEntry.WireSize], $"wire slotIndex[{i}] ascending");
            }
        }

        [Test]
        public void InventoryFullSync_WriteBody_WrongEntryCount_Throws()
        {
            byte[] buffer = new byte[BufferSize];

            Assert.Throws<ArgumentException>(() => InventoryFullSyncCodec.WriteBody(buffer, BuildEntries(InventoryConstants.INVENTORY_SLOT_COUNT - 1)));
        }

        [Test]
        public void InventoryFullSync_TryReadBody_CountByteNineteen_DiscardsAndLogsOneAnomaly()
        {
            // Arrange — a well-formed 201-byte body whose count byte is changed to 19.
            byte[] buffer = new byte[BufferSize];
            InventoryFullSyncCodec.WriteBody(buffer, BuildMixedBag());
            buffer[0] = WrongFullSyncCount;
            var decoded = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
            LogAssert.Expect(LogType.Warning, new Regex(@"\[InventoryFullSyncCodec\].*count byte 19"));
            using var warnings = new WarningCounter(WRONG_COUNT_WARNING);

            // Act
            bool ok = InventoryFullSyncCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, InventoryFullSync.BodySize), decoded);

            // Assert — LogAssert.Expect consumes the warning; Unity does not fail a test on an unexpected warning,
            // so the exact count comes from Application.logMessageReceived.
            Assert.IsFalse(ok);
            Assert.AreEqual(1, warnings.Count, "Exactly one anomaly warning is logged.");
        }

        // Counts the warnings whose text contains a fragment, from subscription until disposal.
        private sealed class WarningCounter : IDisposable
        {
            private readonly string _fragment;

            public WarningCounter(string fragment)
            {
                _fragment = fragment;
                Application.logMessageReceived += OnLog;
            }

            public int Count { get; private set; }

            public void Dispose()
            {
                Application.logMessageReceived -= OnLog;
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains(_fragment))
                    Count++;
            }
        }

        [Test]
        public void InventoryFullSync_TryReadBody_BodyTooShort_IsRejected()
        {
            byte[] buffer = new byte[BufferSize];
            InventoryFullSyncCodec.WriteBody(buffer, BuildMixedBag());
            var decoded = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];

            Assert.IsFalse(InventoryFullSyncCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, InventoryFullSync.BodySize - 1), decoded));
            Assert.IsFalse(InventoryFullSyncCodec.TryReadBody(ReadOnlySpan<byte>.Empty, decoded));
        }

        // -----------------------------------------------------------------------
        // Message type ids.
        // -----------------------------------------------------------------------

        [Test]
        public void MessageTypeIds_AreDistinctAndInPriorityRange()
        {
            Assert.AreNotEqual(InventorySlotUpdate.MessageTypeId, InventoryFullSync.MessageTypeId);
            Assert.GreaterOrEqual(InventorySlotUpdate.MessageTypeId, (ushort)0xE000);
            Assert.LessOrEqual(InventorySlotUpdate.MessageTypeId, (ushort)0xEFFF);
            Assert.GreaterOrEqual(InventoryFullSync.MessageTypeId, (ushort)0xE000);
            Assert.LessOrEqual(InventoryFullSync.MessageTypeId, (ushort)0xEFFF);
        }
    }
}
