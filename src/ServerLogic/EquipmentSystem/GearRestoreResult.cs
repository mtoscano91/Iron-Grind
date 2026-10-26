#nullable enable

using IronGrind.ItemDatabase;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// Outcome of <see cref="IEquipmentService.RestoreSlots"/>: which saved slots were left empty
    /// because the Item Database did not know the item or the item's gear slot did not match
    /// (Story 007, EC-CP-1). Character Persistence uses it to set <c>ItemDatabaseMiss</c>.
    /// </summary>
    public readonly struct GearRestoreResult
    {
        private readonly byte _droppedSlotMask;

        internal GearRestoreResult(byte droppedSlotMask)
        {
            _droppedSlotMask = droppedSlotMask;
        }

        /// <summary>Bit <c>(int)GearSlot</c> is set when that slot was left empty.</summary>
        public byte DroppedSlotMask => _droppedSlotMask;

        /// <summary>Number of dropped slots (popcount of <see cref="DroppedSlotMask"/>).</summary>
        public int DroppedCount
        {
            get
            {
                int count = 0;
                for (int mask = _droppedSlotMask; mask != 0; mask &= mask - 1)
                {
                    count++;
                }

                return count;
            }
        }

        /// <summary>Whether <paramref name="slot"/> was left empty by the restore.</summary>
        /// <param name="slot">The gear slot; a value outside [0, 6] returns false.</param>
        public bool IsDropped(GearSlot slot)
        {
            int index = (int)slot;
            return (uint)index < (uint)EquipmentService.GEAR_SLOT_COUNT && (_droppedSlotMask & (1 << index)) != 0;
        }
    }
}
