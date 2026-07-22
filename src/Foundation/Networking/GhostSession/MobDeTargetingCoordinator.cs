using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// Stateless static sequencer for the CR-GH-6/CR-GH-7 mob de-targeting sequence (Networking Core
    /// Story 019, AC-GH-5): fires the ghost-combat-TTL-expiry signal, then issues a
    /// <c>MobDeTargetCommand</c> for every mob currently targeting the expired ghost. Mirrors
    /// <see cref="GhostCleanupSequencer"/>'s stateless-static shape — this class owns only
    /// sequencing of caller-supplied delegate calls, no per-entity state of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stateless static class, mirroring <see cref="GhostCleanupSequencer"/>'s shape, not
    /// <see cref="ConnectionStateMachine"/>'s or <see cref="GhostEntityTracker"/>'s:</b> every call
    /// is fully described by its arguments — nothing is tracked between calls. This class never
    /// itself maintains a mob-to-target mapping; the caller supplies
    /// <c>targetingMobIds</c> for whichever mobs currently target the expired ghost.
    /// </para>
    /// <para>
    /// <b><paramref name="ProcessTTLExpiry"/>'s <c>issueMobDeTargetCommand</c> is a delegate seam
    /// standing in for the not-yet-built AI subsystem's <c>MobDeTargetCommand</c> receiver</b> (per
    /// this story's own Implementation Notes: "implement against a mock/stub AI subsystem
    /// interface... wire the real AI subsystem when that epic exists").
    /// <see cref="INetworkTestObserver"/> has no callback for <c>MobDeTargetCommand</c> issuance
    /// (confirmed by reading the interface directly) and none was added — this delegate is the
    /// resolution, matching this codebase's established delegate-seam precedent
    /// (<see cref="ConnectionStateMachine.ProcessExplicitDisconnect"/>'s
    /// <c>broadcastPlayerLeftZone</c>, <see cref="GhostCleanupSequencer"/>'s
    /// <c>broadcastGhostExpiredEvent</c>).
    /// </para>
    /// <para>
    /// <b>The <c>DE_TARGET_DEADLINE_MS</c> (250ms default = <c>ZONE_TICK_MS + 2×MOB_AI_TICK_MS</c>,
    /// F-GH-2/CR-GH-7) budget is satisfied structurally, not by a real delay or a stored constant on
    /// this class:</b> <see cref="ProcessTTLExpiry"/> fires <c>issueMobDeTargetCommand</c>
    /// synchronously for every targeting mob, with zero simulated latency — the same "structural
    /// ordering, not measured/simulated timing" proof idiom this epic already established for every
    /// prior tick-based timing claim (e.g. <see cref="ConnectionStateMachine.EvaluateTimeouts"/>'s
    /// tick-based thresholds, or Story 018's ordering tests). A test proves the 250ms budget is met
    /// via tick-based call-order assertions, not a <see cref="System.Diagnostics.Stopwatch"/> or
    /// wall-clock measurement. CR-GH-7's own rationale ("log a warning if any mob remains targeted
    /// on an expired ghost after the deadline") is a runtime-monitoring concern for the real AI
    /// subsystem once it exists — out of scope for this delegate-seam proof.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // AC-GH-5 — mob de-targeting on ghost combat TTL expiry:
    /// MobDeTargetingCoordinator.ProcessTTLExpiry(
    ///     entityId: 555u, disconnectTickNumber: 1000u, expiryTick: 1600u,
    ///     targetingMobIds: new uint[] { 42u, 43u },
    ///     issueMobDeTargetCommand: mobId =&gt; aiSubsystem.DeTarget(mobId),
    ///     observer);
    /// </code>
    /// </example>
    public static class MobDeTargetingCoordinator
    {
        /// <summary>
        /// Executes the CR-GH-6/CR-GH-7 mob de-targeting sequence for one expired ghost (AC-GH-5):
        /// fires <c>OnGhostCombatTTLExpired</c>, then issues a <c>MobDeTargetCommand</c> for every
        /// mob in <paramref name="targetingMobIds"/>, in order.
        /// </summary>
        /// <remarks>
        /// Call order (AC-GH-5): <c>OnGhostCombatTTLExpired(entityId, disconnectTickNumber,
        /// expiryTick)</c> → for each mob ID in <paramref name="targetingMobIds"/>, in order:
        /// <paramref name="issueMobDeTargetCommand"/>.
        /// </remarks>
        /// <param name="entityId">The ghost entity whose combat TTL expired.</param>
        /// <param name="disconnectTickNumber">The server tick the underlying disconnect occurred at.</param>
        /// <param name="expiryTick">The server tick at which the ghost combat TTL expired.</param>
        /// <param name="targetingMobIds">
        /// Every mob entity ID currently targeting <paramref name="entityId"/>, in the order
        /// de-target commands should be issued. Must not be <see langword="null"/> (an empty list is
        /// valid — no mobs were targeting the ghost).
        /// </param>
        /// <param name="issueMobDeTargetCommand">
        /// Issues a <c>MobDeTargetCommand</c> for one mob ID, called once per entry in
        /// <paramref name="targetingMobIds"/>. Delegate seam — see class remarks. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="ArgumentNullException">
        /// Either <paramref name="targetingMobIds"/> or <paramref name="issueMobDeTargetCommand"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// MobDeTargetingCoordinator.ProcessTTLExpiry(
        ///     entityId: 555u, disconnectTickNumber: 1000u, expiryTick: 1600u,
        ///     targetingMobIds: new uint[] { 42u, 43u },
        ///     issueMobDeTargetCommand: mobId =&gt; aiSubsystem.DeTarget(mobId),
        ///     observer);
        /// </code>
        /// </example>
        public static void ProcessTTLExpiry(
            uint entityId,
            uint disconnectTickNumber,
            uint expiryTick,
            IReadOnlyList<uint> targetingMobIds,
            Action<uint> issueMobDeTargetCommand
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

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnGhostCombatTTLExpired(entityId, disconnectTickNumber, expiryTick);
#endif

            IssueDeTargetCommands(targetingMobIds, issueMobDeTargetCommand);
        }

        /// <summary>
        /// Issues <paramref name="issueMobDeTargetCommand"/> once per entry in
        /// <paramref name="targetingMobIds"/>, in order — the bare CR-GH-10 steps 1-2 loop shared by
        /// <see cref="ProcessTTLExpiry"/> (which additionally fires <c>OnGhostCombatTTLExpired</c>
        /// first) and <see cref="GhostDismissalCoordinator.ProcessDismissalRequest"/> (Story 021, which
        /// deliberately does NOT fire that TTL-specific event during a voluntary dismissal — see that
        /// class's own remarks). <c>internal</c> rather than <c>private</c> so both call sites in this
        /// assembly can share one loop implementation without duplicating it or misusing
        /// <see cref="ProcessTTLExpiry"/>'s TTL-specific event. Callers are responsible for their own
        /// null-guards on both parameters before calling this helper — it performs none itself.
        /// </summary>
        internal static void IssueDeTargetCommands(IReadOnlyList<uint> targetingMobIds, Action<uint> issueMobDeTargetCommand)
        {
            for (int i = 0; i < targetingMobIds.Count; i++)
            {
                issueMobDeTargetCommand(targetingMobIds[i]);
            }
        }
    }
}
