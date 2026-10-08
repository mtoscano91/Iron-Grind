using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// The sole sanctioned call site for emitting a <see cref="SelfDamageEvent"/> (Story 027,
    /// EC-CCR-2 / MCR-2). Exists to make the singleton-recipient-set invariant — "sent to the
    /// attacker's own client exclusively" — <b>structural</b> rather than conventional: this class's
    /// only public method accepts a single <see cref="EntityID"/> as the recipient, never a
    /// collection or a predicate to filter a broadcast list down to one entry. A future refactor
    /// cannot accidentally widen the recipient set through this call site, because there is no
    /// parameter shape here that could hold more than one recipient.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this can't just be a check inside <see cref="SelfDamageEventCodec.Write"/>:</b> the
    /// codec's job is wire encoding, not recipient-set policy — mirroring the separation this folder
    /// already draws between <see cref="GoldSyncForcedDeliveryCodec"/> (encode) and
    /// <see cref="GoldSyncForcedDeliveryTracker"/> (policy: when to fire). Here the "policy" is
    /// trivial (always exactly the attacker) but still deserves its own named, narrowly-typed call
    /// site per the story's explicit implementation note: "Constructing it as a filter risks a
    /// future refactor accidentally widening the filter; constructing it as an explicit singleton
    /// makes the exclusivity structural."
    /// </para>
    /// <para>
    /// <b>Defensive assertion:</b> <see cref="SerializeForAttacker"/> throws if the supplied
    /// <paramref name="attackerEntityId"/> parameter (the intended sole recipient) does not match
    /// <c>selfDamageEvent.AttackerEntityId</c> (the payload's own attacker field) — two independent
    /// inputs must agree before anything is written to the wire, catching a caller-side mismatch at
    /// the earliest possible point rather than silently trusting the payload.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var payload = new DamageEvent(attackerEntityId, targetEntityId, finalDamage: 42, isCrit: true, DamageType.Physical);
    /// Span&lt;byte&gt; buffer = stackalloc byte[SelfDamageEvent.WireSize];
    /// int written = SelfDamageEventDispatcher.SerializeForAttacker(buffer, sequenceNumber: 9u, tickNumber: 1003u, attackerEntityId, in payload);
    /// // 'buffer' is destined for attackerEntityId's client alone — no other recipient is representable here.
    /// </code>
    /// </example>
    public static class SelfDamageEventDispatcher
    {
        /// <summary>
        /// Serializes <paramref name="selfDamageEvent"/> as a standalone R-OD <see cref="SelfDamageEvent"/>
        /// addressed to exactly one recipient, <paramref name="attackerEntityId"/> — the only
        /// parameter shape this method exposes for "who receives this," per the class's structural
        /// singleton-recipient guarantee.
        /// </summary>
        /// <param name="destination">The destination buffer — must be at least <see cref="SelfDamageEvent.WireSize"/> bytes.</param>
        /// <param name="sequenceNumber">The outbound envelope <c>SequenceNumber</c> for this message, scoped to <paramref name="attackerEntityId"/>'s connection.</param>
        /// <param name="tickNumber">The server tick this <see cref="SelfDamageEvent"/> was authored on.</param>
        /// <param name="attackerEntityId">
        /// The sole recipient of this message (EC-CCR-2: "the recipient set for SelfDamageEvent must
        /// be constructed as a singleton {attackerEntityId} — not derived from the zone-wide
        /// broadcast list"). Must equal <c>selfDamageEvent.AttackerEntityId</c>.
        /// </param>
        /// <param name="selfDamageEvent">The attacker/target/damage/crit/type tuple to serialize.</param>
        /// <returns><see cref="SelfDamageEvent.WireSize"/> (24), always.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="attackerEntityId"/> does not match
        /// <c>selfDamageEvent.AttackerEntityId</c> — a caller-side construction bug, not a recoverable
        /// network condition.
        /// </exception>
        /// <example>
        /// <code>
        /// int written = SelfDamageEventDispatcher.SerializeForAttacker(buffer, 9u, 1003u, attackerEntityId, in payload);
        /// </code>
        /// </example>
        public static int SerializeForAttacker(
            Span<byte> destination,
            uint sequenceNumber,
            uint tickNumber,
            EntityID attackerEntityId,
            in DamageEvent selfDamageEvent
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (selfDamageEvent.AttackerEntityId != attackerEntityId)
            {
                throw new ArgumentException(
                    $"SelfDamageEventDispatcher: recipient EntityID ({attackerEntityId}) does not match " +
                    $"selfDamageEvent.AttackerEntityId ({selfDamageEvent.AttackerEntityId}) — the sole recipient " +
                    "of a SelfDamageEvent must always be the attacker themselves (EC-CCR-2).",
                    nameof(attackerEntityId));
            }

            return SelfDamageEventCodec.Write(
                destination, sequenceNumber, tickNumber, in selfDamageEvent
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                , observer
#endif
                );
        }
    }
}
