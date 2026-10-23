using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// EditMode unit tests for Zone Instancing Story 005 — <see cref="TownRespawnPointCache"/>
    /// (CR-ZI-11, AC-ZI-9, AC-ZI-10, AC-ZI-18). Definitions are created in memory and destroyed in TearDown.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_TownRespawnPoint_Tests
    {
        private const uint OWN_ZONE_RAW = 1u;
        private const uint FOREIGN_ZONE_RAW = 2u;
        private const int CALL_COUNT = 1000;
        private const string NOT_AUTHORED_PREFIX = "[ZoneInstancing] TownRespawnPointNotAuthored: template StartingZone ";
        private const string MISSING_PREFIX = "[ZoneInstancing] TownRespawnPointMissing: template StartingZone ";
        private static readonly Regex MISSING_FOREIGN_LOG = new Regex(@"\[ZoneInstancing\] TownRespawnPointMissing: foreign zone ZoneID\(2\)");

        private static readonly Vector3 VALID_MIN = new Vector3(-100f, 0f, -50f);
        private static readonly Vector3 VALID_MAX = new Vector3(100f, 10f, 50f);
        private static readonly Vector3 VALID_CENTROID = new Vector3(0f, 5f, 0f);
        private static readonly Vector3 AUTHORED_POINT = new Vector3(10f, 0f, -5f);

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object created in _created)
            {
                if (created != null)
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }
            }

            _created.Clear();
        }

        private static ZoneID Own => new ZoneID(OWN_ZONE_RAW);

        private static ZoneID Foreign => new ZoneID(FOREIGN_ZONE_RAW);

        private static Regex Exact(string message)
        {
            return new Regex("^" + Regex.Escape(message) + "$");
        }

        private static Regex NotAuthoredLog(string reason, string centroid)
        {
            return Exact(NOT_AUTHORED_PREFIX + reason + "; using the bounds centroid " + centroid);
        }

        private static Regex MissingLog(string reason, string boundsFailure)
        {
            return Exact(MISSING_PREFIX + reason + " and no valid bounds (" + boundsFailure + "); returning zero");
        }

        private ZoneDefinition CreateDefinition(Vector3 min, Vector3 max, Vector3 point, bool authored)
        {
            var definition = ScriptableObject.CreateInstance<ZoneDefinition>();
            definition.Initialize(ZoneTemplateID.StartingZone, min, max, point, authored, null);
            _created.Add(definition);
            return definition;
        }

        private ZoneDefinition CreateAuthored()
        {
            return CreateDefinition(VALID_MIN, VALID_MAX, AUTHORED_POINT, true);
        }

        private ZoneDefinition CreateUnauthoredValid()
        {
            return CreateDefinition(VALID_MIN, VALID_MAX, Vector3.zero, false);
        }

        // ----- AC-ZI-9 ---------------------------------------------------------------------------

        [Test]
        public void Test_AuthoredPoint_ThousandCalls_SamePointNoAllocationNoLog()
        {
            var cache = new TownRespawnPointCache(Own, CreateAuthored());
            Vector3 warm = cache.GetTownRespawnPoint(Own);
            Assert.AreEqual(AUTHORED_POINT, warm);

            // GC.GetAllocatedBytesForCurrentThread() (the GDD's AC-ZI-9 measure) always returns 0 in the Unity 6.3
            // Editor's Mono, so the zero check would prove nothing (code review 2026-10-23). The project measure is
            // Unity's GC.Alloc sample count, as in TickLoop_CompletionQueue_tests.cs; the control test below shows
            // it registers an allocation.
            bool allSame = true;
            Assert.That(() =>
            {
                for (int i = 0; i < CALL_COUNT; i++)
                {
                    if (cache.GetTownRespawnPoint(Own) != AUTHORED_POINT)
                    {
                        allSame = false;
                    }
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "the own-zone path allocated");

            Assert.IsTrue(allSame, "a call returned a different point");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_AllocationMeasure_ForeignZoneCall_ReportsAllocation()
        {
            // Positive control for the AC-ZI-9 measure: the foreign-zone path builds a log string, so the GC.Alloc
            // count must move. If it did not, the zero-allocation assertion above would prove nothing.
            var cache = new TownRespawnPointCache(Own, CreateAuthored());
            LogAssert.Expect(LogType.Error, MISSING_FOREIGN_LOG);

            Assert.That(
                () => { cache.GetTownRespawnPoint(Foreign); },
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "the GC.Alloc measure did not register an allocation");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_AuthoredZeroPoint_ReturnsZeroNoLog()
        {
            var definition = CreateDefinition(VALID_MIN, VALID_MAX, Vector3.zero, true);

            var cache = new TownRespawnPointCache(Own, definition);
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(Vector3.zero, result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_AuthoredPoint_InvalidBounds_ReturnsAuthoredNoLog()
        {
            var definition = CreateDefinition(new Vector3(1f, 0f, 0f), Vector3.zero, AUTHORED_POINT, true);

            var cache = new TownRespawnPointCache(Own, definition);
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(AUTHORED_POINT, result);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- AC-ZI-18 --------------------------------------------------------------------------

        [Test]
        public void Test_NoAuthoredPoint_ValidBounds_ReturnsCentroidWithOneWarning()
        {
            LogAssert.Expect(LogType.Warning, NotAuthoredLog("has no authored respawn point", "0, 5, 0"));

            var cache = new TownRespawnPointCache(Own, CreateUnauthoredValid());
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(VALID_CENTROID, result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_NoAuthoredPoint_ZeroSizeBounds_ReturnsPointWithWarningNotAlert()
        {
            LogAssert.Expect(LogType.Warning, NotAuthoredLog("has no authored respawn point", "3, 0, 4"));
            var definition = CreateDefinition(new Vector3(3f, 0f, 4f), new Vector3(3f, 0f, 4f), Vector3.zero, false);

            var cache = new TownRespawnPointCache(Own, definition);
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(new Vector3(3f, 0f, 4f), result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_UnauthoredFlag_IgnoresStoredPointAndUsesCentroid()
        {
            LogAssert.Expect(LogType.Warning, NotAuthoredLog("has no authored respawn point", "0, 5, 0"));
            var definition = CreateDefinition(VALID_MIN, VALID_MAX, new Vector3(7f, 7f, 7f), false);

            var cache = new TownRespawnPointCache(Own, definition);
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(VALID_CENTROID, result);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(251f, 0f, 0f, "axis x = 251", TestName = "Test_InvalidAuthoredPoint_XBeyondLimit_CentroidWithWarning")]
        [TestCase(0f, float.NaN, 0f, "axis y = NaN", TestName = "Test_InvalidAuthoredPoint_YNaN_CentroidWithWarning")]
        [TestCase(0f, 0f, -300f, "axis z = -300", TestName = "Test_InvalidAuthoredPoint_ZBeyondLimit_CentroidWithWarning")]
        public void Test_InvalidAuthoredPoint_ValidBounds_ReturnsCentroidWithOneWarning(
            float x, float y, float z, string axisFailure)
        {
            LogAssert.Expect(LogType.Warning, NotAuthoredLog(
                "has an invalid authored respawn point (" + axisFailure + ")", "0, 5, 0"));
            var definition = CreateDefinition(VALID_MIN, VALID_MAX, new Vector3(x, y, z), true);

            var cache = new TownRespawnPointCache(Own, definition);
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(VALID_CENTROID, result);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- AC-ZI-10 --------------------------------------------------------------------------

        [TestCase(1f, 0f, 0f, 0f, 0f, 0f, "ZoneDefinitionLoadFailed: min above max on axis x",
            TestName = "Test_InvalidBounds_MinXAboveMaxX_ZeroWithOneAlert")]
        [TestCase(0f, 0f, 0f, 0f, 0f, 251f, "ZoneBoundsExceedEncoding: max.z = 251 exceeds +/-250 m",
            TestName = "Test_InvalidBounds_MaxZBeyondLimit_ZeroWithOneAlert")]
        [TestCase(0f, float.NaN, 0f, 0f, 0f, 0f, "ZoneDefinitionLoadFailed: non-finite bound on axis y",
            TestName = "Test_InvalidBounds_MinYNaN_ZeroWithOneAlert")]
        public void Test_NoAuthoredPoint_InvalidBounds_ReturnsZeroWithOneAlert(
            float minX, float minY, float minZ, float maxX, float maxY, float maxZ, string boundsFailure)
        {
            LogAssert.Expect(LogType.Error, MissingLog("has no authored respawn point", boundsFailure));
            var definition = CreateDefinition(
                new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ), new Vector3(7f, 7f, 7f), false);

            Vector3 result = Vector3.one;
            Assert.DoesNotThrow(() =>
            {
                var cache = new TownRespawnPointCache(Own, definition);
                result = cache.GetTownRespawnPoint(Own);
            });

            Assert.AreEqual(Vector3.zero, result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_InvalidAuthoredPoint_InvalidBounds_ReturnsZeroWithOneAlert()
        {
            LogAssert.Expect(LogType.Error, MissingLog(
                "has an invalid authored respawn point (axis x = 251)",
                "ZoneDefinitionLoadFailed: min above max on axis x"));
            var definition = CreateDefinition(new Vector3(1f, 0f, 0f), Vector3.zero, new Vector3(251f, 0f, 0f), true);

            var cache = new TownRespawnPointCache(Own, definition);
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(Vector3.zero, result);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- Log once --------------------------------------------------------------------------

        [Test]
        public void Test_FallbackWarning_LoggedOnceAtConstructionNotPerCall()
        {
            LogAssert.Expect(LogType.Warning, NotAuthoredLog("has no authored respawn point", "0, 5, 0"));
            var cache = new TownRespawnPointCache(Own, CreateUnauthoredValid());

            cache.GetTownRespawnPoint(Own);
            cache.GetTownRespawnPoint(Own);
            cache.GetTownRespawnPoint(Own);

            LogAssert.NoUnexpectedReceived();
        }

        // ----- Survives close --------------------------------------------------------------------

        [Test]
        public void Test_DefinitionDestroyedAfterConstruction_PointStillReturned()
        {
            // CR-ZI-11: the cache copies the values out of the definition and holds no lifecycle reference, so it
            // keeps answering after the zone closes and its assets are released.
            ZoneDefinition definition = CreateAuthored();
            var cache = new TownRespawnPointCache(Own, definition);

            UnityEngine.Object.DestroyImmediate(definition);
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(AUTHORED_POINT, result);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- Foreign ZoneID --------------------------------------------------------------------

        [Test]
        public void Test_ForeignZone_CalledTwice_ZeroAndTwoAlertsThenOwnStillWorks()
        {
            var cache = new TownRespawnPointCache(Own, CreateAuthored());
            LogAssert.Expect(LogType.Error, MISSING_FOREIGN_LOG);
            LogAssert.Expect(LogType.Error, MISSING_FOREIGN_LOG);

            Vector3 first = Vector3.one;
            Vector3 second = Vector3.one;
            Assert.DoesNotThrow(() =>
            {
                first = cache.GetTownRespawnPoint(Foreign);
                second = cache.GetTownRespawnPoint(Foreign);
            });
            Vector3 own = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(Vector3.zero, first);
            Assert.AreEqual(Vector3.zero, second);
            Assert.AreEqual(AUTHORED_POINT, own);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- Constructor arguments -------------------------------------------------------------

        [Test]
        public void Test_Constructor_NullDefinition_Throws()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new TownRespawnPointCache(Own, null));

            Assert.AreEqual("definition", exception.ParamName);
        }

        [Test]
        public void Test_Constructor_DestroyedDefinition_ThrowsArgumentNull()
        {
            ZoneDefinition definition = CreateAuthored();
            UnityEngine.Object.DestroyImmediate(definition);

            var exception = Assert.Throws<ArgumentNullException>(() => new TownRespawnPointCache(Own, definition));

            Assert.AreEqual("definition", exception.ParamName);
        }

        [Test]
        public void Test_Constructor_InvalidOwnZone_Throws()
        {
            ZoneDefinition definition = CreateAuthored();

            var exception = Assert.Throws<ArgumentException>(() => new TownRespawnPointCache(ZoneID.Invalid, definition));

            Assert.AreEqual("ownZone", exception.ParamName);
        }
    }
}
