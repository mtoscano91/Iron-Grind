using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// The design pillars a wire message may serve (MCR-1, <c>networking-message-criticality.md</c>).
    /// A message may be tagged with more than one pillar (e.g. <c>KillEvent</c> is Pillar 1 + Pillar
    /// 3) — see <see cref="MessageRoutingRegistry.ResolveMultiPillarChannel"/> for how a
    /// multiple-pillar tag resolves to a single <see cref="NetworkChannel"/> (MCR-3).
    /// </summary>
    /// <remarks>
    /// <c>[Flags]</c> because MCR-2 rows are frequently tagged with more than one pillar
    /// simultaneously (e.g. <c>PartyMemberHealthUpdate</c> is Pillar 1 + Pillar 3). Declared
    /// <see langword="byte"/>-backed to match this folder's other small enums
    /// (<see cref="NetworkChannel"/>, <see cref="MessageDirection"/>, <see cref="MessageDeliveryContext"/>).
    /// </remarks>
    /// <example>
    /// <code>
    /// DesignPillar killEventPillars = DesignPillar.EarnedPower | DesignPillar.SocialSignals;
    /// </code>
    /// </example>
    [Flags]
    public enum DesignPillar : byte
    {
        /// <summary>No pillar tag. Not a valid tag for any real MCR-2 row — reserved as the default/uninitialized value.</summary>
        None = 0,

        /// <summary>Pillar 1 — Earned Power. Permanent state changes reflecting player decisions (gold, stats, items, kills).</summary>
        EarnedPower = 1 << 0,

        /// <summary>Pillar 2 — Rhythm Mastery. Real-time feedback enabling precise timing.</summary>
        RhythmMastery = 1 << 1,

        /// <summary>Pillar 3 — Social Signals. Status/presence signals that make the world feel alive.</summary>
        SocialSignals = 1 << 2,

        /// <summary>Infrastructure. Transport and session lifecycle — no pillar ownership.</summary>
        Infrastructure = 1 << 3,
    }
}
