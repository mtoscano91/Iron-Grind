using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
        private static readonly Regex NOT_AUTHORED_LOG = new Regex(@"\[ZoneInstancing\] TownRespawnPointNotAuthored: .*StartingZone");
        private static readonly Regex MISSING_TEMPLATE_LOG = new Regex(@"\[ZoneInstancing\] TownRespawnPointMissing: template StartingZone");
        private static readonly Regex MISSING_FOREIGN_LOG = new Regex(@"\[ZoneInstancing\] TownRespawnPointMissing: foreign zone ZoneID\(2\)");

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

        private ZoneDefinition CreateDefinition(Vector3 min, Vector3 max, Vector3 point, bool authored)
        {
            var definition = ScriptableObject.CreateInstance<ZoneDefinition>();
            definition.Initialize(ZoneTemplateID.StartingZone, min, max, point, authored, null);
            _created.Add(definition);
            return definition;
        }

        private ZoneDefinition CreateAuthored()
        {
            return CreateDefinition(new Vector3(-100f, 0f, -50f), new Vector3(100f, 10f, 50f), new Vector3(10f, 0f, -5f), true);
        }

        private ZoneDefinition CreateUnauthoredValid()
        {
            return CreateDefinition(new Vector3(-100f, 0f, -50f), new Vector3(100f, 10f, 50f), Vector3.zero, false);
        }

        // ----- AC-ZI-9 ---------------------------------------------------------------------------

        [Test]
        public void Test_AuthoredPoint_ThousandCalls_SamePointNoAllocationNoLog()
        {
            var cache = new TownRespawnPointCache(Own, CreateAuthored());
            Vector3 expected = new Vector3(10f, 0f, -5f);
            Vector3 warm = cache.GetTownRespawnPoint(Own);
            Assert.AreEqual(expected, warm);

            bool allSame = true;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < CALL_COUNT; i++)
            {
                if (cache.GetTownRespawnPoint(Own) != expected)
                {
                    allSame = false;
                }
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.IsTrue(allSame);
            Assert.AreEqual(0L, after - before);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- AC-ZI-18 --------------------------------------------------------------------------

        [Test]
        public void Test_NoAuthoredPoint_ValidBounds_ReturnsCentroidWithOneWarning()
        {
            LogAssert.Expect(LogType.Warning, NOT_AUTHORED_LOG);

            var cache = new TownRespawnPointCache(Own, CreateUnauthoredValid());
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(new Vector3(0f, 5f, 0f), result);
            Assert.AreNotEqual(Vector3.zero, result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_NoAuthoredPoint_ZeroSizeBounds_ReturnsPointWithWarningNotAlert()
        {
            LogAssert.Expect(LogType.Warning, NOT_AUTHORED_LOG);
            var definition = CreateDefinition(new Vector3(3f, 0f, 4f), new Vector3(3f, 0f, 4f), Vector3.zero, false);

            var cache = new TownRespawnPointCache(Own, definition);
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(new Vector3(3f, 0f, 4f), result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_UnauthoredFlag_IgnoresStoredPointAndUsesCentroid()
        {
            LogAssert.Expect(LogType.Warning, NOT_AUTHORED_LOG);
            var definition = CreateDefinition(
                new Vector3(-100f, 0f, -50f), new Vector3(100f, 10f, 50f), new Vector3(7f, 7f, 7f), false);

            var cache = new TownRespawnPointCache(Own, definition);
            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(new Vector3(0f, 5f, 0f), result);
            Assert.AreNotEqual(new Vector3(7f, 7f, 7f), result);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- AC-ZI-10 --------------------------------------------------------------------------

        [TestCase(1f, 0f, 0f, 0f, 0f, 0f, TestName = "Test_InvalidBounds_MinXAboveMaxX_ZeroWithOneAlert")]
        [TestCase(0f, 0f, 0f, 0f, 0f, 251f, TestName = "Test_InvalidBounds_MaxZBeyondLimit_ZeroWithOneAlert")]
        [TestCase(0f, float.NaN, 0f, 0f, 0f, 0f, TestName = "Test_InvalidBounds_MinYNaN_ZeroWithOneAlert")]
        public void Test_NoAuthoredPoint_InvalidBounds_ReturnsZeroWithOneAlert(
            float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            LogAssert.Expect(LogType.Error, MISSING_TEMPLATE_LOG);
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

        // ----- Log once --------------------------------------------------------------------------

        [Test]
        public void Test_FallbackWarning_LoggedOnceAtConstructionNotPerCall()
        {
            LogAssert.Expect(LogType.Warning, NOT_AUTHORED_LOG);
            var cache = new TownRespawnPointCache(Own, CreateUnauthoredValid());

            cache.GetTownRespawnPoint(Own);
            cache.GetTownRespawnPoint(Own);
            cache.GetTownRespawnPoint(Own);

            LogAssert.NoUnexpectedReceived();
        }

        // ----- Closed zone -----------------------------------------------------------------------

        [Test]
        public void Test_ClosedZone_AuthoredPointStillReturned()
        {
            // The cache holds no lifecycle reference and never observes zone close (CR-ZI-11): whatever state the
            // caller's lifecycle is in (including Closed), the same point is returned.
            var cache = new TownRespawnPointCache(Own, CreateAuthored());

            Vector3 result = cache.GetTownRespawnPoint(Own);

            Assert.AreEqual(new Vector3(10f, 0f, -5f), result);
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
            Assert.AreEqual(new Vector3(10f, 0f, -5f), own);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- Null definition -------------------------------------------------------------------

        [Test]
        public void Test_Constructor_NullDefinition_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new TownRespawnPointCache(Own, null));
        }
    }
}
