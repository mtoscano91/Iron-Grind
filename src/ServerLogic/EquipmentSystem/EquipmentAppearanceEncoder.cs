using IronGrind.ItemDatabase;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// Packs the <c>equipmentAppearanceFlags</c> byte (design/gdd/equipment-system.md CR-EQS-11,
    /// TR-equip-007): WeaponTier <c>[7:6]</c>, ElementType <c>[5:3]</c>, PrestigeBand <c>[2:1]</c>,
    /// ArmorTier <c>[0]</c>. Pure and allocation-free. Equipment Story 005.
    /// </summary>
    public static class EquipmentAppearanceEncoder
    {
        /// <summary>
        /// Returns <c>(weaponTier &amp; 0x03) &lt;&lt; 6 | (elementType &amp; 0x07) &lt;&lt; 3 |
        /// (prestigeBand &amp; 0x03) &lt;&lt; 1 | (armorBit &amp; 0x01)</c>. Each field is masked to its
        /// width, so an out-of-range value cannot spill into a neighbouring field.
        /// </summary>
        /// <param name="weaponTier">Weapon tier bits: Bronze 0 … Dark Steel 3 (<see cref="GetTierBits"/>).</param>
        /// <param name="elementType">Weapon element: the <see cref="ElementType"/> value, None 0 … Poison 4.</param>
        /// <param name="prestigeBand">The weapon's <c>PrestigeBand</c> value, 0 … 3.</param>
        /// <param name="armorBit">1 when the highest equipped armor tier is Steel or Dark Steel, else 0.</param>
        /// <returns>The packed byte.</returns>
        public static byte Encode(byte weaponTier, byte elementType, byte prestigeBand, byte armorBit)
        {
            return (byte)(((weaponTier & 0x03) << 6) | ((elementType & 0x07) << 3) | ((prestigeBand & 0x03) << 1) | (armorBit & 0x01));
        }

        /// <summary>
        /// Maps a <see cref="GearTier"/> to the 2-bit WeaponTier field. The enum starts at
        /// <see cref="GearTier.None"/> = 0, so a cast would be off by one; <see cref="GearTier.None"/>
        /// and undefined values map to 0, as an empty weapon slot does (EC-EQS-9).
        /// </summary>
        /// <param name="tier">The item's tier.</param>
        /// <returns>Bronze 0, Iron 1, Steel 2, Dark Steel 3; 0 otherwise.</returns>
        public static byte GetTierBits(GearTier tier)
        {
            switch (tier)
            {
                case GearTier.Iron: return 1;
                case GearTier.Steel: return 2;
                case GearTier.DarkSteel: return 3;
                default: return 0;
            }
        }
    }
}
