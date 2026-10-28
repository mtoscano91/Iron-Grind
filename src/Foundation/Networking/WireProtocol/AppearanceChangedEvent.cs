namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-all-zone-clients notice that a character's <c>equipmentAppearanceFlags</c> byte changed.
    /// <b>Wire name: <c>AppearanceChangedEvent</c></b> (networking-wire-protocol.md; equipment-system.md CR-EQS-11;
    /// networking-channel-contract.md CCR-3): <c>{ uint entityId; byte equipmentAppearanceFlags; }</c> — 5-byte body,
    /// S→ALL (this zone's clients only), R-OD priority path, not cap-exempt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The byte is absolute (the new value, not a delta), so a receiver replaces what it holds for <c>entityId</c>.
    /// A joining player's initial byte travels in the zone snapshot (<c>EntityState.equipmentAppearanceFlags</c>), not here.
    /// </para>
    /// <para><see cref="MessageTypeId"/> <c>0xE142</c> — readiness decision 2026-10-28 (Equipment Story 009).</para>
    /// </remarks>
    public readonly struct AppearanceChangedEvent
    {
        /// <summary>Wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE142;

        /// <summary>Wire size of the body only: 4 + 1 = 5 bytes.</summary>
        public const int BodySize = 5;
    }
}
