namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Weapon prestige band (CR-ENH-12). The numeric values are the wire bits of
    /// <c>equipmentAppearanceFlags[2:1]</c> (equipment-system.md CR-EQS-11). Level ranges shown
    /// are for the default thresholds 5 / 7 / 8.
    /// </summary>
    public enum PrestigeBand : byte
    {
        /// <summary>Levels 0-4 (below the mid threshold). Bits 0b00.</summary>
        None = 0,

        /// <summary>Levels 5-6 (mid threshold up to the glow threshold). Bits 0b01.</summary>
        VisibleNoGlow = 1,

        /// <summary>Level 7 (glow threshold up to the high threshold). Bits 0b10.</summary>
        GlowLow = 2,

        /// <summary>Levels 8-10 (high threshold and above). Bits 0b11.</summary>
        High = 3,
    }
}
