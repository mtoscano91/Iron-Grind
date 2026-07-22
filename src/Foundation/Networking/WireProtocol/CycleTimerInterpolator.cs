namespace IronGrind.Networking
{
    /// <summary>
    /// MCR-5a's client-side loss-tolerance behavior for <see cref="CycleTimerBroadcast"/> (Story 027,
    /// AC-MCR-05/AC-MCR-08): when up to 3 consecutive <see cref="CycleTimerBroadcast"/> packets are
    /// lost, the charge bar must keep advancing via linear extrapolation from the last two received
    /// <c>cycleTimer</c> values, rather than freezing or resetting to zero. A plain, injectable C#
    /// class — no <c>MonoBehaviour</c>/<c>Update()</c> dependency, matching this folder's engine-
    /// agnostic discipline (see <see cref="PriorityPathQueue{T}"/>'s own remarks on the same point).
    /// This is the first client-render-oriented logic class in this codebase; every other class in
    /// this folder is server-authoring or pure-codec.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Algorithm (networking-message-criticality.md AC-MCR-05, verbatim):</b> "linear extrapolation
    /// from the last two received <c>cycleTimer</c> values." <see cref="RecordReceivedSample"/> feeds
    /// each genuinely-received packet's <c>(serverTickNumber, cycleTimer)</c> pair;
    /// <see cref="Sample"/> computes the extrapolated position at any later tick from whichever two
    /// samples were most recently recorded (i.e. the extrapolation continues from the same rate
    /// through an arbitrarily long gap — this class does not itself limit the gap to 3 ticks; the
    /// 10% tolerance bound in AC-MCR-05 is validated at the call site, over a 3-tick gap, per the
    /// GDD's own tolerance derivation which assumes cycle speed ≤ 1.0 cycle/second).
    /// </para>
    /// <para>
    /// <b>Monotonic non-decreasing guarantee (AC-MCR-08):</b> <see cref="Sample"/> never returns a
    /// value below the most recently recorded real sample's <see cref="CycleTimerBroadcast.CycleTimer"/>
    /// — the charge bar must not freeze (it keeps advancing per the linear rate) or reset to zero
    /// during a loss window. Rate is computed wrap-aware: if the two most recent samples' raw values
    /// decreased (a genuine full-cycle rollover between them, e.g. 9800 -&gt; 150), the delta is
    /// computed across the <see cref="CYCLE_FULL"/> wrap boundary rather than read as a negative rate.
    /// </para>
    /// <para>
    /// <b>Known scope limit (honestly documented, matching this epic's convention — see
    /// <see cref="GoldSyncForcedDeliveryTracker"/>'s own remarks for the precedent):</b> a genuine
    /// full-cycle wrap occurring <i>during</i> the extrapolation window itself (as opposed to between
    /// two already-received real samples) is not specially guarded against — the extrapolated raw
    /// value is wrapped into <c>[0, CYCLE_FULL)</c> for display, which could in principle read as a
    /// large forward jump rather than the "freeze/reset" AC-MCR-08 forbids. This is out of scope for
    /// this story's two blocking ACs: at cycle speed &le; 1.0 cycle/second and a 3-tick (150ms) gap,
    /// a mid-gap wrap can only occur if the cycle was already within 150ms of completion when the
    /// loss began — the GDD's own 10% tolerance derivation does not account for this case either.
    /// </para>
    /// <para>
    /// <b>Stale/out-of-order sample guard (code review fix):</b> <see cref="CycleTimerBroadcast"/> is
    /// delivered over U-U, which ADR-004 Decision 2 documents as "best-effort, no retransmit, no
    /// ordering." <see cref="RecordReceivedSample"/> therefore rejects (no-ops) any incoming sample
    /// whose <c>serverTickNumber</c> is not strictly newer than the already-recorded latest sample —
    /// reusing <see cref="StaleDiscardComparer.IsNewerVersion"/> directly, the same wraparound-safe
    /// helper this codebase uses for every other reordering hazard. Without this guard, a reordered
    /// stale packet arriving after a newer one would overwrite <c>_latest</c> with an older, lower
    /// value, and <see cref="Sample"/>'s subsequent extrapolation would visibly move the charge bar
    /// backward — a direct violation of AC-MCR-08's monotonic-non-decreasing guarantee.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var interpolator = new CycleTimerInterpolator();
    /// interpolator.RecordReceivedSample(serverTickNumber: 100u, cycleTimer: 1000);
    /// interpolator.RecordReceivedSample(serverTickNumber: 101u, cycleTimer: 1200); // rate = 200/tick
    ///
    /// // Ticks 102-104 are lost — no RecordReceivedSample calls for them.
    /// ushort extrapolated102 = interpolator.Sample(102u); // 1400
    /// ushort extrapolated103 = interpolator.Sample(103u); // 1600
    /// ushort extrapolated104 = interpolator.Sample(104u); // 1800
    ///
    /// // Tick 105's packet finally arrives.
    /// interpolator.RecordReceivedSample(serverTickNumber: 105u, cycleTimer: 2010);
    /// </code>
    /// </example>
    public sealed class CycleTimerInterpolator
    {
        /// <summary>
        /// <see cref="CycleTimerBroadcast.CycleTimer"/>'s normalized full-cycle value (CR-NET-7's
        /// Message Schemas section: 0–10,000 fraction).
        /// </summary>
        public const ushort CYCLE_FULL = 10000;

        private (uint tick, ushort value)? _previous;
        private (uint tick, ushort value)? _latest;

        /// <summary>
        /// Records a genuinely-received <see cref="CycleTimerBroadcast"/> sample. Call this only for
        /// packets that actually arrived — never for a synthetic/extrapolated value. A sample whose
        /// <paramref name="serverTickNumber"/> is not strictly newer than the already-recorded latest
        /// sample (a reordered or duplicate U-U delivery — see class remarks) is silently discarded;
        /// it does not become <c>_latest</c> and does not shift the current <c>_latest</c> into
        /// <c>_previous</c>.
        /// </summary>
        /// <param name="serverTickNumber">The tick this sample was authored on (matches the sub-message's authoring tick).</param>
        /// <param name="cycleTimer">The received <see cref="CycleTimerBroadcast.CycleTimer"/> value.</param>
        /// <example>
        /// <code>
        /// interpolator.RecordReceivedSample(100u, 1000);
        /// interpolator.RecordReceivedSample(105u, 2010);
        /// interpolator.RecordReceivedSample(103u, 1800); // reordered/stale — discarded, no-op
        /// </code>
        /// </example>
        public void RecordReceivedSample(uint serverTickNumber, ushort cycleTimer)
        {
            if (_latest.HasValue && !StaleDiscardComparer.IsNewerVersion(current: _latest.Value.tick, candidate: serverTickNumber))
            {
                // Reordered or duplicate U-U delivery (ADR-004 Decision 2: "no ordering") — discard
                // rather than regress the charge bar backward (AC-MCR-08).
                return;
            }

            _previous = _latest;
            _latest = (serverTickNumber, cycleTimer);
        }

        /// <summary>
        /// Returns the extrapolated (or, if <paramref name="currentTick"/> is at or before the latest
        /// received sample's tick, the latest received) <c>cycleTimer</c> value at
        /// <paramref name="currentTick"/>. Returns <c>0</c> if no sample has ever been recorded, and
        /// the latest received value verbatim if only one sample has been recorded (no rate to
        /// extrapolate from yet).
        /// </summary>
        /// <param name="currentTick">The tick to sample the (possibly extrapolated) cycle position at.</param>
        /// <example>
        /// <code>ushort position = interpolator.Sample(currentTick: 103u);</code>
        /// </example>
        public ushort Sample(uint currentTick)
        {
            if (_latest is null)
            {
                return 0;
            }

            (uint latestTick, ushort latestValue) = _latest.Value;

            if (_previous is null)
            {
                return latestValue;
            }

            (uint previousTick, ushort previousValue) = _previous.Value;

            if (IsAtOrBefore(currentTick, latestTick))
            {
                return latestValue;
            }

            // RFC 1982 wraparound-safe tick-delta arithmetic (CR-NET-7.5 discipline, matching
            // StaleDiscardComparer's own (uint)(candidate - current) pattern) — a plain "latestTick -
            // previousTick" would silently produce a huge, wrong value if the uint tick counter itself
            // had wrapped between the two samples.
            long tickDelta = (uint)(latestTick - previousTick);
            if (tickDelta <= 0)
            {
                // Degenerate (duplicate or out-of-order tick numbers) — no meaningful rate; hold the latest value.
                return latestValue;
            }

            long valueDelta = latestValue - previousValue;
            if (valueDelta < 0)
            {
                // A genuine full-cycle rollover occurred between the two most recent real samples —
                // compute the delta across the wrap boundary rather than treat it as a negative rate.
                valueDelta += CYCLE_FULL;
            }

            double ratePerTick = (double)valueDelta / tickDelta;
            long elapsed = (uint)(currentTick - latestTick);
            double extrapolatedRaw = latestValue + (ratePerTick * elapsed);

            // AC-MCR-08: never return below the last real value while extrapolating forward.
            if (extrapolatedRaw < latestValue)
            {
                extrapolatedRaw = latestValue;
            }

            double wrapped = extrapolatedRaw % CYCLE_FULL;
            return (ushort)wrapped;
        }

        /// <summary>
        /// RFC1982-safe "at or before" check for tick numbers, expressed via
        /// <see cref="StaleDiscardComparer.IsTickExpired"/> (reused directly): true when
        /// <paramref name="currentTick"/> has not yet advanced past <paramref name="latestTick"/>.
        /// </summary>
        private static bool IsAtOrBefore(uint currentTick, uint latestTick)
            => !StaleDiscardComparer.IsNewerVersion(current: latestTick, candidate: currentTick);
    }
}
