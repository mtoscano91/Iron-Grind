using System;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 036 — the hold queues, Pass A and Decision 5 of
    /// ADR-014: holding while the gate is closed, release order and timing, polling instead of
    /// subscribing, gate changes made by handlers, discard on release, the queue bound and the pool.
    /// </summary>
    /// <remarks>
    /// A held request's body carries a seed in its first byte so a test can tell the requests apart.
    /// Character A is the character of <see cref="InboundTestIds.ClientOne"/>, character B of
    /// <see cref="InboundTestIds.ClientTwo"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class InboundDispatch_HoldQueue_Tests
    {
        private const uint FirstArrivalTick = 20u;
        private const uint SecondArrivalTick = 22u;
        private const uint FirstDispatchTick = 21u;
        private const uint SecondDispatchTick = 23u;
        private const uint ReleaseArrivalTick = 24u;
        private const uint ReleaseDispatchTick = 25u;
        private const uint Sequence = 1u;
        private const int HeldAcrossTicks = 5;
        private const int OverflowRequestCount = TickCompletionConstants.MAX_HELD_REQUESTS_PER_CHARACTER + 1;
        private const int PoolCycleCount =
            ZoneBufferPool.MAX_PLAYERS_PER_ZONE * TickCompletionConstants.MAX_HELD_REQUESTS_PER_CHARACTER;

        private static CharacterID CharA => InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne);

        private static CharacterID CharB => InboundDispatchHarness.CharacterOf(InboundTestIds.ClientTwo);

        private static byte[] HeldBody(byte seed) =>
            InboundMessageBuilder.Body(InboundTestIds.HeldRodBodySize, seed);

        private static byte[] PlainBody(byte seed) =>
            InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, seed);

        private static void SendHeld(InboundDispatchHarness h, uint clientId, byte seed)
        {
            Assert.IsTrue(h.SendRequest(clientId, InboundTestIds.TypeHeldRod, Sequence, HeldBody(seed)));
        }

        private static void SendPlain(InboundDispatchHarness h, uint clientId, byte seed)
        {
            Assert.IsTrue(h.SendRequest(clientId, InboundTestIds.TypePlainRod, Sequence, PlainBody(seed)));
        }

        // =========================================================================================
        // Hold, release.
        // =========================================================================================

        [Test]
        public void DispatchTick_HeldTypeWithClosedGate_IsHeldAndPlainTypeIsDispatched()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            SendPlain(h, InboundTestIds.ClientOne, InboundTestIds.SeedTwo);

            // Act
            h.Dispatcher.DispatchTick(FirstDispatchTick);

            // Assert
            Assert.AreEqual(1, h.Dispatcher.HeldCount(CharA));
            Assert.AreEqual(1, h.Handler.Calls.Count);
            Assert.AreEqual(InboundTestIds.TypePlainRod, h.Handler.Calls[0].Context.MessageTypeId);
        }

        [Test]
        public void DispatchTick_GateOpensAfterHolding_ReleasesInArrivalOrderBeforeTheInboxWithOriginalContext()
        {
            // Arrange — three held requests over two ticks, then the gate opens and a plain request arrives.
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            h.TickSource.Tick = FirstArrivalTick;
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedTwo);
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            h.TickSource.Tick = SecondArrivalTick;
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedThree);
            h.Dispatcher.DispatchTick(SecondDispatchTick);
            h.Gate.Open(CharA);
            h.TickSource.Tick = ReleaseArrivalTick;
            SendPlain(h, InboundTestIds.ClientOne, InboundTestIds.SeedFour);

            // Act
            h.Dispatcher.DispatchTick(ReleaseDispatchTick);

            // Assert
            Assert.AreEqual(4, h.Handler.Calls.Count);
            byte[] expectedSeeds = { InboundTestIds.SeedOne, InboundTestIds.SeedTwo, InboundTestIds.SeedThree, InboundTestIds.SeedFour };
            uint[] expectedArrival = { FirstArrivalTick, FirstArrivalTick, SecondArrivalTick, ReleaseArrivalTick };
            for (int i = 0; i < expectedSeeds.Length; i++)
            {
                InboundRecordedRequest call = h.Handler.Calls[i];
                Assert.AreEqual(expectedSeeds[i], call.Body[0], $"Call {i}: order.");
                Assert.AreEqual(expectedArrival[i], call.Context.ArrivalTick, $"Call {i}: original ArrivalTick.");
                Assert.AreEqual(ReleaseDispatchTick, call.Context.DispatchTick, $"Call {i}: DispatchTick.");
                Assert.AreEqual(i < 3, call.Context.WasHeld, $"Call {i}: WasHeld.");
            }
            CollectionAssert.AreEqual(HeldBody(InboundTestIds.SeedOne), h.Handler.Calls[0].Body);
            CollectionAssert.AreEqual(HeldBody(InboundTestIds.SeedTwo), h.Handler.Calls[1].Body);
            CollectionAssert.AreEqual(HeldBody(InboundTestIds.SeedThree), h.Handler.Calls[2].Body);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(CharA));
        }

        [Test]
        public void DispatchTick_CharacterStillHasHeldRequests_NewHeldTypeRequestIsHeldEvenWithGateOpen()
        {
            // Arrange — two held; the first released one closes the gate in Pass A; client 2's plain
            // request (earlier in the inbox) opens it again before client 1's new held-type request.
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne, InboundTestIds.ClientTwo);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedTwo);
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            h.Gate.Open(CharA);
            bool closedOnce = false;
            h.Handler.Behaviour = context =>
            {
                if (context.WasHeld && !closedOnce)
                {
                    closedOnce = true;
                    h.Gate.Close(CharA);
                }
                else if (context.ClientId == InboundTestIds.ClientTwo)
                {
                    h.Gate.Open(CharA);
                }
            };
            SendPlain(h, InboundTestIds.ClientTwo, InboundTestIds.SeedFour);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedThree);

            // Act
            h.Dispatcher.DispatchTick(SecondDispatchTick);
            int heldAfterFirstTick = h.Dispatcher.HeldCount(CharA);
            int callsAfterFirstTick = h.Handler.Calls.Count;
            h.Dispatcher.DispatchTick(ReleaseDispatchTick);

            // Assert
            Assert.AreEqual(2, heldAfterFirstTick, "One left after Pass A plus the new request held behind it.");
            Assert.AreEqual(2, callsAfterFirstTick, "The first held request and client 2's plain request.");
            Assert.AreEqual(4, h.Handler.Calls.Count);
            Assert.AreEqual(InboundTestIds.SeedTwo, h.Handler.Calls[2].Body[0]);
            Assert.AreEqual(InboundTestIds.SeedThree, h.Handler.Calls[3].Body[0]);
            Assert.IsTrue(h.Handler.Calls[2].Context.WasHeld);
            Assert.IsTrue(h.Handler.Calls[3].Context.WasHeld);
        }

        [Test]
        public void DispatchTick_GateOpenSubscriberThrowsDuringOpen_HeldRequestsAreStillReleased()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedTwo);
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            h.Gate.OnGateOpened += openedCharacter => throw new InvalidOperationException("Test subscriber failure.");
            Assert.Throws<InvalidOperationException>(() => h.Gate.Open(CharA));

            // Act
            h.Dispatcher.DispatchTick(SecondDispatchTick);

            // Assert — the dispatcher polls the gate; it does not depend on the event.
            Assert.AreEqual(2, h.Handler.Calls.Count);
            Assert.IsTrue(h.Handler.Calls[0].Context.WasHeld);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(CharA));
        }

        [Test]
        public void DispatchTick_HandlerClosesGateInPassB_LaterHeldTypeRequestOfTheCharacterIsHeld()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Handler.Behaviour = context =>
            {
                if (context.MessageTypeId == InboundTestIds.TypePlainRod)
                {
                    h.Gate.Close(CharA);
                }
            };
            SendPlain(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedTwo);

            // Act
            h.Dispatcher.DispatchTick(FirstDispatchTick);

            // Assert
            Assert.AreEqual(1, h.Handler.Calls.Count, "Only the plain request ran.");
            Assert.AreEqual(1, h.Dispatcher.HeldCount(CharA));
        }

        [Test]
        public void DispatchTick_ReleasedRequestClosesGate_RemainingHeldRequestsStayHeldInOrderUntilNextOpening()
        {
            // Arrange — three held; the first released one closes the gate.
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedTwo);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedThree);
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            bool closedOnce = false;
            h.Handler.Behaviour = context =>
            {
                if (!closedOnce)
                {
                    closedOnce = true;
                    h.Gate.Close(CharA);
                }
            };
            h.Gate.Open(CharA);

            // Act
            h.Dispatcher.DispatchTick(SecondDispatchTick);
            int callsAfterFirstRelease = h.Handler.Calls.Count;
            int heldAfterFirstRelease = h.Dispatcher.HeldCount(CharA);
            h.Gate.Open(CharA);
            h.Dispatcher.DispatchTick(ReleaseDispatchTick);

            // Assert
            Assert.AreEqual(1, callsAfterFirstRelease);
            Assert.AreEqual(2, heldAfterFirstRelease);
            Assert.AreEqual(3, h.Handler.Calls.Count);
            Assert.AreEqual(InboundTestIds.SeedTwo, h.Handler.Calls[1].Body[0]);
            Assert.AreEqual(InboundTestIds.SeedThree, h.Handler.Calls[2].Body[0]);
        }

        // =========================================================================================
        // Discard on release.
        // =========================================================================================

        [Test]
        public void DispatchTick_ConnectionRemovedWhileRequestsHeld_DiscardsThemAndRestoresThePool()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedTwo);
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            h.Dispatcher.RemoveConnection(InboundTestIds.ClientOne);
            h.Gate.Open(CharA);

            // Act
            h.Dispatcher.DispatchTick(SecondDispatchTick);

            // Assert
            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(CharA));
            Assert.AreEqual(PoolCycleCount, h.Dispatcher.FreeHoldEntryCount);
        }

        [Test]
        public void DispatchTick_ClientNoLongerSessionReadyAtRelease_DiscardsWithoutEvaluatingTheGuardChain()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            h.GuardChain.ClearSessionReady(InboundTestIds.ClientOne);
            h.Gate.Open(CharA);
            using var capture = new InboundLogCapture(InboundTestIds.GuardLogPrefix);

            // Act
            h.Dispatcher.DispatchTick(SecondDispatchTick);

            // Assert
            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(PoolCycleCount, h.Dispatcher.FreeHoldEntryCount);
            Assert.AreEqual(0, capture.CountWarnings(), "Release must not call Evaluate: a rejection would have been logged.");
        }

        [Test]
        public void DispatchTick_EntityOwnershipLostAtRelease_DiscardsWithoutEvaluatingTheGuardChain()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            h.GuardChain.UnregisterEntityOwnership(InboundDispatchHarness.EntityOf(InboundTestIds.ClientOne));
            h.Gate.Open(CharA);
            using var capture = new InboundLogCapture(InboundTestIds.GuardLogPrefix);

            // Act
            h.Dispatcher.DispatchTick(SecondDispatchTick);

            // Assert
            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(PoolCycleCount, h.Dispatcher.FreeHoldEntryCount);
            Assert.AreEqual(0, capture.CountWarnings());
        }

        [Test]
        public void DispatchTick_RequestHeldAcrossFiveTicks_IsDispatchedOnReleaseWithoutGuardWarnings()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            using var capture = new InboundLogCapture(InboundTestIds.GuardLogPrefix);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            uint tick = FirstDispatchTick;
            for (int i = 0; i < HeldAcrossTicks; i++)
            {
                h.Dispatcher.DispatchTick(tick++);
            }
            h.Gate.Open(CharA);

            // Act
            h.Dispatcher.DispatchTick(tick);

            // Assert
            Assert.AreEqual(1, h.Handler.Calls.Count);
            Assert.IsTrue(h.Handler.Calls[0].Context.WasHeld);
            Assert.AreEqual(tick, h.Handler.Calls[0].Context.DispatchTick);
            Assert.AreEqual(0, capture.CountWarnings());
        }

        // =========================================================================================
        // Bound, pool, independence, failure.
        // =========================================================================================

        [Test]
        public void DispatchTick_SeventeenHeldTypeRequests_SeventeenthDroppedWithOneWarningAndSixteenReleased()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            for (int i = 0; i < OverflowRequestCount; i++)
            {
                SendHeld(h, InboundTestIds.ClientOne, (byte)i);
            }
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);

            // Act
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            int held = h.Dispatcher.HeldCount(CharA);
            h.Gate.Open(CharA);
            h.Dispatcher.DispatchTick(SecondDispatchTick);

            // Assert
            Assert.AreEqual(TickCompletionConstants.MAX_HELD_REQUESTS_PER_CHARACTER, held);
            Assert.AreEqual(1, capture.CountWarnings("HeldRequestOverflow"));
            Assert.AreEqual(TickCompletionConstants.MAX_HELD_REQUESTS_PER_CHARACTER, h.Handler.Calls.Count);
            for (int i = 0; i < h.Handler.Calls.Count; i++)
            {
                Assert.AreEqual((byte)i, h.Handler.Calls[i].Body[0], $"Held request {i} is unaffected by the overflow.");
            }
        }

        [Test]
        public void DispatchTick_EightHundredHoldAndReleaseCycles_PoolIsFullAndARequestCanStillBeHeld()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            uint tick = FirstDispatchTick;

            // Act
            for (int i = 0; i < PoolCycleCount; i++)
            {
                h.Gate.Close(CharA);
                SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
                h.Dispatcher.DispatchTick(tick++);
                h.Gate.Open(CharA);
                h.Dispatcher.DispatchTick(tick++);
            }
            int freeAfterCycles = h.Dispatcher.FreeHoldEntryCount;
            int callsAfterCycles = h.Handler.Calls.Count;
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedTwo);
            h.Dispatcher.DispatchTick(tick);

            // Assert
            Assert.AreEqual(PoolCycleCount, freeAfterCycles);
            Assert.AreEqual(PoolCycleCount, callsAfterCycles, "Every cycle released its request.");
            Assert.AreEqual(1, h.Dispatcher.HeldCount(CharA));
        }

        [Test]
        public void DispatchTick_GateClosedForOneCharacterOnly_OtherCharactersHeldTypeRequestIsDispatched()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne, InboundTestIds.ClientTwo);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            SendHeld(h, InboundTestIds.ClientTwo, InboundTestIds.SeedTwo);

            // Act
            h.Dispatcher.DispatchTick(FirstDispatchTick);

            // Assert
            Assert.AreEqual(1, h.Handler.Calls.Count);
            Assert.AreEqual(InboundTestIds.ClientTwo, h.Handler.Calls[0].Context.ClientId);
            Assert.AreEqual(1, h.Dispatcher.HeldCount(CharA));
            Assert.AreEqual(0, h.Dispatcher.HeldCount(CharB));
        }

        [Test]
        public void DispatchTick_ReleasedHandlerThrows_LogsOneErrorDispatchesNextHeldRequestAndRestoresThePool()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedTwo);
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            bool thrown = false;
            h.Handler.Behaviour = context =>
            {
                if (!thrown)
                {
                    thrown = true;
                    throw new InvalidOperationException("Test handler failure.");
                }
            };
            h.Gate.Open(CharA);
            LogAssert.Expect(LogType.Error,
                InboundTestIds.RequestFailedRegex(InboundTestIds.ClientOne, InboundTestIds.TypeHeldRod, "Release"));

            // Act
            Assert.DoesNotThrow(() => h.Dispatcher.DispatchTick(SecondDispatchTick));

            // Assert
            Assert.AreEqual(2, h.Handler.Calls.Count, "The failing call is recorded, then the next held request runs.");
            Assert.AreEqual(InboundTestIds.SeedTwo, h.Handler.Calls[1].Body[0]);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(CharA));
            Assert.AreEqual(PoolCycleCount, h.Dispatcher.FreeHoldEntryCount);
        }

        [Test]
        public void DispatchTick_ReleasedHandlerRemovesItsOwnConnection_DiscardsTheRemainingHeldRequests()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedTwo);
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            h.Handler.Behaviour = context => h.Dispatcher.RemoveConnection(InboundTestIds.ClientOne);
            h.Gate.Open(CharA);

            // Act
            Assert.DoesNotThrow(() => h.Dispatcher.DispatchTick(SecondDispatchTick));

            // Assert
            Assert.AreEqual(1, h.Handler.Calls.Count, "Only the first held request runs; the second is discarded.");
            Assert.AreEqual(InboundTestIds.SeedOne, h.Handler.Calls[0].Body[0]);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(CharA));
            Assert.AreEqual(PoolCycleCount, h.Dispatcher.FreeHoldEntryCount);
        }

        [Test]
        public void DispatchTick_GateThrowsWhenPolledInPassA_KeepsTheRequestHeldAndReleasesItOnTheNextTick()
        {
            // Arrange
            var gate = new InboundFaultableGate();
            var h = new InboundDispatchHarness(gate: gate);
            h.RegisterStandardTypes();
            h.Dispatcher.Seal();
            h.AddClient(InboundTestIds.ClientOne);
            h.Gate.Close(CharA);
            SendHeld(h, InboundTestIds.ClientOne, InboundTestIds.SeedOne);
            h.Dispatcher.DispatchTick(FirstDispatchTick);
            h.Gate.Open(CharA);
            gate.IsHeldFailuresRemaining = 1;
            LogAssert.Expect(LogType.Error,
                InboundTestIds.RequestFailedRegex(InboundTestIds.ClientOne, InboundTestIds.TypeHeldRod, "Release"));

            // Act
            Assert.DoesNotThrow(() => h.Dispatcher.DispatchTick(SecondDispatchTick));
            int callsWhileGateFailed = h.Handler.Calls.Count;
            int heldWhileGateFailed = h.Dispatcher.HeldCount(CharA);
            h.Dispatcher.DispatchTick(ReleaseDispatchTick);

            // Assert
            Assert.AreEqual(0, callsWhileGateFailed);
            Assert.AreEqual(1, heldWhileGateFailed);
            Assert.AreEqual(1, h.Handler.Calls.Count);
            Assert.IsTrue(h.Handler.Calls[0].Context.WasHeld);
            Assert.AreEqual(InboundTestIds.SeedOne, h.Handler.Calls[0].Body[0]);
            Assert.AreEqual(PoolCycleCount, h.Dispatcher.FreeHoldEntryCount);
        }
    }
}
