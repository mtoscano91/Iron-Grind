using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Per-connection skip-on-activity heartbeat scheduling state (CR-NET-7.10). Tracks the tick of
    /// the most recent outbound packet on this connection — of any type, not just heartbeats — and
    /// answers "is a <see cref="HeartbeatMessage"/> due yet?" against a caller-supplied tick
    /// interval. One instance belongs to exactly one client connection, the same per-connection,
    /// stateful-tracker shape as <see cref="PriorityPathQueue{T}"/> (Story 006).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Tick-count-only scope (no <c>TICK_RATE_HZ</c> dependency):</b> <c>ServerTickLoop.TICK_RATE_HZ</c>
    /// (the 20 Hz server tick rate, <c>src/Foundation/Networking/TickLoop/ServerTickLoop.cs</c>)
    /// now exists as of Story 009 ("Fixed 20Hz Server Tick Loop"), but this class is still not wired
    /// to it — that integration remains a future story's job. This class therefore never converts
    /// <see cref="HEARTBEAT_INTERVAL_SECONDS"/> to a tick count itself; <see cref="IsHeartbeatDue"/>
    /// takes an explicit <c>intervalTicks</c> parameter, and computing that value (conceptually
    /// <c>intervalTicks = HEARTBEAT_INTERVAL_SECONDS * ServerTickLoop.TICK_RATE_HZ</c>) is left to
    /// whichever future story integrates this tracker with the real tick loop — mirroring
    /// <see cref="PriorityPathQueue{T}"/>'s own precedent of pure tick-count logic that does not
    /// assume a running tick loop exists yet.
    /// </para>
    /// <para>
    /// <b>Skip-on-activity falls out of a single field, by design:</b> <see cref="RecordOutboundPacket"/>
    /// is intended to be called for every outbound packet on the connection — including the
    /// heartbeat itself, once it is actually sent. Because sending the heartbeat re-records the
    /// "last outbound" tick through the very same method, the "not a second heartbeat until another
    /// full interval of silence" requirement (AC-NC-38) requires no extra state beyond the single
    /// <c>_lastOutboundPacketTick</c> field — it is the identical code path whether the most recent
    /// outbound packet was an RPC (e.g. <c>NotifySkillUsed</c>) or a heartbeat.
    /// </para>
    /// <para>
    /// <b>Wraparound-safe, never wall-clock:</b> <see cref="IsHeartbeatDue"/> is built directly on
    /// <see cref="StaleDiscardComparer.IsTickExpired"/> (Story 005's RFC-1982 comparison primitive)
    /// — never <see cref="DateTime.UtcNow"/>, per this folder's stale-discard/TTL convention (see
    /// <see cref="StaleDiscardComparer"/> remarks). Equality is boundary-inclusive: a heartbeat
    /// becomes due at exactly <c>currentTick == lastOutboundPacketTick + intervalTicks</c>, not one
    /// tick later (the same equality-is-expired semantics <see cref="StaleDiscardComparer.IsTickExpired"/>
    /// already documents).
    /// </para>
    /// <para>
    /// <b>Default state before any packet is ever recorded:</b> the backing field defaults to
    /// <c>0</c>, equivalent to treating tick <c>0</c> as an implicit first "last outbound" tick —
    /// <see cref="IsHeartbeatDue"/> is <see langword="false"/> for any <c>currentTick</c> in
    /// <c>[0, intervalTicks)</c> and <see langword="true"/> from <c>intervalTicks</c> onward, with
    /// no separate "never recorded" flag. This is a deliberate simplicity judgment call: a real
    /// connection is expected to call <see cref="RecordOutboundPacket"/> at least once at
    /// connection-establishment time (a future story's concern, out of scope here), so this default
    /// is not expected to be observed in production — it exists only to give a brand-new,
    /// never-recorded tracker a well-defined, deterministic answer rather than an undefined one.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var tracker = new HeartbeatActivityTracker();
    /// tracker.RecordOutboundPacket(tickNumber: 100u); // NotifySkillUsed sent at tick 100
    ///
    /// bool dueAtSameTick = tracker.IsHeartbeatDue(currentTick: 100u, intervalTicks: 60u); // false
    /// bool dueAtBoundary = tracker.IsHeartbeatDue(currentTick: 160u, intervalTicks: 60u);  // true (100+60)
    ///
    /// // sending the heartbeat itself resets the counter — no extra state needed:
    /// tracker.RecordOutboundPacket(tickNumber: 160u);
    /// bool dueRightAfter = tracker.IsHeartbeatDue(currentTick: 160u, intervalTicks: 60u); // false again
    /// </code>
    /// </example>
    public sealed class HeartbeatActivityTracker
    {
        /// <summary>
        /// GDD tuning knob (CR-NET-7.10): seconds between client heartbeat sends when no other
        /// outbound packet was sent in the preceding interval. Default <c>3</c>, safe range
        /// <c>[1, 10]</c>. Declared here for discoverability, matching
        /// <see cref="PriorityPathQueue{T}.PRIORITY_PATH_CAP"/>'s precedent of declaring a
        /// governing tuning-knob constant directly on the class it governs rather than reading it
        /// from external config. <b>Not consumed internally by this class</b> — see the class
        /// remarks on tick-count-only scope; this constant exists purely as documentation of the
        /// GDD value, pending the seconds-to-ticks conversion a future tick-loop integration will
        /// perform.
        /// </summary>
        public const int HEARTBEAT_INTERVAL_SECONDS = 3;

        private uint _lastOutboundPacketTick;

        /// <summary>
        /// Records that an outbound packet of any type — including the heartbeat itself — was sent
        /// on this connection at <paramref name="tickNumber"/>. Resets the skip-on-activity window.
        /// </summary>
        /// <param name="tickNumber">The server tick at which the packet was sent.</param>
        /// <example>
        /// <code>
        /// tracker.RecordOutboundPacket(tickNumber: currentTick); // called for every outbound packet, any type
        /// </code>
        /// </example>
        public void RecordOutboundPacket(uint tickNumber)
        {
            _lastOutboundPacketTick = tickNumber;
        }

        /// <summary>
        /// Returns <see langword="true"/> if a <see cref="HeartbeatMessage"/> is due: no outbound
        /// packet has been recorded on this connection for at least <paramref name="intervalTicks"/>
        /// ticks (boundary-inclusive — see class remarks). Wraparound-safe via
        /// <see cref="StaleDiscardComparer.IsTickExpired"/>.
        /// </summary>
        /// <param name="currentTick">The current server tick.</param>
        /// <param name="intervalTicks">
        /// The heartbeat interval expressed as a tick count — the caller's responsibility to derive
        /// from <see cref="HEARTBEAT_INTERVAL_SECONDS"/> and the real tick rate (see class remarks).
        /// </param>
        /// <example>
        /// <code>
        /// if (tracker.IsHeartbeatDue(currentTick, intervalTicks: 60u))
        /// {
        ///     SendHeartbeat();
        ///     tracker.RecordOutboundPacket(currentTick);
        /// }
        /// </code>
        /// </example>
        public bool IsHeartbeatDue(uint currentTick, uint intervalTicks)
        {
            uint expiryTick = _lastOutboundPacketTick + intervalTicks;
            return StaleDiscardComparer.IsTickExpired(currentTick, expiryTick);
        }
    }
}
