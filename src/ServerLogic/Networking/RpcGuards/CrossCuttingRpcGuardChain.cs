using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The cross-cutting guard pipeline every inbound RPC passes through before it reaches game
    /// logic (Networking Core Story 010, Cross-Cutting Constraints 1-3): EntityID validity →
    /// session-ready → rate limit → ownership check. Implemented once, here, as a reusable chain —
    /// not duplicated per RPC type, per the story's own Implementation Notes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Owns its own in-memory registries, matching <see cref="ServerTickLoop"/>'s TTL-registry
    /// precedent (Story 009):</b> this class is the only story-010 code, so it owns
    /// <see cref="RegisterEntityOwnership"/>/<see cref="UnregisterEntityOwnership"/> (the
    /// <c>clientId ↔ EntityID</c> mapping, ADR-004 Decision 4) and
    /// <see cref="MarkSessionReady"/>/<see cref="ClearSessionReady"/>/<see cref="IsSessionReady"/> (the
    /// per-client "has <c>SessionReady</c> been sent" flag) as plain in-memory collections, the same
    /// way <see cref="ServerTickLoop"/> owns its tick-driven delegate list and TTL timer list. A
    /// future story (012 for the session-ready flag's real lifecycle, per this story's own Out of
    /// Scope section) is expected to call these registration methods from the real connection
    /// lifecycle; this story proves only the guard chain's own logic against directly-called
    /// registration methods, via test doubles — no real NGO connection/session registry exists yet
    /// for this class to consume instead (same forward-dependency shape as
    /// <see cref="ServerTickLoop.RegisterTickDriven"/>).
    /// </para>
    /// <para>
    /// <b>Zero allocation on the reject path (Performance Budget):</b> every guard is an O(1)
    /// dictionary/hash-set lookup or a tick-number comparison — no string formatting, boxing, or
    /// collection allocation occurs until <see cref="Debug.LogWarning(object)"/> is called on a
    /// rejection, which is expected and accepted (an anomaly log is required by every rejecting AC;
    /// it is not on any accept-path hot loop).
    /// </para>
    /// <para>
    /// <b>Guard order matters and is exercised by this story's own pipeline-ordering tests:</b> an
    /// unknown <see cref="EntityID"/> is rejected before the session-ready gate is even consulted; a
    /// registered-but-not-yet-session-ready client is rejected before rate limiting or ownership are
    /// consulted; rate limiting is checked before ownership, so a rate-limited request from the
    /// entity's actual owner is still rejected as <see cref="RpcGuardResult.RejectedRateLimited"/>,
    /// not silently accepted because ownership would have passed.
    /// </para>
    /// <para>
    /// <b>Rate limiting is tick-based, never wall-clock</b> (Cross-Cutting Constraint 3): the last
    /// accepted tick for each <c>(EntityID, RpcTypeTag)</c> pair is compared against
    /// <see cref="InboundRpcDescriptor.CurrentTick"/> via
    /// <see cref="StaleDiscardComparer.IsTickExpired"/> — the same wraparound-safe RFC 1982 helper
    /// every other stale-discard check in this folder routes through, reused here rather than
    /// reimplemented. <see cref="ALLOC_FREE_POINT_RATE_LIMIT_MS"/> (200ms → 4 ticks at
    /// <see cref="ServerTickLoop.TICK_RATE_HZ"/>) and <see cref="NOTIFY_SKILL_USED_RATE_LIMIT_MS"/>
    /// (50ms → 1 tick, "one per tick at 20Hz, the physical maximum for a legitimate client" per the
    /// story text) are both compile-time tuning knobs, matching this folder's other structural
    /// constants — not external config, per this story's own Implementation Notes value ranges
    /// (<c>[100,1000]</c> and <c>[25,200]</c> respectively).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var guardChain = new CrossCuttingRpcGuardChain();
    /// guardChain.RegisterEntityOwnership(clientId: 7, entityId: myEntityId);
    /// guardChain.MarkSessionReady(clientId: 7);
    ///
    /// var descriptor = new InboundRpcDescriptor(
    ///     clientId: 7,
    ///     senderEntityId: myEntityId,
    ///     rpcTypeTag: RpcTypeTag.AllocateFreePoint,
    ///     currentTick: tickLoop.ServerTickNumber);
    ///
    /// RpcGuardResult result = guardChain.Evaluate(descriptor);
    /// if (result == RpcGuardResult.Accepted)
    /// {
    ///     // ... forward to the real AllocateFreePointRequest handler ...
    /// }
    /// </code>
    /// </example>
    public sealed class CrossCuttingRpcGuardChain
    {
        /// <summary>
        /// The minimum interval, in milliseconds, between two accepted <c>AllocateFreePoint</c>
        /// requests for the same entity (Cross-Cutting Constraint 3, AC-NC-20). Tuning knob, valid
        /// range <c>[100,1000]</c> per this story's Implementation Notes.
        /// </summary>
        public const int ALLOC_FREE_POINT_RATE_LIMIT_MS = 200;

        /// <summary>
        /// The minimum interval, in milliseconds, between two accepted <c>NotifySkillUsed</c>
        /// requests for the same entity (Cross-Cutting Constraint 3, AC-NC-46) — one per tick at
        /// <see cref="ServerTickLoop.TICK_RATE_HZ"/>, the physical maximum for a legitimate client.
        /// Tuning knob, valid range <c>[25,200]</c> per this story's Implementation Notes.
        /// </summary>
        public const int NOTIFY_SKILL_USED_RATE_LIMIT_MS = 50;

        private const int AllocFreePointRateLimitTicks =
            (ALLOC_FREE_POINT_RATE_LIMIT_MS * ServerTickLoop.TICK_RATE_HZ) / 1000;

        private const int NotifySkillUsedRateLimitTicks =
            (NOTIFY_SKILL_USED_RATE_LIMIT_MS * ServerTickLoop.TICK_RATE_HZ) / 1000;

        // Guard 1 + Guard 4 registry: EntityID -> the clientId that owns it. Existence answers the
        // validity guard; value-equality against the descriptor's ClientId answers the ownership
        // guard. One registry, two checks, at two different pipeline stages (see class remarks).
        private readonly Dictionary<EntityID, uint> _entityOwners = new();

        // Guard 2 registry: the set of clientIds that have received SessionReady.
        private readonly HashSet<uint> _sessionReadyClients = new();

        // Guard 3 registry: the last accepted tick per (EntityID, RpcTypeTag) pair.
        private readonly Dictionary<(EntityID entityId, RpcTypeTag rpcTypeTag), uint> _lastAcceptedTick = new();

        /// <summary>
        /// Registers <paramref name="entityId"/> as owned by <paramref name="clientId"/> (ADR-004
        /// Decision 4's <c>clientId ↔ EntityID</c> mapping). Overwrites any prior owner for the same
        /// <paramref name="entityId"/> — this class does not itself decide when re-registration is
        /// valid; that policy belongs to whichever future story drives this call from the real
        /// connection lifecycle (character select, zone entry, reconnect).
        /// </summary>
        /// <param name="clientId">The connection identity that owns <paramref name="entityId"/>.</param>
        /// <param name="entityId">The entity being registered.</param>
        /// <example>
        /// <code>
        /// guardChain.RegisterEntityOwnership(clientId: 7, entityId: myEntityId);
        /// </code>
        /// </example>
        public void RegisterEntityOwnership(uint clientId, EntityID entityId)
        {
            _entityOwners[entityId] = clientId;
        }

        /// <summary>
        /// Removes <paramref name="entityId"/> from the ownership registry (e.g. on disconnect or
        /// zone exit). After this call, any RPC referencing <paramref name="entityId"/> is rejected
        /// with <see cref="RpcGuardResult.RejectedUnknownEntity"/>, not
        /// <see cref="RpcGuardResult.RejectedNotOwner"/>.
        /// </summary>
        /// <param name="entityId">The entity to unregister.</param>
        /// <returns><see langword="true"/> if <paramref name="entityId"/> was registered and removed.</returns>
        /// <example>
        /// <code>
        /// guardChain.UnregisterEntityOwnership(myEntityId);
        /// </code>
        /// </example>
        public bool UnregisterEntityOwnership(EntityID entityId)
        {
            return _entityOwners.Remove(entityId);
        }

        /// <summary>
        /// Marks <paramref name="clientId"/> as having received <c>SessionReady</c> (Cross-Cutting
        /// Constraint 2). Consumer-only method — this story does not own <c>SessionReady</c>'s real
        /// emission lifecycle (Story 012/013 do); it only consumes the flag.
        /// </summary>
        /// <param name="clientId">The connection identity to mark ready.</param>
        /// <example>
        /// <code>
        /// guardChain.MarkSessionReady(clientId: 7);
        /// </code>
        /// </example>
        public void MarkSessionReady(uint clientId)
        {
            _sessionReadyClients.Add(clientId);
        }

        /// <summary>
        /// Clears <paramref name="clientId"/>'s <c>SessionReady</c> flag (e.g. on disconnect). After
        /// this call, any RPC from <paramref name="clientId"/> is rejected with
        /// <see cref="RpcGuardResult.RejectedSessionNotReady"/>.
        /// </summary>
        /// <param name="clientId">The connection identity to clear.</param>
        /// <example>
        /// <code>
        /// guardChain.ClearSessionReady(clientId: 7);
        /// </code>
        /// </example>
        public void ClearSessionReady(uint clientId)
        {
            _sessionReadyClients.Remove(clientId);
        }

        /// <summary>Returns whether <paramref name="clientId"/> has received <c>SessionReady</c>.</summary>
        /// <param name="clientId">The connection identity to query.</param>
        /// <example>
        /// <code>
        /// bool ready = guardChain.IsSessionReady(clientId: 7);
        /// </code>
        /// </example>
        public bool IsSessionReady(uint clientId)
        {
            return _sessionReadyClients.Contains(clientId);
        }

        /// <summary>
        /// Runs <paramref name="descriptor"/> through the four-stage guard pipeline (EntityID
        /// validity → session-ready → rate limit → ownership) and returns the outcome. Never throws
        /// on a rejecting path; never forwards to game logic itself (that remains the caller's
        /// responsibility once <see cref="RpcGuardResult.Accepted"/> is returned). Every rejection
        /// logs an anomaly via <see cref="Debug.LogWarning(object)"/>, per this story's every
        /// blocking AC.
        /// </summary>
        /// <param name="descriptor">The inbound RPC to evaluate.</param>
        /// <param name="observer">
        /// Optional test/dev-build observer. When <paramref name="descriptor"/>'s
        /// <see cref="InboundRpcDescriptor.RpcTypeTag"/> is <see cref="RpcTypeTag.NotifySkillUsed"/>
        /// and the outcome is <see cref="RpcGuardResult.RejectedRateLimited"/>, its
        /// <c>OnSkillUsedRateLimitRejected</c> hook fires (AC-NC-46). No observer hook exists for the
        /// other three rejection reasons or for <c>AllocateFreePoint</c> rate-limit rejections —
        /// those are asserted directly against this method's return value. This parameter only
        /// exists inside <c>#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD</c> — see remarks on why the
        /// observer's interface type is never referenced in an unguarded production context (Story
        /// 002 release-stripping contract, AC-TC-02).
        /// </param>
        /// <returns>
        /// <see cref="RpcGuardResult.Accepted"/> if every guard passed (and this call's tick is
        /// recorded as the new "last accepted tick" for rate-limiting purposes); otherwise the
        /// specific rejection reason.
        /// </returns>
        /// <example>
        /// <code>
        /// RpcGuardResult result = guardChain.Evaluate(descriptor, observer: myTestObserver);
        /// if (result != RpcGuardResult.Accepted)
        /// {
        ///     return; // dropped — never forwarded to game logic, never queued
        /// }
        /// </code>
        /// </example>
        public RpcGuardResult Evaluate(
            InboundRpcDescriptor descriptor
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            // Guard 1: EntityID validity (Cross-Cutting Constraint 1). Unknown EntityID -> drop,
            // regardless of session-ready/rate-limit/ownership state.
            if (!_entityOwners.TryGetValue(descriptor.SenderEntityId, out uint ownerClientId))
            {
                Debug.LogWarning($"[CrossCuttingRpcGuardChain] Evaluate: RPC tag={descriptor.RpcTypeTag} from " +
                    $"clientId={descriptor.ClientId} references unknown EntityID {descriptor.SenderEntityId} — " +
                    "dropping, not forwarded to game logic (Cross-Cutting Constraint 1).");
                return RpcGuardResult.RejectedUnknownEntity;
            }

            // Guard 2: session-ready (Cross-Cutting Constraint 2, AC-NC-23). Dropped, not queued for
            // once SessionReady is later sent.
            if (!_sessionReadyClients.Contains(descriptor.ClientId))
            {
                Debug.LogWarning($"[CrossCuttingRpcGuardChain] Evaluate: RPC tag={descriptor.RpcTypeTag} from " +
                    $"clientId={descriptor.ClientId} arrived before SessionReady — dropping, not queued " +
                    "(Cross-Cutting Constraint 2, AC-NC-23).");
                return RpcGuardResult.RejectedSessionNotReady;
            }

            // Guard 3: rate limit (Cross-Cutting Constraint 3, AC-NC-20, AC-NC-46). Tick-based only —
            // see class remarks for the StaleDiscardComparer.IsTickExpired reuse.
            var rateLimitKey = (entityId: descriptor.SenderEntityId, rpcTypeTag: descriptor.RpcTypeTag);
            int requiredTickGap = GetRequiredTickGap(descriptor.RpcTypeTag);
            if (_lastAcceptedTick.TryGetValue(rateLimitKey, out uint lastAcceptedTick))
            {
                uint requiredTick = lastAcceptedTick + (uint)requiredTickGap;
                if (!StaleDiscardComparer.IsTickExpired(descriptor.CurrentTick, requiredTick))
                {
                    Debug.LogWarning($"[CrossCuttingRpcGuardChain] Evaluate: RPC tag={descriptor.RpcTypeTag} from " +
                        $"clientId={descriptor.ClientId} rejected — RateLimitExceeded (last accepted tick " +
                        $"{lastAcceptedTick}, required gap {requiredTickGap} ticks, current tick " +
                        $"{descriptor.CurrentTick}).");

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                    if (descriptor.RpcTypeTag == RpcTypeTag.NotifySkillUsed)
                    {
                        observer?.OnSkillUsedRateLimitRejected(descriptor.SenderEntityId.RawValue);
                    }
#endif

                    return RpcGuardResult.RejectedRateLimited;
                }
            }

            // Guard 4: ownership (AC-NC-02). The entity is known (Guard 1 passed) but not owned by
            // the sending connection.
            if (ownerClientId != descriptor.ClientId)
            {
                Debug.LogWarning($"[CrossCuttingRpcGuardChain] Evaluate: RPC tag={descriptor.RpcTypeTag} from " +
                    $"clientId={descriptor.ClientId} references EntityID {descriptor.SenderEntityId} owned by " +
                    $"clientId={ownerClientId} — dropping, not forwarded to game logic (AC-NC-02).");
                return RpcGuardResult.RejectedNotOwner;
            }

            _lastAcceptedTick[rateLimitKey] = descriptor.CurrentTick;
            return RpcGuardResult.Accepted;
        }

        /// <summary>
        /// Resolves the minimum inter-request tick gap for <paramref name="rpcTypeTag"/>. Only the
        /// two rate-limit buckets this story defines are recognized; an unrecognized value throws
        /// rather than silently defaulting, since a new <see cref="RpcTypeTag"/> member added later
        /// without a corresponding rate limit here would otherwise silently bypass rate limiting.
        /// </summary>
        private static int GetRequiredTickGap(RpcTypeTag rpcTypeTag)
        {
            switch (rpcTypeTag)
            {
                case RpcTypeTag.AllocateFreePoint:
                    return AllocFreePointRateLimitTicks;
                case RpcTypeTag.NotifySkillUsed:
                    return NotifySkillUsedRateLimitTicks;
                case RpcTypeTag.SetTarget:
                    // No rate limit at MVP (networking-core.md Cross-Cutting Constraint 3: "All other
                    // RPCs: no rate limit specified at MVP", Story 029). A gap of 0 means
                    // unconstrained: StaleDiscardComparer.IsTickExpired(currentTick, lastAcceptedTick + 0)
                    // returns true whenever currentTick >= lastAcceptedTick, which always holds for a
                    // monotonic tick loop — so gap=0 never rejects on rate-limit grounds.
                    return 0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rpcTypeTag), rpcTypeTag,
                        "CrossCuttingRpcGuardChain has no configured rate limit for this RpcTypeTag.");
            }
        }
    }
}
