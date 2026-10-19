using System;
using System.Linq;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.InventorySystem;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 014: the hand-written JSON codec for the gear_slots and inventory_slots JSONB
    /// columns (ADR-006 Key Interfaces, Amendment 1). Pure string logic: no I/O.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_SlotJsonCodec_Tests
    {
        private const int GEAR_COUNT = CharacterPersistenceConstants.GEAR_SLOT_COUNT;
        private const int INV_COUNT = InventoryConstants.INVENTORY_SLOT_COUNT;

        private static string Nulls(int n)
        {
            return string.Join(",", Enumerable.Repeat("null", n));
        }

        // A gear/inventory array whose first entry is the given text and whose other entries are null.
        private static string GearWithFirst(string entry)
        {
            return "[" + entry + "," + Nulls(GEAR_COUNT - 1) + "]";
        }

        private static string InventoryWithFirst(string entry)
        {
            return "[" + entry + "," + Nulls(INV_COUNT - 1) + "]";
        }

        private static GearSlotRecord[] NewGear()
        {
            return new GearSlotRecord[GEAR_COUNT];
        }

        private static void DecodeInventory(string json, out ItemID[] items, out int[] counts, out byte[] levels)
        {
            items = new ItemID[INV_COUNT];
            counts = new int[INV_COUNT];
            levels = new byte[INV_COUNT];
            CharacterSlotJsonCodec.DecodeInventorySlots(json, items, counts, levels);
        }

        // ---------------------------------------------------------------- gear round trip

        [Test]
        public void test_gear_fullArrayWithExtremes_roundTrips()
        {
            GearSlotRecord[] source = new GearSlotRecord[GEAR_COUNT];
            for (int i = 0; i < GEAR_COUNT; i++)
            {
                source[i] = new GearSlotRecord(new ItemID((uint)(i + 1)), (byte)i);
            }

            source[0] = new GearSlotRecord(new ItemID(uint.MaxValue), 255);
            GearSlotRecord[] decoded = NewGear();

            CharacterSlotJsonCodec.DecodeGearSlots(CharacterSlotJsonCodec.EncodeGearSlots(source), decoded);

            for (int i = 0; i < GEAR_COUNT; i++)
            {
                Assert.AreEqual(source[i].ItemId, decoded[i].ItemId, "item " + i);
                Assert.AreEqual(source[i].EnhancementLevel, decoded[i].EnhancementLevel, "level " + i);
            }
        }

        [Test]
        public void test_gear_allEmpty_encodesAsSevenNullsAndDecodesBack()
        {
            GearSlotRecord[] empty = Enumerable.Repeat(GearSlotRecord.Empty, GEAR_COUNT).ToArray();

            string json = CharacterSlotJsonCodec.EncodeGearSlots(empty);
            GearSlotRecord[] decoded = Enumerable.Repeat(new GearSlotRecord(new ItemID(9u), 9), GEAR_COUNT).ToArray();
            CharacterSlotJsonCodec.DecodeGearSlots(json, decoded);

            Assert.AreEqual("[" + Nulls(GEAR_COUNT) + "]", json);
            Assert.IsTrue(decoded.All(g => g.ItemId == ItemID.Invalid && g.EnhancementLevel == 0));
        }

        [Test]
        public void test_gear_oneSlot_encodesCompactWithInvariantNumbers()
        {
            GearSlotRecord[] slots = Enumerable.Repeat(GearSlotRecord.Empty, GEAR_COUNT).ToArray();
            slots[1] = new GearSlotRecord(new ItemID(77u), 3);

            string json = CharacterSlotJsonCodec.EncodeGearSlots(slots);

            Assert.AreEqual("[null,{\"item_id\":77,\"enhancement_level\":3}," + Nulls(GEAR_COUNT - 2) + "]", json);
        }

        [Test]
        public void test_gear_emptySlotWithLevel_encodesAsNull()
        {
            GearSlotRecord[] slots = Enumerable.Repeat(GearSlotRecord.Empty, GEAR_COUNT).ToArray();
            slots[0] = new GearSlotRecord(ItemID.Invalid, 5);

            Assert.AreEqual("[" + Nulls(GEAR_COUNT) + "]", CharacterSlotJsonCodec.EncodeGearSlots(slots));
        }

        [Test]
        public void test_gear_emptyJsonArray_decodesAsAllEmpty()
        {
            GearSlotRecord[] decoded = Enumerable.Repeat(new GearSlotRecord(new ItemID(9u), 9), GEAR_COUNT).ToArray();

            CharacterSlotJsonCodec.DecodeGearSlots("[]", decoded);

            Assert.IsTrue(decoded.All(g => g.ItemId == ItemID.Invalid && g.EnhancementLevel == 0));
        }

        [Test]
        public void test_gear_jsonbStyleSpacesAndReorderedKeys_decodes()
        {
            string json = " [ { \"enhancement_level\" : 4 , \"item_id\" : 12 } ,\n\t" + Nulls(GEAR_COUNT - 1) + " ] \r\n";
            GearSlotRecord[] decoded = NewGear();

            CharacterSlotJsonCodec.DecodeGearSlots(json, decoded);

            Assert.AreEqual(new ItemID(12u), decoded[0].ItemId);
            Assert.AreEqual(4, decoded[0].EnhancementLevel);
            Assert.AreEqual(ItemID.Invalid, decoded[1].ItemId);
        }

        [Test]
        public void test_gear_missingEnhancementLevel_readsAsZero()
        {
            GearSlotRecord[] decoded = NewGear();

            CharacterSlotJsonCodec.DecodeGearSlots(GearWithFirst("{\"item_id\":5}"), decoded);

            Assert.AreEqual(new ItemID(5u), decoded[0].ItemId);
            Assert.AreEqual(0, decoded[0].EnhancementLevel);
        }

        [Test]
        public void test_gear_levelAtByteMaximum_decodes()
        {
            GearSlotRecord[] decoded = NewGear();

            CharacterSlotJsonCodec.DecodeGearSlots(GearWithFirst("{\"item_id\":4294967295,\"enhancement_level\":255}"), decoded);

            Assert.AreEqual(new ItemID(uint.MaxValue), decoded[0].ItemId);
            Assert.AreEqual(255, decoded[0].EnhancementLevel);
        }

        // ---------------------------------------------------------------- gear corrupt

        [TestCase("{\"item_id\":\"5\"}")]
        [TestCase("{\"item_id\":5.0}")]
        [TestCase("{\"item_id\":1e3}")]
        [TestCase("{\"item_id\":-5}")]
        [TestCase("{\"item_id\":05}")]
        [TestCase("{\"item_id\":null}")]
        [TestCase("{\"item_id\":true}")]
        [TestCase("{\"item_id\":5,\"foo\":1}")]
        [TestCase("{\"item_id\":5,\"count\":1}")]
        [TestCase("{\"item_id\":5,\"item_id\":5}")]
        [TestCase("{\"item_id\":5,\"enhancement_level\":1,\"enhancement_level\":1}")]
        [TestCase("{\"item_id\":0}")]
        [TestCase("{\"item_id\":0,\"enhancement_level\":2}")]
        [TestCase("{\"enhancement_level\":1}")]
        [TestCase("{}")]
        [TestCase("{\"item_id\":5,\"enhancement_level\":256}")]
        [TestCase("{\"item_id\":5,\"enhancement_level\":-1}")]
        [TestCase("{\"item_id\":4294967296}")]
        [TestCase("{\"item_id\":99999999999999999999999}")]
        [TestCase("{\"item_id\":5,}")]
        [TestCase("{\"item_id\" 5}")]
        [TestCase("{item_id:5}")]
        [TestCase("nul")]
        [TestCase("5")]
        [TestCase("\"x\"")]
        [TestCase("[]")]
        public void test_gear_badEntry_throwsCorrupt(string entry)
        {
            Assert.Throws<CharacterRecordCorruptException>(() => CharacterSlotJsonCodec.DecodeGearSlots(GearWithFirst(entry), NewGear()));
        }

        [TestCase(6)]
        [TestCase(8)]
        [TestCase(1)]
        public void test_gear_wrongEntryCount_throwsCorrupt(int count)
        {
            string json = "[" + Nulls(count) + "]";

            Assert.Throws<CharacterRecordCorruptException>(() => CharacterSlotJsonCodec.DecodeGearSlots(json, NewGear()));
        }

        [TestCase("[null,null,null,null,null,null,null")]
        [TestCase("[null,null,null,null,null,null,null,]")]
        [TestCase("[null,null,null,null,null,null,")]
        [TestCase("[null,null,null,null,null,null,null] x")]
        [TestCase("[null,null,null,null,null,null,null]]")]
        [TestCase("[null,null,null,null,null,null,null][]")]
        [TestCase("[null null null null null null null]")]
        [TestCase("[")]
        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void test_gear_badWholeDocument_throwsCorrupt(string json)
        {
            Assert.Throws<CharacterRecordCorruptException>(() => CharacterSlotJsonCodec.DecodeGearSlots(json, NewGear()));
        }

        [Test]
        public void test_gear_corruptMessage_namesColumnAndSlotIndex()
        {
            string json = "[null,null,{\"item_id\":0},null,null,null,null]";

            var ex = Assert.Throws<CharacterRecordCorruptException>(() => CharacterSlotJsonCodec.DecodeGearSlots(json, NewGear()));

            StringAssert.Contains("gear_slots[2]", ex.Message);
        }

        // ---------------------------------------------------------------- inventory round trip

        [Test]
        public void test_inventory_fullArrayWithExtremes_roundTrips()
        {
            var items = new ItemID[INV_COUNT];
            var counts = new int[INV_COUNT];
            var levels = new byte[INV_COUNT];
            for (int i = 0; i < INV_COUNT; i++)
            {
                items[i] = new ItemID((uint)(i + 1));
                counts[i] = i + 1;
                levels[i] = (byte)i;
            }

            items[0] = new ItemID(uint.MaxValue);
            counts[0] = int.MaxValue;
            levels[0] = 255;
            counts[1] = 0;

            DecodeInventory(CharacterSlotJsonCodec.EncodeInventorySlots(items, counts, levels), out ItemID[] outItems, out int[] outCounts, out byte[] outLevels);

            CollectionAssert.AreEqual(items, outItems);
            CollectionAssert.AreEqual(counts, outCounts);
            CollectionAssert.AreEqual(levels, outLevels);
        }

        [Test]
        public void test_inventory_allEmpty_encodesAsTwentyNullsAndDecodesBack()
        {
            var items = new ItemID[INV_COUNT];
            var counts = new int[INV_COUNT];
            var levels = new byte[INV_COUNT];

            string json = CharacterSlotJsonCodec.EncodeInventorySlots(items, counts, levels);
            var outItems = Enumerable.Repeat(new ItemID(9u), INV_COUNT).ToArray();
            var outCounts = Enumerable.Repeat(9, INV_COUNT).ToArray();
            var outLevels = Enumerable.Repeat((byte)9, INV_COUNT).ToArray();
            CharacterSlotJsonCodec.DecodeInventorySlots(json, outItems, outCounts, outLevels);

            Assert.AreEqual("[" + Nulls(INV_COUNT) + "]", json);
            Assert.IsTrue(outItems.All(i => i == ItemID.Invalid));
            Assert.IsTrue(outCounts.All(c => c == 0));
            Assert.IsTrue(outLevels.All(l => l == 0));
        }

        [Test]
        public void test_inventory_emptySlotCountAndLevel_notWritten()
        {
            var items = new ItemID[INV_COUNT];
            var counts = new int[INV_COUNT];
            var levels = new byte[INV_COUNT];
            items[1] = new ItemID(5u);
            counts[1] = 3;
            levels[1] = 2;
            counts[0] = 99;
            levels[0] = 7;

            string json = CharacterSlotJsonCodec.EncodeInventorySlots(items, counts, levels);

            Assert.AreEqual("[null,{\"item_id\":5,\"count\":3,\"enhancement_level\":2}," + Nulls(INV_COUNT - 2) + "]", json);
        }

        [Test]
        public void test_inventory_emptyJsonArray_decodesAsAllEmpty()
        {
            DecodeInventory("[]", out ItemID[] items, out int[] counts, out byte[] levels);

            Assert.IsTrue(items.All(i => i == ItemID.Invalid));
            Assert.IsTrue(counts.All(c => c == 0));
            Assert.IsTrue(levels.All(l => l == 0));
        }

        [Test]
        public void test_inventory_jsonbStyleSpacesAndReorderedKeys_decodes()
        {
            string json = "[ {\"count\": 3, \"item_id\": 5, \"enhancement_level\": 0}, " + Nulls(INV_COUNT - 1) + " ]";

            DecodeInventory(json, out ItemID[] items, out int[] counts, out byte[] levels);

            Assert.AreEqual(new ItemID(5u), items[0]);
            Assert.AreEqual(3, counts[0]);
            Assert.AreEqual(0, levels[0]);
        }

        [Test]
        public void test_inventory_missingEnhancementLevel_readsAsZero()
        {
            DecodeInventory(InventoryWithFirst("{\"item_id\":5,\"count\":2}"), out ItemID[] items, out int[] counts, out byte[] levels);

            Assert.AreEqual(new ItemID(5u), items[0]);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(0, levels[0]);
        }

        [Test]
        public void test_inventory_zeroCount_decodes()
        {
            DecodeInventory(InventoryWithFirst("{\"item_id\":5,\"count\":0}"), out ItemID[] items, out int[] counts, out byte[] _);

            Assert.AreEqual(new ItemID(5u), items[0]);
            Assert.AreEqual(0, counts[0]);
        }

        // ---------------------------------------------------------------- inventory corrupt

        [TestCase("{\"item_id\":\"5\",\"count\":1}")]
        [TestCase("{\"item_id\":5,\"count\":\"1\"}")]
        [TestCase("{\"item_id\":5,\"count\":1.5}")]
        [TestCase("{\"item_id\":5,\"count\":1e2}")]
        [TestCase("{\"item_id\":5,\"count\":-1}")]
        [TestCase("{\"item_id\":5,\"count\":2147483648}")]
        [TestCase("{\"item_id\":5}")]
        [TestCase("{\"count\":1}")]
        [TestCase("{\"item_id\":5,\"count\":1,\"foo\":1}")]
        [TestCase("{\"item_id\":5,\"count\":1,\"count\":1}")]
        [TestCase("{\"item_id\":5,\"item_id\":5,\"count\":1}")]
        [TestCase("{\"item_id\":0,\"count\":1}")]
        [TestCase("{\"item_id\":5,\"count\":1,\"enhancement_level\":256}")]
        [TestCase("{\"item_id\":4294967296,\"count\":1}")]
        [TestCase("{\"item_id\":5,\"count\":1")]
        [TestCase("{}")]
        [TestCase("7")]
        public void test_inventory_badEntry_throwsCorrupt(string entry)
        {
            Assert.Throws<CharacterRecordCorruptException>(() => DecodeInventory(InventoryWithFirst(entry), out _, out _, out _));
        }

        [TestCase(19)]
        [TestCase(21)]
        [TestCase(7)]
        public void test_inventory_wrongEntryCount_throwsCorrupt(int count)
        {
            string json = "[" + Nulls(count) + "]";

            Assert.Throws<CharacterRecordCorruptException>(() => DecodeInventory(json, out _, out _, out _));
        }

        [TestCase("[null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null")]
        [TestCase("[null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null] garbage")]
        [TestCase("[null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,]")]
        [TestCase("{}")]
        [TestCase("")]
        [TestCase(null)]
        public void test_inventory_badWholeDocument_throwsCorrupt(string json)
        {
            Assert.Throws<CharacterRecordCorruptException>(() => DecodeInventory(json, out _, out _, out _));
        }

        [Test]
        public void test_inventory_corruptMessage_namesColumnAndSlotIndex()
        {
            string json = "[null,{\"item_id\":5,\"count\":-1}," + Nulls(INV_COUNT - 2) + "]";

            var ex = Assert.Throws<CharacterRecordCorruptException>(() => DecodeInventory(json, out _, out _, out _));

            StringAssert.Contains("inventory_slots[1]", ex.Message);
        }

        // ---------------------------------------------------------------- argument checks

        [Test]
        public void test_encodeGear_nullArray_throwsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => CharacterSlotJsonCodec.EncodeGearSlots(null));
        }

        [TestCase(0)]
        [TestCase(GEAR_COUNT - 1)]
        [TestCase(GEAR_COUNT + 1)]
        public void test_encodeGear_wrongLength_throwsArgument(int length)
        {
            Assert.Throws<ArgumentException>(() => CharacterSlotJsonCodec.EncodeGearSlots(new GearSlotRecord[length]));
        }

        [Test]
        public void test_encodeInventory_nullArray_throwsArgumentNull()
        {
            var items = new ItemID[INV_COUNT];
            var counts = new int[INV_COUNT];
            var levels = new byte[INV_COUNT];

            Assert.Throws<ArgumentNullException>(() => CharacterSlotJsonCodec.EncodeInventorySlots(null, counts, levels));
            Assert.Throws<ArgumentNullException>(() => CharacterSlotJsonCodec.EncodeInventorySlots(items, null, levels));
            Assert.Throws<ArgumentNullException>(() => CharacterSlotJsonCodec.EncodeInventorySlots(items, counts, null));
        }

        [Test]
        public void test_encodeInventory_wrongLength_throwsArgument()
        {
            var items = new ItemID[INV_COUNT];
            var counts = new int[INV_COUNT];
            var levels = new byte[INV_COUNT];

            Assert.Throws<ArgumentException>(() => CharacterSlotJsonCodec.EncodeInventorySlots(new ItemID[INV_COUNT - 1], counts, levels));
            Assert.Throws<ArgumentException>(() => CharacterSlotJsonCodec.EncodeInventorySlots(items, new int[INV_COUNT + 1], levels));
            Assert.Throws<ArgumentException>(() => CharacterSlotJsonCodec.EncodeInventorySlots(items, counts, new byte[0]));
        }

        [Test]
        public void test_encodeInventory_negativeCountOnOccupiedSlot_throwsArgument()
        {
            var items = new ItemID[INV_COUNT];
            var counts = new int[INV_COUNT];
            var levels = new byte[INV_COUNT];
            items[4] = new ItemID(77u);
            counts[4] = -1;

            Assert.Throws<ArgumentException>(() => CharacterSlotJsonCodec.EncodeInventorySlots(items, counts, levels));
        }

        [Test]
        public void test_encodeInventory_negativeCountOnEmptySlot_isIgnoredAndWritesNull()
        {
            var items = new ItemID[INV_COUNT];
            var counts = new int[INV_COUNT];
            var levels = new byte[INV_COUNT];
            counts[4] = -1;

            string json = CharacterSlotJsonCodec.EncodeInventorySlots(items, counts, levels);

            StringAssert.StartsWith("[null,null,null,null,null,", json);
        }

        [Test]
        public void test_decodeGear_destinationWrongLengthOrNull_throws()
        {
            Assert.Throws<ArgumentException>(() => CharacterSlotJsonCodec.DecodeGearSlots("[]", new GearSlotRecord[GEAR_COUNT + 1]));
            Assert.Throws<ArgumentNullException>(() => CharacterSlotJsonCodec.DecodeGearSlots("[]", null));
        }

        [Test]
        public void test_decodeInventory_destinationWrongLengthOrNull_throws()
        {
            var items = new ItemID[INV_COUNT];
            var counts = new int[INV_COUNT];
            var levels = new byte[INV_COUNT];

            Assert.Throws<ArgumentException>(() => CharacterSlotJsonCodec.DecodeInventorySlots("[]", new ItemID[INV_COUNT + 1], counts, levels));
            Assert.Throws<ArgumentException>(() => CharacterSlotJsonCodec.DecodeInventorySlots("[]", items, new int[1], levels));
            Assert.Throws<ArgumentException>(() => CharacterSlotJsonCodec.DecodeInventorySlots("[]", items, counts, new byte[INV_COUNT - 1]));
            Assert.Throws<ArgumentNullException>(() => CharacterSlotJsonCodec.DecodeInventorySlots("[]", items, counts, null));
        }
    }
}
