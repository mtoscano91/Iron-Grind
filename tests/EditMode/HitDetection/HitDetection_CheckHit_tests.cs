using System;
using System.Reflection;
using System.Text.RegularExpressions;
using IronGrind.HitDetection;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.HitDetection
{
    /// <summary>
    /// EditMode unit tests for Hit Detection Story 001 - CheckHit and constants
    /// (CR-HD-1 to CR-HD-4, F-HD-1, F-HD-2, AC-HD-1 to 10, 14, 17, EC-HD-1, 4, 6, 7).
    /// </summary>
    [TestFixture]
    internal sealed class HitDetection_CheckHit_Tests
    {
        private const float RANGE = 3f;
        private const float DEFAULT_TOLERANCE_DEG = 45f;
        private const float BOUNDARY_INSIDE_DEG = 44.9f;
        private const float BOUNDARY_OUTSIDE_DEG = 45.1f;
        private const float BELOW_SAFE_DEG = 30f;
        private const float ABOVE_SAFE_DEG = 120f;
        private const float SAFE_MAX_DEG = 90f;
        private const float JUST_BEHIND_SAFE_MAX_DEG = 90.1f;
        private const float INSIDE_ABOVE_SAFE_DEG = 110f;
        private const float OUTSIDE_ABOVE_SAFE_DEG = 130f;
        private const float EPSILON_BELOW_FACTOR = 0.9f;
        private const float EPSILON_ABOVE_FACTOR = 1.1f;
        private const float JUST_OUT_OF_RANGE_X = 3.01f;
        private const float FAR_OUT_OF_RANGE_X = 5f;
        private const float TINY_OFFSET_X = 0.001f;
        private const float HEIGHT_OFFSET_Y = 10f;
        private const float SIN_30_X = 0.866f;
        private const float SIN_30_Z = 0.5f;
        private const string CONFIG_WARNING_PATTERN = "HitDetectionConfig";

        private static readonly Vector3 Origin = Vector3.zero;
        private static readonly Vector3 PlusX = new Vector3(1f, 0f, 0f);
        private static readonly Vector3 MinusX = new Vector3(-1f, 0f, 0f);

        private static HitDetector DefaultDetector()
        {
            return new HitDetector(HitDetectionConfig.Default);
        }

        private static HitDetector DetectorWith(float toleranceDeg)
        {
            return new HitDetector(new HitDetectionConfig(toleranceDeg));
        }

        private static Vector3 OffAxis(float angleDeg, float distance)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad) * distance, 0f, Mathf.Sin(rad) * distance);
        }

        // ---- HitResult and constants ----

        [Test]
        public void test_hit_detection_hit_result_is_a_byte_with_wire_values()
        {
            Type underlying = Enum.GetUnderlyingType(typeof(HitResult));

            Assert.AreEqual(typeof(byte), underlying);
            Assert.AreEqual((byte)0, (byte)HitResult.Hit);
            Assert.AreEqual((byte)1, (byte)HitResult.MissOutOfRange);
            Assert.AreEqual((byte)2, (byte)HitResult.MissFacing);
        }

        [Test]
        public void test_hit_detection_constants_match_the_gdd_values()
        {
            // Boundary values: the exact number is the point.
            Assert.AreEqual(45f, HitDetectionConfig.ATTACK_FACING_TOLERANCE_DEG_DEFAULT);
            Assert.AreEqual(45f, HitDetectionConfig.ATTACK_FACING_TOLERANCE_DEG_SAFE_MIN);
            Assert.AreEqual(90f, HitDetectionConfig.ATTACK_FACING_TOLERANCE_DEG_SAFE_MAX);
            Assert.AreEqual(0.0005f, HitDetectionConfig.POINT_BLANK_EPSILON_SQ);
            Assert.AreEqual(0.5f, HitDetectionConfig.ATTACK_RANGE_MIN);
            Assert.AreEqual(180f, HitDetectionConfig.FACING_CHECK_DISABLED_DEG);
        }

        // ---- Range (F-HD-1) ----

        [Test]
        public void test_hit_detection_in_range_diagonal_target_returns_hit_ac_hd_1()
        {
            var forward = new Vector3(1f, 0f, 1f).normalized;

            HitResult result = DefaultDetector().CheckHit(Origin, new Vector3(2f, 0f, 2f), RANGE, forward);

            Assert.AreEqual(HitResult.Hit, result);
        }

        [Test]
        public void test_hit_detection_target_exactly_at_range_returns_hit_ac_hd_2()
        {
            var target = new Vector3(RANGE, 0f, 0f);

            HitResult result = DefaultDetector().CheckHit(Origin, target, RANGE, PlusX);

            Assert.AreEqual(HitResult.Hit, result);
        }

        [Test]
        public void test_hit_detection_target_just_beyond_range_returns_miss_out_of_range_ac_hd_3()
        {
            var target = new Vector3(JUST_OUT_OF_RANGE_X, 0f, 0f);

            HitResult result = DefaultDetector().CheckHit(Origin, target, RANGE, PlusX);

            Assert.AreEqual(HitResult.MissOutOfRange, result);
        }

        [Test]
        public void test_hit_detection_target_high_above_ignores_y_returns_hit_ac_hd_4()
        {
            var target = new Vector3(2f, HEIGHT_OFFSET_Y, 0f);

            HitResult result = DefaultDetector().CheckHit(Origin, target, RANGE, PlusX);

            Assert.AreEqual(HitResult.Hit, result);
        }

        // ---- Facing (F-HD-2) ----

        [Test]
        public void test_hit_detection_target_directly_ahead_returns_hit_ac_hd_5()
        {
            HitResult result = DefaultDetector().CheckHit(Origin, PlusX, RANGE, PlusX);

            Assert.AreEqual(HitResult.Hit, result);
        }

        [Test]
        public void test_hit_detection_target_ninety_degrees_to_side_returns_miss_facing_ac_hd_6()
        {
            var target = new Vector3(0f, 0f, 1f);

            HitResult result = DefaultDetector().CheckHit(Origin, target, RANGE, PlusX);

            Assert.AreEqual(HitResult.MissFacing, result);
        }

        [Test]
        public void test_hit_detection_target_thirty_degrees_off_axis_returns_hit_ac_hd_7()
        {
            var target = new Vector3(SIN_30_X, 0f, SIN_30_Z);

            HitResult result = DefaultDetector().CheckHit(Origin, target, RANGE, PlusX);

            Assert.AreEqual(HitResult.Hit, result);
        }

        [Test]
        public void test_hit_detection_target_behind_attacker_returns_miss_facing_ac_hd_8()
        {
            HitResult result = DefaultDetector().CheckHit(Origin, PlusX, RANGE, MinusX);

            Assert.AreEqual(HitResult.MissFacing, result);
        }

        // ---- Point-blank guard (CR-HD-3) ----

        [Test]
        public void test_hit_detection_co_located_returns_hit_for_any_forward_ac_hd_9()
        {
            var pos = new Vector3(5f, 0f, 5f);
            HitDetector detector = DefaultDetector();

            Assert.AreEqual(HitResult.Hit, detector.CheckHit(pos, pos, RANGE, PlusX));
            Assert.AreEqual(HitResult.Hit, detector.CheckHit(pos, pos, RANGE, MinusX));
            Assert.AreEqual(HitResult.Hit, detector.CheckHit(pos, pos, RANGE, Vector3.zero));
        }

        [Test]
        public void test_hit_detection_target_inside_point_blank_with_reversed_forward_returns_hit_ac_hd_10()
        {
            var target = new Vector3(TINY_OFFSET_X, 0f, 0f);

            HitResult result = DefaultDetector().CheckHit(Origin, target, RANGE, MinusX);

            Assert.AreEqual(HitResult.Hit, result);
        }

        [Test]
        public void test_hit_detection_forward_derived_toward_target_never_returns_miss_facing_ac_hd_14()
        {
            var displacements = new[]
            {
                new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(0f, -1f),
                new Vector2(2f, 2f), new Vector2(-2f, 1.5f), new Vector2(0.15f, -0.1f), new Vector2(-0.8f, -2.4f),
            };
            var attacker = new Vector3(7f, 0f, -3f);
            HitDetector detector = DefaultDetector();

            foreach (Vector2 d in displacements)
            {
                var forward = new Vector3(d.x, 0f, d.y).normalized;
                var target = attacker + new Vector3(d.x, 0f, d.y);
                Assert.AreEqual(HitResult.Hit, detector.CheckHit(attacker, target, RANGE, forward), "displacement " + d);
            }
        }

        [Test]
        public void test_hit_detection_check_hit_takes_only_plain_values_and_holds_no_references_ac_hd_17()
        {
            MethodInfo method = typeof(HitDetector).GetMethod(nameof(HitDetector.CheckHit));

            ParameterInfo[] parameters = method.GetParameters();
            FieldInfo[] fields = typeof(HitDetector).GetFields(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.AreEqual(4, parameters.Length);
            Assert.AreEqual(typeof(Vector3), parameters[0].ParameterType);
            Assert.AreEqual(typeof(Vector3), parameters[1].ParameterType);
            Assert.AreEqual(typeof(float), parameters[2].ParameterType);
            Assert.AreEqual(typeof(Vector3), parameters[3].ParameterType);
            foreach (FieldInfo field in fields)
                Assert.IsTrue(field.FieldType.IsValueType, field.Name + " must not reference a service or EntityState");
        }

        // ---- Check order ----

        [Test]
        public void test_hit_detection_out_of_range_and_facing_away_returns_miss_out_of_range()
        {
            var target = new Vector3(FAR_OUT_OF_RANGE_X, 0f, 0f);

            HitResult result = DefaultDetector().CheckHit(Origin, target, RANGE, MinusX);

            Assert.AreEqual(HitResult.MissOutOfRange, result);
        }

        [Test]
        public void test_hit_detection_inside_point_blank_with_zero_range_returns_miss_out_of_range()
        {
            var target = new Vector3(TINY_OFFSET_X, 0f, 0f);

            HitResult result = DefaultDetector().CheckHit(Origin, target, 0f, PlusX);

            Assert.AreEqual(HitResult.MissOutOfRange, result);
        }

        [Test]
        public void test_hit_detection_co_located_with_zero_range_returns_hit_ec_hd_4()
        {
            HitResult result = DefaultDetector().CheckHit(Origin, Origin, 0f, PlusX);

            Assert.AreEqual(HitResult.Hit, result);
        }

        // ---- Boundaries ----

        [TestCase(BOUNDARY_INSIDE_DEG)]
        [TestCase(-BOUNDARY_INSIDE_DEG)]
        public void test_hit_detection_target_just_inside_tolerance_returns_hit(float angleDeg)
        {
            HitDetector detector = DetectorWith(DEFAULT_TOLERANCE_DEG);

            HitResult result = detector.CheckHit(Origin, OffAxis(angleDeg, 1f), RANGE, PlusX);

            Assert.AreEqual(HitResult.Hit, result);
        }

        [TestCase(BOUNDARY_OUTSIDE_DEG)]
        [TestCase(-BOUNDARY_OUTSIDE_DEG)]
        public void test_hit_detection_target_just_outside_tolerance_returns_miss_facing(float angleDeg)
        {
            HitDetector detector = DetectorWith(DEFAULT_TOLERANCE_DEG);

            HitResult result = detector.CheckHit(Origin, OffAxis(angleDeg, 1f), RANGE, PlusX);

            Assert.AreEqual(HitResult.MissFacing, result);
        }

        [Test]
        public void test_hit_detection_epsilon_boundary_hits_below_and_checks_facing_above()
        {
            float edge = Mathf.Sqrt(HitDetectionConfig.POINT_BLANK_EPSILON_SQ);
            var below = new Vector3(edge * EPSILON_BELOW_FACTOR, 0f, 0f);
            var above = new Vector3(edge * EPSILON_ABOVE_FACTOR, 0f, 0f);
            HitDetector detector = DefaultDetector();

            Assert.AreEqual(HitResult.Hit, detector.CheckHit(Origin, below, RANGE, MinusX));
            Assert.AreEqual(HitResult.MissFacing, detector.CheckHit(Origin, above, RANGE, MinusX));
        }

        [Test]
        public void test_hit_detection_tolerance_ninety_passes_side_target_and_rejects_rear()
        {
            HitDetector detector = DetectorWith(SAFE_MAX_DEG);
            var side = new Vector3(0f, 0f, 1f);

            Assert.AreEqual(HitResult.Hit, detector.CheckHit(Origin, side, RANGE, PlusX));
            Assert.AreEqual(HitResult.MissFacing,
                detector.CheckHit(Origin, OffAxis(JUST_BEHIND_SAFE_MAX_DEG, 1f), RANGE, PlusX));
        }

        [Test]
        public void test_hit_detection_tolerance_above_safe_range_still_applies()
        {
            LogAssert.Expect(LogType.Warning, new Regex(CONFIG_WARNING_PATTERN));
            HitDetector detector = DetectorWith(ABOVE_SAFE_DEG);

            Assert.AreEqual(HitResult.Hit,
                detector.CheckHit(Origin, OffAxis(INSIDE_ABOVE_SAFE_DEG, 1f), RANGE, PlusX));
            Assert.AreEqual(HitResult.MissFacing,
                detector.CheckHit(Origin, OffAxis(OUTSIDE_ABOVE_SAFE_DEG, 1f), RANGE, PlusX));
        }

        [Test]
        public void test_hit_detection_facing_disabled_with_reversed_forward_returns_hit_ec_hd_7()
        {
            HitDetector detector = DetectorWith(HitDetectionConfig.FACING_CHECK_DISABLED_DEG);

            HitResult result = detector.CheckHit(Origin, PlusX, RANGE, MinusX);

            Assert.AreEqual(HitResult.Hit, result);
        }

        [Test]
        public void test_hit_detection_facing_disabled_with_reversed_diagonal_forward_returns_hit()
        {
            var displacements = new[]
            {
                new Vector2(1f, 1f), new Vector2(-1f, 2f), new Vector2(0.3f, -0.7f), new Vector2(-1.7f, -1.1f),
                new Vector2(2.1f, 0.9f), new Vector2(0.1f, 0.3f), new Vector2(-0.9f, 2.3f), new Vector2(1.3f, -2.2f),
            };
            HitDetector detector = DetectorWith(HitDetectionConfig.FACING_CHECK_DISABLED_DEG);

            foreach (Vector2 d in displacements)
            {
                var toTarget = new Vector3(d.x, 0f, d.y);
                Assert.AreEqual(HitResult.Hit, detector.CheckHit(Origin, toTarget, RANGE, -toTarget.normalized),
                    "displacement " + d);
            }
        }

        [Test]
        public void test_hit_detection_facing_disabled_still_applies_the_range_check()
        {
            HitDetector detector = DetectorWith(HitDetectionConfig.FACING_CHECK_DISABLED_DEG);
            var target = new Vector3(FAR_OUT_OF_RANGE_X, 0f, 0f);

            HitResult result = detector.CheckHit(Origin, target, RANGE, PlusX);

            Assert.AreEqual(HitResult.MissOutOfRange, result);
        }

        [Test]
        public void test_hit_detection_zero_forward_beyond_guard_returns_miss_facing()
        {
            HitResult result = DefaultDetector().CheckHit(Origin, PlusX, RANGE, Vector3.zero);

            Assert.AreEqual(HitResult.MissFacing, result);
        }

        // ---- Config ----

        [TestCase(BELOW_SAFE_DEG)]
        [TestCase(ABOVE_SAFE_DEG)]
        public void test_hit_detection_config_tolerance_outside_safe_range_warns_once_and_keeps_value(float toleranceDeg)
        {
            LogAssert.Expect(LogType.Warning, new Regex(CONFIG_WARNING_PATTERN));

            var config = new HitDetectionConfig(toleranceDeg);

            Assert.AreEqual(toleranceDeg, config.AttackFacingToleranceDeg);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(HitDetectionConfig.ATTACK_FACING_TOLERANCE_DEG_SAFE_MIN)]
        [TestCase(SAFE_MAX_DEG)]
        [TestCase(HitDetectionConfig.FACING_CHECK_DISABLED_DEG)]
        public void test_hit_detection_config_tolerance_in_safe_range_or_disabled_logs_nothing(float toleranceDeg)
        {
            var config = new HitDetectionConfig(toleranceDeg);

            Assert.AreEqual(toleranceDeg, config.AttackFacingToleranceDeg);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_hit_detection_config_default_uses_default_tolerance_without_warning()
        {
            // A fresh instance: the static Default may have been built before this test began.
            var fresh = new HitDetectionConfig(HitDetectionConfig.ATTACK_FACING_TOLERANCE_DEG_DEFAULT);

            Assert.AreEqual(HitDetectionConfig.ATTACK_FACING_TOLERANCE_DEG_DEFAULT,
                HitDetectionConfig.Default.AttackFacingToleranceDeg);
            Assert.AreEqual(HitDetectionConfig.Default.AttackFacingToleranceDeg, fresh.AttackFacingToleranceDeg);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(180.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void test_hit_detection_config_invalid_tolerance_throws(float toleranceDeg)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HitDetectionConfig(toleranceDeg));
        }

        [Test]
        public void test_hit_detection_detector_null_config_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new HitDetector(null));
        }

        // ---- Allocation ----

        [Test]
        public void test_hit_detection_check_hit_all_outcomes_do_not_allocate()
        {
            HitDetector detector = DefaultDetector();
            var target = new Vector3(2f, 0f, 0f);
            var far = new Vector3(RANGE * 2f, 0f, 0f);

            Assert.That(() =>
            {
                detector.CheckHit(Origin, target, RANGE, PlusX);
                detector.CheckHit(Origin, far, RANGE, PlusX);
                detector.CheckHit(Origin, target, RANGE, MinusX);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
