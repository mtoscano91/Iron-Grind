namespace IronGrind.Networking
{
    /// <summary>
    /// Wire-transmitted damage classification (CR-NET-7.9), carried on <c>DamageEvent</c>
    /// messages. Backed by <see cref="byte"/> for wire compactness and IL2CPP efficiency,
    /// matching the convention used by <see cref="SessionState"/>/<see cref="ZoneState"/>.
    /// </summary>
    /// <remarks>
    /// Receivers must range-check the received byte before casting — never
    /// <see cref="System.Enum.IsDefined(System.Type, object)"/> (forbidden on IL2CPP, CR-NET-7.4).
    /// See <see cref="WireEnumCodec.DecodeDamageType"/> for the
    /// guarded decode path: an out-of-range byte substitutes <see cref="Physical"/> and logs an
    /// anomaly rather than dropping the message.
    /// </remarks>
    public enum DamageType : byte
    {
        /// <summary>Standard physical damage, reduced by Defense.</summary>
        Physical = 0,

        /// <summary>Magical damage. Reduction rules are defined by the Combat GDD.</summary>
        Magical = 1,

        /// <summary>Ignores defense entirely. Reserved for future use.</summary>
        True = 2,
    }
}
