using NUnit.Framework;
using IronGrind.CharacterStats;

namespace IronGrind.Tests.EditMode.CharacterStats
{
    /// <summary>
    /// EditMode unit tests for the CharacterStats F-1 modifier stack — Story 002 acceptance criteria.
    /// Formula: EffectiveStat = clamp( (Base + ΣFlatEquip + ΣFlatBuff) × (1 + ΣPctEquip) × (1 + ΣPctBuff), StatMin, StatMax )
    /// Int-schema stats: result passed through Mathf.FloorToInt.
    /// Float-schema stats: result returned as clamped float directly.
    /// </summary>
    [TestFixture]
    public class CharacterStats_ModifierStack_Tests
    {
        private IronGrind.CharacterStats.CharacterStats _stats;
        private EntityID _player;
        private EntityID _mob;

        // Distinct item/buff IDs used across test methods.
        private static readonly ItemID Ring   = new ItemID(101u);
        private static readonly ItemID Amulet = new ItemID(102u);
        private static readonly ItemID Gloves = new ItemID(103u);
        private static readonly BuffID Haste  = (BuffID)201u;
        private static readonly BuffID Debuff = (BuffID)202u;

        [SetUp]
        public void SetUp()
        {
            _stats  = CharacterStatsFixture.Create();
            _player = CharacterStatsFixture.PlayerEntityId;
            _mob    = CharacterStatsFixture.MobEntityId;
        }

