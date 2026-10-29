using System;
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
    /// EditMode unit tests for Movement Story 007 - NavMesh stall recovery (EC-MOV-11, AC-MOV-24), the unavailable
    /// NavMesh defensive branch (EC-MOV-12, AC-MOV-25) and the F-MOV-6 encoding guard on the committed value.
    /// </summary>
    [TestFixture]
    internal sealed class MovementSystem_NavMeshFailure_Tests
    {
        private const float DT = ServerTickLoop.FIXED_DELTA_TIME;
        private const float TOLERANCE = 1e-4f;
        private const short FULL = 32767;

        private static readonly EntityID PlayerA = new EntityID(1001u);

        private FakeIntents _intents;
        private FakeSpeeds _speeds;
        private FakeSampler _sampler;
        private FakeSink _sink;
        private FakeIndex _index;
        private MovementService _service;

        [TearDown]
        public void TearDown()
        {
            LogAssert.NoUnexpectedReceived();
        }

        [SetUp]
        public void SetUp()
        {
            _intents = new FakeIntents();
            _speeds = new FakeSpeeds();
            _sampler = new FakeSampler();
            _sink = new FakeSink();
            _index = new FakeIndex();
            _service = new MovementService(_intents, _speeds, _sampler, _index, MovementTuningValues.Defaults, _sink);
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

            public void Clear(int slot) { _has[slot] = false; }

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
            public int Reads;
            public bool Fail;

            public bool TryGetMovementSpeed(EntityID id, out float speed)
            {
                Reads++;
                speed = 5.0f;
                return !Fail;
            }
        }

        // Answers by radius: 0.5 is the normal tick query, 5.0 the recovery query.
        private sealed class FakeSampler : INavMeshSampler
        {
            public bool HitsNear = true;
            public bool HitsFar = true;
            public Func<Vector3, bool> NearHitsWhen;
            public bool HasNearHit;
            public Vector3 NearHit;
            public Vector3 FarHit = new Vector3(3f, 0f, 3f);
            public int NearCalls;
            public int FarCalls;
            public float LastFarRadius;
            public Vector3 LastFarCandidate;

            public bool TrySample(Vector3 candidate, float radius, out Vector3 hit)
            {
                if (Mathf.Approximately(radius, MovementService.NAVMESH_RECOVERY_SEARCH_RADIUS))
                {
                    FarCalls++;
                    LastFarRadius = radius;
                    LastFarCandidate = candidate;
                    hit = FarHit;
                    return HitsFar;
                }

                NearCalls++;
                hit = HasNearHit ? NearHit : candidate;
                return NearHitsWhen != null ? NearHitsWhen(candidate) : HitsNear;
            }
        }

        private sealed class FakeSink : IMovementGeometryErrorSink
        {
            public int Calls;
            public EntityID LastEntity;
            public Vector3 LastPosition;

            public void ReportStuck(EntityID entity, Vector3 committedPosition)
            {
                Calls++;
                LastEntity = entity;
                LastPosition = committedPosition;
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

        // Player A at the origin with a sticky full-speed +X intent facing 450.
        private void SeedMoving(short facing = 450)
        {
            Seed(0, PlayerA, Vector3.zero, 0);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, facing);
        }

        private void RunTicks(uint first, uint last)
        {
            for (uint tick = first; tick <= last; tick++)
                _service.Run(tick, DT);
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

        private static void ExpectRecoveryWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"stall recovery for entity 1001"));
        }

        private static void ExpectOverflowWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"MovementEncodeOverflow"));
        }

        // ---- Construction ----

        [Test]
        public void MovementService_NullGeometryErrorSink_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new MovementService(_intents, _speeds, _sampler, _index, MovementTuningValues.Defaults, null));
        }

        [Test]
        public void MovementService_FourArgumentConstructor_UsesDefaultThresholdTen()
        {
            _service = new MovementService(_intents, _speeds, _sampler, _index);
            SeedMoving();
            _sampler.HitsNear = false;

            RunTicks(1u, 10u);
            Assert.AreEqual(0, _sampler.FarCalls);

            ExpectRecoveryWarning();
            _service.Run(11u, DT);
            Assert.AreEqual(1, _sampler.FarCalls);
        }

        // ---- AC-MOV-24 recovery ----

        [Test]
        public void MovementService_TenMisses_RecoversOnTickEleven_ThenMovesNormally()
        {
            // AC-MOV-24
            SeedMoving();
            _sampler.HitsNear = false;

            RunTicks(1u, 10u);
            Assert.AreEqual(Vector3.zero, Pos(PlayerA), "ticks 1-10 hold position");
            Assert.AreEqual(0, _sampler.FarCalls);

            ExpectRecoveryWarning();
            _service.Run(11u, DT);
            Assert.AreEqual(new Vector3(3f, 0f, 3f), Pos(PlayerA));
            Assert.AreEqual(1, _sampler.FarCalls);
            Assert.AreEqual(5.0f, _sampler.LastFarRadius, 0f);
            Assert.AreEqual(Vector3.zero, _sampler.LastFarCandidate, "recovery samples the committed position");
            Assert.AreEqual(10, _sampler.NearCalls, "recovery tick makes no radius-0.5 sample");
            Assert.AreEqual(10, _speeds.Reads, "recovery tick reads no speed");
            Assert.AreEqual(0, _sink.Calls);

            _sampler.HitsNear = true;
            _service.Run(12u, DT);
            AssertNear(new Vector3(3.25f, 0f, 3f), Pos(PlayerA));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_RecoveryHit_CommittedAsIsIncludingY()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            _sampler.FarHit = new Vector3(3f, 2.5f, 3f);
            RunTicks(1u, 10u);

            ExpectRecoveryWarning();
            _service.Run(11u, DT);

            Assert.AreEqual(new Vector3(3f, 2.5f, 3f), Pos(PlayerA));
        }

        [Test]
        public void MovementService_RecoveryWarning_NamesEntityAndBothPositions()
        {
            Seed(0, PlayerA, new Vector3(1f, 0f, 2f));
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 0);
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);

            LogAssert.Expect(LogType.Warning, new Regex(@"entity 1001.*\(1\.00, 0\.00, 2\.00\).*\(3\.00, 0\.00, 3\.00\)"));
            _service.Run(11u, DT);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_NineMissesThenHit_NoRecoveryOnTickEleven()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            RunTicks(1u, 9u);
            _sampler.HitsNear = true;
            _service.Run(10u, DT);

            _sampler.HitsNear = false;
            _service.Run(11u, DT);

            Assert.AreEqual(0, _sampler.FarCalls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_ThresholdTen_RecoveryNotOnTenNorTwelve()
        {
            SeedMoving();
            _sampler.HitsNear = false;

            RunTicks(1u, 10u);
            Assert.AreEqual(0, _sampler.FarCalls, "not on tick 10");

            ExpectRecoveryWarning();
            _service.Run(11u, DT);
            Assert.AreEqual(1, _sampler.FarCalls, "on tick 11");

            _service.Run(12u, DT);
            Assert.AreEqual(1, _sampler.FarCalls, "not again on tick 12");
        }

        [Test]
        public void MovementService_ThresholdFive_RecoveryOnTickSix()
        {
            _service = new MovementService(_intents, _speeds, _sampler, _index,
                MovementTuningValues.Create(80f, 0.15f, 5.0f, 3, 0.25f, 5 /* navMeshStallRecoveryTicks */), _sink);
            SeedMoving();
            _sampler.HitsNear = false;

            RunTicks(1u, 5u);
            Assert.AreEqual(0, _sampler.FarCalls);

            ExpectRecoveryWarning();
            _service.Run(6u, DT);
            Assert.AreEqual(1, _sampler.FarCalls);
            Assert.AreEqual(new Vector3(3f, 0f, 3f), Pos(PlayerA));
        }

        [Test]
        public void MovementService_ZeroInputTicksInsideStall_DoNotChangeCounter()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            RunTicks(1u, 5u);

            _intents.Set(0, 0, 0, 450);
            RunTicks(6u, 8u);
            _intents.Set(0, FULL, 0, 450);
            RunTicks(9u, 13u);
            Assert.AreEqual(0, _sampler.FarCalls, "ten misses not yet reached before tick 14");

            ExpectRecoveryWarning();
            _service.Run(14u, DT);
            Assert.AreEqual(1, _sampler.FarCalls);
        }

        [Test]
        public void MovementService_RecoveryTickWithZeroInput_StillRecovers()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);

            _intents.Set(0, 0, 0, 450);
            ExpectRecoveryWarning();
            _service.Run(11u, DT);

            Assert.AreEqual(new Vector3(3f, 0f, 3f), Pos(PlayerA));
        }

        [Test]
        public void MovementService_RecoveryTickWithNoIntent_StillRecovers()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);

            _intents.Sticky = false;
            _intents.Clear(0);
            ExpectRecoveryWarning();
            _service.Run(11u, DT);

            Assert.AreEqual(new Vector3(3f, 0f, 3f), Pos(PlayerA));
        }

        [Test]
        public void MovementService_Prohibited_NoRecoveryUntilCleared()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);

            Assert.IsTrue(_service.SetProhibited(PlayerA, true));
            RunTicks(11u, 13u);
            Assert.AreEqual(0, _sampler.FarCalls);
            Assert.AreEqual(Vector3.zero, Pos(PlayerA));

            _service.SetProhibited(PlayerA, false);
            ExpectRecoveryWarning();
            _service.Run(14u, DT);

            Assert.AreEqual(1, _sampler.FarCalls);
            Assert.AreEqual(new Vector3(3f, 0f, 3f), Pos(PlayerA));
        }

        [Test]
        public void MovementService_RecoveryTick_AppliesIntentFacing()
        {
            SeedMoving(450);
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);

            _intents.Set(0, FULL, 0, 900);
            ExpectRecoveryWarning();
            _service.Run(11u, DT);

            Assert.AreEqual((short)900, Facing(PlayerA));
        }

        [Test]
        public void MovementService_RecoveryTick_NoIntent_FacingUnchanged()
        {
            SeedMoving(450);
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);

            _intents.Sticky = false;
            _intents.Clear(0);
            ExpectRecoveryWarning();
            _service.Run(11u, DT);

            Assert.AreEqual((short)450, Facing(PlayerA), "the teleport does not change facing");
        }

        [Test]
        public void MovementService_RecoveryTick_AppliesOverrideFacingAndConsumesIt()
        {
            SeedMoving(450);
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);

            _service.InjectFacingOverride(PlayerA, Vector3.right);
            ExpectRecoveryWarning();
            _service.Run(11u, DT);
            Assert.AreEqual((short)900, Facing(PlayerA));

            _service.Run(12u, DT);
            Assert.AreEqual((short)450, Facing(PlayerA), "override lasted one tick");
        }

        // ---- Recovery miss ----

        [Test]
        public void MovementService_RecoveryMiss_ReportsStuckOnceAndStopsRecovering()
        {
            Seed(0, PlayerA, new Vector3(7f, 0f, 8f));
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 0);
            _sampler.HitsNear = false;
            _sampler.HitsFar = false;
            RunTicks(1u, 10u);

            _service.Run(11u, DT);
            Assert.AreEqual(1, _sink.Calls);
            Assert.AreEqual(PlayerA, _sink.LastEntity);
            Assert.AreEqual(new Vector3(7f, 0f, 8f), _sink.LastPosition);
            Assert.AreEqual(new Vector3(7f, 0f, 8f), Pos(PlayerA));

            int nearBefore = _sampler.NearCalls;
            RunTicks(12u, 20u);
            Assert.AreEqual(1, _sink.Calls, "no second report");
            Assert.AreEqual(1, _sampler.FarCalls, "no second radius-5.0 query");
            Assert.AreEqual(nearBefore + 9, _sampler.NearCalls, "radius-0.5 sampling continues on moving ticks");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_RecoveryMiss_HitClearsStall_NewStallRecoversAgain()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            _sampler.HitsFar = false;
            RunTicks(1u, 11u);
            Assert.AreEqual(1, _sink.Calls);

            _sampler.HitsNear = true;
            _service.Run(12u, DT);
            Assert.IsTrue(Pos(PlayerA).x > 0f, "normal hit commits");

            _sampler.HitsNear = false;
            _sampler.HitsFar = true;
            RunTicks(13u, 22u);
            Assert.AreEqual(1, _sampler.FarCalls, "ten new misses, recovery not yet");

            ExpectRecoveryWarning();
            _service.Run(23u, DT);

            Assert.AreEqual(2, _sampler.FarCalls);
            Assert.AreEqual(new Vector3(3f, 0f, 3f), Pos(PlayerA));
            Assert.AreEqual(1, _sink.Calls);
        }

        // ---- AC-MOV-25 unavailable NavMesh ----

        [Test]
        public void MovementService_NavMeshUnavailable_TwentyTicksNothingAppliedOneError()
        {
            // AC-MOV-25
            Seed(0, PlayerA, new Vector3(1f, 0f, 2f), 100);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 450);
            _service.SetNavMeshAvailable(false);
            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));

            for (uint tick = 1u; tick <= 20u; tick++)
            {
                if (tick == 5u)
                    _service.InjectFacingOverride(PlayerA, Vector3.forward);
                _service.Run(tick, DT);
            }

            Assert.AreEqual(new Vector3(1f, 0f, 2f), Pos(PlayerA));
            Assert.AreEqual((short)100, Facing(PlayerA));
            Assert.AreEqual(0, _sampler.NearCalls + _sampler.FarCalls);
            Assert.AreEqual(0, _speeds.Reads);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_NavMeshUnavailable_ReavailableThenUnavailableAgain_LogsOneMoreError()
        {
            SeedMoving();
            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));
            _service.SetNavMeshAvailable(false);
            RunTicks(1u, 3u);

            _service.SetNavMeshAvailable(true);
            _service.Run(4u, DT);
            AssertNear(new Vector3(0.25f, 0f, 0f), Pos(PlayerA));

            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));
            _service.SetNavMeshAvailable(false);
            RunTicks(5u, 7u);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_NavMeshUnavailable_SettingFalseTwice_DoesNotRearmLog()
        {
            SeedMoving();
            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));
            _service.SetNavMeshAvailable(false);
            _service.Run(1u, DT);
            _service.SetNavMeshAvailable(false);
            _service.Run(2u, DT);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_NavMeshUnavailable_IntentAndOverrideAreConsumed()
        {
            Seed(0, PlayerA, Vector3.zero, 100);
            _intents.Set(0, FULL, 0, 450);
            _service.InjectFacingOverride(PlayerA, Vector3.forward);
            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));
            _service.SetNavMeshAvailable(false);
            _service.Run(1u, DT);

            _service.SetNavMeshAvailable(true);
            _service.Run(2u, DT);

            Assert.AreEqual(Vector3.zero, Pos(PlayerA), "neither the intent nor the override is replayed");
            Assert.AreEqual((short)100, Facing(PlayerA));
            Assert.AreEqual(0, _sampler.NearCalls);
        }

        [Test]
        public void MovementService_NavMeshUnavailable_DoesNotChangeStallCounter()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            RunTicks(1u, 5u);

            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));
            _service.SetNavMeshAvailable(false);
            RunTicks(6u, 15u);
            _service.SetNavMeshAvailable(true);

            RunTicks(16u, 20u);
            Assert.AreEqual(0, _sampler.FarCalls, "five misses before plus five after is ten, recovery next tick");

            ExpectRecoveryWarning();
            _service.Run(21u, DT);
            Assert.AreEqual(1, _sampler.FarCalls);
        }

        [Test]
        public void MovementService_NavMeshUnavailable_NoRecoveryEvenAtThreshold()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);

            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));
            _service.SetNavMeshAvailable(false);
            RunTicks(11u, 13u);

            Assert.AreEqual(0, _sampler.FarCalls);
        }

        // ---- F-MOV-6 encoding guard ----

        [Test]
        public void MovementService_NormalHitOverEncodableRange_NotCommittedWarnsOnce()
        {
            SeedMoving();
            _sampler.HasNearHit = true;
            _sampler.NearHit = new Vector3(327.70f, 0f, 0f);

            ExpectOverflowWarning();
            RunTicks(1u, 3u);

            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_NormalHitAtEncodableLimit_Commits()
        {
            SeedMoving();
            _sampler.HasNearHit = true;
            _sampler.NearHit = new Vector3(327.67f, 0f, 0f);

            _service.Run(1u, DT);

            Assert.AreEqual(327.67f, Pos(PlayerA).x, TOLERANCE);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_GuardRejectedHits_CountAsMissesAndTriggerRecovery()
        {
            SeedMoving();
            _sampler.HasNearHit = true;
            _sampler.NearHit = new Vector3(0f, 0f, -400f);

            ExpectOverflowWarning();
            RunTicks(1u, 10u);
            Assert.AreEqual(0, _sampler.FarCalls);

            ExpectRecoveryWarning();
            _service.Run(11u, DT);

            Assert.AreEqual(1, _sampler.FarCalls);
            Assert.AreEqual(new Vector3(3f, 0f, 3f), Pos(PlayerA));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_OverflowWarning_RearmsAfterCommittedHit()
        {
            SeedMoving();
            _sampler.HasNearHit = true;
            _sampler.NearHit = new Vector3(400f, 0f, 0f);
            ExpectOverflowWarning();
            _service.Run(1u, DT);

            _sampler.NearHit = new Vector3(1f, 0f, 0f);
            _service.Run(2u, DT);

            _sampler.NearHit = new Vector3(400f, 0f, 0f);
            ExpectOverflowWarning();
            _service.Run(3u, DT);

            Assert.AreEqual(new Vector3(1f, 0f, 0f), Pos(PlayerA));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementService_RecoveryHitOverEncodableRange_NotCommittedAndReportsStuck()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            _sampler.FarHit = new Vector3(0f, 0f, -327.70f);
            RunTicks(1u, 10u);

            ExpectOverflowWarning();
            _service.Run(11u, DT);

            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
            Assert.AreEqual(1, _sink.Calls);
            Assert.AreEqual(Vector3.zero, _sink.LastPosition);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Review additions: construction ----

        [Test]
        public void MovementService_DefaultTuningStruct_ThrowsArgumentOutOfRange()
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MovementService(_intents, _speeds, _sampler, _index, default(MovementTuningValues), _sink));
            Assert.AreEqual("tuning", ex.ParamName);
        }

        [Test]
        public void MovementService_NullArguments_ThrowInParameterOrder()
        {
            var first = Assert.Throws<ArgumentNullException>(() =>
                new MovementService(null, null, null, null, MovementTuningValues.Defaults, null));
            Assert.AreEqual("intents", first.ParamName);

            var last = Assert.Throws<ArgumentNullException>(() =>
                new MovementService(_intents, _speeds, _sampler, _index, MovementTuningValues.Defaults, null));
            Assert.AreEqual("geometryErrors", last.ParamName);
        }

        // ---- Review additions: ticks that never sample leave the counter alone ----

        [Test]
        public void MovementService_FailedSpeedReadTicks_DoNotChangeCounter()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            _speeds.Fail = true;
            LogAssert.Expect(LogType.Error, new Regex(@"read failed; no valid speed yet, skipping movement"));
            RunTicks(1u, 3u);
            Assert.AreEqual(0, _sampler.NearCalls, "failed reads never reach the sample");

            _speeds.Fail = false;
            RunTicks(4u, 13u);
            Assert.AreEqual(0, _sampler.FarCalls, "three failing ticks did not count");

            ExpectRecoveryWarning();
            _service.Run(14u, DT);
            Assert.AreEqual(1, _sampler.FarCalls);
        }

        [Test]
        public void MovementService_NonFiniteCandidateTicks_DoNotChangeCounterOrSample()
        {
            Seed(0, PlayerA, new Vector3(float.PositiveInfinity, 0f, 0f));
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 0);

            RunTicks(1u, 12u);

            Assert.AreEqual(0, _sampler.NearCalls);
            Assert.AreEqual(0, _sampler.FarCalls, "non-finite candidates never count toward the stall");
        }

        [Test]
        public void MovementService_ProhibitedTicksBelowThreshold_DoNotCount()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            RunTicks(1u, 5u);
            _service.SetProhibited(PlayerA, true);
            RunTicks(6u, 8u);
            _service.SetProhibited(PlayerA, false);
            RunTicks(9u, 13u);
            Assert.AreEqual(0, _sampler.FarCalls);

            ExpectRecoveryWarning();
            _service.Run(14u, DT);
            Assert.AreEqual(1, _sampler.FarCalls);
        }

        [Test]
        public void MovementService_RecoveryHit_ResetsCounter()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);
            ExpectRecoveryWarning();
            _service.Run(11u, DT);

            RunTicks(12u, 21u);
            Assert.AreEqual(1, _sampler.FarCalls, "ten fresh misses needed after the recovery");

            ExpectRecoveryWarning();
            _service.Run(22u, DT);
            Assert.AreEqual(2, _sampler.FarCalls);
        }

        // ---- Review additions: slots are independent ----

        private static readonly EntityID PlayerB = new EntityID(1002u);

        private void SeedTwoSlots()
        {
            Seed(0, PlayerA, Vector3.zero);
            Seed(1, PlayerB, new Vector3(100f, 0f, 0f));
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 0);
            _intents.Set(1, FULL, 0, 0);
            _sampler.NearHitsWhen = candidate => candidate.x > 50f;
        }

        [Test]
        public void MovementService_TwoSlots_StallIndependently_OnlyStalledSlotRecovers()
        {
            SeedTwoSlots();
            RunTicks(1u, 10u);
            Assert.AreEqual(0, _sampler.FarCalls);

            ExpectRecoveryWarning();
            _service.Run(11u, DT);

            Assert.AreEqual(1, _sampler.FarCalls);
            Assert.AreEqual(new Vector3(3f, 0f, 3f), Pos(PlayerA));
            Assert.AreEqual(102.75f, Pos(PlayerB).x, TOLERANCE, "slot 1 keeps moving normally, 11 ticks of 0.25");
        }

        [Test]
        public void MovementService_TwoSlots_RecoveryMissReportsOnlyStalledEntity()
        {
            SeedTwoSlots();
            _sampler.HitsFar = false;

            RunTicks(1u, 12u);

            Assert.AreEqual(1, _sink.Calls);
            Assert.AreEqual(PlayerA, _sink.LastEntity);
        }

        [Test]
        public void MovementService_TwoSlots_EachEntityGetsItsOwnOverflowWarning()
        {
            Seed(0, PlayerA, Vector3.zero);
            Seed(1, PlayerB, Vector3.zero);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 0);
            _intents.Set(1, FULL, 0, 0);
            _sampler.HasNearHit = true;
            _sampler.NearHit = new Vector3(400f, 0f, 0f);

            LogAssert.Expect(LogType.Warning, new Regex(@"MovementEncodeOverflow: entity 1001"));
            LogAssert.Expect(LogType.Warning, new Regex(@"MovementEncodeOverflow: entity 1002"));
            RunTicks(1u, 3u);
        }

        [Test]
        public void MovementService_TwoSlots_NavMeshUnavailable_OneErrorTotal()
        {
            SeedTwoSlots();
            _service.SetNavMeshAvailable(false);

            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));
            RunTicks(1u, 3u);
        }

        // ---- Review additions: edge cases ----

        [Test]
        public void MovementService_NavMeshUnavailableWhileProhibited_OneErrorNothingMoves()
        {
            Seed(0, PlayerA, Vector3.zero, 100);
            _intents.Sticky = true;
            _intents.Set(0, FULL, 0, 450);
            _service.SetProhibited(PlayerA, true);
            _service.SetNavMeshAvailable(false);

            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));
            RunTicks(1u, 3u);

            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
            Assert.AreEqual((short)100, Facing(PlayerA));
        }

        [Test]
        public void MovementService_BecomingAvailableAtThreshold_RecoversOnNextSteppedTick()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);
            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));
            _service.SetNavMeshAvailable(false);
            RunTicks(11u, 12u);
            Assert.AreEqual(0, _sampler.FarCalls);

            _service.SetNavMeshAvailable(true);
            ExpectRecoveryWarning();
            _service.Run(13u, DT);

            Assert.AreEqual(1, _sampler.FarCalls);
            Assert.AreEqual(new Vector3(3f, 0f, 3f), Pos(PlayerA));
        }

        [Test]
        public void MovementService_OverflowWarning_OneAcrossNormalHitAndRecoveryHit()
        {
            SeedMoving();
            _sampler.HasNearHit = true;
            _sampler.NearHit = new Vector3(400f, 0f, 0f);
            _sampler.FarHit = new Vector3(400f, 0f, 0f);
            ExpectOverflowWarning();
            RunTicks(1u, 10u);

            _service.Run(11u, DT);

            Assert.AreEqual(1, _sampler.FarCalls);
            Assert.AreEqual(1, _sink.Calls);
            Assert.AreEqual(Vector3.zero, Pos(PlayerA));
        }

        [Test]
        public void MovementService_FourArgumentConstructor_RecoveryMiss_LogsStuckErrorForEntity()
        {
            _service = new MovementService(_intents, _speeds, _sampler, _index);
            SeedMoving();
            _sampler.HitsNear = false;
            _sampler.HitsFar = false;
            RunTicks(1u, 10u);

            LogAssert.Expect(LogType.Error, new Regex(@"Entity 1001 is stuck off the NavMesh"));
            _service.Run(11u, DT);
        }

        // ---- Review additions: allocation ----

        private void SeedFifty()
        {
            _intents.Sticky = true;
            for (int slot = 0; slot < ZoneSlotPool.MAX_PLAYERS_PER_ZONE; slot++)
            {
                Seed(slot, new EntityID(2000u + (uint)slot), Vector3.zero);
                _intents.Set(slot, FULL, FULL, 450);
            }
        }

        [Test]
        public void MovementService_Run_FiftyPlayersNavMeshUnavailableSteadyState_DoesNotAllocate()
        {
            SeedFifty();
            _service.SetNavMeshAvailable(false);
            LogAssert.Expect(LogType.Error, new Regex(@"NavMesh unavailable"));
            _service.Run(1u, DT);

            Assert.That(() => _service.Run(2u, DT), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void MovementService_Run_FiftyPlayersTickAfterSuccessfulRecovery_DoesNotAllocate()
        {
            SeedFifty();
            _sampler.HitsNear = false;
            RunTicks(1u, 10u);
            for (int i = 0; i < ZoneSlotPool.MAX_PLAYERS_PER_ZONE; i++)
                LogAssert.Expect(LogType.Warning, new Regex(@"stall recovery for entity 20\d\d"));
            _service.Run(11u, DT);
            _sampler.HitsNear = true;

            Assert.That(() => _service.Run(12u, DT), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        // ---- Seeding ----

        [Test]
        public void MovementService_SeedForTesting_ResetsStallCounterAndReportedFlag()
        {
            SeedMoving();
            _sampler.HitsNear = false;
            _sampler.HitsFar = false;
            RunTicks(1u, 11u);
            Assert.AreEqual(1, _sink.Calls);

            SeedMoving();
            RunTicks(12u, 21u);
            Assert.AreEqual(1, _sampler.FarCalls, "counter restarted at 0 after the reseed");

            _service.Run(22u, DT);
            Assert.AreEqual(2, _sampler.FarCalls, "reported flag cleared: a new stall recovers again");
            Assert.AreEqual(2, _sink.Calls);
        }

        // ---- Allocation ----

        [Test]
        public void MovementService_Run_FiftyPlayersStallingThroughRecoveryMiss_DoesNotAllocate()
        {
            _intents.Sticky = true;
            _sampler.HitsNear = false;
            _sampler.HitsFar = false;
            for (int slot = 0; slot < ZoneSlotPool.MAX_PLAYERS_PER_ZONE; slot++)
            {
                Seed(slot, new EntityID(2000u + (uint)slot), Vector3.zero);
                _intents.Set(slot, FULL, FULL, 450);
            }

            RunTicks(1u, 10u);

            // Tick 11: every slot takes the recovery-miss path and reports stuck.
            Assert.That(() => _service.Run(11u, DT), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.AreEqual(ZoneSlotPool.MAX_PLAYERS_PER_ZONE, _sink.Calls);

            // Tick 12: reported slots keep sampling at 0.5 and missing.
            Assert.That(() => _service.Run(12u, DT), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
