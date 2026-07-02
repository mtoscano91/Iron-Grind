using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;

namespace IronGrind.Tests.EditMode.CharacterStats
{
    /// <summary>
    /// EditMode unit tests for CharacterStats resource pool lifecycle — Story 004 acceptance criteria.
    /// Covers CurrentHP/CurrentMP write-lock, MaxHP reconciliation, ApplyDamage, ApplyRegen,
    /// ConsumeMana, ApplyManaRegen, death boundary, and float precision.
    /// </summary>
    [TestFixture]
    public class CharacterStats_ResourcePool_Tests
    {
        private IronGrind.CharacterStats.CharacterStats _stats;
        private EntityID _player;

        private static readonly ItemID EquipItem01 = new ItemID(401u);
        private static readonly BuffID TestBuff01  = (BuffID)501u;

        [SetUp]
        public void SetUp()
        {
            _stats  = CharacterStatsFixture.Create();
            _player = CharacterStatsFixture.PlayerEntityId;
        }

        // -------------------------------------------------------------------
        // AC-06: Write-locked stats (CurrentHP, CurrentMP, Level, Experience)
        // reject AddBuffModifier. All four must log an error and remain unchanged.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_AddBuffModifier_CurrentHP_WriteLockedReturnsError()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxHP, 1000);
            _stats.SetBaseStat(_player, StatID.MaxMP, 200);
            _stats.SetBaseStat(_player, StatID.Level, 5);
            _stats.SetBaseStat(_player, StatID.Experience, 1000);
            _stats.ApplyRegen(_player, 500f);     // CurrentHP = 500f
            _stats.ApplyManaRegen(_player, 100f); // CurrentMP = 100f

            // Act & Assert — CurrentHP
            LogAssert.Expect(LogType.Error, new Regex("write-locked"));
            _stats.AddBuffModifier(_player, StatID.CurrentHP,
                new BuffModifierEntry(100f, 0f, 10, TestBuff01));
            Assert.AreEqual(500f, _stats.GetCurrentHP(_player), 0.0f,
                "CurrentHP must remain 500f after AddBuffModifier was rejected by write-lock.");

            // CurrentMP
            LogAssert.Expect(LogType.Error, new Regex("write-locked"));
            _stats.AddBuffModifier(_player, StatID.CurrentMP,
                new BuffModifierEntry(100f, 0f, 10, TestBuff01));
            Assert.AreEqual(100f, _stats.GetCurrentMP(_player), 0.0f,
                "CurrentMP must remain 100f after AddBuffModifier was rejected by write-lock.");

            // Level
            LogAssert.Expect(LogType.Error, new Regex("write-locked"));
            _stats.AddBuffModifier(_player, StatID.Level,
                new BuffModifierEntry(5f, 0f, 10, TestBuff01));
            Assert.AreEqual(5, _stats.GetBaseStat(_player, StatID.Level),
                "Level must remain 5 after AddBuffModifier was rejected by write-lock.");

            // Experience
            LogAssert.Expect(LogType.Error, new Regex("write-locked"));
            _stats.AddBuffModifier(_player, StatID.Experience,
                new BuffModifierEntry(100f, 0f, 10, TestBuff01));
            Assert.AreEqual(1000, _stats.GetBaseStat(_player, StatID.Experience),
                "Experience must remain 1000 after AddBuffModifier was rejected by write-lock.");
        }

