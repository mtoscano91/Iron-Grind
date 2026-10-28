using System;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>
    /// EditMode unit tests for Status Effects Story 002 - ApplyEffect validation
    /// (CR-SE-2, CR-SE-3, F-SE-1, F-SE-2, F-SE-3, EC-SE-2, EC-SE-6; AC-SE-03/04/06/07/08/10/17).
    /// </summary>
    [TestFixture]
    internal sealed class StatusEffects_Validation_Tests
    {
        private const uint TARGET_RAW_ID = 21u;
        private const uint OTHER_RAW_ID = 22u;
        private const uint UNKNOWN_RAW_ID = 99u;
        private const uint CASTER_RAW_ID = 31u;
        private const int TARGET_SLOT = 3;
        private const int OTHER_SLOT = 4;
        private const uint VALID_DURATION = 60u;
        private const uint FIRST_FILL_BUFF_RAW_ID = 100u;
        private const uint NEW_BUFF_RAW_ID = 500u;
        private const int ROW_LIMIT = StatusEffectsConstants.MAX_ACTIVE_BUFFS_PER_ENTITY;
        private const int CAPACITY = StatusEffectsConstants.MODIFIER_CAPACITY_PER_ENTITY;
        private const float FLAT = 5f;
        private const float PCT = 0.1f;
        private const float REGEN = 1f;

        private static readonly EntityID Target = new EntityID(TARGET_RAW_ID);
        private static readonly EntityID Other = new EntityID(OTHER_RAW_ID);
        private static readonly EntityID Unknown = new EntityID(UNKNOWN_RAW_ID);
        private static readonly EntityID Caster = new EntityID(CASTER_RAW_ID);
        private static readonly BuffID NewBuff = (BuffID)NEW_BUFF_RAW_ID;

        private static readonly StatID[] DistinctStatPool =
        {
            StatID.Strength, StatID.Dexterity, StatID.Vitality, StatID.Intelligence, StatID.AttackPower,
            StatID.Defense, StatID.MagicDefense, StatID.MaxHP, StatID.MaxMP
        };

        private RecordingBuffModifierLayer _layer;
        private DictionaryEntitySlotIndex _slots;
        private StatusEffectsService _service;

        [SetUp]
        public void SetUp()
        {
            _layer = new RecordingBuffModifierLayer();
            _slots = new DictionaryEntitySlotIndex();
            _slots.Add(Target, TARGET_SLOT);
            _slots.Add(Other, OTHER_SLOT);
            _service = new StatusEffectsService(_layer, _layer, _slots);
        }

        // ---- Helpers ----

        private static BuffDefinition Def(BuffID id, int modifierCount, uint duration = VALID_DURATION,
            float hpRegen = 0f, float mpRegen = 0f, StatID stat = StatID.Strength)
        {
            // Story 003 (CR-SE-2) rejects repeated stats, so slot 0 uses `stat` and the rest take distinct
            // permitted stats from the pool (skipping `stat`).
            Assert.LessOrEqual(modifierCount, DistinctStatPool.Length, "Def helper needs a larger DistinctStatPool");
            var mods = new StatModifier[modifierCount];
            int pool = 0;
            for (int i = 0; i < modifierCount; i++)
            {
                StatID slotStat = stat;
                if (i > 0)
                {
                    if (DistinctStatPool[pool] == stat) pool++;
                    slotStat = DistinctStatPool[pool++];
                }
                mods[i] = new StatModifier(slotStat, FLAT, PCT);
            }
            return new BuffDefinition(id, mods, duration, hpRegen, mpRegen);
        }

        private static BuffDefinition DefWithStats(params StatID[] stats)
        {
            var mods = new StatModifier[stats.Length];
            for (int i = 0; i < stats.Length; i++)
                mods[i] = new StatModifier(stats[i], FLAT, PCT);
            return new BuffDefinition(NewBuff, mods, VALID_DURATION, 0f, 0f);
        }

        /// <summary>Applies one buff per entry (distinct BuffIDs from 100) and expects Success.</summary>
        private void Fill(EntityID entity, params int[] modifierCounts)
        {
            for (int i = 0; i < modifierCounts.Length; i++)
            {
                BuffDefinition def = Def((BuffID)(FIRST_FILL_BUFF_RAW_ID + (uint)i), modifierCounts[i]);
                Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(entity, Caster, def), "fill " + i);
            }
        }

        private static int Sum(int[] values)
        {
            int total = 0;
            for (int i = 0; i < values.Length; i++)
                total += values[i];
            return total;
        }

        private void AssertRejected(BuffDefinition def, ApplyResult expected)
        {
            int rowsBefore = _service.GetActiveEffectCount(Target);
            int occupiedBefore = _service.GetOccupiedSlots(Target);
            ActiveEffect[] filledBefore = SnapshotFilledRows(Target);
            int layerCallsBefore = _layer.TotalCalls;
            bool newBuffBefore = _service.TryGetActiveEffect(Target, NewBuff, out ActiveEffect newBuffRowBefore);

            ApplyResult actual = _service.ApplyEffect(Target, Caster, def);

            Assert.AreEqual(expected, actual);
            Assert.AreEqual(rowsBefore, _service.GetActiveEffectCount(Target), "row count changed");
            Assert.AreEqual(occupiedBefore, _service.GetOccupiedSlots(Target), "OccupiedSlots changed");
            Assert.AreEqual(layerCallsBefore, _layer.TotalCalls, "layer was called by a rejected apply");
            AssertSameRows(filledBefore, SnapshotFilledRows(Target));
            Assert.AreEqual(newBuffBefore, _service.TryGetActiveEffect(Target, NewBuff, out ActiveEffect newBuffRowAfter),
                "NewBuff row presence changed");
            if (newBuffBefore)
                AssertSameRow(newBuffRowBefore, newBuffRowAfter, "NewBuff");
        }

        /// <summary>The rows <see cref="Fill"/> creates (BuffIDs from 100), default where absent.</summary>
        private ActiveEffect[] SnapshotFilledRows(EntityID entity)
        {
            var rows = new ActiveEffect[ROW_LIMIT];
            for (int i = 0; i < ROW_LIMIT; i++)
                _service.TryGetActiveEffect(entity, (BuffID)(FIRST_FILL_BUFF_RAW_ID + (uint)i), out rows[i]);
            return rows;
        }

        private static void AssertSameRows(ActiveEffect[] before, ActiveEffect[] after)
        {
            for (int i = 0; i < before.Length; i++)
                AssertSameRow(before[i], after[i], "fill row " + i);
        }

        private static void AssertSameRow(ActiveEffect before, ActiveEffect after, string label)
        {
            Assert.AreEqual(before.BuffId, after.BuffId, label + " BuffId changed");
            Assert.AreEqual(before.ModifierCount, after.ModifierCount, label + " ModifierCount changed");
            Assert.AreEqual(before.TicksRemaining, after.TicksRemaining, label + " TicksRemaining changed");
            Assert.AreEqual(before.CasterEntityId, after.CasterEntityId, label + " caster changed");
        }

        // ---- Constructor ----

        [Test]
        public void StatusEffects_Validation_Constructor_NullModifierLayerThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new StatusEffectsService(null, _layer, _slots));
        }

        [Test]
        public void StatusEffects_Validation_Constructor_NullResourceSinkThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new StatusEffectsService(_layer, null, _slots));
        }

        [Test]
        public void StatusEffects_Validation_Constructor_NullSlotIndexThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new StatusEffectsService(_layer, _layer, null));
        }

        // ---- Check order (EC-SE-6) ----

        [Test]
        public void StatusEffects_Validation_Order_NoOpShapedAndDeadTarget_ReturnsNoOpDefinition()
        {
            _service.MarkEntityDead(Target);

            AssertRejected(Def(NewBuff, 0), ApplyResult.NoOpDefinition);
        }

        [Test]
        public void StatusEffects_Validation_Order_InvalidDurationAndInvalidStat_ReturnsInvalidDuration()
        {
            AssertRejected(Def(NewBuff, 1, 0u, stat: StatID.Level), ApplyResult.InvalidDuration);
        }

        [Test]
        public void StatusEffects_Validation_Order_InvalidStatAndDeadTarget_ReturnsInvalidStat()
        {
            _service.MarkEntityDead(Target);

            AssertRejected(Def(NewBuff, 1, stat: StatID.Experience), ApplyResult.InvalidStat);
        }

        [Test]
        public void StatusEffects_Validation_Order_DeadTargetWithRowsFull_ReturnsInvalidTarget()
        {
            Fill(Target, 1, 1, 1, 1, 1, 1, 1, 1);
            _service.MarkEntityDead(Target);

            AssertRejected(Def(NewBuff, 1), ApplyResult.InvalidTarget);
        }

        [Test]
        public void StatusEffects_Validation_Order_RowsFullAndCapacityExceeded_ReturnsEffectSlotsFull()
        {
            Fill(Target, 4, 4, 4, 4, 4, 4, 4, 4); // 8 rows, 32 occupied; one more modifier also exceeds capacity

            AssertRejected(Def(NewBuff, 1), ApplyResult.EffectSlotsFull);
        }

        [Test]
        public void StatusEffects_Validation_Order_NoOpShapedAndInvalidDuration_ReturnsNoOpDefinition()
        {
            AssertRejected(Def(NewBuff, 0, 0u), ApplyResult.NoOpDefinition);
        }

        [Test]
        public void StatusEffects_Validation_Order_UnknownTargetWithNoOpDefinition_ReturnsNoOpDefinition()
        {
            Assert.AreEqual(ApplyResult.NoOpDefinition, _service.ApplyEffect(Unknown, Caster, Def(NewBuff, 0)));
        }

        [Test]
        public void StatusEffects_Validation_Order_UnknownTargetWithProhibitedStat_ReturnsInvalidStat()
        {
            Assert.AreEqual(ApplyResult.InvalidStat,
                _service.ApplyEffect(Unknown, Caster, Def(NewBuff, 1, stat: StatID.Level)));
        }

        [Test]
        public void StatusEffects_Validation_Order_DeadTargetWithCapacityFull_ReturnsInvalidTarget()
        {
            Fill(Target, 8, 8, 8, 8); // 4 rows, 32 occupied
            _service.MarkEntityDead(Target);

            AssertRejected(Def(NewBuff, 1), ApplyResult.InvalidTarget);
        }

        [Test]
        public void StatusEffects_Validation_Order_DeadTargetReapplyingActiveBuff_ReturnsInvalidTarget()
        {
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(Target, Caster, Def(NewBuff, 2)));
            _service.MarkEntityDead(Target);

            AssertRejected(Def(NewBuff, 2), ApplyResult.InvalidTarget); // target check runs before the refresh scan
        }

        // ---- AC-SE-06 / F-SE-2 NoOp ----

        [Test]
        public void StatusEffects_Validation_NoOp_NoModifiersAndNoRegen_ReturnsNoOpDefinition() // AC-SE-06
        {
            AssertRejected(Def(NewBuff, 0), ApplyResult.NoOpDefinition);
        }

        [TestCase(REGEN, 0f)]
        [TestCase(0f, REGEN)]
        public void StatusEffects_Validation_NoOp_RegenOnlyDefinition_Succeeds(float hpRegen, float mpRegen)
        {
            ApplyResult result = _service.ApplyEffect(Target, Caster, Def(NewBuff, 0, hpRegen: hpRegen, mpRegen: mpRegen));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(1, _service.GetActiveEffectCount(Target));
            Assert.AreEqual(0, _service.GetOccupiedSlots(Target));
        }

        // ---- AC-SE-07 / F-SE-1 duration ----

        [TestCase(0u, ApplyResult.InvalidDuration)]
        [TestCase(1u, ApplyResult.Success)]
        [TestCase(StatusEffectsConstants.MAX_DURATION_TICKS, ApplyResult.Success)]
        [TestCase(StatusEffectsConstants.MAX_DURATION_TICKS + 1u, ApplyResult.InvalidDuration)]
        public void StatusEffects_Validation_Duration_BoundaryValues(uint ticks, ApplyResult expected) // AC-SE-07
        {
            ApplyResult result = _service.ApplyEffect(Target, Caster, Def(NewBuff, 1, ticks));

            Assert.AreEqual(expected, result);
            int expectedRows = expected == ApplyResult.Success ? 1 : 0;
            Assert.AreEqual(expectedRows, _service.GetActiveEffectCount(Target));
            Assert.AreEqual(expectedRows, _service.GetOccupiedSlots(Target));
        }

        // ---- AC-SE-08 / CR-SE-2 prohibited stats ----

        [TestCase(StatID.Level)]
        [TestCase(StatID.Experience)]
        [TestCase(StatID.CurrentHP)]
        [TestCase(StatID.CurrentMP)]
        public void StatusEffects_Validation_Stat_ProhibitedStatAlone_ReturnsInvalidStat(StatID stat) // AC-SE-08
        {
            AssertRejected(Def(NewBuff, 1, stat: stat), ApplyResult.InvalidStat);
        }

        [Test]
        public void StatusEffects_Validation_Stat_ProhibitedAtIndexTwoOfThree_ReturnsInvalidStat() // AC-SE-08
        {
            AssertRejected(DefWithStats(StatID.Strength, StatID.Defense, StatID.CurrentHP), ApplyResult.InvalidStat);
        }

        [Test]
        public void StatusEffects_Validation_Stat_ProhibitedAtLastPopulatedIndexOfEight_ReturnsInvalidStat()
        {
            AssertRejected(
                DefWithStats(StatID.Strength, StatID.Dexterity, StatID.Vitality, StatID.Intelligence,
                    StatID.AttackPower, StatID.Defense, StatID.MagicDefense, StatID.Level),
                ApplyResult.InvalidStat);
        }

        [TestCase(StatID.MaxHP)]
        [TestCase(StatID.MaxMP)]
        public void StatusEffects_Validation_Stat_MaxHpAndMaxMpArePermitted(StatID stat)
        {
            ApplyResult result = _service.ApplyEffect(Target, Caster, Def(NewBuff, 1, stat: stat));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(1, _service.GetActiveEffectCount(Target));
        }

        // ---- AC-SE-10 row limit ----

        [Test]
        public void StatusEffects_Validation_Rows_NinthDistinctBuff_ReturnsEffectSlotsFull() // AC-SE-10
        {
            Fill(Target, 1, 1, 1, 1, 1, 1, 1, 1);
            Assert.AreEqual(ROW_LIMIT, _service.GetActiveEffectCount(Target));
            Assert.Less(_service.GetOccupiedSlots(Target) + 1, CAPACITY, "capacity must not be the reason");

            AssertRejected(Def(NewBuff, 1), ApplyResult.EffectSlotsFull);
        }

        // ---- AC-SE-03 / AC-SE-04 / F-SE-3 capacity ----

        [Test]
        public void StatusEffects_Validation_Capacity_ThirtyOccupiedPlusThree_ReturnsCapacityExceededAndKeepsRows() // AC-SE-03
        {
            int[] counts = { 7, 7, 7, 7, 2 };
            Fill(Target, counts);
            Assert.AreEqual(30, _service.GetOccupiedSlots(Target));

            AssertRejected(Def(NewBuff, 3), ApplyResult.CapacityExceeded);

            Assert.AreEqual(counts.Length, _service.GetActiveEffectCount(Target));
            for (int i = 0; i < counts.Length; i++)
            {
                Assert.IsTrue(_service.TryGetActiveEffect(Target, (BuffID)(FIRST_FILL_BUFF_RAW_ID + (uint)i), out ActiveEffect effect));
                Assert.AreEqual(counts[i], effect.ModifierCount, "row " + i);
            }
            Assert.IsFalse(_service.TryGetActiveEffect(Target, NewBuff, out _));
        }

        [Test]
        public void StatusEffects_Validation_Capacity_TwentyEightOccupiedPlusFive_ReturnsCapacityExceeded() // AC-SE-04
        {
            Fill(Target, 7, 7, 7, 7);
            Assert.AreEqual(28, _service.GetOccupiedSlots(Target));

            AssertRejected(Def(NewBuff, 5), ApplyResult.CapacityExceeded);
        }

        [Test]
        public void StatusEffects_Validation_Capacity_TwentyEightOccupiedPlusFour_SucceedsAtExactlyCapacity() // AC-SE-04
        {
            Fill(Target, 7, 7, 7, 7);

            ApplyResult result = _service.ApplyEffect(Target, Caster, Def(NewBuff, 4));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(CAPACITY, _service.GetOccupiedSlots(Target));
            Assert.AreEqual(5, _service.GetActiveEffectCount(Target));
        }

        [Test]
        public void StatusEffects_Validation_Capacity_OccupiedIsSumOfModifierCountsNotRowCount() // F-SE-3
        {
            Fill(Target, 4, 4, 4, 4);

            Assert.AreEqual(4, _service.GetActiveEffectCount(Target));
            Assert.AreEqual(16, _service.GetOccupiedSlots(Target));
        }

        [Test]
        public void StatusEffects_Validation_Capacity_ReachingThirtyTwoWithEightModifiers_Succeeds()
        {
            Fill(Target, 8, 8, 8);

            ApplyResult result = _service.ApplyEffect(Target, Caster, Def(NewBuff, 8));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(CAPACITY, _service.GetOccupiedSlots(Target));
        }

        [Test]
        public void StatusEffects_Validation_Capacity_ThirtyThreeWouldBeOccupied_ReturnsCapacityExceeded()
        {
            Fill(Target, 8, 8, 8, 8);
            Assert.AreEqual(CAPACITY, _service.GetOccupiedSlots(Target));

            AssertRejected(Def(NewBuff, 1), ApplyResult.CapacityExceeded);
        }

        // ---- AC-SE-17 / target ----

        [Test]
        public void StatusEffects_Validation_Target_DeadEntity_ReturnsInvalidTarget() // AC-SE-17
        {
            _service.MarkEntityDead(Target);

            AssertRejected(Def(NewBuff, 1), ApplyResult.InvalidTarget);
        }

        [Test]
        public void StatusEffects_Validation_Target_DeadEntityDoesNotAffectOtherEntity()
        {
            _service.MarkEntityDead(Target);

            ApplyResult result = _service.ApplyEffect(Other, Caster, Def(NewBuff, 1));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(1, _service.GetActiveEffectCount(Other));
            Assert.AreEqual(0, _service.GetActiveEffectCount(Target));
        }

        [Test]
        public void StatusEffects_Validation_Target_UnknownEntity_ReturnsInvalidTarget()
        {
            ApplyResult result = _service.ApplyEffect(Unknown, Caster, Def(NewBuff, 1));

            Assert.AreEqual(ApplyResult.InvalidTarget, result);
            Assert.AreEqual(0, _service.GetActiveEffectCount(Unknown));
            Assert.AreEqual(0, _service.GetOccupiedSlots(Unknown));
            Assert.AreEqual(0, _service.GetActiveEffectCount(Target));
            Assert.AreEqual(0, _service.GetActiveEffectCount(Other));
        }

        [TestCase(-1)]
        [TestCase(StatusEffectsConstants.MAX_ENTITY_SLOTS)]
        public void StatusEffects_Validation_Target_ResolverSlotOutOfRange_ReturnsInvalidTargetWithoutThrowing(int slot)
        {
            _slots.Add(Unknown, slot);

            Assert.AreEqual(ApplyResult.InvalidTarget, _service.ApplyEffect(Unknown, Caster, Def(NewBuff, 1)));
            Assert.DoesNotThrow(() => _service.MarkEntityDead(Unknown));
            Assert.AreEqual(0, _service.GetOccupiedSlots(Unknown));
            Assert.AreEqual(0, _service.GetActiveEffectCount(Unknown));
            Assert.IsFalse(_service.TryGetActiveEffect(Unknown, NewBuff, out _));
        }

        [Test]
        public void StatusEffects_Validation_Target_MarkEntityDeadOnUnknownEntity_IsIgnored()
        {
            _service.MarkEntityDead(Unknown);

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(Target, Caster, Def(NewBuff, 1)));
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(Other, Caster, Def(NewBuff, 1)));
        }

        // ---- Regen-only rows count toward the row limit ----

        [Test]
        public void StatusEffects_Validation_Rows_EightRegenOnlyRows_NinthReturnsEffectSlotsFull()
        {
            for (int i = 0; i < ROW_LIMIT; i++)
            {
                BuffDefinition regenOnly = Def((BuffID)(FIRST_FILL_BUFF_RAW_ID + (uint)i), 0, hpRegen: REGEN);
                Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(Target, Caster, regenOnly), "fill " + i);
            }
            Assert.AreEqual(0, _service.GetOccupiedSlots(Target));

            AssertRejected(Def(NewBuff, 0, hpRegen: REGEN), ApplyResult.EffectSlotsFull);
        }

        // ---- Same BuffID with identical values: plain refresh (full refresh and replacement coverage: StatusEffects_Refresh_Tests) ----

        [Test]
        public void StatusEffects_Validation_SameBuff_AppliedTwice_SucceedsWithOneRowAndUnchangedOccupancy()
        {
            BuffDefinition def = Def(NewBuff, 3);
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(Target, Caster, def));

            ApplyResult second = _service.ApplyEffect(Target, Caster, def);

            Assert.AreEqual(ApplyResult.Success, second);
            Assert.AreEqual(1, _service.GetActiveEffectCount(Target));
            Assert.AreEqual(3, _service.GetOccupiedSlots(Target));
        }

        [Test]
        public void StatusEffects_Validation_SameBuff_WhenRowsAndCapacityFull_StillSucceedsWithoutChange()
        {
            int[] counts = { 4, 4, 4, 4, 4, 4, 4, 4 };
            Fill(Target, counts);

            ApplyResult result = _service.ApplyEffect(Target, Caster, Def((BuffID)FIRST_FILL_BUFF_RAW_ID, 4));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(ROW_LIMIT, _service.GetActiveEffectCount(Target));
            Assert.AreEqual(Sum(counts), _service.GetOccupiedSlots(Target));
        }

        // ---- Success storage ----

        [Test]
        public void StatusEffects_Validation_Success_StoresEffectWithCasterDurationAndModifierCount()
        {
            const int MODIFIER_COUNT = 2;
            int addsBefore = _layer.AddCalls;

            ApplyResult result = _service.ApplyEffect(Target, Caster, Def(NewBuff, MODIFIER_COUNT));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(MODIFIER_COUNT, _layer.AddCalls - addsBefore, "one layer add per modifier");
            Assert.IsTrue(_service.TryGetActiveEffect(Target, NewBuff, out ActiveEffect effect));
            Assert.AreEqual(Target, effect.EntityId);
            Assert.AreEqual(Caster, effect.CasterEntityId);
            Assert.AreEqual(VALID_DURATION, effect.TicksRemaining);
            Assert.AreEqual(2, effect.ModifierCount);
            Assert.AreEqual(2, _service.GetOccupiedSlots(Target));
        }

        [Test]
        public void StatusEffects_Validation_Success_RowsAndOccupancyAreIsolatedPerEntity()
        {
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(Target, Caster, Def(NewBuff, 3)));
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(Other, Caster, Def(NewBuff, 5)));

            Assert.AreEqual(3, _service.GetOccupiedSlots(Target));
            Assert.AreEqual(5, _service.GetOccupiedSlots(Other));
            Assert.AreEqual(1, _service.GetActiveEffectCount(Target));
            Assert.AreEqual(1, _service.GetActiveEffectCount(Other));
        }
    }
}
