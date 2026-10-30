namespace IronGrind.Networking
{
    /// <summary>
    /// Outcome of a Hit Detection check (design/gdd/hit-detection.md CR-HD-1). Backed by
    /// <see cref="byte"/> for wire compactness, matching <see cref="DamageType"/>. Lives in Foundation
    /// so wire messages can carry it without referencing server-only assemblies.
    /// </summary>
    public enum HitResult : byte
    {
        /// <summary>The target is in range and inside the facing cone; the attack connects.</summary>
        Hit = 0,

        /// <summary>The target is farther than the attack range on the ground plane.</summary>
        MissOutOfRange = 1,

        /// <summary>The target is in range but outside the attacker's facing cone.</summary>
        MissFacing = 2,
    }
}
