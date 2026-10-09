using System;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 036 — Pass B of
    /// <see cref="InboundRequestDispatcher.DispatchTick"/> (ADR-014 Decision 4): arrival order, the
    /// context, guard rejections, no character, isolation of failures, connection removal and slot
    /// reuse, and the guard-log summary.
    /// </summary>
    [TestFixture]
    internal sealed class InboundDispatch_Dispatch_Tests
    {
        private const uint ArrivalTick = 7u;
        private const uint DispatchTick = 8u;
        private const uint Sequence = 1u;
        private const uint SecondSequence = 2u;
        private const int IdenticalRejectionCount = 3;

        private static byte[] PlainBody(byte seed = InboundTestIds.SeedOne) =>
            InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, seed);

        private static byte[] HeldBody(byte seed = InboundTestIds.SeedOne) =>
            InboundMessageBuilder.Body(InboundTestIds.HeldRodBodySize, seed);

        private static byte[] UuBody(byte seed = InboundTestIds.SeedOne) =>
            InboundMessageBuilder.Body(InboundTestIds.UuBodySize, seed);

        // =========================================================================================
        // Arrival order and context.
        // =========================================================================================

        [Test]
        public void DispatchTick_RequestsOfSeveralConnectionsAndTypes_DispatchedInArrivalOrder()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne, InboundTestIds.ClientTwo);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            h.SendRequest(InboundTestIds.ClientTwo, InboundTestIds.TypeHeldRod, Sequence, HeldBody());
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, Sequence, UuBody());
            h.SendRequest(InboundTestIds.ClientTwo, InboundTestIds.TypePlainRod, SecondSequence, PlainBody());

            // Act
            h.Dispatcher.DispatchTick(DispatchTick);

            // Assert
            Assert.AreEqual(4, h.Handler.Calls.Count);
            ushort[] expectedTypes =
            {
                InboundTestIds.TypePlainRod, InboundTestIds.TypeHeldRod, InboundTestIds.TypeUuA, InboundTestIds.TypePlainRod,
            };
            uint[] expectedClients =
            {
                InboundTestIds.ClientOne, InboundTestIds.ClientTwo, InboundTestIds.ClientOne, InboundTestIds.ClientTwo,
            };
            for (int i = 0; i < expectedTypes.Length; i++)
            {
                Assert.AreEqual(expectedTypes[i], h.Handler.Calls[i].Context.MessageTypeId, $"Call {i}: type.");
                Assert.AreEqual(expectedClients[i], h.Handler.Calls[i].Context.ClientId, $"Call {i}: client.");
            }
        }

        [Test]
        public void DispatchTick_AcceptedRequest_HandlerReceivesEveryContextField()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            byte[] body = PlainBody(InboundTestIds.SeedThree);
            h.TickSource.Tick = ArrivalTick;
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, body);

            // Act
            h.Dispatcher.DispatchTick(DispatchTick);

            // Assert
            Assert.AreEqual(1, h.Handler.Calls.Count);
            InboundRequestContext context = h.Handler.Calls[0].Context;
            Assert.AreEqual(InboundTestIds.ClientOne, context.ClientId);
            Assert.AreEqual(InboundDispatchHarness.EntityOf(InboundTestIds.ClientOne), context.SenderEntityId);
            Assert.AreEqual(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne), context.CharacterId);
            Assert.AreEqual(InboundTestIds.TypePlainRod, context.MessageTypeId);
            Assert.AreEqual(ArrivalTick, context.ArrivalTick, "ArrivalTick is the tick source's value when TryAccept ran.");
            Assert.AreEqual(DispatchTick, context.DispatchTick, "DispatchTick is the argument of DispatchTick.");
            Assert.IsFalse(context.WasHeld);
            CollectionAssert.AreEqual(body, h.Handler.Calls[0].Body);
        }

        // =========================================================================================
        // Guard rejections drop.
        // =========================================================================================

        [Test]
        public void DispatchTick_UnknownEntity_RequestDroppedAndNotHeld()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.SendRequestFrom(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence,
                InboundTestIds.UnknownEntityRaw, PlainBody());

            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne)));
        }

        [Test]
        public void DispatchTick_ClientNotSessionReady_RequestDroppedAndNotHeld()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.GuardChain.ClearSessionReady(InboundTestIds.ClientOne);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());

            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne)));
        }

        [Test]
        public void DispatchTick_UuBTwiceOnOneTick_FirstDispatchedSecondRateLimitedAndDropped()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuB, Sequence, UuBody(InboundTestIds.SeedOne));
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuB, SecondSequence, UuBody(InboundTestIds.SeedTwo));

            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.AreEqual(1, h.Handler.Calls.Count);
            Assert.AreEqual(InboundTestIds.SeedOne, h.Handler.Calls[0].Body[0]);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne)));
        }

        [Test]
        public void DispatchTick_SenderEntityOwnedByAnotherClient_RequestDroppedAndNotHeld()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne, InboundTestIds.ClientTwo);
            h.SendRequestFrom(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence,
                InboundDispatchHarness.EntityOf(InboundTestIds.ClientTwo).RawValue, PlainBody());

            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne)));
        }

        [Test]
        public void DispatchTick_HeldTypeUnknownEntityWithClosedGate_DroppedNotHeld()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Gate.Close(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne));
            h.SendRequestFrom(InboundTestIds.ClientOne, InboundTestIds.TypeHeldRod, Sequence,
                InboundTestIds.UnknownEntityRaw, HeldBody());

            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne)));
        }

        [Test]
        public void DispatchTick_HeldTypeNotSessionReadyWithClosedGate_DroppedNotHeld()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.GuardChain.ClearSessionReady(InboundTestIds.ClientOne);
            h.Gate.Close(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne));
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeHeldRod, Sequence, HeldBody());

            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne)));
        }

        [Test]
        public void DispatchTick_HeldTypeNotOwnerWithClosedGate_DroppedNotHeld()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne, InboundTestIds.ClientTwo);
            h.Gate.Close(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne));
            h.SendRequestFrom(InboundTestIds.ClientOne, InboundTestIds.TypeHeldRod, Sequence,
                InboundDispatchHarness.EntityOf(InboundTestIds.ClientTwo).RawValue, HeldBody());

            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(0, h.Dispatcher.HeldCount(InboundDispatchHarness.CharacterOf(InboundTestIds.ClientOne)));
        }

        // =========================================================================================
        // No character, empty after dispatch.
        // =========================================================================================

        [Test]
        public void DispatchTick_ConnectionWithoutCharacter_DroppedWithAnomalyWarning()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Directory.Remove(InboundTestIds.ClientOne);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);

            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(1, capture.CountWarnings("InboundRequestWithoutCharacter"));
        }

        [Test]
        public void DispatchTick_SecondTickWithNoNewMessage_CallsNoHandlerAndInboxIsEmpty()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            h.Dispatcher.DispatchTick(DispatchTick);
            int callsAfterFirstTick = h.Handler.Calls.Count;

            h.Dispatcher.DispatchTick(DispatchTick + 1u);

            Assert.AreEqual(1, callsAfterFirstTick);
            Assert.AreEqual(callsAfterFirstTick, h.Handler.Calls.Count);
            Assert.AreEqual(0, h.Dispatcher.PendingRequestCount(InboundTestIds.ClientOne));
        }

        // =========================================================================================
        // Isolation.
        // =========================================================================================

        [Test]
        public void DispatchTick_HandlerThrowsForFirstOfThree_LogsOneErrorAndDispatchesTheOthers()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            bool thrown = false;
            h.Handler.Behaviour = context =>
            {
                if (!thrown)
                {
                    thrown = true;
                    throw new InvalidOperationException("Test handler failure.");
                }
            };
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody(InboundTestIds.SeedOne));
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody(InboundTestIds.SeedTwo));
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody(InboundTestIds.SeedThree));
            LogAssert.Expect(LogType.Error,
                InboundTestIds.RequestFailedRegex(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, "Dispatch"));

            Assert.DoesNotThrow(() => h.Dispatcher.DispatchTick(DispatchTick));

            Assert.AreEqual(3, h.Handler.Calls.Count, "The failing call is recorded, then the other two run.");
            Assert.AreEqual(InboundTestIds.SeedTwo, h.Handler.Calls[1].Body[0]);
            Assert.AreEqual(InboundTestIds.SeedThree, h.Handler.Calls[2].Body[0]);
        }

        [Test]
        public void DispatchTick_DirectoryThrowsForOneClient_LogsOneErrorAndDispatchesTheOthers()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne, InboundTestIds.ClientTwo);
            h.Directory.ThrowForClients.Add(InboundTestIds.ClientOne);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            h.SendRequest(InboundTestIds.ClientTwo, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            h.SendRequest(InboundTestIds.ClientTwo, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            LogAssert.Expect(LogType.Error,
                InboundTestIds.RequestFailedRegex(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, "Dispatch"));

            Assert.DoesNotThrow(() => h.Dispatcher.DispatchTick(DispatchTick));

            Assert.AreEqual(2, h.Handler.Calls.Count);
            Assert.AreEqual(InboundTestIds.ClientTwo, h.Handler.Calls[0].Context.ClientId);
            Assert.AreEqual(InboundTestIds.ClientTwo, h.Handler.Calls[1].Context.ClientId);
        }

        [Test]
        public void DispatchTick_GateThrowsForFirstHeldTypeRequest_LogsOneErrorAndDispatchesTheOthers()
        {
            var gate = new InboundFaultableGate { IsHeldFailuresRemaining = 1 };
            var h = new InboundDispatchHarness(gate: gate);
            h.RegisterStandardTypes();
            h.Dispatcher.Seal();
            h.AddClients(InboundTestIds.ClientOne, 3);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeHeldRod, Sequence, HeldBody());
            h.SendRequest(InboundTestIds.ClientTwo, InboundTestIds.TypeHeldRod, Sequence, HeldBody());
            h.SendRequest(InboundTestIds.ClientThree, InboundTestIds.TypeHeldRod, Sequence, HeldBody());
            LogAssert.Expect(LogType.Error,
                InboundTestIds.RequestFailedRegex(InboundTestIds.ClientOne, InboundTestIds.TypeHeldRod, "Dispatch"));

            Assert.DoesNotThrow(() => h.Dispatcher.DispatchTick(DispatchTick));

            Assert.AreEqual(2, h.Handler.Calls.Count);
            Assert.AreEqual(InboundTestIds.ClientTwo, h.Handler.Calls[0].Context.ClientId);
            Assert.AreEqual(InboundTestIds.ClientThree, h.Handler.Calls[1].Context.ClientId);
        }

        // =========================================================================================
        // Connections.
        // =========================================================================================

        [Test]
        public void DispatchTick_HandlerRemovesItsOwnConnection_SkipsItsLaterRequestsAndDispatchesOtherClients()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne, InboundTestIds.ClientTwo);
            h.Handler.Behaviour = context =>
            {
                if (context.ClientId == InboundTestIds.ClientOne)
                {
                    h.Dispatcher.RemoveConnection(InboundTestIds.ClientOne);
                }
            };
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            h.SendRequest(InboundTestIds.ClientTwo, InboundTestIds.TypePlainRod, Sequence, PlainBody());

            Assert.DoesNotThrow(() => h.Dispatcher.DispatchTick(DispatchTick));

            Assert.AreEqual(2, h.Handler.Calls.Count, "Client 1's first request and client 2's request.");
            Assert.AreEqual(InboundTestIds.ClientOne, h.Handler.Calls[0].Context.ClientId);
            Assert.AreEqual(InboundTestIds.ClientTwo, h.Handler.Calls[1].Context.ClientId);
        }

        [Test]
        public void RemoveConnection_BeforeDispatchTick_RequestsOfTheRemovedConnectionAreNotDispatched()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());

            h.Dispatcher.RemoveConnection(InboundTestIds.ClientOne);
            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.AreEqual(0, h.Handler.Calls.Count);
            Assert.AreEqual(0, h.Dispatcher.PendingRequestCount(InboundTestIds.ClientOne));
        }

        [Test]
        public void AddConnection_RemoveThenAddInFullZoneBeforeTick_ReusesSlotAndNeverDispatchesRemovedRequests()
        {
            // Arrange — all MAX_PLAYERS_PER_ZONE slots taken.
            var h = InboundDispatchHarness.CreateReady();
            h.AddClients(InboundTestIds.ClientOne, InboundTestIds.ZoneClientCount);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());

            // Act
            h.Dispatcher.RemoveConnection(InboundTestIds.ClientOne);
            bool added = h.Dispatcher.AddConnection(InboundTestIds.ClientNew);
            h.RegisterClientIdentity(InboundTestIds.ClientNew);
            bool newClientAccepted = h.SendRequest(InboundTestIds.ClientNew, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            h.Dispatcher.DispatchTick(DispatchTick);

            // Assert
            Assert.IsTrue(added, "The removed connection's slot is released by AddConnection outside a pass.");
            Assert.IsTrue(newClientAccepted);
            Assert.AreEqual(1, h.Handler.Calls.Count);
            Assert.AreEqual(InboundTestIds.ClientNew, h.Handler.Calls[0].Context.ClientId);
        }

        [Test]
        public void AddConnection_ZoneFull_ReturnsFalseAndLogsOneError()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady();
            h.AddClients(InboundTestIds.ClientOne, InboundTestIds.ZoneClientCount);
            LogAssert.Expect(LogType.Error, InboundTestIds.AddConnectionFailedRegex(InboundTestIds.ClientNew));

            // Act
            bool added = h.Dispatcher.AddConnection(InboundTestIds.ClientNew);

            // Assert
            Assert.IsFalse(added);
        }

        [Test]
        public void AddConnection_ClientAlreadyLive_ReturnsFalseAndLogsOneError()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            LogAssert.Expect(LogType.Error, InboundTestIds.AddConnectionFailedRegex(InboundTestIds.ClientOne));

            // Act
            bool added = h.Dispatcher.AddConnection(InboundTestIds.ClientOne);

            // Assert
            Assert.IsFalse(added);
        }

        [Test]
        public void AddConnection_RemovedClientInsideAHandler_ReturnsFalseAndLogsOneError()
        {
            // Arrange — inside a pass a removed connection keeps its slot, so the client is still known.
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            bool? added = null;
            h.Handler.Behaviour = context =>
            {
                h.Dispatcher.RemoveConnection(InboundTestIds.ClientOne);
                added = h.Dispatcher.AddConnection(InboundTestIds.ClientOne);
            };
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            LogAssert.Expect(LogType.Error, InboundTestIds.AddConnectionFailedRegex(InboundTestIds.ClientOne));

            // Act
            Assert.DoesNotThrow(() => h.Dispatcher.DispatchTick(DispatchTick));

            // Assert
            Assert.AreEqual(false, added);
        }

        // =========================================================================================
        // Guard log summary.
        // =========================================================================================

        [Test]
        public void DispatchTick_ThreeIdenticalGuardRejections_LogsOneFullWarningAndOneSummary()
        {
            // Arrange
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.GuardChain.ClearSessionReady(InboundTestIds.ClientOne);
            for (int i = 0; i < IdenticalRejectionCount; i++)
            {
                h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            }
            using var capture = new InboundLogCapture(InboundTestIds.GuardLogPrefix);

            // Act
            h.Dispatcher.DispatchTick(DispatchTick);

            // Assert
            Assert.AreEqual(2, capture.CountWarnings(), "One full warning plus one summary line.");
            StringAssert.Contains("arrived before SessionReady", capture.WarningAt(0));
            StringAssert.Contains($"{IdenticalRejectionCount - 1} further rejections not logged", capture.WarningAt(1));
        }
    }
}
