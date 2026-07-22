using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// The ST-NET-2 zone instance lifecycle state machine (<c>networking-session.md</c>). Story 014
    /// builds the four <see cref="ZoneState"/> values' full transition table — <c>Empty → Active</c>,
    /// <c>Active ↔ Draining</c> (both directions), <c>Active → Closed</c> (direct, AC-NC-41),
    /// <c>Draining → Closed</c>, and <c>Closed → Empty</c> — plus the 10–50-player zone capacity
    /// enforcement gate (AC-NC-22, AC-NC-24, EC-NET-3) that governs whether a join is accepted at
    /// all. This is the first (and so far only) zone-level registry in this codebase, parallel to
    /// (but structurally independent of) <see cref="ConnectionStateMachine"/>'s per-account registry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deliberately decoupled from <see cref="ConnectionStateMachine"/> (Out of Scope, per this
    /// story's own scope boundary):</b> this class never queries <see cref="ConnectionStateMachine"/>
    /// directly. Every method that needs to know "how many sessions are currently
    /// <see cref="SessionState.Connected"/> / <see cref="SessionState.Reconnecting"/> /
    /// <see cref="SessionState.Disconnected_SessionActive"/> in this zone right now" takes that as a
    /// caller-supplied parameter (<see cref="EvaluatePlayerCountChange"/>'s <c>hasAnyConnectedSession</c>
    /// / <c>totalOccupiedSlots</c>, <see cref="EvaluateJoinAttempt"/>'s <c>currentOccupiedSlots</c>) —
    /// the same delegate-seam/loose-coupling pattern this whole epic uses between stories (see
    /// <see cref="ConnectionStateMachine.CompleteReAuthSuccess"/>'s own forward-dependency delegates).
    /// A future orchestration layer (not yet built) is expected to compose this class with
    /// <see cref="ConnectionStateMachine"/>, computing those counts from its own registry and driving
    /// both state machines together.
    /// </para>
    /// <para>
    /// <b>Zone capacity is caller-supplied per call, never stored on the zone record:</b>
    /// <see cref="EnterActive"/>'s <c>capacity</c> parameter and <see cref="EvaluateJoinAttempt"/>'s
    /// <c>capacity</c> parameter are both validated in isolation, per call — this class does not own a
    /// persistent "this zone's capacity is N" field. No real zone-configuration system exists yet (the
    /// production capacity value's actual source is out of scope for this Foundation-layer story);
    /// <see cref="IZoneTestConfigurator.SetZoneCapacity"/> is a separate, test-only, independently-owned
    /// override store (Story 001) that this class never reads from or writes to.
    /// </para>
    /// <para>
    /// <b><see cref="EvaluateJoinAttempt"/> is a pure capacity calculation, not a state transition —
    /// deliberately has no <see cref="INetworkTestObserver"/> parameter at all:</b> AC-NC-22/AC-NC-24
    /// only require "the server returns an overflow response" — there is no dedicated
    /// <see cref="INetworkTestObserver"/> callback for a rejected/accepted join anywhere in that
    /// interface's 25-callback surface, so this method's <see langword="bool"/> return value alone
    /// is the entire observable contract. It does not touch <c>_zones</c> at all (no
    /// <c>zoneInstanceId</c> parameter — nothing to look up).
    /// </para>
    /// <para>
    /// <b>Split "teardown" method, not one unified <c>TransitionToClosed</c> (judgment call, technical
    /// necessity, not stylistic preference):</b> <see cref="PersistenceWriteReason"/> is declared
    /// entirely inside the <c>#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD</c> guard in
    /// <c>INetworkTestObserver.cs</c> — the type itself does not exist in a Release build. A single
    /// method taking a caller-supplied, unconditional <see cref="PersistenceWriteReason"/> parameter
    /// (to distinguish the AC-NC-41 <c>Active → Closed</c> direct path's <c>ExplicitDisconnect</c>
    /// reason from the <c>Draining → Closed</c> TTL-expiry path's <c>ZoneClose</c> reason) would
    /// therefore fail to compile in Release. Instead, this class provides two separate teardown
    /// methods — <see cref="CompleteExplicitDisconnectTeardown"/> and
    /// <see cref="CompleteTTLExpiryTeardown"/> — each hardcoding its own determinate
    /// <see cref="PersistenceWriteReason"/> literal strictly inside its own guarded block, exactly
    /// mirroring <see cref="ConnectionStateMachine"/>'s own established convention of hardcoded
    /// literal reason/trigger values for every GDD-determinate transition (e.g.
    /// <see cref="ConnectionStateMachine.ProcessExplicitDisconnect"/> hardcodes
    /// <see cref="PersistenceWriteReason.ExplicitDisconnect"/>; <see cref="ConnectionStateMachine.HandleSessionSteal"/>
    /// hardcodes <see cref="PersistenceWriteReason.SessionSteal"/>) — a caller-supplied trigger/reason
    /// parameter is only ever used in that class when the GDD text itself leaves the label
    /// unspecified (<see cref="ConnectionStateMachine.FailConnecting"/>'s <c>trigger</c> string), which
    /// is not the case here: AC-NC-41 pins <c>ExplicitDisconnect</c> explicitly, and
    /// <see cref="PersistenceWriteReason.ZoneClose"/>'s own doc comment ("The owning zone instance
    /// closed (ST-NET-2 Draining → Closed)") was pre-registered by Story 002 specifically for the
    /// <c>Draining → Closed</c> row this class's <see cref="CompleteTTLExpiryTeardown"/> implements.
    /// </para>
    /// <para>
    /// <b><see cref="ZoneState.Empty"/> is reachable two ways — implicit absence, or explicit
    /// re-registration — and <see cref="EnterActive"/> accepts both:</b> a zone that has never been
    /// registered in <c>_zones</c> at all is treated as <see cref="ZoneState.Empty"/> (mirroring
    /// <see cref="IZoneTestConfigurator.GetCurrentZoneState"/>'s own existing precedent of defaulting
    /// an unregistered zone to <see cref="ZoneState.Empty"/>), while a zone that has previously gone
    /// through the full lifecycle and been reset via <see cref="ReinitializeFromClosed"/>
    /// (<c>Closed → Empty</c>) has an explicit registry entry with <see cref="ZoneState.Empty"/>.
    /// <see cref="EnterActive"/> accepts a zone instance in either condition — it is the only method
    /// in this class with that dual-acceptance shape; every other transition method requires an
    /// explicit, already-registered prior state (see each method's own <c>RequireState</c> guard).
    /// This is deliberately asymmetric with <see cref="TryGetZoneState"/>, which (per this story's own
    /// instruction to mirror <see cref="ConnectionStateMachine.TryGetSessionState"/>'s exact shape)
    /// returns <see langword="false"/> for an unregistered zone rather than synthesizing
    /// <see cref="ZoneState.Empty"/> — the two methods answer different questions ("is this zone
    /// currently eligible for <c>Empty → Active</c>?" vs. "does this class have a registry entry for
    /// this zone?").
    /// </para>
    /// <para>
    /// <b>Out of scope, owned by neighbouring stories (Story 015):</b> the real TTL-tick countdown
    /// infrastructure and zone crash recovery. <see cref="CompleteTTLExpiryTeardown"/> is a
    /// synchronous, caller-driven method — the caller is expected to have already determined
    /// (elsewhere, via its own tick-driven sweep) that every remaining session's TTL has elapsed
    /// before calling this method. This class contains no ticking timer of its own, mirroring
    /// <see cref="ConnectionStateMachine.EvaluateTimeouts"/>'s own caller-supplied-tick-counts
    /// precedent (see that class's remarks).
    /// </para>
    /// <para>
    /// <b>AC-NC-41's fuller text (judgment call, approved before implementation):</b> this story's own
    /// AC-NC-41 acceptance-criteria text (as written in the story file) omits two clauses present in
    /// the GDD's current <c>networking-session.md</c> text: the
    /// <see cref="INetworkTestObserver.OnPersistenceWriteCompleted"/> firing, and the
    /// <see cref="IZoneTestConfigurator.GetCurrentZoneState"/> same-tick-boundary readback. This class
    /// and its test file are built and verified against the fuller GDD text, not the story file's own
    /// abbreviated copy.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var zoneStateMachine = new ZoneSessionStateMachine();
    ///
    /// // First player joins a brand-new zone instance:
    /// bool accepted = zoneStateMachine.EvaluateJoinAttempt(currentOccupiedSlots: 0, capacity: 50); // true
    /// zoneStateMachine.EnterActive(zoneInstanceId: 7u, capacity: 50, observer);
    ///
    /// // The only player disconnects (heartbeat timeout -> ghost) — zone caller reports the new counts:
    /// zoneStateMachine.EvaluatePlayerCountChange(zoneInstanceId: 7u,
    ///     hasAnyConnectedSession: false, totalOccupiedSlots: 1, observer); // Active -> Draining
    ///
    /// // The player reconnects successfully:
    /// zoneStateMachine.EvaluatePlayerCountChange(zoneInstanceId: 7u,
    ///     hasAnyConnectedSession: true, totalOccupiedSlots: 1, observer); // Draining -> Active
    ///
    /// // The player explicitly disconnects, and was the zone's only session (AC-NC-41):
    /// zoneStateMachine.CompleteExplicitDisconnectTeardown(zoneInstanceId: 7u, characterId: 555u,
    ///     persistFinalCharacterState: characterId =&gt; persistence.Save(characterId),
    ///     observer); // Active -> Closed, directly, no intermediate Draining
    ///
    /// // The zone is later re-allocated for a new session:
    /// zoneStateMachine.ReinitializeFromClosed(zoneInstanceId: 7u, observer); // Closed -> Empty
    /// </code>
    /// </example>
    public sealed class ZoneSessionStateMachine
    {
        /// <summary>
        /// One zone instance's in-memory record. A mutable reference type (not a struct) so every
        /// transition method can update <see cref="State"/> in place via a single dictionary lookup,
        /// matching <see cref="ConnectionStateMachine.AccountSessionRecord"/>'s own reasoning.
        /// </summary>
        private sealed class ZoneSessionRecord
        {
            /// <summary>The zone instance's current <see cref="ZoneState"/>.</summary>
            internal ZoneState State;
        }

        private readonly Dictionary<uint, ZoneSessionRecord> _zones = new();

        /// <summary>
        /// Registers <paramref name="zoneInstanceId"/>'s first player connection (ST-NET-2 row 1,
        /// <c>Empty → Active</c>): "First player enters Connected." Accepts a zone instance in either
        /// of the two conditions this class treats as <see cref="ZoneState.Empty"/> — never registered
        /// at all, or explicitly reset via <see cref="ReinitializeFromClosed"/> (see class remarks).
        /// </summary>
        /// <param name="zoneInstanceId">The zone instance receiving its first player.</param>
        /// <param name="capacity">
        /// A defensive sanity check only (GDD's own tunable range is 10–50) — never stored on the zone
        /// record (see class remarks). Must be at least <c>1</c>.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnZoneStateTransitioned(zoneInstanceId, Empty,
        /// Active)</c>.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than <c>1</c>.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="zoneInstanceId"/> is already registered as something other than
        /// <see cref="ZoneState.Empty"/>.
        /// </exception>
        /// <example>
        /// <code>zoneStateMachine.EnterActive(zoneInstanceId: 7u, capacity: 50, observer);</code>
        /// </example>
        public void EnterActive(uint zoneInstanceId, int capacity
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity),
                    $"[ZoneSessionStateMachine] {nameof(EnterActive)}: capacity must be at least 1 (was {capacity}).");
            }

            if (_zones.TryGetValue(zoneInstanceId, out ZoneSessionRecord record))
            {
                if (record.State != ZoneState.Empty)
                {
                    throw new InvalidOperationException(
                        $"[ZoneSessionStateMachine] {nameof(EnterActive)}: zoneInstanceId={zoneInstanceId} " +
                        $"is not currently Empty (state={record.State}).");
                }

                record.State = ZoneState.Active;
            }
            else
            {
                _zones[zoneInstanceId] = new ZoneSessionRecord { State = ZoneState.Active };
            }

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnZoneStateTransitioned(zoneInstanceId, ZoneState.Empty, ZoneState.Active);
#endif
        }

        /// <summary>
        /// Evaluates whether a new join (or an existing session's return to a live connection) should
        /// change <paramref name="zoneInstanceId"/>'s zone-level <see cref="ZoneState"/> between
        /// <see cref="ZoneState.Active"/> and <see cref="ZoneState.Draining"/> (ST-NET-2 rows: last
        /// remaining player enters <see cref="SessionState.Disconnected_SessionActive"/>; last
        /// <see cref="SessionState.Connected"/> player explicitly disconnects while ≥1 ghost remains;
        /// a player reconnects or a new player enters while <see cref="ZoneState.Draining"/>;
        /// re-authentication fails for a <see cref="SessionState.Reconnecting"/> session while
        /// <see cref="ZoneState.Draining"/>). Requires <paramref name="zoneInstanceId"/> to currently
        /// be <see cref="ZoneState.Active"/> or <see cref="ZoneState.Draining"/>.
        /// </summary>
        /// <remarks>
        /// Does not itself decide when the zone should go to <see cref="ZoneState.Closed"/> instead —
        /// see the <paramref name="totalOccupiedSlots"/><c> == 0</c> exception below. Zero remaining
        /// sessions is always routed through <see cref="CompleteExplicitDisconnectTeardown"/> (the only
        /// way <paramref name="totalOccupiedSlots"/> can legitimately reach <c>0</c> while
        /// <paramref name="hasAnyConnectedSession"/> is <see langword="false"/> — an explicit
        /// disconnect removes the session's footprint entirely, per
        /// <see cref="ConnectionStateMachine.ProcessExplicitDisconnect"/>'s "never enters
        /// <see cref="SessionState.Disconnected_SessionActive"/>" guarantee — whereas a heartbeat
        /// timeout always leaves that session counted as a ghost, so <paramref name="totalOccupiedSlots"/>
        /// is always at least <c>1</c> on that path). This guard is symmetric across both
        /// <see cref="ZoneState.Active"/> and <see cref="ZoneState.Draining"/> — a caller reporting zero
        /// remaining sessions with no connected session is a contract violation regardless of which of
        /// the two states the zone is currently in, so both throw rather than only the
        /// <see cref="ZoneState.Active"/> case (code review finding, Story 014 — both the
        /// <c>Draining → Closed</c> real path via <see cref="CompleteTTLExpiryTeardown"/> and this
        /// defensive guard can coexist since the former never calls into this method).
        /// </remarks>
        /// <param name="zoneInstanceId">The zone instance to evaluate.</param>
        /// <param name="hasAnyConnectedSession">
        /// <see langword="true"/> if at least one session in this zone is currently
        /// <see cref="SessionState.Connected"/> (specifically <see cref="SessionState.Connected"/> —
        /// not <see cref="SessionState.Reconnecting"/>; per the GDD's own <c>Draining → Draining</c>
        /// row, a session merely attempting to reconnect does not yet cancel Draining — only a
        /// successful reconnect, i.e. reaching <see cref="SessionState.Connected"/>, does).
        /// </param>
        /// <param name="totalOccupiedSlots">
        /// The current count of sessions counting toward zone capacity in this zone — every session in
        /// <see cref="SessionState.Connected"/>, <see cref="SessionState.Reconnecting"/>, or
        /// <see cref="SessionState.Disconnected_SessionActive"/> (EC-NET-3). Must not be negative.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnZoneStateTransitioned(zoneInstanceId, Draining,
        /// Active)</c> or <c>OnZoneStateTransitioned(zoneInstanceId, Active, Draining)</c> only when the
        /// zone-level state actually changes — the <c>Active → Active</c> and <c>Draining → Draining</c>
        /// bookkeeping rows fire no callback at all ("no zone-level state change" per the GDD's own
        /// transition table).
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="totalOccupiedSlots"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="zoneInstanceId"/> is not currently registered as <see cref="ZoneState.Active"/>
        /// or <see cref="ZoneState.Draining"/>; or <paramref name="hasAnyConnectedSession"/> is
        /// <see langword="false"/> and <paramref name="totalOccupiedSlots"/> is <c>0</c> (use
        /// <see cref="CompleteExplicitDisconnectTeardown"/> instead — see remarks; applies regardless of
        /// whether the zone is currently <see cref="ZoneState.Active"/> or <see cref="ZoneState.Draining"/>).
        /// </exception>
        /// <example>
        /// <code>
        /// zoneStateMachine.EvaluatePlayerCountChange(zoneInstanceId: 7u,
        ///     hasAnyConnectedSession: false, totalOccupiedSlots: 1, observer); // Active -&gt; Draining
        /// </code>
        /// </example>
        public void EvaluatePlayerCountChange(uint zoneInstanceId, bool hasAnyConnectedSession, int totalOccupiedSlots
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (totalOccupiedSlots < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalOccupiedSlots),
                    $"[ZoneSessionStateMachine] {nameof(EvaluatePlayerCountChange)}: totalOccupiedSlots must not be negative (was {totalOccupiedSlots}).");
            }

            ZoneSessionRecord record = RequireActiveOrDraining(zoneInstanceId, nameof(EvaluatePlayerCountChange));

            if (hasAnyConnectedSession)
            {
                if (record.State == ZoneState.Draining)
                {
                    record.State = ZoneState.Active;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                    observer?.OnZoneStateTransitioned(zoneInstanceId, ZoneState.Draining, ZoneState.Active);
#endif
                }

                // Active -> Active: "no zone-level state change" per the GDD's own transition table —
                // no callback fired.
            }
            else
            {
                // Symmetric across both Active and Draining (code review finding, Story 014) — a
                // caller reporting zero remaining sessions with no connected session is a contract
                // violation regardless of the zone's current state; the real Draining -> Closed path
                // never reaches here (it's CompleteTTLExpiryTeardown's exclusive responsibility).
                if (totalOccupiedSlots == 0)
                {
                    throw new InvalidOperationException(
                        $"[ZoneSessionStateMachine] {nameof(EvaluatePlayerCountChange)}: zoneInstanceId={zoneInstanceId} " +
                        $"reports zero remaining sessions with no connected session (state={record.State}) — " +
                        $"use {nameof(CompleteExplicitDisconnectTeardown)} for the zero-remaining-sessions case instead.");
                }

                if (record.State == ZoneState.Active)
                {
                    record.State = ZoneState.Draining;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                    observer?.OnZoneStateTransitioned(zoneInstanceId, ZoneState.Active, ZoneState.Draining);
#endif
                }

                // Draining -> Draining (re-auth failure bookkeeping, no connected session, already
                // Draining, at least one occupied slot remains): "no zone-level state change" per the
                // GDD's own transition table — no callback fired, and no further action needed here.
            }
        }

        /// <summary>
        /// Evaluates whether a join attempt against <paramref name="capacity"/> should be accepted
        /// (AC-NC-22, AC-NC-24, EC-NET-3) — a pure capacity calculation with no observable side effect
        /// beyond its return value (see class remarks for why this method has no
        /// <see cref="INetworkTestObserver"/> parameter at all).
        /// </summary>
        /// <param name="currentOccupiedSlots">
        /// The zone's current occupied-slot count — every session in <see cref="SessionState.Connected"/>,
        /// <see cref="SessionState.Reconnecting"/>, or <see cref="SessionState.Disconnected_SessionActive"/>
        /// (EC-NET-3: ghost sessions count toward the cap). Must not be negative.
        /// </param>
        /// <param name="capacity">The zone's capacity (GDD tunable range 10–50). Must be at least <c>1</c>.</param>
        /// <returns>
        /// <see langword="true"/> if the join is accepted (<paramref name="currentOccupiedSlots"/> is
        /// strictly less than <paramref name="capacity"/>); <see langword="false"/> if the zone is at
        /// or over capacity and the caller should return an overflow response instead.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="capacity"/> is less than <c>1</c>, or <paramref name="currentOccupiedSlots"/> is negative.
        /// </exception>
        /// <example>
        /// <code>
        /// bool accepted = zoneStateMachine.EvaluateJoinAttempt(currentOccupiedSlots: 50, capacity: 50); // false — overflow
        /// </code>
        /// </example>
        public bool EvaluateJoinAttempt(int currentOccupiedSlots, int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity),
                    $"[ZoneSessionStateMachine] {nameof(EvaluateJoinAttempt)}: capacity must be at least 1 (was {capacity}).");
            }

            if (currentOccupiedSlots < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(currentOccupiedSlots),
                    $"[ZoneSessionStateMachine] {nameof(EvaluateJoinAttempt)}: currentOccupiedSlots must not be negative (was {currentOccupiedSlots}).");
            }

            return currentOccupiedSlots < capacity;
        }

        /// <summary>
        /// Completes zone teardown via the <c>Active → Closed</c> direct path (ST-NET-2, AC-NC-41):
        /// the zone's last remaining session — with no <see cref="SessionState.Disconnected_SessionActive"/>
        /// ghosts present — sends an explicit disconnect, so zero sessions remain and the zone skips
        /// <see cref="ZoneState.Draining"/> entirely. Requires <paramref name="zoneInstanceId"/> to
        /// currently be <see cref="ZoneState.Active"/>.
        /// </summary>
        /// <remarks>
        /// Call order (judgment call, approved before implementation — AC-NC-41 does not itself mandate
        /// a strict order between these two callbacks, unlike this epic's Story 013 orderings; this
        /// method follows <see cref="ConnectionStateMachine.ProcessExplicitDisconnect"/>'s own
        /// persist-then-transition precedent for consistency): <paramref name="persistFinalCharacterState"/>
        /// → <c>OnPersistenceWriteCompleted(characterId, ExplicitDisconnect)</c> → registry update →
        /// <c>OnZoneStateTransitioned(zoneInstanceId, Active, Closed)</c>.
        /// </remarks>
        /// <param name="zoneInstanceId">The zone instance closing.</param>
        /// <param name="characterId">
        /// The character of the zone's one remaining (now explicitly-disconnecting) session.
        /// </param>
        /// <param name="persistFinalCharacterState">
        /// Writes the character's final state to persistence, called with <paramref name="characterId"/>.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnPersistenceWriteCompleted(characterId,
        /// ExplicitDisconnect)</c> then <c>OnZoneStateTransitioned(zoneInstanceId, Active, Closed)</c>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="persistFinalCharacterState"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="zoneInstanceId"/> is not currently registered as <see cref="ZoneState.Active"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// zoneStateMachine.CompleteExplicitDisconnectTeardown(zoneInstanceId: 7u, characterId: 555u,
        ///     persistFinalCharacterState: characterId =&gt; persistence.Save(characterId),
        ///     observer);
        /// </code>
        /// </example>
        public void CompleteExplicitDisconnectTeardown(
            uint zoneInstanceId,
            uint characterId,
            Action<uint> persistFinalCharacterState
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (persistFinalCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistFinalCharacterState));
            }

            ZoneSessionRecord record = RequireState(zoneInstanceId, ZoneState.Active, nameof(CompleteExplicitDisconnectTeardown));

            persistFinalCharacterState(characterId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.ExplicitDisconnect);
