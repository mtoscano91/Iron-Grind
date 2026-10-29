using System;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace IronGrind.Tests.EditMode.MovementSystem
{
    /// <summary>
    /// EditMode unit tests for Movement Story 003 - F-MOV-1 position integration, F-MOV-2 effective speed through
    /// the Character Stats adapter, and the EC-MOV-19 speed resolver (AC-MOV-7, 8, 9, 10).
    /// </summary>
    [TestFixture]
    internal sealed class MovementSystem_Integration_Tests
    {
        private const float DT = ServerTickLoop.FIXED_DELTA_TIME;
        private const float TOLERANCE = 1e-5f;
        private const int ALLOC_ITERATIONS = 1000;
        private const string ERROR_PATTERN = @"\[Movement\] Invalid movement speed";

        private static readonly BuffID TestBuff = (BuffID)301u;
        private static readonly BuffID TestBuffB = (BuffID)302u;
        private static readonly ItemID TestItem = new ItemID(401u);
        private static readonly EntityID TestEntity = new EntityID(1001u);

        private IronGrind.CharacterStats.CharacterStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = CharacterStatsFixture.Create();
        }

        private void SetBaseSpeed(float value)
        {
            _stats.SetBaseStatFloat(TestEntity, StatID.MovementSpeed, value);
        }

        private float ReadAdapter()
        {
            var source = new CharacterStatsMovementSpeedSource(_stats);
            source.TryGetMovementSpeed(TestEntity, out float speed);
            return speed;
        }

        private sealed class FakeSource : IMovementSpeedSource
        {
            public bool Succeeds = true;
            public float Value = 5.0f;
            public bool Throws;

            public bool TryGetMovementSpeed(EntityID id, out float speed)
            {
                if (Throws) throw new InvalidOperationException("boom");
                speed = Value;
                return Succeeds;
            }
        }

        // ---- Integrator ----

        [TestCase(float.NaN, 0f)]
        [TestCase(0f, float.NaN)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(0f, float.NegativeInfinity)]
        public void MovementIntegrator_Integrate_NonFiniteDirection_ReturnsOldPosition(float x, float z)
        {
            Vector3 oldPos = new Vector3(10f, 1f, 5f);

            Vector3 result = MovementIntegrator.Integrate(oldPos, new Vector3(x, 0f, z), 5.0f, DT);

            Assert.AreEqual(oldPos, result);
        }

        [Test]
        public void MovementIntegrator_Integrate_ZeroSpeed_LeavesPositionUnchanged()
        {
            Vector3 oldPos = new Vector3(10f, 1f, 5f);

            Vector3 result = MovementIntegrator.Integrate(oldPos, Vector3.forward, 0f, DT);

            Assert.AreEqual(oldPos, result);
        }

        [Test]
        public void MovementIntegrator_Integrate_BaseSpeedAlongZ_AdvancesQuarterMeter()
        {
            Vector3 result = MovementIntegrator.Integrate(new Vector3(10f, 0f, 5f), new Vector3(0f, 0f, 1f), 5.0f, DT);

            Assert.AreEqual(10f, result.x, TOLERANCE);
            Assert.AreEqual(0f, result.y, TOLERANCE);
            Assert.AreEqual(5.25f, result.z, TOLERANCE, "AC-MOV-8: 5.0 x 0.05 = 0.25.");
        }

        [Test]
        public void MovementIntegrator_Integrate_NonUnitDirection_IsNormalized()
        {
            Vector3 result = MovementIntegrator.Integrate(new Vector3(10f, 0f, 5f), new Vector3(0f, 0f, 3f), 5.0f, DT);

            Assert.AreEqual(5.25f, result.z, TOLERANCE);
            Assert.AreEqual(10f, result.x, TOLERANCE);
        }

        [Test]
        public void MovementIntegrator_Integrate_DiagonalDirection_StepLengthEqualsSpeedTimesDt()
        {
            Vector3 start = new Vector3(1f, 2f, 3f);

            Vector3 result = MovementIntegrator.Integrate(start, new Vector3(1f, 0f, 1f), 5.0f, DT);

            Assert.AreEqual(0.25f, (result - start).magnitude, TOLERANCE);
        }

        [Test]
        public void MovementIntegrator_Integrate_DirectionWithY_KeepsOldY()
        {
            Vector3 result = MovementIntegrator.Integrate(new Vector3(0f, 2.5f, 0f), new Vector3(0f, 7f, 1f), 5.0f, DT);

            Assert.AreEqual(2.5f, result.y, 0f);
            Assert.AreEqual(0.25f, result.z, TOLERANCE);
        }

        [Test]
        public void MovementIntegrator_Integrate_ZeroDirection_ReturnsOldPositionExactly()
        {
            Vector3 old = new Vector3(10f, 1f, 5f);

            Vector3 result = MovementIntegrator.Integrate(old, Vector3.zero, 5.0f, DT);

            Assert.AreEqual(old, result);
        }

        [Test]
        public void MovementIntegrator_Integrate_OnlyYNonZero_ReturnsOldPosition()
        {
            Vector3 old = new Vector3(10f, 1f, 5f);

            Vector3 result = MovementIntegrator.Integrate(old, new Vector3(0f, 3f, 0f), 5.0f, DT);

            Assert.AreEqual(old, result);
        }

        [Test]
        public void MovementIntegrator_Integrate_SubEpsilonDirection_NormalizesToZeroAndDoesNotMove()
        {
            // 1e-9 passes the exact-zero guard but Vector3.normalized returns zero below 1e-5 magnitude.
            Vector3 old = new Vector3(10f, 0f, 5f);

            Vector3 result = MovementIntegrator.Integrate(old, new Vector3(1e-9f, 0f, 0f), 5.0f, DT);

            Assert.AreEqual(old, result);
        }

        [Test]
        public void MovementIntegrator_Integrate_MovingTickThenNoInput_DeltaIsZero()
        {
            Vector3 p0 = new Vector3(3f, 0f, 3f);
            Vector3 p1 = MovementIntegrator.Integrate(p0, new Vector3(1f, 0f, 0f), 5.0f, DT);

            Vector3 p2 = MovementIntegrator.Integrate(p1, Vector3.zero, 5.0f, DT);

            Assert.AreNotEqual(p0.x, p1.x);
            Assert.AreEqual(p1.x, p2.x, 0f, "AC-MOV-7");
            Assert.AreEqual(p1.z, p2.z, 0f, "AC-MOV-7");
        }

        [Test]
        public void MovementIntegrator_Integrate_ThousandCalls_DoesNotAllocate()
        {
            Vector3 pos = Vector3.zero;
            Vector3 dir = new Vector3(1f, 0f, 1f);
            MovementIntegrator.Integrate(pos, dir, 5.0f, DT);

            Assert.That(() =>
            {
                Vector3 p = pos;
                for (int i = 0; i < ALLOC_ITERATIONS; i++)
                    p = MovementIntegrator.Integrate(p, dir, 5.0f, DT);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        // ---- Real Character Stats (F-MOV-2, AC-MOV-8/9/10) ----

        [Test]
        public void MovementSpeed_CharacterStats_BaseSpeed_DeltaIsQuarterMeter()
        {
            SetBaseSpeed(5.0f);

            Vector3 result = MovementIntegrator.Integrate(Vector3.zero, new Vector3(0f, 0f, 1f), ReadAdapter(), DT);

            Assert.AreEqual(0.25f, result.z, TOLERANCE);
        }

        [Test]
        public void MovementSpeed_CharacterStats_DebuffBelowMinimum_ClampsToHalfAndDeltaIs0025()
        {
            SetBaseSpeed(5.0f);
            _stats.AddBuffModifier(TestEntity, StatID.MovementSpeed, new BuffModifierEntry(0f, -1.20f, 100, TestBuff));

            float speed = ReadAdapter();
            Vector3 result = MovementIntegrator.Integrate(Vector3.zero, new Vector3(0f, 0f, 1f), speed, DT);

            Assert.AreEqual(0.5f, speed, TOLERANCE);
            Assert.AreEqual(0.025f, result.z, TOLERANCE, "AC-MOV-9");
        }

        [Test]
        public void MovementSpeed_CharacterStats_BuffAboveMaximum_ClampsToTwentyAndDeltaIsOne()
        {
            SetBaseSpeed(5.0f);
            _stats.AddBuffModifier(TestEntity, StatID.MovementSpeed, new BuffModifierEntry(30f, 0f, 100, TestBuff));

            float speed = ReadAdapter();
            Vector3 result = MovementIntegrator.Integrate(Vector3.zero, new Vector3(0f, 0f, 1f), speed, DT);

            Assert.AreEqual(20.0f, speed, TOLERANCE);
            Assert.AreEqual(1.0f, result.z, TOLERANCE, "AC-MOV-10");
        }

        [Test]
        public void MovementSpeed_CharacterStats_FlatAndPercentBuff_Reads96AndDeltaIs048()
        {
            SetBaseSpeed(5.0f);
            _stats.AddBuffModifier(TestEntity, StatID.MovementSpeed, new BuffModifierEntry(3f, 0.20f, 100, TestBuff));

            float speed = ReadAdapter();
            Vector3 result = MovementIntegrator.Integrate(Vector3.zero, new Vector3(0f, 0f, 1f), speed, DT);

            Assert.AreEqual(9.6f, speed, TOLERANCE);
            Assert.AreEqual(0.48f, result.z, TOLERANCE);
        }

        [Test]
        public void MovementSpeed_CharacterStats_FiftyPercentSlow_Reads25AndDeltaIs0125()
        {
            SetBaseSpeed(5.0f);
            _stats.AddBuffModifier(TestEntity, StatID.MovementSpeed, new BuffModifierEntry(0f, -0.50f, 100, TestBuff));

            float speed = ReadAdapter();
            Vector3 result = MovementIntegrator.Integrate(Vector3.zero, new Vector3(0f, 0f, 1f), speed, DT);

            Assert.AreEqual(2.5f, speed, TOLERANCE);
            Assert.AreEqual(0.125f, result.z, TOLERANCE);
        }

        [Test]
        public void MovementSpeed_CharacterStats_TwoPercentLayers_Multiply()
        {
            SetBaseSpeed(5.0f);
            _stats.AddEquipmentModifier(TestEntity, StatID.MovementSpeed, new EquipmentModifierEntry(0f, 0.20f, TestItem));
            _stats.AddBuffModifier(TestEntity, StatID.MovementSpeed, new BuffModifierEntry(0f, 0.20f, 100, TestBuffB));

            Assert.AreEqual(5.0f * 1.44f, ReadAdapter(), TOLERANCE);
        }

        [Test]
        public void MovementSpeed_CharacterStats_TwoPercentBuffsSameLayer_Add()
        {
            SetBaseSpeed(5.0f);
            _stats.AddBuffModifier(TestEntity, StatID.MovementSpeed, new BuffModifierEntry(0f, 0.20f, 100, TestBuff));
            _stats.AddBuffModifier(TestEntity, StatID.MovementSpeed, new BuffModifierEntry(0f, 0.20f, 100, TestBuffB));

            Assert.AreEqual(5.0f * 1.40f, ReadAdapter(), TOLERANCE);
        }

        [Test]
        public void CharacterStatsMovementSpeedSource_FractionalSpeed_UsesFloatReadNotInt()
        {
            SetBaseSpeed(5.0f);
            _stats.AddBuffModifier(TestEntity, StatID.MovementSpeed, new BuffModifierEntry(3f, 0.20f, 100, TestBuff));
            var source = new CharacterStatsMovementSpeedSource(_stats);

            bool ok = source.TryGetMovementSpeed(TestEntity, out float speed);

            Assert.IsTrue(ok);
            Assert.AreEqual(_stats.GetEffectiveStatFloat(TestEntity, StatID.MovementSpeed), speed, 0f);
            Assert.AreEqual(9.6f, speed, TOLERANCE, "An int read would give 9.");
        }

        [Test]
        public void CharacterStatsMovementSpeedSource_NullStats_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterStatsMovementSpeedSource(null));
        }

        [Test]
        public void CharacterStatsMovementSpeedSource_SpeedChangesBetweenTicks_NextReadUsesNewValue()
        {
            SetBaseSpeed(5.0f);
            var source = new CharacterStatsMovementSpeedSource(_stats);
            source.TryGetMovementSpeed(TestEntity, out float tick1);

            SetBaseSpeed(8.0f);
            source.TryGetMovementSpeed(TestEntity, out float tick2);

            Assert.AreEqual(5.0f, tick1, TOLERANCE);
            Assert.AreEqual(8.0f, tick2, TOLERANCE, "Not cached across ticks.");
        }

        // ---- Resolver (EC-MOV-19) ----

        [Test]
        public void MovementSpeedResolver_ValidRead_ReturnsValueAndStoresLastValid()
        {
            var source = new FakeSource { Value = 5.0f };
            float last = 0f;

            bool ok = MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out float vEff);

            Assert.IsTrue(ok);
            Assert.AreEqual(5.0f, vEff, 0f);
            Assert.AreEqual(5.0f, last, 0f);
        }

        [Test]
        public void MovementSpeedResolver_BoundaryValues_AreAccepted()
        {
            var source = new FakeSource();
            float last = 0f;

            source.Value = 0.5f;
            Assert.IsTrue(MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out float low));
            source.Value = 20.0f;
            Assert.IsTrue(MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out float high));

            Assert.AreEqual(0.5f, low, 0f);
            Assert.AreEqual(20.0f, high, 0f);
        }

        [Test]
        public void MovementSpeedResolver_ReadFailsAfterValidSpeed_FallsBackToLastValidAndLogsOnce()
        {
            var source = new FakeSource { Value = 5.0f };
            float last = 0f;
            MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out _);
            MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out _);
            source.Succeeds = false;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));

            bool ok = MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out float vEff);

            Assert.IsTrue(ok);
            Assert.AreEqual(5.0f, vEff, 0f);
            Assert.AreEqual(5.0f, last, 0f, "lastValidSpeed unchanged after a failure.");
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(25.0f)]
        [TestCase(0.4f)]
        [TestCase(0f)]
        public void MovementSpeedResolver_InvalidValueAfterValidSpeed_FallsBackToLastValid(float bad)
        {
            var source = new FakeSource { Value = bad };
            float last = 5.0f;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));

            bool ok = MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out float vEff);

            Assert.IsTrue(ok);
            Assert.AreEqual(5.0f, vEff, 0f);
            Assert.AreEqual(5.0f, last, 0f);
        }

        [Test]
        public void MovementSpeedResolver_ThrowingSourceAfterValidSpeed_FallsBackToLastValid()
        {
            var source = new FakeSource { Throws = true };
            float last = 5.0f;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));

            bool ok = MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out float vEff);

            Assert.IsTrue(ok);
            Assert.AreEqual(5.0f, vEff, 0f);
            Assert.AreEqual(5.0f, last, 0f);
        }

        [Test]
        public void MovementSpeedResolver_FirstTickFailure_ReturnsFalseAndPositionUnchanged()
        {
            var source = new FakeSource { Succeeds = false };
            float last = 0f;
            Vector3 position = new Vector3(4f, 0f, 4f);
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));

            bool ok = MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out float vEff);
            if (ok)
                position = MovementIntegrator.Integrate(position, Vector3.forward, vEff, DT);

            Assert.IsFalse(ok);
            Assert.AreEqual(0f, last, 0f, "No BASE_MOVE_SPEED fallback.");
            Assert.AreEqual(new Vector3(4f, 0f, 4f), position);
        }

        [Test]
        public void MovementSpeedResolver_TwoFailingCalls_LogsOneErrorPerCall()
        {
            var source = new FakeSource { Succeeds = false };
            float last = 5.0f;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));

            MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out _);
            MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out _);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSpeedResolver_NullSource_IsCaughtLoggedAndFallsBack()
        {
            float last = 5.0f;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN + @".*NullReferenceException"));

            bool ok = MovementSpeedResolver.TryResolve(null, TestEntity, ref last, out float vEff);

            Assert.IsTrue(ok);
            Assert.AreEqual(5.0f, vEff, 0f);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void MovementSpeedResolver_InvalidLastValidSpeed_IsTreatedAsNone(float corruptLast)
        {
            var source = new FakeSource { Succeeds = false };
            float last = corruptLast;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));

            bool ok = MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out float vEff);

            Assert.IsFalse(ok);
            Assert.AreEqual(0f, vEff, 0f);
        }

        [Test]
        public void MovementSpeedResolver_FirstTickThrowingSource_ReturnsFalse()
        {
            var source = new FakeSource { Throws = true };
            float last = 0f;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));

            Assert.IsFalse(MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out _));
        }

        [Test]
        public void MovementSpeedResolver_UnknownEntityThroughRealCharacterStats_IsTreatedAsFailure()
        {
            var source = new CharacterStatsMovementSpeedSource(_stats);
            float last = 0f;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));

            bool ok = MovementSpeedResolver.TryResolve(source, new EntityID(9999u), ref last, out _);

            Assert.IsFalse(ok);
        }

        [Test]
        public void MovementSpeedResolver_UnknownEntityAfterValidSpeed_FallsBackToLastValid()
        {
            var source = new CharacterStatsMovementSpeedSource(_stats);
            float last = 5.0f;
            LogAssert.Expect(LogType.Error, new Regex(ERROR_PATTERN));

            bool ok = MovementSpeedResolver.TryResolve(source, new EntityID(9999u), ref last, out float vEff);

            Assert.IsTrue(ok);
            Assert.AreEqual(5.0f, vEff, 0f);
        }

        [Test]
        public void MovementSpeedResolver_SpeedChangesBetweenTicks_SecondTickUsesNewValue()
        {
            var source = new FakeSource { Value = 5.0f };
            float last = 0f;
            MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out float tick1);

            source.Value = 7.0f;
            MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out float tick2);

            Assert.AreEqual(5.0f, tick1, 0f);
            Assert.AreEqual(7.0f, tick2, 0f);
            Assert.AreEqual(7.0f, last, 0f);
        }

        [Test]
        public void MovementSpeedResolver_ThousandSuccessfulCalls_DoesNotAllocate()
        {
            var source = new FakeSource { Value = 5.0f };
            float last = 0f;
            MovementSpeedResolver.TryResolve(source, TestEntity, ref last, out _);

            Assert.That(() =>
            {
                float lastValid = 5.0f;
                for (int i = 0; i < ALLOC_ITERATIONS; i++)
                    MovementSpeedResolver.TryResolve(source, TestEntity, ref lastValid, out _);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
