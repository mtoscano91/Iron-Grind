using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.MovementSystem
{
    /// <summary>
    /// EditMode unit tests for Movement Story 001 - tuning knobs TK-MOV-1 to TK-MOV-6 with safe-range clamping, the
    /// base speed parity with Character Stats, the AC-MOV-11 position round trip and the F-MOV-6 encoding guard.
    /// </summary>
    [TestFixture]
    internal sealed class MovementSystem_Tuning_Tests
    {
        // AC-MOV-11 sweep: -327.0 + i * 0.654 for i = 0..999 stays inside +-327.0.
        private const int SWEEP_COUNT = 1000;
        private const float SWEEP_START = -327.0f;
        private const float SWEEP_STEP = 0.654f;
        private const float MAX_ROUND_TRIP_ERROR = 0.005f;
        // A float sample such as 1.005f is stored a few ULPs off the decimal value, so the half-LSB bound is
        // checked with a small representation allowance (float ULP at 327 is about 3e-5).
        private const float FLOAT_REPRESENTATION_ALLOWANCE = 1e-4f;
        private const float OVER_ENCODING_LIMIT = 327.68f;

        private MovementTuning _asset;

        [TearDown]
        public void TearDown()
        {
            if (_asset != null)
            {
                Object.DestroyImmediate(_asset);
                _asset = null;
            }
        }

        // One knob replaced, the other five at their defaults. Index order: 0 radius, 1 dead zone, 2 base speed,
        // 3 stale tolerance, 4 camera sensitivity, 5 stall ticks.
        private static MovementTuningValues CreateWithKnob(int knob, float value)
        {
            MovementTuningValues d = MovementTuningValues.Defaults;
            return MovementTuningValues.Create(
                knob == 0 ? value : d.JoystickRadiusPx,
                knob == 1 ? value : d.DeadZoneNormalized,
                knob == 2 ? value : d.BaseMoveSpeed,
                knob == 3 ? (int)value : d.StaleTickTolerance,
                knob == 4 ? value : d.CameraDragSensitivity,
                knob == 5 ? (int)value : d.NavMeshStallRecoveryTicks);
        }

        private static float ReadKnob(MovementTuningValues values, int knob)
        {
            switch (knob)
            {
                case 0: return values.JoystickRadiusPx;
                case 1: return values.DeadZoneNormalized;
                case 2: return values.BaseMoveSpeed;
                case 3: return values.StaleTickTolerance;
                case 4: return values.CameraDragSensitivity;
                default: return values.NavMeshStallRecoveryTicks;
            }
        }

        // Distinct values per axis (a, -a, a / 2), each compared with its own input, so a swapped or shifted axis in
        // the codec fails. Returns the largest per-axis error.
        private static float RoundTrip(float axis)
        {
            var input = new Vector3(axis, -axis, axis * 0.5f);
            Span<byte> buffer = stackalloc byte[WireFixedPointCodec.PositionWireSize];
            WireFixedPointCodec.EncodePosition(buffer, input);
            Vector3 decoded = WireFixedPointCodec.DecodePosition(buffer);
            float error = Mathf.Max(Mathf.Abs(decoded.x - input.x), Mathf.Abs(decoded.y - input.y));
            return Mathf.Max(error, Mathf.Abs(decoded.z - input.z));
        }

        private static string Invariant(float value)
        {
            return Regex.Escape(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AssertSameKnobs(MovementTuningValues expected, MovementTuningValues actual)
        {
            Assert.AreEqual(expected.JoystickRadiusPx, actual.JoystickRadiusPx, "TK-MOV-1");
            Assert.AreEqual(expected.DeadZoneNormalized, actual.DeadZoneNormalized, "TK-MOV-2");
            Assert.AreEqual(expected.BaseMoveSpeed, actual.BaseMoveSpeed, "TK-MOV-3");
            Assert.AreEqual(expected.StaleTickTolerance, actual.StaleTickTolerance, "TK-MOV-4");
            Assert.AreEqual(expected.CameraDragSensitivity, actual.CameraDragSensitivity, "TK-MOV-5");
            Assert.AreEqual(expected.NavMeshStallRecoveryTicks, actual.NavMeshStallRecoveryTicks, "TK-MOV-6");
        }

        private static void AssertAssetUnchanged(MovementTuning asset, float radius, float deadZone, float speed,
            int stale, float sensitivity, int stall)
        {
            Assert.AreEqual(radius, asset.JoystickRadiusPx, "the authored TK-MOV-1 is not changed");
            Assert.AreEqual(deadZone, asset.DeadZoneNormalized, "the authored TK-MOV-2 is not changed");
            Assert.AreEqual(speed, asset.BaseMoveSpeed, "the authored TK-MOV-3 is not changed");
            Assert.AreEqual(stale, asset.StaleTickTolerance, "the authored TK-MOV-4 is not changed");
            Assert.AreEqual(sensitivity, asset.CameraDragSensitivity, "the authored TK-MOV-5 is not changed");
            Assert.AreEqual(stall, asset.NavMeshStallRecoveryTicks, "the authored TK-MOV-6 is not changed");
        }

        private static void InvokeOnValidate(MovementTuning asset)
        {
            MethodInfo onValidate = typeof(MovementTuning).GetMethod("OnValidate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(onValidate, "MovementTuning must keep its OnValidate editor feedback");
            onValidate.Invoke(asset, null);
        }

        // ---------------------------------------------------------------------------------------
        // Defaults and parity
        // ---------------------------------------------------------------------------------------

        [Test]
        public void MovementSystem_Tuning_Defaults_MatchGddTuningKnobs()
        {
            MovementTuningValues d = MovementTuningValues.Defaults;

            Assert.AreEqual(80f, d.JoystickRadiusPx, "TK-MOV-1");
            Assert.AreEqual(0.15f, d.DeadZoneNormalized, "TK-MOV-2");
            Assert.AreEqual(5.0f, d.BaseMoveSpeed, "TK-MOV-3 (registry BASE_MOVE_SPEED, revised 2026-10-29)");
            Assert.AreEqual(3, d.StaleTickTolerance, "TK-MOV-4");
            Assert.AreEqual(0.25f, d.CameraDragSensitivity, "TK-MOV-5");
            Assert.AreEqual(10, d.NavMeshStallRecoveryTicks, "TK-MOV-6");
        }

        [Test]
        public void MovementSystem_Tuning_FreshAsset_MapsToDefaultsWithoutWarnings()
        {
            _asset = ScriptableObject.CreateInstance<MovementTuning>();

            MovementTuningValues values = MovementTuningValues.FromAsset(_asset);

            AssertSameKnobs(MovementTuningValues.Defaults, values);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSystem_Tuning_BaseMoveSpeed_EqualsCharacterStatsStartingSpeed()
        {
            Assert.AreEqual(CharacterStubDefaults.MOVEMENT_SPEED, MovementTuningValues.Defaults.BaseMoveSpeed,
                "BASE_MOVE_SPEED is a reference value; it must equal the Character Stats starting MovementSpeed");
        }

        [Test]
        public void MovementSystem_Tuning_FromAssetNull_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => MovementTuningValues.FromAsset(null));
        }

        // ---------------------------------------------------------------------------------------
        // Safe ranges
        // ---------------------------------------------------------------------------------------

        [TestCase(0, 60f)]
        [TestCase(0, 120f)]
        [TestCase(1, 0.05f)]
        [TestCase(1, 0.30f)]
        [TestCase(2, 4.0f)]
        [TestCase(2, 10.0f)]
        [TestCase(3, 2f)]
        [TestCase(3, 5f)]
        [TestCase(4, 0.10f)]
        [TestCase(4, 0.50f)]
        [TestCase(5, 5f)]
        [TestCase(5, 30f)]
        public void MovementSystem_Tuning_KnobAtRangeBound_KeptWithoutWarning(int knob, float bound)
        {
            MovementTuningValues values = CreateWithKnob(knob, bound);

            Assert.AreEqual(bound, ReadKnob(values, knob));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0, "JOYSTICK_RADIUS_PX", 50f, 60f)]
        [TestCase(0, "JOYSTICK_RADIUS_PX", 121f, 120f)]
        [TestCase(1, "DEAD_ZONE_NORMALIZED", 0.04f, 0.05f)]
        [TestCase(1, "DEAD_ZONE_NORMALIZED", 0.40f, 0.30f)]
        [TestCase(2, "BASE_MOVE_SPEED", 3.9f, 4.0f)]
        [TestCase(2, "BASE_MOVE_SPEED", 10.1f, 10.0f)]
        [TestCase(3, "STALE_TICK_TOLERANCE", 1f, 2f)]
        [TestCase(3, "STALE_TICK_TOLERANCE", 6f, 5f)]
        [TestCase(4, "CAMERA_DRAG_SENSITIVITY", 0.09f, 0.10f)]
        [TestCase(4, "CAMERA_DRAG_SENSITIVITY", 0.51f, 0.50f)]
        [TestCase(5, "NAVMESH_STALL_RECOVERY_TICKS", 4f, 5f)]
        [TestCase(5, "NAVMESH_STALL_RECOVERY_TICKS", 31f, 30f)]
        [TestCase(0, "JOYSTICK_RADIUS_PX", float.PositiveInfinity, 120f)]
        [TestCase(0, "JOYSTICK_RADIUS_PX", float.NegativeInfinity, 60f)]
        [TestCase(4, "CAMERA_DRAG_SENSITIVITY", float.PositiveInfinity, 0.50f)]
        public void MovementSystem_Tuning_KnobOutsideRange_ClampedWithOneWarning(int knob, string name, float given, float expected)
        {
            // The warning names the knob, the given value and the value used.
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[MovementTuning\] " + name + " " + Invariant(given)
                + @" is outside \[.*\]; using " + Invariant(expected) + "$"));

            MovementTuningValues values = CreateWithKnob(knob, given);

            Assert.AreEqual(expected, ReadKnob(values, knob));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSystem_Tuning_IntKnobExtremes_ClampedWithoutOverflow()
        {
            // Direct Create: CreateWithKnob's float-to-int cast cannot carry int.MaxValue / int.MinValue.
            LogAssert.Expect(LogType.Warning, new Regex(@"STALE_TICK_TOLERANCE 2147483647 .*using 5$"));
            LogAssert.Expect(LogType.Warning, new Regex(@"NAVMESH_STALL_RECOVERY_TICKS -2147483648 .*using 5$"));

            MovementTuningValues values = MovementTuningValues.Create(80f, 0.15f, 5.0f, int.MaxValue, 0.25f, int.MinValue);

            Assert.AreEqual(5, values.StaleTickTolerance);
            Assert.AreEqual(5, values.NavMeshStallRecoveryTicks);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSystem_Tuning_TwoBadKnobs_LogTwoWarnings()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[MovementTuning\] JOYSTICK_RADIUS_PX 50 "));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[MovementTuning\] STALE_TICK_TOLERANCE 1 "));

            MovementTuningValues values = MovementTuningValues.Create(50f, 0.15f, 5.0f, 1, 0.25f, 10);

            Assert.AreEqual(60f, values.JoystickRadiusPx);
            Assert.AreEqual(2, values.StaleTickTolerance);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0, "JOYSTICK_RADIUS_PX", 80f)]
        [TestCase(1, "DEAD_ZONE_NORMALIZED", 0.15f)]
        [TestCase(2, "BASE_MOVE_SPEED", 5.0f)]
        [TestCase(4, "CAMERA_DRAG_SENSITIVITY", 0.25f)]
        public void MovementSystem_Tuning_NaNKnob_UsesDefaultWithOneWarning(int knob, string name, float expectedDefault)
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[MovementTuning\] " + name + " is NaN; using default "
                + Invariant(expectedDefault) + "$"));

            MovementTuningValues values = CreateWithKnob(knob, float.NaN);

            Assert.AreEqual(expectedDefault, ReadKnob(values, knob));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSystem_Tuning_FromAsset_ClampsAuthoredValues()
        {
            _asset = ScriptableObject.CreateInstance<MovementTuning>();
            _asset.SetForTesting(50f, 0.40f, 5.0f, 1, 0.25f, 10);
            LogAssert.Expect(LogType.Warning, new Regex(@"JOYSTICK_RADIUS_PX 50 .*using 60"));
            LogAssert.Expect(LogType.Warning, new Regex(@"DEAD_ZONE_NORMALIZED 0\.4 .*using 0\.3"));
            LogAssert.Expect(LogType.Warning, new Regex(@"STALE_TICK_TOLERANCE 1 .*using 2"));

            MovementTuningValues values = MovementTuningValues.FromAsset(_asset);

            Assert.AreEqual(60f, values.JoystickRadiusPx);
            Assert.AreEqual(0.30f, values.DeadZoneNormalized);
            Assert.AreEqual(2, values.StaleTickTolerance);
            AssertAssetUnchanged(_asset, 50f, 0.40f, 5.0f, 1, 0.25f, 10);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSystem_Tuning_OnValidate_LogsOutOfRangeWarning()
        {
            _asset = ScriptableObject.CreateInstance<MovementTuning>();
            _asset.SetForTesting(80f, 0.15f, 5.0f, 3, 0.25f, 31);
            LogAssert.Expect(LogType.Warning, new Regex(@"NAVMESH_STALL_RECOVERY_TICKS 31 .*using 30"));

            InvokeOnValidate(_asset);

            AssertAssetUnchanged(_asset, 80f, 0.15f, 5.0f, 3, 0.25f, 31);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSystem_Tuning_OnValidateValidAsset_LogsNothing()
        {
            _asset = ScriptableObject.CreateInstance<MovementTuning>();

            InvokeOnValidate(_asset);

            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-11 and the F-MOV-6 guard
        // ---------------------------------------------------------------------------------------

        [Test]
        public void MovementSystem_Tuning_PositionRoundTripSweep_ErrorWithinHalfCentimetre()
        {
            float maxError = 0f;
            for (int i = 0; i < SWEEP_COUNT; i++)
            {
                maxError = Mathf.Max(maxError, RoundTrip(SWEEP_START + i * SWEEP_STEP));
            }

            Assert.LessOrEqual(maxError, MAX_ROUND_TRIP_ERROR + FLOAT_REPRESENTATION_ALLOWANCE);
            // Non-vacuity: most sweep values are not whole centimetres, so a real round trip has some error; a
            // RoundTrip that always returned 0 (for example, decoding the input instead of the bytes) fails here.
            Assert.Greater(maxError, 0f, "the sweep must exercise rounding");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovementSystem_Tuning_PositionRoundTripKnownSample_RoundsToNearestCentimetre()
        {
            Span<byte> buffer = stackalloc byte[WireFixedPointCodec.PositionWireSize];
            WireFixedPointCodec.EncodePosition(buffer, new Vector3(12.344f, -85.236f, 0.004f));

            Vector3 decoded = WireFixedPointCodec.DecodePosition(buffer);

            Assert.AreEqual(12.34f, decoded.x, 1e-4f);
            Assert.AreEqual(-85.24f, decoded.y, 1e-4f);
            Assert.AreEqual(0f, decoded.z, 1e-4f);
        }

        [TestCase(-327.0f)]
        [TestCase(0f)]
        [TestCase(327.0f)]
        [TestCase(1.005f)]
        [TestCase(-1.005f)]
        [TestCase(326.995f)]
        public void MovementSystem_Tuning_PositionRoundTripEdgeValues_ErrorWithinHalfCentimetre(float axis)
        {
            Assert.LessOrEqual(RoundTrip(axis), MAX_ROUND_TRIP_ERROR + FLOAT_REPRESENTATION_ALLOWANCE);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(327.67f, 0f, true)]
        [TestCase(-327.67f, 0f, true)]
        [TestCase(OVER_ENCODING_LIMIT, 0f, false)]
        [TestCase(-OVER_ENCODING_LIMIT, 0f, false)]
        [TestCase(0f, 327.67f, true)]
        [TestCase(0f, -327.67f, true)]
        [TestCase(0f, OVER_ENCODING_LIMIT, false)]
        [TestCase(0f, -OVER_ENCODING_LIMIT, false)]
        public void MovementSystem_Tuning_IsEncodable_ChecksXAndZAgainstLimit(float x, float z, bool expected)
        {
            Assert.AreEqual(expected, MovementPositionGuard.IsEncodable(new Vector3(x, 0f, z)));
        }

        [Test]
        public void MovementSystem_Tuning_IsEncodable_IgnoresY()
        {
            Assert.IsTrue(MovementPositionGuard.IsEncodable(new Vector3(0f, 1000f, 0f)));
        }

        [Test]
        public void MovementSystem_Tuning_IsEncodableNaN_ReturnsFalse()
        {
            Assert.IsFalse(MovementPositionGuard.IsEncodable(new Vector3(float.NaN, 0f, 0f)));
            Assert.IsFalse(MovementPositionGuard.IsEncodable(new Vector3(0f, 0f, float.NaN)));
        }

        [Test]
        public void MovementSystem_Tuning_EncodingLimit_IsWireCodecLimit()
        {
            Assert.AreEqual(327.67f, WireFixedPointCodec.MaxPositionMeters);
        }
    }
}
