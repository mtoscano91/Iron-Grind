using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Stateless static sequencer for the CGS-4 (TTL expiry, no death) and CGS-5 (ghost death)
    /// write-ordering guarantees — the ghost-specific instance of CR-NET-5's commit-before-broadcast
    /// principle (Story 011's <see cref="CommitBeforeBroadcastSequencer"/>), applied to the ghost
    /// promotion/cleanup/death boundary (Networking Core Story 018). This class owns only
    /// sequencing of caller-supplied delegate calls — no domain logic, and no per-character state of
    /// its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stateless static class, mirroring <see cref="CommitBeforeBroadcastSequencer"/>'s shape, not
    /// <see cref="ConnectionStateMachine"/>'s or <see cref="GhostEntityTracker"/>'s:</b> unlike those
    /// two registry-holding classes, this sequencer tracks nothing between calls — every call is
    /// fully described by its arguments, exactly matching
    /// <see cref="CommitBeforeBroadcastSequencer.Execute{TOutcome}"/>'s own reasoning for why it is a
    /// stateless static rather than a constructible class.
    /// </para>
    /// <para>
    /// <b>The caller resolves <c>snapshotHp</c> (and, for ghost death, <c>respawnPosition</c>) from
    /// <see cref="PreDisconnectSnapshotWal.TryGetSnapshot"/> before calling either method below —
    /// this class does not own or query the WAL itself.</b> Keeping the WAL lookup as the caller's
    /// responsibility keeps this class's own signature free of a WAL dependency, matching
    /// <see cref="CommitBeforeBroadcastSequencer.Execute{TOutcome}"/>'s "behavior seams, not data"
    /// parameter philosophy (see that class's own remarks).
    /// </para>
    /// <para>
    /// <b><see cref="CompleteTTLExpiryCleanup"/> is a new, ghost-specific parallel path — it never
    /// calls into <see cref="ConnectionStateMachine.CompleteSessionActiveTTLExpiry"/> (Story 015):</b>
    /// that existing method hardcodes <see cref="PersistenceWriteReason.SessionExpiry"/>, which is the
    /// wrong reason code for the ghost-TTL-expiry case (<see cref="PersistenceWriteReason.GhostCombatTTLExpiry"/>
    /// is required instead — AC-CGS-1). Reusing that method would require either weakening its
    /// hardcoded reason code (a change to an already-Complete story's method, out of scope) or
    /// bypassing its own persistence step, neither of which is acceptable. This method independently
    /// reuses only the same <c>"TTLExpired"</c> trigger string for consistency with that method's own
    /// <c>OnSessionStateTransitioned</c> call — the two methods describe the same kind of session
    /// event (TTL elapsed while disconnected) even though one is ghost-specific and reports a
    /// different persistence reason.
    /// </para>
    /// <para>
    /// <b><see cref="CompleteGhostDeathCleanup"/> deliberately does not transition session state:</b>
    /// AC-CGS-2's own restored pass-condition text asserts only the persistence write and its
    /// resulting record contents (HP, position, <c>wasKilledWhileDisconnected</c>) — it does not test
    /// a session-state transition. The death-during-reconnect-race scenario (AC-CGS-3) that DOES need
    /// a state transition is handled by the new
    /// <see cref="ConnectionStateMachine.HandleGhostDeathWhileReconnecting"/> method instead, which
    /// owns the <c>Reconnecting → Disconnected_SessionExpired</c> row directly (it is, after all, a
    /// <see cref="ConnectionStateMachine"/> registry mutation, not something this stateless sequencer
    /// could perform even if it wanted to).
    /// </para>
    /// <para>
    /// <b><c>wasKilledWhileDisconnected = true</c> is data flowing through the caller's
    /// <c>persistCharacterState</c> delegate parameters, not state this class tracks:</b> this
    /// sequencer has no field for it — the caller's own <c>persistCharacterState</c> delegate
    /// implementation is responsible for writing that flag as part of whatever persistence record it
    /// constructs. This class only guarantees the delegate is called before
    /// <c>OnPersistenceWriteCompleted</c>/<c>broadcastGhostExpiredEvent</c>, in that order.
    /// </para>
    /// <para>
    /// <b><paramref name="broadcastGhostExpiredEvent"/> is a delegate seam standing in for the
    /// <c>GhostExpiredEvent</c> R-OD broadcast — no such observer callback exists, and none should be
    /// added</b> (confirmed before implementation): mirrors
    /// <see cref="ConnectionStateMachine.ProcessExplicitDisconnect"/>'s own
    /// <c>broadcastPlayerLeftZone</c> precedent for an event this codebase has no serializer for yet.
    /// </para>
    /// <para>
    /// <b>Story 021 addition — <see cref="CompleteVoluntaryDismissalCleanup"/> (CR-GH-12 step 3):</b>
    /// a third sibling method, exactly mirroring <see cref="CompleteTTLExpiryCleanup"/>'s shape (same
    /// delegate signatures) but hardcoding the <c>"GhostDismissed"</c> reason literal throughout,
    /// instead of duplicating that method's own reasoning for why a single, parameterized
    /// <c>reason</c> parameter was rejected in favor of one method per GDD-determinate scenario (see
    /// the <see cref="CompleteTTLExpiryCleanup"/> paragraph above, and
    /// <see cref="ZoneSessionStateMachine"/>'s own identical precedent for its two teardown methods).
    /// Per CR-GH-12.1, voluntary dismissal applies the identical forfeit policy as TTL expiry — this
    /// method's own <c>snapshotHp</c> parameter plays the exact same role as
    /// <see cref="CompleteTTLExpiryCleanup"/>'s; the caller resolves it from
    /// <see cref="PreDisconnectSnapshotWal.TryGetSnapshot"/> exactly as before. Adds a new
    /// <see cref="PersistenceWriteReason.GhostDismissed"/> enum value (Story 021) — reusing
    /// <see cref="PersistenceWriteReason.GhostCombatTTLExpiry"/> here would mislabel the persistence
    /// write's actual cause, the same reasoning <see cref="CompleteTTLExpiryCleanup"/>'s own remarks
    /// give for why it does not reuse <see cref="ConnectionStateMachine.CompleteSessionActiveTTLExpiry"/>'s
    /// <see cref="PersistenceWriteReason.SessionExpiry"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // AC-CGS-1/AC-CGS-4 — TTL expiry, no death:
    /// GhostCleanupSequencer.CompleteTTLExpiryCleanup(
    ///     accountId: 7, characterId: 555u, snapshotHp: 42,
    ///     persistCharacterState: hp =&gt; persistence.SaveGhostTtlExpiry(555u, hp),
    ///     broadcastGhostExpiredEvent: (characterId, reason) =&gt; zone.BroadcastGhostExpired(characterId, reason),
    ///     observer);
    ///
    /// // AC-CGS-2 — ghost death:
    /// GhostCleanupSequencer.CompleteGhostDeathCleanup(
    ///     characterId: 555u, snapshotHp: 42, respawnPosition: (0, 0, 0),
    ///     persistCharacterState: (hp, position) =&gt; persistence.SaveGhostDeath(555u, hp, position),
    ///     broadcastGhostExpiredEvent: (characterId, reason) =&gt; zone.BroadcastGhostExpired(characterId, reason),
    ///     observer);
    /// </code>
    /// </example>
    public static class GhostCleanupSequencer
    {
        /// <summary>
        /// Completes the CGS-4 ghost TTL-expiry cleanup sequence (AC-CGS-1, AC-CGS-4): persists the
        /// pre-disconnect snapshot HP (never the ghost-period-reduced HP), confirms the write via
        /// <see cref="INetworkTestObserver.OnPersistenceWriteCompleted"/>, transitions session state,
        /// then broadcasts the ghost-expired event — in that exact order.
        /// </summary>
        /// <remarks>
        /// Call order (AC-CGS-1, AC-CGS-4): <paramref name="persistCharacterState"/> →
        /// <c>OnPersistenceWriteCompleted(characterId, GhostCombatTTLExpiry)</c> →
        /// <c>OnSessionStateTransitioned(accountId, Disconnected_SessionActive,
        /// Disconnected_SessionExpired, "TTLExpired")</c> → <paramref name="broadcastGhostExpiredEvent"/>
        /// (with <c>"GhostTtlExpired"</c>). See class remarks for why this does not call into
        /// <see cref="ConnectionStateMachine.CompleteSessionActiveTTLExpiry"/>.
        /// </remarks>
        /// <param name="accountId">The account whose ghosted session's TTL has expired.</param>
        /// <param name="characterId">The character being cleaned up.</param>
        /// <param name="snapshotHp">
        /// The pre-disconnect snapshot HP (resolved by the caller from
        /// <see cref="PreDisconnectSnapshotWal.TryGetSnapshot"/>) — persisted verbatim, never the
        /// ghost-period-reduced HP.
        /// </param>
        /// <param name="persistCharacterState">
        /// Writes the character's final HP to persistence, called with <paramref name="snapshotHp"/>.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <param name="broadcastGhostExpiredEvent">
        /// Broadcasts the <c>GhostExpiredEvent</c>, called with (<paramref name="characterId"/>,
        /// <c>"GhostTtlExpired"</c>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="ArgumentNullException">Either delegate parameter is <see langword="null"/>.</exception>
        /// <example>
        /// <code>
        /// GhostCleanupSequencer.CompleteTTLExpiryCleanup(
        ///     accountId: 7, characterId: 555u, snapshotHp: 42,
        ///     persistCharacterState: hp =&gt; persistence.SaveGhostTtlExpiry(555u, hp),
        ///     broadcastGhostExpiredEvent: (characterId, reason) =&gt; zone.BroadcastGhostExpired(characterId, reason),
        ///     observer);
        /// </code>
        /// </example>
        public static void CompleteTTLExpiryCleanup(
            uint accountId,
            uint characterId,
            int snapshotHp,
            Action<int> persistCharacterState,
            Action<uint, string> broadcastGhostExpiredEvent
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (persistCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistCharacterState));
            }

            if (broadcastGhostExpiredEvent == null)
            {
                throw new ArgumentNullException(nameof(broadcastGhostExpiredEvent));
            }

            persistCharacterState(snapshotHp);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostCombatTTLExpiry);
