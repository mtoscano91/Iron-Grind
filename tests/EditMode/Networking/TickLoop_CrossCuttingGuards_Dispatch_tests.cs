using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 035 — the changes ADR-014 Decision 4a makes to
    /// <see cref="CrossCuttingRpcGuardChain"/>: every <see cref="RpcTypeTag"/> member listed with a
    /// gap (0 = no limit), the read-only <c>IsLiveOwner</c> query, <c>IsRateLimited</c>, and the
    /// throttled rejection logs with their per-tick summary.
    /// </summary>
    /// <remarks>
    /// Log messages are counted with a named handler on <see cref="Application.logMessageReceived"/>
    /// because <c>LogAssert</c> does not fail on an unexpected warning and so cannot prove that a log
    /// was suppressed. The sibling file <c>TickLoop_CrossCuttingGuards_tests.cs</c> (Story 010) is
    /// not edited.
    /// </remarks>
    [TestFixture]
    internal sealed class TickLoop_CrossCuttingGuards_Dispatch_Tests
    {
        private static readonly EntityID EntityA = new EntityID(1u);
        private static readonly EntityID EntityB = new EntityID(2u);
        private const uint ClientA = 7u;
        private const uint ClientB = 8u;
        private const uint TickTen = 10u;
        private const uint TickEleven = 11u;
        private const int SetTargetRepeatCount = 100;
        private const int IdenticalRejectionCount = 3;
        private const int ObserverRejectionCount = 5;
        private const int IsLiveOwnerCallCount = 10;
        private const byte UndefinedTagValue = 250;
        private const string GuardLogPrefix = "[CrossCuttingRpcGuardChain]";
        private static readonly EntityID EntityUnregistered = new EntityID(3u);
        private const uint AllocFreePointGapTicks =
            (CrossCuttingRpcGuardChain.ALLOC_FREE_POINT_RATE_LIMIT_MS * ServerTickLoop.TICK_RATE_HZ) / 1000;

        private readonly List<(LogType type, string message)> _logs = new();

        [SetUp]
        public void SetUp()
        {
            _logs.Clear();
            Application.logMessageReceived += OnLogMessageReceived;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            // Only this class's messages: a warning from any other source must not change a count.
            if (condition.StartsWith(GuardLogPrefix, StringComparison.Ordinal))
            {
                _logs.Add((type, condition));
            }
        }

        private static CrossCuttingRpcGuardChain CreateChainWithReadyOwner(uint clientId, EntityID entityId)
        {
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(clientId, entityId);
            guardChain.MarkSessionReady(clientId);
            return guardChain;
        }

        // Entity is registered to ClientA, but ClientA never received SessionReady.
        private static CrossCuttingRpcGuardChain CreateChainWithNotReadyClient()
        {
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            return guardChain;
        }

        private static InboundRpcDescriptor Describe(uint clientId, EntityID entityId, RpcTypeTag tag, uint tick)
        {
            return new InboundRpcDescriptor(clientId, entityId, tag, tick);
        }

        // The summary line exactly as the story words it.
        private static string SummaryLine(uint tick, int unknownEntity, int sessionNotReady, int rateLimited, int notOwner)
        {
            int total = unknownEntity + sessionNotReady + rateLimited + notOwner;
            return $"{GuardLogPrefix} Tick {tick}: {total} further rejections not logged " +
                $"(UnknownEntity={unknownEntity}, SessionNotReady={sessionNotReady}, " +
                $"RateLimited={rateLimited}, NotOwner={notOwner}).";
        }

        private int CountWarnings()
        {
            int count = 0;
            foreach ((LogType type, string message) in _logs)
            {
                if (type == LogType.Warning)
                {
                    count++;
                }
            }
            return count;
        }

        // =========================================================================================
        // Tags with no rate limit.
        // =========================================================================================

        [Test]
        public void Evaluate_EveryRpcTypeTagMember_AcceptsWithoutThrowing()
        {
            // Arrange / Act / Assert — a fresh chain per member, so a missing `case` for a newly
            // added member fails here and nowhere else needs editing.
            foreach (RpcTypeTag tag in Enum.GetValues(typeof(RpcTypeTag)))
            {
                RpcTypeTag currentTag = tag;
                var guardChain = CreateChainWithReadyOwner(ClientA, EntityA);
                RpcGuardResult result = RpcGuardResult.RejectedUnknownEntity;

                Assert.DoesNotThrow(
                    () => result = guardChain.Evaluate(Describe(ClientA, EntityA, currentTag, TickTen)),
                    $"Evaluate must not throw for RpcTypeTag.{currentTag}; add a case to GetRequiredTickGap.");
                Assert.AreEqual(RpcGuardResult.Accepted, result,
                    $"An owned, session-ready entity must be accepted for RpcTypeTag.{currentTag}.");
            }
        }

        [Test]
        public void Evaluate_UndefinedRpcTypeTagValue_ThrowsArgumentOutOfRange()
        {
            // Arrange
            var guardChain = CreateChainWithReadyOwner(ClientA, EntityA);
            var undefinedTag = (RpcTypeTag)UndefinedTagValue;

            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(
                () => guardChain.Evaluate(Describe(ClientA, EntityA, undefinedTag, TickTen)),
                "A value outside the enum must still throw, so a new tag cannot bypass rate limiting silently.");
        }

        [Test]
        public void Evaluate_SetTargetHundredRequestsOnOneTick_AllAccepted()
        {
            // Arrange
            var guardChain = CreateChainWithReadyOwner(ClientA, EntityA);

            // Act / Assert
            for (int i = 0; i < SetTargetRepeatCount; i++)
            {
                RpcGuardResult result = guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));
                Assert.AreEqual(RpcGuardResult.Accepted, result,
                    $"SetTarget has no rate limit; request {i} on one tick must be accepted.");
            }
            Assert.AreEqual(0, CountWarnings(), "Accepted requests must not log.");
        }

        [Test]
        public void Evaluate_NotifySkillUsedTwiceOnOneTick_AcceptedThenRateLimited()
        {
            // Arrange
            var guardChain = CreateChainWithReadyOwner(ClientA, EntityA);

            // Act
            RpcGuardResult first = guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, TickTen));
            RpcGuardResult second = guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, TickTen));

            // Assert
            Assert.AreEqual(RpcGuardResult.Accepted, first, "The first NotifySkillUsed request must be accepted.");
            Assert.AreEqual(RpcGuardResult.RejectedRateLimited, second,
                "The second NotifySkillUsed request on the same tick must still be rate limited.");
        }

        [Test]
        public void IsRateLimited_ReflectsGapPerTagAndNeverThrowsForMembers()
        {
            // Act / Assert
            Assert.IsTrue(CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.AllocateFreePoint), "AllocateFreePoint has a 4-tick gap.");
            Assert.IsTrue(CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.NotifySkillUsed), "NotifySkillUsed has a 1-tick gap.");
            Assert.IsFalse(CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.SetTarget), "SetTarget has no rate limit.");

            foreach (RpcTypeTag tag in Enum.GetValues(typeof(RpcTypeTag)))
            {
                RpcTypeTag currentTag = tag;
                Assert.DoesNotThrow(() => CrossCuttingRpcGuardChain.IsRateLimited(currentTag),
                    $"IsRateLimited must not throw for RpcTypeTag.{currentTag}.");
            }
        }

        [Test]
        public void IsRateLimited_UndefinedRpcTypeTagValue_ThrowsArgumentOutOfRange()
        {
            // Arrange
            var undefinedTag = (RpcTypeTag)UndefinedTagValue;

            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => CrossCuttingRpcGuardChain.IsRateLimited(undefinedTag));
        }

        // =========================================================================================
        // IsLiveOwner.
        // =========================================================================================

        [Test]
        public void IsLiveOwner_RegisteredOwnerAndSessionReady_ReturnsTrue()
        {
            // Arrange
            var guardChain = CreateChainWithReadyOwner(ClientA, EntityA);

            // Act / Assert
            Assert.IsTrue(guardChain.IsLiveOwner(ClientA, EntityA));
        }

        [Test]
        public void IsLiveOwner_UnregisteredEntity_ReturnsFalse()
        {
            // Arrange
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.MarkSessionReady(ClientA);

            // Act / Assert
            Assert.IsFalse(guardChain.IsLiveOwner(ClientA, EntityA));
        }

        [Test]
        public void IsLiveOwner_EntityOwnedByAnotherClient_ReturnsFalse()
        {
            // Arrange
            var guardChain = CreateChainWithReadyOwner(ClientA, EntityA);
            guardChain.MarkSessionReady(ClientB);

            // Act / Assert
            Assert.IsFalse(guardChain.IsLiveOwner(ClientB, EntityA));
        }

        [Test]
        public void IsLiveOwner_ClientNotSessionReady_ReturnsFalse()
        {
            // Arrange
            var guardChain = CreateChainWithNotReadyClient();

            // Act / Assert
            Assert.IsFalse(guardChain.IsLiveOwner(ClientA, EntityA));
        }

        [Test]
        public void IsLiveOwner_AfterClearSessionReady_ReturnsFalse()
        {
            // Arrange
            var guardChain = CreateChainWithReadyOwner(ClientA, EntityA);
            guardChain.ClearSessionReady(ClientA);

            // Act / Assert
            Assert.IsFalse(guardChain.IsLiveOwner(ClientA, EntityA));
        }

        [Test]
        public void IsLiveOwner_AfterUnregisterEntityOwnership_ReturnsFalse()
        {
            // Arrange
            var guardChain = CreateChainWithReadyOwner(ClientA, EntityA);
            guardChain.UnregisterEntityOwnership(EntityA);

            // Act / Assert
            Assert.IsFalse(guardChain.IsLiveOwner(ClientA, EntityA));
        }

        [Test]
        public void IsLiveOwner_CalledRepeatedly_SpendsNoRateLimitSlotAndLogsNothing()
        {
            // Arrange — one accepted AllocateFreePoint on tick 10 stores the last accepted tick.
            var guardChain = CreateChainWithReadyOwner(ClientA, EntityA);
            RpcGuardResult first = guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, TickTen));

            // Act
            for (int i = 0; i < IsLiveOwnerCallCount; i++)
            {
                guardChain.IsLiveOwner(ClientA, EntityA);
            }
            RpcGuardResult atBoundary = guardChain.Evaluate(
                Describe(ClientA, EntityA, RpcTypeTag.AllocateFreePoint, TickTen + AllocFreePointGapTicks));

            // Assert — the stored tick is still 10, so the request exactly one gap later is accepted.
            Assert.AreEqual(RpcGuardResult.Accepted, first);
            Assert.AreEqual(RpcGuardResult.Accepted, atBoundary,
                "IsLiveOwner must not write rate-limit state; the request one full gap after the first must be accepted.");
            Assert.AreEqual(0, _logs.Count, "IsLiveOwner and accepted requests must produce no log message.");
        }

        [Test]
        public void IsLiveOwner_FalseResults_LogNothingAndLeaveThrottleUntouched()
        {
            // Arrange
            var guardChain = CreateChainWithNotReadyClient();

            // Act
            for (int i = 0; i < IsLiveOwnerCallCount; i++)
            {
                Assert.IsFalse(guardChain.IsLiveOwner(ClientA, EntityA));
            }
            int logsAfterQueries = _logs.Count;
            guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));
            int warningsAfterRejection = CountWarnings();
            guardChain.FlushRejectionSummary();

            // Assert
            Assert.AreEqual(0, logsAfterQueries, "A false IsLiveOwner must log nothing.");
            Assert.AreEqual(1, warningsAfterRejection,
                "IsLiveOwner must not mark the pair as logged: the first rejection is still logged in full.");
            Assert.AreEqual(1, CountWarnings(), "IsLiveOwner must not count as a suppressed rejection.");
        }

        // =========================================================================================
        // Throttled rejection logs.
        // =========================================================================================

        [Test]
        public void Evaluate_ThreeIdenticalRejectionsOnOneTick_LogsOnlyTheFirstInFull()
        {
            // Arrange
            var guardChain = CreateChainWithNotReadyClient();

            // Act
            var results = new RpcGuardResult[IdenticalRejectionCount];
            for (int i = 0; i < IdenticalRejectionCount; i++)
            {
                results[i] = guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));
            }

            // Assert
            foreach (RpcGuardResult result in results)
            {
                Assert.AreEqual(RpcGuardResult.RejectedSessionNotReady, result, "Throttling must not change the return value.");
            }
            Assert.AreEqual(1, CountWarnings(), "Only the first rejection per (client, result) per tick is logged.");
            Assert.AreEqual(
                $"{GuardLogPrefix} Evaluate: RPC tag=SetTarget from clientId={ClientA} arrived before SessionReady — " +
                "dropping, not queued (Cross-Cutting Constraint 2, AC-NC-23).",
                _logs[0].message,
                "The full log keeps the text it had before the throttle.");
        }

        [Test]
        public void FlushRejectionSummary_AfterSuppressedRejections_LogsOneSummaryThenNothing()
        {
            // Arrange
            var guardChain = CreateChainWithNotReadyClient();
            for (int i = 0; i < IdenticalRejectionCount; i++)
            {
                guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));
            }

            // Act
            guardChain.FlushRejectionSummary();
            int warningsAfterFirstFlush = CountWarnings();
            guardChain.FlushRejectionSummary();

            // Assert
            Assert.AreEqual(2, warningsAfterFirstFlush, "One full warning plus one summary.");
            Assert.AreEqual(
                SummaryLine(TickTen, unknownEntity: 0, sessionNotReady: IdenticalRejectionCount - 1, rateLimited: 0, notOwner: 0),
                _logs[1].message);
            Assert.AreEqual(2, CountWarnings(), "A second flush with nothing suppressed must log nothing.");
        }

        [Test]
        public void Evaluate_RejectionAfterFlushOnSameTick_IsCountedNotLoggedInFull()
        {
            // Arrange
            var guardChain = CreateChainWithNotReadyClient();
            for (int i = 0; i < IdenticalRejectionCount; i++)
            {
                guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));
            }
            guardChain.FlushRejectionSummary();
            int warningsBeforeRejection = CountWarnings();

            // Act
            RpcGuardResult result = guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));
            int warningsAfterRejection = CountWarnings();
            guardChain.FlushRejectionSummary();

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedSessionNotReady, result);
            Assert.AreEqual(warningsBeforeRejection, warningsAfterRejection,
                "The flush clears the counts only; the logged pair stays until the tick changes.");
            Assert.AreEqual(warningsBeforeRejection + 1, CountWarnings(), "The second flush must log one summary.");
            Assert.AreEqual(
                SummaryLine(TickTen, unknownEntity: 0, sessionNotReady: 1, rateLimited: 0, notOwner: 0),
                _logs[_logs.Count - 1].message);
        }

        [Test]
        public void FlushRejectionSummary_MixedResultsForOneClient_ReportsEachCountAndTotal()
        {
            // Arrange — EntityA is ClientA's, EntityB is ClientB's, EntityUnregistered is unknown.
            // ClientA is not session-ready at first.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.RegisterEntityOwnership(ClientB, EntityB);
            const int unknownEntityRejections = 2;
            const int sessionNotReadyRejections = 3;
            const int notOwnerRejections = 4;
            const int rateLimitedRejections = 5;
            const int rejectingResults = 4;

            // Act — each result is rejected a different number of times on one tick.
            for (int i = 0; i < unknownEntityRejections; i++)
            {
                Assert.AreEqual(RpcGuardResult.RejectedUnknownEntity,
                    guardChain.Evaluate(Describe(ClientA, EntityUnregistered, RpcTypeTag.SetTarget, TickTen)));
            }
            for (int i = 0; i < sessionNotReadyRejections; i++)
            {
                Assert.AreEqual(RpcGuardResult.RejectedSessionNotReady,
                    guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen)));
            }
            guardChain.MarkSessionReady(ClientA);
            for (int i = 0; i < notOwnerRejections; i++)
            {
                Assert.AreEqual(RpcGuardResult.RejectedNotOwner,
                    guardChain.Evaluate(Describe(ClientA, EntityB, RpcTypeTag.SetTarget, TickTen)));
            }
            Assert.AreEqual(RpcGuardResult.Accepted,
                guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, TickTen)));
            for (int i = 0; i < rateLimitedRejections; i++)
            {
                Assert.AreEqual(RpcGuardResult.RejectedRateLimited,
                    guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, TickTen)));
            }
            int fullWarnings = CountWarnings();
            guardChain.FlushRejectionSummary();

            // Assert — one full log per result, every further rejection counted under its own result.
            Assert.AreEqual(rejectingResults, fullWarnings, "Each of the four results is logged in full exactly once.");
            Assert.AreEqual(rejectingResults + 1, CountWarnings(), "The flush adds one summary line.");
            Assert.AreEqual(
                SummaryLine(
                    TickTen,
                    unknownEntity: unknownEntityRejections - 1,
                    sessionNotReady: sessionNotReadyRejections - 1,
                    rateLimited: rateLimitedRejections - 1,
                    notOwner: notOwnerRejections - 1),
                _logs[_logs.Count - 1].message);
        }

        [Test]
        public void FlushRejectionSummary_NothingSuppressed_LogsNothingExtra()
        {
            // Arrange
            var guardChain = CreateChainWithNotReadyClient();
            guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));

            // Act
            guardChain.FlushRejectionSummary();

            // Assert
            Assert.AreEqual(1, CountWarnings(), "Only the one full warning; the flush has nothing to report.");
        }

        [Test]
        public void Evaluate_TwoDifferentResultsForOneClient_BothLoggedInFull()
        {
            // Arrange — ClientA is session-ready; EntityB is unregistered; EntityA is owned by ClientB.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientB, EntityA);
            guardChain.MarkSessionReady(ClientA);

            // Act
            RpcGuardResult unknown = guardChain.Evaluate(Describe(ClientA, EntityB, RpcTypeTag.SetTarget, TickTen));
            RpcGuardResult notOwner = guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedUnknownEntity, unknown);
            Assert.AreEqual(RpcGuardResult.RejectedNotOwner, notOwner);
            Assert.AreEqual(2, CountWarnings(), "Different results for the same client are independent throttle keys.");
        }

        [Test]
        public void Evaluate_SameResultForTwoClients_BothLoggedInFull()
        {
            // Arrange — neither client is session-ready.
            var guardChain = new CrossCuttingRpcGuardChain();
            guardChain.RegisterEntityOwnership(ClientA, EntityA);
            guardChain.RegisterEntityOwnership(ClientB, EntityB);

            // Act
            RpcGuardResult fromA = guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));
            RpcGuardResult fromB = guardChain.Evaluate(Describe(ClientB, EntityB, RpcTypeTag.SetTarget, TickTen));

            // Assert
            Assert.AreEqual(RpcGuardResult.RejectedSessionNotReady, fromA);
            Assert.AreEqual(RpcGuardResult.RejectedSessionNotReady, fromB);
            Assert.AreEqual(2, CountWarnings(), "The same result for two clients are independent throttle keys.");
        }

        [Test]
        public void Evaluate_NewTickWithoutFlush_LogsPendingSummaryThenFullWarningForNewTick()
        {
            // Arrange
            var guardChain = CreateChainWithNotReadyClient();
            for (int i = 0; i < IdenticalRejectionCount; i++)
            {
                guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));
            }

            // Act
            guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickEleven));

            // Assert — order: full (tick 10), summary for tick 10, full (tick 11).
            Assert.AreEqual(3, CountWarnings());
            StringAssert.Contains("arrived before SessionReady", _logs[0].message);
            Assert.AreEqual(
                SummaryLine(TickTen, unknownEntity: 0, sessionNotReady: IdenticalRejectionCount - 1, rateLimited: 0, notOwner: 0),
                _logs[1].message);
            StringAssert.Contains("arrived before SessionReady", _logs[2].message);
        }

        [Test]
        public void Evaluate_AcceptedRequestOnNewTick_LogsPendingSummaryOfEarlierTick()
        {
            // Arrange — ClientA is rejected three times on tick 10; ClientB is a session-ready owner.
            var guardChain = CreateChainWithNotReadyClient();
            guardChain.RegisterEntityOwnership(ClientB, EntityB);
            guardChain.MarkSessionReady(ClientB);
            for (int i = 0; i < IdenticalRejectionCount; i++)
            {
                guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.SetTarget, TickTen));
            }
            int warningsBeforeNewTick = CountWarnings();

            // Act — the first request of tick 11 is an accepted one.
            RpcGuardResult result = guardChain.Evaluate(Describe(ClientB, EntityB, RpcTypeTag.SetTarget, TickEleven));
            int warningsAfterNewTick = CountWarnings();
            guardChain.FlushRejectionSummary();

            // Assert
            Assert.AreEqual(RpcGuardResult.Accepted, result);
            Assert.AreEqual(1, warningsBeforeNewTick);
            Assert.AreEqual(2, warningsAfterNewTick, "The tick change is seen on the accept path too.");
            Assert.AreEqual(
                SummaryLine(TickTen, unknownEntity: 0, sessionNotReady: IdenticalRejectionCount - 1, rateLimited: 0, notOwner: 0),
                _logs[1].message);
            Assert.AreEqual(2, CountWarnings(), "The summary was already logged; the flush has nothing left to report.");
        }

        [Test]
        public void Evaluate_FiveRateLimitedNotifySkillUsedRejections_ObserverFiresEveryTimeButOnlyOneFullLog()
        {
            // Arrange
            var guardChain = CreateChainWithReadyOwner(ClientA, EntityA);
            var observer = new NetworkTestObserver();
            guardChain.Evaluate(Describe(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, TickTen), observer); // accepted

            // Act
            for (int i = 0; i < ObserverRejectionCount; i++)
            {
                RpcGuardResult result = guardChain.Evaluate(
                    Describe(ClientA, EntityA, RpcTypeTag.NotifySkillUsed, TickTen), observer);
                Assert.AreEqual(RpcGuardResult.RejectedRateLimited, result);
            }

            // Assert
            Assert.AreEqual(ObserverRejectionCount, observer.SkillUsedRateLimitRejectedCalls.Count,
                "The observer hook is not throttled: it must fire for every rate-limited rejection.");
            Assert.AreEqual(1, CountWarnings(), "Only the first rate-limited rejection is logged in full.");
            StringAssert.Contains("RateLimitExceeded", _logs[0].message);
        }
    }
}
