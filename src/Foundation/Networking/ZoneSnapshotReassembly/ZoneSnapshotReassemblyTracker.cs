using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// Tracks per-client <c>ZoneStateSnapshot</c> fragment-reassembly progress during zone entry,
    /// proving the B-NP-8 retransmit-and-timeout contract (Networking Core Story 015, AC-NC-35 /
    /// AC-NC-40): a client waiting on a dropped fragment retransmits up to
    /// <see cref="MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS"/> times, the zone-entry gate stays closed for the
    /// entire wait, and after the final attempt is exhausted the client gives up rather than stalling
    /// forever.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stateful, per-client registry — same shape as <see cref="ConnectionStateMachine"/>, not
    /// <see cref="CommitBeforeBroadcastSequencer"/>'s stateless-static shape:</b> reassembly progress
    /// (attempt count, gate-open/exhausted flags, the timeout baseline tick) is inherently state that
    /// must survive across separate <see cref="EvaluateReassemblyTimeout"/> calls for the same client,
    /// so this class owns an in-memory <see cref="Dictionary{TKey,TValue}"/> registry keyed by
    /// <c>clientId</c>, exactly like <see cref="ConnectionStateMachine"/>'s own account registry.
    /// </para>
    /// <para>
    /// <b>Tick-driven, no wall-clock, no <see cref="System.Threading.Thread.Sleep"/>:</b>
    /// <see cref="EvaluateReassemblyTimeout"/> takes <c>currentTick</c> and a caller-supplied
    /// <c>reassemblyTimeoutTicks</c> — never a hardcoded <c>FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS</c>
    /// constant — matching <see cref="ConnectionStateMachine.EvaluateTimeouts"/>'s
    /// <c>heartbeatTimeoutTicks</c>/<c>connectingTimeoutTicks</c> precedent: the GDD's own default (10s)
    /// and safe range ([5, 30]) are documentation only, never baked into this class.
    /// </para>
    /// <para>
    /// <b><see cref="MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS"/> is declared here, provisionally</b> — the one
    /// value AC-NC-40 itself pins to a concrete literal (3), following
    /// <see cref="CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS"/>'s precedent (Story 011) for a
    /// GDD-tunable value an AC requires as a concrete number, pending a real Networking Core zone-entry
    /// story that owns its full lifecycle.
    /// </para>
    /// <para>
    /// <b>"Client gives up" is an observable query, not a disconnect:</b> this class does not own
    /// transport disconnection (out of scope — a future zone-entry orchestration story owns wiring a
    /// real disconnect-and-reconnect to <see cref="HasExhaustedRetransmitAttempts"/> returning
    /// <see langword="true"/>). Once exhausted, <see cref="EvaluateReassemblyTimeout"/> becomes a
    /// permanent no-op for that <c>clientId</c> — no further <c>OnSnapshotRetransmitAttempt</c> calls are
    /// possible without a fresh <see cref="BeginReassembly"/> call representing a new zone-entry attempt.
    /// </para>
    /// <para>
    /// <b>The reassembly-timeout baseline resets after each non-exhausting retransmit:</b> per B-NP-8's
    /// retransmit-cycle model, each of the <see cref="MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS"/> attempts gets
    /// its own full <c>reassemblyTimeoutTicks</c> window — <see cref="EvaluateReassemblyTimeout"/>
    /// re-baselines the timeout clock to <c>currentTick</c> after every non-exhausting retransmit, so
    /// AC-NC-40's "3rd <c>FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS</c> elapses" scenario is three independent
    /// timeout windows, not one long one.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var tracker = new ZoneSnapshotReassemblyTracker();
    /// tracker.BeginReassembly(clientId: 7, characterId: 555u, firstFragmentTick: 100u);
    ///
    /// // Fragment N is dropped; FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS (10s @ 20Hz = 200 ticks) elapses:
    /// tracker.EvaluateReassemblyTimeout(clientId: 7, currentTick: 300u, reassemblyTimeoutTicks: 200u, observer);
    /// // -&gt; OnSnapshotRetransmitAttempt(555u, 1, 3) fires; gate still closed.
    ///
    /// // The server re-sends and the client reassembles successfully:
    /// tracker.CompleteReassembly(clientId: 7, observer);
    /// // -&gt; OnZoneGateOpened(7) fires; tracker.IsGateOpen(7) is now true.
    /// </code>
    /// </example>
    public sealed class ZoneSnapshotReassemblyTracker
    {
        /// <summary>
        /// Maximum <c>ZoneSnapshotRequest</c> retransmit attempts per zone-entry attempt before the
        /// client gives up (AC-NC-40 — the one value this story's own AC text pins to a concrete
        /// literal; see class remarks).
        /// </summary>
        public const int MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS = 3;

        private sealed class ZoneEntryRecord
        {
            internal uint CharacterId;
            internal uint TimeoutBaselineTick;
            internal int RetransmitAttempts;
            internal bool GateOpen;
            internal bool Exhausted;
        }

        private readonly Dictionary<uint, ZoneEntryRecord> _entries = new();

        /// <summary>
        /// Begins tracking a new zone-entry reassembly attempt for <paramref name="clientId"/>.
        /// Unconditionally overwrites any prior entry for <paramref name="clientId"/> — a fresh zone
        /// entry (initial join, or a new attempt after a prior one gave up) always starts a clean
        /// attempt, matching <see cref="ConnectionStateMachine.EnterConnecting"/>'s own overwrite
        /// precedent.
        /// </summary>
        /// <param name="clientId">The joining client's connection identity.</param>
        /// <param name="characterId">The character associated with this zone-entry attempt.</param>
        /// <param name="firstFragmentTick">
        /// The server tick at which fragment 1 of the <c>ZoneStateSnapshot</c> arrived — the initial
        /// reassembly-timeout baseline.
        /// </param>
        /// <example>
        /// <code>tracker.BeginReassembly(clientId: 7, characterId: 555u, firstFragmentTick: 100u);</code>
        /// </example>
        public void BeginReassembly(uint clientId, uint characterId, uint firstFragmentTick)
        {
            _entries[clientId] = new ZoneEntryRecord
            {
                CharacterId = characterId,
                TimeoutBaselineTick = firstFragmentTick,
                RetransmitAttempts = 0,
                GateOpen = false,
                Exhausted = false,
            };
        }

        /// <summary>
        /// Checks whether <paramref name="clientId"/>'s reassembly has timed out as of
        /// <paramref name="currentTick"/> (AC-NC-35a, AC-NC-40) and, if so, records one retransmit
        /// attempt. A no-op if the gate is already open, the attempts are already exhausted, or the
        /// timeout has not yet elapsed — intended to be called on any cadence a caller chooses (e.g.
        /// once per tick) for every client currently mid-reassembly.
        /// </summary>
        /// <param name="clientId">The client to evaluate.</param>
        /// <param name="currentTick">The current server tick.</param>
        /// <param name="reassemblyTimeoutTicks">
        /// <c>FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS × TICK_RATE_HZ</c> — caller-supplied, never hardcoded
        /// here (see class remarks). Must be greater than <c>0</c>.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnSnapshotRetransmitAttempt(characterId,
        /// attemptNumber, MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS)</c> when a timeout is recorded.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="reassemblyTimeoutTicks"/> is <c>0</c>.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="clientId"/> has no <see cref="BeginReassembly"/> entry.</exception>
        /// <example>
        /// <code>tracker.EvaluateReassemblyTimeout(clientId: 7, currentTick: 300u, reassemblyTimeoutTicks: 200u, observer);</code>
        /// </example>
        public void EvaluateReassemblyTimeout(uint clientId, uint currentTick, uint reassemblyTimeoutTicks
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (reassemblyTimeoutTicks == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reassemblyTimeoutTicks), "Must be greater than 0.");
            }

            ZoneEntryRecord record = RequireEntry(clientId, nameof(EvaluateReassemblyTimeout));

            if (record.GateOpen || record.Exhausted)
            {
                return; // already resolved (either way) — nothing further to evaluate
            }

            uint expiryTick = record.TimeoutBaselineTick + reassemblyTimeoutTicks;
            if (!StaleDiscardComparer.IsTickExpired(currentTick, expiryTick))
            {
                return; // not yet timed out
            }

            record.RetransmitAttempts++;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSnapshotRetransmitAttempt(record.CharacterId, record.RetransmitAttempts, MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS);
