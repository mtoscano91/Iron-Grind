using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.Navigation
{
    /// <summary>EditMode unit tests for Navigation Story 007: SpawnPointValidator (CR-NAV-13, EC-NAV-5, AC-NAV-21).</summary>
    [TestFixture]
    internal sealed class Navigation_SpawnPointValidation_Tests
    {
        private static readonly ZoneID ZONE = new ZoneID(7u);

        private NavigationConfig _config;
        private FakeMeshQuery _query;
        private List<SpawnPointInput> _valid;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            _query = new FakeMeshQuery();
            _valid = new List<SpawnPointInput>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_config);
        }

        private void SetConfig(float? fraction = null, float? radius = null)
        {
            NavigationConfigValues d = NavigationConfigValues.Defaults;
            _config.SetForTesting(new NavigationConfigValues(
                d.MaxAgentPoolSize, d.MaxPathUpdatesPerTick, d.PartialPathTimeoutTicks, d.ReturnSpeedCap,
                fraction ?? d.MinValidSpawnFraction, d.NavStuckRerouteDetectionTicks, d.NavOffMeshRecoveryTicks,
                radius ?? d.SpawnSampleRadius, d.StandInMobMoveSpeed));
        }

        // Points 0..count-1 at (i, 0, 0), ids 100 + i. Indexes in invalid (ascending) are off the mesh.
        private List<SpawnPointInput> MakePoints(int count, params int[] invalid)
        {
            var points = new List<SpawnPointInput>();
            for (int i = 0; i < count; i++)
            {
                points.Add(new SpawnPointInput(100u + (uint)i, new Vector3(i, 0f, 0f)));
            }

            var bad = new HashSet<int>(invalid);
            _query.Hits = pos => !bad.Contains((int)pos.x);
            foreach (int i in invalid)
            {
                if (i >= 0 && i < count)
                {
                    ExpectOffMeshError(100u + (uint)i, new Vector3(i, 0f, 0f));
                }
            }

            return points;
        }

        // The indexes 0..count-1.
        private static int[] FirstN(int count)
        {
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = i;
            }

            return result;
        }

        private static void ExpectOffMeshError(uint id, Vector3 position)
        {
            LogAssert.Expect(LogType.Error, new Regex(
                "^\\[Navigation\\] Spawn point off the NavMesh: zone ZoneID\\(7\\), spawn point " + id
                + ", position " + Regex.Escape(position.ToString("F3")) + "$"));
        }

        private static void ExpectCritical(int valid, int total)
        {
            LogAssert.Expect(LogType.Error, new Regex(
                "^\\[Navigation\\] CRITICAL: zone ZoneID\\(7\\) has only " + valid + " of " + total
                + " valid spawn points; the zone cannot serve\\.$"));
        }

        private static void AssertIds(List<SpawnPointInput> actual, params uint[] expected)
        {
            var ids = new List<uint>();
            foreach (SpawnPointInput p in actual)
            {
                ids.Add(p.Id);
            }

            Assert.That(ids, Is.EqualTo(expected));
        }

        private SpawnValidationResult Run(List<SpawnPointInput> points)
        {
            var validator = new SpawnPointValidator(_query, _config);
            return validator.Validate(ZONE, points, _valid);
        }

        [Test]
        public void test_validate_oneOffMeshPointOfTen_keepsNineInOrderAndLogsOneError()
        {
            SetConfig();
            List<SpawnPointInput> points = MakePoints(10, 4);

            SpawnValidationResult r = Run(points);

            Assert.That(r.ValidCount, Is.EqualTo(9));
            Assert.That(r.TotalCount, Is.EqualTo(10));
            Assert.That(r.ShouldAbort, Is.False);
            AssertIds(_valid, 100u, 101u, 102u, 103u, 105u, 106u, 107u, 108u, 109u);
            Assert.That(_query.CallCount, Is.EqualTo(10));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_offMeshError_containsZoneIdSpawnPointIdAndPosition()
        {
            SetConfig(fraction: 0.50f);
            List<SpawnPointInput> points = MakePoints(3);
            _query.Hits = pos => pos.x != 1f;
            ExpectOffMeshError(101u, new Vector3(1f, 0f, 0f));

            Run(points);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_config_defaultMinValidSpawnFraction_isNinetyPercent()
        {
            Assert.That(NavigationConfigValues.Defaults.MinValidSpawnFraction, Is.EqualTo(0.90f));
        }

        [Test]
        public void test_validate_nineOfTenAtFraction090_doesNotAbort()
        {
            SetConfig(fraction: 0.90f);

            SpawnValidationResult r = Run(MakePoints(10, 0));

            Assert.That(r.ShouldAbort, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_eightOfTenAtFraction090_abortsWithOneCritical()
        {
            SetConfig(fraction: 0.90f);
            List<SpawnPointInput> points = MakePoints(10, 0, 1);
            ExpectCritical(8, 10);

            SpawnValidationResult r = Run(points);

            Assert.That(r.ValidCount, Is.EqualTo(8));
            Assert.That(r.TotalCount, Is.EqualTo(10));
            Assert.That(r.ShouldAbort, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_floatFraction055_elevenOfTwentyDoesNotAbort()
        {
            SetConfig(fraction: 0.55f);

            SpawnValidationResult r = Run(MakePoints(20, FirstN(9)));

            Assert.That(r.ValidCount, Is.EqualTo(11));
            Assert.That(r.ShouldAbort, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_floatFraction055_tenOfTwentyAborts()
        {
            SetConfig(fraction: 0.55f);
            List<SpawnPointInput> points = MakePoints(20, FirstN(10));
            ExpectCritical(10, 20);

            SpawnValidationResult r = Run(points);

            Assert.That(r.ValidCount, Is.EqualTo(10));
            Assert.That(r.ShouldAbort, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_fractionOne_tenOfTenDoesNotAbort()
        {
            SetConfig(fraction: 1.00f);

            SpawnValidationResult r = Run(MakePoints(10));

            Assert.That(r.ValidCount, Is.EqualTo(10));
            Assert.That(r.ShouldAbort, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_fractionOne_nineOfTenAborts()
        {
            SetConfig(fraction: 1.00f);
            List<SpawnPointInput> points = MakePoints(10, 3);
            ExpectCritical(9, 10);

            SpawnValidationResult r = Run(points);

            Assert.That(r.ValidCount, Is.EqualTo(9));
            Assert.That(r.ShouldAbort, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_fractionHalf_fiveOfTenDoesNotAbort()
        {
            SetConfig(fraction: 0.50f);

            SpawnValidationResult r = Run(MakePoints(10, FirstN(5)));

            Assert.That(r.ValidCount, Is.EqualTo(5));
            Assert.That(r.ShouldAbort, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_fractionHalf_fourOfTenAborts()
        {
            SetConfig(fraction: 0.50f);
            List<SpawnPointInput> points = MakePoints(10, FirstN(6));
            ExpectCritical(4, 10);

            SpawnValidationResult r = Run(points);

            Assert.That(r.ValidCount, Is.EqualTo(4));
            Assert.That(r.ShouldAbort, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_fraction0905_oneHundredEightyOneOfTwoHundredDoesNotAbort()
        {
            SetConfig(fraction: 0.905f);

            SpawnValidationResult r = Run(MakePoints(200, FirstN(19)));

            Assert.That(r.ValidCount, Is.EqualTo(181));
            Assert.That(r.ShouldAbort, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_fraction0905_oneHundredEightyOfTwoHundredAborts()
        {
            SetConfig(fraction: 0.905f);
            List<SpawnPointInput> points = MakePoints(200, FirstN(20));
            ExpectCritical(180, 200);

            SpawnValidationResult r = Run(points);

            Assert.That(r.ValidCount, Is.EqualTo(180));
            Assert.That(r.ShouldAbort, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_singlePointValid_doesNotAbort()
        {
            SetConfig(fraction: 0.90f);

            SpawnValidationResult r = Run(MakePoints(1));

            Assert.That(r.ValidCount, Is.EqualTo(1));
            Assert.That(r.TotalCount, Is.EqualTo(1));
            Assert.That(r.ShouldAbort, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_singlePointInvalid_logsOneErrorAndOneCriticalAndAborts()
        {
            SetConfig(fraction: 0.90f);
            List<SpawnPointInput> points = MakePoints(1, 0);
            ExpectCritical(0, 1);

            SpawnValidationResult r = Run(points);

            Assert.That(r.ValidCount, Is.EqualTo(0));
            Assert.That(r.TotalCount, Is.EqualTo(1));
            Assert.That(r.ShouldAbort, Is.True);
            Assert.That(_valid, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_allInvalid_emptiesOutputAbortsWithThreeErrorsAndOneCritical()
        {
            SetConfig();
            List<SpawnPointInput> points = MakePoints(3, 0, 1, 2);
            ExpectCritical(0, 3);

            SpawnValidationResult r = Run(points);

            Assert.That(_valid, Is.Empty);
            Assert.That(r.ValidCount, Is.EqualTo(0));
            Assert.That(r.TotalCount, Is.EqualTo(3));
            Assert.That(r.ShouldAbort, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_emptyList_logsExactWarningAndDoesNotAbortOrQuery()
        {
            SetConfig();
            _valid.Add(new SpawnPointInput(1u, Vector3.one));
            LogAssert.Expect(LogType.Warning,
                "[Navigation] Zone " + ZONE + " has an empty spawn table — no mobs will spawn. Verify this is intentional.");

            SpawnValidationResult r = Run(new List<SpawnPointInput>());

            Assert.That(r.ValidCount, Is.EqualTo(0));
            Assert.That(r.TotalCount, Is.EqualTo(0));
            Assert.That(r.ShouldAbort, Is.False);
            Assert.That(_query.CallCount, Is.EqualTo(0));
            Assert.That(_valid, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(float.NaN, 0f, 0f)]
        [TestCase(0f, float.NaN, 0f)]
        [TestCase(0f, 0f, float.NaN)]
        [TestCase(float.NaN, float.NaN, float.NaN)]
        [TestCase(0f, float.PositiveInfinity, 0f)]
        [TestCase(0f, 0f, float.NegativeInfinity)]
        [TestCase(float.NegativeInfinity, 0f, 0f)]
        public void test_validate_nonFinitePosition_isInvalidLogsErrorAndSkipsQuery(float x, float y, float z)
        {
            SetConfig(fraction: 0.50f);
            var bad = new Vector3(x, y, z);
            var points = new List<SpawnPointInput>
            {
                new SpawnPointInput(1u, new Vector3(1f, 0f, 0f)),
                new SpawnPointInput(2u, bad),
                new SpawnPointInput(3u, new Vector3(3f, 0f, 0f)),
            };
            ExpectOffMeshError(2u, bad);

            SpawnValidationResult r = Run(points);

            Assert.That(r.ValidCount, Is.EqualTo(2));
            Assert.That(r.TotalCount, Is.EqualTo(3));
            Assert.That(r.ShouldAbort, Is.False);
            Assert.That(_query.CallCount, Is.EqualTo(2));
            Assert.That(_query.CalledPositions,
                Is.EqualTo(new[] { new Vector3(1f, 0f, 0f), new Vector3(3f, 0f, 0f) }));
            AssertIds(_valid, 1u, 3u);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_mixedNonFiniteMissAndValid_countsOrderAndLogsExactlyTwoErrors()
        {
            SetConfig(fraction: 0.50f);
            var points = new List<SpawnPointInput>();
            for (int i = 0; i < 6; i++)
            {
                points.Add(new SpawnPointInput(100u + (uint)i,
                    i == 1 ? new Vector3(float.NaN, 0f, 0f) : new Vector3(i, 0f, 0f)));
            }

            _query.Hits = pos => pos.x != 3f;
            ExpectOffMeshError(101u, points[1].Position);
            ExpectOffMeshError(103u, new Vector3(3f, 0f, 0f));

            SpawnValidationResult r = Run(points);

            // 4 of 6 valid is above the 0.50 threshold, so no CRITICAL.
            Assert.That(r.ValidCount, Is.EqualTo(4));
            Assert.That(r.TotalCount, Is.EqualTo(6));
            Assert.That(r.ShouldAbort, Is.False);
            AssertIds(_valid, 100u, 102u, 104u, 105u);
            Assert.That(_query.CallCount, Is.EqualTo(5));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_nullPoints_throwsArgumentNullException()
        {
            SetConfig();
            var validator = new SpawnPointValidator(_query, _config);

            Assert.Throws<ArgumentNullException>(() => validator.Validate(ZONE, null, _valid));
        }

        [Test]
        public void test_validate_nullValidOut_throwsArgumentNullException()
        {
            SetConfig();
            var validator = new SpawnPointValidator(_query, _config);

            Assert.Throws<ArgumentNullException>(
                () => validator.Validate(ZONE, new List<SpawnPointInput>(), null));
        }

        [Test]
        public void test_constructor_nullQuery_throwsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new SpawnPointValidator(null, _config));
        }

        [Test]
        public void test_constructor_nullConfig_throwsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new SpawnPointValidator(_query, null));
        }

        [Test]
        public void test_validate_staleValidOut_isClearedFirst()
        {
            SetConfig();
            _valid.Add(new SpawnPointInput(999u, new Vector3(9f, 9f, 9f)));
            _valid.Add(new SpawnPointInput(998u, new Vector3(8f, 8f, 8f)));
            List<SpawnPointInput> points = MakePoints(2);

            Run(points);

            AssertIds(_valid, 100u, 101u);
        }

        [Test]
        public void test_validate_reusedValidator_secondCallDoesNotCarryOverFirstCall()
        {
            SetConfig(fraction: 0.50f);
            var validator = new SpawnPointValidator(_query, _config);
            validator.Validate(ZONE, MakePoints(3), _valid);
            _query.CalledPositions.Clear();
            var second = new List<SpawnPointInput>
            {
                new SpawnPointInput(200u, new Vector3(10f, 0f, 0f)),
                new SpawnPointInput(201u, new Vector3(11f, 0f, 0f)),
            };
            _query.Hits = pos => pos.x != 11f;
            ExpectOffMeshError(201u, new Vector3(11f, 0f, 0f));

            SpawnValidationResult r = validator.Validate(ZONE, second, _valid);

            Assert.That(r.ValidCount, Is.EqualTo(1));
            Assert.That(r.TotalCount, Is.EqualTo(2));
            Assert.That(r.ShouldAbort, Is.False);
            AssertIds(_valid, 200u);
            Assert.That(_query.CalledPositions,
                Is.EqualTo(new[] { new Vector3(10f, 0f, 0f), new Vector3(11f, 0f, 0f) }));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_queryCalls_useConfigRadiusAndAllAreas()
        {
            SetConfig(radius: 0.35f);

            Run(MakePoints(3));

            Assert.That(_query.CalledRadii, Is.EqualTo(new[] { 0.35f, 0.35f, 0.35f }));
            Assert.That(_query.CalledAreaMasks, Is.EqualTo(new[] { NavMesh.AllAreas, NavMesh.AllAreas, NavMesh.AllAreas }));
        }

        [Test]
        public void test_validate_configChangedAfterConstruction_usesOriginalRadiusAndFraction()
        {
            SetConfig(fraction: 0.90f, radius: 0.35f);
            var validator = new SpawnPointValidator(_query, _config);
            SetConfig(fraction: 0.10f, radius: 2.5f);
            List<SpawnPointInput> points = MakePoints(10, 0, 1);
            ExpectCritical(8, 10);

            SpawnValidationResult r = validator.Validate(ZONE, points, _valid);

            Assert.That(r.ShouldAbort, Is.True);
            Assert.That(_query.CalledRadii.Count, Is.EqualTo(10));
            Assert.That(_query.CalledRadii, Is.All.EqualTo(0.35f));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_hitOffsetFromInput_keepsAuthoredPosition()
        {
            SetConfig();
            _query.HitOffset = new Vector3(0f, 0.5f, 0.25f);
            var points = new List<SpawnPointInput>
            {
                new SpawnPointInput(5u, new Vector3(1f, 2f, 3f)),
                new SpawnPointInput(6u, new Vector3(4f, 5f, 6f)),
            };

            Run(points);

            Assert.That(_valid[0].Position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(_valid[1].Position, Is.EqualTo(new Vector3(4f, 5f, 6f)));
        }

        [Test]
        public void test_validate_validPoints_keepDeclarationOrder()
        {
            SetConfig(fraction: 0.50f);
            List<SpawnPointInput> points = MakePoints(6, 2);

            Run(points);

            AssertIds(_valid, 100u, 101u, 103u, 104u, 105u);
        }
    }
}
