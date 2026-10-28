using System;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>
    /// EditMode unit tests for Status Effects Story 004 - refresh on reapply and in-place replacement
    /// (CR-SE-4, CR-SE-8, F-SE-4, EC-SE-3, EC-SE-8; AC-SE-11/12/19/27).
    /// </summary>
    [TestFixture]
    internal sealed class StatusEffects_Refresh_Tests
    {
        private const uint A_RAW_ID = 11u;
        private const uint B_RAW_ID = 12u;
        private const int A_SLOT = 2;
        private const int B_SLOT = 5;
        private const uint BUFF_X_RAW_ID = 1u;
        private const uint BUFF_Y_RAW_ID = 2u;
        private const uint BUFF_Z_RAW_ID = 3u;
        private const uint FILL_BUFF_FIRST_RAW_ID = 100u;
        private const uint DURATION_60 = 60u;
        private const uint DURATION_40 = 40u;
        private const uint ADVANCED_30 = 30u;
        private const uint ADVANCED_35 = 35u;
        private const uint ROW_DURATION = 10u;
        private const uint NEW_DURATION = 20u;
        private const uint SHORT_DURATION = 25u;
        private const uint ONE_TICK = 1u;
        private const int BASE_ATTACK = 20;
        private const int BASE_DEFENSE = 10;
        private const float FLAT_OLD = 10f;
        private const float FLAT_NEW = 20f;
        private const float PCT_NONE = 0f;
        private const float PCT_DIFFERENT = 0.1f;
        private const float REGEN_ROW_0 = 1f;
        private const float REGEN_ROW_1 = 2f;
        private const float REGEN_ROW_2 = 3f;
        private const float REGEN_REPLACED = 9f;
        private const float REGEN_OLD = 5f;
        private const float REGEN_NEW = 6f;
        private const int FULL_ROWS = StatusEffectsConstants.MAX_ACTIVE_BUFFS_PER_ENTITY;
        private const int MODIFIERS_PER_FULL_DEF = StatusEffectsConstants.MAX_STATS_PER_BUFF;
        private const int CAPACITY = StatusEffectsConstants.MODIFIER_CAPACITY_PER_ENTITY;

        private static readonly StatID[] DistinctStats =
        {
            StatID.Strength, StatID.Dexterity, StatID.Vitality, StatID.Intelligence,
            StatID.AttackPower, StatID.Defense, StatID.MagicDefense, StatID.MaxHP
        };

        private static readonly EntityID A = new EntityID(A_RAW_ID);
        private static readonly EntityID B = new EntityID(B_RAW_ID);
        private static readonly BuffID BuffX = (BuffID)BUFF_X_RAW_ID;
        private static readonly BuffID BuffY = (BuffID)BUFF_Y_RAW_ID;
        private static readonly BuffID BuffZ = (BuffID)BUFF_Z_RAW_ID;

        private RecordingBuffModifierLayer _recorder;
        private DictionaryEntitySlotIndex _slots;
        private StatusEffectsService _service;
        private IronGrind.CharacterStats.CharacterStats _stats;

        [SetUp]
        public void SetUp()
        {
            _slots = new DictionaryEntitySlotIndex();
            _slots.Add(A, A_SLOT);
            _slots.Add(B, B_SLOT);
            _recorder = new RecordingBuffModifierLayer();
            _service = new StatusEffectsService(_recorder, _recorder, _slots);
        }

        // ---- Helpers ----

        private static StatModifier Mod(StatID stat, float flat)
        {
            return new StatModifier(stat, flat, PCT_NONE);
        }

        private static BuffDefinition Def(BuffID id, uint duration, float hpRegen, float mpRegen, params StatModifier[] mods)
        {
            return new BuffDefinition(id, mods, duration, hpRegen, mpRegen);
        }

        private static BuffDefinition ApDef(BuffID id, uint duration, float flat)
        {
            return Def(id, duration, 0f, 0f, Mod(StatID.AttackPower, flat));
        }

        private static BuffDefinition RegenDef(BuffID id, uint duration, float hpRegen, float flat)
        {
            return Def(id, duration, hpRegen, 0f, Mod(StatID.AttackPower, flat));
        }

        /// <summary>A definition with one modifier on each of the first <paramref name="count"/> distinct stats.</summary>
        private static BuffDefinition WideDef(BuffID id, uint duration, int count, float flat)
        {
            var mods = new StatModifier[count];
            for (int i = 0; i < count; i++)
                mods[i] = Mod(DistinctStats[i], flat);
            return Def(id, duration, 0f, 0f, mods);
        }

        private static BuffID FillBuff(int index)
        {
            return (BuffID)(FILL_BUFF_FIRST_RAW_ID + (uint)index);
        }

        private void Apply(EntityID target, in BuffDefinition def)
        {
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(target, target, def));
        }

        /// <summary>Calls of one kind in the recorder's log since its last <c>Calls.Clear()</c> (the counters are cumulative).</summary>
        private int CallsSinceClear(LayerCallKind kind)
        {
            int n = 0;
            foreach (LayerCall call in _recorder.Calls)
            {
                if (call.Kind == kind)
                    n++;
            }

            return n;
        }

        private void Tick(int count)
        {
            for (int i = 0; i < count; i++)
                _service.OnServerTick();
        }

        private void BuildReal()
        {
            _stats = CharacterStatsFixture.Create();
            _stats.SetBaseStat(A, StatID.AttackPower, BASE_ATTACK);
            _stats.SetBaseStat(A, StatID.Defense, BASE_DEFENSE);
            _recorder = new RecordingBuffModifierLayer(
                new CharacterStatsBuffModifierLayer(_stats), new CharacterStatsResourceSink(_stats));
            _service = new StatusEffectsService(_recorder, _recorder, _slots);
        }

        private int Effective(EntityID entity, StatID stat)
        {
            return _stats.GetEffectiveStat(entity, stat);
        }

        private ActiveEffect Row(EntityID entity, BuffID buff)
        {
            Assert.IsTrue(_service.TryGetActiveEffect(entity, buff, out ActiveEffect row), "row " + buff + " missing");
            return row;
        }

        private int CountRegen(float amount)
        {
            int count = 0;
            for (int i = 0; i < _recorder.Calls.Count; i++)
            {
                if (_recorder.Calls[i].Kind == LayerCallKind.Regen && _recorder.Calls[i].Amount == amount)
                    count++;
            }
            return count;
        }

        /// <summary>Three rows X, Y, Z on A with distinct HP regen, then clears the call log.</summary>
        private void ApplyThreeRegenRows()
        {
            Apply(A, RegenDef(BuffX, ROW_DURATION, REGEN_ROW_0, FLAT_OLD));
            Apply(A, RegenDef(BuffY, ROW_DURATION, REGEN_ROW_1, FLAT_OLD));
            Apply(A, RegenDef(BuffZ, ROW_DURATION, REGEN_ROW_2, FLAT_OLD));
            _recorder.Calls.Clear();
        }

        // ---- AC-SE-11, AC-SE-12 ----

        [Test]
        public void StatusEffects_Refresh_SameValuesAfterThirtyTicks_ResetsTicksWithNoLayerCalls() // AC-SE-11
        {
            BuildReal();
            Apply(A, ApDef(BuffX, DURATION_60, FLAT_OLD));
            int effectiveAfterFirst = Effective(A, StatID.AttackPower);
            Tick((int)ADVANCED_30);
            Assert.AreEqual(DURATION_60 - ADVANCED_30, Row(A, BuffX).TicksRemaining);
            _recorder.Calls.Clear();

            ApplyResult result = _service.ApplyEffect(A, A, ApDef(BuffX, DURATION_60, FLAT_OLD));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(DURATION_60, Row(A, BuffX).TicksRemaining);
            Assert.AreEqual(0, _recorder.Calls.Count, "no add and no remove");
            Assert.AreEqual(effectiveAfterFirst, Effective(A, StatID.AttackPower));
            Assert.AreEqual(1, _service.GetActiveEffectCount(A));
        }

        [Test]
        public void StatusEffects_Refresh_FiveTicksLeft_ResetsToFullDurationNotSum() // AC-SE-12
        {
            Apply(A, ApDef(BuffX, DURATION_40, FLAT_OLD));
            Tick((int)ADVANCED_35);
            Assert.AreEqual(DURATION_40 - ADVANCED_35, Row(A, BuffX).TicksRemaining);

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(BuffX, DURATION_40, FLAT_OLD)));

            Assert.AreEqual(DURATION_40, Row(A, BuffX).TicksRemaining);
        }

        // ---- AC-SE-27 ----

        [Test]
        public void StatusEffects_Refresh_ChangedFlat_RemovesOnceThenAddsNewAndUpdatesCaster() // AC-SE-27
        {
            BuildReal();
            Apply(A, ApDef(BuffX, ROW_DURATION, FLAT_OLD));
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_OLD, Effective(A, StatID.AttackPower));
            _recorder.Calls.Clear();

            ApplyResult result = _service.ApplyEffect(A, B, ApDef(BuffX, NEW_DURATION, FLAT_NEW));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(2, _recorder.Calls.Count);
            Assert.AreEqual(LayerCallKind.Remove, _recorder.Calls[0].Kind);
            Assert.AreEqual(StatID.AttackPower, _recorder.Calls[0].Stat);
            Assert.AreEqual(BuffX, _recorder.Calls[0].Buff);
            Assert.AreEqual(LayerCallKind.Add, _recorder.Calls[1].Kind);
            Assert.AreEqual(FLAT_NEW, _recorder.Calls[1].Entry.FlatBonus);
            Assert.AreEqual((int)NEW_DURATION, _recorder.Calls[1].Entry.DurationTicks);
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_NEW, Effective(A, StatID.AttackPower));
            ActiveEffect row = Row(A, BuffX);
            Assert.AreEqual(NEW_DURATION, row.TicksRemaining);
            Assert.AreEqual(B, row.CasterEntityId);
            Assert.AreEqual(1, _service.GetOccupiedSlots(A));
        }

        // ---- Same-values definition ----

        [Test]
        public void StatusEffects_Refresh_SameMultiModifierValues_PlainRefreshNoLayerCalls() // F-SE-4
        {
            BuffDefinition def = Def(BuffX, ROW_DURATION, REGEN_OLD, REGEN_ROW_0,
                Mod(StatID.AttackPower, FLAT_OLD), Mod(StatID.Defense, FLAT_NEW));
            Apply(A, def);
            _recorder.Calls.Clear();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, def));

            Assert.AreEqual(0, _recorder.Calls.Count);
            Assert.AreEqual(2, _service.GetOccupiedSlots(A));
        }

        [Test]
        public void StatusEffects_Refresh_DifferentPctBonus_Replaces() // F-SE-4
        {
            Apply(A, ApDef(BuffX, ROW_DURATION, FLAT_OLD));
            _recorder.Calls.Clear();
            BuffDefinition changed = Def(BuffX, ROW_DURATION, 0f, 0f,
                new StatModifier(StatID.AttackPower, FLAT_OLD, PCT_DIFFERENT));

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, changed));

            Assert.AreEqual(1, CallsSinceClear(LayerCallKind.Remove));
            Assert.AreEqual(1, CallsSinceClear(LayerCallKind.Add));
            Assert.AreEqual(PCT_DIFFERENT, Row(A, BuffX).Modifier0.PctBonus);
        }

        [Test]
        public void StatusEffects_Refresh_SameModifiersInDifferentOrder_Replaces() // F-SE-4 per-index compare
        {
            Apply(A, Def(BuffX, ROW_DURATION, 0f, 0f, Mod(StatID.AttackPower, FLAT_OLD), Mod(StatID.Defense, FLAT_OLD)));
            _recorder.Calls.Clear();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A,
                Def(BuffX, ROW_DURATION, 0f, 0f, Mod(StatID.Defense, FLAT_OLD), Mod(StatID.AttackPower, FLAT_OLD))));

            Assert.AreEqual(2, CallsSinceClear(LayerCallKind.Remove));
            Assert.AreEqual(2, CallsSinceClear(LayerCallKind.Add));
            Assert.AreEqual(StatID.Defense, Row(A, BuffX).Modifier0.StatId);
        }

        [Test]
        public void StatusEffects_Refresh_DurationOnlyDifference_PlainRefreshWithNewDuration() // criterion "same values"
        {
            Apply(A, ApDef(BuffX, ROW_DURATION, FLAT_OLD));
            Tick(3);
            _recorder.Calls.Clear();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(BuffX, SHORT_DURATION, FLAT_OLD)));

            Assert.AreEqual(SHORT_DURATION, Row(A, BuffX).TicksRemaining);
            Assert.AreEqual(0, _recorder.Calls.Count);
        }

        [Test]
        public void StatusEffects_Refresh_CasterOnlyDifference_PlainRefreshWithNewCaster() // criterion "same values"
        {
            Apply(A, ApDef(BuffX, ROW_DURATION, FLAT_OLD));
            _recorder.Calls.Clear();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, B, ApDef(BuffX, ROW_DURATION, FLAT_OLD)));

            Assert.AreEqual(B, Row(A, BuffX).CasterEntityId);
            Assert.AreEqual(0, _recorder.Calls.Count);
        }

        // ---- Edges ----

        [Test]
        public void StatusEffects_Refresh_AtOneTickRemaining_ResetsAndSurvivesNextTick()
        {
            Apply(A, ApDef(BuffX, ROW_DURATION, FLAT_OLD));
            Tick((int)ROW_DURATION - 1);
            Assert.AreEqual(ONE_TICK, Row(A, BuffX).TicksRemaining);

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(BuffX, ROW_DURATION, FLAT_OLD)));
            Tick(1);

            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffX).TicksRemaining);
            Assert.AreEqual(0, CallsSinceClear(LayerCallKind.Remove));
        }

        [Test]
        public void StatusEffects_Refresh_WithEightRowsFull_StillSucceeds() // skips the row limit
        {
            for (int i = 0; i < FULL_ROWS; i++)
                Apply(A, ApDef(FillBuff(i), ROW_DURATION, FLAT_OLD));
            Tick(2);
            int occupiedBefore = _service.GetOccupiedSlots(A);

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(FillBuff(3), NEW_DURATION, FLAT_OLD)));

            Assert.AreEqual(FULL_ROWS, _service.GetActiveEffectCount(A));
            Assert.AreEqual(NEW_DURATION, Row(A, FillBuff(3)).TicksRemaining);
            Assert.AreEqual(occupiedBefore, _service.GetOccupiedSlots(A));
            ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(A);
            for (int i = 0; i < FULL_ROWS; i++)
                Assert.AreEqual(FillBuff(i), rows[i].BuffId, "row order unchanged at " + i);
        }

        [Test]
        public void StatusEffects_Refresh_ReplacementAtExactlyFullCapacityWithFourModifierBuffs_Succeeds()
        {
            const int FOUR_MODS = 4;
            for (int i = 0; i < FULL_ROWS; i++)
                Apply(A, WideDef(FillBuff(i), ROW_DURATION, FOUR_MODS, FLAT_OLD));
            Assert.AreEqual(CAPACITY, _service.GetOccupiedSlots(A));

            ApplyResult result = _service.ApplyEffect(A, A, WideDef(FillBuff(3), ROW_DURATION, FOUR_MODS, FLAT_NEW));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(CAPACITY, _service.GetOccupiedSlots(A));
            Assert.AreEqual(FULL_ROWS, _service.GetActiveEffectCount(A));
            Assert.AreEqual(FLAT_NEW, Row(A, FillBuff(3)).Modifier0.FlatBonus);
        }

        [Test]
        public void StatusEffects_Refresh_ReplacementAtExactlyFullCapacity_Succeeds()
        {
            const int FULL_DEFS = CAPACITY / MODIFIERS_PER_FULL_DEF;
            for (int i = 0; i < FULL_DEFS; i++)
                Apply(A, WideDef(FillBuff(i), ROW_DURATION, MODIFIERS_PER_FULL_DEF, FLAT_OLD));
            Assert.AreEqual(CAPACITY, _service.GetOccupiedSlots(A));

            ApplyResult result = _service.ApplyEffect(A, A, WideDef(FillBuff(1), ROW_DURATION, MODIFIERS_PER_FULL_DEF, FLAT_NEW));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(CAPACITY, _service.GetOccupiedSlots(A));
            Assert.AreEqual(FLAT_NEW, Row(A, FillBuff(1)).Modifier0.FlatBonus);
        }

        [Test]
        public void StatusEffects_Refresh_DeadEntity_InvalidTargetAndNoRefresh() // EC-SE-3
        {
            Apply(A, ApDef(BuffX, ROW_DURATION, FLAT_OLD));
            Tick(2);
            _service.MarkEntityDead(A);
            _recorder.Calls.Clear();
            int occupiedBefore = _service.GetOccupiedSlots(A);

            Assert.AreEqual(ApplyResult.InvalidTarget, _service.ApplyEffect(A, B, ApDef(BuffX, NEW_DURATION, FLAT_OLD)));
            Assert.AreEqual(occupiedBefore, _service.GetOccupiedSlots(A));

            ActiveEffect row = Row(A, BuffX);
            Assert.AreEqual(ROW_DURATION - 2u, row.TicksRemaining);
            Assert.AreEqual(A, row.CasterEntityId);
            Assert.AreEqual(0, _recorder.Calls.Count);
        }

        // ---- Replacement ----

        [Test]
        public void StatusEffects_Refresh_ReplaceMiddleOfThree_KeepsRowIndexAndOthersUntouched()
        {
            Apply(A, ApDef(BuffX, ROW_DURATION, FLAT_OLD));
            Apply(A, ApDef(BuffY, ROW_DURATION, FLAT_OLD));
            Apply(A, ApDef(BuffZ, ROW_DURATION, FLAT_OLD));
            Tick(2);

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, B, ApDef(BuffY, NEW_DURATION, FLAT_NEW)));

            ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(A);
            Assert.AreEqual(3, rows.Length);
            Assert.AreEqual(BuffX, rows[0].BuffId);
            Assert.AreEqual(BuffY, rows[1].BuffId);
            Assert.AreEqual(BuffZ, rows[2].BuffId);
            Assert.AreEqual(FLAT_NEW, rows[1].Modifier0.FlatBonus);
            Assert.AreEqual(NEW_DURATION, rows[1].TicksRemaining);
            Assert.AreEqual(B, rows[1].CasterEntityId);
            Assert.AreEqual(ROW_DURATION - 2u, rows[0].TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 2u, rows[2].TicksRemaining);
            Assert.AreEqual(FLAT_OLD, rows[0].Modifier0.FlatBonus);
            Assert.AreEqual(FLAT_OLD, rows[2].Modifier0.FlatBonus);
        }

        [Test]
        public void StatusEffects_Refresh_ReplacementWithTwoModifiers_RemovesInIndexOrderThenAddsInIndexOrder()
        {
            Apply(A, Def(BuffX, ROW_DURATION, 0f, 0f, Mod(StatID.AttackPower, FLAT_OLD), Mod(StatID.Defense, FLAT_OLD)));
            _recorder.Calls.Clear();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A,
                Def(BuffX, ROW_DURATION, 0f, 0f, Mod(StatID.Strength, FLAT_NEW), Mod(StatID.Vitality, FLAT_NEW))));

            Assert.AreEqual(4, _recorder.Calls.Count);
            Assert.AreEqual(LayerCallKind.Remove, _recorder.Calls[0].Kind);
            Assert.AreEqual(StatID.AttackPower, _recorder.Calls[0].Stat);
            Assert.AreEqual(LayerCallKind.Remove, _recorder.Calls[1].Kind);
            Assert.AreEqual(StatID.Defense, _recorder.Calls[1].Stat);
            Assert.AreEqual(LayerCallKind.Add, _recorder.Calls[2].Kind);
            Assert.AreEqual(StatID.Strength, _recorder.Calls[2].Stat);
            Assert.AreEqual(LayerCallKind.Add, _recorder.Calls[3].Kind);
            Assert.AreEqual(StatID.Vitality, _recorder.Calls[3].Stat);
        }

        [Test]
        public void StatusEffects_Refresh_TwoModifiersReplacedByThree_OccupancyGrowsByOne()
        {
            Apply(A, WideDef(BuffX, ROW_DURATION, 2, FLAT_OLD));
            Apply(A, WideDef(BuffY, ROW_DURATION, 1, FLAT_OLD));
            Assert.AreEqual(3, _service.GetOccupiedSlots(A));

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, WideDef(BuffX, ROW_DURATION, 3, FLAT_NEW)));

            Assert.AreEqual(4, _service.GetOccupiedSlots(A));
            Assert.AreEqual(3, Row(A, BuffX).ModifierCount);
        }

        [Test]
        public void StatusEffects_Refresh_ThreeModifiersReplacedByTwo_OccupancyShrinksByOne()
        {
            Apply(A, WideDef(BuffX, ROW_DURATION, 3, FLAT_OLD));
            Apply(A, WideDef(BuffY, ROW_DURATION, 1, FLAT_OLD));
            Assert.AreEqual(4, _service.GetOccupiedSlots(A));

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, WideDef(BuffX, ROW_DURATION, 2, FLAT_NEW)));

            Assert.AreEqual(3, _service.GetOccupiedSlots(A));
            Assert.AreEqual(2, Row(A, BuffX).ModifierCount);
        }

        [Test]
        public void StatusEffects_Refresh_ReplacementWouldExceedCapacity_FailsWithNoLayerCallsAndOldRowIntact()
        {
            // 3 x 8 + 4 + 2 = 30 occupied; replacing the 2-modifier buff with 5 modifiers needs 33.
            const int WIDE_DEFS = 3;
            const int MEDIUM_MODS = 4;
            const int OLD_MODS = 2;
            const int NEW_MODS = 5;
            for (int i = 0; i < WIDE_DEFS; i++)
                Apply(A, WideDef(FillBuff(i), ROW_DURATION, MODIFIERS_PER_FULL_DEF, FLAT_OLD));
            Apply(A, WideDef(FillBuff(WIDE_DEFS), ROW_DURATION, MEDIUM_MODS, FLAT_OLD));
            Apply(A, WideDef(BuffX, ROW_DURATION, OLD_MODS, FLAT_OLD));
            int occupied = _service.GetOccupiedSlots(A);
            int rowCount = _service.GetActiveEffectCount(A);
            Assert.AreEqual(30, occupied);
            _recorder.Calls.Clear();

            ApplyResult result = _service.ApplyEffect(A, B, WideDef(BuffX, NEW_DURATION, NEW_MODS, FLAT_NEW));

            Assert.AreEqual(ApplyResult.CapacityExceeded, result);
            Assert.AreEqual(0, _recorder.Calls.Count, "no layer call before the pre-check passes");
            Assert.AreEqual(occupied, _service.GetOccupiedSlots(A));
            Assert.AreEqual(rowCount, _service.GetActiveEffectCount(A));
            ActiveEffect row = Row(A, BuffX);
            Assert.AreEqual(OLD_MODS, row.ModifierCount);
            Assert.AreEqual(ROW_DURATION, row.TicksRemaining);
            Assert.AreEqual(A, row.CasterEntityId);
        }

        [Test]
        public void StatusEffects_Refresh_LayerAddFails_OldRowGoneLaterRowsShiftedOccupancyReduced()
        {
            Apply(A, ApDef(BuffX, ROW_DURATION, FLAT_OLD));
            Apply(A, WideDef(BuffY, ROW_DURATION, 2, FLAT_OLD));
            Apply(A, ApDef(BuffZ, ROW_DURATION, FLAT_OLD));
            Assert.AreEqual(4, _service.GetOccupiedSlots(A));
            // Second add of the replacement fails. AddCalls is cumulative, so reading it here (before Calls.Clear(),
            // which only clears the log) gives the number of the add that follows.
            _recorder.FailOnAddNumber = _recorder.AddCalls + 2;
            _recorder.Calls.Clear();

            ApplyResult result = _service.ApplyEffect(A, A, WideDef(BuffY, NEW_DURATION, 2, FLAT_NEW));

            Assert.AreEqual(ApplyResult.CapacityExceeded, result);
            ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(A);
            Assert.AreEqual(2, rows.Length);
            Assert.AreEqual(BuffX, rows[0].BuffId);
            Assert.AreEqual(BuffZ, rows[1].BuffId, "later row shifted down");
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffY, out _));
            Assert.AreEqual(2, _service.GetOccupiedSlots(A), "occupancy drops by the old row's two modifiers");
            // 2 old removals + 1 rollback of the first new add.
            Assert.AreEqual(3, CallsSinceClear(LayerCallKind.Remove));
            Assert.AreEqual(2, CallsSinceClear(LayerCallKind.Add));
        }

        [Test]
        public void StatusEffects_Refresh_RegenOnlyChange_TriggersReplacement() // F-SE-4 regen rates
        {
            Apply(A, RegenDef(BuffX, ROW_DURATION, REGEN_OLD, FLAT_OLD));
            _recorder.Calls.Clear();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, RegenDef(BuffX, ROW_DURATION, REGEN_NEW, FLAT_OLD)));

            Assert.AreEqual(1, CallsSinceClear(LayerCallKind.Remove));
            Assert.AreEqual(1, CallsSinceClear(LayerCallKind.Add));
            Assert.AreEqual(REGEN_NEW, Row(A, BuffX).HpRegenPerTick);
        }

        [Test]
        public void StatusEffects_Refresh_RegenOnlyBuffWithNewRate_UpdatesRateWithoutLayerCalls()
        {
            Apply(A, Def(BuffX, ROW_DURATION, REGEN_OLD, 0f));
            _recorder.Calls.Clear();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, Def(BuffX, ROW_DURATION, REGEN_NEW, 0f)));

            Assert.AreEqual(0, _recorder.Calls.Count);
            Assert.AreEqual(REGEN_NEW, Row(A, BuffX).HpRegenPerTick);
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
        }

        // ---- During a tick (EC-SE-8, AC-SE-19). Injection fires after the first row (X) has been processed. ----

        [Test]
        public void StatusEffects_Refresh_DuringTickOfProcessedRow_KeepsFullDuration()
        {
            ApplyThreeRegenRows();
            _service.InjectApplyEffectDuringTick(A, RegenDef(BuffX, NEW_DURATION, REGEN_ROW_0, FLAT_OLD));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual(NEW_DURATION, Row(A, BuffX).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffY).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffZ).TicksRemaining);
            Assert.AreEqual(1, CountRegen(REGEN_ROW_0));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_1));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
        }

        [Test]
        public void StatusEffects_Refresh_DuringTickOfLaterRow_DecrementedByThisTick()
        {
            ApplyThreeRegenRows();
            _service.InjectApplyEffectDuringTick(A, RegenDef(BuffY, NEW_DURATION, REGEN_ROW_1, FLAT_OLD));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual(NEW_DURATION - 1u, Row(A, BuffY).TicksRemaining);
            Assert.AreEqual(1, CountRegen(REGEN_ROW_1), "later row still gets this tick's regen");
        }

        [Test]
        public void StatusEffects_Refresh_DuringTickReplacingLaterRow_NewValuesDecrementedOnce()
        {
            ApplyThreeRegenRows();
            _service.InjectApplyEffectDuringTick(A, RegenDef(BuffY, NEW_DURATION, REGEN_REPLACED, FLAT_NEW));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            ActiveEffect row = Row(A, BuffY);
            Assert.AreEqual(NEW_DURATION - 1u, row.TicksRemaining);
            Assert.AreEqual(FLAT_NEW, row.Modifier0.FlatBonus);
            Assert.AreEqual(REGEN_REPLACED, row.HpRegenPerTick);
            Assert.AreEqual(1, CountRegen(REGEN_REPLACED));
            Assert.AreEqual(0, CountRegen(REGEN_ROW_1), "old regen rate no longer applied");
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
            Assert.AreEqual(3, _service.GetActiveEffectCount(A));
        }

        [Test]
        public void StatusEffects_Refresh_DuringTickFailedReplacementOfProcessedRow_EveryRemainingRowProcessedOnce()
        {
            ApplyThreeRegenRows();
            _recorder.FailOnAddNumber = _recorder.AddCalls + 1;
            _service.InjectApplyEffectDuringTick(A, RegenDef(BuffX, NEW_DURATION, REGEN_ROW_0, FLAT_NEW));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.CapacityExceeded, _service.LastInjectedResult);
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffX, out _));
            Assert.AreEqual(2, _service.GetActiveEffectCount(A));
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffY).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffZ).TicksRemaining);
            Assert.AreEqual(1, CountRegen(REGEN_ROW_0), "freed row was processed once before it was freed");
            Assert.AreEqual(1, CountRegen(REGEN_ROW_1));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
            Assert.AreEqual(2, _service.GetOccupiedSlots(A));
        }

        [Test]
        public void StatusEffects_Refresh_DuringTickFailedReplacementOfNextRow_FollowingRowShiftedInAndProcessedOnce()
        {
            ApplyThreeRegenRows();
            _recorder.FailOnAddNumber = _recorder.AddCalls + 1;
            _service.InjectApplyEffectDuringTick(A, RegenDef(BuffY, NEW_DURATION, REGEN_ROW_1, FLAT_NEW));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.CapacityExceeded, _service.LastInjectedResult);
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffY, out _));
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffX).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffZ).TicksRemaining, "no row skipped");
            Assert.AreEqual(1, CountRegen(REGEN_ROW_0));
            Assert.AreEqual(0, CountRegen(REGEN_ROW_1), "freed before it was reached");
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2), "processed exactly once");
        }

        [Test]
        public void StatusEffects_Refresh_DuringTickFailedReplacementOfLastRow_OtherRowsProcessedOnce()
        {
            ApplyThreeRegenRows();
            _recorder.FailOnAddNumber = _recorder.AddCalls + 1;
            _service.InjectApplyEffectDuringTick(A, RegenDef(BuffZ, NEW_DURATION, REGEN_ROW_2, FLAT_NEW));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.CapacityExceeded, _service.LastInjectedResult);
            Assert.AreEqual(2, _service.GetActiveEffectCount(A));
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffX).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffY).TicksRemaining);
            Assert.AreEqual(1, CountRegen(REGEN_ROW_0));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_1));
            Assert.AreEqual(0, CountRegen(REGEN_ROW_2));
        }

        [Test]
        public void StatusEffects_Refresh_DuringTickFailedReplacementThenNextTick_RemainingRowsDecrementOncePerTick()
        {
            ApplyThreeRegenRows();
            _recorder.FailOnAddNumber = _recorder.AddCalls + 1;
            _service.InjectApplyEffectDuringTick(A, RegenDef(BuffX, NEW_DURATION, REGEN_ROW_0, FLAT_NEW));
            _service.OnServerTick();
            _recorder.Calls.Clear();

            _service.OnServerTick();

            Assert.AreEqual(ROW_DURATION - 2u, Row(A, BuffY).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 2u, Row(A, BuffZ).TicksRemaining);
            Assert.AreEqual(1, CountRegen(REGEN_ROW_1));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
        }

        [Test]
        public void StatusEffects_Refresh_DuringTickSuccessfulReplacementOfProcessedRow_KeepsFullDurationAndSkipsNewRegen()
        {
            ApplyThreeRegenRows();
            _service.InjectApplyEffectDuringTick(A, RegenDef(BuffX, NEW_DURATION, REGEN_REPLACED, FLAT_NEW));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            ActiveEffect row = Row(A, BuffX);
            Assert.AreEqual(NEW_DURATION, row.TicksRemaining, "not decremented by this tick");
            Assert.AreEqual(REGEN_REPLACED, row.HpRegenPerTick);
            Assert.AreEqual(0, CountRegen(REGEN_REPLACED), "new regen not applied this tick");
            Assert.AreEqual(1, CountRegen(REGEN_ROW_0), "old regen applied before the replacement");
            Assert.AreEqual(1, CountRegen(REGEN_ROW_1));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
            Assert.AreEqual(3, _service.GetActiveEffectCount(A));
        }

        // B1 regression: a failed replacement on a slot the tick has not reached yet.
        [Test]
        public void StatusEffects_Refresh_DuringTickFailedReplacementInLaterSlot_LaterSlotProcessesRemainingRowsOnce()
        {
            Apply(A, RegenDef(FillBuff(0), ROW_DURATION, REGEN_OLD, FLAT_OLD));
            Apply(B, RegenDef(BuffX, ROW_DURATION, REGEN_ROW_0, FLAT_OLD));
            Apply(B, RegenDef(BuffY, ROW_DURATION, REGEN_ROW_1, FLAT_OLD));
            Apply(B, RegenDef(BuffZ, ROW_DURATION, REGEN_ROW_2, FLAT_OLD));
            _recorder.Calls.Clear();
            _recorder.FailOnAddNumber = _recorder.AddCalls + 1;
            _service.InjectApplyEffectDuringTick(B, RegenDef(BuffY, NEW_DURATION, REGEN_REPLACED, FLAT_NEW), A);

            Assert.DoesNotThrow(() => _service.OnServerTick());

            Assert.AreEqual(ApplyResult.CapacityExceeded, _service.LastInjectedResult);
            Assert.AreEqual(2, _service.GetActiveEffectCount(B));
            Assert.IsFalse(_service.TryGetActiveEffect(B, BuffY, out _));
            Assert.AreEqual(1, CountRegen(REGEN_OLD));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_0));
            Assert.AreEqual(0, CountRegen(REGEN_ROW_1));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
            Assert.AreEqual(ROW_DURATION - 1u, Row(B, BuffX).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 1u, Row(B, BuffZ).TicksRemaining);
            Assert.AreEqual(2, _service.GetOccupiedSlots(B));

            _recorder.Calls.Clear();
            _service.OnServerTick();

            Assert.AreEqual(1, CountRegen(REGEN_ROW_0));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
            Assert.AreEqual(ROW_DURATION - 2u, Row(B, BuffX).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 2u, Row(B, BuffZ).TicksRemaining);
        }

        // ---- Two in-tick applies: the first creates a row, the second touches it (AC-SE-19) ----

        [Test]
        public void StatusEffects_Refresh_DuringTickRefreshOfRowCreatedThisTick_FullDurationAndNoRegen() // AC-SE-19
        {
            ApplyThreeRegenRows();
            _service.InjectApplyEffectDuringTick(A, RegenDef(FillBuff(0), NEW_DURATION, REGEN_OLD, FLAT_OLD));
            _service.InjectSecondApplyEffectDuringTick(A, RegenDef(FillBuff(0), SHORT_DURATION, REGEN_OLD, FLAT_OLD));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual(ApplyResult.Success, _service.LastSecondInjectedResult);
            Assert.AreEqual(SHORT_DURATION, Row(A, FillBuff(0)).TicksRemaining);
            Assert.AreEqual(0, CountRegen(REGEN_OLD), "row created this tick gets no regen");
            Assert.AreEqual(1, CountRegen(REGEN_ROW_0));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_1));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
            Assert.AreEqual(4, _service.GetActiveEffectCount(A));
        }

        [Test]
        public void StatusEffects_Refresh_DuringTickReplacementOfRowCreatedThisTick_FullDurationAndNoRegen() // AC-SE-19
        {
            ApplyThreeRegenRows();
            _service.InjectApplyEffectDuringTick(A, RegenDef(FillBuff(0), NEW_DURATION, REGEN_OLD, FLAT_OLD));
            _service.InjectSecondApplyEffectDuringTick(A, RegenDef(FillBuff(0), SHORT_DURATION, REGEN_NEW, FLAT_NEW));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastSecondInjectedResult);
            ActiveEffect row = Row(A, FillBuff(0));
            Assert.AreEqual(SHORT_DURATION, row.TicksRemaining);
            Assert.AreEqual(FLAT_NEW, row.Modifier0.FlatBonus);
            Assert.AreEqual(0, CountRegen(REGEN_OLD));
            Assert.AreEqual(0, CountRegen(REGEN_NEW));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_1));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
            Assert.AreEqual(4, _service.GetOccupiedSlots(A));
        }

        [Test]
        public void StatusEffects_Refresh_DuringTickFailedReplacementOfRowCreatedThisTick_FreesItAndRemainingRowsProcessedOnce()
        {
            ApplyThreeRegenRows();
            // The first injection's add is number +1, the second's (the replacement) is number +2.
            _recorder.FailOnAddNumber = _recorder.AddCalls + 2;
            _service.InjectApplyEffectDuringTick(A, RegenDef(FillBuff(0), NEW_DURATION, REGEN_OLD, FLAT_OLD));
            _service.InjectSecondApplyEffectDuringTick(A, RegenDef(FillBuff(0), SHORT_DURATION, REGEN_NEW, FLAT_NEW));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual(ApplyResult.CapacityExceeded, _service.LastSecondInjectedResult);
            Assert.IsFalse(_service.TryGetActiveEffect(A, FillBuff(0), out _));
            Assert.AreEqual(3, _service.GetActiveEffectCount(A));
            Assert.AreEqual(3, _service.GetOccupiedSlots(A));
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffX).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffY).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffZ).TicksRemaining);
            Assert.AreEqual(1, CountRegen(REGEN_ROW_0));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_1));
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
            Assert.AreEqual(0, CountRegen(REGEN_OLD));
            Assert.AreEqual(0, CountRegen(REGEN_NEW));
        }

        [Test]
        public void StatusEffects_Refresh_DuringTickReplacementThrows_RowFreedTickAbortsAndNextTickRunsNormally()
        {
            ApplyThreeRegenRows();
            _recorder.ThrowOnAddNumber = _recorder.AddCalls + 1;
            _service.InjectApplyEffectDuringTick(A, RegenDef(BuffX, NEW_DURATION, REGEN_REPLACED, FLAT_NEW));

            Assert.Throws<InvalidOperationException>(() => _service.OnServerTick());

            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffX, out _), "row freed");
            Assert.AreEqual(2, _service.GetActiveEffectCount(A));
            Assert.AreEqual(2, _service.GetOccupiedSlots(A), "occupancy drops by the old row's modifier count");
            _recorder.Calls.Clear();

            _service.OnServerTick();

            Assert.AreEqual(1, CountRegen(REGEN_ROW_1), "tick state was reset: remaining rows processed once");
            Assert.AreEqual(1, CountRegen(REGEN_ROW_2));
            Assert.AreEqual(0, CountRegen(REGEN_ROW_0));
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffY).TicksRemaining);
            Assert.AreEqual(ROW_DURATION - 1u, Row(A, BuffZ).TicksRemaining);
        }

        // ---- Layer failures with the real stats layer, and throws outside a tick ----

        [Test]
        public void StatusEffects_Refresh_SecondAddRejectedWithRealStats_StatsBackToBaseAndFollowUpAppends()
        {
            BuildReal();
            Apply(A, Def(BuffX, ROW_DURATION, 0f, 0f, Mod(StatID.AttackPower, FLAT_OLD), Mod(StatID.Defense, FLAT_OLD)));
            Apply(A, Def(BuffY, ROW_DURATION, 0f, 0f, Mod(StatID.Defense, FLAT_OLD)));
            Assert.AreEqual(3, _service.GetOccupiedSlots(A));
            // Second add of the replacement is rejected (AddCalls is cumulative; read before the log is cleared).
            _recorder.FailOnAddNumber = _recorder.AddCalls + 2;
            _recorder.Calls.Clear();

            ApplyResult result = _service.ApplyEffect(A, A,
                Def(BuffX, NEW_DURATION, 0f, 0f, Mod(StatID.AttackPower, FLAT_NEW), Mod(StatID.Defense, FLAT_NEW)));

            Assert.AreEqual(ApplyResult.CapacityExceeded, result);
            Assert.AreEqual(5, _recorder.Calls.Count);
            AssertCall(0, LayerCallKind.Remove, StatID.AttackPower, BuffX);
            AssertCall(1, LayerCallKind.Remove, StatID.Defense, BuffX);
            AssertCall(2, LayerCallKind.Add, StatID.AttackPower, BuffX);
            AssertCall(3, LayerCallKind.Add, StatID.Defense, BuffX);
            AssertCall(4, LayerCallKind.Remove, StatID.AttackPower, BuffX);
            Assert.AreEqual(BASE_ATTACK, Effective(A, StatID.AttackPower), "old and half-added modifiers gone");
            Assert.AreEqual(BASE_DEFENSE + (int)FLAT_OLD, Effective(A, StatID.Defense), "only the other buff remains");
            Assert.AreEqual(1, _service.GetOccupiedSlots(A));

            Apply(A, ApDef(BuffZ, ROW_DURATION, FLAT_OLD));

            ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(A);
            Assert.AreEqual(2, rows.Length);
            Assert.AreEqual(BuffY, rows[0].BuffId);
            Assert.AreEqual(BuffZ, rows[1].BuffId, "fresh buff lands at the end");
            Assert.AreEqual(2, _service.GetOccupiedSlots(A));
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_OLD, Effective(A, StatID.AttackPower));
        }

        [Test]
        public void StatusEffects_Refresh_ReplacementThrowsOutsideTick_RethrowsAndFreesRow()
        {
            BuildReal();
            Apply(A, ApDef(BuffX, ROW_DURATION, FLAT_OLD));
            Apply(A, Def(BuffY, ROW_DURATION, 0f, 0f, Mod(StatID.Defense, FLAT_OLD)));
            Apply(A, Def(BuffZ, ROW_DURATION, 0f, 0f, Mod(StatID.Strength, FLAT_OLD)));
            _recorder.ThrowOnAddNumber = _recorder.AddCalls + 1;
            _recorder.Calls.Clear();

            Assert.Throws<InvalidOperationException>(
                () => _service.ApplyEffect(A, A, ApDef(BuffX, NEW_DURATION, FLAT_NEW)));

            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffX, out _), "row freed");
            ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(A);
            Assert.AreEqual(2, rows.Length);
            Assert.AreEqual(BuffY, rows[0].BuffId);
            Assert.AreEqual(BuffZ, rows[1].BuffId, "later row shifted down");
            Assert.AreEqual(2, _service.GetOccupiedSlots(A), "occupancy drops by the old row's modifier count");
            Assert.AreEqual(BASE_ATTACK, Effective(A, StatID.AttackPower));
            Assert.AreEqual(2, CallsSinceClear(LayerCallKind.Remove), "old removal + rollback of the throwing add");
            Assert.AreEqual(1, CallsSinceClear(LayerCallKind.Add));
        }

        private void AssertCall(int index, LayerCallKind kind, StatID stat, BuffID buff)
        {
            LayerCall call = _recorder.Calls[index];
            Assert.AreEqual(kind, call.Kind, "call " + index + " kind");
            Assert.AreEqual(stat, call.Stat, "call " + index + " stat");
            Assert.AreEqual(buff, call.Buff, "call " + index + " buff");
        }
    }
}
