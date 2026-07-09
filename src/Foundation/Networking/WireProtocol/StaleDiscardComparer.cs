namespace IronGrind.Networking
{
    /// <summary>
    /// RFC 1982 serial-number-arithmetic comparison helpers for wraparound-safe <c>uint</c>
    /// version/sequence/tick comparisons (CR-NET-7.5). Every stale-state-discard check in the
    /// project — envelope <c>SequenceNumber</c>, <c>GoldSyncEvent.Version</c>, any future
    /// versioned-state message, and TTL-expiry checks against <c>ServerTickNumber</c> — must route
    /// through <see cref="IsNewerVersion"/> or <see cref="IsTickExpired"/> rather than a plain
    /// <c>&gt;</c>/<c>&gt;=</c> comparison on the raw value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why not a plain comparison:</b> <c>uint</c> version/sequence counters wrap from
    /// <see cref="System.UInt32.MaxValue"/> back to a small value once a connection has been open
    /// long enough. A raw comparison like <c>candidate &gt; current</c> treats a freshly-wrapped
    /// small value as "older" than a pre-wrap large value, which is backwards — the wrapped value
    /// was produced later in real time. RFC 1982 serial-number arithmetic treats the unsigned
    /// difference as a signed half-circle: any difference whose top bit is unset (i.e. less than
    /// <c>0x80000000</c>) means the second operand is ahead of the first in sequence, regardless of
    /// where the wraparound boundary falls.
    /// </para>
    /// <para>
    /// <b>Two different equality semantics — do not conflate them</b> (CR-NET-7.5,
    /// <c>networking-session.md</c>): <see cref="IsNewerVersion"/> returns <see langword="false"/>
    /// at equality (an identical version is not "newer," so a duplicate/replayed message is
    /// correctly treated as stale and discarded). <see cref="IsTickExpired"/> returns
    /// <see langword="true"/> at equality (a TTL that expires exactly "now" has expired, not one
    /// tick away from expiring).
    /// </para>
    /// <para>
    /// <c>SequenceNumber</c> starts at <c>1</c> per connection; <c>0</c> is reserved as
    /// "uninitialized" and must never appear in a valid message (CR-NET-7.5). This class does not
    /// itself enforce that invariant — it is a producer/receiver-side concern outside these pure
    /// comparison helpers — but callers comparing a freshly-received <c>SequenceNumber</c> should
    /// treat an observed <c>0</c> as a protocol violation, not merely "not newer."
    /// </para>
    /// <para>
    /// <see cref="System.DateTime.UtcNow"/> must never be used anywhere in a stale-discard or
    /// TTL-expiry check — wall-clock time is not synchronized with the server tick loop and
    /// introduces a correctness hazard across restarts and clock skew (<c>networking-session.md</c>
    /// EC-TOK-4).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // GoldSyncEvent stale-discard: a Version=5 update arrives after Version=6 was already applied.
    /// if (StaleDiscardComparer.IsNewerVersion(current: appliedVersion, candidate: incomingVersion))
    /// {
    ///     ApplyGoldSyncEvent(incoming);
    /// }
    /// // else: incoming is stale (older or duplicate) — discard.
    ///
    /// // TTL expiry against the server tick loop (Story 009 owns producing currentTick).
    /// if (StaleDiscardComparer.IsTickExpired(currentTick, expiryTick))
    /// {
    ///     ExpireToken();
    /// }
    /// </code>
    /// </example>
    public static class StaleDiscardComparer
    {
        /// <summary>
        /// RFC 1982 serial-number comparison: <see langword="true"/> when <paramref name="candidate"/>
        /// is strictly newer than <paramref name="current"/>, correctly handling <c>uint</c>
        /// wraparound. Equality returns <see langword="false"/> (not newer) — a duplicate/replayed
        /// value must be discarded as stale, not re-applied.
        /// </summary>
        /// <param name="current">The already-applied version/sequence value.</param>
        /// <param name="candidate">The newly-received value to test against <paramref name="current"/>.</param>
        /// <returns><see langword="true"/> if <paramref name="candidate"/> should replace <paramref name="current"/>.</returns>
        /// <example>
        /// <code>
        /// StaleDiscardComparer.IsNewerVersion(current: 6, candidate: 5); // false — 5 is stale, discard
        /// StaleDiscardComparer.IsNewerVersion(current: 4294967295, candidate: 1); // true — wraparound-safe
        /// </code>
        /// </example>
        public static bool IsNewerVersion(uint current, uint candidate) =>
            (uint)(candidate - current) < 0x80000000u && candidate != current;

        /// <summary>
        /// RFC 1982 serial-number comparison: <see langword="true"/> when <paramref name="currentTick"/>
        /// is at or past <paramref name="expiryTick"/>, correctly handling <c>uint</c> wraparound.
        /// Equality returns <see langword="true"/> (expired) — the opposite of
        /// <see cref="IsNewerVersion"/>'s equality behavior.
        /// </summary>
        /// <param name="currentTick">The current server tick.</param>
        /// <param name="expiryTick">The tick at which the value expires.</param>
        /// <returns><see langword="true"/> if the value has expired as of <paramref name="currentTick"/>.</returns>
        /// <example>
        /// <code>
        /// StaleDiscardComparer.IsTickExpired(currentTick: 100, expiryTick: 100); // true — equality = expired
        /// StaleDiscardComparer.IsTickExpired(currentTick: 3, expiryTick: uint.MaxValue - 2); // true — wraparound-safe
        /// </code>
        /// </example>
        public static bool IsTickExpired(uint currentTick, uint expiryTick) =>
            (uint)(currentTick - expiryTick) < 0x80000000u;
    }
}
