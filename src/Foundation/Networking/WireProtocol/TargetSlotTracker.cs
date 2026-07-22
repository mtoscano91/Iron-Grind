using System.Collections.Generic;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Per-client target-slot state (Networking Core Story 029, RFR-3/RFR-4/RFR-5): remembers each
    /// client's current target across calls and applies the <c>SetTarget</c> RPC's business
    /// validation (self-target rejection, zone-entity-validity check) once a call has already passed
    /// <see cref="CrossCuttingRpcGuardChain"/>'s transport-boundary guards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Sealed, stateful class — NOT a stateless static</b>, unlike <see cref="RelevanceFilter"/> and
    /// <see cref="MobDeTargetingCoordinator"/>: this class's whole job is remembering each client's
    /// current target across calls, so it owns a <c>clientId -&gt; EntityID</c> dictionary as instance
    /// state, the same way <see cref="CrossCuttingRpcGuardChain"/> owns its own in-memory registries.
    /// </para>
    /// <para>
    /// <b>Division of responsibility:</b> <see cref="ProcessSetTarget"/> does NOT call into
    /// <see cref="CrossCuttingRpcGuardChain"/> itself — that stays the caller's responsibility. The
    /// guard chain owns transport-boundary rejection (EntityID validity, session-ready, rate limit,
    /// ownership); this class owns RFR-specific business validation (self-target, zone-entity
    /// validity) once a call has already passed the guard chain. See the example below for the
    /// expected composition.
    /// </para>
    /// <para>
    /// <b>Atomic replacement (RFR-4):</b> <c>_targetSlots[clientId] = targetEntityId;</c> (the final
    /// step of <see cref="ProcessSetTarget"/>) IS the atomic replacement RFR-4 requires — there is no
    /// separate "remove old, add new" two-step, because there is only ever one value stored per
    /// client. A dictionary assignment is inherently atomic from the perspective of any
    /// single-threaded caller (the server tick loop), so no transition window where both old and new
    /// target are simultaneously represented can exist.
    /// </para>
    /// <para>
    /// <b>No opinion on party membership (RFR-6/EC-RFR-5):</b> this class stores whatever valid
    /// non-self <see cref="EntityID"/> it is given — player, mob, or NPC alike (RFR-6: the exclusivity
    /// rule is the same for all). The separation invariant (suppressing the target-slot EHU when the
    /// target happens to be a party member) is handled entirely downstream, by
    /// <see cref="RelevanceFilter.BuildHealthUpdateSubMessages"/>'s existing <c>targetIsPartyMember</c>
    /// check — this class still stores the target normally in that case.
    /// </para>
    /// <para>
    /// <b>Before-flush/after-flush timing (RFR-3, AC-RFR-03):</b> mutation is synchronous;
    /// <see cref="RelevanceFilter.BuildHealthUpdateSubMessages"/> reads whatever <see cref="GetTarget"/>
    /// returns at the moment it's called. The "before flush" vs. "after flush" distinction is proven
    /// purely by call ordering: call <see cref="ProcessSetTarget"/> then read <see cref="GetTarget"/>
    /// (and build the batch) = the new target is reflected that tick; build the batch first, then call
    /// <see cref="ProcessSetTarget"/> = the old target is reflected that tick, and the new target is
    /// only visible on the next <see cref="GetTarget"/> call representing tick T+1. This class adds no
    /// tick-boundary machinery of its own — matching this epic's established "structural ordering
    /// proof, not a real timer" idiom (see <see cref="MobDeTargetingCoordinator"/>'s remarks on
    /// <c>DE_TARGET_DEADLINE_MS</c>).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var guardChain = new CrossCuttingRpcGuardChain();
    /// var tracker = new TargetSlotTracker();
    /// // ... guardChain.RegisterEntityOwnership / MarkSessionReady set up elsewhere ...
    ///
    /// var descriptor = new InboundRpcDescriptor(clientId: 7, senderEntityId: myEntityId, RpcTypeTag.SetTarget, currentTick: tick);
    /// if (guardChain.Evaluate(descriptor, observer) == RpcGuardResult.Accepted)
    /// {
    ///     SetTargetOutcome outcome = tracker.ProcessSetTarget(
    ///         clientId: 7, ownEntityId: myEntityId, targetEntityId: new EntityID(42), validZoneEntityIds, observer);
    /// }
    /// </code>
    /// </example>
    public sealed class TargetSlotTracker
    {
        // clientId -> current target. Absent from the dictionary, or a stored EntityID.Invalid, both
        // mean "no target" — see GetTarget.
        private readonly Dictionary<uint, EntityID> _targetSlots = new();

        /// <summary>
        /// Returns <paramref name="clientId"/>'s current target, or <see cref="EntityID.Invalid"/> if
        /// the client has no entry (never targeted, or last action was a deselect).
        /// </summary>
        /// <param name="clientId">The connection identity to query.</param>
        /// <example>
        /// <code>
        /// EntityID currentTarget = tracker.GetTarget(clientId: 7);
        /// </code>
        /// </example>
        public EntityID GetTarget(uint clientId)
        {
            return _targetSlots.TryGetValue(clientId, out EntityID target) ? target : EntityID.Invalid;
        }

        /// <summary>
        /// Processes a <see cref="SetTarget"/> RPC that has already passed
        /// <see cref="CrossCuttingRpcGuardChain.Evaluate"/> (see class remarks on division of
        /// responsibility). Order of checks matters and is exercised by this story's own tests
        /// (RFR-3a): deselect always succeeds; self-target is rejected before the zone-validity
        /// check; a target not present in <paramref name="validZoneEntityIds"/> is rejected.
        /// </summary>
        /// <param name="clientId">The connection identity that sent the RPC.</param>
        /// <param name="ownEntityId">The sending client's own <c>EntityID</c> (for the self-target check, RFR-5).</param>
        /// <param name="targetEntityId">
        /// The entity to target, or <see cref="EntityID.Invalid"/> to deselect (RFR-3a's wire
        /// convention).
        /// </param>
        /// <param name="validZoneEntityIds">
        /// Every <see cref="EntityID"/> currently valid in the client's zone (RFR-3a: "must be a valid
        /// EntityID present in the current zone"). A plain caller-supplied collection, not an injected
        /// provider/service interface — no real zone entity registry exists yet in this codebase (same
        /// forward-dependency idiom as <see cref="MobDeTargetingCoordinator"/>'s <c>targetingMobIds</c>
        /// and <see cref="PartyDisbandCoordinator"/>'s <c>ghostedPartyMemberCharacterIds</c>). Ignored
        /// entirely — may be <see langword="null"/> — when <paramref name="targetEntityId"/> is
        /// <see cref="EntityID.Invalid"/> (deselect always succeeds, regardless of this collection's
        /// contents).
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <returns>The outcome of processing this RPC — see <see cref="SetTargetOutcome"/>.</returns>
        /// <example>
        /// <code>
        /// SetTargetOutcome outcome = tracker.ProcessSetTarget(
        ///     clientId: 7, ownEntityId: myEntityId, targetEntityId: new EntityID(42), validZoneEntityIds, observer);
        /// </code>
        /// </example>
        public SetTargetOutcome ProcessSetTarget(
            uint clientId,
            EntityID ownEntityId,
            EntityID targetEntityId,
            IReadOnlyCollection<EntityID> validZoneEntityIds
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            bool isDeselect = targetEntityId == EntityID.Invalid;

            if (!isDeselect)
            {
                // Guard: self-target (RFR-5/EC-RFR-4) — checked before zone-validity, matching this
                // codebase's convention that guard order is exercised by tests.
                if (targetEntityId == ownEntityId)
                {
                    Debug.LogWarning($"[TargetSlotTracker] ProcessSetTarget: SelfTargetAttempt — " +
                        $"clientId={clientId} attempted to target its own EntityID {ownEntityId} — " +
                        "rejected silently, target slot unchanged (RFR-5/EC-RFR-4).");

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                    observer?.OnSelfTargetAttemptLogged(ownEntityId.RawValue);
#endif

                    return SetTargetOutcome.RejectedSelfTarget;
                }

                // Guard: zone-entity validity (RFR-3a) — "must be a valid EntityID present in the
                // current zone... Invalid EntityIDs are discarded silently."
                if (!ContainsEntity(validZoneEntityIds, targetEntityId))
                {
                    Debug.LogWarning($"[TargetSlotTracker] ProcessSetTarget: InvalidTargetEntityId — " +
                        $"clientId={clientId} targeted EntityID {targetEntityId}, not present in the " +
                        "current zone — discarded silently (RFR-3a).");

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                    observer?.OnInvalidTargetEntityIdLogged(clientId, targetEntityId.RawValue);
#endif

                    return SetTargetOutcome.RejectedInvalidTarget;
                }
            }

            // Accepted: deselect, or a valid non-self target. A plain dictionary assignment IS the
            // atomic replacement RFR-4 requires — see class remarks.
            _targetSlots[clientId] = targetEntityId;
            return SetTargetOutcome.Accepted;
        }

        private static bool ContainsEntity(IReadOnlyCollection<EntityID> candidates, EntityID target)
        {
            if (candidates == null)
            {
                return false;
            }

            foreach (EntityID candidate in candidates)
            {
                if (candidate == target)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
