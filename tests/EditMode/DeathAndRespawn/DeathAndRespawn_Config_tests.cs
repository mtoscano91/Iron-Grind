using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using IronGrind.Networking;
using IronGrind.ServerLogic.DeathAndRespawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// EditMode tests for Death and Respawn Story 001: <see cref="DeathAndRespawnConfigValues"/> (AC-1 to AC-5).
    /// AC-1 assembly placement (ADR-012) is covered by the assembly boundary test.
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_Config_Tests
    {
        private const float VALID_SECONDS = DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_DEFAULT;
        private const int VALID_RETRY = DeathAndRespawnConfigValues.RESPAWN_ANCHOR_RETRY_TICKS_DEFAULT;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private DeathAndRespawnConfig MakeAsset()
        {
            var asset = ScriptableObject.CreateInstance<DeathAndRespawnConfig>();
            _created.Add(asset);
            return asset;
        }

        private static string Inv(float value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Expects the seconds error naming the offending value and the allowed range.</summary>
        private static void ExpectSecondsError(float seconds)
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "RESPAWN_DELAY_SECONDS = " + Inv(seconds) + " is outside ["
                + Inv(DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_MIN) + ", "
                + Inv(DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_MAX) + "].")));
        }

        /// <summary>Expects the retry error naming the offending value and the allowed range.</summary>
        private static void ExpectRetryError(int retry)
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "RESPAWN_ANCHOR_RETRY_TICKS = " + Inv(retry) + " is outside ["
                + Inv(DeathAndRespawnConfigValues.RESPAWN_ANCHOR_RETRY_TICKS_MIN) + ", "
                + Inv(DeathAndRespawnConfigValues.RESPAWN_ANCHOR_RETRY_TICKS_MAX) + "].")));
        }

        // ---- AC-1 ----

        [Test]
        public void test_defaults_validAndExpectedValues()
        {
            DeathAndRespawnConfigValues v = DeathAndRespawnConfigValues.Defaults;

            Assert.IsTrue(v.Validate());
            Assert.AreEqual(7.0f, v.RespawnDelaySeconds);
            Assert.AreEqual(40, v.RespawnAnchorRetryTicks);
        }

        [Test]
        public void test_freshConfigAsset_fieldsEqualDefaults()
        {
            DeathAndRespawnConfig asset = MakeAsset();

            Assert.AreEqual(DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_DEFAULT, asset.RespawnDelaySeconds);
            Assert.AreEqual(DeathAndRespawnConfigValues.RESPAWN_ANCHOR_RETRY_TICKS_DEFAULT, asset.RespawnAnchorRetryTicks);
            Assert.IsTrue(DeathAndRespawnConfigValues.FromAsset(asset).Validate());
        }

        [Test]
        public void test_fromAsset_copiesBothFields()
        {
            DeathAndRespawnConfig asset = MakeAsset();
            asset.SetForTests(8.0f, 60);

            DeathAndRespawnConfigValues v = DeathAndRespawnConfigValues.FromAsset(asset);

            Assert.AreEqual(8.0f, v.RespawnDelaySeconds);
            Assert.AreEqual(60, v.RespawnAnchorRetryTicks);
        }

        [Test]
        public void test_fromAsset_outOfRangeValuesCopiedUnclampedThenRejected()
        {
            DeathAndRespawnConfig asset = MakeAsset();
            asset.SetForTests(3.0f, 5);

            DeathAndRespawnConfigValues v = DeathAndRespawnConfigValues.FromAsset(asset);

            Assert.AreEqual(3.0f, v.RespawnDelaySeconds);
            Assert.AreEqual(5, v.RespawnAnchorRetryTicks);
            ExpectSecondsError(3.0f);
            ExpectRetryError(5);
            Assert.IsFalse(v.Validate());
        }

        [Test]
        public void test_fromAsset_nullGivesDefaultsAndWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no config asset"));

            DeathAndRespawnConfigValues v = DeathAndRespawnConfigValues.FromAsset(null);

            Assert.AreEqual(7.0f, v.RespawnDelaySeconds);
            Assert.AreEqual(40, v.RespawnAnchorRetryTicks);
        }

        [Test]
        public void test_defaultStruct_failsWithTwoErrors()
        {
            ExpectSecondsError(0f);
            ExpectRetryError(0);

            Assert.IsFalse(default(DeathAndRespawnConfigValues).Validate());
        }

        // ---- AC-2 (F-DR-1) ----

        [Test]
        public void test_tickRate_isTwentyHz()
        {
            // F-DR-1 expected values below assume the networking constant is 20 Hz.
            Assert.AreEqual(20, ServerTickLoop.TICK_RATE_HZ);
        }

        [TestCase(7.0f, 140)]
        [TestCase(5.0f, 100)]
        [TestCase(10.0f, 200)]
        [TestCase(7.05f, 141)]
        [TestCase(6.999f, 140)]
        public void test_respawnDelayTicks_roundsSecondsTimesTickRate(float seconds, int expectedTicks)
        {
            var v = new DeathAndRespawnConfigValues(seconds, VALID_RETRY);

            Assert.AreEqual(expectedTicks, v.RespawnDelayTicks);
            Assert.AreEqual(expectedTicks, DeathAndRespawnConfigValues.ToTicks(seconds));
        }

        // ---- AC-3 ----

        [TestCase(4.9f)]
        [TestCase(0.0f)]
        [TestCase(-1.0f)]
        [TestCase(10.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void test_secondsOutOfRange_failsWithOneErrorNamingValueAndRange(float seconds)
        {
            ExpectSecondsError(seconds);

            Assert.IsFalse(new DeathAndRespawnConfigValues(seconds, VALID_RETRY).Validate());
        }

        [TestCase(5.0f)]
        [TestCase(10.0f)]
        public void test_secondsBoundaries_passWithNoLog(float seconds)
        {
            Assert.IsTrue(new DeathAndRespawnConfigValues(seconds, VALID_RETRY).Validate());
        }

        // ---- AC-4 ----

        [TestCase(19)]
        [TestCase(201)]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        public void test_retryTicksOutOfRange_failsWithOneErrorNamingValueAndRange(int retry)
        {
            ExpectRetryError(retry);

            Assert.IsFalse(new DeathAndRespawnConfigValues(VALID_SECONDS, retry).Validate());
        }

        [TestCase(20)]
        [TestCase(200)]
        public void test_retryTicksBoundaries_passWithNoLog(int retry)
        {
            Assert.IsTrue(new DeathAndRespawnConfigValues(VALID_SECONDS, retry).Validate());
        }

        // ---- AC-5 ----

        [TestCase(5.0f)]
        [TestCase(7.0f)]
        [TestCase(10.0f)]
        public void test_validatedValues_delayTicksAtLeastMinimum(float seconds)
        {
            var v = new DeathAndRespawnConfigValues(seconds, VALID_RETRY);

            Assert.IsTrue(v.Validate());
            Assert.GreaterOrEqual(v.RespawnDelayTicks, DeathAndRespawnConfigValues.RESPAWN_DELAY_TICKS_MIN);
        }

        [Test]
        public void test_delayTicksFloor_belowMinimumFailsWithOneError()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "RESPAWN_DELAY_TICKS = 99 is below the minimum 100.")));

            Assert.IsFalse(DeathAndRespawnConfigValues.CheckDelayTicksFloor(99));
        }

        [Test]
        public void test_delayTicksFloor_atMinimumPassesWithNoLog()
        {
            Assert.IsTrue(DeathAndRespawnConfigValues.CheckDelayTicksFloor(
                DeathAndRespawnConfigValues.RESPAWN_DELAY_TICKS_MIN));
        }

        // ---- both knobs bad ----

        [Test]
        public void test_bothKnobsBad_twoErrorsAndFalse()
        {
            ExpectSecondsError(4.0f);
            ExpectRetryError(19);

            Assert.IsFalse(new DeathAndRespawnConfigValues(4.0f, 19).Validate());
        }
    }
}
