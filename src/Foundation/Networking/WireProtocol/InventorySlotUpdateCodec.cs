using System;
using System.Buffers.Binary;
using IronGrind.CharacterStats;
using IronGrind.InventorySystem;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="InventorySlotUpdate"/>: one <c>count</c> byte followed by <c>count</c>
    /// <see cref="InventorySlotEntry"/> records of 10 bytes each. No allocation.
    /// </summary>
    /// <remarks>
    /// <c>itemId</c> is a nullable ID field (CR-NET-7.3 exception, stated in the schema): an emptied slot writes
    /// <c>0</c> without the zero-write assertion of <see cref="WireIdCodec.SerializeItemId"/>, so this codec writes
    /// the raw <see cref="uint"/> directly. The entry helpers are shared with <see cref="InventoryFullSyncCodec"/>.
    /// </remarks>
    public static class InventorySlotUpdateCodec
    {
        /// <summary>
        /// Writes the body at offset 0 of <paramref name="destination"/> and returns the number of bytes written
        /// (<c>1 + 10 × entries.Length</c>).
        /// </summary>
        /// <param name="destination">At least <see cref="InventorySlotUpdate.GetBodySize"/> bytes for the entry count.</param>
        /// <param name="entries">1 to <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> entries; no slot index twice.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="entries"/> is empty or longer than the slot count.</exception>
        public static int WriteBody(Span<byte> destination, ReadOnlySpan<InventorySlotEntry> entries)
        {
            if (entries.Length < 1 || entries.Length > InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                throw new ArgumentOutOfRangeException(nameof(entries), entries.Length,
                    "InventorySlotUpdate carries 1 to INVENTORY_SLOT_COUNT entries.");
            }

            destination[0] = (byte)entries.Length;
            WriteEntries(destination.Slice(InventorySlotUpdate.CountFieldSize), entries);
            return InventorySlotUpdate.GetBodySize(entries.Length);
        }

        /// <summary>
        /// Attempts to decode the body into <paramref name="destination"/>. Returns <see langword="false"/> (and
        /// <paramref name="count"/> 0) if <paramref name="body"/> is empty, if the <c>count</c> byte is 0 or above
        /// <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>, if <paramref name="body"/> is shorter than
        /// <c>1 + 10 × count</c>, or if <paramref name="destination"/> is shorter than <c>count</c>. Extra trailing
        /// bytes are ignored. Does not log.
        /// </summary>
        /// <remarks>
        /// The decoder checks the count and the lengths only. It does not validate the slot index range, a negative
        /// quantity or a repeated slot index; the consumer (the client bag model) must.
        /// </remarks>
        /// <param name="body">The message body (the bytes after the envelope).</param>
        /// <param name="destination">Receives the entries; give it <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> elements.</param>
        /// <param name="count">The number of entries written to <paramref name="destination"/>.</param>
        public static bool TryReadBody(ReadOnlySpan<byte> body, Span<InventorySlotEntry> destination, out int count)
        {
            count = 0;
            if (body.Length < InventorySlotUpdate.CountFieldSize)
            {
                return false;
            }

            int claimed = body[0];
            if (claimed < 1 || claimed > InventoryConstants.INVENTORY_SLOT_COUNT
                || body.Length < InventorySlotUpdate.GetBodySize(claimed)
                || destination.Length < claimed)
            {
                return false;
            }

            ReadEntries(body.Slice(InventorySlotUpdate.CountFieldSize), destination, claimed);
            count = claimed;
            return true;
        }

        // Field layout of one InventorySlotEntry record, each offset derived from the previous field.
        private const int SlotIndexOffset = 0;
        private const int SlotIndexSize = 1;
        private const int ItemIdOffset = SlotIndexOffset + SlotIndexSize;
        private const int QuantityOffset = ItemIdOffset + WireIdCodec.ItemIdWireSize;
        private const int QuantitySize = 4;
        private const int EnhancementLevelOffset = QuantityOffset + QuantitySize;

        internal static void WriteEntries(Span<byte> destination, ReadOnlySpan<InventorySlotEntry> entries)
        {
            int offset = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                InventorySlotEntry entry = entries[i];
                destination[offset + SlotIndexOffset] = entry.SlotIndex;
                // Nullable ID field (CR-NET-7.3): 0 is the empty-slot value, so no zero-write assertion.
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + ItemIdOffset, WireIdCodec.ItemIdWireSize), entry.ItemId.RawValue);
                BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset + QuantityOffset, QuantitySize), entry.Quantity);
                destination[offset + EnhancementLevelOffset] = entry.EnhancementLevel;
                offset += InventorySlotEntry.WireSize;
            }
        }

        internal static void ReadEntries(ReadOnlySpan<byte> source, Span<InventorySlotEntry> destination, int count)
        {
            int offset = 0;
            for (int i = 0; i < count; i++)
            {
                destination[i] = new InventorySlotEntry(
                    source[offset + SlotIndexOffset],
                    new ItemID(BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset + ItemIdOffset, WireIdCodec.ItemIdWireSize))),
                    BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset + QuantityOffset, QuantitySize)),
                    source[offset + EnhancementLevelOffset]);
                offset += InventorySlotEntry.WireSize;
            }
        }
    }
}
