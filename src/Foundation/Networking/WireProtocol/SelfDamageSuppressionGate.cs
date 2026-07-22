namespace IronGrind.Networking
{
    /// <summary>
    /// EC-MCR-2's client-side suppression rules for <see cref="SelfDamageEvent"/> (Story 027): the
    /// attacker's client must never show a phantom damage number, whether the message never arrives
    /// in time or arrives too stale to trust. A pure, stateless query class — the caller supplies
    /// whatever tick bookkeeping it already has (triggering Beat tick, arrived event's
    /// <c>ServerTickNumber</c>, current tick); this class holds no state of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two independent suppression checks, both reusing <see cref="StaleDiscardComparer.IsTickExpired"/>
    /// directly (never reimplemented)</b> per this project's stale-discard discipline (CR-NET-7.5):
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <b><see cref="HasSuppressionWindowElapsed"/></b> — non-arrival timeout. If 5 tick periods
    /// (250ms at 20Hz) have passed since the triggering Beat with no <see cref="SelfDamageEvent"/>
    /// received, the client must suppress the damage number entirely — critically, <i>never</i>
    /// falling back to <see cref="DamageEvent"/> (structurally impossible anyway: MCR-2 routes
    /// <see cref="DamageEvent"/> to every zone client except the attacker, so a
    /// <see cref="DamageEvent"/> matching the attacker's own ID never arrives at the attacker's
    /// client — EC-MCR-2's own wording).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b><see cref="IsStaleOnArrival"/></b> — a <see cref="SelfDamageEvent"/> that DOES arrive, but
    /// whose <c>ServerTickNumber</c> is more than 5 ticks older than the current server tick, must
    /// also be suppressed (a stale/delayed-transport delivery, not a fresh hit).
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Parameter order matters — <see cref="StaleDiscardComparer.IsTickExpired"/> takes
    /// <c>(currentTick, expiryTick)</c>, current first.</b> Both methods below pass the caller's
    /// <c>currentTick</c> into the first slot — transposing the two arguments would silently invert
    /// the suppression logic (treating "not yet due" as "already expired" and vice versa).
    /// </para>
    /// <para>
    /// <b>The two methods deliberately use DIFFERENT <c>expiryTick</c> offsets — this is intentional,
    /// not an inconsistency to "fix" toward matching each other.</b> EC-MCR-2's own wording gives each
    /// rule a different boundary qualifier:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Rule 1 ("does not receive... <i>within</i> 5 tick periods... suppress") has no strict
    /// qualifier — the grace window is ticks 0-5 inclusive, so exactly 5 elapsed ticks with nothing
    /// received already means suppress. <see cref="HasSuppressionWindowElapsed"/> therefore uses
    /// <c>expiryTick = triggerBeatTick + SUPPRESSION_WINDOW_TICKS</c> directly, relying on
    /// <see cref="StaleDiscardComparer.IsTickExpired"/>'s native "true at equality" semantics.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Rule 2 ("suppress any SelfDamageEvent whose ServerTickNumber is <i>more than</i> 5 ticks
    /// older") has an explicit strict-inequality qualifier — exactly 5 ticks stale must NOT be
    /// suppressed; only 6+ ticks stale must. <see cref="IsStaleOnArrival"/> therefore computes
    /// <c>expiryTick = eventServerTickNumber + SUPPRESSION_WINDOW_TICKS + 1</c> before calling
    /// <see cref="StaleDiscardComparer.IsTickExpired"/> — the <c>+1</c> converts the helper's native
    /// <c>&gt;=</c> equality-is-expired behavior into the strict <c>&gt;</c> ("more than 5") the GDD
    /// requires, without reimplementing the comparison itself.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Non-arrival: 6 ticks have passed since the triggering Beat with nothing received yet.
    /// bool suppress = SelfDamageSuppressionGate.HasSuppressionWindowElapsed(triggerBeatTick: 1000u, currentTick: 1006u); // true
    ///
    /// // Stale-on-arrival: the event finally arrives, but it's tagged tick 1000 while we're on tick 1007
    /// // (7 ticks stale, which is "more than 5").
    /// bool stale = SelfDamageSuppressionGate.IsStaleOnArrival(eventServerTickNumber: 1000u, currentTick: 1007u); // true
    ///
    /// // Exactly 5 ticks stale is NOT "more than 5" — must not be suppressed.
    /// bool notStale = SelfDamageSuppressionGate.IsStaleOnArrival(eventServerTickNumber: 1000u, currentTick: 1005u); // false
    /// </code>
    /// </example>
    public static class SelfDamageSuppressionGate
    {
        /// <summary>
        /// EC-MCR-2's suppression window: 5 tick periods (250ms at the fixed 20Hz tick rate).
        /// </summary>
        public const uint SUPPRESSION_WINDOW_TICKS = 5;

        /// <summary>
        /// Returns <see langword="true"/> once <see cref="SUPPRESSION_WINDOW_TICKS"/> have elapsed
        /// since <paramref name="triggerBeatTick"/> with no <see cref="SelfDamageEvent"/> received —
        /// the client must suppress the damage number entirely (EC-MCR-2).
        /// </summary>
        /// <param name="triggerBeatTick">The server tick the triggering Beat resolution occurred on.</param>
        /// <param name="currentTick">The client's current server tick.</param>
        /// <example>
        /// <code>SelfDamageSuppressionGate.HasSuppressionWindowElapsed(1000u, 1006u); // true — 6 &gt; 5 ticks elapsed</code>
        /// </example>
        public static bool HasSuppressionWindowElapsed(uint triggerBeatTick, uint currentTick)
        {
            uint expiryTick = triggerBeatTick + SUPPRESSION_WINDOW_TICKS;
            return StaleDiscardComparer.IsTickExpired(currentTick, expiryTick);
        }

        /// <summary>
        /// Returns <see langword="true"/> if a <i>received</i> <see cref="SelfDamageEvent"/>'s
        /// <c>ServerTickNumber</c> is <b>more than</b> <see cref="SUPPRESSION_WINDOW_TICKS"/> ticks
        /// older than <paramref name="currentTick"/> (strict inequality — exactly
        /// <see cref="SUPPRESSION_WINDOW_TICKS"/> ticks stale is NOT suppressed, per EC-MCR-2's
        /// literal "more than 5 ticks" wording) — the client must suppress the damage number even
        /// though the message did arrive (EC-MCR-2's second suppression rule).
        /// </summary>
        /// <param name="eventServerTickNumber">The arrived <see cref="SelfDamageEvent"/>'s envelope <c>ServerTickNumber</c>.</param>
        /// <param name="currentTick">The client's current server tick.</param>
        /// <example>
        /// <code>
        /// SelfDamageSuppressionGate.IsStaleOnArrival(1000u, 1005u); // false — exactly 5 ticks stale, not "more than 5"
        /// SelfDamageSuppressionGate.IsStaleOnArrival(1000u, 1006u); // true — 6 &gt; 5 ticks stale
        /// </code>
        /// </example>
        public static bool IsStaleOnArrival(uint eventServerTickNumber, uint currentTick)
        {
            // +1 (unlike HasSuppressionWindowElapsed) converts IsTickExpired's native ">=" equality-
            // is-expired semantics into the strict ">" the GDD's "more than 5 ticks" wording requires
            // — see class remarks for why the two methods intentionally differ here.
            uint expiryTick = eventServerTickNumber + SUPPRESSION_WINDOW_TICKS + 1;
            return StaleDiscardComparer.IsTickExpired(currentTick, expiryTick);
        }
    }
}
