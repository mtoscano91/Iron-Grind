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
    /// for this class to consume instead (same forward-dependency shape as the composition root's
    /// single registration of <c>ZoneTickPipeline.Tick</c> with <see cref="ServerTickLoop"/>).
    /// </para>
    /// <para>
    /// <b>Allocation and logging (Performance Budget, ADR-014 Decision 4a):</b> every guard is an
    /// O(1) dictionary/hash-set lookup or a tick-number comparison, so the accept path allocates
    /// nothing. A rejection logs an anomaly through <see cref="Debug.LogWarning(object)"/> only the
    /// first time per <c>(client id, result)</c> pair per tick; every further rejection with the same
    /// pair on that tick only increments a counter (no string is built). The counts are reported in
    /// one summary line per tick, by <see cref="FlushRejectionSummary"/> or when
    /// <see cref="Evaluate"/> first sees a different tick. The throttle storage is created once with
    /// the chain and reused. Return values and the <c>OnSkillUsedRateLimitRejected</c> observer hook
    /// are never throttled.
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

        // Rejection-log throttle (ADR-014 Decision 4a). Created once, cleared per tick, never
        // recreated. _throttleTick is the tick the pairs and counts below belong to.
        // The pair (client id, result) is packed into one ulong — client id in the high bits, the
        // byte-sized result in the low 8 — so the set never hashes an enum (possible boxing under
        // IL2CPP, cf. CharacterStats). Pre-sized for every client of a zone rejecting with every
        // rejecting result, so the set does not grow on a rejection.
        private const int RejectingResultCount = 4;
        private const int ThrottlePairCapacity = ZoneBufferPool.MAX_PLAYERS_PER_ZONE * RejectingResultCount;
        private uint _throttleTick;
        private readonly HashSet<ulong> _loggedThisTick = new(ThrottlePairCapacity);
        private int _suppressedUnknownEntity;
        private int _suppressedSessionNotReady;
        private int _suppressedRateLimited;
        private int _suppressedNotOwner;

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
        /// Read-only liveness query: true when <paramref name="entityId"/> is registered, owned by
        /// <paramref name="clientId"/>, and <paramref name="clientId"/> is session-ready. Writes
        /// nothing and logs nothing, so a caller can re-check a held request without spending a
        /// rate-limit slot. It does not know about connections a dispatcher has marked removed.
        /// </summary>
        /// <param name="clientId">The connection identity expected to own the entity.</param>
        /// <param name="entityId">The entity to check.</param>
        /// <example>
        /// <code>
        /// if (guardChain.IsLiveOwner(clientId: 7, entityId: myEntityId))
        /// {
        ///     // the held request may still be delivered
        /// }
        /// </code>
        /// </example>
        public bool IsLiveOwner(uint clientId, EntityID entityId)
        {
            return _entityOwners.TryGetValue(entityId, out uint ownerClientId)
                && ownerClientId == clientId
                && _sessionReadyClients.Contains(clientId);
        }

        /// <summary>
        /// Returns whether <paramref name="rpcTypeTag"/> has a rate limit (a required tick gap above 0).
        /// Throws <see cref="ArgumentOutOfRangeException"/> for a value that is not an
        /// <see cref="RpcTypeTag"/> member.
        /// </summary>
        /// <param name="rpcTypeTag">The tag to query.</param>
        /// <example>
        /// <code>
        /// bool limited = CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.NotifySkillUsed); // true
        /// </code>
        /// </example>
        public static bool IsRateLimited(RpcTypeTag rpcTypeTag)
        {
            return GetRequiredTickGap(rpcTypeTag) > 0;
        }

        /// <summary>
        /// Logs one warning with the number of rejections whose log was suppressed on the current
        /// throttle tick, per result, then zeroes the counts. Logs nothing when nothing was
        /// suppressed. Only the counts are cleared: the <c>(client id, result)</c> pairs already
        /// logged stay until the tick changes, so a further rejection with the same pair on the same
        /// tick is counted, not logged in full.
        /// </summary>
        /// <example>
        /// <code>
        /// // at the end of the tick's dispatch:
        /// guardChain.FlushRejectionSummary();
        /// </code>
        /// </example>
        public void FlushRejectionSummary()
        {
            LogSummaryAndZeroCounts();
        }

        /// <summary>
        /// Runs <paramref name="descriptor"/> through the four-stage guard pipeline (EntityID
        /// validity → session-ready → rate limit → ownership) and returns the outcome. Throws
        /// <see cref="ArgumentOutOfRangeException"/> only for an <see cref="RpcTypeTag"/> value that
        /// is not an enum member; never forwards to game logic itself (that remains the caller's
        /// responsibility once <see cref="RpcGuardResult.Accepted"/> is returned). A rejection logs
        /// an anomaly via <see cref="Debug.LogWarning(object)"/> the first time per
        /// <c>(client id, result)</c> pair per tick; further identical rejections on that tick are
        /// counted and reported by <see cref="FlushRejectionSummary"/> or when a later tick is seen.
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
            // A different tick starts a new throttle window: report the earlier tick's suppressed
            // rejections first, then clear the pairs and counts.
            if (descriptor.CurrentTick != _throttleTick)
            {
                LogSummaryAndZeroCounts();
                _loggedThisTick.Clear();
                _throttleTick = descriptor.CurrentTick;
            }

            // Guard 1: EntityID validity (Cross-Cutting Constraint 1). Unknown EntityID -> drop,
            // regardless of session-ready/rate-limit/ownership state.
            if (!_entityOwners.TryGetValue(descriptor.SenderEntityId, out uint ownerClientId))
            {
                if (ShouldLogInFull(descriptor.ClientId, RpcGuardResult.RejectedUnknownEntity))
                {
                    Debug.LogWarning($"[CrossCuttingRpcGuardChain] Evaluate: RPC tag={descriptor.RpcTypeTag} from " +
                        $"clientId={descriptor.ClientId} references unknown EntityID {descriptor.SenderEntityId} — " +
                        "dropping, not forwarded to game logic (Cross-Cutting Constraint 1).");
                }
                return RpcGuardResult.RejectedUnknownEntity;
            }

            // Guard 2: session-ready (Cross-Cutting Constraint 2, AC-NC-23). Dropped, not queued for
            // once SessionReady is later sent.
            if (!_sessionReadyClients.Contains(descriptor.ClientId))
            {
                if (ShouldLogInFull(descriptor.ClientId, RpcGuardResult.RejectedSessionNotReady))
                {
                    Debug.LogWarning($"[CrossCuttingRpcGuardChain] Evaluate: RPC tag={descriptor.RpcTypeTag} from " +
                        $"clientId={descriptor.ClientId} arrived before SessionReady — dropping, not queued " +
                        "(Cross-Cutting Constraint 2, AC-NC-23).");
                }
                return RpcGuardResult.RejectedSessionNotReady;
            }

            // Guard 3: rate limit (Cross-Cutting Constraint 3, AC-NC-20, AC-NC-46). Tick-based only —
            // see class remarks for the StaleDiscardComparer.IsTickExpired reuse. A gap of 0 means
            // no rate limit: the last-accepted-tick read and write are both skipped.
            var rateLimitKey = (entityId: descriptor.SenderEntityId, rpcTypeTag: descriptor.RpcTypeTag);
            int requiredTickGap = GetRequiredTickGap(descriptor.RpcTypeTag);
            if (requiredTickGap > 0 && _lastAcceptedTick.TryGetValue(rateLimitKey, out uint lastAcceptedTick))
            {
                uint requiredTick = lastAcceptedTick + (uint)requiredTickGap;
                if (!StaleDiscardComparer.IsTickExpired(descriptor.CurrentTick, requiredTick))
                {
                    if (ShouldLogInFull(descriptor.ClientId, RpcGuardResult.RejectedRateLimited))
                    {
                        Debug.LogWarning($"[CrossCuttingRpcGuardChain] Evaluate: RPC tag={descriptor.RpcTypeTag} from " +
                            $"clientId={descriptor.ClientId} rejected — RateLimitExceeded (last accepted tick " +
                            $"{lastAcceptedTick}, required gap {requiredTickGap} ticks, current tick " +
                            $"{descriptor.CurrentTick}).");
                    }

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
                if (ShouldLogInFull(descriptor.ClientId, RpcGuardResult.RejectedNotOwner))
                {
                    Debug.LogWarning($"[CrossCuttingRpcGuardChain] Evaluate: RPC tag={descriptor.RpcTypeTag} from " +
                        $"clientId={descriptor.ClientId} references EntityID {descriptor.SenderEntityId} owned by " +
                        $"clientId={ownerClientId} — dropping, not forwarded to game logic (AC-NC-02).");
                }
                return RpcGuardResult.RejectedNotOwner;
            }

            if (requiredTickGap > 0)
            {
                _lastAcceptedTick[rateLimitKey] = descriptor.CurrentTick;
            }
            return RpcGuardResult.Accepted;
        }

        // Decides between "log in full" and "count". Returns true for the first rejection of a
        // (client, result) pair on the throttle's tick; otherwise increments that result's counter
        // and returns false. Callers build the log message only inside the true branch.
        private bool ShouldLogInFull(uint clientId, RpcGuardResult result)
        {
            if (_loggedThisTick.Add(((ulong)clientId << 8) | (byte)result))
            {
                return true;
            }

            switch (result)
            {
                case RpcGuardResult.RejectedUnknownEntity:
                    _suppressedUnknownEntity++;
                    break;
                case RpcGuardResult.RejectedSessionNotReady:
                    _suppressedSessionNotReady++;
                    break;
                case RpcGuardResult.RejectedRateLimited:
                    _suppressedRateLimited++;
                    break;
                case RpcGuardResult.RejectedNotOwner:
                    _suppressedNotOwner++;
                    break;
            }
            return false;
        }

        // Shared by FlushRejectionSummary and the tick-change path in Evaluate. Logs one line if
        // anything was suppressed, then zeroes the counts (the logged pairs are left untouched).
        private void LogSummaryAndZeroCounts()
        {
            int total = _suppressedUnknownEntity + _suppressedSessionNotReady
                + _suppressedRateLimited + _suppressedNotOwner;
            if (total == 0)
            {
                return;
            }

            Debug.LogWarning($"[CrossCuttingRpcGuardChain] Tick {_throttleTick}: {total} further rejections not logged " +
                $"(UnknownEntity={_suppressedUnknownEntity}, SessionNotReady={_suppressedSessionNotReady}, " +
                $"RateLimited={_suppressedRateLimited}, NotOwner={_suppressedNotOwner}).");

            _suppressedUnknownEntity = 0;
            _suppressedSessionNotReady = 0;
            _suppressedRateLimited = 0;
            _suppressedNotOwner = 0;
        }

        /// <summary>
        /// Resolves the minimum inter-request tick gap for <paramref name="rpcTypeTag"/>; 0 means no
        /// rate limit. Every <see cref="RpcTypeTag"/> member is listed. A value that is not a member
        /// throws, and a test enumerates the enum so that a new member without a
        /// <c>case</c> here fails the suite instead of silently bypassing rate limiting (ADR-014
        /// Decision 4a).
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
                    // RPCs: no rate limit specified at MVP", Story 029). A gap of 0 means no rate
                    // limit: Evaluate neither reads nor writes _lastAcceptedTick for this tag, so it
                    // never rejects on rate-limit grounds (ADR-014 Decision 4a).
                    return 0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rpcTypeTag), rpcTypeTag,
                        "CrossCuttingRpcGuardChain has no configured rate limit for this RpcTypeTag.");
            }
        }
    }
}