#endif

            if (record.RetransmitAttempts >= MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS)
            {
                record.Exhausted = true; // AC-NC-40: gives up — no further reassembly is attempted
            }
            else
            {
                // Each retransmit attempt gets its own full timeout window (see class remarks).
                record.TimeoutBaselineTick = currentTick;
            }
        }

        /// <summary>
        /// Marks <paramref name="clientId"/>'s zone-entry reassembly as successfully complete and opens
        /// the zone-entry gate (AC-NC-35c). Fires immediately, with no simulated delay — "within 100ms
        /// of the final fragment being received" is satisfied by construction in a tick-driven test with
        /// no wall-clock wait between reassembly completion and this call.
        /// </summary>
        /// <param name="clientId">The client whose reassembly completed.</param>
        /// <param name="observer">Optional test/dev-build observer. Fires <c>OnZoneGateOpened(clientId)</c>.</param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="clientId"/> has no <see cref="BeginReassembly"/> entry, or its retransmit
        /// attempts are already exhausted (a fresh <see cref="BeginReassembly"/> call is required to
        /// represent a new zone-entry attempt after giving up).
        /// </exception>
        /// <example>
        /// <code>tracker.CompleteReassembly(clientId: 7, observer);</code>
        /// </example>
        public void CompleteReassembly(uint clientId
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            ZoneEntryRecord record = RequireEntry(clientId, nameof(CompleteReassembly));

            if (record.Exhausted)
            {
                throw new InvalidOperationException(
                    $"[ZoneSnapshotReassemblyTracker] {nameof(CompleteReassembly)}: clientId={clientId} has already " +
                    "exhausted its retransmit attempts — a fresh BeginReassembly call is required to represent a new zone-entry attempt.");
            }

            record.GateOpen = true;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnZoneGateOpened(clientId);
#endif
        }

        /// <summary>Returns whether <paramref name="clientId"/>'s zone-entry gate is currently open.</summary>
        /// <param name="clientId">The client to query.</param>
        /// <example><code>bool open = tracker.IsGateOpen(clientId: 7);</code></example>
        public bool IsGateOpen(uint clientId) =>
            _entries.TryGetValue(clientId, out ZoneEntryRecord record) && record.GateOpen;

        /// <summary>
        /// Returns whether <paramref name="clientId"/> has exhausted
        /// <see cref="MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS"/> without successful reassembly (AC-NC-40) — the
        /// "gives up, begins a fresh reconnect" signal this class exposes as an observable query rather
        /// than performing any transport disconnect itself (see class remarks).
        /// </summary>
        /// <param name="clientId">The client to query.</param>
        /// <example><code>bool exhausted = tracker.HasExhaustedRetransmitAttempts(clientId: 7);</code></example>
        public bool HasExhaustedRetransmitAttempts(uint clientId) =>
            _entries.TryGetValue(clientId, out ZoneEntryRecord record) && record.Exhausted;

        /// <summary>Returns the number of retransmit attempts recorded so far for <paramref name="clientId"/>.</summary>
        /// <param name="clientId">The client to query.</param>
        /// <example><code>int attempts = tracker.GetRetransmitAttemptCount(clientId: 7);</code></example>
        public int GetRetransmitAttemptCount(uint clientId) =>
            _entries.TryGetValue(clientId, out ZoneEntryRecord record) ? record.RetransmitAttempts : 0;

        /// <summary>Returns whether <paramref name="clientId"/> currently has a <see cref="BeginReassembly"/> entry.</summary>
        /// <param name="clientId">The client to query.</param>
        /// <example><code>bool tracked = tracker.IsTracked(clientId: 7);</code></example>
        public bool IsTracked(uint clientId) => _entries.ContainsKey(clientId);

        private ZoneEntryRecord RequireEntry(uint clientId, string callerName)
        {
            if (!_entries.TryGetValue(clientId, out ZoneEntryRecord record))
            {
                throw new InvalidOperationException(
                    $"[ZoneSnapshotReassemblyTracker] {callerName}: clientId={clientId} has no BeginReassembly entry.");
            }

            return record;
        }
    }
}