        // -------------------------------------------------------------------
        // AC-07: RemoveEquipmentModifier reducing MaxHP clamps CurrentHP
        // synchronously in the same call — entity not dead.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_RemoveEquipModifier_MaxHpDecrease_ClampsCurrentHpSameCall()
        {
            // Arrange — base MaxHP=2000, equip +1000 flat → effective MaxHP=3000
            _stats.SetBaseStat(_player, StatID.MaxHP, 2000);
            _stats.AddEquipmentModifier(_player, StatID.MaxHP,
                new EquipmentModifierEntry(1000f, 0f, EquipItem01));
            _stats.ApplyRegen(_player, 2500f); // CurrentHP = 2500f (< 3000 cap)

            Assert.AreEqual(2500f, _stats.GetCurrentHP(_player), 0.0f,
                "Precondition: CurrentHP must be 2500f before equip removal.");
            Assert.AreEqual(3000, _stats.GetEffectiveStat(_player, StatID.MaxHP),
                "Precondition: effective MaxHP must be 3000 with equip modifier active.");

            // Subscribe before act — MaxHP clamp must NOT fire OnEntityDied (AC-07: entity not dead)
            int diedCount = 0;
            _stats.Subscribe(_ => diedCount++);

            // Act
            _stats.RemoveEquipmentModifier(_player, StatID.MaxHP, EquipItem01);

            // Assert — CurrentHP immediately clamped to new MaxHP=2000; entity not dead
            Assert.AreEqual(2000f, _stats.GetCurrentHP(_player), 0.0f,
                "CurrentHP must be clamped to 2000f in the same RemoveEquipmentModifier call — not 2500f (unreconciled).");
            Assert.AreEqual(2000, _stats.GetEffectiveStat(_player, StatID.MaxHP),
                "Effective MaxHP must be 2000 after equip removal.");
            Assert.AreEqual(0, diedCount,
                "OnEntityDied must NOT fire when CurrentHP is clamped by MaxHP reduction — entity is not dead, just capped.");
        }

        // AC-07 edge: removing equip when CurrentHP already equals the new (lower) MaxHP.
        // The reconciliation guard uses > (not >=) so an exact match must not clamp further.
        [Test]
        public void CharacterStats_RemoveEquipModifier_MaxHpDecrease_ExactBoundary_NoFurtherClamp()
        {
            // Arrange — base MaxHP=2000, equip +1000 → effective MaxHP=3000; CurrentHP = 2000 (= post-removal MaxHP)
            _stats.SetBaseStat(_player, StatID.MaxHP, 2000);
            _stats.AddEquipmentModifier(_player, StatID.MaxHP,
                new EquipmentModifierEntry(1000f, 0f, EquipItem01));
            _stats.ApplyRegen(_player, 2000f); // CurrentHP = 2000f exactly (< 3000 cap)

            Assert.AreEqual(2000f, _stats.GetCurrentHP(_player), 0.0f,
                "Precondition: CurrentHP=2000f (equals the post-removal MaxHP).");

            int diedCount = 0;
            _stats.Subscribe(_ => diedCount++);

            // Act
            _stats.RemoveEquipmentModifier(_player, StatID.MaxHP, EquipItem01);

            // Assert — hp == newMaxHp (not hp > newMaxHp), so the guard must NOT reduce further
            Assert.AreEqual(2000f, _stats.GetCurrentHP(_player), 0.0f,
                "CurrentHP must remain 2000f — it already equals the new MaxHP and must not be reduced.");
            Assert.AreEqual(2000, _stats.GetEffectiveStat(_player, StatID.MaxHP),
                "Effective MaxHP must be 2000 after equip removal.");
            Assert.AreEqual(0, diedCount,
                "OnEntityDied must not fire on exact-boundary case.");
        }

        // -------------------------------------------------------------------
        // AC-08: SetBaseStat MaxHP increase grants no free HP.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_SetBaseStat_MaxHpIncrease_DoesNotGrantFreeHp()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxHP, 1000);
            _stats.ApplyRegen(_player, 800f); // CurrentHP = 800f

            Assert.AreEqual(800f, _stats.GetCurrentHP(_player), 0.0f,
                "Precondition: CurrentHP must be 800f.");

            // Act — increase MaxHP; CurrentHP must stay unchanged
            _stats.SetBaseStat(_player, StatID.MaxHP, 1200);

