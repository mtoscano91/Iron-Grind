using NUnit.Framework;
using IronGrind.CharacterStats;

namespace IronGrind.Tests.EditMode.CharacterStats
{
    /// <summary>
    /// EditMode unit tests for the CharacterStats modifier lifecycle — Story 003 acceptance criteria.
    /// Covers AddBuffModifier, AddEquipmentModifier, RemoveBuffModifier, RemoveEquipmentModifier,
    /// idempotency, duplicate-ID overwrite, write-lock rejection, and capacity overflow.
    /// </summary>
    [TestFixture]
    public class CharacterStats_ModifierLifecycle_Tests
    {
        private IronGrind.CharacterStats.CharacterStats _stats;
        private EntityID _player;
        private EntityID _mob;

        // Item IDs — distinct from Story 002 to avoid cross-test coupling.
        private static readonly ItemID HeavyArmor  = new ItemID(201u);
        private static readonly ItemID Sword01     = new ItemID(202u);
        private static readonly ItemID RingOfSpeed = new ItemID(203u);

        // Buff IDs
        private static readonly BuffID WarriorCry = (BuffID)301u;
        private static readonly BuffID Poison     = (BuffID)302u;

        [SetUp]
        public void SetUp()
        {
            _stats  = CharacterStatsFixture.Create();
            _player = CharacterStatsFixture.PlayerEntityId;
            _mob    = CharacterStatsFixture.MobEntityId;
        }

        // -------------------------------------------------------------------
        // AC-16: Buff add → remove recomputes from scratch; no undo-delta stored.
        // Base=150, +50 flat WarriorCry → 200. Remove → 150.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_AddBuffModifier_ThenRemove_RecomputesFromScratch()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 150);

            // Act — add
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(50f, 0f, 60, WarriorCry));

            // Assert — 150 + 50 = 200
            Assert.AreEqual(200, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "Base=150, +50 flat buff must yield EffectiveStat=200.");

            // Act — remove
            _stats.RemoveBuffModifier(_player, StatID.AttackPower, WarriorCry);

