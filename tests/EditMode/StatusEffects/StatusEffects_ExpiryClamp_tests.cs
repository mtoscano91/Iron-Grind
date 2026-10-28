using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>
    /// EditMode unit tests for Status Effects Story 006 - expiry clamp and batched stat events
    /// (CR-SE-4 replacement exception, CR-SE-8 steps 2-3, CR-SE-17, EC-SE-4, EC-SE-5, OQ-SE-1;
    /// AC-SE-15, AC-SE-29; TD-085). Every test runs against real CharacterStats through a recording decorator.
    /// </summary>
    [TestFixture]
    internal sealed class StatusEffects_ExpiryClamp_Tests
    {
        private const uint A_RAW_ID = 11u;
        private const int A_SLOT = 2;
        private const uint BUFF_W_RAW_ID = 1u;
        private const uint BUFF_X_RAW_ID = 2u;
        private const uint BUFF_Y_RAW_ID = 3u;
        private const uint BUFF_Z_RAW_ID = 4u;
        private const uint DURATION = 5u;
        private const uint LONG_DURATION = 50u;
        private const uint ONE_TICK = 1u;
        private const int BASE_ATTACK = 20;
        private const int BASE_DEFENSE = 10;
        private const int BASE_MAX_HP = 400;
        private const int BASE_MAX_MP = 80;
        private const float PCT_NONE = 0f;
        private const float FLAT_AP_OLD = 10f;
        private const float FLAT_AP_NEW = 20f;
        private const float FLAT_AP_SECOND = 15f;
        private const float FLAT_DEFENSE = 10f;
        private const float HP_BUFF_100 = 100f;
        private const float HP_BUFF_200 = 200f;
        private const float HP_BUFF_300 = 300f;
        private const float MP_BUFF_50 = 50f;
        private const float MP_BUFF_100 = 100f;
        private const float MP_BUFF_200 = 200f;
        private const int MAX_HP_WITH_100 = BASE_MAX_HP + 100;
        private const int MAX_HP_WITH_200 = BASE_MAX_HP + 200;
        private const int MAX_HP_WITH_300 = BASE_MAX_HP + 300;
        private const int MAX_MP_WITH_50 = BASE_MAX_MP + 50;
        private const int MAX_MP_WITH_100 = BASE_MAX_MP + 100;
        private const int MAX_MP_WITH_200 = BASE_MAX_MP + 200;
        private const float HP_BELOW_BASE_MAX = 300f;
        private const float REGEN_X = 5f;
        private const float REGEN_Y = 7f;
        private const float REGEN_Z = 3f;
        private const int REGEN_ROWS = 3;
        private const uint B_RAW_ID = 12u;
        private const uint C_RAW_ID = 13u;
        private const uint D_RAW_ID = 14u;
        private const int B_SLOT = 5;
        private const int C_SLOT = 6;
        private const int D_SLOT = 7;
        private const int BASE_STAT = 10;
        private const int MP_BELOW_MAX_DELTA = 30;
        private const int SEVEN_STATS = 7;
        private const int MANY_ENTITIES = 4;
        private const int ONE_EVENT = 1;

        private static readonly EntityID A = new EntityID(A_RAW_ID);
        private static readonly BuffID BuffW = (BuffID)BUFF_W_RAW_ID;
        private static readonly BuffID BuffX = (BuffID)BUFF_X_RAW_ID;
        private static readonly BuffID BuffY = (BuffID)BUFF_Y_RAW_ID;
        private static readonly BuffID BuffZ = (BuffID)BUFF_Z_RAW_ID;
        private static readonly EntityID B = new EntityID(B_RAW_ID);
        private static readonly EntityID C = new EntityID(C_RAW_ID);
        private static readonly EntityID D = new EntityID(D_RAW_ID);

        private static readonly StatID[] SevenStats =
        {
            StatID.Strength, StatID.Dexterity, StatID.Vitality, StatID.Intelligence,
            StatID.AttackPower, StatID.Defense, StatID.MagicDefense
        };

        private RecordingBuffModifierLayer _recorder;
        private DictionaryEntitySlotIndex _slots;
        private StatusEffectsService _service;
        private IronGrind.CharacterStats.CharacterStats _stats;
        private List<(EntityID Entity, StatID Stat)> _events;

        [SetUp]
        public void SetUp()
        {
            _slots = new DictionaryEntitySlotIndex();
            _slots.Add(A, A_SLOT);
            _stats = CharacterStatsFixture.Create();
            _stats.SetBaseStat(A, StatID.AttackPower, BASE_ATTACK);
            _stats.SetBaseStat(A, StatID.Defense, BASE_DEFENSE);
            _stats.SetBaseStat(A, StatID.MaxHP, BASE_MAX_HP);
            _stats.SetBaseStat(A, StatID.MaxMP, BASE_MAX_MP);
            _recorder = new RecordingBuffModifierLayer(
                new CharacterStatsBuffModifierLayer(_stats), new CharacterStatsResourceSink(_stats))
            {
                LogBatchCalls = true
            };
            _service = new StatusEffectsService(_recorder, _recorder, _slots);
            _events = new List<(EntityID Entity, StatID Stat)>();
            _stats.Subscribe((entity, stat) => _events.Add((entity, stat)));
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

        private static BuffDefinition MaxHpDef(BuffID id, uint duration, float flat)
        {
            return Def(id, duration, 0f, 0f, Mod(StatID.MaxHP, flat));
        }

        private static BuffDefinition MaxMpDef(BuffID id, uint duration, float flat)
        {
            return Def(id, duration, 0f, 0f, Mod(StatID.MaxMP, flat));
        }

        private void Apply(in BuffDefinition def)
        {
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, def));
        }

        private void Tick(int count)
        {
            for (int i = 0; i < count; i++)
                _service.OnServerTick();
        }

        /// <summary>Forgets the setup noise: clears the call log and the recorded stat events.</summary>
        private void Settle()
        {
            _recorder.Calls.Clear();
            _events.Clear();
        }

        private int Effective(StatID stat)
        {
            return _stats.GetEffectiveStat(A, stat);
        }

        private ActiveEffect Row(BuffID buff)
        {
            Assert.IsTrue(_service.TryGetActiveEffect(A, buff, out ActiveEffect row), "row " + buff + " missing");
            return row;
        }

        private int CountEvents(StatID stat)
        {
            int n = 0;
            foreach ((EntityID Entity, StatID Stat) e in _events)
            {
                if (e.Stat == stat)
                    n++;
            }

            return n;
        }

        private int CountKind(LayerCallKind kind)
        {
            int n = 0;
            foreach (LayerCall call in _recorder.Calls)
            {
                if (call.Kind == kind)
                    n++;
            }

            return n;
        }

        private void AssertKinds(params LayerCallKind[] expected)
        {
            var actual = new List<LayerCallKind>();
            foreach (LayerCall call in _recorder.Calls)
                actual.Add(call.Kind);
            CollectionAssert.AreEqual(expected, actual, "recorded call order: " + string.Join(", ", actual));
        }

        /// <summary>Registers another entity at a slot and gives it base values for every stat the tests touch.</summary>
        private void AddEntity(EntityID id, int slot)
        {
            _slots.Add(id, slot);
            _stats.SetBaseStat(id, StatID.MaxHP, BASE_MAX_HP);
            _stats.SetBaseStat(id, StatID.MaxMP, BASE_MAX_MP);
            foreach (StatID stat in SevenStats)
                _stats.SetBaseStat(id, stat, BASE_STAT);
        }

        private int CountPair(EntityID entity, StatID stat)
        {
            int n = 0;
            foreach ((EntityID Entity, StatID Stat) e in _events)
            {
                if (e.Entity.Equals(entity) && e.Stat == stat)
                    n++;
            }

            return n;
        }

        /// <summary>A replacement outside a tick must do its own Begin/End pair (proves the batch state was reset).</summary>
        private void AssertReplacementOutsideTickBrackets()
        {
            Apply(ApDef(BuffY, LONG_DURATION, FLAT_AP_OLD));
            int beginBefore = _recorder.BeginCalls;
            int endBefore = _recorder.EndCalls;

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(BuffY, LONG_DURATION, FLAT_AP_NEW)));

            Assert.AreEqual(beginBefore + 1, _recorder.BeginCalls);
            Assert.AreEqual(endBefore + 1, _recorder.EndCalls);
        }

        // ---- AC-SE-15, EC-SE-4, EC-SE-5: expiry clamp ----

        [Test]
        public void StatusEffects_ExpiryClamp_MaxHpBuffAtFullHp_RemoveThenClampThenHpReduced() // AC-SE-15
        {
            Apply(MaxHpDef(BuffX, DURATION, HP_BUFF_200));
            _stats.SetCurrentHP(A, MAX_HP_WITH_200);
            Tick((int)DURATION - 1);
            Settle();

            Tick(1);

            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.ClampHp, LayerCallKind.EndBatch);
            Assert.AreEqual(StatID.MaxHP, _recorder.Calls[1].Stat);
            Assert.AreEqual(BuffX, _recorder.Calls[1].Buff);
            Assert.AreEqual(A, _recorder.Calls[2].Entity);
            Assert.AreEqual((float)BASE_MAX_HP, _stats.GetCurrentHP(A));
            Assert.AreEqual(BASE_MAX_HP, Effective(StatID.MaxHP));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_HpBelowNewMax_ClampStillCalledHpUnchangedNoHpEvent() // EC-SE-4
        {
            Apply(MaxHpDef(BuffX, DURATION, HP_BUFF_200));
            _stats.SetCurrentHP(A, HP_BELOW_BASE_MAX);
            Tick((int)DURATION - 1);
            Settle();

            Tick(1);

            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.ClampHp, LayerCallKind.EndBatch);
            Assert.AreEqual(HP_BELOW_BASE_MAX, _stats.GetCurrentHP(A));
            Assert.AreEqual(0, CountEvents(StatID.CurrentHP));
            Assert.AreEqual(1, CountEvents(StatID.MaxHP));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_MaxMpBuffAtFullMp_RemoveThenClampThenMpReduced() // EC-SE-5
        {
            Apply(MaxMpDef(BuffX, DURATION, MP_BUFF_100));
            _stats.SetCurrentMP(A, MAX_MP_WITH_100);
            Tick((int)DURATION - 1);
            Settle();

            Tick(1);

            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.ClampMp, LayerCallKind.EndBatch);
            Assert.AreEqual(StatID.MaxMP, _recorder.Calls[1].Stat);
            Assert.AreEqual((float)BASE_MAX_MP, _stats.GetCurrentMP(A));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_MpBelowNewMax_ClampCalledMpUnchangedNoMpEvent() // EC-SE-5 no-op
        {
            Apply(MaxMpDef(BuffX, DURATION, MP_BUFF_100));
            _stats.SetCurrentMP(A, BASE_MAX_MP - MP_BELOW_MAX_DELTA);
            Tick((int)DURATION - 1);
            Settle();

            Tick(1);

            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.ClampMp, LayerCallKind.EndBatch);
            Assert.AreEqual((float)(BASE_MAX_MP - MP_BELOW_MAX_DELTA), _stats.GetCurrentMP(A));
            Assert.AreEqual(0, CountEvents(StatID.CurrentMP));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_BuffWithMaxHpAndMaxMp_HpClampBeforeMpClampAfterAllRemoves() // CR-SE-8 steps 2-3
        {
            Apply(Def(BuffX, DURATION, 0f, 0f, Mod(StatID.MaxMP, MP_BUFF_100), Mod(StatID.MaxHP, HP_BUFF_200)));
            _stats.SetCurrentHP(A, MAX_HP_WITH_200);
            _stats.SetCurrentMP(A, MAX_MP_WITH_100);
            Tick((int)DURATION - 1);
            Settle();

            Tick(1);

            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.Remove,
                LayerCallKind.ClampHp, LayerCallKind.ClampMp, LayerCallKind.EndBatch);
            Assert.AreEqual((float)BASE_MAX_HP, _stats.GetCurrentHP(A));
            Assert.AreEqual((float)BASE_MAX_MP, _stats.GetCurrentMP(A));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_NonMaxBuffExpires_NoClampCalls()
        {
            Apply(ApDef(BuffX, ONE_TICK, FLAT_AP_OLD));
            Settle();

            Tick(1);

            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.EndBatch);
            Assert.AreEqual(0, _recorder.ClampHpCalls);
            Assert.AreEqual(0, _recorder.ClampMpCalls);
        }

        // ---- AC-SE-29: batched events ----

        [Test]
        public void StatusEffects_ExpiryClamp_ThreeStatBuffExpires_ExactlyThreeEventsOnePerStat() // AC-SE-29
        {
            Apply(Def(BuffX, DURATION, 0f, 0f,
                Mod(StatID.AttackPower, FLAT_AP_OLD), Mod(StatID.MaxHP, HP_BUFF_200), Mod(StatID.Defense, FLAT_DEFENSE)));
            Tick((int)DURATION - 1);
            Settle();

            Tick(1);

            Assert.AreEqual(3, _events.Count);
            foreach ((EntityID Entity, StatID Stat) e in _events)
                Assert.AreEqual(A, e.Entity, "event for " + e.Stat);
            Assert.AreEqual(1, CountEvents(StatID.AttackPower));
            Assert.AreEqual(1, CountEvents(StatID.MaxHP));
            Assert.AreEqual(1, CountEvents(StatID.Defense));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_TwoEffectsShareStatExpireSameTick_StatFiresOnce() // AC-SE-29
        {
            Apply(ApDef(BuffX, DURATION, FLAT_AP_OLD));
            Apply(ApDef(BuffY, DURATION, FLAT_AP_SECOND));
            Tick((int)DURATION - 1);
            Settle();

            Tick(1);

            Assert.AreEqual(1, CountEvents(StatID.AttackPower));
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(BASE_ATTACK, Effective(StatID.AttackPower));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_TickEvents_FireOnlyAfterTheWholePassNotPerRemove() // CR-SE-17
        {
            Apply(ApDef(BuffX, DURATION, FLAT_AP_OLD));
            Apply(ApDef(BuffY, DURATION, FLAT_AP_SECOND));
            Tick((int)DURATION - 1);
            Settle();
            int removesSeenByHandler = -1;
            _stats.Subscribe((entity, stat) =>
            {
                if (removesSeenByHandler < 0)
                    removesSeenByHandler = CountKind(LayerCallKind.Remove);
            });

            Tick(1);

            Assert.AreEqual(2, removesSeenByHandler, "the first event fires after both removes");
        }

        // ---- Bracket order ----

        [Test]
        public void StatusEffects_ExpiryClamp_TickWithNoRows_BeginThenEnd()
        {
            Tick(1);

            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.EndBatch);
        }

        [Test]
        public void StatusEffects_ExpiryClamp_TickWithRegenRow_BeginFirstEndLast()
        {
            Apply(Def(BuffX, DURATION, REGEN_X, 0f));
            Settle();

            Tick(1);

            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Regen, LayerCallKind.EndBatch);
        }

        [Test]
        public void StatusEffects_ExpiryClamp_PassThrows_EndStillLoggedNoTransactionLeftOpen()
        {
            Apply(ApDef(BuffX, ONE_TICK, FLAT_AP_OLD));
            Settle();
            _recorder.ThrowOnRemoveNumber = _recorder.RemoveCalls + 1;

            Assert.Throws<InvalidOperationException>(() => _service.OnServerTick());

            Assert.AreEqual(LayerCallKind.EndBatch, _recorder.Calls[_recorder.Calls.Count - 1].Kind);
            Assert.AreEqual(_recorder.BeginCalls, _recorder.EndCalls);
            _recorder.ThrowOnRemoveNumber = 0;
            Assert.DoesNotThrow(() => _service.OnServerTick(), "no transaction left open");
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.AreEqual(BASE_ATTACK, Effective(StatID.AttackPower));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_TransactionAlreadyOpen_TickThrowsAndProcessesNothing()
        {
            Apply(Def(BuffX, DURATION, REGEN_X, 0f, Mod(StatID.AttackPower, FLAT_AP_OLD)));
            Settle();
            _stats.BeginStatTransaction();

            try
            {
                Assert.Throws<InvalidOperationException>(() => _service.OnServerTick());

                Assert.AreEqual(DURATION, Row(BuffX).TicksRemaining, "no row processed");
                Assert.AreEqual(0, _recorder.RegenCalls);
                Assert.AreEqual(0, _recorder.EndCalls, "End is not called when Begin failed");
            }
            finally
            {
                _stats.EndStatTransaction();
            }

            Assert.DoesNotThrow(() => _service.OnServerTick(), "after the outer transaction closes the tick runs");
            Assert.AreEqual(DURATION - 1u, Row(BuffX).TicksRemaining);
        }

        // ---- TD-085: throwing subscriber on the expiry tick ----

        [Test]
        public void StatusEffects_ExpiryClamp_SubscriberThrowsOnFirstExpiryEvent_EveryRowProcessedOnceAndNextTickClean() // TD-085
        {
            Apply(Def(BuffX, ONE_TICK, REGEN_X, 0f, Mod(StatID.AttackPower, FLAT_AP_OLD)));
            Apply(Def(BuffY, ONE_TICK, REGEN_Y, 0f, Mod(StatID.Defense, FLAT_DEFENSE)));
            Apply(Def(BuffZ, LONG_DURATION, REGEN_Z, 0f));
            Settle();
            bool armed = true;
            _stats.Subscribe((entity, stat) =>
            {
                if (armed)
                {
                    armed = false;
                    throw new InvalidOperationException("subscriber failure");
                }
            });

            Assert.Throws<InvalidOperationException>(() => _service.OnServerTick());

            Assert.AreEqual(REGEN_ROWS, _recorder.RegenCalls, "each row regenerated exactly once");
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffX, out _));
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffY, out _));
            Assert.AreEqual(1, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.AreEqual(LONG_DURATION - 1u, Row(BuffZ).TicksRemaining);
            Assert.AreEqual(BASE_ATTACK, Effective(StatID.AttackPower));
            Assert.AreEqual(BASE_DEFENSE, Effective(StatID.Defense));
            Assert.AreEqual(_recorder.BeginCalls, _recorder.EndCalls);

            int regenBefore = _recorder.RegenCalls;
            Assert.DoesNotThrow(() => _service.OnServerTick());

            Assert.AreEqual(1, _recorder.RegenCalls - regenBefore, "only the surviving row regenerates; no double regen");
            Assert.AreEqual(LONG_DURATION - 2u, Row(BuffZ).TicksRemaining);
        }

        // ---- Replacement clamp (CR-SE-4 exception) ----

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceMaxHpUpgradeAtFullHp_HpStaysAtFullOfNewMax()
        {
            Apply(MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_200));
            _stats.SetCurrentHP(A, MAX_HP_WITH_200);
            Settle();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_300)));

            Assert.AreEqual((float)MAX_HP_WITH_200, _stats.GetCurrentHP(A), "not 400: the clamp runs after the new add");
            Assert.AreEqual(MAX_HP_WITH_300, Effective(StatID.MaxHP));
            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.Add,
                LayerCallKind.ClampHp, LayerCallKind.EndBatch);
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceMaxHpDowngradeAtFullHp_HpClampedToNewMax()
        {
            Apply(MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_300));
            _stats.SetCurrentHP(A, MAX_HP_WITH_300);
            Settle();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_100)));

            Assert.AreEqual((float)MAX_HP_WITH_100, _stats.GetCurrentHP(A));
            Assert.AreEqual(MAX_HP_WITH_100, Effective(StatID.MaxHP));
            Assert.AreEqual(1, _recorder.ClampHpCalls);
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceMaxMpUpgradeAtFullMp_MpStaysAtOldValue()
        {
            Apply(MaxMpDef(BuffX, LONG_DURATION, MP_BUFF_100));
            _stats.SetCurrentMP(A, MAX_MP_WITH_100);
            Settle();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, MaxMpDef(BuffX, LONG_DURATION, MP_BUFF_200)));

            Assert.AreEqual((float)MAX_MP_WITH_100, _stats.GetCurrentMP(A));
            Assert.AreEqual(MAX_MP_WITH_200, Effective(StatID.MaxMP));
            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.Add,
                LayerCallKind.ClampMp, LayerCallKind.EndBatch);
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceMaxMpDowngradeAtFullMp_MpClampedToNewMax()
        {
            Apply(MaxMpDef(BuffX, LONG_DURATION, MP_BUFF_200));
            _stats.SetCurrentMP(A, MAX_MP_WITH_200);
            Settle();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, MaxMpDef(BuffX, LONG_DURATION, MP_BUFF_50)));

            Assert.AreEqual((float)MAX_MP_WITH_50, _stats.GetCurrentMP(A));
            Assert.AreEqual(1, _recorder.ClampMpCalls);
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceMaxHpBuffWithNonMaxBuff_OldMaxHpAloneTriggersClamp()
        {
            Apply(MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_200));
            _stats.SetCurrentHP(A, MAX_HP_WITH_200);
            Settle();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(BuffX, LONG_DURATION, FLAT_AP_OLD)));

            Assert.AreEqual(1, _recorder.ClampHpCalls);
            Assert.AreEqual((float)BASE_MAX_HP, _stats.GetCurrentHP(A));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceNonMaxBuffWithMaxHpBuff_NewMaxHpAloneTriggersClamp()
        {
            Apply(ApDef(BuffX, LONG_DURATION, FLAT_AP_OLD));
            _stats.SetCurrentHP(A, HP_BELOW_BASE_MAX);
            Settle();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_200)));

            Assert.AreEqual(1, _recorder.ClampHpCalls);
            Assert.AreEqual(HP_BELOW_BASE_MAX, _stats.GetCurrentHP(A));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceWithoutMaxStats_NoClampCalls()
        {
            Apply(ApDef(BuffX, LONG_DURATION, FLAT_AP_OLD));
            Settle();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(BuffX, LONG_DURATION, FLAT_AP_NEW)));

            Assert.AreEqual(0, _recorder.ClampHpCalls);
            Assert.AreEqual(0, _recorder.ClampMpCalls);
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceAddRejected_ClampRunsAfterRollbackAndOldEffectLost()
        {
            Apply(MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_200));
            _stats.SetCurrentHP(A, MAX_HP_WITH_200);
            Settle();
            _recorder.FailOnAddNumber = _recorder.AddCalls + 1;

            ApplyResult result = _service.ApplyEffect(A, A, MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_300));

            Assert.AreEqual(ApplyResult.CapacityExceeded, result);
            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.Add,
                LayerCallKind.ClampHp, LayerCallKind.EndBatch);
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffX, out _));
            Assert.AreEqual((float)BASE_MAX_HP, _stats.GetCurrentHP(A));
            Assert.AreEqual(BASE_MAX_HP, Effective(StatID.MaxHP));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceAddThrows_ClampRunsBeforeRethrowAndBatchEnds()
        {
            Apply(MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_200));
            _stats.SetCurrentHP(A, MAX_HP_WITH_200);
            Settle();
            _recorder.ThrowOnAddNumber = _recorder.AddCalls + 1;

            Assert.Throws<InvalidOperationException>(
                () => _service.ApplyEffect(A, A, MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_300)));

            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.Add, LayerCallKind.Remove,
                LayerCallKind.ClampHp, LayerCallKind.EndBatch);
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffX, out _));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.AreEqual((float)BASE_MAX_HP, _stats.GetCurrentHP(A));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceInsideTick_UsesTheTicksSingleBracketAndClamps()
        {
            Apply(ApDef(BuffW, LONG_DURATION, FLAT_AP_OLD));
            Apply(MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_200));
            _stats.SetCurrentHP(A, MAX_HP_WITH_200);
            _service.InjectApplyEffectDuringTick(A, MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_300), A);
            Settle();

            Tick(1);

            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual(1, CountKind(LayerCallKind.BeginBatch));
            Assert.AreEqual(1, CountKind(LayerCallKind.EndBatch));
            Assert.AreEqual(LayerCallKind.BeginBatch, _recorder.Calls[0].Kind);
            Assert.AreEqual(LayerCallKind.EndBatch, _recorder.Calls[_recorder.Calls.Count - 1].Kind);
            Assert.AreEqual(1, CountKind(LayerCallKind.ClampHp));
            Assert.AreEqual((float)MAX_HP_WITH_200, _stats.GetCurrentHP(A));
            Assert.AreEqual(MAX_HP_WITH_300, Effective(StatID.MaxHP));
            Assert.AreEqual(1, CountEvents(StatID.MaxHP), "the replacement's remove and add collapse into one event");
        }

        // ---- Replacement bracket outside the tick (TD-085 replacement path) ----

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceOutsideTick_OneEventAndHandlerSeesFinalValue()
        {
            Apply(ApDef(BuffX, LONG_DURATION, FLAT_AP_OLD));
            Settle();
            var seen = new List<int>();
            _stats.Subscribe((entity, stat) =>
            {
                if (stat == StatID.AttackPower)
                    seen.Add(_stats.GetEffectiveStat(entity, stat));
            });

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(BuffX, LONG_DURATION, FLAT_AP_NEW)));

            Assert.AreEqual(1, CountEvents(StatID.AttackPower));
            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_NEW, seen[0]);
            Assert.AreEqual(1, _recorder.BeginCalls, "one Begin for this replacement (no tick ran)");
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceOutsideTickSubscriberThrows_ApplyThrowsStateConsistentNothingLeftOpen()
        {
            Apply(ApDef(BuffX, LONG_DURATION, FLAT_AP_OLD));
            Settle();
            bool armed = true;
            _stats.Subscribe((entity, stat) =>
            {
                if (armed)
                {
                    armed = false;
                    throw new InvalidOperationException("subscriber failure");
                }
            });

            Assert.Throws<InvalidOperationException>(
                () => _service.ApplyEffect(A, A, ApDef(BuffX, LONG_DURATION, FLAT_AP_NEW)));

            Assert.AreEqual(FLAT_AP_NEW, Row(BuffX).Modifier0.FlatBonus, "row holds the new values");
            Assert.AreEqual(1, _service.GetOccupiedSlots(A));
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_NEW, Effective(StatID.AttackPower));
            Assert.AreEqual(_recorder.BeginCalls, _recorder.EndCalls);
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(BuffY, LONG_DURATION, FLAT_AP_SECOND)));
            Assert.DoesNotThrow(() => _service.OnServerTick(), "no transaction left open");
        }

        // ---- Not bracketed: new buffs and plain refreshes ----

        [Test]
        public void StatusEffects_ExpiryClamp_NewBuffApplyOutsideTick_NotBracketedEventFiresPerAdd()
        {
            Settle();

            Apply(Def(BuffX, LONG_DURATION, 0f, 0f,
                Mod(StatID.AttackPower, FLAT_AP_OLD), Mod(StatID.Defense, FLAT_DEFENSE)));

            Assert.AreEqual(0, _recorder.BeginCalls);
            Assert.AreEqual(0, _recorder.EndCalls);
            Assert.AreEqual(2, _events.Count);
            Assert.AreEqual(1, CountEvents(StatID.AttackPower));
            Assert.AreEqual(1, CountEvents(StatID.Defense));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_PlainRefreshOutsideTick_NotBracketedNoEvents()
        {
            Apply(ApDef(BuffX, LONG_DURATION, FLAT_AP_OLD));
            Settle();

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(BuffX, LONG_DURATION, FLAT_AP_OLD)));

            Assert.AreEqual(0, _recorder.BeginCalls);
            Assert.AreEqual(0, _recorder.EndCalls);
            Assert.AreEqual(0, _recorder.Calls.Count);
            Assert.AreEqual(0, _events.Count);
        }

        // ---- W1: a throwing death subscriber during the expiry clamp (free first, finish, rethrow) ----
        // With real CharacterStats the death path is unreachable (effective MaxHP is floored at 1, and a clamp
        // never takes HP to 0), so the fake sink's ThrowOnClampHpNumber stands in for the throwing subscriber.

        [Test]
        public void StatusEffects_ExpiryClamp_HpClampThrows_RowFreedOnceMpClampRunsPassFinishesThenRethrown()
        {
            // Arrange
            AddEntity(B, B_SLOT);
            Apply(Def(BuffX, ONE_TICK, 0f, 0f, Mod(StatID.MaxHP, HP_BUFF_200), Mod(StatID.MaxMP, MP_BUFF_100)));
            Apply(Def(BuffY, LONG_DURATION, REGEN_Y, 0f, Mod(StatID.AttackPower, FLAT_AP_OLD)));
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(B, B, Def(BuffZ, LONG_DURATION, REGEN_Z, 0f)));
            Settle();
            _recorder.ThrowOnClampHpNumber = _recorder.ClampHpCalls + 1;
            int regenBefore = _recorder.RegenCalls;

            // Act
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => _service.OnServerTick());

            // Assert
            StringAssert.Contains("clamp failure", ex.Message);
            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.Remove, LayerCallKind.ClampHp,
                LayerCallKind.ClampMp, LayerCallKind.Regen, LayerCallKind.Regen, LayerCallKind.EndBatch);
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffX, out _), "expired row is gone");
            Assert.AreEqual(1, _service.GetActiveEffectCount(A));
            Assert.AreEqual(1, _service.GetOccupiedSlots(A), "the expired row's two slots were released exactly once");
            Assert.AreEqual(2, _recorder.RegenCalls - regenBefore, "later row and later slot processed once");
            Assert.AreEqual(1, _recorder.ClampMpCalls, "MP clamp still ran");
            Assert.AreEqual(_recorder.BeginCalls, _recorder.EndCalls);

            Assert.DoesNotThrow(() => _service.OnServerTick(), "next tick runs normally");
            Assert.AreEqual(1, _service.GetOccupiedSlots(A), "occupancy is not reduced again");
            Assert.AreEqual(4, _recorder.RegenCalls - regenBefore);
            Assert.AreEqual(LONG_DURATION - 2u, Row(BuffY).TicksRemaining);
        }

        [Test]
        public void StatusEffects_ExpiryClamp_HpClampDoesNotThrow_TickCompletesHpClampBeforeMpClamp()
        {
            // Arrange
            Apply(Def(BuffX, ONE_TICK, 0f, 0f, Mod(StatID.MaxHP, HP_BUFF_200), Mod(StatID.MaxMP, MP_BUFF_100)));
            Settle();

            // Act
            Assert.DoesNotThrow(() => _service.OnServerTick());

            // Assert
            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.Remove,
                LayerCallKind.ClampHp, LayerCallKind.ClampMp, LayerCallKind.EndBatch);
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
        }

        // ---- W3: the original exception wins over End's ----

        [Test]
        public void StatusEffects_ExpiryClamp_PassThrowsAndEndThrows_PassExceptionObservedBatchReset()
        {
            // Arrange
            Apply(ApDef(BuffX, ONE_TICK, FLAT_AP_OLD));
            Settle();
            _recorder.ThrowOnRemoveNumber = _recorder.RemoveCalls + 1;
            _recorder.ThrowOnEndNumber = _recorder.EndCalls + 1;

            // Act
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => _service.OnServerTick());

            // Assert
            StringAssert.Contains("remove failure", ex.Message);
            Assert.AreEqual(_recorder.BeginCalls, _recorder.EndCalls);
            AssertReplacementOutsideTickBrackets();
        }

        [Test]
        public void StatusEffects_ExpiryClamp_OnlyEndThrows_EndExceptionObservedBatchReset()
        {
            // Arrange
            Apply(ApDef(BuffX, LONG_DURATION, FLAT_AP_OLD));
            Settle();
            _recorder.ThrowOnEndNumber = _recorder.EndCalls + 1;

            // Act
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => _service.OnServerTick());

            // Assert
            StringAssert.Contains("end failure", ex.Message);
            Assert.AreEqual(LONG_DURATION - 1u, Row(BuffX).TicksRemaining, "the pass completed");
            AssertReplacementOutsideTickBrackets();
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceThrowsAndEndThrows_ReplaceExceptionObserved()
        {
            // Arrange
            Apply(ApDef(BuffX, LONG_DURATION, FLAT_AP_OLD));
            Settle();
            _recorder.ThrowOnAddNumber = _recorder.AddCalls + 1;
            _recorder.ThrowOnEndNumber = _recorder.EndCalls + 1;

            // Act
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _service.ApplyEffect(A, A, ApDef(BuffX, LONG_DURATION, FLAT_AP_NEW)));

            // Assert
            StringAssert.Contains("add failure", ex.Message);
            Assert.AreEqual(_recorder.BeginCalls, _recorder.EndCalls);
            AssertReplacementOutsideTickBrackets();
        }

        // ---- A1: replacement outside a tick with a Character Stats transaction already open ----

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceOutsideTickTransactionOpen_ThrowsNothingChangedThenSucceedsAfterClose()
        {
            // Arrange
            Apply(ApDef(BuffX, LONG_DURATION, FLAT_AP_OLD));
            Settle();
            int removeBefore = _recorder.RemoveCalls;
            int endBefore = _recorder.EndCalls;
            _stats.BeginStatTransaction();

            try
            {
                // Act
                Assert.Throws<InvalidOperationException>(
                    () => _service.ApplyEffect(A, A, ApDef(BuffX, LONG_DURATION, FLAT_AP_NEW)));

                // Assert
                Assert.AreEqual(FLAT_AP_OLD, Row(BuffX).Modifier0.FlatBonus, "row holds the old values");
                Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_OLD, Effective(StatID.AttackPower));
                Assert.AreEqual(endBefore, _recorder.EndCalls);
                Assert.AreEqual(removeBefore, _recorder.RemoveCalls);
            }
            finally
            {
                _stats.EndStatTransaction();
            }

            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, ApDef(BuffX, LONG_DURATION, FLAT_AP_NEW)));
            Assert.AreEqual(FLAT_AP_NEW, Row(BuffX).Modifier0.FlatBonus);
            Assert.AreEqual(BASE_ATTACK + (int)FLAT_AP_NEW, Effective(StatID.AttackPower));
        }

        // ---- A4: replacements inside a tick ----

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceDowngradeInsideTick_HpClampedOneClampOneEventOneBracket()
        {
            // Arrange
            Apply(ApDef(BuffW, LONG_DURATION, FLAT_AP_OLD));
            Apply(MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_300));
            _stats.SetCurrentHP(A, MAX_HP_WITH_300);
            _service.InjectApplyEffectDuringTick(A, MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_100), A);
            Settle();

            // Act
            Tick(1);

            // Assert
            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual((float)MAX_HP_WITH_100, _stats.GetCurrentHP(A));
            Assert.AreEqual(1, CountKind(LayerCallKind.ClampHp));
            Assert.AreEqual(ONE_EVENT, CountEvents(StatID.MaxHP));
            Assert.AreEqual(1, CountKind(LayerCallKind.BeginBatch));
            Assert.AreEqual(1, CountKind(LayerCallKind.EndBatch));
        }

        [Test]
        public void StatusEffects_ExpiryClamp_ReplaceInsideTickSameTickAsOtherMaxHpExpiry_ExpiryClampThenReplacementClamp()
        {
            // Arrange: W (index 0) expires first; the injection fires after its row, then replaces X.
            Apply(MaxHpDef(BuffW, ONE_TICK, HP_BUFF_100));
            Apply(MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_200));
            _stats.SetCurrentHP(A, BASE_MAX_HP + HP_BUFF_100 + HP_BUFF_200);
            _service.InjectApplyEffectDuringTick(A, MaxHpDef(BuffX, LONG_DURATION, HP_BUFF_300), A);
            Settle();

            // Act
            Tick(1);

            // Assert
            Assert.AreEqual(ApplyResult.Success, _service.LastInjectedResult);
            Assert.AreEqual(2, _recorder.ClampHpCalls);
            AssertKinds(LayerCallKind.BeginBatch, LayerCallKind.Remove, LayerCallKind.ClampHp,
                LayerCallKind.Remove, LayerCallKind.Add, LayerCallKind.ClampHp, LayerCallKind.EndBatch);
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffW, out _));
            Assert.AreEqual(MAX_HP_WITH_300, Effective(StatID.MaxHP));
        }

        // ---- B1: more than 18 (entity, stat) pairs in one pass ----

        [Test]
        public void StatusEffects_ExpiryClamp_ManyEntitiesManyStatsExpireSameTick_EveryPairFiresExactlyOnce()
        {
            // Arrange
            AddEntity(B, B_SLOT);
            AddEntity(C, C_SLOT);
            AddEntity(D, D_SLOT);
            EntityID[] entities = { A, B, C, D };
            var mods = new StatModifier[SEVEN_STATS];
            for (int i = 0; i < SEVEN_STATS; i++)
                mods[i] = Mod(SevenStats[i], FLAT_AP_OLD);
            foreach (EntityID entity in entities)
                Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(entity, entity, Def(BuffX, ONE_TICK, 0f, 0f, mods)));
            Settle();

            // Act
            Tick(1);

            // Assert
            Assert.AreEqual(MANY_ENTITIES * SEVEN_STATS, _events.Count);
            foreach (EntityID entity in entities)
            {
                foreach (StatID stat in SevenStats)
                    Assert.AreEqual(ONE_EVENT, CountPair(entity, stat), entity + " " + stat);
            }
        }
    }
}
