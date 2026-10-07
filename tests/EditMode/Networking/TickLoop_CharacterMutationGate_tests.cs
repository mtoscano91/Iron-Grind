using System;
using System.Collections.Generic;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 030 — the per-character mutation gate
    /// (<see cref="CharacterMutationGate"/>, ADR-011 Decision 4).
    /// </summary>
    [TestFixture]
    internal sealed class TickLoop_CharacterMutationGate_Tests
    {
        private const uint CHARACTER_A_RAW = 101u;
        private const uint CHARACTER_B_RAW = 202u;

        private readonly CharacterID _charA = new CharacterID(CHARACTER_A_RAW);
        private readonly CharacterID _charB = new CharacterID(CHARACTER_B_RAW);

        private CharacterMutationGate _gate;
        private List<CharacterID> _openedEvents;
        private bool _heldInsideHandler;

        [SetUp]
        public void SetUp()
        {
            _gate = new CharacterMutationGate();
            _openedEvents = new List<CharacterID>();
            _heldInsideHandler = true;
        }

        private void OnGateOpenedHandler(CharacterID charId)
        {
            _openedEvents.Add(charId);
            _heldInsideHandler = _gate.IsHeld(charId);
        }

        private void ThrowingHandler(CharacterID charId)
        {
            throw new InvalidOperationException("subscriber boom");
        }

        [Test]
        public void IsHeld_DefaultCloseOpen_TracksHoldStatePerCharacter()
        {
            // Arrange / Act / Assert
            Assert.IsFalse(_gate.IsHeld(_charA), "A character never closed is not held.");

            _gate.Close(_charA);
            Assert.IsTrue(_gate.IsHeld(_charA));
            Assert.IsFalse(_gate.IsHeld(_charB), "Closing one character must not hold another.");

            _gate.Open(_charA);
            Assert.IsFalse(_gate.IsHeld(_charA));
        }

        [Test]
        public void Close_AlreadyClosed_ThrowsAndLeavesGateClosed()
        {
            // Arrange
            _gate.Close(_charA);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _gate.Close(_charA));
            Assert.IsTrue(_gate.IsHeld(_charA));
        }

        [Test]
        public void Open_ClosedGate_RaisesEventOnceWithCharacterAfterGateIsOpen()
        {
            // Arrange
            _gate.OnGateOpened += OnGateOpenedHandler;
            _gate.Close(_charA);

            // Act
            _gate.Open(_charA);

            // Assert
            Assert.AreEqual(1, _openedEvents.Count);
            Assert.AreEqual(_charA, _openedEvents[0]);
            Assert.IsFalse(_heldInsideHandler, "IsHeld must already be false when the event is raised.");
            _gate.OnGateOpened -= OnGateOpenedHandler;
        }

        [Test]
        public void Open_GateNeverClosed_DoesNothingAndRaisesNoEvent()
        {
            // Arrange
            _gate.OnGateOpened += OnGateOpenedHandler;

            // Act
            TestDelegate open = () => _gate.Open(_charA);

            // Assert
            Assert.DoesNotThrow(open);
            Assert.AreEqual(0, _openedEvents.Count);
            _gate.OnGateOpened -= OnGateOpenedHandler;
        }

        [Test]
        public void Open_CloseOpenOpen_RaisesEventOnlyOnce()
        {
            // Arrange
            _gate.OnGateOpened += OnGateOpenedHandler;

            // Act
            _gate.Close(_charA);
            _gate.Open(_charA);
            _gate.Open(_charA);

            // Assert
            Assert.AreEqual(1, _openedEvents.Count);
            _gate.OnGateOpened -= OnGateOpenedHandler;
        }

        [Test]
        public void Close_AfterOpen_WorksAgainOnSameCharacter()
        {
            // Arrange
            _gate.Close(_charA);
            _gate.Open(_charA);

            // Act
            TestDelegate reclose = () => _gate.Close(_charA);

            // Assert
            Assert.DoesNotThrow(reclose);
            Assert.IsTrue(_gate.IsHeld(_charA));
        }

        [Test]
        public void Open_SubscriberThrows_ExceptionPropagatesAndGateIsAlreadyOpen()
        {
            // Arrange
            _gate.OnGateOpened += ThrowingHandler;
            _gate.Close(_charA);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _gate.Open(_charA));
            Assert.IsFalse(_gate.IsHeld(_charA));
            _gate.OnGateOpened -= ThrowingHandler;
        }
    }
}
