using System.Collections.Generic;
using System.Reflection;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using UnityEngine;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 012 (Party XP Detriment Integration).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>F-LS-2 (a +30% party XP bonus) is SUPERSEDED and is deliberately NOT implemented
    /// anywhere in this file.</b> The canonical formula is <c>party-system.md</c>'s
    /// <c>F-PS-1_PartyXpDeduction</c> (a DETRIMENT, opposite direction from F-LS-2):
    /// <c>XP_member = Mathf.RoundToInt(Mathf.Max(0f, XP_base * (1.0f - 0.10f * (N_eligible - 1))))</c>.
    /// </para>
    /// <para>
    /// The real Party System epic does not exist yet in this codebase. Per CR-1.5, the Leveling
    /// System is party-unaware — it only ever receives a final, pre-computed, already-detriment-
    /// adjusted <c>int</c> XP amount via <see cref="IronGrind.CharacterStats.CharacterStats.AddExperience"/>.
    /// This story proves that boundary contract via a TEST-LOCAL stand-in of F-PS-1
    /// (<see cref="ComputePartyXpDeduction_TestOnly"/>, not production code — see its doc comment)
    /// feeding the real <c>AddExperience</c>, plus a reflection-based structural guard (EC-LS-32)
    /// that <c>AddExperience</c> never grows a party-size parameter. Story 001's
    /// <c>LevelingSystem_XpAccumulation_tests.cs</c> already covers <c>AddExperience</c>'s own
    /// accumulation/threshold logic — out of scope here.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class LevelingSystem_PartyXpDetrimentIntegration_Tests
    {
        // ---------------------------------------------------------------
        // TEST-ONLY F-PS-1 stand-in. NOT production code — the real F-PS-1
        // implementation belongs to the (not-yet-created) Party System epic and
        // will live in that epic's own production code, not here. This exists
        // solely to produce a pre-computed, already-detriment-adjusted XP amount
        // to hand to the real CharacterStats.AddExperience boundary under test,
        // mirroring this codebase's established mock-provider-for-forward-
        // dependencies pattern.
        // ---------------------------------------------------------------

        /// <summary>
        /// TEST-ONLY stand-in for party-system.md's F-PS-1_PartyXpDeduction formula
        /// (design/gdd/party-system.md line 141). Not production code.
        /// </summary>
        private static int ComputePartyXpDeduction_TestOnly(int xpBase, int nEligible)
        {
            // 0.10f is party-system.md's XP_PARTY_DEDUCTION_RATE constant (line 141), inlined
            // here since this is a test-local stand-in, not the production constant reference.
            return Mathf.RoundToInt(Mathf.Max(0f, xpBase * (1.0f - 0.10f * (nEligible - 1))));
        }

        /// <summary>
        /// Performs the full ADR-010 wiring sequence required to break the CharacterStats
        /// &lt;-&gt; ILevelingService constructor cycle, mirroring Story 001's
        /// <c>LevelingSystem_XpAccumulation_tests.cs</c> CreateWiredStats helper exactly. This
        /// story doesn't touch level-ups, classes, or tier multipliers, so no additional wiring
        /// (class registry, tier tables) is needed beyond this minimal setup.
        /// </summary>
        private static (IronGrind.CharacterStats.CharacterStats stats, LevelingService leveling) CreateWiredStats(
            IReadOnlyList<int> xpThresholds)
        {
            var leveling = new LevelingService(xpThresholds);
            var stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            leveling.AttachCharacterStats(stats);
            return (stats, leveling);
        }

        // ---------------------------------------------------------------
        // AC-LS-33 — F-PS-1 test-local stand-in multipliers
        // ---------------------------------------------------------------

        [Test]
        public void ComputePartyXpDeduction_TestOnly_AtPartySizesOneToFour_MatchesExpectedMultipliers()
        {
            // Arrange
            const int xpBase = 1000;

            // Act
            int resultN1 = ComputePartyXpDeduction_TestOnly(xpBase, 1);
            int resultN2 = ComputePartyXpDeduction_TestOnly(xpBase, 2);
            int resultN3 = ComputePartyXpDeduction_TestOnly(xpBase, 3);
            int resultN4 = ComputePartyXpDeduction_TestOnly(xpBase, 4);

            // Assert — N=1 -> x1.00, N=2 -> x0.90, N=3 -> x0.80, N=4 -> x0.70
            Assert.AreEqual(1000, resultN1, "N=1 must apply no detriment (x1.00).");
            Assert.AreEqual(900, resultN2, "N=2 must apply x0.90.");
            Assert.AreEqual(800, resultN3, "N=3 must apply x0.80.");
            Assert.AreEqual(700, resultN4, "N=4 must apply x0.70.");
        }

        // ---------------------------------------------------------------
        // AC-LS-33 — float-rounding edge case (Mathf.RoundToInt, not truncation)
        // ---------------------------------------------------------------

        [Test]
        public void ComputePartyXpDeduction_TestOnly_XpBase333AtN4_RoundsToTwoThreeThree()
        {
            // Arrange — 333 * (1.0 - 0.10*3) = 333 * 0.7 = 233.1 -> Mathf.RoundToInt(233.1f) = 233.
            // Note: this specific case does not distinguish Mathf.RoundToInt from a truncating
            // (int) cast, since both floor(233.1) and round(233.1) equal 233 — the story's own
            // worked example (leveling-system.md EC-LS-33) uses this exact case, so that is
            // accepted here per the story's Implementation Notes. Mathf.RoundToInt's actual
            // tie-breaking semantics (round-half-to-even / "banker's rounding", matching
            // System.Math.Round's default MidpointRounding.ToEven, per Unity's documented
            // behavior) are not exercised by this non-tied value and are not independently
            // re-verified here, since no live Unity Editor is available this session to execute
            // a confirming test — see the story report for this caveat.
            const int xpBase = 333;
            const int nEligible = 4;

            // Act
            int result = ComputePartyXpDeduction_TestOnly(xpBase, nEligible);

            // Assert
            Assert.AreEqual(233, result,
                "333 at N=4 must round to 233 via Mathf.RoundToInt, matching the story's worked example.");
        }

        // ---------------------------------------------------------------
        // AC-LS-33 — a genuinely round-vs-truncate-distinguishing case (code-review suggestion,
        // qa-tester, Story 012). The story's own 333@N=4 worked example above does NOT distinguish
        // Mathf.RoundToInt from a truncating (int) cast (both give 233) — this case does.
        // ---------------------------------------------------------------

        [Test]
        public void ComputePartyXpDeduction_TestOnly_XpBase1001AtN4_DistinguishesRoundingFromTruncation()
        {
            // Arrange — 1001 * (1.0 - 0.10*3) = 1001 * 0.7 = 700.7. Mathf.RoundToInt(700.7f) = 701;
            // a truncating (int)700.7f cast would give 700. This fractional part (0.7) is well
            // clear of any 0.5 midpoint, so the divergence is not sensitive to float-representation
            // edge effects near a tie.
            const int xpBase = 1001;
            const int nEligible = 4;

            // Act
            int result = ComputePartyXpDeduction_TestOnly(xpBase, nEligible);

            // Assert
            Assert.AreEqual(701, result,
                "1001 at N=4 must round UP to 701 via Mathf.RoundToInt — a truncating cast would " +
                "incorrectly give 700, proving this stand-in (and by extension the documented " +
                "contract on the real F-PS-1 implementation) genuinely rounds rather than truncates.");
        }

        // ---------------------------------------------------------------
        // AC-LS-33 — the stand-in's output feeding the real AddExperience boundary
        // ---------------------------------------------------------------

        [Test]
        public void AddExperience_GivenPartyXpDeductionStandInOutput_IncreasesExperienceByExactAmount()
        {
            // Arrange — thresholds sized high enough that the grant below never crosses one;
            // AC-LS-33's final leg only cares that AddExperience accepts the stand-in's
            // pre-computed amount and accumulates it, not about level-up/threshold behaviour
            // (already covered by Story 001).
            var xpThresholds = new int[] { 0, 100, 200, 300, 400, 500, 100000 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);

            stats.SetBaseStat(entity, StatID.Level, 5);
            stats.SetBaseStat(entity, StatID.Experience, 5000);

            int partyAdjustedAmount = ComputePartyXpDeduction_TestOnly(333, 4); // 233

            // Code-review addition (qa-tester, Story 012): independently verify the Arrange
            // comment's claim ("thresholds sized high enough that the grant never crosses one")
            // via a zero-firing assertion, matching this epic's established idiom (see
            // LevelingSystem_LevelCapBehavior_tests.cs / LevelingSystem_SpawnPersistenceLoad_tests.cs)
            // rather than trusting the Experience delta alone to prove no level-up occurred.
            int levelUpCount = 0;
            leveling.OnLevelUp += _ => levelUpCount++;

            // Act
            stats.AddExperience(entity, partyAdjustedAmount);

            // Assert
            Assert.AreEqual(233, partyAdjustedAmount,
                "Sanity check — the stand-in must still produce 233 for this call.");
            Assert.AreEqual(5233, stats.GetBaseStat(entity, StatID.Experience),
                "Experience must increase by exactly the stand-in's pre-computed, already-" +
                "detriment-adjusted amount (233), from a baseline of 5000, with no exception.");
            Assert.AreEqual(0, levelUpCount,
                "OnLevelUp must never fire — the grant must not cross xpThresholds[6]=100000.");
        }

        // ---------------------------------------------------------------
        // EC-LS-32 — AddExperience has no party-size parameter anywhere in its signature
        // ---------------------------------------------------------------

        [Test]
        public void AddExperience_PublicSignature_HasNoPartySizeParameter()
        {
            // Arrange
            MethodInfo method = typeof(IronGrind.CharacterStats.CharacterStats).GetMethod("AddExperience");

            // Act
            Assert.IsNotNull(method,
                "CharacterStats.AddExperience must exist as a public method.");
            ParameterInfo[] parameters = method.GetParameters();

            // Assert — exactly 2 parameters, neither name nor type related to party size.
            Assert.AreEqual(2, parameters.Length,
                "AddExperience must have exactly 2 parameters — a party-size parameter would " +
                "violate CR-1.5 (Leveling System is party-unaware).");

            // Code-review adjustment (qa-tester, Story 012): type-only checks — the real EC-LS-32
            // regression guard is the substring loop below, which catches any party-related
            // rename/addition. Asserting exact parameter NAMES here would also fail on a harmless,
            // unrelated rename (e.g. amount -> xpAmount) that introduces no CR-1.5 violation at all.
            Assert.AreEqual(typeof(EntityID), parameters[0].ParameterType,
                "First parameter must be of type EntityID.");
            Assert.AreEqual(typeof(int), parameters[1].ParameterType,
                "Second parameter must be of type System.Int32 — a pre-computed, already-" +
                "detriment-adjusted XP grant, not a party size or member count.");

            foreach (ParameterInfo parameter in parameters)
            {
                string lowerName = parameter.Name.ToLowerInvariant();
                Assert.IsFalse(lowerName.Contains("party"),
                    $"Parameter '{parameter.Name}' must not reference 'party' — AddExperience must stay party-unaware (CR-1.5).");
                Assert.IsFalse(lowerName == "n" || lowerName == "neligible" || lowerName.Contains("membercount"),
                    $"Parameter '{parameter.Name}' must not reference a party member count — AddExperience must stay party-unaware (CR-1.5).");
            }
        }
    }
}