#endif

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Disconnected_SessionActive,
                SessionState.Disconnected_SessionExpired, "TTLExpired");
#endif

            broadcastGhostExpiredEvent(characterId, "GhostTtlExpired");
        }

        /// <summary>
        /// Completes the CGS-5 ghost-death cleanup sequence (AC-CGS-2): persists the pre-disconnect
        /// snapshot HP unchanged (no HP penalty on ghost death) with the respawn position substituted
        /// in place of the disconnect-moment position, confirms the write, then broadcasts the
        /// ghost-expired event.
        /// </summary>
        /// <remarks>
        /// Call order (AC-CGS-2): <paramref name="persistCharacterState"/> →
        /// <c>OnPersistenceWriteCompleted(characterId, GhostDeath)</c> →
        /// <paramref name="broadcastGhostExpiredEvent"/> (with <c>"GhostDeath"</c>). Deliberately no
        /// session-state-transition step — see class remarks.
        /// </remarks>
        /// <param name="characterId">The character that died while ghosted.</param>
        /// <param name="snapshotHp">
        /// The pre-disconnect snapshot HP (resolved by the caller from
        /// <see cref="PreDisconnectSnapshotWal.TryGetSnapshot"/>) — persisted unchanged; ghost death
        /// applies no HP penalty (GD-CGS-2).
        /// </param>
        /// <param name="respawnPosition">
        /// The zone-entry respawn position, substituted for the disconnect-moment position in the
        /// persisted record.
        /// </param>
        /// <param name="persistCharacterState">
        /// Writes the character's final HP and position to persistence, called with
        /// (<paramref name="snapshotHp"/>, <paramref name="respawnPosition"/>). The caller's own
        /// implementation is responsible for also writing <c>wasKilledWhileDisconnected = true</c> as
        /// part of this same persisted record — this class does not track that flag itself (see class
        /// remarks). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="broadcastGhostExpiredEvent">
        /// Broadcasts the <c>GhostExpiredEvent</c>, called with (<paramref name="characterId"/>,
        /// <c>"GhostDeath"</c>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="ArgumentNullException">Either delegate parameter is <see langword="null"/>.</exception>
        /// <example>
        /// <code>
        /// GhostCleanupSequencer.CompleteGhostDeathCleanup(
        ///     characterId: 555u, snapshotHp: 42, respawnPosition: (0, 0, 0),
        ///     persistCharacterState: (hp, position) =&gt; persistence.SaveGhostDeath(555u, hp, position),
        ///     broadcastGhostExpiredEvent: (characterId, reason) =&gt; zone.BroadcastGhostExpired(characterId, reason),
        ///     observer);
        /// </code>
        /// </example>
        public static void CompleteGhostDeathCleanup(
            uint characterId,
            int snapshotHp,
            (short x, short y, short z) respawnPosition,
            Action<int, (short x, short y, short z)> persistCharacterState,
            Action<uint, string> broadcastGhostExpiredEvent
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (persistCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistCharacterState));
            }

            if (broadcastGhostExpiredEvent == null)
            {
                throw new ArgumentNullException(nameof(broadcastGhostExpiredEvent));
            }

            persistCharacterState(snapshotHp, respawnPosition);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostDeath);
