using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using IronGrind.Networking;
using IronGrind.ServerLogic.PartySystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.PartySystem
{
    /// <summary>
    /// EditMode tests for Party System Story 001: <see cref="PartySystemConfigValues"/> (AC-1, AC-2) and
    /// <see cref="PartyXpFormula"/> F-PS-1 (AC-3, AC-4).
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_ConfigAndFormula_Tests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in _created)
            {
                UnityEngine.Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private static string Inv(float value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Defaults with any subset of fields replaced.</summary>
        private static PartySystemConfigValues Make(
            int size = PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT,
            float rate = PartySystemConfigValues.XP_PARTY_DEDUCTION_RATE_DEFAULT,
            int gate = PartySystemConfigValues.LEVEL_RANGE_GATE_DEFAULT,
            float timeout = PartySystemConfigValues.INVITE_TIMEOUT_SECONDS_DEFAULT,
            float retry = PartySystemConfigValues.INVITE_RETRY_SECONDS_DEFAULT,
            float grace = PartySystemConfigValues.RECONNECT_GRACE_SECONDS_DEFAULT,
            float hp = PartySystemConfigValues.HP_DELTA_THRESHOLD_FRACTION_DEFAULT,
            float disband = PartySystemConfigValues.DISBAND_TIMEOUT_SECONDS_DEFAULT)
        {
            return new PartySystemConfigValues(size, rate, gate, timeout, retry, grace, hp, disband);
        }

        private static void ExpectRangeError(string knob, string value, string min, string max)
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                knob + " = " + value + " is outside [" + min + ", " + max + "].")));
        }

        private static void AssertFloatField(string knob, Func<float, PartySystemConfigValues> build,
            float min, float max, float below, float above)
        {
            Assert.IsTrue(build(min).Validate(), knob + " min edge");
            Assert.IsTrue(build(max).Validate(), knob + " max edge");

            ExpectRangeError(knob, Inv(below), Inv(min), Inv(max));
            Assert.IsFalse(build(below).Validate(), knob + " below");

            ExpectRangeError(knob, Inv(above), Inv(min), Inv(max));
            Assert.IsFalse(build(above).Validate(), knob + " above");

            ExpectRangeError(knob, "NaN", Inv(min), Inv(max));
            Assert.IsFalse(build(float.NaN).Validate(), knob + " NaN");
        }

        // ---- AC-1 ----

        [Test]
        public void test_partyConfig_defaults_equalGddValuesAndValidate()
        {
            PartySystemConfigValues v = PartySystemConfigValues.Defaults;

            Assert.IsTrue(v.Validate());
            Assert.AreEqual(4, v.MaxPartySize);
            Assert.AreEqual(0.10f, v.XpPartyDeductionRate);
            Assert.AreEqual(5, v.LevelRangeGate);
            Assert.AreEqual(60.0f, v.InviteTimeoutSeconds);
            Assert.AreEqual(5.0f, v.InviteRetrySeconds);
            Assert.AreEqual(5.0f, v.ReconnectGraceSeconds);
            Assert.AreEqual(0.05f, v.HpDeltaThresholdFraction);
            Assert.AreEqual(10.0f, v.DisbandTimeoutSeconds);
        }

        [Test]
        public void test_partyConfig_tickConversions_useTwentyHertz()
        {
            PartySystemConfigValues v = PartySystemConfigValues.Defaults;

            Assert.AreEqual(20, ServerTickLoop.TICK_RATE_HZ, "the expected tick counts below assume 20 Hz");
            Assert.AreEqual(1200, v.InviteTimeoutTicks);
            Assert.AreEqual(100, v.InviteRetryTicks);
            Assert.AreEqual(100, v.ReconnectGraceTicks);
            Assert.AreEqual(200, v.DisbandTimeoutTicks);
            Assert.AreEqual(1200, PartySystemConfigValues.ToTicks(60.0f));
            Assert.AreEqual(100, PartySystemConfigValues.ToTicks(5.0f));
            Assert.AreEqual(200, PartySystemConfigValues.ToTicks(10.0f));
        }

        [Test]
        public void test_partyConfig_freshAsset_fieldsEqualDefaults()
        {
            var asset = ScriptableObject.CreateInstance<PartySystemConfig>();
            _created.Add(asset);

            PartySystemConfigValues v = PartySystemConfigValues.FromAsset(asset);

            Assert.AreEqual(PartySystemConfigValues.Defaults.MaxPartySize, v.MaxPartySize);
            Assert.AreEqual(PartySystemConfigValues.Defaults.XpPartyDeductionRate, v.XpPartyDeductionRate);
            Assert.AreEqual(PartySystemConfigValues.Defaults.LevelRangeGate, v.LevelRangeGate);
            Assert.AreEqual(PartySystemConfigValues.Defaults.InviteTimeoutSeconds, v.InviteTimeoutSeconds);
            Assert.AreEqual(PartySystemConfigValues.Defaults.InviteRetrySeconds, v.InviteRetrySeconds);
            Assert.AreEqual(PartySystemConfigValues.Defaults.ReconnectGraceSeconds, v.ReconnectGraceSeconds);
            Assert.AreEqual(PartySystemConfigValues.Defaults.HpDeltaThresholdFraction, v.HpDeltaThresholdFraction);
            Assert.AreEqual(PartySystemConfigValues.Defaults.DisbandTimeoutSeconds, v.DisbandTimeoutSeconds);
            Assert.IsTrue(v.Validate());
        }

        [Test]
        public void test_partyConfig_fromAsset_copiesEveryField()
        {
            var asset = ScriptableObject.CreateInstance<PartySystemConfig>();
            _created.Add(asset);
            asset.SetForTests(Make(size: 3, rate: 0.15f, gate: 7, timeout: 90.0f, retry: 2.0f, grace: 12.0f,
                hp: 0.10f, disband: 20.0f));

            PartySystemConfigValues v = PartySystemConfigValues.FromAsset(asset);

            Assert.AreEqual(3, v.MaxPartySize);
            Assert.AreEqual(0.15f, v.XpPartyDeductionRate);
            Assert.AreEqual(7, v.LevelRangeGate);
            Assert.AreEqual(90.0f, v.InviteTimeoutSeconds);
            Assert.AreEqual(2.0f, v.InviteRetrySeconds);
            Assert.AreEqual(12.0f, v.ReconnectGraceSeconds);
            Assert.AreEqual(0.10f, v.HpDeltaThresholdFraction);
            Assert.AreEqual(20.0f, v.DisbandTimeoutSeconds);
        }

        [Test]
        public void test_partyConfig_fromAssetOutOfRange_copiedUnclampedThenRejected()
        {
            var asset = ScriptableObject.CreateInstance<PartySystemConfig>();
            _created.Add(asset);
            asset.SetForTests(Make(size: 6, rate: 0.30f));

            PartySystemConfigValues v = PartySystemConfigValues.FromAsset(asset);

            Assert.AreEqual(6, v.MaxPartySize, "size copied as authored, not clamped to 4");
            Assert.AreEqual(0.30f, v.XpPartyDeductionRate, "rate copied as authored, not clamped to 0.20");

            LogAssert.Expect(LogType.Error, new Regex(
                @"MAX_PARTY_SIZE = 6 is outside \[2, 4\]\..*4-slot wire schema"));
            ExpectRangeError("XP_PARTY_DEDUCTION_RATE", Inv(0.30f), Inv(0.00f), Inv(0.20f));
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "XP_PARTY_DEDUCTION_RATE x (MAX_PARTY_SIZE - 1) = " + Inv(0.30f * 5f) + " must be below 1.")));
            Assert.IsFalse(v.Validate());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_partyConfig_fromNullAsset_warnsAndReturnsDefaults()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no config asset; using defaults"));

            PartySystemConfigValues v = PartySystemConfigValues.FromAsset(null);

            Assert.AreEqual(PartySystemConfigValues.Defaults.MaxPartySize, v.MaxPartySize);
            Assert.AreEqual(PartySystemConfigValues.Defaults.InviteTimeoutSeconds, v.InviteTimeoutSeconds);
            Assert.IsTrue(v.Validate());
        }

        // ---- AC-2 ----

        [Test]
        public void test_partyConfig_maxPartySizeRange_edgesValidOutsideInvalid()
        {
            Assert.IsTrue(Make(size: 2).Validate());
            Assert.IsTrue(Make(size: 4).Validate());

            // Anchored at the end: the below-range message carries no wire-schema suffix.
            LogAssert.Expect(LogType.Error, new Regex(@"MAX_PARTY_SIZE = 1 is outside \[2, 4\]\.$"));
            Assert.IsFalse(Make(size: 1).Validate());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_partyConfig_maxPartySizeFive_errorNamesFourSlotWireSchema()
        {
            LogAssert.Expect(LogType.Error, new Regex(
                @"MAX_PARTY_SIZE = 5 is outside \[2, 4\]\..*4-slot wire schema"));

            Assert.IsFalse(Make(size: 5).Validate());
        }

        [Test]
        public void test_partyConfig_deductionRateRange_edgesValidOutsideInvalid()
        {
            AssertFloatField("XP_PARTY_DEDUCTION_RATE", r => Make(rate: r), 0.00f, 0.20f, -0.001f, 0.201f);
        }

        [Test]
        public void test_partyConfig_levelRangeGateRange_edgesValidOutsideInvalid()
        {
            Assert.IsTrue(Make(gate: 0).Validate());
            Assert.IsTrue(Make(gate: 15).Validate());

            ExpectRangeError("LEVEL_RANGE_GATE", "-1", "0", "15");
            Assert.IsFalse(Make(gate: -1).Validate());
            ExpectRangeError("LEVEL_RANGE_GATE", "16", "0", "15");
            Assert.IsFalse(Make(gate: 16).Validate());
        }

        [Test]
        public void test_partyConfig_inviteTimeoutRange_edgesValidOutsideInvalid()
        {
            AssertFloatField("INVITE_TIMEOUT_SECONDS", t => Make(timeout: t), 30.0f, 120.0f, 29.9f, 120.1f);
        }

        [Test]
        public void test_partyConfig_inviteRetryRange_edgesValidOutsideInvalid()
        {
            AssertFloatField("INVITE_RETRY_SECONDS", r => Make(retry: r), 1.0f, 10.0f, 0.9f, 10.1f);
        }

        [Test]
        public void test_partyConfig_reconnectGraceRange_edgesValidOutsideInvalid()
        {
            AssertFloatField("RECONNECT_GRACE_SECONDS", g => Make(grace: g), 2.0f, 30.0f, 1.9f, 30.1f);
        }

        [Test]
        public void test_partyConfig_hpDeltaThresholdRange_edgesValidOutsideInvalid()
        {
            AssertFloatField("HP_DELTA_THRESHOLD_FRACTION", h => Make(hp: h), 0.00f, 0.20f, -0.001f, 0.201f);
        }

        [Test]
        public void test_partyConfig_disbandTimeoutRange_edgesValidOutsideInvalid()
        {
            AssertFloatField("DISBAND_TIMEOUT_SECONDS", d => Make(disband: d), 5.0f, 30.0f, 4.9f, 30.1f);
        }

        [Test]
        public void test_partyConfig_retryEqualsTimeout_logsRetryRuleError()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "INVITE_RETRY_SECONDS = 60 must be below INVITE_TIMEOUT_SECONDS = 60.")));

            Assert.IsFalse(PartySystemConfigValues.CheckRetryBelowTimeout(60.0f, 60.0f));
        }

        [Test]
        public void test_partyConfig_retryAboveRangeAndTimeout_logsRangeErrorAndRetryRuleError()
        {
            ExpectRangeError("INVITE_RETRY_SECONDS", "60", "1", "10");
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "INVITE_RETRY_SECONDS = 60 must be below INVITE_TIMEOUT_SECONDS = 60.")));

            Assert.IsFalse(Make(timeout: 60.0f, retry: 60.0f).Validate());
        }

        [Test]
        public void test_partyConfig_retryAboveTimeout_helperLogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "INVITE_RETRY_SECONDS = 70 must be below INVITE_TIMEOUT_SECONDS = 60.")));

            Assert.IsFalse(PartySystemConfigValues.CheckRetryBelowTimeout(70.0f, 60.0f));
        }

        [Test]
        public void test_partyConfig_retryBelowTimeout_helperPasses()
        {
            Assert.IsTrue(PartySystemConfigValues.CheckRetryBelowTimeout(5.0f, 60.0f));
        }

        [Test]
        public void test_partyConfig_jointConstraintBreach_helperLogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "XP_PARTY_DEDUCTION_RATE x (MAX_PARTY_SIZE - 1) = 1 must be below 1.")));

            Assert.IsFalse(PartySystemConfigValues.CheckJointDeduction(0.5f, 3));
        }

        [Test]
        public void test_partyConfig_jointConstraintAtRangeMaxima_holds()
        {
            Assert.IsTrue(PartySystemConfigValues.CheckJointDeduction(
                PartySystemConfigValues.XP_PARTY_DEDUCTION_RATE_MAX, PartySystemConfigValues.MAX_PARTY_SIZE_MAX));
            Assert.IsTrue(Make(size: 4, rate: 0.20f).Validate());
        }

        [Test]
        public void test_partyConfig_jointConstraintJustBelowLimit_helperHolds()
        {
            Assert.IsTrue(PartySystemConfigValues.CheckJointDeduction(0.49f, 3));
        }

        [Test]
        public void test_partyConfig_defaultStruct_failsWithSixExactErrors()
        {
            // All-zero knobs: size, the four durations and the retry rule fail; rate, gate and HP delta 0 are valid,
            // and 0 x (0 - 1) passes the joint constraint.
            ExpectRangeError("MAX_PARTY_SIZE", "0", "2", "4");
            ExpectRangeError("INVITE_TIMEOUT_SECONDS", "0", "30", "120");
            ExpectRangeError("INVITE_RETRY_SECONDS", "0", "1", "10");
            ExpectRangeError("RECONNECT_GRACE_SECONDS", "0", "2", "30");
            ExpectRangeError("DISBAND_TIMEOUT_SECONDS", "0", "5", "30");
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "INVITE_RETRY_SECONDS = 0 must be below INVITE_TIMEOUT_SECONDS = 0.")));

            Assert.IsFalse(default(PartySystemConfigValues).Validate());
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-3 ----

        [TestCase(1000, 1, 0.10f, 1000)]
        [TestCase(1000, 2, 0.10f, 900)]
        [TestCase(1000, 4, 0.10f, 700)]
        [TestCase(200, 4, 0.10f, 140)]
        [TestCase(2000000, 4, 0.10f, 1400000)]
        [TestCase(5, 2, 0.10f, 4)]
        public void test_partyXpFormula_compute_matchesGddTable(int xpBase, int n, float rate, int expected)
        {
            Assert.AreEqual(expected, PartyXpFormula.Compute(xpBase, n, rate));
        }

        // ---- AC-4 ----

        [TestCase(1000, 0, 0.10f)]
        [TestCase(1000, -1, 0.10f)]
        [TestCase(0, 2, 0.10f)]
        [TestCase(-5, 2, 0.10f)]
        [TestCase(1000, 4, 0.60f)]
        public void test_partyXpFormula_compute_guardsReturnZero(int xpBase, int n, float rate)
        {
            Assert.AreEqual(0, PartyXpFormula.Compute(xpBase, n, rate));
        }

        [Test]
        public void test_partyXpFormula_multiplierExactlyZero_returnsZero()
        {
            // 1 - 0.5 x (3 - 1) = 0 exactly.
            Assert.AreEqual(0, PartyXpFormula.Compute(1000, 3, 0.5f));
        }

        [Test]
        public void test_partyXpFormula_nanRate_returnsZero()
        {
            Assert.AreEqual(0, PartyXpFormula.Compute(1000, 2, float.NaN));
        }

        [Test]
        public void test_partyXpFormula_negativeRate_returnsBaseUnchanged()
        {
            Assert.AreEqual(1000, PartyXpFormula.Compute(1000, 2, -0.10f));
        }

        [Test]
        public void test_partyXpFormula_intMaxBaseSolo_returnsBaseWithoutOverflow()
        {
            Assert.AreEqual(int.MaxValue, PartyXpFormula.Compute(int.MaxValue, 1, 0.10f));
        }

        [Test]
        public void test_partyXpFormula_intMaxBaseAtTwo_returnsFloatProduct()
        {
            // (float)int.MaxValue is 2^31 and 0.9f is 15099494 x 2^-24, so the product is exactly 15099494 x 2^7.
            Assert.AreEqual(1932735232, PartyXpFormula.Compute(int.MaxValue, 2, 0.10f));
        }
    }
}