#endif

            record.State = ZoneState.Closed;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnZoneStateTransitioned(zoneInstanceId, ZoneState.Active, ZoneState.Closed);
#endif
        }

        /// <summary>
        /// Completes zone teardown via the <c>Draining → Closed</c> path (ST-NET-2): every remaining
        /// session's TTL has elapsed (determined by the caller — see class remarks; this method
        /// contains no real timer). Requires <paramref name="zoneInstanceId"/> to currently be
        /// <see cref="ZoneState.Draining"/>.
        /// </summary>
        /// <remarks>
        /// Call order per remaining session, then the zone transition (same persist-then-transition
        /// judgment call as <see cref="CompleteExplicitDisconnectTeardown"/>): for each entry in
        /// <paramref name="remainingCharacterIds"/>, in order — <paramref name="persistFinalCharacterState"/>
        /// → <c>OnPersistenceWriteCompleted(characterId, ZoneClose)</c> — this loop runs to completion
        /// strictly before → registry update → <c>OnZoneStateTransitioned(zoneInstanceId, Draining,
        /// Closed)</c>.
        /// </remarks>
        /// <param name="zoneInstanceId">The zone instance closing.</param>
        /// <param name="remainingCharacterIds">
        /// Every character whose session is still present in this zone (all
        /// <see cref="SessionState.Disconnected_SessionActive"/> ghosts, and any
        /// <see cref="SessionState.Reconnecting"/> session whose TTL also elapsed). Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="persistFinalCharacterState">
        /// Writes a character's final state to persistence, called once per entry in
        /// <paramref name="remainingCharacterIds"/>, in order. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnPersistenceWriteCompleted(characterId,
        /// ZoneClose)</c> once per <paramref name="remainingCharacterIds"/> entry, then
        /// <c>OnZoneStateTransitioned(zoneInstanceId, Draining, Closed)</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="remainingCharacterIds"/> or <paramref name="persistFinalCharacterState"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="zoneInstanceId"/> is not currently registered as <see cref="ZoneState.Draining"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// zoneStateMachine.CompleteTTLExpiryTeardown(zoneInstanceId: 7u,
        ///     remainingCharacterIds: new[] { 555u, 556u },
        ///     persistFinalCharacterState: characterId =&gt; persistence.Save(characterId),
        ///     observer);
        /// </code>
        /// </example>
        public void CompleteTTLExpiryTeardown(
            uint zoneInstanceId,
            IReadOnlyList<uint> remainingCharacterIds,
            Action<uint> persistFinalCharacterState
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (remainingCharacterIds == null)
            {
                throw new ArgumentNullException(nameof(remainingCharacterIds));
            }

            if (persistFinalCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistFinalCharacterState));
            }

            ZoneSessionRecord record = RequireState(zoneInstanceId, ZoneState.Draining, nameof(CompleteTTLExpiryTeardown));

            for (int i = 0; i < remainingCharacterIds.Count; i++)
            {
                uint characterId = remainingCharacterIds[i];

                persistFinalCharacterState(characterId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                observer?.OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.ZoneClose);
#endif
            }

            record.State = ZoneState.Closed;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnZoneStateTransitioned(zoneInstanceId, ZoneState.Draining, ZoneState.Closed);
