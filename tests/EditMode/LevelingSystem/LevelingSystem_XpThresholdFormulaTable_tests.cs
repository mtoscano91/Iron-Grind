using System;
using IronGrind.LevelingSystem;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 010 (XP Threshold Formula &amp; Table — F-LS-1,
    /// <c>design/gdd/leveling-system.md</c>). Covers AC-LS-31 (cumulative <c>XpThreshold</c> spot
    /// checks plus a build-time double-precision verification of the full shipped array) and
    /// AC-LS-32 (strict monotonic increase, L1-L59).
    /// </summary>
    /// <remarks>
    /// <para><b>AC-LS-31's "verified against a reference table at build time" requirement</b> is
    /// implemented here as a unit test, since this project has no separate build-time codegen
    /// step: <see cref="XpThreshold_FullTableIndependentlyRecomputed_MatchesShippedArrayExactly"/>
    /// recomputes every one of the 60 real entries from scratch, in <see langword="double"/>
    /// precision, directly from F-LS-1's formula and constants (<c>System.Math</c>, not
    /// <c>UnityEngine.Mathf</c>, and not by reading <see cref="XpThresholdTable"/> itself for
    /// anything but the final comparison) and asserts index-by-index equality against the shipped
    /// <see cref="XpThresholdTable.Values"/> literal array. This is independent of — and a second
    /// pass beyond — the Node.js double-precision verification (two methods: direct
    /// exponentiation and the <c>exp(ln(x)*n)</c> identity) already performed before this table
    /// was handed to this story for implementation.</para>
    /// <para>Out of scope (per the story): wiring <see cref="XpThresholdTable"/> into
    /// <see cref="LevelingService"/>'s constructor call site — Stories 002/003/008/011 all
    /// continue to supply their own test-local fake tables, unaffected by this file.</para>
    /// </remarks>
    [TestFixture]
    internal sealed class LevelingSystem_XpThresholdFormulaTable_Tests
    {
        // F-LS-1 constants (design/gdd/leveling-system.md) — duplicated here deliberately, NOT
        // read from XpThresholdTable or LevelingService, so this test recomputes independently
        // rather than trivially asserting the shipped array equals itself.
        private const double C = 178.6;
        private const double Alpha = 0.648;
        private const double R = 1.1198;

        // ---------------------------------------------------------------
        // AC-LS-31 — named spot checks against the corrected, exact reference values (the story's
        // original XpThreshold[20]~=62,728 approximation was corrected to the exact 65824 on
        // 2026-09-24 — see F-LS-1/EC-LS-35 in the GDD and TD-038).
        // ---------------------------------------------------------------

        [Test]
        public void XpThreshold_SpotChecks_MatchExactReferenceValues()
        {
            Assert.AreEqual(0, XpThresholdTable.Values[1], "XpThreshold[1] must be 0 — start of L1, no XP accumulated yet.");
            Assert.AreEqual(200, XpThresholdTable.Values[2], "XpThreshold[2] must be exactly 200 (= XP(1)).");
            Assert.AreEqual(65824, XpThresholdTable.Values[20], "XpThreshold[20] must be exactly 65824 (corrected 2026-09-24 from the stale ~62,728 approximation — see F-LS-1/EC-LS-35 and TD-038).");
            Assert.AreEqual(int.MaxValue, XpThresholdTable.Values[61], "XpThreshold[61] must be the CR-5.3 sentinel, int.MaxValue.");
        }

        // ---------------------------------------------------------------
        // AC-LS-31 — array shape guard: exactly 62 entries (index 0 unused, 1-60 real, 61 =
        // sentinel). Without this, a stray trailing entry appended past index 61 would be
        // invisible to every other test in this file, since none of them iterate past 61 or
        // check Count.
        // ---------------------------------------------------------------

        [Test]
        public void XpThreshold_ArrayLength_IsExactly62Entries()
        {
            Assert.AreEqual(62, XpThresholdTable.Values.Count, "XpThresholdTable.Values must have exactly 62 entries (index 0 unused, 1-60 real cumulative values, 61 = int.MaxValue sentinel).");
        }

        // ---------------------------------------------------------------
        // AC-LS-31 — build-time verification: independently recompute the FULL array (indices
        // 1-60; index 61 is the separately-authored sentinel, not formula-derived per EC-LS-35)
        // in double precision, directly from F-LS-1, and assert exact equality against the
        // shipped literal array, index by index.
        // ---------------------------------------------------------------

        [Test]
        public void XpThreshold_FullTableIndependentlyRecomputed_MatchesShippedArrayExactly()
        {
            // Independently recompute XpThreshold[1..60] in double precision, straight from
            // F-LS-1: XP(L) = round(C * L^Alpha * R^L) is the PER-LEVEL cost; XpThreshold[L] is
            // the CUMULATIVE sum of XP(i) for i in [1, L-1]. XpThreshold[61] is the sentinel and
            // is asserted separately below, not recomputed from the formula.
            var recomputed = new int[62];
            double cumulative = 0.0;
            for (int level = 1; level <= 60; level++)
            {
                recomputed[level] = (int)Math.Round(cumulative, MidpointRounding.AwayFromZero);

                if (level < 60)
                {
                    double xpForThisLevel = Math.Round(
                        C * Math.Pow(level, Alpha) * Math.Pow(R, level),
                        MidpointRounding.AwayFromZero);
                    cumulative += xpForThisLevel;
                }
            }

            for (int level = 1; level <= 60; level++)
            {
                Assert.AreEqual(
                    recomputed[level],
                    XpThresholdTable.Values[level],
                    $"XpThreshold[{level}]: shipped literal array must match the independent double-precision F-LS-1 recomputation exactly (this is the AC-LS-31 build-time reference-table verification).");
            }

            // Sentinel — separately authored (EC-LS-35), asserted directly rather than recomputed.
            Assert.AreEqual(int.MaxValue, XpThresholdTable.Values[61], "XpThreshold[61] sentinel must be int.MaxValue.");
        }

        // ---------------------------------------------------------------
        // AC-LS-32 — strict monotonic increase, XpThreshold[i] < XpThreshold[i+1] for all i in
        // [1, 58].
        // ---------------------------------------------------------------

        [Test]
        public void XpThreshold_StrictlyMonotonicIncreasing_AcrossAllLevelsBelowCap()
        {
            for (int i = 1; i <= 58; i++)
            {
                Assert.Less(
                    XpThresholdTable.Values[i],
                    XpThresholdTable.Values[i + 1],
                    $"XpThreshold[{i}] must be strictly less than XpThreshold[{i + 1}] (AC-LS-32).");
            }
        }
    }
}
