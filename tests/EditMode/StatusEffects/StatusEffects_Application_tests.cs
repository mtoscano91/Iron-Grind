using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>
    /// EditMode unit tests for Status Effects Story 003 - application through the Character Stats seam,
    /// atomic rollback, caster attribution, repeated-stat rejection
    /// (CR-SE-2, CR-SE-3, CR-SE-13, EC-SE-11; AC-SE-01/05/08/09/31).
    /// </summary>
    [TestFixture]
    internal sealed class StatusEffects_Application_Tests
    {
        private const uint TARGET_RAW_ID = 71u;
        private const uint CASTER_RAW_ID = 42u;
        private const uint UNKNOWN_RAW_ID = 98u;
        private const int TARGET_SLOT = 6;
        private const uint BUFF_RAW_ID = 700u;
        private const uint SECOND_BUFF_RAW_ID = 701u;
        private const uint REENTRANT_BUFF_RAW_ID = 703u;
        private const uint UNREGISTERED_RAW_ID = 77u;
        private const int UNDEFINED_STAT_VALUE = 200;
        private const int THROWING_EVENT_NUMBER = 2; // the event for the 2nd modifier's add
        private const uint SEED_BUFF_FIRST_RAW_ID = 9000u;
        private const uint DURATION = 40u;
        private const int ENTRY_CAPACITY_PER_STAT = 32;

        private const int BASE_ATTACK = 20;
        private const int BASE_MAX_HP = 300;
        private const int BASE_MAX_MP = 80;
        private const int BASE_PRIMARY = 10;
        private const float FLAT_ATTACK = 10f;
        private const float FLAT_MAX_HP = 100f;
        private const float FLAT_MAX_MP = 50f;
        private const float FLAT_OTHER = 3f;
        private const float PCT_NONE = 0f;
        private const float PCT_SMALL = 0.05f;
        private const float REGEN_HP = 5f;

        private static readonly EntityID Target = new EntityID(TARGET_RAW_ID);
        private static readonly EntityID Caster = new EntityID(CASTER_RAW_ID);
        private static readonly EntityID Unknown = new EntityID(UNKNOWN_RAW_ID);
        private static readonly BuffID Buff = (BuffID)BUFF_RAW_ID;

        private RecordingBuffModifierLayer _layer;
        private DictionaryEntitySlotIndex _slots;
        private StatusEffectsService _service;

        [SetUp]
        public void SetUp()
        {
            _layer = new RecordingBuffModifierLayer();
            _slots = new DictionaryEntitySlotIndex();
            _slots.Add(Target, TARGET_SLOT);
            _service = new StatusEffectsService(_layer, _layer, _slots, new FakeEntityDeathSource());
        }

        // ---- Helpers ----

        private static BuffDefinition Def(params StatModifier[] modifiers)
        {
            return new BuffDefinition(Buff, modifiers, DURATION, 0f, 0f);
        }

        private static StatModifier Mod(StatID stat, float flat, float pct = PCT_NONE)
        {
            return new StatModifier(stat, flat, pct);
        }

        private static BuffDefinition ThreeStatDef()
        {
            return Def(
                Mod(StatID.AttackPower, FLAT_OTHER),
                Mod(StatID.MaxHP, FLAT_OTHER),
                Mod(StatID.Defense, FLAT_OTHER));
        }

        /// <summary>Real CharacterStats with base values for the stats these tests touch, plus a service over it.</summary>
        private StatusEffectsService RealService(out IronGrind.CharacterStats.CharacterStats stats)
        {
            stats = CharacterStatsFixture.Create();
            stats.SetBaseStat(Target, StatID.AttackPower, BASE_ATTACK);
            stats.SetBaseStat(Target, StatID.MaxHP, BASE_MAX_HP);
            stats.SetBaseStat(Target, StatID.MaxMP, BASE_MAX_MP);
            stats.SetBaseStat(Target, StatID.Defense, BASE_PRIMARY);
            return new StatusEffectsService(
                new CharacterStatsBuffModifierLayer(stats), new CharacterStatsResourceSink(stats), _slots, new FakeEntityDeathSource());
        }

        private static BuffDefinition DefFor(BuffID id, params StatModifier[] modifiers)
        {
            return new BuffDefinition(id, modifiers, DURATION, 0f, 0f);
        }

        private static int BuffCount(IronGrind.CharacterStats.CharacterStats stats, StatID stat)
        {
            return stats.EntityBuffCount.TryGetValue(Target, out int[] counts) ? counts[(int)stat] : 0;
        }

        /// <summary>Seeds a full (32-entry) buff stack on MaxHP so the 2nd modifier of ThreeStatDef is rejected.</summary>
        private static void FillMaxHpStack(IronGrind.CharacterStats.CharacterStats stats)
        {
            var seeds = new BuffModifierEntry[ENTRY_CAPACITY_PER_STAT];
            for (int i = 0; i < seeds.Length; i++)
                seeds[i] = new BuffModifierEntry(0f, PCT_NONE, (int)DURATION, (BuffID)(SEED_BUFF_FIRST_RAW_ID + (uint)i));
            CharacterStatsFixture.SetBuffModifiers(stats, Target, StatID.MaxHP, seeds);
        }

        private void AssertNoRowNoOccupancy()
        {
            Assert.AreEqual(0, _service.GetActiveEffectCount(Target), "row count");
            Assert.AreEqual(0, _service.GetOccupiedSlots(Target), "occupancy");
            Assert.IsFalse(_service.TryGetActiveEffect(Target, Buff, out _), "row present");
        }

        private void AssertCall(int index, LayerCallKind kind, StatID stat)
        {
            LayerCall call = _layer.Calls[index];
            Assert.AreEqual(kind, call.Kind, "kind of call " + index);
            Assert.AreEqual(Target, call.Entity, "entity of call " + index);
            Assert.AreEqual(stat, call.Stat, "stat of call " + index);
            Assert.AreEqual(Buff, call.Buff, "buff of call " + index);
        }

        // ---- Seam / adapter with real CharacterStats ----

        [Test]
        public void StatusEffects_Application_Adapter_NullStatsThrows()
        {
            Assert.Throws<System.ArgumentNullException>(() => new CharacterStatsBuffModifierLayer(null));
        }

        [Test]
        public void StatusEffects_Application_Adapter_NormalAdd_ReturnsTrueAndChangesEffectiveStat()
        {
            RealService(out var stats);
            var adapter = new CharacterStatsBuffModifierLayer(stats);

            bool added = adapter.TryAddBuffModifier(Target, StatID.AttackPower,
                new BuffModifierEntry(FLAT_ATTACK, PCT_NONE, (int)DURATION, Buff));

            Assert.IsTrue(added);
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_ATTACK, stats.GetEffectiveStat(Target, StatID.AttackPower));
        }

        [Test]
        public void StatusEffects_Application_Adapter_WriteLockedStat_ReturnsFalse()
        {
            RealService(out var stats);
            var adapter = new CharacterStatsBuffModifierLayer(stats);
            LogAssert.Expect(LogType.Error, new Regex("write-locked"));

            bool added = adapter.TryAddBuffModifier(Target, StatID.CurrentHP,
                new BuffModifierEntry(FLAT_ATTACK, PCT_NONE, (int)DURATION, Buff));

            Assert.IsFalse(added);
        }

        [Test]
        public void StatusEffects_Application_Adapter_ThirtyThirdAddOnOneStat_ReturnsFalse()
        {
            RealService(out var stats);
            var adapter = new CharacterStatsBuffModifierLayer(stats);
            for (int i = 0; i < ENTRY_CAPACITY_PER_STAT; i++)
            {
                var seed = new BuffModifierEntry(0f, PCT_NONE, (int)DURATION, (BuffID)(SEED_BUFF_FIRST_RAW_ID + (uint)i));
                Assert.IsTrue(adapter.TryAddBuffModifier(Target, StatID.AttackPower, seed), "add " + i);
            }
            LogAssert.Expect(LogType.Error, new Regex("at capacity"));

            bool added = adapter.TryAddBuffModifier(Target, StatID.AttackPower,
                new BuffModifierEntry(FLAT_ATTACK, PCT_NONE, (int)DURATION, Buff));

            Assert.IsFalse(added);
            Assert.AreEqual(BASE_ATTACK, stats.GetEffectiveStat(Target, StatID.AttackPower));
        }

        [Test]
        public void StatusEffects_Application_Adapter_Remove_ForwardsToCharacterStats()
        {
            RealService(out var stats);
            var adapter = new CharacterStatsBuffModifierLayer(stats);
            adapter.TryAddBuffModifier(Target, StatID.AttackPower,
                new BuffModifierEntry(FLAT_ATTACK, PCT_NONE, (int)DURATION, Buff));

            adapter.RemoveBuffModifier(Target, StatID.AttackPower, Buff);

            Assert.AreEqual(BASE_ATTACK, stats.GetEffectiveStat(Target, StatID.AttackPower));
        }

        // ---- AC-SE-01 ----

        [Test]
        public void StatusEffects_Application_SingleModifier_RecordingFake_OneAddWithExactArguments() // AC-SE-01
        {
            ApplyResult result = _service.ApplyEffect(Target, Caster, Def(Mod(StatID.AttackPower, FLAT_ATTACK)));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(1, _layer.TotalCalls);
            Assert.AreEqual(1, _layer.AddCalls);
            AssertCall(0, LayerCallKind.Add, StatID.AttackPower);
            BuffModifierEntry entry = _layer.Calls[0].Entry;
            Assert.AreEqual(FLAT_ATTACK, entry.FlatBonus);
            Assert.AreEqual(PCT_NONE, entry.PctBonus);
            Assert.AreEqual((int)DURATION, entry.DurationTicks);
            Assert.AreEqual(Buff, entry.Id);
        }

        [Test]
        public void StatusEffects_Application_SingleModifier_RealCharacterStats_EffectiveStatAndRow() // AC-SE-01
        {
            StatusEffectsService service = RealService(out var stats);

            ApplyResult result = service.ApplyEffect(Target, Caster, Def(Mod(StatID.AttackPower, FLAT_ATTACK)));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_ATTACK, stats.GetEffectiveStat(Target, StatID.AttackPower));
            Assert.AreEqual(1, service.GetActiveEffectCount(Target));
            Assert.IsTrue(service.TryGetActiveEffect(Target, Buff, out ActiveEffect row));
            Assert.AreEqual(DURATION, row.TicksRemaining);
        }

        // ---- AC-SE-05 atomic rollback ----

        [Test]
        public void StatusEffects_Application_Rollback_SecondOfThreeFails_RemovesFirstAndSkipsThird() // AC-SE-05
        {
            _layer.FailOnAddNumber = 2;

            ApplyResult result = _service.ApplyEffect(Target, Caster, ThreeStatDef());

            Assert.AreEqual(ApplyResult.CapacityExceeded, result);
            Assert.AreEqual(3, _layer.Calls.Count);
            AssertCall(0, LayerCallKind.Add, StatID.AttackPower);
            AssertCall(1, LayerCallKind.Add, StatID.MaxHP);
            AssertCall(2, LayerCallKind.Remove, StatID.AttackPower);
            AssertNoRowNoOccupancy();
        }

        [Test]
        public void StatusEffects_Application_Rollback_FirstOfThreeFails_NoRemoves()
        {
            _layer.FailOnAddNumber = 1;

            ApplyResult result = _service.ApplyEffect(Target, Caster, ThreeStatDef());

            Assert.AreEqual(ApplyResult.CapacityExceeded, result);
            Assert.AreEqual(1, _layer.Calls.Count);
            AssertCall(0, LayerCallKind.Add, StatID.AttackPower);
            Assert.AreEqual(0, _layer.RemoveCalls);
            AssertNoRowNoOccupancy();
        }

        [Test]
        public void StatusEffects_Application_Rollback_ThirdOfThreeFails_RemovesSecondThenFirst()
        {
            _layer.FailOnAddNumber = 3;

            ApplyResult result = _service.ApplyEffect(Target, Caster, ThreeStatDef());

            Assert.AreEqual(ApplyResult.CapacityExceeded, result);
            Assert.AreEqual(5, _layer.Calls.Count);
            AssertCall(0, LayerCallKind.Add, StatID.AttackPower);
            AssertCall(1, LayerCallKind.Add, StatID.MaxHP);
            AssertCall(2, LayerCallKind.Add, StatID.Defense);
            AssertCall(3, LayerCallKind.Remove, StatID.MaxHP);
            AssertCall(4, LayerCallKind.Remove, StatID.AttackPower);
            AssertNoRowNoOccupancy();
        }

        [Test]
        public void StatusEffects_Application_Rollback_RealCharacterStatsSecondStatFull_EffectiveValuesUnchanged() // AC-SE-05
        {
            StatusEffectsService service = RealService(out var stats);
            var seeds = new BuffModifierEntry[ENTRY_CAPACITY_PER_STAT];
            for (int i = 0; i < seeds.Length; i++)
                seeds[i] = new BuffModifierEntry(0f, PCT_NONE, (int)DURATION, (BuffID)(SEED_BUFF_FIRST_RAW_ID + (uint)i));
            CharacterStatsFixture.SetBuffModifiers(stats, Target, StatID.MaxHP, seeds); // 2nd stat of the definition is full
            int attackBefore = stats.GetEffectiveStat(Target, StatID.AttackPower);
            int hpBefore = stats.GetEffectiveStat(Target, StatID.MaxHP);
            int defenseBefore = stats.GetEffectiveStat(Target, StatID.Defense);
            LogAssert.Expect(LogType.Error, new Regex("at capacity"));

            ApplyResult result = service.ApplyEffect(Target, Caster, ThreeStatDef());

            Assert.AreEqual(ApplyResult.CapacityExceeded, result);
            Assert.AreEqual(attackBefore, stats.GetEffectiveStat(Target, StatID.AttackPower));
            Assert.AreEqual(hpBefore, stats.GetEffectiveStat(Target, StatID.MaxHP));
            Assert.AreEqual(defenseBefore, stats.GetEffectiveStat(Target, StatID.Defense));
            Assert.AreEqual(0, service.GetActiveEffectCount(Target));
            Assert.AreEqual(0, service.GetOccupiedSlots(Target));
            Assert.AreEqual(0, BuffCount(stats, StatID.AttackPower), "AttackPower buff count back to 0");
            Assert.AreEqual(ENTRY_CAPACITY_PER_STAT, BuffCount(stats, StatID.MaxHP), "seeded MaxHP stack untouched");
            Assert.AreEqual(0, BuffCount(stats, StatID.Defense), "Defense never touched");
        }

        [Test]
        public void StatusEffects_Application_Rollback_RealStatsSecondStatFull_EventSequenceIsAddThenRollbackRemoval() // AC-SE-05
        {
            StatusEffectsService service = RealService(out var stats);
            FillMaxHpStack(stats);
            var events = new List<(EntityID Entity, StatID Stat)>();
            IronGrind.CharacterStats.CharacterStats.StatChangedHandler handler = (e, s) => events.Add((e, s));
            stats.Subscribe(handler);
            // Character Stats fires OnStatChanged only for an add that changes a stack (and for a removal that finds
            // an entry). The rejected MaxHP add (stack full) logs an error and fires nothing; the 3rd stat is never tried.
            LogAssert.Expect(LogType.Error, new Regex("at capacity"));

            try
            {
                ApplyResult result = service.ApplyEffect(Target, Caster, ThreeStatDef());

                Assert.AreEqual(ApplyResult.CapacityExceeded, result);
            }
            finally
            {
                stats.Unsubscribe(handler);
            }

            Assert.AreEqual(2, events.Count, "add event + rollback removal event only");
            Assert.AreEqual(Target, events[0].Entity);
            Assert.AreEqual(StatID.AttackPower, events[0].Stat, "event for the add");
            Assert.AreEqual(Target, events[1].Entity);
            Assert.AreEqual(StatID.AttackPower, events[1].Stat, "event for the rollback removal");
        }

        [Test]
        public void StatusEffects_Application_Rollback_SubscriberThrowsOnSecondAdd_RemovesAllAndRethrows() // AC-SE-05
        {
            StatusEffectsService service = RealService(out var stats);
            int attackBefore = stats.GetEffectiveStat(Target, StatID.AttackPower);
            int hpBefore = stats.GetEffectiveStat(Target, StatID.MaxHP);
            int defenseBefore = stats.GetEffectiveStat(Target, StatID.Defense);
            int eventCount = 0;
            bool armed = true;
            IronGrind.CharacterStats.CharacterStats.StatChangedHandler handler = (e, s) =>
            {
                if (!armed) return;
                eventCount++;
                if (eventCount == THROWING_EVENT_NUMBER)
                {
                    armed = false; // disarm so the rollback removals do not throw
                    throw new System.InvalidOperationException("subscriber failure");
                }
            };
            stats.Subscribe(handler);

            try
            {
                Assert.Throws<System.InvalidOperationException>(() => service.ApplyEffect(Target, Caster, ThreeStatDef()));
            }
            finally
            {
                stats.Unsubscribe(handler);
            }

            // Character Stats stores the 2nd entry before firing, so rollback must remove it too.
            Assert.AreEqual(0, BuffCount(stats, StatID.AttackPower), "AttackPower buff count");
            Assert.AreEqual(0, BuffCount(stats, StatID.MaxHP), "MaxHP buff count");
            Assert.AreEqual(0, BuffCount(stats, StatID.Defense), "Defense buff count");
            Assert.AreEqual(attackBefore, stats.GetEffectiveStat(Target, StatID.AttackPower));
            Assert.AreEqual(hpBefore, stats.GetEffectiveStat(Target, StatID.MaxHP));
            Assert.AreEqual(defenseBefore, stats.GetEffectiveStat(Target, StatID.Defense));
            Assert.AreEqual(0, service.GetActiveEffectCount(Target));
            Assert.AreEqual(0, service.GetOccupiedSlots(Target));
            Assert.IsFalse(service.TryGetActiveEffect(Target, Buff, out _));
        }

        [Test]
        public void StatusEffects_Application_Rollback_PreExistingEffect_LeavesEarlierRowUntouched() // AC-SE-05
        {
            BuffID second = (BuffID)SECOND_BUFF_RAW_ID;
            Assert.AreEqual(ApplyResult.Success,
                _service.ApplyEffect(Target, Caster, DefFor(Buff, Mod(StatID.AttackPower, FLAT_ATTACK))));
            _service.TryGetActiveEffect(Target, Buff, out ActiveEffect rowBefore);
            // FailOnAddNumber counts every add on the fake, including buff A's one add.
            _layer.FailOnAddNumber = _layer.AddCalls + 2;

            ApplyResult result = _service.ApplyEffect(Target, Caster, DefFor(second,
                Mod(StatID.MaxHP, FLAT_OTHER), Mod(StatID.MaxMP, FLAT_OTHER), Mod(StatID.Defense, FLAT_OTHER)));

            Assert.AreEqual(ApplyResult.CapacityExceeded, result);
            Assert.AreEqual(1, _service.GetActiveEffectCount(Target), "row count");
            Assert.AreEqual(1, _service.GetOccupiedSlots(Target), "occupancy");
            Assert.IsFalse(_service.TryGetActiveEffect(Target, second, out _), "failed buff has no row");
            Assert.IsTrue(_service.TryGetActiveEffect(Target, Buff, out ActiveEffect rowAfter));
            Assert.AreEqual(rowBefore.BuffId, rowAfter.BuffId);
            Assert.AreEqual(rowBefore.ModifierCount, rowAfter.ModifierCount);
            Assert.AreEqual(rowBefore.TicksRemaining, rowAfter.TicksRemaining);
            Assert.AreEqual(rowBefore.CasterEntityId, rowAfter.CasterEntityId);
        }

        // ---- Re-entrancy (call made from an OnStatChanged subscriber) ----

        [Test]
        public void StatusEffects_Application_Reentrant_ApplyFromSubscriber_InnerRejectedOuterSucceeds()
        {
            // In the Editor / development builds Character Stats throws InvalidOperationException ("Re-entrance")
            // on a write made inside an event handler (IsFiringAndAssert), instead of logging; nothing is logged.
            // The inner apply therefore throws (its rollback removal is also rejected the same way) rather than
            // returning CapacityExceeded, which is the release-build result. The subscriber catches it so the
            // outer apply can finish.
            StatusEffectsService service = RealService(out var stats);
            BuffID reentrant = (BuffID)REENTRANT_BUFF_RAW_ID;
            BuffDefinition inner = DefFor(reentrant, Mod(StatID.Defense, FLAT_OTHER));
            System.Exception caught = null;
            bool armed = true;
            IronGrind.CharacterStats.CharacterStats.StatChangedHandler handler = (e, s) =>
            {
                if (!armed) return;
                armed = false;
                try
                {
                    service.ApplyEffect(Target, Caster, inner);
                }
                catch (System.InvalidOperationException ex)
                {
                    caught = ex;
                }
            };
            stats.Subscribe(handler);

            ApplyResult outer;
            try
            {
                outer = service.ApplyEffect(Target, Caster, DefFor(Buff, Mod(StatID.AttackPower, FLAT_ATTACK)));
            }
            finally
            {
                stats.Unsubscribe(handler);
            }

            Assert.AreEqual(ApplyResult.Success, outer);
            Assert.IsNotNull(caught, "inner apply must be rejected by Character Stats");
            StringAssert.Contains("Re-entrance", caught.Message);
            Assert.IsFalse(service.TryGetActiveEffect(Target, reentrant, out _), "no row for the inner buff");
            Assert.AreEqual(0, BuffCount(stats, StatID.Defense), "Defense buff count");
            Assert.IsTrue(service.TryGetActiveEffect(Target, Buff, out _), "outer row present");
            Assert.AreEqual(1, service.GetActiveEffectCount(Target));
            Assert.AreEqual(1, service.GetOccupiedSlots(Target));
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_ATTACK, stats.GetEffectiveStat(Target, StatID.AttackPower));
        }

        // ---- AC-SE-09 ----

        [Test]
        public void StatusEffects_Application_MaxHp_FlatBonusShowsInEffectiveStat() // AC-SE-09
        {
            StatusEffectsService service = RealService(out var stats);

            ApplyResult result = service.ApplyEffect(Target, Caster, Def(Mod(StatID.MaxHP, FLAT_MAX_HP)));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(BASE_MAX_HP + (int)FLAT_MAX_HP, stats.GetEffectiveStat(Target, StatID.MaxHP));
        }

        [Test]
        public void StatusEffects_Application_MaxMp_FlatBonusShowsInEffectiveStat() // AC-SE-09
        {
            StatusEffectsService service = RealService(out var stats);

            ApplyResult result = service.ApplyEffect(Target, Caster, Def(Mod(StatID.MaxMP, FLAT_MAX_MP)));

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(BASE_MAX_MP + (int)FLAT_MAX_MP, stats.GetEffectiveStat(Target, StatID.MaxMP));
        }

        // ---- AC-SE-31 caster attribution / read-only accessors ----

        [Test]
        public void StatusEffects_Application_Caster_StoredAndReadableWithoutLayerCalls() // AC-SE-31
        {
            _service.ApplyEffect(Target, Caster, Def(Mod(StatID.AttackPower, FLAT_ATTACK)));
            int callsBefore = _layer.TotalCalls;

            bool found = _service.TryGetActiveEffect(Target, Buff, out ActiveEffect row);
            System.ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(Target);

            Assert.IsTrue(found);
            Assert.AreEqual(Caster, row.CasterEntityId);
            Assert.AreEqual(1, rows.Length);
            Assert.AreEqual(Caster, rows[0].CasterEntityId);
            Assert.AreEqual(Buff, rows[0].BuffId);
            Assert.AreEqual(callsBefore, _layer.TotalCalls);
        }

        [Test]
        public void StatusEffects_Application_Accessors_UnknownEntity_ReturnFalseAndEmpty() // AC-SE-31
        {
            Assert.IsFalse(_service.TryGetActiveEffect(Unknown, Buff, out _));
            Assert.AreEqual(0, _service.GetActiveEffects(Unknown).Length);
        }

        // ---- Regen-only ----

        [Test]
        public void StatusEffects_Application_RegenOnly_SucceedsWithRowAndZeroAdds()
        {
            var regenOnly = new BuffDefinition(Buff, System.ReadOnlySpan<StatModifier>.Empty, DURATION, REGEN_HP, 0f);

            ApplyResult result = _service.ApplyEffect(Target, Caster, regenOnly);

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(1, _service.GetActiveEffectCount(Target));
            Assert.IsTrue(_service.TryGetActiveEffect(Target, Buff, out _));
            Assert.AreEqual(0, _layer.AddCalls);
        }

        // ---- Snapshot ----

        [Test]
        public void StatusEffects_Application_Snapshot_RowModifiersEqualDefinitionNowAndOnLaterRead()
        {
            BuffDefinition def = Def(Mod(StatID.AttackPower, FLAT_ATTACK, PCT_SMALL), Mod(StatID.MaxHP, FLAT_MAX_HP));
            _service.ApplyEffect(Target, Caster, def);

            _service.TryGetActiveEffect(Target, Buff, out ActiveEffect first);
            _service.TryGetActiveEffect(Target, Buff, out ActiveEffect later);

            Assert.AreEqual(def.ModifierCount, first.ModifierCount);
            for (int i = 0; i < def.ModifierCount; i++)
            {
                StatModifier expected = def.GetModifier(i);
                Assert.AreEqual(expected.StatId, first.GetModifier(i).StatId, "stat " + i);
                Assert.AreEqual(expected.FlatBonus, first.GetModifier(i).FlatBonus, "flat " + i);
                Assert.AreEqual(expected.PctBonus, first.GetModifier(i).PctBonus, "pct " + i);
                Assert.AreEqual(first.GetModifier(i).StatId, later.GetModifier(i).StatId, "later stat " + i);
                Assert.AreEqual(first.GetModifier(i).FlatBonus, later.GetModifier(i).FlatBonus, "later flat " + i);
                Assert.AreEqual(first.GetModifier(i).PctBonus, later.GetModifier(i).PctBonus, "later pct " + i);
            }
        }

        // ---- Repeated stat (CR-SE-2, EC-SE-11) ----

        [Test]
        public void StatusEffects_Application_RepeatedStat_AdjacentAttackPower_ReturnsInvalidStat()
        {
            ApplyResult result = _service.ApplyEffect(Target, Caster,
                Def(Mod(StatID.AttackPower, FLAT_ATTACK), Mod(StatID.AttackPower, 0f, PCT_SMALL)));

            Assert.AreEqual(ApplyResult.InvalidStat, result);
            Assert.AreEqual(0, _layer.TotalCalls);
            AssertNoRowNoOccupancy();
        }

        [Test]
        public void StatusEffects_Application_RepeatedStat_NonAdjacent_ReturnsInvalidStat()
        {
            ApplyResult result = _service.ApplyEffect(Target, Caster,
                Def(Mod(StatID.AttackPower, FLAT_ATTACK), Mod(StatID.MaxHP, FLAT_MAX_HP), Mod(StatID.AttackPower, FLAT_ATTACK)));

            Assert.AreEqual(ApplyResult.InvalidStat, result);
            Assert.AreEqual(0, _layer.TotalCalls);
            AssertNoRowNoOccupancy();
        }

        // ---- Undefined stat values ----

        [Test]
        public void StatusEffects_Application_UndefinedStat_SecondOfTwo_ReturnsInvalidStatWithNoLayerCalls()
        {
            ApplyResult result = _service.ApplyEffect(Target, Caster,
                Def(Mod(StatID.AttackPower, FLAT_ATTACK), Mod((StatID)UNDEFINED_STAT_VALUE, FLAT_ATTACK)));

            Assert.AreEqual(ApplyResult.InvalidStat, result);
            Assert.AreEqual(0, _layer.TotalCalls);
            AssertNoRowNoOccupancy();
        }

        [Test]
        public void StatusEffects_Application_UndefinedStat_FirstValueAboveRange_ReturnsInvalidStat()
        {
            // StatSlotCount is the first value outside the defined range (boundary).
            StatID boundary = (StatID)IronGrind.CharacterStats.CharacterStats.StatSlotCount;

            ApplyResult result = _service.ApplyEffect(Target, Caster, Def(Mod(boundary, FLAT_ATTACK)));

            Assert.AreEqual(ApplyResult.InvalidStat, result);
            Assert.AreEqual(0, _layer.TotalCalls);
            AssertNoRowNoOccupancy();
        }

        // ---- Adapter with an unregistered entity ----

        [Test]
        public void StatusEffects_Application_Adapter_EntityNeverRegisteredInStats_AddReturnsTrue()
        {
            // Character Stats creates per-entity buff storage lazily on the first AddBuffModifier, so an entity with
            // no base stats still accepts a modifier. Pinned here: the adapter does not treat it as a failure.
            var stats = CharacterStatsFixture.Create();
            var adapter = new CharacterStatsBuffModifierLayer(stats);
            var unregistered = new EntityID(UNREGISTERED_RAW_ID);

            bool added = adapter.TryAddBuffModifier(unregistered, StatID.AttackPower,
                new BuffModifierEntry(FLAT_ATTACK, PCT_NONE, (int)DURATION, Buff));

            Assert.IsTrue(added);
        }

        // ---- Two buffs on one entity ----

        [Test]
        public void StatusEffects_Application_TwoDistinctBuffs_RowsInApplyOrderAndOccupancySummed()
        {
            BuffID second = (BuffID)SECOND_BUFF_RAW_ID;
            _service.ApplyEffect(Target, Caster, DefFor(Buff, Mod(StatID.AttackPower, FLAT_ATTACK)));
            _service.ApplyEffect(Target, Caster,
                DefFor(second, Mod(StatID.MaxHP, FLAT_MAX_HP), Mod(StatID.MaxMP, FLAT_MAX_MP)));

            System.ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(Target);

            Assert.AreEqual(2, rows.Length);
            Assert.AreEqual(Buff, rows[0].BuffId, "first applied is first");
            Assert.AreEqual(second, rows[1].BuffId, "second applied is second");
            Assert.AreEqual(3, _service.GetOccupiedSlots(Target));
        }

        [Test]
        public void StatusEffects_Application_RepeatedStat_CurrentHpTwice_ReturnsInvalidStat()
        {
            ApplyResult result = _service.ApplyEffect(Target, Caster,
                Def(Mod(StatID.CurrentHP, FLAT_ATTACK), Mod(StatID.CurrentHP, FLAT_ATTACK)));

            Assert.AreEqual(ApplyResult.InvalidStat, result);
            Assert.AreEqual(0, _layer.TotalCalls);
            AssertNoRowNoOccupancy();
        }
    }
}
