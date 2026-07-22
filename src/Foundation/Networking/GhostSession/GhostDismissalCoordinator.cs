using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// Stateless static coordinator for the CR-GH-12/CR-GH-12.1 voluntary-ghost-dismissal sequence
    /// (Networking Core Story 021, AC-GH-11): validates a <c>GhostDismissRequest</c>, then — on a
    /// valid request — executes the CR-GH-10 de-target/cleanup sequence with the
    /// <c>GHOST_DISMISSED</c> reason substituted throughout. Mirrors
    /// <see cref="PartyDisbandCoordinator"/>'s exact stateless-static shape (Story 020) — this class
    /// owns only sequencing and validation of caller-supplied state and delegate calls, no per-party
    /// or per-character state of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stateless static class, mirroring <see cref="PartyDisbandCoordinator"/>'s and
    /// <see cref="MobDeTargetingCoordinator"/>'s shape, not <see cref="GhostEntityTracker"/>'s or
    /// <see cref="GhostXpPoolTracker"/>'s:</b> every call is fully described by its arguments —
    /// nothing is tracked between calls. The actual registry mutations
    /// (<see cref="GhostEntityTracker.RemoveGhost"/>, <see cref="GhostXpPoolTracker.EndTracking"/>)
    /// are delegated to the caller-supplied instances, not owned here.
    /// </para>
    /// <para>
    /// <b>Mock party-membership provider, per this story's own Implementation Notes:</b> no real
    /// Party System exists yet in this codebase (confirmed — the GDD's own dependency table lists it
    /// as "not yet authored"). <c>requesterIsValidPartyMember</c> is supplied by the caller's own
    /// test-local mock party roster, standing in for the real system's membership query — the same
    /// forward-dependency treatment <see cref="PartyDisbandCoordinator"/> gives the not-yet-built
    /// Party System (see that class's own remarks). A future Party System story is expected to call
    /// this coordinator with a real membership check.
    /// </para>
    /// <para>
    /// <b>Returns <see langword="bool"/> rather than throwing on an invalid request (judgment call,
    /// approved before implementation):</b> CR-GH-12 step 1 describes "the server validates ..." as a
    /// normal negative-response gate, not an exceptional caller-contract violation — the same framing
    /// <see cref="ZoneSessionStateMachine.EvaluateJoinAttempt"/> already established for "the join is
    /// rejected" (a routine, expected outcome, not a bug). This story's own AC-GH-11 has no dedicated
    /// rejection-path acceptance criterion, so either choice was reasonable; a <see langword="bool"/>
    /// return keeps the invalid-request path a cheap, allocation-free check a caller can act on
    /// directly (e.g. send the requester a rejection response), rather than forcing every caller to
    /// wrap a valid, everyday "the requester wasn't a party member" outcome in a try/catch.
    /// </para>
    /// <para>
    /// <b>No <see cref="MobDeTargetingCoordinator.ProcessTTLExpiry"/> call — shares its bare loop
    /// helper instead (judgment call, approved before implementation, per this story's own design
    /// guidance):</b> <see cref="MobDeTargetingCoordinator.ProcessTTLExpiry"/> unconditionally fires
    /// <c>OnGhostCombatTTLExpired</c> before its de-target loop — an observer event whose name is
    /// TTL-expiry-specific. Calling it from this voluntary-dismissal path would mislabel the event
    /// (the ghost was not TTL-expired; it was dismissed). This method instead calls
    /// <see cref="MobDeTargetingCoordinator.IssueDeTargetCommands"/> — the bare CR-GH-10 steps 1-2
    /// loop extracted (code review finding, Story 021) so both call sites share one implementation
    /// without either duplicating the loop or misusing the TTL-specific event. The
    /// <c>DE_TARGET_DEADLINE_MS</c> budget is satisfied structurally here for the exact same reason
    /// <see cref="MobDeTargetingCoordinator"/>'s own remarks give — zero simulated latency, synchronous
    /// delegate calls.
    /// </para>
    /// <para>
    /// <b>"Cancel the TTL timer" (CR-GH-12 step 2) has no code of its own — it is structural, not a
    /// real timer object:</b> mirrors this epic's established "structural, not simulated" timing
    /// idiom (e.g. <see cref="ConnectionStateMachine.RecordFailedReAuthAttempt"/>'s own
    /// "<c>sessionExpiryTick</c> never written" structural proof). Proceeding directly from the
    /// validation gate to the de-target loop and cleanup below, without ever consulting or updating
    /// any TTL-expiry state, IS the cancellation — there is no separate timer object anywhere in this
    /// codebase for a real cancellation call to target.
    /// </para>
    /// <para>
    /// <b>Does not resolve <see cref="GhostXpPoolTracker.ResolveFinalXp"/> internally</b> — matching
    /// Story 020's own precedent (see <c>GhostSession_RewardForfeitPolicy_tests.cs</c>'s remarks on
    /// AC-GH-7/AC-GH-12): resolving the forfeit-vs-restore XP total is the caller's
    /// <paramref name="persistCharacterState"/> delegate's own responsibility, exactly as it already
    /// is for <see cref="GhostCleanupSequencer.CompleteTTLExpiryCleanup"/>'s callers. This method
    /// only calls <see cref="GhostXpPoolTracker.EndTracking"/> — CR-GH-10 step 5's registry release,
    /// not the XP-resolution decision point (CR-GH-9/9.1/9.2, owned entirely by
    /// <see cref="GhostXpPoolTracker.ResolveFinalXp"/>). This is the same already-tracked gap as
    /// TD-020 (<c>docs/tech-debt-register.md</c>) — no story in this epic yet wires
    /// <see cref="GhostXpPoolTracker.ResolveFinalXp"/> into a real, non-test
    /// <paramref name="persistCharacterState"/> call site.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// bool accepted = GhostDismissalCoordinator.ProcessDismissalRequest(
    ///     accountId: 7, characterId: 555u, snapshotHp: 42,
    ///     requesterIsValidPartyMember: true,
    ///     currentState: SessionState.Disconnected_SessionActive,
    ///     targetingMobIds: new uint[] { 42u, 43u },
    ///     issueMobDeTargetCommand: mobId =&gt; aiSubsystem.DeTarget(mobId),
    ///     persistCharacterState: hp =&gt; persistence.SaveGhostDismissal(555u, hp),
    ///     broadcastGhostExpiredEvent: (characterId, reason) =&gt; zone.BroadcastGhostExpired(characterId, reason),
    ///     ghostEntityTracker, xpPoolTracker, observer); // true
    /// </code>
    /// </example>
    public static class GhostDismissalCoordinator
    {
        /// <summary>
        /// Validates and, if valid, executes the CR-GH-12/CR-GH-12.1 voluntary-dismissal sequence for
        /// one ghosted character (AC-GH-11).
        /// </summary>
        /// <remarks>
        /// Call order on a valid request: for each mob ID in <paramref name="targetingMobIds"/>, in
        /// order — <paramref name="issueMobDeTargetCommand"/> (CR-GH-10 steps 1-2, no
        /// <c>OnGhostCombatTTLExpired</c> — see class remarks) — this loop runs to completion
        /// strictly before → <see cref="GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup"/>
        /// (CR-GH-10 steps 3, 6, 7 with <c>GHOST_DISMISSED</c>) →
        /// <see cref="GhostEntityTracker.RemoveGhost"/> (CR-GH-10 step 4) →
        /// <see cref="GhostXpPoolTracker.EndTracking"/> (CR-GH-10 step 5). On an invalid request (see
        /// the <paramref name="requesterIsValidPartyMember"/>/<paramref name="currentState"/>
        /// parameters below), none of the above runs — returns <see langword="false"/> immediately,
        /// with no side effects at all.
        /// </remarks>
        /// <param name="accountId">The account whose ghosted session is being dismissed.</param>
        /// <param name="characterId">
        /// The ghost character being dismissed — also used as the entity ID for the de-target loop
        /// and the XP-pool key (characterId doubles as entityId throughout this cluster; no Character
        /// &lt;-&gt; Entity mapping system exists yet in this codebase).
        /// </param>
        /// <param name="snapshotHp">
        /// The pre-disconnect snapshot HP (resolved by the caller from
        /// <see cref="PreDisconnectSnapshotWal.TryGetSnapshot"/>) — forwarded verbatim to
        /// <see cref="GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup"/>.
        /// </param>
        /// <param name="requesterIsValidPartyMember">
        /// Whether the requesting session is a valid party member of the ghost's party (CR-GH-12
        /// step 1, first half) — caller-supplied mock party-membership check (see class remarks).
        /// </param>
        /// <param name="currentState">
        /// The ghost session's current <see cref="SessionState"/> (CR-GH-12 step 1, second half) —
        /// the request is only valid when this is <see cref="SessionState.Disconnected_SessionActive"/>.
        /// </param>
        /// <param name="targetingMobIds">
        /// Every mob entity ID currently targeting <paramref name="characterId"/>, in the order
        /// de-target commands should be issued (CR-GH-10 steps 1-2). Must not be
        /// <see langword="null"/> (an empty list is valid — no mobs were targeting the ghost).
        /// </param>
        /// <param name="issueMobDeTargetCommand">
        /// Issues a <c>MobDeTargetCommand</c> for one mob ID, called once per entry in
        /// <paramref name="targetingMobIds"/>. Delegate seam — see
        /// <see cref="MobDeTargetingCoordinator"/>'s own remarks. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="persistCharacterState">
        /// Writes the character's final HP to persistence, forwarded to
        /// <see cref="GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup"/>. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="broadcastGhostExpiredEvent">
        /// Broadcasts the <c>GhostExpiredEvent</c>, forwarded to
        /// <see cref="GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup"/>. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="ghostEntityTracker">
        /// The ghost registry to remove <paramref name="characterId"/> from on a valid request
        /// (CR-GH-10 step 4). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="xpPoolTracker">
        /// The XP-pool registry to end tracking on for <paramref name="characterId"/> on a valid
        /// request (CR-GH-10 step 5). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <returns>
        /// <see langword="true"/> if the request was valid and the full cleanup sequence executed;
        /// <see langword="false"/> if the request was rejected (see class remarks — no side effects
        /// in that case).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="targetingMobIds"/>, <paramref name="issueMobDeTargetCommand"/>,
        /// <paramref name="persistCharacterState"/>, <paramref name="broadcastGhostExpiredEvent"/>,
        /// <paramref name="ghostEntityTracker"/>, or <paramref name="xpPoolTracker"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// bool accepted = GhostDismissalCoordinator.ProcessDismissalRequest(
        ///     accountId: 7, characterId: 555u, snapshotHp: 42,
        ///     requesterIsValidPartyMember: true,
        ///     currentState: SessionState.Disconnected_SessionActive,
        ///     targetingMobIds: new uint[] { 42u, 43u },
        ///     issueMobDeTargetCommand: mobId =&gt; aiSubsystem.DeTarget(mobId),
        ///     persistCharacterState: hp =&gt; persistence.SaveGhostDismissal(555u, hp),
        ///     broadcastGhostExpiredEvent: (characterId, reason) =&gt; zone.BroadcastGhostExpired(characterId, reason),
        ///     ghostEntityTracker, xpPoolTracker, observer); // true
        /// </code>
        /// </example>
        public static bool ProcessDismissalRequest(
            uint accountId,
            uint characterId,
            int snapshotHp,
            bool requesterIsValidPartyMember,
            SessionState currentState,
            IReadOnlyList<uint> targetingMobIds,
            Action<uint> issueMobDeTargetCommand,
            Action<int> persistCharacterState,
            Action<uint, string> broadcastGhostExpiredEvent,
            GhostEntityTracker ghostEntityTracker,
            GhostXpPoolTracker xpPoolTracker
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (targetingMobIds == null)
            {
                throw new ArgumentNullException(nameof(targetingMobIds));
            }

            if (issueMobDeTargetCommand == null)
            {
                throw new ArgumentNullException(nameof(issueMobDeTargetCommand));
            }

            if (persistCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistCharacterState));
            }

            if (broadcastGhostExpiredEvent == null)
            {
                throw new ArgumentNullException(nameof(broadcastGhostExpiredEvent));
            }

            if (ghostEntityTracker == null)
            {
                throw new ArgumentNullException(nameof(ghostEntityTracker));
            }

            if (xpPoolTracker == null)
            {
                throw new ArgumentNullException(nameof(xpPoolTracker));
            }

            // CR-GH-12 step 1: validate. Both halves must hold, or the request is rejected outright —
            // no side effects at all (see class remarks for why this returns false rather than
            // throwing).
            if (!requesterIsValidPartyMember || currentState != SessionState.Disconnected_SessionActive)
            {
                return false;
            }

            // CR-GH-12 step 2: "cancel the TTL timer" is structural, not a real timer object — see
            // class remarks. Proceeding directly to the cleanup sequence below IS the cancellation.

            // CR-GH-10 steps 1-2: the shared bare de-target loop (not
            // MobDeTargetingCoordinator.ProcessTTLExpiry itself — see class remarks for the
            // OnGhostCombatTTLExpired mislabeling this avoids).
            MobDeTargetingCoordinator.IssueDeTargetCommands(targetingMobIds, issueMobDeTargetCommand);

            // CR-GH-10 steps 3, 6, 7 (GHOST_DISMISSED substituted throughout).
            GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup(accountId, characterId, snapshotHp,
                persistCharacterState, broadcastGhostExpiredEvent
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                , observer
#endif
                );

            // CR-GH-10 steps 4-5.
            ghostEntityTracker.RemoveGhost(characterId);
            xpPoolTracker.EndTracking(characterId);

            return true;
        }
    }
}
