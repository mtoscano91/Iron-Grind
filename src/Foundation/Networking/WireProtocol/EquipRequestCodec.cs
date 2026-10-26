using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="EquipRequest"/> (10-byte body: <c>uint requestId</c>, <c>byte gearSlot</c>,
    /// <c>uint itemId</c>, <c>byte inventorySlot</c>, little-endian). No allocation. Fields are not range-checked.
    /// </summary>
    public static class EquipRequestCodec
    {
        /// <summary>Writes the 10-byte body at offset 0 of <paramref name="destination"/>.</summary>
        /// <param name="destination">At least <see cref="EquipRequest.BodySize"/> bytes.</param>
        /// <param name="requestId">Client-generated monotonically increasing request id.</param>
        /// <param name="gearSlot">Target gear slot (<c>GearSlot</c> value; not range-checked).</param>
        /// <param name="itemId">Item to equip; <see cref="EquipRequest.UnequipItemId"/> (0) to unequip.</param>
        /// <param name="inventorySlot">Bag slot of the item; <see cref="EquipRequest.UnusedInventorySlot"/> for an unequip.</param>
        /// <returns><see cref="EquipRequest.BodySize"/> (10), always.</returns>
        public static int WriteBody(Span<byte> destination, uint requestId, byte gearSlot, uint itemId, byte inventorySlot)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), requestId);
            destination[4] = gearSlot;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(5, 4), itemId);
            destination[9] = inventorySlot;
            return EquipRequest.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> if <paramref name="body"/> is not exactly
        /// <see cref="EquipRequest.BodySize"/> bytes — a short or a long body is rejected (Story 008), unlike the
        /// Enhancement codecs, which ignore extra bytes.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out uint requestId, out byte gearSlot, out uint itemId, out byte inventorySlot)
        {
            if (body.Length != EquipRequest.BodySize)
            {
                requestId = 0;
                gearSlot = 0;
                itemId = 0;
                inventorySlot = 0;
                return false;
            }

            requestId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            gearSlot = body[4];
            itemId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(5, 4));
            inventorySlot = body[9];
            return true;
        }
    }
}