#endif

            broadcastGhostExpiredEvent(characterId, "GhostDeath");
        }

        /// <summary>
        /// Completes the CR-GH-12/CR-GH-12.1 voluntary-dismissal cleanup sequence (AC-GH-11):
        /// persists the pre-disconnect snapshot HP (identical forfeit policy to TTL expiry —
        /// CR-GH-12.1), confirms the write via
        /// <see cref="INetworkTestObserver.OnPersistenceWriteCompleted"/> with the
        /// <see cref="PersistenceWriteReason.GhostDismissed"/> reason, transitions session state with
        /// the <c>"GhostDismissed"</c> trigger, then broadcasts the ghost-expired event with the
        /// matching <c>"GhostDismissed"</c> reason — in that exact order. See class remarks' "Story
        /// 021 addition" paragraph for why this is a new sibling method rather than a parameterized
        /// extension of <see cref="CompleteTTLExpiryCleanup"/>.
        /// </summary>
        /// <remarks>
        /// Call order (AC-GH-11): <paramref name="persistCharacterState"/> →
        /// <c>OnPersistenceWriteCompleted(characterId, GhostDismissed)</c> →
        /// <c>OnSessionStateTransitioned(accountId, Disconnected_SessionActive,
        /// Disconnected_SessionExpired, "GhostDismissed")</c> →
        /// <paramref name="broadcastGhostExpiredEvent"/> (with <c>"GhostDismissed"</c>).
        /// </remarks>
        /// <param name="accountId">The account whose ghosted session is being voluntarily dismissed.</param>
        /// <param name="characterId">The character being cleaned up.</param>
        /// <param name="snapshotHp">
        /// The pre-disconnect snapshot HP (resolved by the caller from
        /// <see cref="PreDisconnectSnapshotWal.TryGetSnapshot"/>) — persisted verbatim, never the
        /// ghost-period-reduced HP (CR-GH-12.1).
        /// </param>
        /// <param name="persistCharacterState">
        /// Writes the character's final HP to persistence, called with <paramref name="snapshotHp"/>.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <param name="broadcastGhostExpiredEvent">
        /// Broadcasts the <c>GhostExpiredEvent</c>, called with (<paramref name="characterId"/>,
        /// <c>"GhostDismissed"</c>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="ArgumentNullException">Either delegate parameter is <see langword="null"/>.</exception>
        /// <example>
        /// <code>
        /// GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup(
        ///     accountId: 7, characterId: 555u, snapshotHp: 42,
        ///     persistCharacterState: hp =&gt; persistence.SaveGhostDismissal(555u, hp),
        ///     broadcastGhostExpiredEvent: (characterId, reason) =&gt; zone.BroadcastGhostExpired(characterId, reason),
        ///     observer);
        /// </code>
        /// </example>
        public static void CompleteVoluntaryDismissalCleanup(
            uint accountId,
            uint characterId,
            int snapshotHp,
            Action<int> persistCharacterState,
            Action<uint, string> broadcastGhostExpiredEvent
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (persistCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistCharacterState));
            }

            if (broadcastGhostExpiredEvent == null)
            {
                throw new ArgumentNullException(nameof(broadcastGhostExpiredEvent));
            }

            persistCharacterState(snapshotHp);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostDismissed);
#endif

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Disconnected_SessionActive,
                SessionState.Disconnected_SessionExpired, "GhostDismissed");
#endif

            broadcastGhostExpiredEvent(characterId, "GhostDismissed");
        }
    }
}
