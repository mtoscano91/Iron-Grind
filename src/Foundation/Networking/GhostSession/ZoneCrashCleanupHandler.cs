using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// One ghosted or reconnecting session's minimal caller-supplied snapshot, as needed by
    /// <see cref="ZoneCrashCleanupHandler.ProcessZoneCrash"/> (Networking Core Story 021, CR-GH-11).
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="PendingPurchaseRecord"/>'s own "minimal caller-facing view" shape — a
    /// <see langword="readonly"/> struct carrying exactly the fields this one call site needs, no
    /// more. The caller is responsible for having already enumerated every session in
    /// <see cref="SessionState.Disconnected_SessionActive"/> or <see cref="SessionState.Reconnecting"/>
    /// for the crashed zone instance (CR-GH-11 step 1) before constructing a list of these.
    /// </remarks>
    public readonly struct GhostZoneCrashSession
    {
        /// <summary>The account whose session is being force-expired by the zone crash.</summary>
        public readonly uint AccountId;

        /// <summary>The character whose pre-disconnect snapshot must be persisted before termination.</summary>
        public readonly uint CharacterId;

        /// <summary>The pre-disconnect snapshot HP (CR-GH-11 step 2 — equivalent to CR-GH-10 step 3).</summary>
        public readonly int SnapshotHp;

        /// <summary>
        /// This session's state at the moment of the crash — either
        /// <see cref="SessionState.Disconnected_SessionActive"/> or
        /// <see cref="SessionState.Reconnecting"/> (CR-GH-11 step 1). Reported verbatim as the
        /// <c>fromState</c> of this session's <c>OnSessionStateTransitioned</c> call.
        /// </summary>
        public readonly SessionState FromState;

        /// <summary>Initializes a new <see cref="GhostZoneCrashSession"/> with the specified field values.</summary>
        public GhostZoneCrashSession(uint accountId, uint characterId, int snapshotHp, SessionState fromState)
        {
            AccountId = accountId;
            CharacterId = characterId;
            SnapshotHp = snapshotHp;
            FromState = fromState;
        }
    }

    /// <summary>
    /// Stateless static handler for the CR-GH-11 zone-crash cleanup sequence (Networking Core Story
    /// 021, AC-GH-10, AC-GH-20): enumerates every affected ghost/reconnecting session and durably
    /// persists each one's pre-disconnect snapshot before the (already-crashed) zone instance is
    /// considered fully torn down. Mirrors <see cref="GhostCleanupSequencer"/>'s and
    /// <see cref="MobDeTargetingCoordinator"/>'s stateless-static shape — no per-zone state of its
    /// own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The caller has already enumerated the affected sessions (CR-GH-11 step 1) — this class
    /// does not query any registry itself:</b> matching
    /// <see cref="ZoneSessionStateMachine.CompleteTTLExpiryTeardown"/>'s own
    /// <c>remainingCharacterIds</c> precedent, <see cref="ProcessZoneCrash"/>'s
    /// <c>affectedSessions</c> parameter is the caller's own pre-built list of every session in
    /// <see cref="SessionState.Disconnected_SessionActive"/> or <see cref="SessionState.Reconnecting"/>
    /// at the moment of the crash. This class has no dependency on
    /// <see cref="ConnectionStateMachine"/>'s registry at all.
    /// </para>
    /// <para>
    /// <b>No <c>GhostExpiredEvent</c> or <c>MobDeTargetCommand</c> delegate parameters at all
    /// (CR-GH-11 step 4) — not merely unused, structurally absent:</b> the zone instance has already
    /// crashed; there is no one left to deliver either event to. Unlike
    /// <see cref="GhostCleanupSequencer"/>'s two cleanup methods (which both accept a
    /// <c>broadcastGhostExpiredEvent</c> delegate) and <see cref="GhostDismissalCoordinator"/> (which
    /// accepts an <c>issueMobDeTargetCommand</c> delegate), this method's signature has no seam for
    /// either — a reader cannot even attempt to wire one in without changing the method signature,
    /// which is the point.
    /// </para>
    /// <para>
    /// <b>New <see cref="PersistenceWriteReason.GhostZoneCrash"/> enum value (Story 021):</b> none of
    /// the 6 pre-existing <see cref="PersistenceWriteReason"/> values name a zone-crash scenario —
    /// <see cref="PersistenceWriteReason.ZoneClose"/> is a different, already-owned scenario (Story
    /// 014's non-ghost <c>Draining → Closed</c> teardown reason; see that enum member's own doc
    /// comment and <see cref="ZoneSessionStateMachine"/>'s class remarks). Reusing it here would
    /// conflate two structurally different rows in two different state machines. Mirrors this
    /// cluster's own precedent of adding a new, precisely-named reason whenever an existing one
    /// doesn't fit (Story 018 added <see cref="PersistenceWriteReason.GhostCombatTTLExpiry"/> and
    /// <see cref="PersistenceWriteReason.GhostDeath"/> for the identical reason).
    /// </para>
    /// <para>
    /// <b>Idempotency (CR-GH-11 step 2's "if a snapshot write is already in progress ... prevents
    /// double-write") is out of scope for this class specifically:</b> that guarantee belongs to
    /// <see cref="PreDisconnectSnapshotWal"/> (CGS-3, Story 018) — the caller-supplied
    /// <c>persistCharacterState</c> delegate's own implementation is expected to route through that
    /// WAL, exactly as every other cleanup delegate in this cluster does. This class only guarantees
    /// the enumeration order and the per-session call sequence.
    /// </para>
    /// <para>
    /// <b>Per-session call order, not a single global order:</b> for each entry in
    /// <c>affectedSessions</c>, in order, <c>persistCharacterState</c> (called with that entry's
    /// <c>characterId</c> and <c>snapshotHp</c>) strictly precedes that same entry's
    /// <c>OnPersistenceWriteCompleted</c> and <c>OnSessionStateTransitioned</c> calls — but entries
    /// are otherwise processed independently, one at a time, matching
    /// <see cref="ZoneSessionStateMachine.CompleteTTLExpiryTeardown"/>'s own per-entry loop shape.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// ZoneCrashCleanupHandler.ProcessZoneCrash(
    ///     affectedSessions: new[]
    ///     {
    ///         new GhostZoneCrashSession(accountId: 7u, characterId: 555u, snapshotHp: 42,
    ///             SessionState.Disconnected_SessionActive),
    ///         new GhostZoneCrashSession(accountId: 8u, characterId: 556u, snapshotHp: 90,
    ///             SessionState.Reconnecting),
    ///     },
    ///     persistCharacterState: (characterId, hp) =&gt; persistence.SaveGhostZoneCrash(characterId, hp),
    ///     observer);
    /// </code>
    /// </example>
    public static class ZoneCrashCleanupHandler
    {
        /// <summary>
        /// Executes the CR-GH-11 zone-crash cleanup sequence: for each entry in
        /// <paramref name="affectedSessions"/>, in order, persists that session's pre-disconnect
        /// snapshot, confirms the write, then transitions the session to
        /// <see cref="SessionState.Disconnected_SessionExpired"/> with trigger <c>"ZoneCrash"</c>. No
        /// <c>GhostExpiredEvent</c> or <c>MobDeTargetCommand</c> is ever emitted (CR-GH-11 step 4 —
        /// see class remarks).
        /// </summary>
        /// <param name="affectedSessions">
        /// Every session the caller has already determined is in
        /// <see cref="SessionState.Disconnected_SessionActive"/> or
        /// <see cref="SessionState.Reconnecting"/> for the crashed zone instance (CR-GH-11 step 1), in
        /// the order persistence writes should be issued. Must not be <see langword="null"/> (an
        /// empty list is valid — no ghost/reconnecting sessions were present when the zone crashed).
        /// </param>
        /// <param name="persistCharacterState">
        /// Writes one session's final HP to persistence, called once per
        /// <paramref name="affectedSessions"/> entry with that entry's (<c>characterId</c>,
        /// <c>snapshotHp</c>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnPersistenceWriteCompleted(characterId,
        /// GhostZoneCrash)</c> then <c>OnSessionStateTransitioned(accountId, fromState,
        /// Disconnected_SessionExpired, "ZoneCrash")</c>, once per <paramref name="affectedSessions"/>
        /// entry, in order.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="affectedSessions"/> or <paramref name="persistCharacterState"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// ZoneCrashCleanupHandler.ProcessZoneCrash(
        ///     affectedSessions: new[] { new GhostZoneCrashSession(7u, 555u, 42, SessionState.Disconnected_SessionActive) },
        ///     persistCharacterState: (characterId, hp) =&gt; persistence.SaveGhostZoneCrash(characterId, hp),
        ///     observer);
        /// </code>
        /// </example>
        public static void ProcessZoneCrash(
            IReadOnlyList<GhostZoneCrashSession> affectedSessions,
            Action<uint, int> persistCharacterState
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (affectedSessions == null)
            {
                throw new ArgumentNullException(nameof(affectedSessions));
            }

            if (persistCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistCharacterState));
            }

            for (int i = 0; i < affectedSessions.Count; i++)
            {
                GhostZoneCrashSession session = affectedSessions[i];

                persistCharacterState(session.CharacterId, session.SnapshotHp);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                observer?.OnPersistenceWriteCompleted(session.CharacterId, PersistenceWriteReason.GhostZoneCrash);
#endif

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                observer?.OnSessionStateTransitioned(session.AccountId, session.FromState,
                    SessionState.Disconnected_SessionExpired, "ZoneCrash");
#endif
            }
        }
    }
}
