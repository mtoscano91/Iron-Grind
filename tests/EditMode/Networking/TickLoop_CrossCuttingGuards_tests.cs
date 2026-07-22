using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 010 — the cross-cutting RPC guard chain
    /// (<see cref="CrossCuttingRpcGuardChain"/>). Covers AC-NC-02 (ownership), AC-NC-20
    /// (<c>AllocateFreePoint</c> rate limit), AC-NC-46 (<c>NotifySkillUsed</c> rate limit), and
    /// AC-NC-23 (session-ready gate), plus the guard pipeline's own ordering invariant (EntityID
    /// validity -> session-ready -> rate limit -> ownership) and the per-entity/per-tag scoping of
    /// the rate-limit registry.
    /// </summary>
    /// <remarks>
    /// No concrete <c>AllocateFreePointRequest</c>/<c>NotifySkillUsed</c> message types exist in this
    /// codebase yet (owned by the not-yet-started Leveling System and Skill System epics) — every
    /// test here exercises <see cref="CrossCuttingRpcGuardChain"/> directly against
    /// <see cref="InboundRpcDescriptor"/>, per the story's own generic-descriptor scoping.
    /// </remarks>
    [TestFixture]
    internal sealed class TickLoop_CrossCuttingGuards_Tests
    {
        private static readonly EntityID EntityA = new EntityID(1u);
        private static readonly EntityID EntityB = new EntityID(2u);
        private const uint ClientA = 100u;
        private const uint ClientB = 200u;

        // =========================================================================================
        // AC-NC-23: session-ready gate.
        // =========================================================================================

        [Test]
        public void Evaluate_ClientWithoutSessionReady_RejectsWithSessionNotReadyAndLogsAnomaly()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA); // entity known, but SessionReady never sent
            var descriptor = new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*arrived before SessionReady"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(descriptor);

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedSessionNotReady, result,
                "An RPC from a client that never received SessionReady must be dropped, not forwarded to game logic.");
        }

        [Test]
        public void Evaluate_ClientWithSessionReady_Accepts()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            var descriptor = new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);

            // Act
            RpcGuardResult result = guardChain.Evaluate(descriptor);

            // Assert
            Assert.AreEqual(RpcGuardResult.Accepted, result,
                "A valid, owned, session-ready, non-rate-limited RPC must be accepted.");
        }

        [Test]
        public void Evaluate_ClearSessionReady_SubsequentRpcRejectedWithSessionNotReady()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            guardChain.ClearSessionReady(ClientA);
            var descriptor = new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*arrived before SessionReady"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(descriptor);

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedSessionNotReady, result,
                "ClearSessionReady must make subsequent RPCs from that client rejected again.");
        }

        [Test]
        public void IsSessionReady_ReflectsMarkAndClear()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();

            // Act / Assert
            Assert.IsFalse(guardChain.IsSessionReady(ClientA), "A client not yet marked ready must report false.");
            guardChain.MarkSessionReady(ClientA);
            Assert.IsTrue(guardChain.IsSessionReady(ClientA), "MarkSessionReady must make IsSessionReady report true.");
            guardChain.ClearSessionReady(ClientA);
            Assert.IsFalse(guardChain.IsSessionReady(ClientA), "ClearSessionReady must make IsSessionReady report false again.");
        }

        // =========================================================================================
        // AC-NC-02: ownership check.
        // =========================================================================================

        [Test]
        public void Evaluate_EntityOwnedByAnotherClient_RejectsWithNotOwnerAndLogsAnomaly()
        {
            // Arrange — EntityA belongs to ClientA; ClientB (a different, fully session-ready
            // connection) sends an AllocateFreePointRequest claiming EntityA.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientB);
            var descriptor = new InboundRpcDescriptor(ClientB, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*owned by clientId=100"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(descriptor);

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedNotOwner, result,
                "A client must not be able to act on an EntityID it does not own — the request is discarded (AC-NC-02).");
        }

        [Test]
        public void Evaluate_EntityOwnedByRequestingClient_Accepts()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            var descriptor = new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);

            // Act
            RpcGuardResult result = guardChain.Evaluate(descriptor);

            // Assert
            Assert.AreEqual(RpcGuardResult.Accepted, result,
                "A client acting on its own owned EntityID must be accepted.");
        }

        // =========================================================================================
        // EntityID validity (Cross-Cutting Constraint 1) — distinct from ownership: the entity is
        // not registered to anyone at all, vs. registered to someone else.
        // =========================================================================================

        [Test]
        public void Evaluate_UnregisteredEntity_RejectsWithUnknownEntityAndLogsAnomaly()
        {
            // Arrange — EntityA was never registered to any client at all.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.MarkSessionReady(ClientA);
            var descriptor = new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*references unknown EntityID"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(descriptor);

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedUnknownEntity, result,
                "An RPC referencing an EntityID unknown to the server must be dropped as RejectedUnknownEntity, distinct from RejectedNotOwner.");
        }

        [Test]
        public void UnregisterEntityOwnership_RemovedEntity_SubsequentRpcRejectedAsUnknownNotNotOwner()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);

            // Act
            bool removed = guardChain.UnregisterEntityOwnership(EntityA);
            var descriptor = new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*references unknown EntityID"));
            RpcGuardResult result = guardChain.Evaluate(descriptor);

            // Assert
            Assert.IsTrue(removed, "UnregisterEntityOwnership must report true when a registered entity was removed.");
            Assert.AreEqual(RpcGuardResult.RejectedUnknownEntity, result,
                "After unregistration, even the former owner's RPC must be rejected as unknown, not accepted.");
        }

        [Test]
        public void UnregisterEntityOwnership_NeverRegisteredEntity_ReturnsFalse()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();

            // Act
            bool removed = guardChain.UnregisterEntityOwnership(EntityA);

            // Assert
            Assert.IsFalse(removed, "Unregistering an entity that was never registered must return false.");
        }

        // =========================================================================================
        // Pipeline ordering: EntityID validity -> session-ready -> rate limit -> ownership.
        // =========================================================================================

        [Test]
        public void Evaluate_UnknownEntityAndClientNotSessionReady_RejectsAsUnknownEntityNotSessionNotReady()
        {
            // Arrange — neither guard 1 nor guard 2 would pass; validity must win because it runs first.
            var guardChain = new CrossCuttingRpcGuardChain();
            var descriptor = new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*references unknown EntityID"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(descriptor);

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedUnknownEntity, result,
                "EntityID validity must be checked before session-ready — the ordering the story specifies.");
        }

        [Test]
        public void Evaluate_NotOwnedButNotSessionReady_RejectsAsSessionNotReadyNotNotOwner()
        {
            // Arrange — EntityA is known (owned by ClientA), sent by ClientB (not the owner), and
            // ClientB is not session-ready. Session-ready must be checked before ownership.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            var descriptor = new InboundRpcDescriptor(ClientB, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*arrived before SessionReady"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(descriptor);

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedSessionNotReady, result,
                "Session-ready must be checked before ownership — a not-ready client is rejected for that reason even if it also doesn't own the entity.");
        }

        [Test]
        public void Evaluate_SessionNotReadyButAlsoRateLimited_RejectsAsSessionNotReadyNotRateLimited()
        {
            // Arrange — code-review follow-up (qa-tester gap): the existing ordering tests all use
            // descriptors with no pre-existing rate-limit history, so the rate-limit check is a
            // no-op regardless of where in the pipeline it runs — none of them would catch a bug
            // that swapped guard 2 (session-ready) and guard 3 (rate limit). This test seeds a real
            // rate-limit history first (an accepted request), then clears session-ready, then
            // resubmits within the rate-limit window — proving session-ready is still checked
            // before rate limit even when rate limit would also reject.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u)); // accepted; seeds lastAcceptedTick=10
            guardChain.ClearSessionReady(ClientA);
            var descriptor = new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 11u); // within the 4-tick rate-limit window too
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*arrived before SessionReady"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(descriptor);

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedSessionNotReady, result,
                "Session-ready must be checked before rate limit — a not-ready client is rejected for that reason even when the request would also be rate-limited.");
        }

        [Test]
        public void Evaluate_RateLimitedButNotOwner_RejectsAsRateLimitedNotNotOwner()
        {
            // Arrange — EntityA is owned by ClientA, not ClientB (the sender). Rate limit must be
            // checked before ownership, per the story's specified pipeline order.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientB);
            var nonOwnerFirstAttempt = new InboundRpcDescriptor(ClientB, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*owned by clientId=100"));
            guardChain.Evaluate(nonOwnerFirstAttempt); // rejected as RejectedNotOwner; the accept-path tick record is never written on this path

            // Seed the rate-limit registry via the entity's real owner accepting a request.
            guardChain.MarkSessionReady(ClientA);
            var ownerRequest = new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 10u);
            guardChain.Evaluate(ownerRequest); // accepted; records lastAcceptedTick = 10 for (EntityA, AllocateFreePoint)

            var nonOwnerSecondAttempt = new InboundRpcDescriptor(ClientB, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 11u); // within the 4-tick gap
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*RateLimitExceeded"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(nonOwnerSecondAttempt);

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedRateLimited, result,
                "Rate limit must be checked before ownership — a rate-limited request from a non-owner is rejected for that reason, not RejectedNotOwner.");
        }

        // =========================================================================================
        // AC-NC-20: AllocateFreePoint rate limit — 200ms (4 ticks at 20Hz) boundary.
        // =========================================================================================

        [Test]
        public void Evaluate_AllocateFreePoint_SecondRequestWellWithinTwoHundredMs_RejectsWithRateLimitExceeded()
        {
            // Arrange — two requests 1 tick (50ms) apart, well inside the 4-tick (200ms) minimum gap.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 100u));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*RateLimitExceeded"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 101u));

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedRateLimited, result,
                "A second AllocateFreePoint request within 200ms (4 ticks) of the prior accepted request must be rejected.");
        }

        [Test]
        public void Evaluate_AllocateFreePoint_SecondRequestOneTickBeforeBoundary_Rejects()
        {
            // Arrange — 3 ticks (150ms) later, one tick short of the 4-tick boundary.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 100u));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*RateLimitExceeded"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 103u));

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedRateLimited, result,
                "One tick short of the 200ms boundary must still reject.");
        }

        [Test]
        public void Evaluate_AllocateFreePoint_SecondRequestAtExactlyTwoHundredMs_Accepts()
        {
            // Arrange — exactly 4 ticks (200ms) later — the boundary is inclusive (accepted).
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 100u));

            // Act
            RpcGuardResult result = guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 104u));

            // Assert
            Assert.AreEqual(RpcGuardResult.Accepted, result,
                "A request separated by exactly 200ms (4 ticks) from the prior accepted request must be accepted, per AC-NC-20's inclusive '>=200ms' boundary.");
        }

        [Test]
        public void Evaluate_AllocateFreePoint_RateLimitedWithObserverPassed_DoesNotFireSkillUsedRateLimitObserverHook()
        {
            // Arrange — code-review follow-up (qa-tester gap): every other AllocateFreePoint
            // rate-limit test calls Evaluate without an observer at all, so the
            // `descriptor.RpcTypeTag == RpcTypeTag.NotifySkillUsed` guard around the observer
            // callback (which the class's own doc comment claims exists) was never actually
            // exercised in the negative direction. This test passes a live observer into an
            // AllocateFreePoint rejection and asserts it is never called.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            var observer = new NetworkTestObserver();
            guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 100u), observer);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*RateLimitExceeded"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 101u), observer);

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedRateLimited, result, "The second request must still be rejected as rate-limited.");
            Assert.AreEqual(0, observer.SkillUsedRateLimitRejectedCalls.Count,
                "OnSkillUsedRateLimitRejected must never fire for an AllocateFreePoint rate-limit rejection — that hook is NotifySkillUsed-specific (AC-NC-46).");
        }

        [Test]
        public void Evaluate_AllocateFreePoint_RepeatedAtOneHundredMsIntervals_AlternatesAcceptAndReject()
        {
            // Arrange — AC-NC-20's literal scenario: requests every 100ms (2 ticks). Every other
            // request lands on/after the 4-tick boundary from the last *accepted* request.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            var results = new RpcGuardResult[5];
            LogAssert.ignoreFailingMessages = true; // several RateLimitExceeded warnings expected; asserted via return values instead

            // Act — ticks 0, 2, 4, 6, 8 (100ms apart at 20Hz)
            for (int i = 0; i < 5; i++)
            {
                results[i] = guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: (uint)(i * 2)));
            }

            LogAssert.ignoreFailingMessages = false;

            // Assert — accepted at tick 0 (first ever), tick 4 (gap=4 from tick 0), tick 8 (gap=4
            // from tick 4); rejected at tick 2 (gap=2 from tick 0) and tick 6 (gap=2 from tick 4).
            Assert.AreEqual(RpcGuardResult.Accepted, results[0], "tick 0: first-ever request, always accepted.");
            Assert.AreEqual(RpcGuardResult.RejectedRateLimited, results[1], "tick 2: only 100ms since tick 0, rejected.");
            Assert.AreEqual(RpcGuardResult.Accepted, results[2], "tick 4: exactly 200ms since tick 0, accepted.");
            Assert.AreEqual(RpcGuardResult.RejectedRateLimited, results[3], "tick 6: only 100ms since tick 4, rejected.");
            Assert.AreEqual(RpcGuardResult.Accepted, results[4], "tick 8: exactly 200ms since tick 4, accepted.");
        }

        // =========================================================================================
        // AC-NC-46: NotifySkillUsed rate limit — 50ms (1 tick at 20Hz) boundary, 10 requests at
        // 10ms-equivalent spacing within a 100ms (2-tick) window.
        // =========================================================================================

        [Test]
        public void Evaluate_NotifySkillUsed_TenRequestsWithinTwoTicks_AtMostTwoAcceptedAndRestRejectedWithObserverNotified()
        {
            // Arrange — 10 requests distributed across exactly 2 ticks (100ms at 20Hz), simulating
            // "10ms intervals" collapsed onto this guard chain's tick-granular clock: 5 requests
            // land in tick 50, 5 in tick 51.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            var observer = new NetworkTestObserver();
            var results = new RpcGuardResult[10];
            LogAssert.ignoreFailingMessages = true; // 8 RateLimitExceeded warnings expected; asserted via return values/observer instead

            // Act
            for (int i = 0; i < 10; i++)
            {
                uint tick = i < 5 ? 50u : 51u;
                results[i] = guardChain.Evaluate(
                    new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, currentTick: tick),
                    observer);
            }

            LogAssert.ignoreFailingMessages = false;

            // Assert
            int acceptedCount = 0;
            int rejectedCount = 0;
            foreach (RpcGuardResult result in results)
            {
                if (result == RpcGuardResult.Accepted)
                {
                    acceptedCount++;
                }
                else if (result == RpcGuardResult.RejectedRateLimited)
                {
                    rejectedCount++;
                }
            }

            Assert.AreEqual(2, acceptedCount, "At most 2 of 10 NotifySkillUsed RPCs within 100ms must be accepted (one per 50ms tick).");
            Assert.AreEqual(8, rejectedCount, "The remaining 8 NotifySkillUsed RPCs must be rejected with RateLimitExceeded.");
            Assert.AreEqual(RpcGuardResult.Accepted, results[0], "The first request in tick 50 must be accepted.");
            Assert.AreEqual(RpcGuardResult.Accepted, results[5], "The first request in tick 51 must be accepted (a full tick after tick 50's accepted request).");
            Assert.AreEqual(8, observer.SkillUsedRateLimitRejectedCalls.Count, "OnSkillUsedRateLimitRejected must fire exactly once per rejected NotifySkillUsed RPC.");
            foreach (uint reportedEntityId in observer.SkillUsedRateLimitRejectedCalls)
            {
                Assert.AreEqual(EntityA.RawValue, reportedEntityId, "Each rejection callback must report the rate-limited entity.");
            }
        }

        [Test]
        public void Evaluate_NotifySkillUsed_SameTickTwice_SecondRejected()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, currentTick: 5u));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[CrossCuttingRpcGuardChain\] Evaluate:.*RateLimitExceeded"));

            // Act
            RpcGuardResult result = guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, currentTick: 5u));

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedRateLimited, result,
                "A second NotifySkillUsed RPC in the same tick must be rejected — one per tick is the physical maximum.");
        }

        [Test]
        public void Evaluate_NotifySkillUsed_NextTick_Accepts()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, currentTick: 5u));

            // Act
            RpcGuardResult result = guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, currentTick: 6u));

            // Assert
            Assert.AreEqual(RpcGuardResult.Accepted, result,
                "A NotifySkillUsed RPC exactly one tick after the prior accepted request must be accepted.");
        }

        // =========================================================================================
        // Rate limit buckets are per-entity, per-RPC-type-tag, not global.
        // =========================================================================================

        [Test]
        public void Evaluate_RateLimitIsPerEntity_TwoDifferentEntitiesDoNotShareARateLimitBucket()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.RegisterEntityOwnership(ClientA, EntityB);
            guardChain.MarkSessionReady(ClientA);
            guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, currentTick: 5u));

            // Act — EntityB's first-ever NotifySkillUsed, same tick as EntityA's accepted request.
            RpcGuardResult result = guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityB, RpcTypeTag.NotifySkillUsed, currentTick: 5u));

            // Assert
            Assert.AreEqual(RpcGuardResult.Accepted, result,
                "Rate limiting must be scoped per-entity — EntityB's first request must not be rejected due to EntityA's rate-limit state.");
        }

        [Test]
        public void Evaluate_RateLimitIsPerRpcTypeTag_SameEntityDifferentTagsDoNotShareABucket()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientA);
            guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, currentTick: 5u));

            // Act — same entity, same tick, but NotifySkillUsed's own bucket has no history yet.
            RpcGuardResult result = guardChain.Evaluate(new InboundRpcDescriptor(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, currentTick: 5u));

            // Assert
            Assert.AreEqual(RpcGuardResult.Accepted, result,
                "AllocateFreePoint and NotifySkillUsed must use independent rate-limit buckets, even for the same entity and tick.");
        }
    }
}
