using System;
using System.Collections.Generic;
using System.IO;
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
    /// EditMode unit tests for Movement Story 005 - the movement tick step, NavMesh validation seam, speed-failure
    /// logging, read API and AC-MOV-21 (no runtime bake).
    /// </summary>
    [TestFixture]
    internal sealed class MovementSystem_TickStep_Tests
    {
        private const float DT = ServerTickLoop.FIXED_DELTA_TIME;
        private const float TOLERANCE = 1e-4f;
        private const short FULL = 32767;
        private const string ERROR_PATTERN = @"\[Movement\] Invalid movement speed";
        private const string SOURCE_FOLDER = "src/ServerLogic/MovementSystem";

        private static readonly EntityID PlayerA = new EntityID(1001u);
        private static readonly EntityID PlayerB = new EntityID(1002u);

        private FakeIntents _intents;
        private FakeSpeeds _speeds;
        private FakeSampler _sampler;
        private FakeIndex _index;
        private MovementService _service;
        private List<string> _events;

        [SetUp]
        public void SetUp()
        {
            _events = new List<string>();
            _intents = new FakeIntents(_events);
            _speeds = new FakeSpeeds(_events);
            _sampler = new FakeSampler(_events);
            _index = new FakeIndex();
            _service = new MovementService(_intents, _speeds, _sampler, _index);
        }

        private sealed class FakeIntents : IMovementIntentSource
        {
            private readonly bool[] _has = new bool[ZoneSlotPool.MAX_PLAYERS_PER_ZONE];
            private readonly MovementIntentMessage[] _msg = new MovementIntentMessage[ZoneSlotPool.MAX_PLAYERS_PER_ZONE];
            private readonly List<string> _events;

            public bool Sticky;

            public FakeIntents(List<string> events) { _events = events; }

            public void Set(int slot, short dirX, short dirZ, short facing)
            {
                _has[slot] = true;
                _msg[slot] = new MovementIntentMessage(dirX, dirZ, facing, 0u);
            }

            public bool TryTakeIntent(int playerSlot, out MovementIntentMessage intent)
            {
                _events?.Add("intent" + playerSlot);
                intent = _msg[playerSlot];
                bool has = _has[playerSlot];
                if (!Sticky) _has[playerSlot] = false;
                return has;
            }
        }

        private sealed class FakeSpeeds : IMovementSpeedSource
        {
            private readonly List<string> _events;
            public bool Succeeds = true;
            public float Value = 5.0f;
            public int Reads;

            public FakeSpeeds(List<string> events) { _events = events; }

            public bool TryGetMovementSpeed(EntityID id, out float speed)
            {
                Reads++;
                _events?.Add("speed");
                speed = Value;
                return Succeeds;
            }
        }

        private sealed class FakeSampler : INavMeshSampler
        {
            private readonly List<string> _events;
            public bool Hits = true;
            public bool UseFixedHit;
            public Vector3 FixedHit;
            public int Calls;
            public Vector3 LastCandidate;
            public float LastRadius;

            public FakeSampler(List<string> events) { _events = events; }

            public bool TrySample(Vector3 candidate, float radius, out Vector3 hit)
            {
                Calls++;
                _events?.Add("sample");
                LastCandidate = candidate;
                LastRadius = radius;
                hit = UseFixedHit ? FixedHit : candidate;
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

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, TOLERANCE);
            Assert.AreEqual(expected.y, actual.y, TOLERANCE);
            Assert.AreEqual(expected.z, actual.z, TOLERANCE);
        }

        // ---- Constructor ----

        [Test]
        public void MovementService_Constructor_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new MovementService(null, _speeds, _sampler, _index));
            Assert.Throws<ArgumentNullException>(() => new MovementService(_intents, null, _sampler, _index));
            Assert.Throws<ArgumentNullException>(() => new MovementService(_intents, _speeds, null, _index));
            Assert.Throws<ArgumentNullException>(() => new MovementService(_intents, _speeds, _sampler, null));
        }

        // ---- Hit / no hit ----

        [Test]
        public void MovementService_Run_SamplerHit_CommitsHitIncludingY()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Set(0, 0, FULL, 0);
            _sampler.UseFixedHit = true;
            _sampler.FixedHit = new Vector3(0f, 0.2f, 0.24f);

            _service.Run(1u, DT);

            Assert.IsTrue(_service.TryGetPosition(PlayerA, out Vector3 pos));
            AssertNear(new Vector3(0f, 0.2f, 0.24f), pos);
            AssertNear(new Vector3(0f, 0f, 0.25f), _sampler.LastCandidate);
            Assert.AreEqual(0.5f, _sampler.LastRadius, 0f);
            Assert.AreEqual(MovementService.NAVMESH_SAMPLE_RADIUS, _sampler.LastRadius, 0f);
            Assert.AreEqual(1, _sampler.Calls);
        }

        [Test]
        public void MovementService_Run_SamplerMiss_KeepsPreviousPositionAndLogsNothing()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Set(0, 0, FULL, 0);
            _sampler.Hits = false;

            _service.Run(1u, DT);

            _service.TryGetPosition(PlayerA, out Vector3 pos);
            Assert.AreEqual(Vector3.zero, pos);
            Assert.AreEqual(1, _sampler.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_Run_SamplerMiss_NextCandidateStartsFromUnchangedPosition()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, 0, FULL, 0);
            _sampler.Hits = false;

            _service.Run(1u, DT);
            _service.Run(2u, DT);

            AssertNear(new Vector3(0f, 0f, 0.25f), _sampler.LastCandidate);
        }

        [Test]
        public void MovementService_Run_CommitBeforeNextTick_SecondCandidateBuiltFromSampledHit()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, 0, FULL, 0);
            _sampler.UseFixedHit = true;
            _sampler.FixedHit = new Vector3(0f, 0.3f, 0.2f);

            _service.Run(1u, DT);
            _service.Run(2u, DT);

            AssertNear(new Vector3(0f, 0.3f, 0.45f), _sampler.LastCandidate);
        }

        [Test]
        public void MovementService_Run_NonFiniteCandidate_HoldsPositionWithoutSampling()
        {
            Seed(0, PlayerA, new Vector3(float.NaN, 0f, 0f));
            _intents.Set(0, 0, FULL, 0);

            _service.Run(1u, DT);

            Assert.AreEqual(0, _sampler.Calls);
            _service.TryGetPosition(PlayerA, out Vector3 pos);
            Assert.IsTrue(float.IsNaN(pos.x), "Position held as seeded.");
        }

        [Test]
        public void MovementService_Run_IntentTaken_SecondTickWithoutNewMessageDoesNotMove()
        {
            // Exercises the take-clears contract through the fake; Story 004's buffer owns the real guarantee.
            Seed(0, PlayerA, Vector3.zero, 0);
            _intents.Set(0, 0, FULL, 900);

            _service.Run(1u, DT);
            _service.TryGetPosition(PlayerA, out Vector3 afterFirst);
            _service.Run(2u, DT);
            _service.TryGetPosition(PlayerA, out Vector3 afterSecond);

            Assert.AreEqual(afterFirst, afterSecond);
            Assert.AreEqual(1, _sampler.Calls);
        }

        // ---- Facing / idle ----

        [Test]
        public void MovementService_Run_ZeroDirectionWithMessage_StoresFacingAndSkipsSpeedAndSampler()
        {
            Seed(0, PlayerA, new Vector3(3f, 0f, 4f), 100);
            _intents.Set(0, 0, 0, 900);

            _service.Run(1u, DT);

            _service.TryGetFacing(PlayerA, out short facing);
            _service.TryGetPosition(PlayerA, out Vector3 pos);
            Assert.AreEqual((short)900, facing);
            Assert.AreEqual(new Vector3(3f, 0f, 4f), pos);
            Assert.AreEqual(0, _sampler.Calls);
            Assert.AreEqual(0, _speeds.Reads);
        }

        [Test]
        public void MovementService_Run_NoMessage_FacingAndPositionUnchanged()
        {
            Seed(0, PlayerA, new Vector3(3f, 0f, 4f), 450);

            _service.Run(1u, DT);

            _service.TryGetFacing(PlayerA, out short facing);
            _service.TryGetPosition(PlayerA, out Vector3 pos);
            Assert.AreEqual((short)450, facing);
            Assert.AreEqual(new Vector3(3f, 0f, 4f), pos);
            Assert.AreEqual(0, _sampler.Calls);
            Assert.AreEqual(0, _speeds.Reads);
        }

        [Test]
        public void MovementService_Run_MovingMessage_StoresFacingAsReceived()
        {
            Seed(0, PlayerA, Vector3.zero, 0);
            _intents.Set(0, FULL, 0, 2700);

            _service.Run(1u, DT);

            _service.TryGetFacing(PlayerA, out short facing);
            Assert.AreEqual((short)2700, facing);
        }

        // ---- Order ----

        [Test]
        public void MovementService_Run_MixedSlots_RecordsIntentThenSpeedThenSamplerOnlyForMovingSlot()
        {
            Seed(0, PlayerA, Vector3.zero);
            Seed(1, PlayerB, Vector3.zero);
            Seed(2, new EntityID(1003u), Vector3.zero);
            _intents.Set(0, 0, FULL, 0);
            _intents.Set(2, 0, 0, 300);

            _service.Run(1u, DT);

            CollectionAssert.AreEqual(
                new[] { "intent0", "speed", "sample", "intent1", "intent2" }, _events);
        }

        [Test]
        public void MovementService_Run_UnoccupiedSlots_AreNeverQueried()
        {
            Seed(7, PlayerA, Vector3.zero);

            _service.Run(1u, DT);

            CollectionAssert.AreEqual(new[] { "intent7" }, _events);
        }

        // ---- Two players ----

        [Test]
        public void MovementService_Run_OneMovingOneIdle_IndependentStateAndOneSamplerCall()
        {
            Seed(0, PlayerA, Vector3.zero, 10);
            Seed(1, PlayerB, new Vector3(5f, 0f, 5f), 20);
            _intents.Set(0, FULL, 0, 111);

            _service.Run(1u, DT);

            _service.TryGetPosition(PlayerA, out Vector3 a);
            _service.TryGetPosition(PlayerB, out Vector3 b);
            _service.TryGetFacing(PlayerB, out short facingB);
            AssertNear(new Vector3(0.25f, 0f, 0f), a);
            Assert.AreEqual(new Vector3(5f, 0f, 5f), b);
            Assert.AreEqual((short)20, facingB);
            Assert.AreEqual(1, _sampler.Calls);
        }

        // ---- Speed failure ----

        [Test]
        public void MovementService_Run_SpeedFailureSequence_LogsOnlyOnValidToFailingTransitions()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, 0, FULL, 0);
            bool[] succeeds = { true, false, false, false, true, false };

            for (int i = 0; i < succeeds.Length; i++)
            {
                _speeds.Succeeds = succeeds[i];
                if (i == 1 || i == 5)
                    LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN + @" for .*: read failed; using last valid speed 5"));
                _service.Run((uint)(i + 1), DT);
                LogAssert.NoUnexpectedReceived();
            }

            _service.TryGetPosition(PlayerA, out Vector3 pos);
            Assert.AreEqual(6 * 0.25f, pos.z, TOLERANCE, "Failing ticks move with the last valid 5.0.");
            Assert.AreEqual(6, _sampler.Calls);
        }

        [Test]
        public void MovementService_Run_SpeedFailingFromFirstTick_NoMovementNoSamplerOneError()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, 0, FULL, 0);
            _speeds.Succeeds = false;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN + @" for .*: read failed; no valid speed yet, skipping movement"));

            _service.Run(1u, DT);
            _service.Run(2u, DT);
            _service.Run(3u, DT);

            _service.TryGetPosition(PlayerA, out Vector3 pos);
            Assert.AreEqual(Vector3.zero, pos);
            Assert.AreEqual(0, _sampler.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_Run_SpeedOutOfRange_TransitionLogNamesValue()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Set(0, 0, FULL, 0);
            _speeds.Value = 25.0f;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN + @" for .*: value 25 outside \[.*\]; no valid speed yet"));

            _service.Run(1u, DT);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_SeedForTesting_ResetsLastValidSpeedAndFailingFlag()
        {
            Seed(0, PlayerA, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, 0, FULL, 0);
            _service.Run(1u, DT);
            _speeds.Succeeds = false;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));
            _service.Run(2u, DT);

            Seed(0, PlayerA, Vector3.zero);
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));
            int callsBefore = _sampler.Calls;
            _service.Run(3u, DT);

            Assert.AreEqual(callsBefore, _sampler.Calls, "Last valid speed was reset, so no fallback movement.");
        }

        // ---- Resolver overload ----

        [Test]
        public void MovementSpeedResolver_NonLoggingOverload_FailureLogsNothingAndFlagsReadFailed()
        {
            float last = 0f;
            _speeds.Succeeds = false;

            bool ok = MovementSpeedResolver.TryResolve(_speeds, PlayerA, ref last, out float vEff, out bool readFailed);

            Assert.IsFalse(ok);
            Assert.IsTrue(readFailed);
            Assert.AreEqual(0f, vEff, 0f);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSpeedResolver_NonLoggingOverload_FailureWithLastValid_FallsBackSilently()
        {
            float last = 7.0f;
            _speeds.Succeeds = false;

            bool ok = MovementSpeedResolver.TryResolve(_speeds, PlayerA, ref last, out float vEff, out bool readFailed);

            Assert.IsTrue(ok);
            Assert.IsTrue(readFailed);
            Assert.AreEqual(7.0f, vEff, 0f);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSpeedResolver_NonLoggingOverload_ValidRead_ReadFailedFalseAndStoresLast()
        {
            float last = 0f;
            _speeds.Value = 6.0f;

            bool ok = MovementSpeedResolver.TryResolve(_speeds, PlayerA, ref last, out float vEff, out bool readFailed);

            Assert.IsTrue(ok);
            Assert.IsFalse(readFailed);
            Assert.AreEqual(6.0f, vEff, 0f);
            Assert.AreEqual(6.0f, last, 0f);
        }

        [Test]
        public void MovementSpeedResolver_LoggingOverload_OutOfRangeMessageNamesValueAndFallback()
        {
            float last = 5.0f;
            _speeds.Value = 25.0f;
            LogAssert.Expect(LogType.Error, new Regex(@"\[Movement\] Invalid movement speed for .*: value 25 outside \[.*\]; using last valid speed 5"));

            MovementSpeedResolver.TryResolve(_speeds, PlayerA, ref last, out _);
        }

        // ---- Read API ----

        [Test]
        public void MovementService_ReadApi_UnknownEntity_ReturnsFalseWithoutLog()
        {
            Assert.IsFalse(_service.TryGetPosition(new EntityID(9999u), out _));
            Assert.IsFalse(_service.TryGetFacing(new EntityID(9999u), out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_ReadApi_IndexedButNeverSeededSlot_ReturnsFalse()
        {
            _index.Map(PlayerA, 3);

            Assert.IsFalse(_service.TryGetPosition(PlayerA, out _));
            Assert.IsFalse(_service.TryGetFacing(PlayerA, out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_ReadApi_SlotSeededWithAnotherEntity_ReturnsFalse()
        {
            Seed(3, PlayerB, new Vector3(9f, 0f, 9f), 77);
            _index.Map(PlayerA, 3);

            Assert.IsFalse(_service.TryGetPosition(PlayerA, out _));
            Assert.IsFalse(_service.TryGetFacing(PlayerA, out _));
        }

        [Test]
        public void MovementService_ReadApi_SlotReseededWithNewEntity_OldEntityReturnsFalse()
        {
            Seed(3, PlayerA, new Vector3(1f, 0f, 1f), 10);
            _service.SeedForTesting(3, PlayerB, new Vector3(2f, 0f, 2f), 20);

            Assert.IsFalse(_service.TryGetPosition(PlayerA, out _));
        }

        [TestCase(-1)]
        [TestCase(ZoneSlotPool.MAX_PLAYERS_PER_ZONE)]
        public void MovementService_ReadApi_IndexReturnsOutOfRangeSlot_ReturnsFalse(int badSlot)
        {
            _index.Map(PlayerA, badSlot);

            Assert.IsFalse(_service.TryGetPosition(PlayerA, out _));
            Assert.IsFalse(_service.TryGetFacing(PlayerA, out _));
        }

        [Test]
        public void MovementService_ReadApi_KnownEntity_ReturnsCommittedValues()
        {
            Seed(4, PlayerA, new Vector3(1f, 2f, 3f), 1234);

            Assert.IsTrue(_service.TryGetPosition(PlayerA, out Vector3 pos));
            Assert.IsTrue(_service.TryGetFacing(PlayerA, out short facing));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), pos);
            Assert.AreEqual((short)1234, facing);
        }

        // ---- AC-MOV-21 ----

        [Test]
        public void MovementService_Run_SixtySecondsTwoMovingPlayers_SamplerCalledOncePerMovingTickPerPlayer()
        {
            Seed(0, PlayerA, Vector3.zero);
            Seed(1, PlayerB, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 0);
            _intents.Set(1, 0, FULL, 0);

            for (uint tick = 1; tick <= 1200u; tick++)
                _service.Run(tick, DT);

            Assert.AreEqual(2400, _sampler.Calls);
        }

        // The two source-scan tests read files, as the architecture tests do (AssemblyBoundary_tests.cs locates the
        // project the same way). They match code forms, not bare names, so a doc comment naming an API does not trip them.

        [Test]
        public void MovementSystem_Source_ContainsNoRuntimeBakeApis()
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, SOURCE_FOLDER);
            string[] forbidden = { "NavMeshBuilder.", ".BuildNavMesh(", ".UpdateNavMeshData(" };

            string[] files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
            Assert.IsNotEmpty(files);
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                foreach (string name in forbidden)
                    Assert.IsFalse(text.Contains(name), Path.GetFileName(file) + " contains " + name);
            }
        }

        [Test]
        public void MovementSystem_Source_OnlyUnityNavMeshSamplerUsesUnityEngineAI()
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, SOURCE_FOLDER);
            var users = new List<string>();

            foreach (string file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (text.Contains("using UnityEngine.AI;") || text.Contains("UnityEngine.AI.NavMesh"))
                    users.Add(Path.GetFileName(file));
            }

            CollectionAssert.AreEqual(new[] { "UnityNavMeshSampler.cs" }, users);
        }

        // ---- Allocation ----

        [Test]
        public void MovementService_Run_FiftyMovingPlayers_DoesNotAllocate()
        {
            // Fresh fakes without the event recorder: recording builds strings, which would allocate.
            _intents = new FakeIntents(null);
            _speeds = new FakeSpeeds(null);
            _sampler = new FakeSampler(null);
            _service = new MovementService(_intents, _speeds, _sampler, _index);
            _intents.Sticky = true;
            for (int slot = 0; slot < ZoneSlotPool.MAX_PLAYERS_PER_ZONE; slot++)
            {
                Seed(slot, new EntityID(2000u + (uint)slot), Vector3.zero);
                _intents.Set(slot, FULL, FULL, 450);
            }
            _service.Run(1u, DT);

            Assert.That(() => _service.Run(2u, DT), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
