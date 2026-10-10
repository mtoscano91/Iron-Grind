using System;
using IronGrind.InventorySystem;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="InventoryFullSync"/>: one <c>count</c> byte (always
    /// <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>) followed by that many <see cref="InventorySlotEntry"/>
    /// records. No allocation. The entry layout is shared with <see cref="InventorySlotUpdateCodec"/>, including the
    /// nullable <c>itemId</c> (CR-NET-7.3 exception).
    /// </summary>
    public static class InventoryFullSyncCodec
    {
        /// <summary>
        /// Writes the body at offset 0 of <paramref name="destination"/> and returns <see cref="InventoryFullSync.BodySize"/>.
        /// The caller supplies the entries in ascending <c>slotIndex</c> order.
        /// </summary>
        /// <param name="destination">At least <see cref="InventoryFullSync.BodySize"/> bytes.</param>
        /// <param name="entries">Exactly <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> entries, one per slot.</param>
        /// <exception cref="ArgumentException"><paramref name="entries"/> does not hold exactly one entry per bag slot.</exception>
        public static int WriteBody(Span<byte> destination, ReadOnlySpan<InventorySlotEntry> entries)
        {
            if (entries.Length != InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                throw new ArgumentException("InventoryFullSync carries exactly INVENTORY_SLOT_COUNT entries.", nameof(entries));
            }

            destination[0] = (byte)entries.Length;
            InventorySlotUpdateCodec.WriteEntries(destination.Slice(InventorySlotUpdate.CountFieldSize), entries);
            return InventoryFullSync.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body into <paramref name="destination"/>. A <c>count</c> byte other than
        /// <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> is a malformed message (AC-NC-42): returns
        /// <see langword="false"/> and logs exactly one <see cref="Debug.LogWarning(object)"/> anomaly. A body shorter than
        /// <see cref="InventoryFullSync.BodySize"/> or a <paramref name="destination"/> with fewer elements than the slot
        /// count also returns <see langword="false"/>, without a log. Extra trailing bytes are ignored.
        /// </summary>
        /// <remarks>
        /// The decoder checks the count and the lengths only. It does not validate the slot index range, a negative
        /// quantity or a repeated slot index; the consumer (the client bag model) must.
        /// </remarks>
        /// <param name="body">The message body (the bytes after the envelope).</param>
        /// <param name="destination">Receives the entries; at least <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> elements.</param>
        public static bool TryReadBody(ReadOnlySpan<byte> body, Span<InventorySlotEntry> destination)
        {
            if (body.Length < InventorySlotUpdate.CountFieldSize)
            {
                return false;
            }

            if (body[0] != InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogWarning($"[InventoryFullSyncCodec] TryReadBody: received count byte {body[0]} for " +
                    $"messageTypeId={InventoryFullSync.MessageTypeId} — expected INVENTORY_SLOT_COUNT, discarding the message (AC-NC-42).");
                return false;
            }

            if (body.Length < InventoryFullSync.BodySize || destination.Length < InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                return false;
            }

            InventorySlotUpdateCodec.ReadEntries(body.Slice(InventorySlotUpdate.CountFieldSize), destination, InventoryConstants.INVENTORY_SLOT_COUNT);
            return true;
        }
    }
}
