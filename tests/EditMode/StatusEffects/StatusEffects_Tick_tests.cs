using System;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>
    /// EditMode unit tests for Status Effects Story 005 - per-tick processing and expiry
    /// (CR-SE-6/7/8/9/16, F-SE-5, EC-SE-1/7/8/10; AC-SE-02/13/14/18/19/28).
    /// </summary>
    [TestFixture]
    internal sealed class StatusEffects_Tick_Tests
    {
        private const uint A_RAW_ID = 11u;
        private const uint B_RAW_ID = 12u;
        private const uint EMPTY_RAW_ID = 13u;
        private const int A_SLOT = 2;
        private const int B_SLOT = 5;
        private const int EMPTY_SLOT = 7;
        private const uint BUFF_ONE_RAW_ID = 1u;
        private const uint BUFF_TWO_RAW_ID = 2u;
        private const uint BUFF_THREE_RAW_ID = 3u;
        private const uint INJECTED_BUFF_RAW_ID = 900u;
        private const uint DURATION = 5u;
        private const uint SHORT_DURATION = 2u;
        private const uint ONE_TICK = 1u;
        private const uint LONG_DURATION = 20u;
        private const uint MID_DURATION = 10u;
        private const uint REGEN_DURATION = 3u;
        private const int BASE_ATTACK = 20;
        private const int BASE_DEFENSE = 10;
        private const int BASE_MAX_HP = 300;
        private const int BASE_MAX_MP = 80;
        private const float FLAT_MODIFIER = 10f;
        private const float FLAT_AP_SMALL = 10f;
        private const float FLAT_AP_LARGE = 15f;
        private const float DEBUFF_FLAT = -15f;
        private const float REGEN_HP = 5f;
        private const float REGEN_MP = 3f;
        private const float PCT_NONE = 0f;
        private const float SINK_HP_AMOUNT = 25f;
        private const float SINK_MP_AMOUNT = 10f;
        private const float REGEN_HP_SECOND = 7f;
        private const uint FILL_BUFF_FIRST_RAW_ID = 100u;
        private const int FULL_ROWS = StatusEffectsConstants.MAX_ACTIVE_BUFFS_PER_ENTITY;
        private const int MODIFIERS_PER_FULL_DEF = StatusEffectsConstants.MAX_STATS_PER_BUFF;

        private static readonly StatID[] DistinctStats =
        {
            StatID.Strength, StatID.Dexterity, StatID.Vitality, StatID.Intelligence,
            StatID.AttackPower, StatID.Defense, StatID.MagicDefense, StatID.MaxHP
        };

        private static readonly EntityID A = new EntityID(A_RAW_ID);
        private static readonly EntityID B = new EntityID(B_RAW_ID);
        private static readonly EntityID Empty = new EntityID(EMPTY_RAW_ID);
        private static readonly BuffID BuffOne = (BuffID)BUFF_ONE_RAW_ID;
        private static readonly BuffID BuffTwo = (BuffID)BUFF_TWO_RAW_ID;
        private static readonly BuffID BuffThree = (BuffID)BUFF_THREE_RAW_ID;
        private static readonly BuffID Injected = (BuffID)INJECTED_BUFF_RAW_ID;

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
            _slots.Add(Empty, EMPTY_SLOT);
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

        private void Apply(EntityID target, in BuffDefinition def)
        {
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(target, target, def));
        }

        private void Tick(int count)
        {
            for (int i = 0; i < count; i++)
                _service.OnServerTick();
        }

        /// <summary>Replaces the pure fake with a recording decorator over real CharacterStats.</summary>
        private void BuildReal()
        {
            _stats = CharacterStatsFixture.Create();
            _stats.SetBaseStat(A, StatID.AttackPower, BASE_ATTACK);
            _stats.SetBaseStat(A, StatID.Defense, BASE_DEFENSE);
            _stats.SetBaseStat(A, StatID.MaxHP, BASE_MAX_HP);
            _stats.SetBaseStat(A, StatID.MaxMP, BASE_MAX_MP);
            _recorder = new RecordingBuffModifierLayer(
                new CharacterStatsBuffModifierLayer(_stats), new CharacterStatsResourceSink(_stats));
            _service = new StatusEffectsService(_recorder, _recorder, _slots);
        }

        private int Effective(EntityID entity, StatID stat)
        {
            return _stats.GetEffectiveStat(entity, stat);
        }

        private int CountRemoves(BuffID buff)
        {
            int count = 0;
            for (int i = 0; i < _recorder.Calls.Count; i++)
            {
                if (_recorder.Calls[i].Kind == LayerCallKind.Remove && _recorder.Calls[i].Buff == buff)
                    count++;
            }
            return count;
        }

        private static BuffID FillBuff(int index)
        {
            return (BuffID)(FILL_BUFF_FIRST_RAW_ID + (uint)index);
        }

        /// <summary>A definition with one modifier on each of the first <paramref name="count"/> distinct stats.</summary>
        private static BuffDefinition WideDef(BuffID id, uint duration, int count)
        {
            var mods = new StatModifier[count];
            for (int i = 0; i < count; i++)
                mods[i] = Mod(DistinctStats[i], FLAT_MODIFIER);
            return Def(id, duration, 0f, 0f, mods);
        }

        private ActiveEffect Row(EntityID entity, BuffID buff)
        {
            Assert.IsTrue(_service.TryGetActiveEffect(entity, buff, out ActiveEffect row), "row " + buff + " missing");
            return row;
        }

        // ---- Per-row order, AC-SE-13 ----

        [Test]
        public void StatusEffects_Tick_DurationFive_CountsDownThenRowIsGone() // AC-SE-13
        {
            Apply(A, ApDef(BuffOne, DURATION, FLAT_AP_SMALL));

            for (uint expected = DURATION - 1u; expected >= 1u; expected--)
            {
                _service.OnServerTick();
                Assert.AreEqual(expected, Row(A, BuffOne).TicksRemaining);
            }
            Assert.AreEqual(0, _recorder.RemoveCalls, "no removal before the last tick");

            _service.OnServerTick();

            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffOne, out _));
            Assert.AreEqual(1, _recorder.RemoveCalls);
        }

        // ---- AC-SE-02 ----

        [Test]
        public void StatusEffects_Tick_ThreeModifierBuff_ExpiryRemovesThreeAndRestoresBase() // AC-SE-02
        {
            BuildReal();
            Apply(A, Def(BuffOne, DURATION, 0f, 0f,
                Mod(StatID.AttackPower, FLAT_MODIFIER), Mod(StatID.Defense, FLAT_MODIFIER), Mod(StatID.MaxHP, FLAT_MODIFIER)));
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_MODIFIER, Effective(A, StatID.AttackPower));

            Tick((int)DURATION - 1);
            Assert.AreEqual(0, _recorder.RemoveCalls, "nothing removed before expiry");
            Tick(1);

            Assert.AreEqual(3, _recorder.RemoveCalls);
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.AreEqual(BASE_ATTACK, Effective(A, StatID.AttackPower));
            Assert.AreEqual(BASE_DEFENSE, Effective(A, StatID.Defense));
            Assert.AreEqual(BASE_MAX_HP, Effective(A, StatID.MaxHP));
        }

        // ---- AC-SE-14 ----

        [Test]
        public void StatusEffects_Tick_RegenOnlyBuff_RegenCalledEveryTickIncludingExpiryTick() // AC-SE-14
        {
            Apply(A, Def(BuffOne, REGEN_DURATION, REGEN_HP, 0f));

            Tick((int)REGEN_DURATION);

            Assert.AreEqual((int)REGEN_DURATION, _recorder.RegenCalls);
            Assert.AreEqual(0, _recorder.ManaRegenCalls);
            Assert.AreEqual(0, _recorder.AddCalls);
            for (int i = 0; i < _recorder.Calls.Count; i++)
            {
                Assert.AreEqual(LayerCallKind.Regen, _recorder.Calls[i].Kind);
                Assert.AreEqual(A, _recorder.Calls[i].Entity);
                Assert.AreEqual(REGEN_HP, _recorder.Calls[i].Amount);
            }
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
        }

        // ---- AC-SE-18 ----

        [Test]
        public void StatusEffects_Tick_DebuffNegativeFlat_AppliesThenExpiresLikeABuff() // AC-SE-18, CR-SE-9
        {
            BuildReal();
            Apply(A, ApDef(BuffOne, DURATION, DEBUFF_FLAT));
            Assert.AreEqual(BASE_ATTACK + (int)DEBUFF_FLAT, Effective(A, StatID.AttackPower));

            Tick((int)DURATION);

            Assert.AreEqual(BASE_ATTACK, Effective(A, StatID.AttackPower));
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
        }

        // ---- AC-SE-28 ----

        [Test]
        public void StatusEffects_Tick_TwoBuffsSameStat_OnlyShorterOneIsRemoved() // AC-SE-28
        {
            BuildReal();
            Apply(A, ApDef(BuffOne, MID_DURATION, FLAT_AP_SMALL));
            Apply(A, ApDef(BuffTwo, LONG_DURATION, FLAT_AP_LARGE));
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_SMALL + (int)FLAT_AP_LARGE, Effective(A, StatID.AttackPower));

            Tick((int)MID_DURATION);

            Assert.AreEqual(1, CountRemoves(BuffOne));
            Assert.AreEqual(0, CountRemoves(BuffTwo));
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_LARGE, Effective(A, StatID.AttackPower));

            Tick((int)(LONG_DURATION - MID_DURATION));

            Assert.AreEqual(1, CountRemoves(BuffTwo));
            Assert.AreEqual(BASE_ATTACK, Effective(A, StatID.AttackPower));
        }

        // ---- AC-SE-19 / EC-SE-8 / skip mechanism ----

        [Test]
        public void StatusEffects_Tick_InjectIntoEntityBeingProcessed_NewRowNotProcessedThisTick() // AC-SE-19
        {
            Apply(A, ApDef(BuffOne, DURATION, FLAT_AP_SMALL));
            _service.InjectApplyEffectDuringTick(A, ApDef(Injected, DURATION, FLAT_AP_LARGE));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual(DURATION - 1u, Row(A, BuffOne).TicksRemaining, "existing row decremented once");
            Assert.AreEqual(DURATION, Row(A, Injected).TicksRemaining, "new row untouched this tick");

            _service.OnServerTick();

            Assert.AreEqual(DURATION - 1u, Row(A, Injected).TicksRemaining);
        }

        [Test]
        public void StatusEffects_Tick_InjectIntoLaterSlot_NewRowNotProcessedThisTick() // AC-SE-19
        {
            Apply(A, ApDef(BuffOne, DURATION, FLAT_AP_SMALL));
            Apply(B, ApDef(BuffTwo, DURATION, FLAT_AP_SMALL));
            _service.InjectApplyEffectDuringTick(B, ApDef(Injected, DURATION, FLAT_AP_LARGE), A);

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual(DURATION - 1u, Row(B, BuffTwo).TicksRemaining, "existing row of B decremented once");
            Assert.AreEqual(DURATION, Row(B, Injected).TicksRemaining);

            _service.OnServerTick();

            Assert.AreEqual(DURATION - 1u, Row(B, Injected).TicksRemaining);
        }

        [Test]
        public void StatusEffects_Tick_InjectIntoAlreadyProcessedSlot_NewRowNotProcessedThisTick() // AC-SE-19
        {
            Apply(A, ApDef(BuffOne, DURATION, FLAT_AP_SMALL));
            Apply(B, ApDef(BuffTwo, DURATION, FLAT_AP_SMALL));
            _service.InjectApplyEffectDuringTick(A, ApDef(Injected, DURATION, FLAT_AP_LARGE), B);

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual(DURATION - 1u, Row(A, BuffOne).TicksRemaining);
            Assert.AreEqual(DURATION, Row(A, Injected).TicksRemaining);

            _service.OnServerTick();

            Assert.AreEqual(DURATION - 2u, Row(A, BuffOne).TicksRemaining);
            Assert.AreEqual(DURATION - 1u, Row(A, Injected).TicksRemaining);
        }

        [Test]
        public void StatusEffects_Tick_InjectedOneTickEffect_SurvivesTheInjectionTick() // EC-SE-8 skip mechanism
        {
            Apply(A, ApDef(BuffOne, DURATION, FLAT_AP_SMALL));
            _service.InjectApplyEffectDuringTick(A, ApDef(Injected, ONE_TICK, FLAT_AP_LARGE));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual(ONE_TICK, Row(A, Injected).TicksRemaining, "would have expired if processed");
            Assert.AreEqual(2, _service.GetActiveEffectCount(A));

            _service.OnServerTick();

            Assert.IsFalse(_service.TryGetActiveEffect(A, Injected, out _));
        }

        [Test]
        public void StatusEffects_Tick_ExpiryAndInjectionSameTick_OldGoneNewKeptWithFullDuration() // edge
        {
            Apply(A, ApDef(BuffOne, ONE_TICK, FLAT_AP_SMALL));
            _service.InjectApplyEffectDuringTick(A, Def(Injected, DURATION, 0f, 0f,
                Mod(StatID.AttackPower, FLAT_AP_LARGE), Mod(StatID.Defense, FLAT_AP_LARGE)));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffOne, out _));
            Assert.AreEqual(1, _service.GetActiveEffectCount(A));
            Assert.AreEqual(DURATION, Row(A, Injected).TicksRemaining);
            Assert.AreEqual(2, _service.GetOccupiedSlots(A));
        }

        [Test]
        public void StatusEffects_Tick_InjectBeforeLaterExpiryInSameSlot_InjectedRowShiftedNotLost() // EC-SE-8, compaction
        {
            Apply(A, ApDef(BuffOne, DURATION, FLAT_AP_SMALL));
            Apply(A, ApDef(BuffTwo, ONE_TICK, FLAT_AP_SMALL));
            _service.InjectApplyEffectDuringTick(A, ApDef(Injected, DURATION, FLAT_AP_LARGE)); // fires after row 0

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(A);
            Assert.AreEqual(2, rows.Length);
            Assert.AreEqual(BuffOne, rows[0].BuffId);
            Assert.AreEqual(DURATION - 1u, rows[0].TicksRemaining);
            Assert.AreEqual(Injected, rows[1].BuffId, "the expiry shift must keep the row appended mid-tick");
            Assert.AreEqual(DURATION, rows[1].TicksRemaining);
            Assert.AreEqual(2, _service.GetOccupiedSlots(A));
        }

        [Test]
        public void StatusEffects_Tick_FullSlotRowZeroExpiresAndInjection_InjectedTakesLastRow() // compaction, EC-SE-8
        {
            Apply(A, ApDef(FillBuff(0), ONE_TICK, FLAT_AP_SMALL));
            for (int i = 1; i < FULL_ROWS; i++)
                Apply(A, ApDef(FillBuff(i), DURATION, FLAT_AP_SMALL));
            _service.InjectApplyEffectDuringTick(A, ApDef(Injected, DURATION, FLAT_AP_LARGE));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(A);
            Assert.AreEqual(FULL_ROWS, rows.Length);
            for (int i = 0; i < FULL_ROWS - 1; i++)
            {
                Assert.AreEqual(FillBuff(i + 1), rows[i].BuffId, "order kept after the shift");
                Assert.AreEqual(DURATION - 1u, rows[i].TicksRemaining);
            }
            Assert.AreEqual(Injected, rows[FULL_ROWS - 1].BuffId);
            Assert.AreEqual(DURATION, rows[FULL_ROWS - 1].TicksRemaining);
        }

        [Test]
        public void StatusEffects_Tick_FullSlotNoExpiry_InjectionReportsSlotsFullAndTickCompletes() // EC-SE-8
        {
            for (int i = 0; i < FULL_ROWS; i++)
                Apply(A, ApDef(FillBuff(i), DURATION, FLAT_AP_SMALL));
            Apply(B, Def(BuffTwo, DURATION, REGEN_HP, 0f));
            _service.InjectApplyEffectDuringTick(A, ApDef(Injected, DURATION, FLAT_AP_LARGE));

            _service.OnServerTick();

            Assert.AreEqual(ApplyResult.EffectSlotsFull, _service.LastInjectedResult);
            ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(A);
            Assert.AreEqual(FULL_ROWS, rows.Length);
            for (int i = 0; i < FULL_ROWS; i++)
                Assert.AreEqual(DURATION - 1u, rows[i].TicksRemaining, "every row still processed");
            Assert.AreEqual(DURATION - 1u, Row(B, BuffTwo).TicksRemaining, "later slots still processed");
            Assert.AreEqual(1, _recorder.RegenCalls);
        }

        [Test]
        public void StatusEffects_Tick_InjectionDuringTurnOfEntityWithoutRows_DoesNotFire() // test seam
        {
            Apply(A, ApDef(BuffOne, DURATION, FLAT_AP_SMALL));
            _service.InjectApplyEffectDuringTick(A, ApDef(Injected, DURATION, FLAT_AP_LARGE), Empty);

            _service.OnServerTick();

            Assert.IsNull(_service.LastInjectedResult);
            Assert.IsFalse(_service.TryGetActiveEffect(A, Injected, out _));
            Assert.AreEqual(DURATION - 1u, Row(A, BuffOne).TicksRemaining);
        }

        // ---- Compaction ----

        [Test]
        public void StatusEffects_Tick_MiddleRowExpires_RowsCompactedInOrder() // compaction
        {
            Apply(A, Def(BuffOne, DURATION, 0f, 0f, Mod(StatID.AttackPower, FLAT_MODIFIER)));
            Apply(A, Def(BuffTwo, SHORT_DURATION, 0f, 0f, Mod(StatID.Defense, FLAT_MODIFIER), Mod(StatID.MaxHP, FLAT_MODIFIER)));
            Apply(A, Def(BuffThree, DURATION, 0f, 0f, Mod(StatID.MaxMP, FLAT_MODIFIER)));
            Assert.AreEqual(4, _service.GetOccupiedSlots(A));

            Tick((int)SHORT_DURATION);

            ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(A);
            Assert.AreEqual(2, rows.Length);
            Assert.AreEqual(BuffOne, rows[0].BuffId);
            Assert.AreEqual(BuffThree, rows[1].BuffId);
            Assert.AreEqual(DURATION - SHORT_DURATION, rows[0].TicksRemaining);
            Assert.AreEqual(DURATION - SHORT_DURATION, rows[1].TicksRemaining);
            Assert.AreEqual(2, _service.GetOccupiedSlots(A), "occupancy drops by the expired row's two modifiers");
            Assert.AreEqual(2, CountRemoves(BuffTwo), "one removal per modifier of BuffTwo");
            Assert.AreEqual(0, CountRemoves(BuffOne));
            Assert.AreEqual(0, CountRemoves(BuffThree));
        }

        [Test]
        public void StatusEffects_Tick_TwoConsecutiveRowsExpire_OnlyLastRowRemains() // compaction
        {
            Apply(A, ApDef(BuffOne, ONE_TICK, FLAT_AP_SMALL));
            Apply(A, Def(BuffTwo, ONE_TICK, 0f, 0f, Mod(StatID.Defense, FLAT_MODIFIER), Mod(StatID.MaxHP, FLAT_MODIFIER)));
            Apply(A, ApDef(BuffThree, DURATION, FLAT_AP_LARGE));

            _service.OnServerTick();

            ReadOnlySpan<ActiveEffect> rows = _service.GetActiveEffects(A);
            Assert.AreEqual(1, rows.Length);
            Assert.AreEqual(BuffThree, rows[0].BuffId);
            Assert.AreEqual(DURATION - 1u, rows[0].TicksRemaining, "the twice-shifted row is processed exactly once");
            Assert.AreEqual(1, _service.GetOccupiedSlots(A));
            Assert.AreEqual(1, CountRemoves(BuffOne));
            Assert.AreEqual(2, CountRemoves(BuffTwo));
        }

        [Test]
        public void StatusEffects_Tick_ExpiryFreesCapacity_PreviouslyRejectedApplySucceeds() // F-SE-3 occupancy
        {
            const int FULL_DEFS = StatusEffectsConstants.MODIFIER_CAPACITY_PER_ENTITY / MODIFIERS_PER_FULL_DEF;
            Apply(A, WideDef(FillBuff(0), SHORT_DURATION, MODIFIERS_PER_FULL_DEF));
            for (int i = 1; i < FULL_DEFS; i++)
                Apply(A, WideDef(FillBuff(i), DURATION, MODIFIERS_PER_FULL_DEF));
            BuffDefinition extra = ApDef(Injected, DURATION, FLAT_AP_SMALL);
            Assert.AreEqual(ApplyResult.CapacityExceeded, _service.ApplyEffect(A, A, extra));

            Tick((int)SHORT_DURATION);

            Assert.AreEqual(StatusEffectsConstants.MODIFIER_CAPACITY_PER_ENTITY - MODIFIERS_PER_FULL_DEF,
                _service.GetOccupiedSlots(A));
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, extra));
        }

        // ---- Dead slot ----

        [Test]
        public void StatusEffects_Tick_DeadEntity_NoRegenNoDecrementNoRemoval() // Dead entities
        {
            Apply(A, Def(BuffOne, DURATION, REGEN_HP, 0f, Mod(StatID.AttackPower, FLAT_AP_SMALL)));
            _service.MarkEntityDead(A);

            Tick(2);

            Assert.AreEqual(0, _recorder.RegenCalls);
            Assert.AreEqual(0, _recorder.RemoveCalls);
            Assert.AreEqual(DURATION, Row(A, BuffOne).TicksRemaining);
        }

        [Test]
        public void StatusEffects_Tick_DeadEntityBeforeLiveOne_LiveEntityStillProcessed() // Dead entities
        {
            Apply(A, Def(BuffOne, DURATION, REGEN_HP, 0f));
            Apply(B, Def(BuffTwo, DURATION, REGEN_HP, 0f));
            _service.MarkEntityDead(A);

            _service.OnServerTick();

            Assert.AreEqual(1, _recorder.RegenCalls);
            Assert.AreEqual(B, _recorder.Calls[0].Entity);
            Assert.AreEqual(DURATION, Row(A, BuffOne).TicksRemaining);
            Assert.AreEqual(DURATION - 1u, Row(B, BuffTwo).TicksRemaining);
        }

        // ---- EC-SE-1, order ----

        [Test]
        public void StatusEffects_Tick_DurationOneWithRegenAndModifier_RegenThenRemoveOnTickOne() // EC-SE-1
        {
            Apply(A, Def(BuffOne, ONE_TICK, REGEN_HP, 0f, Mod(StatID.AttackPower, FLAT_AP_SMALL)));

            _service.OnServerTick();

            Assert.AreEqual(1, _recorder.AddCalls);
            Assert.AreEqual(1, _recorder.RegenCalls);
            Assert.AreEqual(1, _recorder.RemoveCalls);
            Assert.AreEqual(LayerCallKind.Add, _recorder.Calls[0].Kind);
            Assert.AreEqual(LayerCallKind.Regen, _recorder.Calls[1].Kind);
            Assert.AreEqual(LayerCallKind.Remove, _recorder.Calls[2].Kind);
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
        }

        [Test]
        public void StatusEffects_Tick_RegenAtFullHp_StillCalledAndHpStaysAtMax() // EC-SE-10, F-SE-5
        {
            BuildReal();
            _stats.SetCurrentHP(A, BASE_MAX_HP);
            Apply(A, Def(BuffOne, DURATION, REGEN_HP, 0f));

            _service.OnServerTick();

            Assert.AreEqual(1, _recorder.RegenCalls);
            Assert.AreEqual(REGEN_HP, _recorder.Calls[0].Amount);
            Assert.AreEqual((float)BASE_MAX_HP, _stats.GetCurrentHP(A));
        }

        [Test]
        public void StatusEffects_Tick_HpThenMpThenRemovesInModifierOrder() // CR-SE-6 order
        {
            Apply(A, Def(BuffOne, ONE_TICK, REGEN_HP, REGEN_MP,
                Mod(StatID.AttackPower, FLAT_AP_SMALL), Mod(StatID.Defense, FLAT_AP_SMALL)));
            _recorder.Calls.Clear();

            _service.OnServerTick();

            Assert.AreEqual(4, _recorder.Calls.Count);
            Assert.AreEqual(LayerCallKind.Regen, _recorder.Calls[0].Kind);
            Assert.AreEqual(REGEN_HP, _recorder.Calls[0].Amount);
            Assert.AreEqual(LayerCallKind.ManaRegen, _recorder.Calls[1].Kind);
            Assert.AreEqual(REGEN_MP, _recorder.Calls[1].Amount);
            Assert.AreEqual(LayerCallKind.Remove, _recorder.Calls[2].Kind);
            Assert.AreEqual(StatID.AttackPower, _recorder.Calls[2].Stat);
            Assert.AreEqual(LayerCallKind.Remove, _recorder.Calls[3].Kind);
            Assert.AreEqual(StatID.Defense, _recorder.Calls[3].Stat);
        }

        [Test]
        public void StatusEffects_Tick_MpOnlyRegen_ManaRegenEveryTickNoHpRegen() // CR-SE-6 step 3
        {
            Apply(A, Def(BuffOne, REGEN_DURATION, 0f, REGEN_MP));

            Tick((int)REGEN_DURATION);

            Assert.AreEqual((int)REGEN_DURATION, _recorder.ManaRegenCalls);
            Assert.AreEqual(0, _recorder.RegenCalls);
            Assert.AreEqual(REGEN_MP, _recorder.Calls[0].Amount);
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
        }

        // ---- Processing order and empty slots ----

        [Test]
        public void StatusEffects_Tick_SlotsInIndexOrderRowsInRowOrder_EmptySlotMakesNoCalls() // slot order, row-count skip
        {
            Apply(B, Def(BuffThree, DURATION, REGEN_HP, 0f)); // B (higher slot) applied first
            Apply(A, Def(BuffOne, DURATION, REGEN_HP, 0f));
            Apply(A, Def(BuffTwo, DURATION, REGEN_HP_SECOND, 0f));
            _recorder.Calls.Clear();

            _service.OnServerTick();

            Assert.AreEqual(3, _recorder.Calls.Count, "no call for the entity without effects");
            Assert.AreEqual(A, _recorder.Calls[0].Entity);
            Assert.AreEqual(REGEN_HP, _recorder.Calls[0].Amount, "row 0 before row 1");
            Assert.AreEqual(A, _recorder.Calls[1].Entity);
            Assert.AreEqual(REGEN_HP_SECOND, _recorder.Calls[1].Amount);
            Assert.AreEqual(B, _recorder.Calls[2].Entity, "lower slot before higher slot");
            for (int i = 0; i < _recorder.Calls.Count; i++)
                Assert.AreNotEqual(Empty, _recorder.Calls[i].Entity);
        }

        [Test]
        public void StatusEffects_Tick_EmptyService_MakesNoCalls()
        {
            Tick(2);

            Assert.AreEqual(0, _recorder.Calls.Count);
        }

        // ---- Constructor and adapter ----

        [Test]
        public void StatusEffects_Tick_ResourceSinkAdapter_NullStatsThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterStatsResourceSink(null));
        }

        [Test]
        public void StatusEffects_Tick_ResourceSinkAdapter_ForwardsHpAndMpRegen()
        {
            BuildReal();
            var sink = new CharacterStatsResourceSink(_stats);

            sink.ApplyRegen(A, SINK_HP_AMOUNT);
            sink.ApplyManaRegen(A, SINK_MP_AMOUNT);

            Assert.AreEqual(SINK_HP_AMOUNT, _stats.GetCurrentHP(A));
            Assert.AreEqual(SINK_MP_AMOUNT, _stats.GetCurrentMP(A));
        }
    }
}
