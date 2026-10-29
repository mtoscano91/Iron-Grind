using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace IronGrind.Tests.EditMode.MovementSystem
{
    /// <summary>
    /// EditMode unit tests for Movement Story 006 - Prohibited state (CR-MOV-8) and the one-tick auto-face
    /// override (CR-MOV-9), through <see cref="IMovementSystem"/>.
    /// </summary>
    [TestFixture]
    internal sealed class MovementSystem_ProhibitedAndOverride_Tests
    {
        private const float DT = ServerTickLoop.FIXED_DELTA_TIME;
        private const float TOLERANCE = 1e-4f;
        private const short FULL = 32767;

        private static readonly EntityID PlayerA = new EntityID(1001u);
        private static readonly EntityID PlayerB = new EntityID(1002u);

        private FakeIntents _intents;
        private FakeSpeeds _speeds;
        private FakeSampler _sampler;
        private FakeIndex _index;
        private MovementService _service;
        private IMovementSystem _movement;

        [SetUp]
        public void SetUp()
        {
            _intents = new FakeIntents();
            _speeds = new FakeSpeeds();
            _sampler = new FakeSampler();
            _index = new FakeIndex();
            _service = new MovementService(_intents, _speeds, _sampler, _index);
            _movement = _service;
        }

        private sealed class FakeIntents : IMovementIntentSource
        {
            private readonly bool[] _has = new bool[ZoneSlotPool.MAX_PLAYERS_PER_ZONE];
            private readonly MovementIntentMessage[] _msg = new MovementIntentMessage[ZoneSlotPool.MAX_PLAYERS_PER_ZONE];

            public bool Sticky;

            public void Set(int slot, short dirX, short dirZ, short facing)
            {
                _has[slot] = true;
                _msg[slot] = new MovementIntentMessage(dirX, dirZ, facing, 0u);
            }

            public bool TryTakeIntent(int playerSlot, out MovementIntentMessage intent)
            {
                intent = _msg[playerSlot];
                bool has = _has[playerSlot];
                if (!Sticky) _has[playerSlot] = false;
                return has;
            }
        }

        private sealed class FakeSpeeds : IMovementSpeedSource
        {
            public float Value = 5.0f;
            public bool Fail;
            public int Reads;

            public bool TryGetMovementSpeed(EntityID id, out float speed)
            {
                Reads++;
                speed = Value;
                return !Fail;
            }
        }

        private sealed class FakeSampler : INavMeshSampler
        {
            public bool Hits = true;
            public int Calls;
            public Vector3 LastCandidate;

            public bool TrySample(Vector3 candidate, float radius, out Vector3 hit)
            {
                Calls++;
                LastCandidate = candidate;
                hit = candidate;
                return Hits;
            }
        }

        private sealed class FakeIndex : IEntitySlotIndex
        {
            private readonly Dictionary<uint, int> _map = new Dictionary<uint, int>();

            public void Map(EntityID id, int slot) { _map[id.RawValue] = slot; }

            public bool TryGetSlotIndex(EntityID id, out int index) { return _map.TryGetValue(id.RawValue, out index); }
        }

        private void Seed(int slot, EntityID id, Vector3 position, short facing = 0)
        {
            _index.Map(id, slot);
            _service.SeedForTesting(slot, id, position, facing);
        }

        private Vector3 Pos(EntityID id)
        {
            Assert.IsTrue(_service.TryGetPosition(id, out Vector3 pos));
            return pos;
        }

        private short Facing(EntityID id)
        {
            Assert.IsTrue(_service.TryGetFacing(id, out short facing));
            return facing;
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, TOLERANCE);
            Assert.AreEqual(expected.y, actual.y, TOLERANCE);
            Assert.AreEqual(expected.z, actual.z, TOLERANCE);
        }

        // ---- Interface / unknown entity ----

        [Test]
        public void MovementService_ImplementsIMovementSystem()
        {
            Assert.IsInstanceOf<IMovementSystem>(_service);
        }

        [Test]
        public void IMovementSystem_DeclaresExactlySetProhibitedAndInjectFacingOverride()
        {
            var methods = typeof(IMovementSystem).GetMethods();

            Assert.AreEqual(2, methods.Length);
            Assert.IsNotNull(typeof(IMovementSystem).GetMethod(nameof(IMovementSystem.SetProhibited),
                new[] { typeof(EntityID), typeof(bool) }));
            Assert.IsNotNull(typeof(IMovementSystem).GetMethod(nameof(IMovementSystem.InjectFacingOverride),
                new[] { typeof(EntityID), typeof(Vector3) }));
        }

        [Test]
        public void MovementSystem_SlotHeldByAnotherEntity_BothCallsReturnFalseAndOwnerUnaffected()
        {
            // The index maps PlayerB to a slot movement still holds for PlayerA (TryFindSlot id check).
            Seed(0, PlayerA, Vector3.zero);
            _index.Map(PlayerB, 0);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 450);

            Assert.IsFalse(_movement.SetProhibited(PlayerB, true));
            Assert.IsFalse(_movement.InjectFacingOverride(PlayerB, Vector3.forward));
            _service.Run(1u, DT);

            AssertNear(new Vector3(0.25f, 0f, 0f), Pos(PlayerA));
            Assert.AreEqual((short)450, Facing(PlayerA));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSystem_KnownEntity_BothCallsReturnTrue()
        {
            Seed(0, PlayerA, Vector3.zero);

            Assert.IsTrue(_movement.SetProhibited(PlayerA, true));
            Assert.IsTrue(_movement.InjectFacingOverride(PlayerA, Vector3.forward));
        }

        [Test]
        public void MovementSystem_UnknownEntity_BothCallsReturnFalseWithoutLogOrStateChange()
        {
            Seed(0, PlayerA, new Vector3(1f, 0f, 2f), 300);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 450);

            Assert.IsFalse(_movement.SetProhibited(new EntityID(9999u), true));
            Assert.IsFalse(_movement.InjectFacingOverride(new EntityID(9999u), Vector3.forward));
            _service.Run(1u, DT);

            AssertNear(new Vector3(1.25f, 0f, 2f), Pos(PlayerA));
            Assert.AreEqual((short)450, Facing(PlayerA));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSystem_IndexedButNeverSeededSlot_BothCallsReturnFalse()
        {
            _index.Map(PlayerA, 3);

            Assert.IsFalse(_movement.SetProhibited(PlayerA, true));
            Assert.IsFalse(_movement.InjectFacingOverride(PlayerA, Vector3.forward));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Prohibited ----

        [Test]
        public void MovementService_Prohibited_NonZeroIntent_NoDeltaFacingStoredSamplerNotCalled()
        {
            // AC-MOV-18
            Seed(0, PlayerA, Vector3.zero, 0);
            _movement.SetProhibited(PlayerA, true);
            _intents.Set(0, FULL, 0, 450);

            _service.Run(1u, DT);

            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
            Assert.AreEqual((short)450, Facing(PlayerA));
            Assert.AreEqual(0, _sampler.Calls);
            Assert.AreEqual(0, _speeds.Reads);
        }

        [Test]
        public void MovementService_Prohibited_TicksTenToTwenty_PositionConstantThenClearedMoves()
        {
            // AC-MOV-23
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 0);
            _movement.SetProhibited(PlayerA, true);

            for (uint tick = 10u; tick <= 20u; tick++)
            {
                _service.Run(tick, DT);
                Assert.AreEqual(Vector3.zero, Pos(PlayerA), "tick " + tick);
            }

            _movement.SetProhibited(PlayerA, false);
            _service.Run(21u, DT);

            AssertNear(new Vector3(0.25f, 0f, 0f), Pos(PlayerA));
            Assert.AreEqual(1, _sampler.Calls, "only tick 21 samples");
        }

        [Test]
        public void MovementService_Prohibited_IntentStillTakenSoItIsNotReplayedAfterClear()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Set(0, FULL, 0, 0);
            _movement.SetProhibited(PlayerA, true);
            _service.Run(1u, DT);
            _movement.SetProhibited(PlayerA, false);

            _service.Run(2u, DT);

            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
            Assert.AreEqual(0, _sampler.Calls);
        }

        [Test]
        public void MovementService_Prohibited_OtherPlayerUnaffected()
        {
            Seed(0, PlayerA, Vector3.zero);
            Seed(1, PlayerB, Vector3.zero);
            _intents.Set(0, FULL, 0, 0);
            _intents.Set(1, FULL, 0, 0);
            _movement.SetProhibited(PlayerA, true);

            _service.Run(1u, DT);

            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
            AssertNear(new Vector3(0.25f, 0f, 0f), Pos(PlayerB));
            Assert.AreEqual(1, _sampler.Calls);
        }

        // ---- Override: one tick ----

        [Test]
        public void MovementService_Override_AppliesOnExactlyOneTick()
        {
            // CR-MOV-9 and the QA "Override one tick" case.
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 450);

            for (uint tick = 1u; tick <= 6u; tick++)
            {
                if (tick == 5u)
                    _movement.InjectFacingOverride(PlayerA, Vector3.forward);
                Vector3 before = Pos(PlayerA);
                _service.Run(tick, DT);
                Vector3 delta = Pos(PlayerA) - before;

                if (tick == 5u)
                    AssertNear(new Vector3(0f, 0f, 0.25f), delta);
                else
                    AssertNear(new Vector3(0.25f, 0f, 0f), delta);
            }
        }

        [Test]
        public void MovementService_Override_WithForwardClientIntent_OverrideWinsOnlyThatTick()
        {
            // EC-MOV-8: the client message is ignored on tick 5 only.
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, 0, FULL, 450);

            _service.Run(4u, DT);
            _movement.InjectFacingOverride(PlayerA, Vector3.right);
            _service.Run(5u, DT);
            _service.Run(6u, DT);

            // Tick 4: +Z 0.25, tick 5: +X 0.25 (override), tick 6: +Z 0.25.
            AssertNear(new Vector3(0.25f, 0f, 0.5f), Pos(PlayerA));
            Assert.AreEqual((short)450, Facing(PlayerA));
        }

        [Test]
        public void MovementService_Override_NoClientMessage_StillMovesAlongOverride()
        {
            Seed(0, PlayerA, Vector3.zero);
            _movement.InjectFacingOverride(PlayerA, Vector3.right);

            _service.Run(1u, DT);

            AssertNear(new Vector3(0.25f, 0f, 0f), Pos(PlayerA));
        }

        [Test]
        public void MovementService_Override_ZeroDirectionClientIntent_StillMovesAlongOverride()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Set(0, 0, 0, 450);
            _movement.InjectFacingOverride(PlayerA, Vector3.right);

            _service.Run(1u, DT);

            AssertNear(new Vector3(0.25f, 0f, 0f), Pos(PlayerA));
            Assert.AreEqual((short)900, Facing(PlayerA));
        }

        [Test]
        public void MovementService_Override_OneShotClientIntent_NotReplayedAfterOverrideTick()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Set(0, FULL, 0, 450);
            _movement.InjectFacingOverride(PlayerA, Vector3.forward);

            _service.Run(1u, DT);
            _service.Run(2u, DT);

            AssertNear(new Vector3(0f, 0f, 0.25f), Pos(PlayerA));
            Assert.AreEqual(1, _sampler.Calls);
        }

        [Test]
        public void MovementService_Override_SpeedReadFails_ConsumedAndNextTickUsesClientIntent()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 450);
            _speeds.Fail = true;
            _movement.InjectFacingOverride(PlayerA, Vector3.forward);
            LogAssert.Expect(LogType.Error, new Regex(@"read failed; no valid speed yet, skipping movement"));

            _service.Run(1u, DT);
            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
            Assert.AreEqual((short)0, Facing(PlayerA), "override facing applies even without movement");

            _speeds.Fail = false;
            _service.Run(2u, DT);

            AssertNear(new Vector3(0.25f, 0f, 0f), Pos(PlayerA));
            Assert.AreEqual((short)450, Facing(PlayerA));
        }

        [Test]
        public void MovementService_Override_SamplerNoHit_ZeroDisplacementThenNextTickMoves()
        {
            // EC-MOV-9 / AC-MOV-20 server part.
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, 0, FULL, 0);
            _service.Run(4u, DT);
            Vector3 before = Pos(PlayerA);

            _movement.InjectFacingOverride(PlayerA, Vector3.right);
            _sampler.Hits = false;
            _service.Run(5u, DT);
            Assert.AreEqual(before, Pos(PlayerA));
            AssertNear(before + new Vector3(0.25f, 0f, 0f), _sampler.LastCandidate);
            Assert.AreEqual((short)900, Facing(PlayerA), "override facing applies on a NavMesh miss");

            _sampler.Hits = true;
            _service.Run(6u, DT);
            AssertNear(before + new Vector3(0f, 0f, 0.25f), Pos(PlayerA));
        }

        [Test]
        public void MovementService_Override_InjectedTwice_SecondWinsAndNothingExtraNextTick()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, 0, FULL, 0);

            _movement.InjectFacingOverride(PlayerA, Vector3.right);
            _movement.InjectFacingOverride(PlayerA, Vector3.left);
            _service.Run(5u, DT);
            AssertNear(new Vector3(-0.25f, 0f, 0f), Pos(PlayerA));
            Assert.AreEqual((short)2700, Facing(PlayerA));

            _service.Run(6u, DT);
            AssertNear(new Vector3(-0.25f, 0f, 0.25f), Pos(PlayerA));
            Assert.AreEqual((short)0, Facing(PlayerA));
        }

        // ---- Override: facing ----

        [TestCase(1f, 0f, 0f, (short)900)]
        [TestCase(0f, 0f, 1f, (short)0)]
        [TestCase(0f, 0f, -1f, (short)1800)]
        [TestCase(-0f, 0f, -1f, (short)1800)]
        [TestCase(-1f, 0f, 0f, (short)2700)]
        [TestCase(1f, 0f, 1f, (short)450)]
        [TestCase(-1f, 0f, -1f, (short)2250)]
        [TestCase(-0.00175f, 0f, 1f, (short)3599)]
        public void MovementService_Override_StoresYawFacingAndRestoresClientFacingNextTick(
            float x, float y, float z, short expected)
        {
            Seed(0, PlayerA, Vector3.zero, 0);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 450);

            _movement.InjectFacingOverride(PlayerA, new Vector3(x, y, z));
            _service.Run(1u, DT);
            Assert.AreEqual(expected, Facing(PlayerA), "override tick: client facing ignored");

            _service.Run(2u, DT);
            Assert.AreEqual((short)450, Facing(PlayerA), "next tick: client facing again");
        }

        [Test]
        public void MovementService_Override_NonUnitDirection_NormalizedAndYIgnored()
        {
            Seed(0, PlayerA, Vector3.zero);

            _movement.InjectFacingOverride(PlayerA, new Vector3(3f, 7f, 4f));
            _service.Run(1u, DT);

            Vector3 pos = Pos(PlayerA);
            AssertNear(new Vector3(0.15f, 0f, 0.2f), pos);
            Assert.AreEqual(0.25f, new Vector2(pos.x, pos.z).magnitude, TOLERANCE);
            Assert.AreEqual(0f, _sampler.LastCandidate.y, 0f);
        }

        [TestCase(1e30f, 0f, 0f, 0.25f, 0f, (short)900)]
        [TestCase(1e-30f, 0f, 0f, 0.25f, 0f, (short)900)]
        [TestCase(-1e30f, 0f, 1e30f, -0.1767767f, 0.1767767f, (short)3150)]
        public void MovementService_Override_ExtremeFiniteMagnitude_NormalizedNotTreatedAsZero(
            float x, float y, float z, float expectedX, float expectedZ, short expectedFacing)
        {
            Seed(0, PlayerA, Vector3.zero);

            _movement.InjectFacingOverride(PlayerA, new Vector3(x, y, z));
            _service.Run(1u, DT);

            AssertNear(new Vector3(expectedX, 0f, expectedZ), Pos(PlayerA));
            Assert.AreEqual(expectedFacing, Facing(PlayerA));
        }

        // ---- Override while Prohibited ----

        [Test]
        public void MovementService_Override_WhileProhibited_NoMoveFacingAppliedAndConsumed()
        {
            Seed(0, PlayerA, Vector3.zero, 100);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 450);
            _movement.SetProhibited(PlayerA, true);

            _movement.InjectFacingOverride(PlayerA, Vector3.forward);
            _service.Run(5u, DT);

            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
            Assert.AreEqual(0, _sampler.Calls);
            Assert.AreEqual(0, _speeds.Reads);
            Assert.AreEqual((short)0, Facing(PlayerA));

            _service.Run(6u, DT);
            Assert.AreEqual((short)450, Facing(PlayerA));
            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
        }

        [Test]
        public void MovementService_ZeroOverride_WhileProhibited_FacingUnchangedAndClientFacingNotApplied()
        {
            Seed(0, PlayerA, Vector3.zero, 100);
            _intents.Set(0, FULL, 0, 450);
            _movement.SetProhibited(PlayerA, true);
            _movement.InjectFacingOverride(PlayerA, Vector3.zero);

            _service.Run(1u, DT);

            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
            Assert.AreEqual((short)100, Facing(PlayerA));
            Assert.AreEqual(0, _sampler.Calls);
            Assert.AreEqual(0, _speeds.Reads);
        }

        [Test]
        public void MovementService_Prohibited_EntryAndExit_DoNotClearPendingOverride()
        {
            // CR-MOV-8: no auto-face re-arm, no clearing.
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 0);
            _movement.InjectFacingOverride(PlayerA, Vector3.forward);

            _movement.SetProhibited(PlayerA, true);
            _movement.SetProhibited(PlayerA, false);
            _service.Run(1u, DT);

            AssertNear(new Vector3(0f, 0f, 0.25f), Pos(PlayerA));
        }

        [Test]
        public void MovementService_Prohibited_ClearedAfterConsumedOverride_DoesNotReArm()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 0);
            _movement.SetProhibited(PlayerA, true);
            _movement.InjectFacingOverride(PlayerA, Vector3.forward);
            _service.Run(1u, DT);

            _movement.SetProhibited(PlayerA, false);
            _service.Run(2u, DT);

            AssertNear(new Vector3(0.25f, 0f, 0f), Pos(PlayerA));
        }

        // ---- Zero / non-finite override ----

        [TestCase(0f, 1f, 0f)]
        [TestCase(0f, 0f, 0f)]
        [TestCase(float.NaN, 0f, 0f)]
        [TestCase(0f, 0f, float.PositiveInfinity)]
        [TestCase(0f, float.NaN, 1f)]
        public void MovementService_Override_ZeroOrNonFinite_ZeroDisplacementFacingUnchangedThenNextTickMoves(
            float x, float y, float z)
        {
            Seed(0, PlayerA, Vector3.zero, 100);
            _intents.Set(0, FULL, 0, 450);
            _service.Run(1u, DT);
            Vector3 afterFirst = Pos(PlayerA);
            int callsAfterFirst = _sampler.Calls;
            int readsAfterFirst = _speeds.Reads;

            _intents.Set(0, FULL, 0, 900);
            _movement.InjectFacingOverride(PlayerA, new Vector3(x, y, z));
            _service.Run(2u, DT);

            Assert.AreEqual(afterFirst, Pos(PlayerA));
            Assert.AreEqual((short)450, Facing(PlayerA), "facing unchanged from the previous tick");
            Assert.AreEqual(callsAfterFirst, _sampler.Calls);
            Assert.AreEqual(readsAfterFirst, _speeds.Reads);

            _intents.Set(0, FULL, 0, 900);
            _service.Run(3u, DT);

            AssertNear(afterFirst + new Vector3(0.25f, 0f, 0f), Pos(PlayerA));
            Assert.AreEqual((short)900, Facing(PlayerA));
        }

        // ---- Seeding ----

        [Test]
        public void MovementService_SeedForTesting_ClearsProhibitedAndPendingOverride()
        {
            Seed(0, PlayerA, Vector3.zero);
            _movement.SetProhibited(PlayerA, true);
            _movement.InjectFacingOverride(PlayerA, Vector3.forward);

            Seed(0, PlayerA, Vector3.zero);
            _intents.Set(0, FULL, 0, 0);
            _service.Run(1u, DT);

            AssertNear(new Vector3(0.25f, 0f, 0f), Pos(PlayerA));
        }

        // ---- Allocation ----

        [Test]
        public void MovementService_Run_FiftyPlayersHalfProhibitedHalfOverride_DoesNotAllocate()
        {
            _intents = new FakeIntents();
            _speeds = new FakeSpeeds();
            _sampler = new FakeSampler();
            _service = new MovementService(_intents, _speeds, _sampler, _index);
            _movement = _service;
            _intents.Sticky = true;
            var ids = new EntityID[ZoneSlotPool.MAX_PLAYERS_PER_ZONE];
            for (int slot = 0; slot < ZoneSlotPool.MAX_PLAYERS_PER_ZONE; slot++)
            {
                ids[slot] = new EntityID(2000u + (uint)slot);
                Seed(slot, ids[slot], Vector3.zero);
                _intents.Set(slot, FULL, FULL, 450);
                if (slot % 2 == 0)
                    _movement.SetProhibited(ids[slot], true);
            }

            // Warm-up tick with overrides pending on the odd slots.
            for (int slot = 1; slot < ZoneSlotPool.MAX_PLAYERS_PER_ZONE; slot += 2)
                _movement.InjectFacingOverride(ids[slot], new Vector3(1f, 0f, 1f));
            _service.Run(1u, DT);

            // Re-arm outside the measured region (the id lookup is not part of the tick).
            for (int slot = 1; slot < ZoneSlotPool.MAX_PLAYERS_PER_ZONE; slot += 2)
                _movement.InjectFacingOverride(ids[slot], new Vector3(0f, 0f, 1f));

            Assert.That(() => _service.Run(2u, DT), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