            // Assert — recomputes from scratch: no undo-delta, no cached intermediate
            Assert.AreEqual(150, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "After removing WarriorCry, GetEffectiveStat must return base=150. F-1 recomputes from scratch.");
        }

        // -------------------------------------------------------------------
        // AC-16 edge: two buffs active; remove one; only the remaining buff's
        // contribution persists. Distinguishes targeted swap-erase from a full-clear.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_AddBuffModifier_MultipleBuffs_RemoveOne_OnlyRemainingPersists()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);

            // Act — add two distinct buffs
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(30f, 0f, 10, WarriorCry));
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(20f, 0f, 10, Poison));

            Assert.AreEqual(150, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "Base=100 + WarriorCry(+30) + Poison(+20) must yield 150.");

            // Act — remove WarriorCry only; Poison must remain active
            _stats.RemoveBuffModifier(_player, StatID.AttackPower, WarriorCry);

            // Assert — swap-erase must target only the specified buff ID
            Assert.AreEqual(120, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "After removing WarriorCry, only Poison(+20) remains: 100+20=120. " +
                "Swap-erase must target the specified BuffID, not clear all buffs.");
        }

        // -------------------------------------------------------------------
        // AC-17: Duplicate BuffID → overwrite; no double-stack.
        // Base=100, +30 WarriorCry → 130. Add again → still 130. Third add → still 130.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_AddBuffModifier_DuplicateBuffID_OverwritesNeverDoubleStacks()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);

            // Act — first add
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(30f, 0f, 20, WarriorCry));
            Assert.AreEqual(130, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "First AddBuffModifier(+30, 20 ticks) → must be 130.");

            // Act — second add (same BuffID, different duration)
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(30f, 0f, 40, WarriorCry));
            Assert.AreEqual(130, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "Second AddBuffModifier with same BuffID must overwrite, not stack — result must still be 130, not 160.");

            // Edge: third add (same BuffID, yet another duration)
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(30f, 0f, 60, WarriorCry));
            Assert.AreEqual(130, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "Third AddBuffModifier with same BuffID must still overwrite — result must be 130, not 190.");
        }

        // -------------------------------------------------------------------
        // AC-18: Duplicate ItemID → overwrite; no double-count.
        // Base=100, +40 Sword01 → 140. Add again → still 140.
        // Overwrite with new value (+60) → 160, not 200.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_AddEquipmentModifier_DuplicateItemID_OverwritesNeverDoubleCounts()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);

            // Act — first add
            _stats.AddEquipmentModifier(_player, StatID.AttackPower,
                new EquipmentModifierEntry(40f, 0f, Sword01));
            Assert.AreEqual(140, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "First AddEquipmentModifier(+40) → must be 140.");

            // Act — second add with same ItemID and same value
            _stats.AddEquipmentModifier(_player, StatID.AttackPower,
                new EquipmentModifierEntry(40f, 0f, Sword01));
            Assert.AreEqual(140, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "Second AddEquipmentModifier with same ItemID must overwrite, not double-count — result must still be 140, not 180.");

            // Edge: overwrite with a different flat value
            _stats.AddEquipmentModifier(_player, StatID.AttackPower,
                new EquipmentModifierEntry(60f, 0f, Sword01));
            Assert.AreEqual(160, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "Overwrite Sword01 with new flat=60 → must be 160, not 200. Old flat=40 must not persist.");
        }

        // -------------------------------------------------------------------
        // AC-19: RemoveEquipmentModifier / RemoveBuffModifier for non-existent ID
        // → no exception; stat unchanged.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_RemoveEquipmentModifier_NonExistentID_NoopNoException()
        {
            // Arrange — base stat set; no modifiers ever added
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);

            // Act & Assert — remove on entity with no modifier data at all
            Assert.DoesNotThrow(
                () => _stats.RemoveEquipmentModifier(_player, StatID.AttackPower, Sword01),
                "RemoveEquipmentModifier for a non-existent entity-stat entry must not throw.");

            Assert.AreEqual(100, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "Stat must be unchanged after a no-op remove.");

            // Add one modifier, then attempt to remove a different ItemID
            _stats.AddEquipmentModifier(_player, StatID.AttackPower,
                new EquipmentModifierEntry(40f, 0f, HeavyArmor));

            Assert.DoesNotThrow(
                () => _stats.RemoveEquipmentModifier(_player, StatID.AttackPower, Sword01),
                "RemoveEquipmentModifier for a present-but-mismatched ItemID must not throw.");

            Assert.AreEqual(140, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "HeavyArmor must remain active; only the non-existent Sword01 was targeted for removal.");

            // Edge: call remove twice for the same (already-absent) ID
            Assert.DoesNotThrow(
                () => _stats.RemoveEquipmentModifier(_player, StatID.AttackPower, Sword01),
                "Second remove of non-existent ID must also not throw.");
        }

        // -------------------------------------------------------------------
        // AC-20: Equipment with flat+pct; remove→restore; floor precision.
        // Base=50, HeavyArmor(+80 flat, +0.12 pct) → floor((50+80)×1.12) = floor(145.6) = 145.
        // Remove → 50. Re-add → 145 again. floor(145.6) must be 145, not 146.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_AddEquipmentModifier_FlatAndPct_RemoveRestoresBase_FloorPrecision()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.Defense, 50);

            // Act — add HeavyArmor with flat and pct
            _stats.AddEquipmentModifier(_player, StatID.Defense,
                new EquipmentModifierEntry(80f, 0.12f, HeavyArmor));

            int withArmor = _stats.GetEffectiveStat(_player, StatID.Defense);

            // Assert — (50+80)×1.12 = 145.6 → floor = 145, not 146
            Assert.AreEqual(145, withArmor,
                "Defense: (50+80)×1.12 = 145.6 → Mathf.FloorToInt must return 145, not 146.");
            Assert.AreNotEqual(146, withArmor,
                "floor(145.6) must be 145. A result of 146 indicates RoundToInt or ceiling is being used instead of FloorToInt.");

            // Act — remove (flat AND pct removed atomically)
            _stats.RemoveEquipmentModifier(_player, StatID.Defense, HeavyArmor);

            Assert.AreEqual(50, _stats.GetEffectiveStat(_player, StatID.Defense),
                "After removing HeavyArmor, Defense must return to base=50. Both flat and pct must be removed atomically.");

            // Edge: re-add must yield same result (no residual state)
            _stats.AddEquipmentModifier(_player, StatID.Defense,
                new EquipmentModifierEntry(80f, 0.12f, HeavyArmor));
            Assert.AreEqual(145, _stats.GetEffectiveStat(_player, StatID.Defense),
                "Re-adding HeavyArmor must yield 145 again — no residual state from the prior remove.");
        }

        // -------------------------------------------------------------------
        // AC-22 [ADVISORY]: Mob entity buff modifier applies F-1 identically.
        // Mob AP=80, Poison(-20 flat) → 60. Debuff below StatMin clamps to 1.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_AddBuffModifier_MobEntity_AppliesF1Identically()
        {
            // Arrange
            _stats.SetBaseStat(_mob, StatID.AttackPower, 80);

            // Act — apply a debuff (negative flat) to mob
            _stats.AddBuffModifier(_mob, StatID.AttackPower,
                new BuffModifierEntry(-20f, 0f, 30, Poison));

            // Assert — F-1 formula applied identically for mob: (80-20)×1.0×1.0 = 60
            Assert.AreEqual(60, _stats.GetEffectiveStat(_mob, StatID.AttackPower),
                "Mob AP=80 with Poison(-20 flat) → F-1 must yield 60.");

            // Edge: large debuff drives pre-clamp below StatMin(AP)=1 → clamps to 1
            _stats.RemoveBuffModifier(_mob, StatID.AttackPower, Poison);
            _stats.AddBuffModifier(_mob, StatID.AttackPower,
                new BuffModifierEntry(-9999f, 0f, 30, Poison));

            Assert.AreEqual(1, _stats.GetEffectiveStat(_mob, StatID.AttackPower),
                "Debuff driving pre-clamp below StatMin(AP)=1 must clamp to 1. StatMin is applied for mob entities.");
        }

        // -------------------------------------------------------------------
        // Capacity overflow [BLOCKING]: 16 equipment modifiers (capacity full),
        // 17th attempt → no exception; GetEffectiveStat reflects only 16.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_AddEquipmentModifier_AtCapacity_17thModifierRejectedNoException()
        {
            // Arrange — base stat set to non-zero so formula is fully applied
            _stats.SetBaseStat(_player, StatID.AttackPower, 10);

            // Fill to capacity: 16 distinct ItemIDs, each contributing +10 flat
            for (uint i = 1; i <= 16; i++)
            {
                _stats.AddEquipmentModifier(_player, StatID.AttackPower,
                    new EquipmentModifierEntry(10f, 0f, new ItemID(1000u + i)));
            }

            // Verify capacity is full: base=10, 16×10 flat = 160 total flat, effective = 170
            // StatMin(AP)=1 and StatMax(AP)=99999 — no clamping at 170
            Assert.AreEqual(170, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "base=10 + 16 modifiers × +10 flat: effective must be 170 before overflow attempt.");

            // Act — attempt 17th modifier (distinct ItemID to bypass the overwrite path)
            Assert.DoesNotThrow(
                () => _stats.AddEquipmentModifier(_player, StatID.AttackPower,
                    new EquipmentModifierEntry(10f, 0f, new ItemID(1017u))),
                "Adding a 17th modifier at capacity must not throw an exception.");

            // Assert — only 16 modifiers active; 17th was silently dropped
            Assert.AreEqual(170, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "After overflow rejection, GetEffectiveStat must still reflect only 16 modifiers (170). The 17th must not have been added.");
        }
    }
}