#endif
        }

        /// <summary>
        /// Reinitializes a torn-down zone instance for re-allocation (ST-NET-2, <c>Closed → Empty</c>):
        /// "Zone re-allocated for a new session." Requires <paramref name="zoneInstanceId"/> to
        /// currently be <see cref="ZoneState.Closed"/>.
        /// </summary>
        /// <param name="zoneInstanceId">The zone instance being re-allocated.</param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnZoneStateTransitioned(zoneInstanceId, Closed,
        /// Empty)</c>.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="zoneInstanceId"/> is not currently registered as <see cref="ZoneState.Closed"/>.
        /// </exception>
        /// <example>
        /// <code>zoneStateMachine.ReinitializeFromClosed(zoneInstanceId: 7u, observer);</code>
        /// </example>
        public void ReinitializeFromClosed(uint zoneInstanceId
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            ZoneSessionRecord record = RequireState(zoneInstanceId, ZoneState.Closed, nameof(ReinitializeFromClosed));

            record.State = ZoneState.Empty;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnZoneStateTransitioned(zoneInstanceId, ZoneState.Closed, ZoneState.Empty);
#endif
        }

        /// <summary>Returns the current <see cref="ZoneState"/> for <paramref name="zoneInstanceId"/>, if registered.</summary>
        /// <param name="zoneInstanceId">The zone instance to query.</param>
        /// <param name="state">The zone's current state, if registered; otherwise <see langword="default"/>.</param>
        /// <returns><see langword="true"/> if <paramref name="zoneInstanceId"/> has a registry entry.</returns>
        /// <example>
        /// <code>
        /// if (zoneStateMachine.TryGetZoneState(zoneInstanceId: 7u, out ZoneState state))
        /// {
        ///     // ...
        /// }
        /// </code>
        /// </example>
        public bool TryGetZoneState(uint zoneInstanceId, out ZoneState state)
        {
            if (_zones.TryGetValue(zoneInstanceId, out ZoneSessionRecord record))
            {
                state = record.State;
                return true;
            }

            state = default;
            return false;
        }

        /// <summary>Returns whether <paramref name="zoneInstanceId"/> currently has any registry entry.</summary>
        /// <param name="zoneInstanceId">The zone instance to query.</param>
        /// <example>
        /// <code>bool registered = zoneStateMachine.IsZoneRegistered(zoneInstanceId: 7u);</code>
        /// </example>
        public bool IsZoneRegistered(uint zoneInstanceId) => _zones.ContainsKey(zoneInstanceId);

        /// <summary>
        /// Looks up <paramref name="zoneInstanceId"/>'s registry entry and throws
        /// <see cref="InvalidOperationException"/> if it is missing or not currently
        /// <paramref name="requiredState"/>.
        /// </summary>
        private ZoneSessionRecord RequireState(uint zoneInstanceId, ZoneState requiredState, string callerName)
        {
            if (!_zones.TryGetValue(zoneInstanceId, out ZoneSessionRecord record) || record.State != requiredState)
            {
                throw new InvalidOperationException(
                    $"[ZoneSessionStateMachine] {callerName}: zoneInstanceId={zoneInstanceId} is not currently registered as {requiredState}.");
            }

            return record;
        }

        /// <summary>
        /// Looks up <paramref name="zoneInstanceId"/>'s registry entry and throws
        /// <see cref="InvalidOperationException"/> if it is missing or not currently
        /// <see cref="ZoneState.Active"/> or <see cref="ZoneState.Draining"/> — the two-state
        /// precondition <see cref="EvaluatePlayerCountChange"/> shares across both bookkeeping
        /// directions.
        /// </summary>
        private ZoneSessionRecord RequireActiveOrDraining(uint zoneInstanceId, string callerName)
        {
            if (!_zones.TryGetValue(zoneInstanceId, out ZoneSessionRecord record) ||
                (record.State != ZoneState.Active && record.State != ZoneState.Draining))
            {
                throw new InvalidOperationException(
                    $"[ZoneSessionStateMachine] {callerName}: zoneInstanceId={zoneInstanceId} is not currently Active or Draining.");
            }

            return record;
        }
    }
}