            // Assert
            Assert.AreEqual(800f, _stats.GetCurrentHP(_player), 0.0f,
                "CurrentHP must remain 800f — MaxHP increase grants no free HP. Only ApplyRegen may increase CurrentHP.");
            Assert.AreEqual(1200, _stats.GetEffectiveStat(_player, StatID.MaxHP),
                "Effective MaxHP must now be 1200.");
        }

        // -------------------------------------------------------------------
        // AC-09: ApplyDamage exact kill fires OnEntityDied exactly once.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_ApplyDamage_ExactKill_FiresOnEntityDiedOnce()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxHP, 1000);
            _stats.ApplyRegen(_player, 50f); // CurrentHP = 50f
            int diedCount = 0;
            _stats.Subscribe(_ => diedCount++);

            // Act
            _stats.ApplyDamage(_player, 50f);

            // Assert
            Assert.AreEqual(0f, _stats.GetCurrentHP(_player), 0.0f,
                "CurrentHP must be exactly 0f after exact-damage kill.");
            Assert.AreEqual(1, diedCount,
                "OnEntityDied must fire exactly once on exact-damage kill — not zero, not twice.");
        }

        // -------------------------------------------------------------------
        // AC-10: Overkill damage clamps at 0; OnEntityDied fires exactly once.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_ApplyDamage_Overkill_ClampsAt0_FiresOnEntityDiedOnce()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxHP, 1000);
            _stats.ApplyRegen(_player, 30f); // CurrentHP = 30f
            int diedCount = 0;
            _stats.Subscribe(_ => diedCount++);

            // Act — massive overkill
            _stats.ApplyDamage(_player, 500f);

            // Assert
            Assert.AreEqual(0f, _stats.GetCurrentHP(_player), 0.0f,
                "CurrentHP must be 0f after overkill — no negative HP stored.");
            Assert.AreEqual(1, diedCount,
                "OnEntityDied must fire exactly once on overkill — not zero, not twice.");
        }

        // -------------------------------------------------------------------
        // AC-10b: ApplyDamage on dead entity (CurrentHP=0) is a no-op;
        // OnEntityDied does not re-fire.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_ApplyDamage_DeadEntity_IsNoopEventNotRefired()
        {
            // Arrange — kill the entity first (before subscribing so setup kill is not counted)
            _stats.SetBaseStat(_player, StatID.MaxHP, 1000);
            _stats.ApplyRegen(_player, 50f);
            _stats.ApplyDamage(_player, 50f); // entity dies; no subscriber yet

            Assert.AreEqual(0f, _stats.GetCurrentHP(_player), 0.0f,
                "Precondition: entity must be dead (CurrentHP=0f).");

            // Now subscribe — any re-fire would increment this counter
            int diedCount = 0;
            _stats.Subscribe(_ => diedCount++);

            // Act — apply damage three times to dead entity (QA spec requires triple-call to verify no state drift)
            _stats.ApplyDamage(_player, 100f);
            _stats.ApplyDamage(_player, 100f);
            _stats.ApplyDamage(_player, 100f);

            // Assert
            Assert.AreEqual(0f, _stats.GetCurrentHP(_player), 0.0f,
                "CurrentHP must remain 0f — ApplyDamage on dead entity is a no-op.");
            Assert.AreEqual(0, diedCount,
                "OnEntityDied must NOT re-fire when entity is already dead.");
        }

        // -------------------------------------------------------------------
        // AC-11: ConsumeMana returns false when mana is insufficient; no write.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_ConsumeMana_InsufficientMana_ReturnsFalseNoWrite()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxMP, 200);
            _stats.ApplyManaRegen(_player, 40f); // CurrentMP = 40f

            // Act
            bool result = _stats.ConsumeMana(_player, 100f);

            // Assert
            Assert.IsFalse(result,
                "ConsumeMana must return false when CurrentMP (40f) < cost (100f).");
            Assert.AreEqual(40f, _stats.GetCurrentMP(_player), 0.0f,
                "CurrentMP must remain 40f — no write when mana is insufficient.");
        }

        // -------------------------------------------------------------------
        // AC-12: ConsumeMana succeeds and subtracts exactly the cost.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_ConsumeMana_SufficientMana_ReturnsTrueSubtracts()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxMP, 200);
            _stats.ApplyManaRegen(_player, 150f); // CurrentMP = 150f

            // Act
            bool result = _stats.ConsumeMana(_player, 100f);

            // Assert
            Assert.IsTrue(result,
                "ConsumeMana must return true when CurrentMP (150f) >= cost (100f).");
            Assert.AreEqual(50f, _stats.GetCurrentMP(_player), 0.0f,
                "CurrentMP must be exactly 50f after consuming 100f from 150f.");
        }

        // -------------------------------------------------------------------
        // AC-11/AC-12 boundary: ConsumeMana with cost == CurrentMP exactly.
        // Validates the < guard (not <=) and that exact drain stores 0.0f.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_ConsumeMana_ExactDrain_ReturnsTrueZeroRemaining()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxMP, 200);
            _stats.ApplyManaRegen(_player, 100f); // CurrentMP = 100f

            // Act — cost == CurrentMP exactly
            bool result = _stats.ConsumeMana(_player, 100f);

            // Assert
            Assert.IsTrue(result,
                "ConsumeMana must return true when cost == CurrentMP exactly (guard uses <, not <=).");
            Assert.AreEqual(0f, _stats.GetCurrentMP(_player), 0.0f,
                "CurrentMP must be exactly 0f after exact drain — not negative, no float artifact.");
        }

        // -------------------------------------------------------------------
        // AC-12b: ApplyRegen preserves IEEE 754 float precision.
        // 50.25f + 0.75f = 51.0f exactly. Assert delta = 0.0f.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_ApplyRegen_FloatPrecision_ExactRepresentableValues()
        {
            // Arrange — 50.25f is exactly representable (50 + 1/4)
            _stats.SetBaseStat(_player, StatID.MaxHP, 100);
            _stats.ApplyRegen(_player, 50.25f); // CurrentHP = 50.25f

            Assert.AreEqual(50.25f, _stats.GetCurrentHP(_player), 0.0f,
                "Precondition: CurrentHP must be exactly 50.25f.");

            // Act — 0.75f is exactly representable; sum 50.25f + 0.75f = 51.0f exactly
            _stats.ApplyRegen(_player, 0.75f);

            // Assert — delta = 0.0f: no epsilon tolerance
            Assert.AreEqual(51.0f, _stats.GetCurrentHP(_player), 0.0f,
                "50.25f + 0.75f must equal exactly 51.0f (both IEEE 754 representable). " +
                "Do NOT use 50.3f + 0.7f — their sum is ~50.9999f on ARM IL2CPP.");
        }

        // -------------------------------------------------------------------
        // AC-12c: ApplyRegen clamps at MaxHP; no overflow.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_ApplyRegen_ClampsAtMaxHp_NoOverflow()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxHP, 100);
            _stats.ApplyRegen(_player, 99.5f); // CurrentHP = 99.5f

            // Act — regen would bring HP to 104.5f without clamping
            _stats.ApplyRegen(_player, 5.0f);

            // Assert
            Assert.AreEqual(100f, _stats.GetCurrentHP(_player), 0.0f,
                "CurrentHP must be clamped at MaxHP=100f — not 104.5f, no exception.");
        }

        // -------------------------------------------------------------------
        // AC-12d: ApplyManaRegen preserves IEEE 754 float precision.
        // 50.25f + 0.75f = 51.0f exactly.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_ApplyManaRegen_FloatPrecision_ExactRepresentableValues()
        {
            // Arrange — 50.25f is exactly representable
            _stats.SetBaseStat(_player, StatID.MaxMP, 200);
            _stats.ApplyManaRegen(_player, 50.25f); // CurrentMP = 50.25f

            Assert.AreEqual(50.25f, _stats.GetCurrentMP(_player), 0.0f,
                "Precondition: CurrentMP must be exactly 50.25f.");

            // Act
            _stats.ApplyManaRegen(_player, 0.75f);

            // Assert — delta = 0.0f
            Assert.AreEqual(51.0f, _stats.GetCurrentMP(_player), 0.0f,
                "50.25f + 0.75f must equal exactly 51.0f (both IEEE 754 representable).");
        }

        // -------------------------------------------------------------------
        // AC-12e: ApplyManaRegen clamps at MaxMP; no overflow.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_ApplyManaRegen_ClampsAtMaxMp_NoOverflow()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxMP, 200);
            _stats.ApplyManaRegen(_player, 195f); // CurrentMP = 195f

            // Act — regen would bring MP to 205f without clamping
            _stats.ApplyManaRegen(_player, 10f);

            // Assert
            Assert.AreEqual(200f, _stats.GetCurrentMP(_player), 0.0f,
                "CurrentMP must be clamped at MaxMP=200f — not 205f, no exception.");
        }
    }
}
