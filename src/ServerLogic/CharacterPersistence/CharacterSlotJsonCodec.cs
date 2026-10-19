using System;
using System.Globalization;
using System.Text;
using IronGrind.CharacterStats;
using IronGrind.InventorySystem;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Hand-written codec for the <c>gear_slots</c> and <c>inventory_slots</c> JSONB columns (ADR-006 Key Interfaces,
    /// Amendment 1; Character Persistence Story 014). No reflection, no JSON library.
    /// </summary>
    /// <remarks>
    /// Format: <c>gear_slots</c> is an array of exactly <see cref="CharacterPersistenceConstants.GEAR_SLOT_COUNT"/> entries,
    /// each <c>null</c> (empty slot) or <c>{"item_id":N,"enhancement_level":M}</c>; <c>inventory_slots</c> is an array of
    /// exactly <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> entries, each <c>null</c> or
    /// <c>{"item_id":N,"count":C,"enhancement_level":M}</c>. The decoder accepts what PostgreSQL JSONB returns: any JSON
    /// whitespace and object keys in any order. <c>[]</c> (the column default) means all slots empty. A missing
    /// <c>enhancement_level</c> reads as 0. Every other deviation throws <see cref="CharacterRecordCorruptException"/>.
    /// </remarks>
    internal static class CharacterSlotJsonCodec
    {
        private const string GEAR_COLUMN = "gear_slots";
        private const string INVENTORY_COLUMN = "inventory_slots";
        private const string KEY_ITEM_ID = "item_id";
        private const string KEY_COUNT = "count";
        private const string KEY_LEVEL = "enhancement_level";

        // Larger than any accepted value; stops the digit accumulation from overflowing ulong.
        private const ulong MAX_PARSED_VALUE = 100000000000UL;

        /// <summary>Encodes the gear slots as the compact <c>gear_slots</c> JSON array.</summary>
        /// <param name="slots">Exactly <see cref="CharacterPersistenceConstants.GEAR_SLOT_COUNT"/> slots.</param>
        /// <returns>The JSON text; an empty slot (<see cref="ItemID.Invalid"/>) is written as <c>null</c>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="slots"/> is null.</exception>
        /// <exception cref="ArgumentException">The length is not <see cref="CharacterPersistenceConstants.GEAR_SLOT_COUNT"/>.</exception>
        internal static string EncodeGearSlots(GearSlotRecord[] slots)
        {
            if (slots == null)
            {
                throw new ArgumentNullException(nameof(slots));
            }

            if (slots.Length != CharacterPersistenceConstants.GEAR_SLOT_COUNT)
            {
                throw new ArgumentException("Expected " + CharacterPersistenceConstants.GEAR_SLOT_COUNT + " gear slots, got " + slots.Length + ".", nameof(slots));
            }

            var sb = new StringBuilder(slots.Length * 40);
            sb.Append('[');
            for (int i = 0; i < slots.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                if (slots[i].ItemId == ItemID.Invalid)
                {
                    sb.Append("null");
                    continue;
                }

                sb.Append("{\"item_id\":").Append(slots[i].ItemId.RawValue.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"enhancement_level\":").Append(slots[i].EnhancementLevel.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }

            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>Encodes the inventory slots as the compact <c>inventory_slots</c> JSON array.</summary>
        /// <param name="items">Item per slot; length <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>.</param>
        /// <param name="counts">Stack count per slot; same length. Not written for an empty slot; must not be negative for an occupied one.</param>
        /// <param name="enhancementLevels">Enhancement level per slot; same length. Not written for an empty slot.</param>
        /// <returns>The JSON text; an empty slot (<see cref="ItemID.Invalid"/>) is written as <c>null</c>.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">An array length is not <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>, or an
        /// occupied slot has a negative count (the decoder would reject it, so the save fails instead of writing a row that
        /// cannot be loaded).</exception>
        internal static string EncodeInventorySlots(ItemID[] items, int[] counts, byte[] enhancementLevels)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (counts == null)
            {
                throw new ArgumentNullException(nameof(counts));
            }

            if (enhancementLevels == null)
            {
                throw new ArgumentNullException(nameof(enhancementLevels));
            }

            RequireInventoryLength(items.Length, nameof(items));
            RequireInventoryLength(counts.Length, nameof(counts));
            RequireInventoryLength(enhancementLevels.Length, nameof(enhancementLevels));

            var sb = new StringBuilder(items.Length * 50);
            sb.Append('[');
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                if (items[i] == ItemID.Invalid)
                {
                    sb.Append("null");
                    continue;
                }

                if (counts[i] < 0)
                {
                    throw new ArgumentException("Inventory slot " + i + " holds " + items[i] + " with a negative count (" + counts[i] + ").", nameof(counts));
                }

                sb.Append("{\"item_id\":").Append(items[i].RawValue.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"count\":").Append(counts[i].ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"enhancement_level\":").Append(enhancementLevels[i].ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }

            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>Decodes a <c>gear_slots</c> JSON array into <paramref name="destination"/>.</summary>
        /// <param name="json">The column text.</param>
        /// <param name="destination">Receives every slot; length <see cref="CharacterPersistenceConstants.GEAR_SLOT_COUNT"/>.</param>
        /// <remarks>On <see cref="CharacterRecordCorruptException"/> the contents of <paramref name="destination"/> are unspecified.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="destination"/> has the wrong length.</exception>
        /// <exception cref="CharacterRecordCorruptException"><paramref name="json"/> is null, empty or malformed.</exception>
        internal static void DecodeGearSlots(string json, GearSlotRecord[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length != CharacterPersistenceConstants.GEAR_SLOT_COUNT)
            {
                throw new ArgumentException("Expected " + CharacterPersistenceConstants.GEAR_SLOT_COUNT + " gear slots, got " + destination.Length + ".", nameof(destination));
            }

            DecodeCore(json, GEAR_COLUMN, false, destination, null, null, null);
        }

        /// <summary>Decodes an <c>inventory_slots</c> JSON array into the three parallel arrays.</summary>
        /// <param name="json">The column text.</param>
        /// <param name="items">Receives the item per slot; length <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>.</param>
        /// <param name="counts">Receives the count per slot; same length.</param>
        /// <param name="enhancementLevels">Receives the enhancement level per slot; same length.</param>
        /// <remarks>On <see cref="CharacterRecordCorruptException"/> the contents of the arrays are unspecified.</remarks>
        /// <exception cref="ArgumentNullException">A destination array is null.</exception>
        /// <exception cref="ArgumentException">A destination array has the wrong length.</exception>
        /// <exception cref="CharacterRecordCorruptException"><paramref name="json"/> is null, empty or malformed.</exception>
        internal static void DecodeInventorySlots(string json, ItemID[] items, int[] counts, byte[] enhancementLevels)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (counts == null)
            {
                throw new ArgumentNullException(nameof(counts));
            }

            if (enhancementLevels == null)
            {
                throw new ArgumentNullException(nameof(enhancementLevels));
            }

            RequireInventoryLength(items.Length, nameof(items));
            RequireInventoryLength(counts.Length, nameof(counts));
            RequireInventoryLength(enhancementLevels.Length, nameof(enhancementLevels));

            DecodeCore(json, INVENTORY_COLUMN, true, null, items, counts, enhancementLevels);
        }

        private static void RequireInventoryLength(int length, string paramName)
        {
            if (length != InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                throw new ArgumentException("Expected " + InventoryConstants.INVENTORY_SLOT_COUNT + " inventory slots, got " + length + ".", paramName);
            }
        }

        private enum SlotKey
        {
            ItemId,
            Count,
            Level,
        }

        // One parsed object entry.
        private struct SlotValue
        {
            public uint ItemId;
            public int Count;
            public byte Level;
        }

        // Where decoded entries go: the gear array, or the three inventory arrays.
        private sealed class Target
        {
            public string Column;
            public bool Inventory;
            public int Expected;
            public GearSlotRecord[] Gear;
            public ItemID[] Items;
            public int[] Counts;
            public byte[] Levels;
        }

        private static void DecodeCore(
            string json,
            string column,
            bool inventory,
            GearSlotRecord[] gear,
            ItemID[] items,
            int[] counts,
            byte[] levels)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw Corrupt(column, -1, "the column is null or empty");
            }

            var target = new Target
            {
                Column = column,
                Inventory = inventory,
                Expected = inventory ? items.Length : gear.Length,
                Gear = gear,
                Items = items,
                Counts = counts,
                Levels = levels,
            };
            int pos = 0;
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] != '[')
            {
                throw Corrupt(column, -1, "expected '[' at position " + pos);
            }

            pos++;
            int entries = ParseEntries(json, ref pos, target);
            SkipWhitespace(json, ref pos);
            if (pos != json.Length)
            {
                throw Corrupt(column, -1, "trailing characters at position " + pos);
            }

            if (entries == 0)
            {
                ClearTarget(target);
            }
        }

        // Parses the entries after '[' up to and including ']'. Returns 0 for '[]', otherwise exactly target.Expected.
        private static int ParseEntries(string json, ref int pos, Target target)
        {
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ']')
            {
                pos++;
                return 0;
            }

            int index = 0;
            while (true)
            {
                if (index >= target.Expected)
                {
                    throw Corrupt(target.Column, -1, "more than " + target.Expected + " entries");
                }

                bool present = ParseEntry(json, ref pos, target.Column, index, target.Inventory, out SlotValue value);
                StoreEntry(target, index, present, value);
                index++;
                if (ReadSeparator(json, ref pos, target.Column, index - 1, ']'))
                {
                    break;
                }
            }

            if (index != target.Expected)
            {
                throw Corrupt(target.Column, -1, "has " + index + " entries, expected " + target.Expected);
            }

            return index;
        }

        private static void StoreEntry(Target target, int index, bool present, SlotValue value)
        {
            if (target.Inventory)
            {
                target.Items[index] = present ? new ItemID(value.ItemId) : ItemID.Invalid;
                target.Counts[index] = present ? value.Count : 0;
                target.Levels[index] = present ? value.Level : (byte)0;
            }
            else
            {
                target.Gear[index] = present ? new GearSlotRecord(new ItemID(value.ItemId), value.Level) : GearSlotRecord.Empty;
            }
        }

        private static void ClearTarget(Target target)
        {
            if (target.Inventory)
            {
                Array.Clear(target.Items, 0, target.Items.Length);
                Array.Clear(target.Counts, 0, target.Counts.Length);
                Array.Clear(target.Levels, 0, target.Levels.Length);
                return;
            }

            for (int i = 0; i < target.Gear.Length; i++)
            {
                target.Gear[i] = GearSlotRecord.Empty;
            }
        }

        // Reads ',' (returns false) or the closing character (returns true) after optional whitespace.
        private static bool ReadSeparator(string json, ref int pos, string column, int index, char closing)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length)
            {
                throw Corrupt(column, index, "truncated after the entry");
            }

            char c = json[pos];
            pos++;
            if (c == closing)
            {
                return true;
            }

            if (c != ',')
            {
                throw Corrupt(column, index, "expected ',' or '" + closing + "' at position " + (pos - 1));
            }

            return false;
        }

        // Returns false for a null entry, true for an object (value filled).
        private static bool ParseEntry(string json, ref int pos, string column, int index, bool inventory, out SlotValue value)
        {
            value = default;
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length)
            {
                throw Corrupt(column, index, "truncated, expected null or an object");
            }

            if (json[pos] == 'n')
            {
                if (string.CompareOrdinal(json, pos, "null", 0, 4) != 0)
                {
                    throw Corrupt(column, index, "invalid literal at position " + pos);
                }

                pos += 4;
                return false;
            }

            if (json[pos] != '{')
            {
                throw Corrupt(column, index, "expected null or an object at position " + pos);
            }

            pos++;
            value = ParseObject(json, ref pos, column, index, inventory);
            return true;
        }

        // Parses the members after '{' up to and including '}', and checks the required keys.
        private static SlotValue ParseObject(string json, ref int pos, string column, int index, bool inventory)
        {
            var value = default(SlotValue);
            bool haveItem = false;
            bool haveCount = false;
            bool haveLevel = false;
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == '}')
            {
                pos++;
            }
            else
            {
                do
                {
                    SlotKey key = ReadKey(json, ref pos, column, index, inventory);
                    ulong number = ParseUnsigned(json, ref pos, column, index, KeyName(key));
                    ApplyMember(ref value, key, number, column, index, ref haveItem, ref haveCount, ref haveLevel);
                }
                while (!ReadSeparator(json, ref pos, column, index, '}'));
            }

            if (!haveItem)
            {
                throw Corrupt(column, index, "missing " + KEY_ITEM_ID);
            }

            if (inventory && !haveCount)
            {
                throw Corrupt(column, index, "missing " + KEY_COUNT);
            }

            if (value.ItemId == 0u)
            {
                throw Corrupt(column, index, KEY_ITEM_ID + " 0 in an object (an empty slot must be null)");
            }

            return value;
        }

        // Reads '"key"' and ':' (with whitespace). Keys are compared in place; a string is built only for an error.
        // A key with an escape is not one of the three and is rejected as unknown, which is right for the shapes
        // this codec writes.
        private static SlotKey ReadKey(string json, ref int pos, string column, int index, bool inventory)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] != '"')
            {
                throw Corrupt(column, index, "expected a key at position " + pos);
            }

            pos++;
            int keyStart = pos;
            while (pos < json.Length && json[pos] != '"')
            {
                pos++;
            }

            if (pos >= json.Length)
            {
                throw Corrupt(column, index, "truncated inside a key");
            }

            int length = pos - keyStart;
            pos++;
            SlotKey key;
            if (KeyIs(json, keyStart, length, KEY_ITEM_ID))
            {
                key = SlotKey.ItemId;
            }
            else if (inventory && KeyIs(json, keyStart, length, KEY_COUNT))
            {
                key = SlotKey.Count;
            }
            else if (KeyIs(json, keyStart, length, KEY_LEVEL))
            {
                key = SlotKey.Level;
            }
            else
            {
                throw Corrupt(column, index, "unknown key '" + json.Substring(keyStart, length) + "'");
            }

            SkipWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] != ':')
            {
                throw Corrupt(column, index, "expected ':' after key '" + KeyName(key) + "'");
            }

            pos++;
            SkipWhitespace(json, ref pos);
            return key;
        }

        private static bool KeyIs(string json, int start, int length, string key)
        {
            return length == key.Length && string.CompareOrdinal(json, start, key, 0, length) == 0;
        }

        private static string KeyName(SlotKey key)
        {
            switch (key)
            {
                case SlotKey.ItemId:
                    return KEY_ITEM_ID;
                case SlotKey.Count:
                    return KEY_COUNT;
                default:
                    return KEY_LEVEL;
            }
        }

        // Range-checks one member's value and stores it; rejects a duplicate key.
        private static void ApplyMember(
            ref SlotValue value,
            SlotKey key,
            ulong number,
            string column,
            int index,
            ref bool haveItem,
            ref bool haveCount,
            ref bool haveLevel)
        {
            switch (key)
            {
                case SlotKey.ItemId:
                    RejectDuplicate(ref haveItem, column, index, KEY_ITEM_ID);
                    value.ItemId = (uint)RequireAtMost(number, uint.MaxValue, column, index, KEY_ITEM_ID);
                    break;
                case SlotKey.Count:
                    RejectDuplicate(ref haveCount, column, index, KEY_COUNT);
                    value.Count = (int)RequireAtMost(number, int.MaxValue, column, index, KEY_COUNT);
                    break;
                default:
                    RejectDuplicate(ref haveLevel, column, index, KEY_LEVEL);
                    value.Level = (byte)RequireAtMost(number, byte.MaxValue, column, index, KEY_LEVEL);
                    break;
            }
        }

        private static ulong RequireAtMost(ulong number, ulong max, string column, int index, string key)
        {
            if (number > max)
            {
                throw Corrupt(column, index, key + " " + number + " is above " + max);
            }

            return number;
        }

        private static void RejectDuplicate(ref bool seen, string column, int index, string key)
        {
            if (seen)
            {
                throw Corrupt(column, index, "duplicate key '" + key + "'");
            }

            seen = true;
        }

        // A non-negative integer literal: digits only, no sign, fraction, exponent or leading zero.
        private static ulong ParseUnsigned(string json, ref int pos, string column, int index, string key)
        {
            int start = pos;
            ulong value = 0UL;
            while (pos < json.Length && json[pos] >= '0' && json[pos] <= '9')
            {
                value = (value * 10UL) + (ulong)(json[pos] - '0');
                if (value > MAX_PARSED_VALUE)
                {
                    throw Corrupt(column, index, "value of '" + key + "' is out of range");
                }

                pos++;
            }

            if (pos == start)
            {
                throw Corrupt(column, index, "value of '" + key + "' is not a non-negative integer at position " + start);
            }

            if (json[start] == '0' && pos - start > 1)
            {
                throw Corrupt(column, index, "value of '" + key + "' has a leading zero");
            }

            if (pos < json.Length && (json[pos] == '.' || json[pos] == 'e' || json[pos] == 'E'))
            {
                throw Corrupt(column, index, "value of '" + key + "' is not an integer literal");
            }

            return value;
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                {
                    return;
                }

                pos++;
            }
        }

        private static CharacterRecordCorruptException Corrupt(string column, int index, string detail)
        {
            string where = index >= 0 ? column + "[" + index + "]" : column;
            return new CharacterRecordCorruptException("Corrupt " + where + ": " + detail + ".");
        }
    }
}