        // -------------------------------------------------------------------
        // AC-01: F-1 layer order — flat first, pct multiplicative, FloorToInt
        // (100+30+10) × 1.15 × 1.20 = 193.2 → 193
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStat_FlatAndPctBothLayers_FloorIntResult()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.AttackPower,
                new EquipmentModifierEntry(30f, 0.15f, Ring));
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.AttackPower,
                new BuffModifierEntry(10f, 0.20f, 100, Haste));

            // Act
            int result = _stats.GetEffectiveStat(_player, StatID.AttackPower);

            // Assert — (100+30+10) × 1.15 × 1.20 = 193.2 → FloorToInt = 193
            Assert.AreEqual(193, result,
                "F-1 formula: (base+flatEquip+flatBuff) × (1+pctEquip) × (1+pctBuff), FloorToInt.");

            // Reverse: swap equip/buff roles — result must be identical (inter-layer pct is commutative).
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.AttackPower,
                new EquipmentModifierEntry(10f, 0.20f, Gloves));
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.AttackPower,
                new BuffModifierEntry(30f, 0.15f, 100, Haste));

            int resultReversed = _stats.GetEffectiveStat(_player, StatID.AttackPower);
            Assert.AreEqual(193, resultReversed,
                "Swapping equip/buff modifier values must produce the same result — inter-layer order is commutative.");
        }

        // -------------------------------------------------------------------
        // AC-02: Intra-layer pct is additive (not compounding)
        // two equip ×10% → ΣPctEquip=0.20, not 1.10×1.10
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStat_IntraLayerPctIsAdditive_NotCompounding()
        {
            // Arrange — two equip entries each +10% pct, no flat, no buffs
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.AttackPower,
                new EquipmentModifierEntry(0f, 0.10f, Ring),
                new EquipmentModifierEntry(0f, 0.10f, Amulet));

            // Act
            int result = _stats.GetEffectiveStat(_player, StatID.AttackPower);

            // Assert — 100 × (1+0.10+0.10) × 1.0 = 120, NOT 121 (compounding)
            Assert.AreEqual(120, result,
                "Two +10% equip entries must sum to ΣPctEquip=0.20, not compound to 1.10×1.10=1.21.");

            // Three equip entries each +10% → 130, not 133.1
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.AttackPower,
                new EquipmentModifierEntry(0f, 0.10f, Ring),
                new EquipmentModifierEntry(0f, 0.10f, Amulet),
                new EquipmentModifierEntry(0f, 0.10f, Gloves));

            int resultThree = _stats.GetEffectiveStat(_player, StatID.AttackPower);
            Assert.AreEqual(130, resultThree,
                "Three +10% equip entries must sum to ΣPctEquip=0.30, not compound to ≈133.");
        }

        // -------------------------------------------------------------------
        // AC-03: Float stat clamped at StatMax; no FloorToInt
        // (0.05+0.35+0.40)×1.0×1.0 = 0.80 → clamped to StatMax(CritChance)=0.75
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStatFloat_CritChance_CappedAtStatMax()
        {
            // Arrange — base + equip flat + buff flat exceeds StatMax(CritChance)=0.75
            CharacterStatsFixture.SetFloatBaseStat(_stats, _player, StatID.CritChance, 0.05f);
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.CritChance,
                new EquipmentModifierEntry(0.35f, 0f, Ring));
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.CritChance,
                new BuffModifierEntry(0.40f, 0f, 100, Haste));

            // Act
            float result = _stats.GetEffectiveStatFloat(_player, StatID.CritChance);

            // Assert — pre-clamp 0.80 → clamped to 0.75; no FloorToInt
            Assert.AreEqual(0.75f, result, 1e-5f,
                "CritChance effective 0.80 must be clamped to StatMax=0.75. No FloorToInt on float-schema stats.");

            // Edge: at-cap — 0.74 + buff 0.01 → result within tolerance of 0.75
            CharacterStatsFixture.SetFloatBaseStat(_stats, _player, StatID.CritChance, 0.74f);
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.CritChance);
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.CritChance,
                new BuffModifierEntry(0.01f, 0f, 100, Haste));
            float atCap = _stats.GetEffectiveStatFloat(_player, StatID.CritChance);
            Assert.AreEqual(0.75f, atCap, 1e-5f, "0.74 + buff 0.01 must reach the 0.75 cap.");

            // Edge: over-cap — 0.74 + buff 0.02 → still 0.75 (clamped)
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.CritChance,
                new BuffModifierEntry(0.02f, 0f, 100, Haste));
            float overCap = _stats.GetEffectiveStatFloat(_player, StatID.CritChance);
            Assert.AreEqual(0.75f, overCap, 1e-5f, "0.74 + buff 0.02 exceeds cap; result must still be 0.75.");
        }

        // -------------------------------------------------------------------
        // AC-04: Negative flat debuff clamped at StatMin
        // (50+30-200)×1.0×1.0 = -120 → clamped to StatMin(AttackPower)=1
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStat_NegativeFlatDebuff_ClampedAtStatMin()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 50);
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.AttackPower,
                new EquipmentModifierEntry(30f, 0f, Ring));
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.AttackPower,
                new BuffModifierEntry(-200f, 0f, 100, Debuff));

            // Act
            int result = _stats.GetEffectiveStat(_player, StatID.AttackPower);

            // Assert — pre-clamp -120 → clamped to StatMin(AP)=1
            Assert.AreEqual(1, result,
                "Debuff of -200 must drive effective AP to pre-clamp=-120, clamped to StatMin=1. A return of 0 or negative is a failure.");

            // Edge: debuff that brings pre-clamp exactly to 1 → assert 1
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.AttackPower,
                new BuffModifierEntry(-79f, 0f, 100, Debuff));
            int atFloor = _stats.GetEffectiveStat(_player, StatID.AttackPower);
            Assert.AreEqual(1, atFloor, "Pre-clamp exactly 1 must return 1.");

            // Edge: debuff brings pre-clamp to 0 → clamped to StatMin=1
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.AttackPower,
                new BuffModifierEntry(-80f, 0f, 100, Debuff));
            int atZero = _stats.GetEffectiveStat(_player, StatID.AttackPower);
            Assert.AreEqual(1, atZero, "Pre-clamp of 0 must be clamped to StatMin=1, not returned as 0.");
        }

        // -------------------------------------------------------------------
        // AC-05: Pct debuff cannot produce ≤0 value on float stat
        // 5.0 × (1 - 1.20) × 1.0 = -1.0 → clamped to StatMin(MovementSpeed)=0.5
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStatFloat_PctDebuff_ClampedAtStatMin()
        {
            // Arrange
            CharacterStatsFixture.SetFloatBaseStat(_stats, _player, StatID.MovementSpeed, 5.0f);
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.MovementSpeed,
                new BuffModifierEntry(0f, -1.20f, 100, Debuff));

            // Act
            float result = _stats.GetEffectiveStatFloat(_player, StatID.MovementSpeed);

            // Assert — pre-clamp 5.0×(1-1.20)=-1.0 → clamped to StatMin=0.5
            Assert.AreEqual(0.5f, result, 1e-5f,
                "PctBuff=-1.20 drives effective to -1.0; must be clamped to StatMin(MovementSpeed)=0.5. Any value ≤0 is a failure.");

            // Edge: PctBuff=-1.0 → 5.0×0.0=0.0 → clamped to 0.5
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.MovementSpeed,
                new BuffModifierEntry(0f, -1.0f, 100, Debuff));
            float atZero = _stats.GetEffectiveStatFloat(_player, StatID.MovementSpeed);
            Assert.AreEqual(0.5f, atZero, 1e-5f, "PctBuff=-1.0 produces effective=0.0; must clamp to 0.5.");

            // Edge: PctBuff=-0.99 → 5.0×0.01=0.05 → clamped to 0.5
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.MovementSpeed,
                new BuffModifierEntry(0f, -0.99f, 100, Debuff));
            float nearZero = _stats.GetEffectiveStatFloat(_player, StatID.MovementSpeed);
            Assert.AreEqual(0.5f, nearZero, 1e-5f, "PctBuff=-0.99 produces effective=0.05; must clamp to StatMin=0.5.");
        }

        // -------------------------------------------------------------------
        // AC-24: No cap hysteresis on float stat
        // Prior cap state must not persist after modifiers are cleared.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStatFloat_NoCapsHysteresis_AfterModifierClear()
        {
            // Arrange — install modifiers that push past StatMax(CritChance)=0.75
            CharacterStatsFixture.SetFloatBaseStat(_stats, _player, StatID.CritChance, 0.30f);
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.CritChance,
                new EquipmentModifierEntry(0.50f, 0f, Ring));

            // Act & Assert step 1 — verify cap is active
            float capped = _stats.GetEffectiveStatFloat(_player, StatID.CritChance);
            Assert.AreEqual(0.75f, capped, 1e-5f, "Pre-condition: 0.30+0.50=0.80 must be capped at 0.75.");

            // Act: clear all modifiers
            CharacterStatsFixture.ClearModifiers(_stats, _player);

            // Assert step 2 — cap state must not persist; base stat unchanged
            float afterClear = _stats.GetEffectiveStatFloat(_player, StatID.CritChance);
            Assert.AreEqual(0.30f, afterClear, 1e-5f,
                "After clearing modifiers, CritChance must return base=0.30f with no memory of the prior cap.");

            // Assert step 3 — install different modifiers and verify normal (uncapped) result
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.CritChance,
                new EquipmentModifierEntry(0.30f, 0f, Amulet));
            float notCapped = _stats.GetEffectiveStatFloat(_player, StatID.CritChance);
            Assert.AreEqual(0.60f, notCapped, 1e-5f,
                "After reinstalling equip flat=0.30, result must be 0.60 — not capped and not hysteretic.");
        }

        // -------------------------------------------------------------------
        // AC-28a: AttackSpeedMultiplier clamped at StatMax (2.0)
        // 1.414 + equip flat 1.0 = 2.414 → clamped to 2.0
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStatFloat_AttackSpeedMultiplier_CappedAtStatMax()
        {
            // Arrange
            CharacterStatsFixture.SetFloatBaseStat(_stats, _player, StatID.AttackSpeedMultiplier, 1.414f);
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.AttackSpeedMultiplier,
                new EquipmentModifierEntry(1.0f, 0f, Ring));

            // Act
            float result = _stats.GetEffectiveStatFloat(_player, StatID.AttackSpeedMultiplier);

            // Assert — pre-clamp 2.414 → clamped to StatMax=2.0
            Assert.AreEqual(2.0f, result, 1e-5f,
                "AttackSpeedMultiplier 1.414+1.0=2.414 must be clamped to StatMax=2.0.");

            // Edge: equip flat brings result exactly to StatMax — at-cap, still 2.0
            CharacterStatsFixture.SetEquipmentModifiers(_stats, _player, StatID.AttackSpeedMultiplier,
                new EquipmentModifierEntry(0.586f, 0f, Amulet));
            float atCap = _stats.GetEffectiveStatFloat(_player, StatID.AttackSpeedMultiplier);
            Assert.AreEqual(2.0f, atCap, 1e-5f, "1.414+0.586=2.000 is at-cap; must return 2.0f.");
        }

        // -------------------------------------------------------------------
        // AC-28b: AttackSpeedMultiplier clamped at StatMin (0.5)
        // 1.03 + buff flat -0.70 = 0.33 → clamped to StatMin=0.5
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStatFloat_AttackSpeedMultiplier_ClampedAtStatMin()
        {
            // Arrange
            CharacterStatsFixture.SetFloatBaseStat(_stats, _player, StatID.AttackSpeedMultiplier, 1.03f);
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.AttackSpeedMultiplier,
                new BuffModifierEntry(-0.70f, 0f, 100, Debuff));

            // Act
            float result = _stats.GetEffectiveStatFloat(_player, StatID.AttackSpeedMultiplier);

            // Assert — pre-clamp 0.33 → clamped to StatMin=0.5
            Assert.AreEqual(0.5f, result, 1e-5f,
                "AttackSpeedMultiplier 1.03-0.70=0.33 must be clamped to StatMin=0.5.");

            // Edge: buff flat brings result exactly to StatMin — at-floor, still 0.5
            CharacterStatsFixture.SetBuffModifiers(_stats, _player, StatID.AttackSpeedMultiplier,
                new BuffModifierEntry(-0.53f, 0f, 100, Debuff));
            float atFloor = _stats.GetEffectiveStatFloat(_player, StatID.AttackSpeedMultiplier);
            Assert.AreEqual(0.5f, atFloor, 1e-5f, "1.03-0.53=0.50 is at-floor; must return 0.5f.");
        }

        // -------------------------------------------------------------------
        // AC-21 (GetEffectiveStat portion): Player-only stats return 0 on mob
        // No modifiers; absent-stat guard must fire for all 7 player-only fields.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStat_MobPlayerOnlyStats_ReturnZero()
        {
            // Arrange — mob has only derived stats written, as in a data-table load.
            // No player-only fields written. No modifier arrays installed.
            _stats.SetBaseStat(_mob, StatID.MaxHP,        500);
            _stats.SetBaseStat(_mob, StatID.AttackPower,   75);
            _stats.SetBaseStat(_mob, StatID.Defense,       30);

            // Act & Assert — all 7 player-only int-schema fields must return 0; no exception thrown.
            // STR/DEX/VIT/INT have StatMin=1, but the absent-stat guard prevents StatMin
            // from incorrectly elevating an unset stat to 1.
            Assert.AreEqual(0, _stats.GetEffectiveStat(_mob, StatID.Strength),
                "Strength is player-only; mob must return 0 from GetEffectiveStat.");
            Assert.AreEqual(0, _stats.GetEffectiveStat(_mob, StatID.Dexterity),
                "Dexterity is player-only; mob must return 0 from GetEffectiveStat.");
            Assert.AreEqual(0, _stats.GetEffectiveStat(_mob, StatID.Vitality),
                "Vitality is player-only; mob must return 0 from GetEffectiveStat.");
            Assert.AreEqual(0, _stats.GetEffectiveStat(_mob, StatID.Intelligence),
                "Intelligence is player-only; mob must return 0 from GetEffectiveStat.");
            Assert.AreEqual(0, _stats.GetEffectiveStat(_mob, StatID.MaxMP),
                "MaxMP is player-only; mob must return 0 from GetEffectiveStat.");
            Assert.AreEqual(0, _stats.GetEffectiveStat(_mob, StatID.CurrentMP),
                "CurrentMP is player-only; mob must return 0 from GetEffectiveStat.");
            Assert.AreEqual(0, _stats.GetEffectiveStat(_mob, StatID.Experience),
                "Experience is player-only; mob must return 0 from GetEffectiveStat.");
        }

        // -------------------------------------------------------------------
        // AC-30 (GetEffectiveStat portion): Mob base stat round-trip through formula
        // No modifiers → formula is neutral; result equals BaseStat.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStat_MobAttackPower_RoundTripThroughFormula()
        {
            // Arrange — mob with AttackPower=75, no modifier arrays installed
            _stats.SetBaseStat(_mob, StatID.AttackPower, 75);

            // Act
            int result = _stats.GetEffectiveStat(_mob, StatID.AttackPower);

            // Assert — 75 × 1.0 × 1.0 = 75.0; clamp [1, 99999] is a no-op; FloorToInt = 75
            Assert.AreEqual(75, result,
                "No modifier layers must leave the mob base stat unchanged through the formula.");
        }

        // -------------------------------------------------------------------
        // AC-26: Experience — GetBaseStat == GetEffectiveStat (no modifier effect)
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetEffectiveStat_Experience_EqualsGetBaseStat()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.Experience, 3500);

            // Act
            int baseStat      = _stats.GetBaseStat(_player, StatID.Experience);
            int effectiveStat = _stats.GetEffectiveStat(_player, StatID.Experience);

            // Assert — 3500 × 1.0 × 1.0 = 3500; clamp [0, 999999] is a no-op
            Assert.AreEqual(3500, baseStat,      "GetBaseStat(Experience) must return stored value.");
            Assert.AreEqual(3500, effectiveStat, "GetEffectiveStat(Experience) must equal GetBaseStat when no modifiers are present.");

            // Edge: base = 0 → absent-stat guard fires → both return 0
            _stats.SetBaseStat(_player, StatID.Experience, 0);
            Assert.AreEqual(0, _stats.GetBaseStat(_player, StatID.Experience),      "GetBaseStat(Experience=0) must return 0.");
            Assert.AreEqual(0, _stats.GetEffectiveStat(_player, StatID.Experience), "GetEffectiveStat(Experience=0) must return 0.");

            // Edge: base = 999999 (StatMax) → no clamping; both return 999999
            _stats.SetBaseStat(_player, StatID.Experience, 999999);
            Assert.AreEqual(999999, _stats.GetBaseStat(_player, StatID.Experience),      "GetBaseStat(Experience=999999) must return 999999.");
            Assert.AreEqual(999999, _stats.GetEffectiveStat(_player, StatID.Experience), "GetEffectiveStat(Experience=999999) must return 999999; StatMax=999999 is a no-op.");
        }
    }
}
